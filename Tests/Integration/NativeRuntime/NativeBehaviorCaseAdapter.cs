using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Authoring.BehaviorCases;
using AIBT.Tests.Integration.SemanticSlice;
using Unity.Collections;

namespace AIBT.Tests.Integration.NativeRuntime
{
    internal enum NativeGoldenExecutionPolicyV1 : byte
    {
        Immediate = 0,
        Budgeted = 1,
        BatchedJobsSameFrame = 2,

        /// <summary>
        /// This adapter drives exactly one tree instance at a time. Pipelining (P4-003) is a
        /// property of when a *population's* batch work is submitted versus when results are
        /// consumed -- one instance's own atomic steps remain strictly sequential regardless of
        /// policy (step N+1 cannot even be determined until step N's dispatch completes). So for
        /// this single-instance adapter, "same observable results, latency only differs" reduces
        /// to identical per-instance behavior to <see cref="Immediate"/>; genuine cross-stage
        /// latency insertion is proven separately, on a real multi-round pipeline, by
        /// <c>NativeExecutionEquivalenceTests.RunPipelined</c> and by
        /// <c>Tests/Runtime/NativeExecution/Scheduling/NativePipelinedPhaseControllerTests.cs</c>.
        /// </summary>
        PipelinedJobs = 3,
    }

    internal sealed class NativeBehaviorCaseExecutorFactory : IBehaviorCaseExecutorFactory
    {
        private static readonly CompiledCompilerVersion CompilerVersion = new CompiledCompilerVersion(1, 0, 0, 1);
        private readonly string _treeRoot;
        private readonly NativeGoldenExecutionPolicyV1 _policy;

        internal NativeBehaviorCaseExecutorFactory(
            string treeRoot,
            NativeGoldenExecutionPolicyV1 policy = NativeGoldenExecutionPolicyV1.Immediate)
        {
            _treeRoot = Path.GetFullPath(treeRoot ?? throw new ArgumentNullException(nameof(treeRoot)));
            _policy = policy;
        }

        public IBehaviorCaseExecutor Create(BehaviorCaseExecutorConfiguration configuration)
        {
            if (configuration.RootSeed != 0) throw new NotSupportedException("Native golden fixtures require rootSeed zero.");
            var path = Path.GetFullPath(Path.Combine(_treeRoot, configuration.Tree.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = _treeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Tree path escapes fixture root.");
            var read = CanonicalTreeJson.Parse(File.ReadAllBytes(path), configuration.Tree);
            if (!read.Success) throw new InvalidOperationException("Tree fixture is invalid.");
            var registry = SemanticSliceNodeContracts.CreateAuthoringRegistry();
            var validation = TreeValidator.Validate(read.Document, registry);
            if (validation.Any(item => item.Severity == DiagnosticSeverity.Error)) throw new InvalidOperationException("Tree fixture is invalid.");
            var compilation = ReferenceCompiler.Compile(read.Document, registry, new ReferenceCompilerOptions(
                configuration.Tree.Replace('\\', '/'), ReferenceCompilationPolicy.Phase1, CompilerVersion));
            if (!compilation.Success) throw new InvalidOperationException("Tree fixture did not compile.");
            return new NativeBehaviorCaseExecutor(compilation.Program, read.Document, configuration, _policy);
        }
    }

    internal sealed class NativeBehaviorCaseExecutor : IBehaviorCaseExecutor
    {
        private readonly CompiledProgram _program;
        private readonly TreeInstanceId _treeInstanceId;
        private readonly Dictionary<string, BlackboardEntry> _blackboard;
        private NativeArray<NativeCompiledNodeRecordV1> _nodes;
        private NativeArray<uint> _children;
        private NativeArray<NativeLifecycleNodeBindingV1> _bindings;
        private NativeArray<byte> _memory;
        private NativeArray<byte> _configuration;
        private NativeArray<NativeFrameStateV1> _frames;
        private NativeArray<uint> _generations;
        private NativeArray<byte> _cooldown;
        private NativeArray<NativeParallelBranchStateV1> _parallel;
        private NativeArray<NativeLifecycleControlV1> _control;
        private NodeStatus[] _lastStatuses;
        private NativeLifecycleMachineV1 _machine;
        private readonly NativeGoldenExecutionPolicyV1 _policy;
        private NativeBatchedLifecycleOwnerV1 _batched;
        private NativeArray<NativeLifecycleStepResultV1> _batchedResults;
        private NativeArray<NativeRuntimeFailureV1> _batchedFailures;
        private NativeBudgetStateV1 _budgetState;
        private readonly List<BehaviorCaseObservedTrace> _trace = new List<BehaviorCaseObservedTrace>();
        private readonly List<BehaviorCaseCommandExpectation> _commands = new List<BehaviorCaseCommandExpectation>();
        private ulong _traceSequence;
        private ulong _commandSequence;
        private ulong _currentUpdateId;
        private Revision _currentRevision;
        private bool _suspended;
        private bool _abortPending;
        private bool _observerPending;
        private bool _asyncActive;
        private OperationId _operation;
        private CompletionOutcome? _completion;
        private BurstNodeAbortReason _activeAbortReason;
        private uint _activeAbortSource;
        private NodeStatus _lastTerminalStatus;

        internal NativeBehaviorCaseExecutor(
            CompiledProgram program,
            TreeDocument document,
            BehaviorCaseExecutorConfiguration configuration,
            NativeGoldenExecutionPolicyV1 policy)
        {
            _program = program;
            _treeInstanceId = configuration.TreeInstanceId;
            _policy = policy;
            _blackboard = CreateBlackboard(document, configuration.InitialBlackboard);
            CreateMachine();
        }

        public BehaviorCaseExecutorStepResult Execute(BehaviorCaseStep step)
        {
            _trace.Clear();
            _commands.Clear();
            var before = _control[0].SemanticSteps;
            ulong? budget;
            switch (step)
            {
                case BehaviorCaseUpdateStep update:
                    if (update.Events.Count != 0) throw new NotSupportedException("Native golden adapter has no external event fixture.");
                    Begin(update.UpdateId, update.SnapshotRevision, update.TimeMicroseconds, update.Completions, false);
                    budget = update.StepBudget;
                    break;
                case BehaviorCaseAbortStep abort:
                    Begin(abort.UpdateId, abort.SnapshotRevision, abort.TimeMicroseconds, abort.Completions, true);
                    budget = abort.StepBudget;
                    break;
                case BehaviorCaseResumeStep resume:
                    if (!_suspended) throw new InvalidOperationException("Only a suspended pass can resume.");
                    _suspended = false;
                    Trace(BehaviorCaseTraceEvent.ExecutionResumed);
                    budget = resume.StepBudget;
                    break;
                case BehaviorCaseControlStep _:
                    throw new NotSupportedException("The accepted Phase 2 golden inventory has no restart step.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(step));
            }

            var limit = budget ?? ulong.MaxValue;
            ulong consumed = 0;
            while (consumed < limit)
            {
                if (!TryAdvance(out var nativeStep, out var failure))
                    throw new InvalidOperationException("Native lifecycle failed: " + failure.Code);
                consumed++;
                if (nativeStep.Kind == NativeLifecycleStepKindV1.CompositeEntered)
                    Trace(BehaviorCaseTraceEvent.NodeEntered, nativeStep.NodeIndex);
                else if (nativeStep.Kind == NativeLifecycleStepKindV1.CompositeExited)
                {
                    Trace(BehaviorCaseTraceEvent.NodeTicked, nativeStep.NodeIndex, _lastTerminalStatus);
                    Trace(BehaviorCaseTraceEvent.NodeExited, nativeStep.NodeIndex, null,
                        _lastTerminalStatus == NodeStatus.Failure ? NodeExitReason.Failure : NodeExitReason.Success);
                }
                else if (nativeStep.Kind == NativeLifecycleStepKindV1.DispatchRequired) Complete(nativeStep);
                else if (nativeStep.Kind == NativeLifecycleStepKindV1.Waiting)
                    return Finish(BehaviorCaseProgress.Waiting, null, before);
                else if (nativeStep.Kind == NativeLifecycleStepKindV1.Completed)
                    return Finish(BehaviorCaseProgress.Completed, nativeStep.RootStatus, before);
            }

            _suspended = true;
            Trace(BehaviorCaseTraceEvent.BudgetYielded);
            return Result(BehaviorCaseProgress.Suspended, null, _control[0].SemanticSteps - before);
        }

        public void Dispose()
        {
            if (_batched != null)
            {
                if (!_batched.TryDispose(out var failure))
                    throw new InvalidOperationException("Native batch disposal failed: " + failure.Code);
                _batched = null;
            }
            Dispose(ref _batchedFailures);
            Dispose(ref _batchedResults);
            Dispose(ref _control); Dispose(ref _parallel); Dispose(ref _cooldown); Dispose(ref _generations);
            Dispose(ref _frames); Dispose(ref _configuration); Dispose(ref _memory); Dispose(ref _bindings);
            Dispose(ref _children); Dispose(ref _nodes);
        }

        private bool TryAdvance(
            out NativeLifecycleStepResultV1 step,
            out NativeRuntimeFailureV1 failure)
        {
            if (_policy == NativeGoldenExecutionPolicyV1.Immediate
                || _policy == NativeGoldenExecutionPolicyV1.PipelinedJobs)
                return _machine.TryAdvance(out step, out failure);
            if (_policy == NativeGoldenExecutionPolicyV1.Budgeted)
            {
                if (!NativeLifecycleBudgetDriverV1.TryBeginSegment(1, ref _budgetState))
                {
                    step = default;
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                        NativeResourceKindV1.InstanceBudgetState);
                    return false;
                }
                return NativeLifecycleBudgetDriverV1.TryAdvance(
                    ref _machine, ref _budgetState, out _, out step, out failure);
            }

            if (!_batched.TrySchedule(1, default, out var dependency, out failure))
            {
                step = default;
                return false;
            }
            dependency.Complete();
            if (!_batched.TryComplete(_batchedResults, _batchedFailures, out failure))
            {
                step = default;
                return false;
            }
            step = _batchedResults[0];
            return true;
        }

        private void Begin(
            ulong updateId,
            Revision revision,
            long timeMicroseconds,
            IReadOnlyList<BehaviorCaseCompletion> completions,
            bool abort)
        {
            if (_suspended) throw new InvalidOperationException("A suspended pass must resume before a new update.");
            _currentUpdateId = updateId;
            _currentRevision = revision;
            _completion = null;
            for (var index = 0; index < completions.Count; index++)
                if (_asyncActive && completions[index].OperationId == _operation)
                {
                    _completion = completions[index].Outcome;
                    break;
                }
            if (!_machine.TryBeginUpdate(updateId, timeMicroseconds, out var failure))
                throw new InvalidOperationException("Native update failed: " + failure.Code);
            Trace(BehaviorCaseTraceEvent.UpdateStarted);
            if (!abort) return;
            _abortPending = true;
            _activeAbortReason = BurstNodeAbortReason.Explicit;
            _activeAbortSource = 0;
            if (!_machine.TryRequestAbort(BurstNodeAbortReason.Explicit, out failure))
                throw new InvalidOperationException("Native abort failed: " + failure.Code);
        }

        private void Complete(NativeLifecycleStepResultV1 step)
        {
            var status = NodeStatus.Running;
            switch (step.Phase)
            {
                case BurstCallbackPhase.Enter:
                    Trace(BehaviorCaseTraceEvent.NodeEntered, step.NodeIndex);
                    break;
                case BurstCallbackPhase.Tick:
                    status = Tick(step.NodeIndex);
                    _lastStatuses[step.NodeIndex] = status;
                    Trace(BehaviorCaseTraceEvent.NodeTicked, step.NodeIndex, status);
                    break;
                case BurstCallbackPhase.Abort:
                    if (Is(step.NodeIndex, SemanticSliceNodeContracts.AsyncActionTypeId) && _asyncActive)
                    {
                        EmitCommand(CommandPhase.Cancel, SemanticSliceNodeContracts.AsyncCancelCommandType, _operation);
                        _asyncActive = false;
                    }
                    Trace(BehaviorCaseTraceEvent.NodeAbortStarted, step.NodeIndex, null, null,
                        Map(_activeAbortReason), new RuntimeNodeIndex(_activeAbortSource));
                    break;
                case BurstCallbackPhase.Exit:
                    var aborted = _abortPending || _activeAbortReason == BurstNodeAbortReason.ObserverLowerPriority;
                    var terminal = _lastStatuses[step.NodeIndex];
                    Trace(BehaviorCaseTraceEvent.NodeExited, step.NodeIndex, null,
                        aborted ? NodeExitReason.Aborted : terminal == NodeStatus.Failure ? NodeExitReason.Failure : NodeExitReason.Success);
                    if (terminal == NodeStatus.Success || terminal == NodeStatus.Failure) _lastTerminalStatus = terminal;
                    if (aborted)
                    {
                        _abortPending = false;
                        _activeAbortReason = default;
                        _activeAbortSource = 0;
                    }
                    break;
                default:
                    throw new InvalidOperationException("Observer callbacks are evaluator-only.");
            }
            if (!_machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, status, out var failure))
                throw new InvalidOperationException("Native dispatch completion failed: " + failure.Code);
            if (step.Phase == BurstCallbackPhase.Tick && _observerPending)
            {
                _observerPending = false;
                Trace(BehaviorCaseTraceEvent.ObserverEvaluated, 1, NodeStatus.Success);
                _activeAbortReason = BurstNodeAbortReason.ObserverLowerPriority;
                _activeAbortSource = 1;
                if (!_machine.TryApplyObserverTransition(new NativeObserverTransitionV1(
                    1, 0, CompiledObserverMode.LowerPriority,
                    BurstNodeAbortReason.ObserverLowerPriority), out var observerFailure))
                    throw new InvalidOperationException("Native observer transition failed: " + observerFailure.Code);
            }
        }

        private NodeStatus Tick(uint nodeIndex)
        {
            if (Is(nodeIndex, ReferenceFixtureNodeManifests.SuccessTypeId)) return NodeStatus.Success;
            if (Is(nodeIndex, ReferenceFixtureNodeManifests.FailureTypeId)) return NodeStatus.Failure;
            if (Is(nodeIndex, ReferenceFixtureNodeManifests.RunningTypeId)) return NodeStatus.Running;
            if (Is(nodeIndex, SemanticSliceNodeContracts.AlertConditionTypeId))
                return Bool(SemanticSliceNodeContracts.AlertKey) ? NodeStatus.Success : NodeStatus.Failure;
            if (Is(nodeIndex, SemanticSliceNodeContracts.RaiseAlertTypeId))
            {
                var entry = _blackboard[SemanticSliceNodeContracts.AlertKey];
                if (!Bool(SemanticSliceNodeContracts.AlertKey))
                {
                    entry.Value = BlackboardValue.FromBool(true);
                    entry.Version++;
                    Trace(BehaviorCaseTraceEvent.BlackboardChanged, nodeIndex, null, null, null, null,
                        StableHash.Fnv1A64(SemanticSliceNodeContracts.AlertKey), entry.Version - 1, entry.Version);
                    Trace(BehaviorCaseTraceEvent.ObserverQueued, 1);
                    _observerPending = true;
                }
                return NodeStatus.Running;
            }
            if (Is(nodeIndex, SemanticSliceNodeContracts.AsyncActionTypeId))
            {
                if (!_asyncActive)
                {
                    _commandSequence++;
                    _operation = new OperationId(_treeInstanceId, new RuntimeNodeIndex(nodeIndex), _generations[(int)nodeIndex], _commandSequence);
                    _asyncActive = true;
                    EmitCommand(CommandPhase.Execute, SemanticSliceNodeContracts.AsyncStartCommandType, _operation, false);
                    return NodeStatus.Running;
                }
                if (!_completion.HasValue) return NodeStatus.Running;
                _asyncActive = false;
                switch (_completion.Value)
                {
                    case CompletionOutcome.Succeeded: return NodeStatus.Success;
                    case CompletionOutcome.Failed:
                    case CompletionOutcome.Cancelled: return NodeStatus.Failure;
                    default: throw new ArgumentOutOfRangeException();
                }
            }
            throw new InvalidOperationException("Golden native adapter encountered an unsupported leaf.");
        }

        private void EmitCommand(CommandPhase phase, string typeId, OperationId operation, bool increment = true)
        {
            if (increment) _commandSequence++;
            var type = new CommandType(StableHash.Fnv1A64(typeId), 1);
            _commands.Add(new BehaviorCaseCommandExpectation(
                phase, type, _treeInstanceId, _commandSequence, operation,
                BehaviorCaseValue.Registered(type.TypeId, type.Version, Array.Empty<byte>())));
            Trace(BehaviorCaseTraceEvent.CommandEmitted, operation.NodeIndex.Value);
        }

        private BehaviorCaseExecutorStepResult Finish(BehaviorCaseProgress progress, NodeStatus? status, ulong before)
        {
            Trace(BehaviorCaseTraceEvent.UpdateCompleted);
            _activeAbortReason = default;
            _activeAbortSource = 0;
            return Result(progress, status, _control[0].SemanticSteps - before);
        }

        private BehaviorCaseExecutorStepResult Result(BehaviorCaseProgress progress, NodeStatus? status, ulong steps)
            => new BehaviorCaseExecutorStepResult(
                progress, status, steps, Snapshot(), _commands, _trace, DiagnosticCollection.Empty,
                _asyncActive ? 1u : 0u, _control[0].Depth);

        private IReadOnlyDictionary<string, BehaviorCaseObservedBlackboardValue> Snapshot()
        {
            var result = new SortedDictionary<string, BehaviorCaseObservedBlackboardValue>(Utf8OrdinalComparer.Instance);
            foreach (var pair in _blackboard)
                result.Add(pair.Key, new BehaviorCaseObservedBlackboardValue(
                    BehaviorCaseValue.BuiltIn(pair.Value.Value), pair.Value.Version));
            return new ReadOnlyDictionary<string, BehaviorCaseObservedBlackboardValue>(result);
        }

        private void Trace(
            BehaviorCaseTraceEvent kind,
            uint? node = null,
            NodeStatus? status = null,
            NodeExitReason? exit = null,
            NodeAbortReason? abort = null,
            RuntimeNodeIndex? source = null,
            ulong? key = null,
            ulong? oldVersion = null,
            ulong? newVersion = null)
            => _trace.Add(new BehaviorCaseObservedTrace(
                kind, BehaviorCaseObservedTrace.SupportedFormatVersion, _program.Header.CanonicalSemanticHash,
                _treeInstanceId, ++_traceSequence, _currentUpdateId, _currentRevision,
                node.HasValue ? new RuntimeNodeIndex(node.Value) : (RuntimeNodeIndex?)null,
                status, exit, abort, source, null, key, oldVersion, newVersion));

        private bool Bool(string key)
        {
            if (!_blackboard[key].Value.TryGetBool(out var value)) throw new InvalidOperationException("Expected Bool blackboard value.");
            return value;
        }

        private bool Is(uint nodeIndex, string typeId)
            => _nodes[(int)nodeIndex].NodeTypeId == StableHash.Fnv1A64(typeId);

        private void CreateMachine()
        {
            _nodes = new NativeArray<NativeCompiledNodeRecordV1>(_program.Nodes.Count, Allocator.Persistent);
            _children = new NativeArray<uint>(_program.ChildIndices.Count, Allocator.Persistent);
            _bindings = new NativeArray<NativeLifecycleNodeBindingV1>(_program.Nodes.Count, Allocator.Persistent);
            _memory = new NativeArray<byte>((int)_program.Header.InstanceNodeMemorySize, Allocator.Persistent);
            _configuration = new NativeArray<byte>(_program.ConfigBlob.Count, Allocator.Persistent);
            _frames = new NativeArray<NativeFrameStateV1>(_program.Nodes.Count, Allocator.Persistent);
            _generations = new NativeArray<uint>(_program.Nodes.Count, Allocator.Persistent);
            _control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            _lastStatuses = new NodeStatus[_program.Nodes.Count];
            uint parallelCount = 0;
            var hasCooldown = false;
            for (var index = 0; index < _program.Nodes.Count; index++)
            {
                _nodes[index] = new NativeCompiledNodeRecordV1(_program.Nodes[index]);
                var kind = Kind(_program.Nodes[index].NodeTypeId);
                _bindings[index] = new NativeLifecycleNodeBindingV1((uint)index, kind);
                if (kind == NativeLifecycleNodeKindV1.Parallel) parallelCount += _program.Nodes[index].Children.Count;
                if (kind == NativeLifecycleNodeKindV1.Cooldown) hasCooldown = true;
            }
            for (var index = 0; index < _program.ChildIndices.Count; index++) _children[index] = _program.ChildIndices[index];
            for (var index = 0; index < _program.ConfigBlob.Count; index++) _configuration[index] = _program.ConfigBlob[index];
            _cooldown = new NativeArray<byte>(hasCooldown ? _program.Nodes.Count : 0, Allocator.Persistent);
            _parallel = new NativeArray<NativeParallelBranchStateV1>((int)parallelCount, Allocator.Persistent);
            if (!NativeLifecycleMachineV1.TryCreate(
                    _nodes, _children, _bindings, _memory, _frames, _generations, _control,
                    _configuration, _cooldown, _parallel, out _machine, out var failure))
                throw new InvalidOperationException("Native lifecycle creation failed: " + failure.Code);
            if (_policy == NativeGoldenExecutionPolicyV1.BatchedJobsSameFrame)
            {
                _batchedResults = new NativeArray<NativeLifecycleStepResultV1>(1, Allocator.Persistent);
                _batchedFailures = new NativeArray<NativeRuntimeFailureV1>(1, Allocator.Persistent);
                if (!NativeBatchedLifecycleOwnerV1.TryCreate(
                        new[] { _machine }, Allocator.Persistent, out _batched, out failure))
                    throw new InvalidOperationException("Native batch creation failed: " + failure.Code);
            }
        }

        private static NativeLifecycleNodeKindV1 Kind(ulong typeId)
        {
            if (typeId == StableHash.Fnv1A64("aibt.core.memory-sequence")) return NativeLifecycleNodeKindV1.MemorySequence;
            if (typeId == StableHash.Fnv1A64("aibt.core.memory-selector")) return NativeLifecycleNodeKindV1.MemorySelector;
            if (typeId == StableHash.Fnv1A64("aibt.core.reactive-sequence")) return NativeLifecycleNodeKindV1.ReactiveSequence;
            if (typeId == StableHash.Fnv1A64("aibt.core.reactive-selector")) return NativeLifecycleNodeKindV1.ReactiveSelector;
            if (typeId == StableHash.Fnv1A64("aibt.core.inverter")) return NativeLifecycleNodeKindV1.Inverter;
            if (typeId == StableHash.Fnv1A64("aibt.core.succeeder")) return NativeLifecycleNodeKindV1.Succeeder;
            if (typeId == StableHash.Fnv1A64("aibt.core.failer")) return NativeLifecycleNodeKindV1.Failer;
            if (typeId == StableHash.Fnv1A64("aibt.core.repeater")) return NativeLifecycleNodeKindV1.Repeater;
            if (typeId == StableHash.Fnv1A64("aibt.core.timeout")) return NativeLifecycleNodeKindV1.Timeout;
            if (typeId == StableHash.Fnv1A64("aibt.core.cooldown")) return NativeLifecycleNodeKindV1.Cooldown;
            if (typeId == StableHash.Fnv1A64("aibt.core.parallel")) return NativeLifecycleNodeKindV1.Parallel;
            return NativeLifecycleNodeKindV1.GeneratedLeaf;
        }

        private static NodeAbortReason Map(BurstNodeAbortReason value)
        {
            switch (value)
            {
                case BurstNodeAbortReason.ObserverLowerPriority: return NodeAbortReason.ObserverLowerPriority;
                case BurstNodeAbortReason.ObserverSelf: return NodeAbortReason.ObserverSelf;
                case BurstNodeAbortReason.Timeout: return NodeAbortReason.Timeout;
                default: return NodeAbortReason.Explicit;
            }
        }

        private static Dictionary<string, BlackboardEntry> CreateBlackboard(
            TreeDocument document,
            IReadOnlyDictionary<string, BehaviorCaseValue> initial)
        {
            var result = new Dictionary<string, BlackboardEntry>(StringComparer.Ordinal);
            for (var index = 0; index < document.Blackboard.Count; index++)
            {
                var key = document.Blackboard[index];
                if (key.Type.IsRegistered) throw new NotSupportedException("Golden native fixtures use built-in blackboard values.");
                BlackboardValue value;
                if (initial.TryGetValue(key.Id, out var supplied)) value = supplied.BuiltInValue;
                else if (key.DefaultValue != null && key.DefaultValue.TryGetRuntimeValue(out var defaultValue)) value = defaultValue;
                else value = BlackboardValue.FromBool(false);
                result.Add(key.Id, new BlackboardEntry { Value = value });
            }
            return result;
        }

        private static void Dispose<T>(ref NativeArray<T> value) where T : struct
        {
            if (value.IsCreated) value.Dispose();
            value = default;
        }

        private sealed class BlackboardEntry
        {
            internal BlackboardValue Value;
            internal ulong Version;
        }
    }
}
