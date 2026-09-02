using System;
using System.Collections.Generic;
using Unity.Profiling;

namespace AIBT
{
    internal sealed class ReferenceExecutionMachine
    {
        private static readonly ProfilerMarker s_AdvanceOneStepMarker =
            new ProfilerMarker("AIBT.Reference.LifecycleTick");

        private readonly CompiledProgram _program;
        private readonly TreeInstanceId _treeInstanceId;
        private readonly ReferenceLeafRegistry _leafRegistry;
        private readonly ReferenceMemoryCompositeRegistry _memoryCompositeRegistry;
        private readonly ReferenceReactiveCompositeRegistry _reactiveCompositeRegistry;
        private readonly ReferenceDecoratorRegistry _decoratorRegistry;
        private readonly ReferenceParallelRegistry _parallelRegistry;
        private readonly IReferenceTraceSink _traceSink;
        private readonly byte[] _configuration;
        private readonly byte[] _nodeMemory;
        private readonly uint[] _activationGenerations;
        private readonly bool[] _cooldownInitialized;
        private readonly List<ReferenceFrame> _frames = new List<ReferenceFrame>();
        private readonly ReferenceOperationLedger _operationLedger;
        private readonly ReferenceCompletionInbox _completionInbox;
        private readonly ReferenceCommandBuffer _commandBuffer;
        private readonly ReferenceAsyncCommandCoordinator _asyncCoordinator;
        private readonly ReferenceNodeServices _nodeServices;
        private readonly ReferenceBlackboardStorage _blackboard;
        private readonly ReferenceObserverConditionRegistry _observerRegistry;
        private readonly ReferenceObserverRuntimeState[] _observerStates;
        private readonly ReferenceObserverReadServices _observerReadServices;
        private readonly List<int> _observerQueue = new List<int>();
        private readonly List<BlackboardSlotChange> _pendingResetChanges = new List<BlackboardSlotChange>();
        private readonly List<Diagnostic> _asyncDiagnostics = new List<Diagnostic>();
        private readonly Diagnostic _blackboardConstructionDiagnostic;
        private Diagnostic _pendingNodeServiceFault;

        private ReferenceUpdateContext _currentUpdate;
        private ulong _lastAcceptedUpdateId;
        private ulong _traceSequence;
        private bool _hasOpenUpdate;
        private bool _isExecuting;
        private bool _faulted;
        private bool _stopped;
        private bool _waitingPending;
        private bool _parallelSuspendPending;
        private bool _terminalCompletionPending;
        private bool _budgetSuspended;
        private ulong _cumulativeUpdateSteps;
        private NodeStatus? _terminalResult;
        private int _abortTargetDepth = -1;
        private NodeAbortReason _abortReason;
        private RuntimeNodeIndex _abortSourceNodeIndex = RuntimeNodeIndex.Invalid;
        private int _reactiveResetOwnerDepth = -1;
        private bool _abortForReactiveReset;
        private bool _abortForTimeout;
        private int _timeoutOwnerDepth = -1;

        internal ReferenceExecutionMachine(
            CompiledProgram program,
            TreeInstanceId treeInstanceId,
            ReferenceLeafRegistry leafRegistry,
            IReferenceTraceSink traceSink = null,
            ReferenceMemoryCompositeRegistry memoryCompositeRegistry = null,
            ReferenceReactiveCompositeRegistry reactiveCompositeRegistry = null,
            ReferenceDecoratorRegistry decoratorRegistry = null,
            ReferenceParallelRegistry parallelRegistry = null,
            RegisteredBlackboardRegistry registeredBlackboardRegistry = null,
            ReferenceObserverConditionRegistry observerRegistry = null,
            CompiledHash expectedRegisteredBlackboardRegistryHash = default,
            IReadOnlyList<ReferenceBlackboardInitialValue> initialBlackboard = null)
        {
            _program = program ?? throw new ArgumentNullException(nameof(program));
            if (!treeInstanceId.IsValid) throw new ArgumentException("A tree instance ID is required.", nameof(treeInstanceId));
            _treeInstanceId = treeInstanceId;
            _leafRegistry = leafRegistry ?? throw new ArgumentNullException(nameof(leafRegistry));
            _memoryCompositeRegistry = memoryCompositeRegistry ?? ReferenceMemoryCompositeRegistry.Empty;
            _reactiveCompositeRegistry = reactiveCompositeRegistry ?? ReferenceReactiveCompositeRegistry.Empty;
            _decoratorRegistry = decoratorRegistry ?? ReferenceDecoratorRegistry.Empty;
            _parallelRegistry = parallelRegistry ?? ReferenceParallelRegistry.Empty;
            _observerRegistry = observerRegistry ?? ReferenceObserverConditionRegistry.Empty;
            _traceSink = traceSink ?? NullReferenceTraceSink.Instance;
            _configuration = Copy(program.ConfigBlob);
            _nodeMemory = new byte[checked((int)program.Header.InstanceNodeMemorySize)];
            _activationGenerations = new uint[program.Nodes.Count];
            _cooldownInitialized = new bool[program.Nodes.Count];
            _operationLedger = new ReferenceOperationLedger(treeInstanceId);
            _completionInbox = new ReferenceCompletionInbox(_operationLedger);
            _commandBuffer = new ReferenceCommandBuffer(treeInstanceId);
            _asyncCoordinator = new ReferenceAsyncCommandCoordinator(_operationLedger, _commandBuffer);
            _nodeServices = new ReferenceNodeServices(this);
            if (!ReferenceBlackboardStorage.TryCreate(
                program,
                treeInstanceId,
                registeredBlackboardRegistry ?? RegisteredBlackboardRegistry.Empty,
                out _blackboard,
                out var blackboardDiagnostic,
                expectedRegisteredBlackboardRegistryHash,
                initialBlackboard))
            {
                _blackboardConstructionDiagnostic = blackboardDiagnostic;
                _pendingNodeServiceFault = blackboardDiagnostic;
            }
            _observerStates = new ReferenceObserverRuntimeState[program.Observers.Count];
            for (var index = 0; index < _observerStates.Length; index++)
                _observerStates[index] = new ReferenceObserverRuntimeState();
            _observerReadServices = new ReferenceObserverReadServices(this);
        }

        internal bool IsFaulted => _faulted;

        internal bool HasOpenUpdate => _hasOpenUpdate;

        internal NodeStatus? TerminalResult => _terminalResult;

        internal ReferenceExecutionInspection CaptureInspection()
        {
            if (_isExecuting)
                throw new InvalidOperationException("Execution inspection is available only at a stable API boundary.");
            if (_blackboard == null)
                throw new InvalidOperationException("Execution inspection requires a valid blackboard instance.");

            return new ReferenceExecutionInspection(
                _blackboard.CaptureSnapshot(),
                CountActiveFrames(_frames),
                checked((uint)_operationLedger.ActiveCount));
        }

        /// <summary>
        /// Captures <paramref name="nodeIndex"/>'s persisted instance state (memory, activation
        /// generation, cooldown-init flag) for hot-reload migration into another instance
        /// (<c>ADR-P5-001</c>). Read-only; never mutates this instance. Does not capture this
        /// node's position on the active frame stack -- migration
        /// (<c>Runtime/HotReload/Migration/</c>) only ever calls this when the source instance has
        /// no active frames at all.
        /// </summary>
        internal ReferenceNodeStateSnapshot CaptureNodeState(RuntimeNodeIndex nodeIndex)
        {
            if (!nodeIndex.IsValid || nodeIndex.Value >= (uint)_program.Nodes.Count)
                throw new ArgumentOutOfRangeException(nameof(nodeIndex));

            var record = _program.Nodes[(int)nodeIndex.Value];
            var memory = new byte[record.InstanceMemorySize];
            Array.Copy(_nodeMemory, (int)record.InstanceMemoryOffset, memory, 0, (int)record.InstanceMemorySize);
            return new ReferenceNodeStateSnapshot(
                memory,
                _activationGenerations[(int)nodeIndex.Value],
                _cooldownInitialized[(int)nodeIndex.Value]);
        }

        /// <summary>
        /// Seeds <paramref name="nodeIndex"/>'s persisted instance state from a snapshot captured
        /// on another, layout-compatible instance, for hot-reload migration (<c>ADR-P5-001</c>).
        /// Valid only before this instance's first accepted update -- migration happens once,
        /// immediately after construction, never mid-execution, matching how the constructor's own
        /// <c>initialBlackboard</c> parameter seeds blackboard state.
        /// </summary>
        internal void SeedNodeState(RuntimeNodeIndex nodeIndex, ReferenceNodeStateSnapshot snapshot)
        {
            if (_lastAcceptedUpdateId != 0 || _hasOpenUpdate)
                throw new InvalidOperationException("Node state can be seeded only before this instance's first update.");
            if (!nodeIndex.IsValid || nodeIndex.Value >= (uint)_program.Nodes.Count)
                throw new ArgumentOutOfRangeException(nameof(nodeIndex));

            var record = _program.Nodes[(int)nodeIndex.Value];
            if (snapshot.Memory.Length != (int)record.InstanceMemorySize)
            {
                throw new ArgumentException(
                    "The snapshot's memory size does not match this node's instance-memory size.", nameof(snapshot));
            }

            Array.Copy(snapshot.Memory, 0, _nodeMemory, (int)record.InstanceMemoryOffset, (int)record.InstanceMemorySize);
            _activationGenerations[(int)nodeIndex.Value] = snapshot.ActivationGeneration;
            _cooldownInitialized[(int)nodeIndex.Value] = snapshot.CooldownInitialized;
        }

        internal ReferenceExecutionEnvelope Update(ReferenceUpdateContext update)
            => Update(update, ReferenceStepBudget.Unlimited);

        internal ReferenceExecutionEnvelope Update(
            ReferenceUpdateContext update,
            ReferenceStepBudget budget)
        {
            if (!TryEnterApi(out var rejected, update)) return rejected;
            try
            {
                var begin = BeginUpdateCore(update);
                if (begin.Progress != ReferenceExecutionProgress.Suspended) return Deliver(begin);
                return RunBudgetSegment(budget, false);
            }
            finally
            {
                _isExecuting = false;
            }
        }

        internal ReferenceExecutionEnvelope Resume(ReferenceStepBudget budget)
        {
            if (!TryEnterApi(out var rejected)) return rejected;
            try
            {
                if (!_hasOpenUpdate || !_budgetSuspended)
                    return Deliver(Reject("Only a budget-suspended update can be resumed."));
                return RunBudgetSegment(budget, true);
            }
            finally
            {
                _isExecuting = false;
            }
        }

        private ReferenceExecutionEnvelope RunBudgetSegment(
            ReferenceStepBudget budget,
            bool isResume)
        {
            if (!budget.IsUnlimited && budget.StepLimit == 0)
                return YieldForBudget(0);

            if (isResume)
            {
                var boundary = DrainZeroStepBoundary();
                if (boundary.HasValue && boundary.Value.Progress != ReferenceExecutionProgress.Suspended)
                {
                    var completed = boundary.Value;
                    return Deliver(Envelope(
                        completed.Progress,
                        completed.RootResult,
                        0,
                        completed.Diagnostics));
                }

                _budgetSuspended = false;
                Emit(ReferenceTraceEventKind.ExecutionResumed);
            }

            ulong segmentSteps = 0;
            while (_hasOpenUpdate)
            {
                if (!budget.IsUnlimited && segmentSteps >= budget.StepLimit)
                {
                    var boundary = DrainZeroStepBoundary();
                    if (boundary.HasValue)
                    {
                        var completed = boundary.Value;
                        if (completed.Progress != ReferenceExecutionProgress.Suspended)
                        {
                            return Deliver(Envelope(
                                completed.Progress,
                                completed.RootResult,
                                segmentSteps,
                                completed.Diagnostics));
                        }
                    }

                    if (_hasOpenUpdate) return YieldForBudget(segmentSteps);
                    return Deliver(Envelope(
                        ReferenceExecutionProgress.Completed,
                        _terminalResult,
                        segmentSteps));
                }

                var result = AdvanceOneStepCore();
                if (!TryAccumulateSteps(ref segmentSteps, result.Steps))
                {
                    return Deliver(WithMetrics(Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidOperation,
                        "The deterministic step counter exceeded UInt64.",
                        RuntimeNodeIndex.Invalid), segmentSteps));
                }
                if (result.Progress != ReferenceExecutionProgress.Suspended)
                {
                    return Deliver(Envelope(
                        result.Progress,
                        result.RootResult,
                        segmentSteps,
                        result.Diagnostics));
                }
            }

            return Deliver(Envelope(
                ReferenceExecutionProgress.Completed,
                _terminalResult,
                segmentSteps));
        }

        private ReferenceExecutionEnvelope? DrainZeroStepBoundary()
        {
            ReferenceExecutionEnvelope? last = null;
            while (_hasOpenUpdate && CanAdvanceWithoutSemanticStep())
            {
                var result = AdvanceOneStepCore();
                if (result.Steps != 0)
                {
                    return Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidOperation,
                        "A zero-step budget boundary unexpectedly executed a semantic step.",
                        RuntimeNodeIndex.Invalid);
                }
                last = result;
                if (result.Progress != ReferenceExecutionProgress.Suspended) return result;
            }
            return last;
        }

        private bool CanAdvanceWithoutSemanticStep()
        {
            if (_observerQueue.Count != 0) return false;
            if (_abortTargetDepth >= 0 && _frames.Count != 0) return false;
            return _parallelSuspendPending
                || _terminalCompletionPending
                || _waitingPending
                || _frames.Count == 0;
        }

        private ReferenceExecutionEnvelope YieldForBudget(ulong segmentSteps)
        {
            if (!_budgetSuspended)
            {
                _budgetSuspended = true;
                Emit(ReferenceTraceEventKind.BudgetYielded);
            }
            return Deliver(Envelope(ReferenceExecutionProgress.Suspended, null, segmentSteps));
        }

        private bool TryAccumulateSteps(ref ulong segmentSteps, ulong steps)
        {
            if (ulong.MaxValue - segmentSteps < steps
                || ulong.MaxValue - _cumulativeUpdateSteps < steps)
            {
                return false;
            }

            segmentSteps += steps;
            _cumulativeUpdateSteps += steps;
            return true;
        }

        private ReferenceExecutionEnvelope WithMetrics(
            ReferenceExecutionEnvelope envelope,
            ulong segmentSteps)
        {
            return new ReferenceExecutionEnvelope(
                envelope.Progress,
                envelope.RootResult,
                segmentSteps,
                _cumulativeUpdateSteps,
                envelope.Diagnostics,
                envelope.Commands);
        }

        internal ReferenceExecutionEnvelope BeginUpdate(ReferenceUpdateContext update)
        {
            if (!TryEnterApi(out var rejected, update)) return rejected;
            try
            {
                return Deliver(BeginUpdateCore(update));
            }
            finally
            {
                _isExecuting = false;
            }
        }

        internal ReferenceExecutionEnvelope AdvanceOneStep()
        {
            using var _ = s_AdvanceOneStepMarker.Auto();
            if (!TryEnterApi(out var rejected)) return rejected;
            try
            {
                if (_budgetSuspended)
                    return Deliver(Reject("A budget-suspended update must continue through Resume."));
                var result = AdvanceOneStepCore();
                ulong segmentSteps = 0;
                if (!TryAccumulateSteps(ref segmentSteps, result.Steps))
                {
                    return Deliver(Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidOperation,
                        "The deterministic step counter exceeded UInt64.",
                        RuntimeNodeIndex.Invalid));
                }
                return Deliver(WithMetrics(result, result.Steps));
            }
            finally
            {
                _isExecuting = false;
            }
        }

        internal ReferenceExecutionEnvelope RequestAbort(NodeAbortReason reason, RuntimeNodeIndex sourceNodeIndex)
        {
            if (!TryEnterApi(out var rejected)) return rejected;
            try
            {
                if (!TryValidateAbort(reason, sourceNodeIndex, out rejected)) return Deliver(rejected);
                if (!_hasOpenUpdate)
                {
                    return Deliver(Reject("An abort request requires an open update."));
                }

                if (_frames.Count == 0)
                {
                    return Deliver(Reject("There is no active node to abort."));
                }

                BeginAbortTraversal(0, reason, sourceNodeIndex);
                return Deliver(Envelope(ReferenceExecutionProgress.Suspended, null, 0));
            }
            finally
            {
                _isExecuting = false;
            }
        }

        internal ReferenceExecutionEnvelope Abort(
            ReferenceUpdateContext update,
            NodeAbortReason reason,
            RuntimeNodeIndex sourceNodeIndex)
            => Abort(update, reason, sourceNodeIndex, ReferenceStepBudget.Unlimited);

        internal ReferenceExecutionEnvelope Abort(
            ReferenceUpdateContext update,
            NodeAbortReason reason,
            RuntimeNodeIndex sourceNodeIndex,
            ReferenceStepBudget budget)
        {
            if (!TryEnterApi(out var rejected, update)) return rejected;
            try
            {
                if (!TryValidateAbort(reason, sourceNodeIndex, out rejected, update)) return Deliver(rejected);
                if (_terminalResult.HasValue || _faulted || _stopped || _frames.Count == 0)
                {
                    return Deliver(Reject("Only an active tree instance can be aborted.", update));
                }

                var begin = BeginUpdateCore(update);
                if (begin.Progress != ReferenceExecutionProgress.Suspended) return Deliver(begin);
                BeginAbortTraversal(0, reason, sourceNodeIndex);
                return RunBudgetSegment(budget, false);
            }
            finally
            {
                _isExecuting = false;
            }
        }

        internal ReferenceExecutionEnvelope Restart()
        {
            if (!TryEnterApi(out var rejected)) return rejected;
            try
            {
                if (_hasOpenUpdate || (!_terminalResult.HasValue && !_faulted && !_stopped && _frames.Count != 0))
                {
                    return Deliver(Reject("An active tree instance cannot be restarted."));
                }

                for (var index = 0; index < _activationGenerations.Length; index++)
                {
                    if (_activationGenerations[index] == uint.MaxValue)
                    {
                        return Deliver(Reject("The tree instance cannot restart because a node activation generation is exhausted."));
                    }
                }

                if (_blackboard == null)
                {
                    return Deliver(Envelope(
                        ReferenceExecutionProgress.Rejected,
                        _terminalResult,
                        0,
                        new DiagnosticCollection(new[] { _blackboardConstructionDiagnostic })));
                }

                var blackboardReset = _blackboard.Reset();
                if (!blackboardReset.Success)
                {
                    return Deliver(Envelope(
                        ReferenceExecutionProgress.Rejected,
                        _terminalResult,
                        0,
                        new DiagnosticCollection(new[] { blackboardReset.Diagnostic })));
                }
                Array.Clear(_nodeMemory, 0, _nodeMemory.Length);
                Array.Clear(_cooldownInitialized, 0, _cooldownInitialized.Length);
                _pendingResetChanges.Clear();
                for (var index = 0; index < blackboardReset.Changes.Count; index++)
                    _pendingResetChanges.Add(blackboardReset.Changes[index]);
                _observerQueue.Clear();
                for (var index = 0; index < _observerStates.Length; index++)
                {
                    _observerStates[index].IsQueued = false;
                    _observerStates[index].HasLastResult = false;
                }
                _frames.Clear();
                _terminalResult = null;
                _faulted = false;
                _stopped = false;
                _waitingPending = false;
                _parallelSuspendPending = false;
                _terminalCompletionPending = false;
                _budgetSuspended = false;
                _cumulativeUpdateSteps = 0;
                ClearAbortTraversal();
                _reactiveResetOwnerDepth = -1;
                return Deliver(Envelope(ReferenceExecutionProgress.Completed, null, 0));
            }
            finally
            {
                _isExecuting = false;
            }
        }

        private ReferenceExecutionEnvelope BeginUpdateCore(ReferenceUpdateContext update)
        {
            if (_hasOpenUpdate) return Reject("A tree instance already has an open update.", update);
            if (update.UpdateId <= _lastAcceptedUpdateId) return Reject("Update IDs must strictly increase.", update);
            if (_faulted) return Reject("A faulted tree instance must be restarted before another update.", update);
            if (_stopped) return Reject("A stopped tree instance must be restarted before another update.", update);

            _lastAcceptedUpdateId = update.UpdateId;
            _currentUpdate = update;
            _hasOpenUpdate = true;
            _budgetSuspended = false;
            _cumulativeUpdateSteps = 0;
            Emit(ReferenceTraceEventKind.UpdateStarted);
            if (_pendingNodeServiceFault != null) return BlackboardFault();
            if (!TryValidateObserverTopology(out var observerTopologyFailure)) return observerTopologyFailure;
            PublishPendingResetChanges();
            AddAsyncDiagnostics(_completionInbox.Normalize(update.Completions, _activationGenerations), true);

            if (_terminalResult.HasValue)
            {
                if (_observerQueue.Count != 0)
                {
                    _terminalCompletionPending = true;
                    return Envelope(ReferenceExecutionProgress.Suspended, _terminalResult, 0);
                }
                FinishUpdate();
                return Envelope(ReferenceExecutionProgress.Completed, _terminalResult, 0);
            }

            if (_frames.Count == 0)
            {
                _frames.Add(new ReferenceFrame(new RuntimeNodeIndex(_program.Header.RootNodeIndex)));
            }
            else
            {
                if (!PrepareExpiredTimeoutForNewUpdate()) PrepareReactiveResetForNewUpdate();
            }

            return Envelope(ReferenceExecutionProgress.Suspended, null, 0);
        }

        internal BlackboardResetResult ResetBlackboard()
        {
            if (_isExecuting || _hasOpenUpdate)
            {
                return BlackboardResetResult.Failed(BlackboardStorageDiagnostics.Create(
                    BlackboardStorageDiagnosticCodes.InvalidSlot,
                    "Blackboard reset requires an idle, non-reentrant tree instance.",
                    _treeInstanceId));
            }

            if (_blackboard == null)
            {
                return BlackboardResetResult.Failed(_blackboardConstructionDiagnostic);
            }

            _isExecuting = true;
            try
            {
                var result = _blackboard.Reset();
                if (!result.Success) return result;
                for (var index = 0; index < result.Changes.Count; index++)
                    _pendingResetChanges.Add(result.Changes[index]);
                return result;
            }
            finally
            {
                _isExecuting = false;
            }
        }

        private ReferenceExecutionEnvelope AdvanceOneStepCore()
        {
            if (!_hasOpenUpdate) return Reject("Advancing requires an open update.");
            if (_observerQueue.Count != 0 && _abortTargetDepth < 0)
                return EvaluateNextObserver();
            if (_parallelSuspendPending && _abortTargetDepth < 0)
            {
                _parallelSuspendPending = false;
                if (!TrySuspendActiveParallelBranch())
                    return Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                        "A pending parallel branch suspension has no active branch.",
                        _frames[_frames.Count - 1].NodeIndex);
                return Envelope(ReferenceExecutionProgress.Suspended, null, 0);
            }
            if (_terminalCompletionPending && _abortTargetDepth < 0)
            {
                _terminalCompletionPending = false;
                FinishUpdate();
                return Envelope(ReferenceExecutionProgress.Completed, _terminalResult, 0);
            }
            if (_waitingPending && _abortTargetDepth < 0)
            {
                _waitingPending = false;
                FinishUpdate();
                return Envelope(ReferenceExecutionProgress.Waiting, null, 0);
            }
            if (_frames.Count == 0)
            {
                ClearAbortTraversal();
                FinishUpdate();
                return Envelope(ReferenceExecutionProgress.Completed, _terminalResult, 0);
            }

            var frame = _frames[_frames.Count - 1];
            var node = _program.Nodes[(int)frame.NodeIndex.Value];
            if (_parallelRegistry.Contains(node.NodeTypeId, node.NodeTypeVersion))
            {
                return AdvanceParallel(frame, node);
            }

            if (_decoratorRegistry.TryGet(node.NodeTypeId, node.NodeTypeVersion, out var decoratorKind))
            {
                return AdvanceDecorator(frame, node, decoratorKind);
            }

            if (_reactiveCompositeRegistry.TryGet(node.NodeTypeId, node.NodeTypeVersion, out var reactiveHandler))
            {
                return AdvanceReactiveComposite(frame, node, reactiveHandler);
            }

            if (_memoryCompositeRegistry.TryGet(node.NodeTypeId, node.NodeTypeVersion, out var compositeHandler))
            {
                return AdvanceMemoryComposite(frame, node, compositeHandler);
            }

            if (!_leafRegistry.TryGet(node.NodeTypeId, node.NodeTypeVersion, out var handler))
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.MissingHandler,
                    "No explicit reference leaf handler is bound for the compiled node type and version.",
                    frame.NodeIndex);
            }

            if (node.Children.Count != 0)
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.UnsupportedNode,
                    "A reference leaf binding cannot execute a node with children.",
                    frame.NodeIndex);
            }

            switch (frame.State)
            {
                case ReferenceFrameState.Inactive:
                    return Enter(frame, node, handler);
                case ReferenceFrameState.Entered:
                case ReferenceFrameState.Running:
                    return Tick(frame, node, handler);
                case ReferenceFrameState.TerminalPendingExit:
                    return ExitTerminal(frame, node, handler);
                case ReferenceFrameState.Aborting:
                    return frame.AbortCallbackCompleted
                        ? ExitAborted(frame, node, handler)
                        : AbortFrame(frame, node, handler);
                default:
                    return Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidOperation,
                        "The reference frame has an unknown lifecycle state.",
                        frame.NodeIndex);
            }
        }

        private ReferenceExecutionEnvelope AdvanceMemoryComposite(
            ReferenceFrame frame,
            CompiledNodeRecord node,
            IReferenceMemoryCompositeHandler handler)
        {
            if (!IsValidMemoryCompositeStorage(node))
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                    "A memory composite requires exactly four bytes of four-byte-aligned Activation memory.",
                    frame.NodeIndex);
            }

            switch (frame.State)
            {
                case ReferenceFrameState.Inactive:
                    return EnterComposite(frame, node);
                case ReferenceFrameState.Entered:
                case ReferenceFrameState.Running:
                    return frame.HasPendingChildResult
                        ? AcceptCompositeChild(frame, node, handler)
                        : SelectCompositeChild(frame, node, handler);
                case ReferenceFrameState.TerminalPendingExit:
                    return ExitCompositeTerminal(frame, node);
                case ReferenceFrameState.Aborting:
                    return frame.AbortCallbackCompleted
                        ? ExitCompositeAborted(frame, node)
                        : AbortComposite(frame);
                default:
                    return Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                        "The memory-composite frame has an unknown lifecycle state.",
                        frame.NodeIndex);
            }
        }

        private ReferenceExecutionEnvelope AdvanceDecorator(
            ReferenceFrame frame,
            CompiledNodeRecord node,
            ReferenceDecoratorKind kind)
        {
            if (node.Children.Count != 1)
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidNodeConfiguration,
                    "A Phase 1 decorator requires exactly one child.",
                    frame.NodeIndex);
            }

            if (!IsValidDecoratorStorage(node, kind))
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidNodeConfiguration,
                    "The decorator memory descriptor does not match its Phase 1 contract.",
                    frame.NodeIndex);
            }

            if (!frame.ConfigurationDecoded && !TryDecodeDecoratorConfiguration(frame, node, kind, out var failure))
            {
                return failure;
            }

            switch (frame.State)
            {
                case ReferenceFrameState.Inactive:
                    if (!TryBeginActivation(frame, node, out failure)) return failure;
                    frame.State = ReferenceFrameState.Entered;
                    var deadline = default(long);
                    if (kind == ReferenceDecoratorKind.Timeout
                        && !TryAddDuration(_currentUpdate.TimeMicroseconds, frame.DurationMicroseconds, out deadline))
                    {
                        return Fault(ReferenceExecutionDiagnosticCodes.TimeOverflow, "The timeout deadline overflowed Int64 time.", frame.NodeIndex);
                    }
                    else if (kind == ReferenceDecoratorKind.Timeout)
                    {
                        frame.DeadlineMicroseconds = deadline;
                        WriteInt64(node, deadline);
                    }
                    Emit(ReferenceTraceEventKind.NodeEntered, frame.NodeIndex);
                    return Envelope(ReferenceExecutionProgress.Suspended, null, 1);

                case ReferenceFrameState.Entered:
                case ReferenceFrameState.Running:
                    if (frame.TimeoutTriggered)
                    {
                        frame.TimeoutTriggered = false;
                        return SetDecoratorTerminal(frame, frame.ConfiguredResult);
                    }
                    if (frame.HasPendingChildResult) return AcceptDecoratorChild(frame, node);
                    return SelectDecoratorChild(frame, node);

                case ReferenceFrameState.TerminalPendingExit:
                    return ExitCompositeTerminal(frame, node);

                case ReferenceFrameState.Aborting:
                    return frame.AbortCallbackCompleted
                        ? ExitCompositeAborted(frame, node)
                        : AbortComposite(frame);

                default:
                    return Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidOperation,
                        "The decorator frame has an unknown lifecycle state.",
                        frame.NodeIndex);
            }
        }

        private bool PrepareExpiredTimeoutForNewUpdate(int startDepth = 0)
        {
            for (var depth = startDepth; depth < _frames.Count; depth++)
            {
                var frame = _frames[depth];
                var node = _program.Nodes[(int)frame.NodeIndex.Value];
                if (_decoratorRegistry.TryGet(node.NodeTypeId, node.NodeTypeVersion, out var kind)
                    && kind == ReferenceDecoratorKind.Timeout
                    && frame.ConfigurationDecoded
                    && frame.ChildSelected
                    && _currentUpdate.TimeMicroseconds >= ReadInt64(node))
                {
                    _timeoutOwnerDepth = depth;
                    _abortForTimeout = true;
                    frame.TimeoutTriggered = true;
                    frame.ChildSelected = false;
                    BeginAbortTraversal(
                        depth + 1,
                        NodeAbortReason.Timeout,
                        frame.NodeIndex,
                        forTimeout: true);
                    return true;
                }
            }

            return false;
        }

        private ReferenceExecutionEnvelope AdvanceParallel(ReferenceFrame frame, CompiledNodeRecord node)
        {
            if (!IsValidParallelStorage(node))
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidNodeConfiguration,
                    "Parallel requires exactly eight bytes of four-byte-aligned Activation memory.",
                    frame.NodeIndex);
            }

            if (!frame.ConfigurationDecoded && !TryDecodeParallelConfiguration(frame, node, out var failure))
            {
                return failure;
            }

            switch (frame.State)
            {
                case ReferenceFrameState.Inactive:
                    if (!TryBeginActivation(frame, node, out failure)) return failure;
                    frame.ParallelBranches = new ReferenceParallelBranch[node.Children.Count];
                    for (uint ordinal = 0; ordinal < node.Children.Count; ordinal++)
                        frame.ParallelBranches[ordinal] = new ReferenceParallelBranch(ordinal);
                    frame.State = ReferenceFrameState.Entered;
                    Emit(ReferenceTraceEventKind.NodeEntered, frame.NodeIndex);
                    return Envelope(ReferenceExecutionProgress.Suspended, null, 1);

                case ReferenceFrameState.Entered:
                case ReferenceFrameState.Running:
                    if (frame.ParallelAbortPending) return AdvanceParallelAbort(frame);
                    if (frame.HasPendingChildResult) return AcceptParallelChild(frame, node);
                    return SelectParallelBranch(frame, node);

                case ReferenceFrameState.TerminalPendingExit:
                    return ExitCompositeTerminal(frame, node);

                case ReferenceFrameState.Aborting:
                    RetireActiveParallelBranchAfterTraversal(frame);
                    if (frame.ParallelAbortPending
                        || frame.ParallelBranchAbortActive
                        || HasRunningParallelBranch(frame))
                    {
                        frame.ParallelAbortPending = true;
                        return AdvanceParallelAbort(frame);
                    }
                    return frame.AbortCallbackCompleted
                        ? ExitCompositeAborted(frame, node)
                        : AbortComposite(frame);

                default:
                    return Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                        "The parallel frame has an unknown lifecycle state.",
                        frame.NodeIndex);
            }
        }

        private bool TryDecodeParallelConfiguration(
            ReferenceFrame frame,
            CompiledNodeRecord node,
            out ReferenceExecutionEnvelope failure)
        {
            try
            {
                frame.ParallelConfiguration = ReferenceParallelConfigurationDecoder.Decode(
                    GetConfiguration(node),
                    node.Children.Count);
                frame.ConfigurationDecoded = true;
                failure = default;
                return true;
            }
            catch (Exception exception)
            {
                failure = Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidNodeConfiguration,
                    "Invalid parallel configuration: " + exception.GetType().Name + ".",
                    frame.NodeIndex);
                return false;
            }
        }

        private ReferenceExecutionEnvelope SelectParallelBranch(ReferenceFrame frame, CompiledNodeRecord node)
        {
            if (frame.ParallelVisitCursor >= node.Children.Count)
                return CompleteParallelVisit(frame);

            var ordinal = frame.ParallelVisitCursor++;
            var branch = frame.ParallelBranches[ordinal];
            if (branch.State == ReferenceParallelChildState.Success
                || branch.State == ReferenceParallelChildState.Failure)
            {
                return frame.ParallelVisitCursor == node.Children.Count
                    ? CompleteParallelVisit(frame)
                    : Envelope(ReferenceExecutionProgress.Suspended, null, 1);
            }

            frame.ActiveParallelBranchOrdinal = checked((int)ordinal);
            frame.State = ReferenceFrameState.Running;
            if (branch.State == ReferenceParallelChildState.NotStarted)
            {
                var childTableIndex = checked((int)(node.Children.Offset + ordinal));
                _frames.Add(new ReferenceFrame(new RuntimeNodeIndex(_program.ChildIndices[childTableIndex])));
            }
            else
            {
                if (branch.SuspendedFrames == null || branch.SuspendedFrames.Count == 0)
                {
                    return Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                        "A running parallel branch has no suspended execution frames.",
                        frame.NodeIndex);
                }

                var branchStartDepth = _frames.Count;
                _frames.AddRange(branch.SuspendedFrames);
                branch.SuspendedFrames = null;
                if (!PrepareExpiredTimeoutForNewUpdate(branchStartDepth))
                {
                    PrepareReactiveResetForNewUpdate(branchStartDepth);
                }
            }

            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private ReferenceExecutionEnvelope AcceptParallelChild(ReferenceFrame frame, CompiledNodeRecord node)
        {
            if (frame.ActiveParallelBranchOrdinal < 0
                || frame.ActiveParallelBranchOrdinal >= frame.ParallelBranches.Length)
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                    "A parallel child result has no matching active branch.",
                    frame.NodeIndex);
            }

            var ordinal = frame.ActiveParallelBranchOrdinal;
            var expectedChild = _program.ChildIndices[checked((int)(node.Children.Offset + (uint)ordinal))];
            if (!frame.PendingChildNodeIndex.IsValid || frame.PendingChildNodeIndex.Value != expectedChild)
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                    "The pending child result does not match the selected parallel branch.",
                    frame.NodeIndex);
            }

            var branch = frame.ParallelBranches[ordinal];
            branch.State = frame.PendingChildResult == NodeStatus.Success
                ? ReferenceParallelChildState.Success
                : ReferenceParallelChildState.Failure;
            branch.SuspendedFrames = null;
            frame.ActiveParallelBranchOrdinal = -1;
            frame.HasPendingChildResult = false;
            frame.PendingChildNodeIndex = RuntimeNodeIndex.Invalid;
            return frame.ParallelVisitCursor == node.Children.Count
                ? CompleteParallelVisit(frame)
                : Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private ReferenceExecutionEnvelope CompleteParallelVisit(ReferenceFrame frame)
        {
            var decision = ReferenceParallelPolicyEvaluator.Evaluate(
                frame.ParallelConfiguration,
                frame.ParallelBranches);
            frame.ParallelVisitCursor = 0;
            if (!decision.IsTerminal)
            {
                if (TrySuspendActiveParallelBranch())
                    return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
                if (_observerQueue.Count != 0)
                {
                    _waitingPending = true;
                    return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
                }
                FinishUpdate();
                return Envelope(ReferenceExecutionProgress.Waiting, null, 1);
            }

            frame.PendingTerminalStatus = decision.Status;
            if (HasRunningParallelBranch(frame))
            {
                frame.ParallelAbortPending = true;
                return AdvanceParallelAbort(frame);
            }

            return SetParallelTerminal(frame);
        }

        private ReferenceExecutionEnvelope SetParallelTerminal(ReferenceFrame frame)
        {
            frame.ParallelAbortPending = false;
            frame.State = ReferenceFrameState.TerminalPendingExit;
            Emit(ReferenceTraceEventKind.NodeTicked, frame.NodeIndex, status: frame.PendingTerminalStatus);
            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private ReferenceExecutionEnvelope AdvanceParallelAbort(ReferenceFrame frame)
        {
            var ownerDepth = _frames.IndexOf(frame);
            if (ownerDepth < 0)
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                    "The parallel abort owner is not on the execution stack.",
                    frame.NodeIndex);
            }

            for (var ordinal = frame.ParallelBranches.Length - 1; ordinal >= 0; ordinal--)
            {
                var branch = frame.ParallelBranches[ordinal];
                if (branch.State != ReferenceParallelChildState.Running) continue;
                if (branch.SuspendedFrames == null || branch.SuspendedFrames.Count == 0)
                {
                    return Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                        "A running parallel branch has no frames to abort.",
                        frame.NodeIndex);
                }

                if (!frame.ParallelBranchAbortActive)
                {
                    frame.ParallelBranchAbortActive = true;
                    frame.ParallelAbortResumeTargetDepth = _abortTargetDepth;
                    frame.ParallelAbortResumeReason = _abortReason;
                    frame.ParallelAbortResumeSource = _abortSourceNodeIndex;
                    frame.ParallelAbortResumeReactiveReset = _abortForReactiveReset;
                    frame.ParallelAbortResumeTimeout = _abortForTimeout;
                }

                frame.ActiveParallelBranchOrdinal = ordinal;
                _frames.AddRange(branch.SuspendedFrames);
                branch.SuspendedFrames = null;
                var reason = frame.State == ReferenceFrameState.Aborting
                    ? frame.AbortReason
                    : NodeAbortReason.Explicit;
                var source = frame.State == ReferenceFrameState.Aborting
                    ? frame.SourceNodeIndex
                    : frame.NodeIndex;
                BeginAbortTraversal(ownerDepth + 1, reason, source);
                return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
            }

            RestoreParallelAbortTraversal(frame);
            frame.ParallelAbortPending = false;
            return frame.State == ReferenceFrameState.Aborting
                ? (frame.AbortCallbackCompleted ? ExitCompositeAborted(frame, _program.Nodes[(int)frame.NodeIndex.Value]) : AbortComposite(frame))
                : SetParallelTerminal(frame);
        }

        private bool TrySuspendActiveParallelBranch()
        {
            for (var ownerDepth = _frames.Count - 2; ownerDepth >= 0; ownerDepth--)
            {
                var owner = _frames[ownerDepth];
                var node = _program.Nodes[(int)owner.NodeIndex.Value];
                if (!_parallelRegistry.Contains(node.NodeTypeId, node.NodeTypeVersion)
                    || owner.ActiveParallelBranchOrdinal < 0)
                    continue;

                var branch = owner.ParallelBranches[owner.ActiveParallelBranchOrdinal];
                branch.State = ReferenceParallelChildState.Running;
                branch.SuspendedFrames = _frames.GetRange(ownerDepth + 1, _frames.Count - ownerDepth - 1);
                _frames.RemoveRange(ownerDepth + 1, _frames.Count - ownerDepth - 1);
                owner.ActiveParallelBranchOrdinal = -1;
                return true;
            }

            return false;
        }

        private bool TryCompleteParallelBranchAbortBoundary()
        {
            if (_abortTargetDepth <= 0 || _frames.Count != _abortTargetDepth) return false;
            var owner = _frames[_abortTargetDepth - 1];
            if (!owner.ParallelBranchAbortActive || owner.ActiveParallelBranchOrdinal < 0) return false;

            var branch = owner.ParallelBranches[owner.ActiveParallelBranchOrdinal];
            branch.State = ReferenceParallelChildState.NotStarted;
            branch.SuspendedFrames = null;
            owner.ActiveParallelBranchOrdinal = -1;
            ClearAbortTraversal();
            return true;
        }

        private void RestoreParallelAbortTraversal(ReferenceFrame frame)
        {
            if (!frame.ParallelBranchAbortActive) return;
            _abortTargetDepth = frame.ParallelAbortResumeTargetDepth;
            _abortReason = frame.ParallelAbortResumeReason;
            _abortSourceNodeIndex = frame.ParallelAbortResumeSource;
            _abortForReactiveReset = frame.ParallelAbortResumeReactiveReset;
            _abortForTimeout = frame.ParallelAbortResumeTimeout;
            frame.ParallelBranchAbortActive = false;
            frame.ParallelAbortResumeTargetDepth = -1;
        }

        private static bool HasRunningParallelBranch(ReferenceFrame frame)
        {
            if (frame.ParallelBranches == null) return false;
            for (var index = 0; index < frame.ParallelBranches.Length; index++)
                if (frame.ParallelBranches[index].State == ReferenceParallelChildState.Running) return true;
            return false;
        }

        private static void RetireActiveParallelBranchAfterTraversal(ReferenceFrame frame)
        {
            if (frame.ActiveParallelBranchOrdinal < 0) return;
            var branch = frame.ParallelBranches[frame.ActiveParallelBranchOrdinal];
            if (branch.SuspendedFrames != null) return;
            branch.State = ReferenceParallelChildState.NotStarted;
            frame.ActiveParallelBranchOrdinal = -1;
        }

        private ReferenceExecutionEnvelope SelectDecoratorChild(ReferenceFrame frame, CompiledNodeRecord node)
        {
            if (frame.DecoratorKind == ReferenceDecoratorKind.Timeout
                && _currentUpdate.TimeMicroseconds >= frame.DeadlineMicroseconds)
            {
                return SetDecoratorTerminal(frame, frame.ConfiguredResult);
            }

            if (frame.DecoratorKind == ReferenceDecoratorKind.Cooldown)
            {
                var nextAllowed = ReadInt64(node);
                if (_cooldownInitialized[frame.NodeIndex.Value] && _currentUpdate.TimeMicroseconds < nextAllowed)
                {
                    return SetDecoratorTerminal(frame, frame.ConfiguredResult);
                }

                if (frame.CooldownStartPolicy == ReferenceCooldownStartPolicy.OnEnter)
                {
                    frame.CooldownDeadlinePending = true;
                }
            }

            var childIndex = _program.ChildIndices[checked((int)node.Children.Offset)];
            frame.ChildSelected = true;
            frame.State = ReferenceFrameState.Running;
            _frames.Add(new ReferenceFrame(new RuntimeNodeIndex(childIndex)));
            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private ReferenceExecutionEnvelope AcceptDecoratorChild(ReferenceFrame frame, CompiledNodeRecord node)
        {
            var childResult = frame.PendingChildResult;
            frame.HasPendingChildResult = false;
            frame.PendingChildNodeIndex = RuntimeNodeIndex.Invalid;
            frame.ChildSelected = false;

            switch (frame.DecoratorKind)
            {
                case ReferenceDecoratorKind.Inverter:
                    return SetDecoratorTerminal(frame, childResult == NodeStatus.Success ? NodeStatus.Failure : NodeStatus.Success);
                case ReferenceDecoratorKind.Succeeder:
                    return SetDecoratorTerminal(frame, NodeStatus.Success);
                case ReferenceDecoratorKind.Failer:
                    return SetDecoratorTerminal(frame, NodeStatus.Failure);
                case ReferenceDecoratorKind.Repeater:
                    frame.RepeaterCompleted = checked(frame.RepeaterCompleted + 1);
                    WriteUInt32(node, frame.RepeaterCompleted);
                    if (childResult == NodeStatus.Failure && frame.RepeaterStopOnFailure)
                    {
                        return SetDecoratorTerminal(frame, NodeStatus.Failure);
                    }

                    return frame.RepeaterCompleted >= frame.RepeaterCount
                        ? SetDecoratorTerminal(frame, NodeStatus.Success)
                        : Envelope(ReferenceExecutionProgress.Suspended, null, 1);
                case ReferenceDecoratorKind.Timeout:
                    return SetDecoratorTerminal(frame, childResult);
                case ReferenceDecoratorKind.Cooldown:
                    if (childResult == NodeStatus.Success
                        && frame.CooldownStartPolicy == ReferenceCooldownStartPolicy.OnSuccessfulExit)
                    {
                        if (!TryAddDuration(_currentUpdate.TimeMicroseconds, frame.DurationMicroseconds, out var deadline))
                        {
                            return Fault(ReferenceExecutionDiagnosticCodes.TimeOverflow, "The cooldown deadline overflowed Int64 time.", frame.NodeIndex);
                        }

                        WriteInt64(node, deadline);
                        _cooldownInitialized[frame.NodeIndex.Value] = true;
                    }

                    return SetDecoratorTerminal(frame, childResult);
                default:
                    return Fault(ReferenceExecutionDiagnosticCodes.InvalidNodeConfiguration, "Unknown decorator kind.", frame.NodeIndex);
            }
        }

        private ReferenceExecutionEnvelope SetDecoratorTerminal(ReferenceFrame frame, NodeStatus status)
        {
            if (status != NodeStatus.Success && status != NodeStatus.Failure)
            {
                return Fault(ReferenceExecutionDiagnosticCodes.InvalidNodeConfiguration, "A decorator terminal result must be Success or Failure.", frame.NodeIndex);
            }

            frame.PendingTerminalStatus = status;
            frame.State = ReferenceFrameState.TerminalPendingExit;
            Emit(ReferenceTraceEventKind.NodeTicked, frame.NodeIndex, status: status);
            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private bool TryDecodeDecoratorConfiguration(
            ReferenceFrame frame,
            CompiledNodeRecord node,
            ReferenceDecoratorKind kind,
            out ReferenceExecutionEnvelope failure)
        {
            try
            {
                var bytes = GetConfiguration(node);
                frame.DecoratorKind = kind;
                switch (kind)
                {
                    case ReferenceDecoratorKind.Inverter:
                    case ReferenceDecoratorKind.Succeeder:
                    case ReferenceDecoratorKind.Failer:
                        if (bytes.Length != 0) throw new ArgumentException("A simple decorator requires empty configuration.");
                        break;
                    case ReferenceDecoratorKind.Repeater:
                        var repeater = ReferenceDecoratorConfigurationDecoder.DecodeRepeater(bytes);
                        frame.RepeaterCount = repeater.Count;
                        frame.RepeaterStopOnFailure = repeater.StopOnFailure;
                        break;
                    case ReferenceDecoratorKind.Timeout:
                        var timeout = ReferenceDecoratorConfigurationDecoder.DecodeTimeout(bytes);
                        frame.DurationMicroseconds = timeout.DurationMicroseconds;
                        frame.ConfiguredResult = timeout.TerminalResult;
                        break;
                    case ReferenceDecoratorKind.Cooldown:
                        var cooldown = ReferenceDecoratorConfigurationDecoder.DecodeCooldown(bytes);
                        frame.DurationMicroseconds = cooldown.DurationMicroseconds;
                        frame.ConfiguredResult = cooldown.BlockedResult;
                        frame.CooldownStartPolicy = cooldown.StartPolicy;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind));
                }

                frame.ConfigurationDecoded = true;
                failure = default;
                return true;
            }
            catch (Exception exception)
            {
                failure = Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidNodeConfiguration,
                    "Invalid decorator configuration: " + exception.GetType().Name + ".",
                    frame.NodeIndex);
                return false;
            }
        }

        private ReferenceExecutionEnvelope AdvanceReactiveComposite(
            ReferenceFrame frame,
            CompiledNodeRecord node,
            IReferenceReactiveCompositeHandler handler)
        {
            if (frame.ReactiveResetPending)
            {
                if (_frames[_frames.Count - 1] != frame)
                {
                    return Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                        "A reactive reset cannot run while its previous subtree is still active.",
                        frame.NodeIndex);
                }

                WriteCompositeCursor(node, 0);
                frame.HasPendingChildResult = false;
                frame.PendingChildNodeIndex = RuntimeNodeIndex.Invalid;
                frame.ReactiveResetPending = false;
                frame.LastReactiveResetUpdateId = _currentUpdate.UpdateId;
                frame.State = ReferenceFrameState.Running;
                _reactiveResetOwnerDepth = -1;
                ClearAbortTraversal();
                return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
            }

            return AdvanceMemoryComposite(frame, node, handler);
        }

        private void PrepareReactiveResetForNewUpdate(int startDepth = 0)
        {
            _reactiveResetOwnerDepth = -1;
            for (var depth = startDepth; depth < _frames.Count; depth++)
            {
                var candidate = _frames[depth];
                var node = _program.Nodes[(int)candidate.NodeIndex.Value];
                if (_reactiveCompositeRegistry.TryGet(node.NodeTypeId, node.NodeTypeVersion, out _)
                    && candidate.LastReactiveResetUpdateId != _currentUpdate.UpdateId)
                {
                    _reactiveResetOwnerDepth = depth;
                    candidate.ReactiveResetPending = true;
                    candidate.HasPendingChildResult = false;
                    candidate.PendingChildNodeIndex = RuntimeNodeIndex.Invalid;
                    break;
                }
            }

            if (_reactiveResetOwnerDepth < 0 || _frames.Count <= _reactiveResetOwnerDepth + 1)
            {
                return;
            }

            BeginAbortTraversal(
                _reactiveResetOwnerDepth + 1,
                NodeAbortReason.Explicit,
                _frames[_reactiveResetOwnerDepth].NodeIndex,
                forReactiveReset: true);
        }

        private ReferenceExecutionEnvelope EnterComposite(ReferenceFrame frame, CompiledNodeRecord node)
        {
            if (!TryBeginActivation(frame, node, out var failed)) return failed;
            frame.State = ReferenceFrameState.Entered;
            Emit(ReferenceTraceEventKind.NodeEntered, frame.NodeIndex);
            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private ReferenceExecutionEnvelope SelectCompositeChild(
            ReferenceFrame frame,
            CompiledNodeRecord node,
            IReferenceMemoryCompositeHandler handler)
        {
            var cursor = ReadCompositeCursor(node);
            if (cursor > node.Children.Count)
            {
                return InvalidCompositeCursor(frame.NodeIndex);
            }

            if (node.Children.Count == 0)
            {
                var emptyResult = handler.EmptyResult;
                if (emptyResult != NodeStatus.Success && emptyResult != NodeStatus.Failure)
                {
                    return Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                        "A memory composite must return Success or Failure for an empty child list.",
                        frame.NodeIndex);
                }

                frame.PendingTerminalStatus = emptyResult;
                frame.State = ReferenceFrameState.TerminalPendingExit;
                Emit(ReferenceTraceEventKind.NodeTicked, frame.NodeIndex, status: frame.PendingTerminalStatus);
                return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
            }

            if (cursor >= node.Children.Count)
            {
                return InvalidCompositeCursor(frame.NodeIndex);
            }

            var childTableIndex = checked((int)(node.Children.Offset + cursor));
            var childNodeIndex = new RuntimeNodeIndex(_program.ChildIndices[childTableIndex]);
            frame.State = ReferenceFrameState.Running;
            _frames.Add(new ReferenceFrame(childNodeIndex));
            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private ReferenceExecutionEnvelope AcceptCompositeChild(
            ReferenceFrame frame,
            CompiledNodeRecord node,
            IReferenceMemoryCompositeHandler handler)
        {
            var cursor = ReadCompositeCursor(node);
            if (cursor >= node.Children.Count)
            {
                return InvalidCompositeCursor(frame.NodeIndex);
            }

            var expectedChild = _program.ChildIndices[checked((int)(node.Children.Offset + cursor))];
            if (!frame.PendingChildNodeIndex.IsValid || frame.PendingChildNodeIndex.Value != expectedChild)
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                    "The pending child result does not match the retained memory-composite cursor.",
                    frame.NodeIndex);
            }

            ReferenceCompositeDecision decision;
            try
            {
                decision = handler.Accept(frame.PendingChildResult, cursor, node.Children.Count);
            }
            catch (Exception exception)
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                    "The memory-composite handler rejected a child result: " + exception.GetType().Name + ".",
                    frame.NodeIndex);
            }

            var isDefinedTerminal = decision.Status == NodeStatus.Success || decision.Status == NodeStatus.Failure;
            var expectedNextCursor = checked(cursor + 1);
            var invalidTerminal = decision.IsTerminal
                && (!isDefinedTerminal || decision.CursorAfterAcceptance > node.Children.Count);
            var invalidContinue = !decision.IsTerminal
                && (decision.CursorAfterAcceptance != expectedNextCursor
                    || decision.CursorAfterAcceptance >= node.Children.Count);
            if (invalidTerminal || invalidContinue)
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                    "The memory-composite handler returned an invalid acceptance decision.",
                    frame.NodeIndex);
            }

            WriteCompositeCursor(node, decision.CursorAfterAcceptance);
            frame.HasPendingChildResult = false;
            frame.PendingChildNodeIndex = RuntimeNodeIndex.Invalid;
            if (!decision.IsTerminal)
            {
                frame.State = ReferenceFrameState.Running;
                return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
            }

            frame.PendingTerminalStatus = decision.Status;
            frame.State = ReferenceFrameState.TerminalPendingExit;
            Emit(ReferenceTraceEventKind.NodeTicked, frame.NodeIndex, status: decision.Status);
            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private ReferenceExecutionEnvelope AbortComposite(ReferenceFrame frame)
        {
            frame.AbortCallbackCompleted = true;
            Emit(
                ReferenceTraceEventKind.NodeAbortStarted,
                frame.NodeIndex,
                abortReason: frame.AbortReason,
                sourceNodeIndex: frame.SourceNodeIndex);
            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private ReferenceExecutionEnvelope ExitCompositeTerminal(ReferenceFrame frame, CompiledNodeRecord node)
        {
            var reason = frame.PendingTerminalStatus == NodeStatus.Success
                ? NodeExitReason.Success
                : NodeExitReason.Failure;
            return CompleteTerminalExit(frame, node, reason);
        }

        private ReferenceExecutionEnvelope ExitCompositeAborted(ReferenceFrame frame, CompiledNodeRecord node)
        {
            return CompleteAbortedExit(frame, node);
        }

        private ReferenceExecutionEnvelope Enter(ReferenceFrame frame, CompiledNodeRecord node, IReferenceLeafHandler handler)
        {
            if (handler is ReferenceAsyncActionHandler && !IsValidAsyncActionStorage(node))
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.UnsupportedNode,
                    "The reference async action requires exactly 16 bytes of aligned Activation memory.",
                    frame.NodeIndex);
            }

            if (!TryBeginActivation(frame, node, out var failed)) return failed;
            try
            {
                var context = CreateNodeContext(frame, node);
                handler.Enter(ref context);
            }
            catch (Exception exception)
            {
                return HandlerFault(frame.NodeIndex, "Enter", exception);
            }
            if (_pendingNodeServiceFault != null) return BlackboardFault();

            frame.State = ReferenceFrameState.Entered;
            Emit(ReferenceTraceEventKind.NodeEntered, frame.NodeIndex);
            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private ReferenceExecutionEnvelope Tick(ReferenceFrame frame, CompiledNodeRecord node, IReferenceLeafHandler handler)
        {
            if (frame.LastTickUpdateId == _currentUpdate.UpdateId)
            {
                if (TrySuspendActiveParallelBranch())
                    return Envelope(ReferenceExecutionProgress.Suspended, null, 0);
                if (_observerQueue.Count != 0)
                {
                    _waitingPending = true;
                    return Envelope(ReferenceExecutionProgress.Suspended, null, 0);
                }
                FinishUpdate();
                return Envelope(ReferenceExecutionProgress.Waiting, null, 0);
            }

            NodeStatus status;
            try
            {
                var context = CreateNodeContext(frame, node);
                status = handler.Tick(ref context);
                if (!Enum.IsDefined(typeof(NodeStatus), status))
                {
                    throw new InvalidOperationException("A reference leaf returned an undefined node status.");
                }
            }
            catch (Exception exception)
            {
                return HandlerFault(frame.NodeIndex, "Tick", exception);
            }

            if (_pendingNodeServiceFault != null) return BlackboardFault();

            frame.LastTickUpdateId = _currentUpdate.UpdateId;
            Emit(ReferenceTraceEventKind.NodeTicked, frame.NodeIndex, status: status);
            if (status == NodeStatus.Running)
            {
                frame.State = ReferenceFrameState.Running;
                if (_observerQueue.Count != 0 && HasActiveParallelOwner())
                {
                    _parallelSuspendPending = true;
                    return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
                }
                if (TrySuspendActiveParallelBranch())
                    return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
                if (_observerQueue.Count != 0)
                {
                    _waitingPending = true;
                    return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
                }
                FinishUpdate();
                return Envelope(ReferenceExecutionProgress.Waiting, null, 1);
            }

            frame.PendingTerminalStatus = status;
            frame.State = ReferenceFrameState.TerminalPendingExit;
            SeedObserverResult(frame.NodeIndex, status);
            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private ReferenceExecutionEnvelope AbortFrame(ReferenceFrame frame, CompiledNodeRecord node, IReferenceLeafHandler handler)
        {
            try
            {
                var context = CreateNodeContext(frame, node);
                handler.Abort(ref context, frame.AbortReason);
            }
            catch (Exception exception)
            {
                return HandlerFault(frame.NodeIndex, "Abort", exception);
            }
            if (_pendingNodeServiceFault != null) return BlackboardFault();

            frame.AbortCallbackCompleted = true;
            Emit(
                ReferenceTraceEventKind.NodeAbortStarted,
                frame.NodeIndex,
                abortReason: frame.AbortReason,
                sourceNodeIndex: frame.SourceNodeIndex);
            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private ReferenceExecutionEnvelope ExitTerminal(ReferenceFrame frame, CompiledNodeRecord node, IReferenceLeafHandler handler)
        {
            var reason = frame.PendingTerminalStatus == NodeStatus.Success
                ? NodeExitReason.Success
                : NodeExitReason.Failure;
            try
            {
                var context = CreateNodeContext(frame, node);
                handler.Exit(ref context, reason);
            }
            catch (Exception exception)
            {
                return HandlerFault(frame.NodeIndex, "Exit", exception);
            }
            if (_pendingNodeServiceFault != null) return BlackboardFault();

            return CompleteTerminalExit(frame, node, reason);
        }

        private ReferenceExecutionEnvelope ExitAborted(ReferenceFrame frame, CompiledNodeRecord node, IReferenceLeafHandler handler)
        {
            try
            {
                var context = CreateNodeContext(frame, node);
                handler.Exit(ref context, NodeExitReason.Aborted);
            }
            catch (Exception exception)
            {
                return HandlerFault(frame.NodeIndex, "Exit", exception);
            }
            if (_pendingNodeServiceFault != null) return BlackboardFault();

            return CompleteAbortedExit(frame, node);
        }

        private bool TryBeginActivation(
            ReferenceFrame frame,
            CompiledNodeRecord node,
            out ReferenceExecutionEnvelope failed)
        {
            if (_activationGenerations[frame.NodeIndex.Value] == uint.MaxValue)
            {
                failed = Fault(
                    ReferenceExecutionDiagnosticCodes.ActivationGenerationOverflow,
                    "The node activation generation cannot be incremented without overflow.",
                    frame.NodeIndex);
                return false;
            }

            if (!PrepareCooldownBeforeChildEnter(frame, out failed)) return false;
            if (node.MemoryLifetime == NodeMemoryLifetime.Activation) ClearMemory(node);
            frame.ActivationGeneration = ++_activationGenerations[frame.NodeIndex.Value];
            failed = default;
            return true;
        }

        private bool PrepareCooldownBeforeChildEnter(
            ReferenceFrame childFrame,
            out ReferenceExecutionEnvelope failed)
        {
            failed = default;
            if (_frames.Count < 2 || _frames[_frames.Count - 1] != childFrame) return true;
            var parent = _frames[_frames.Count - 2];
            if (!parent.CooldownDeadlinePending) return true;

            var parentNode = _program.Nodes[(int)parent.NodeIndex.Value];
            if (!_decoratorRegistry.TryGet(parentNode.NodeTypeId, parentNode.NodeTypeVersion, out var kind)
                || kind != ReferenceDecoratorKind.Cooldown)
            {
                failed = Fault(
                    ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                    "A pending cooldown deadline does not belong to a cooldown parent.",
                    parent.NodeIndex);
                return false;
            }

            if (!TryAddDuration(_currentUpdate.TimeMicroseconds, parent.DurationMicroseconds, out var deadline))
            {
                failed = Fault(
                    ReferenceExecutionDiagnosticCodes.TimeOverflow,
                    "The cooldown deadline overflowed Int64 time.",
                    parent.NodeIndex);
                return false;
            }

            WriteInt64(parentNode, deadline);
            _cooldownInitialized[parent.NodeIndex.Value] = true;
            parent.CooldownDeadlinePending = false;
            return true;
        }

        private ReferenceExecutionEnvelope CompleteTerminalExit(
            ReferenceFrame frame,
            CompiledNodeRecord node,
            NodeExitReason reason)
        {
            if (node.MemoryLifetime == NodeMemoryLifetime.Activation) ClearMemory(node);
            _frames.RemoveAt(_frames.Count - 1);
            Emit(ReferenceTraceEventKind.NodeExited, frame.NodeIndex, exitReason: reason);

            if (_abortTargetDepth >= 0)
            {
                if (TryCompleteParallelBranchAbortBoundary())
                    return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
                if (_abortForReactiveReset && _frames.Count == _abortTargetDepth)
                {
                    ClearAbortTraversal();
                    return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
                }

                if (_frames.Count <= _abortTargetDepth)
                {
                    ClearAbortTraversal();
                }
                else
                {
                    PrepareTopFrameForAbort();
                    return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
                }
            }

            if (_frames.Count == 0)
            {
                _terminalResult = frame.PendingTerminalStatus;
                if (_observerQueue.Count != 0)
                {
                    _terminalCompletionPending = true;
                    return Envelope(ReferenceExecutionProgress.Suspended, _terminalResult, 1);
                }
                FinishUpdate();
                return Envelope(ReferenceExecutionProgress.Completed, _terminalResult, 1);
            }

            var parent = _frames[_frames.Count - 1];
            parent.HasPendingChildResult = true;
            parent.PendingChildResult = frame.PendingTerminalStatus;
            parent.PendingChildNodeIndex = frame.NodeIndex;
            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private ReferenceExecutionEnvelope CompleteAbortedExit(ReferenceFrame frame, CompiledNodeRecord node)
        {
            if (node.MemoryLifetime == NodeMemoryLifetime.Activation) ClearMemory(node);
            _frames.RemoveAt(_frames.Count - 1);
            Emit(ReferenceTraceEventKind.NodeExited, frame.NodeIndex, exitReason: NodeExitReason.Aborted);

            if (_abortTargetDepth >= 0 && _frames.Count > _abortTargetDepth)
            {
                PrepareTopFrameForAbort();
                return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
            }

            if (TryCompleteParallelBranchAbortBoundary())
                return Envelope(ReferenceExecutionProgress.Suspended, null, 1);

            if (_abortForReactiveReset && _frames.Count == _abortTargetDepth)
            {
                ClearAbortTraversal();
                return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
            }

            if (_abortForTimeout && _frames.Count == _abortTargetDepth)
            {
                ClearAbortTraversal();
                return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
            }

            ClearAbortTraversal();
            _stopped = _frames.Count == 0;
            if (_stopped) ClearObserverQueue();
            FinishUpdate();
            return Envelope(ReferenceExecutionProgress.Completed, null, 1);
        }

        private void BeginAbortTraversal(
            int targetDepth,
            NodeAbortReason reason,
            RuntimeNodeIndex sourceNodeIndex,
            bool forReactiveReset = false,
            bool forTimeout = false)
        {
            _abortTargetDepth = targetDepth;
            _abortReason = reason;
            _abortSourceNodeIndex = sourceNodeIndex;
            _abortForReactiveReset = forReactiveReset;
            _abortForTimeout = forTimeout;
            PrepareTopFrameForAbort();
        }

        private void PrepareTopFrameForAbort()
        {
            while (_frames.Count > _abortTargetDepth)
            {
                var frame = _frames[_frames.Count - 1];
                if (frame.State == ReferenceFrameState.TerminalPendingExit)
                {
                    return;
                }

                if (frame.State == ReferenceFrameState.Inactive)
                {
                    _frames.RemoveAt(_frames.Count - 1);
                    continue;
                }

                frame.AbortReason = _abortReason;
                frame.SourceNodeIndex = _abortSourceNodeIndex;
                frame.AbortCallbackCompleted = false;
                frame.HasPendingChildResult = false;
                frame.PendingChildNodeIndex = RuntimeNodeIndex.Invalid;
                frame.State = ReferenceFrameState.Aborting;
                return;
            }

            _stopped = _frames.Count == 0;
        }

        private void ClearAbortTraversal()
        {
            _abortTargetDepth = -1;
            _abortSourceNodeIndex = RuntimeNodeIndex.Invalid;
            _abortForReactiveReset = false;
            _abortForTimeout = false;
        }

        private bool TryValidateObserverTopology(out ReferenceExecutionEnvelope failure)
        {
            for (var observerIndex = 0; observerIndex < _program.Observers.Count; observerIndex++)
            {
                var observer = _program.Observers[observerIndex];
                var observerNode = new RuntimeNodeIndex(observer.ObserverNodeIndex);
                var ownerNode = _program.Nodes[(int)observer.OwningReactiveCompositeIndex];
                if (!_reactiveCompositeRegistry.TryGetKind(
                    ownerNode.NodeTypeId,
                    ownerNode.NodeTypeVersion,
                    out var ownerKind))
                {
                    failure = Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                        "An observer owner must have an explicit reactive-composite binding.",
                        observerNode);
                    return false;
                }

                var directChild = false;
                for (uint childOrdinal = 0; childOrdinal < ownerNode.Children.Count; childOrdinal++)
                {
                    if (_program.ChildIndices[(int)(ownerNode.Children.Offset + childOrdinal)] == observer.ObserverNodeIndex)
                    {
                        directChild = true;
                        break;
                    }
                }
                if (!directChild)
                {
                    failure = Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                        "An observer node must be a direct child of its owning reactive composite.",
                        observerNode);
                    return false;
                }

                if ((observer.Mode == CompiledObserverMode.LowerPriority || observer.Mode == CompiledObserverMode.Both)
                    && ownerKind != ReferenceReactiveCompositeKind.Selector)
                {
                    failure = Fault(
                        ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                        "Lower-priority observer modes require a reactive selector owner.",
                        observerNode);
                    return false;
                }
            }

            failure = default;
            return true;
        }

        private void RecordBlackboardWrite(RuntimeNodeIndex writerNodeIndex, BlackboardStorageResult result)
        {
            if (!result.Success)
            {
                _pendingNodeServiceFault = result.Diagnostic;
                return;
            }

            if (!result.Changed) return;
            Emit(
                ReferenceTraceEventKind.BlackboardChanged,
                writerNodeIndex,
                stableBlackboardKeyId: result.StableKeyId,
                oldBlackboardVersion: result.OldVersion,
                newBlackboardVersion: result.NewVersion);
            QueueObserversForSlot(result.SlotIndex);
        }

        private void PublishPendingResetChanges()
        {
            for (var index = 0; index < _pendingResetChanges.Count; index++)
            {
                var change = _pendingResetChanges[index];
                Emit(
                    ReferenceTraceEventKind.BlackboardChanged,
                    stableBlackboardKeyId: change.StableKeyId,
                    oldBlackboardVersion: change.OldVersion,
                    newBlackboardVersion: change.NewVersion);
                QueueObserversForSlot(change.SlotIndex);
            }
            _pendingResetChanges.Clear();
        }

        private void QueueObserversForSlot(uint slotIndex)
        {
            var newlyQueued = new List<int>();
            for (var observerIndex = 0; observerIndex < _program.Observers.Count; observerIndex++)
            {
                var observer = _program.Observers[observerIndex];
                var watchesSlot = false;
                var end = checked((int)(observer.WatchedSlots.Offset + observer.WatchedSlots.Count));
                for (var watch = (int)observer.WatchedSlots.Offset; watch < end; watch++)
                {
                    if (_program.WatchedSlotIndices[watch] == slotIndex)
                    {
                        watchesSlot = true;
                        break;
                    }
                }

                if (!watchesSlot || _observerStates[observerIndex].IsQueued) continue;
                _observerStates[observerIndex].IsQueued = true;
                var insert = 0;
                while (insert < _observerQueue.Count
                    && _program.Observers[_observerQueue[insert]].ObserverNodeIndex < observer.ObserverNodeIndex)
                    insert++;
                _observerQueue.Insert(insert, observerIndex);
                newlyQueued.Add(observerIndex);
            }
            newlyQueued.Sort((left, right) => _program.Observers[left].ObserverNodeIndex.CompareTo(
                _program.Observers[right].ObserverNodeIndex));
            for (var index = 0; index < newlyQueued.Count; index++)
                Emit(ReferenceTraceEventKind.ObserverQueued,
                    new RuntimeNodeIndex(_program.Observers[newlyQueued[index]].ObserverNodeIndex));
        }

        private ReferenceExecutionEnvelope EvaluateNextObserver()
        {
            var observerIndex = _observerQueue[0];
            _observerQueue.RemoveAt(0);
            var observer = _program.Observers[observerIndex];
            var state = _observerStates[observerIndex];
            state.IsQueued = false;
            var observerNodeIndex = new RuntimeNodeIndex(observer.ObserverNodeIndex);
            var node = _program.Nodes[(int)observer.ObserverNodeIndex];
            if (!_observerRegistry.TryGet(node.NodeTypeId, node.NodeTypeVersion, out var evaluator))
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.MissingHandler,
                    "No explicit observer condition evaluator is bound for the compiled node type and version.",
                    observerNodeIndex);
            }

            NodeStatus result;
            try
            {
                var context = new ReferenceObserverConditionContext(
                    _configuration,
                    checked((int)node.ConfigOffset),
                    checked((int)node.ConfigSize),
                    _currentUpdate,
                    _treeInstanceId,
                    observerNodeIndex,
                    _blackboard,
                    _observerReadServices);
                result = evaluator.Evaluate(ref context);
            }
            catch (Exception exception)
            {
                return HandlerFault(observerNodeIndex, "observer Evaluate", exception);
            }
            if (_pendingNodeServiceFault != null) return BlackboardFault();

            if (result != NodeStatus.Success && result != NodeStatus.Failure)
            {
                return Fault(
                    ReferenceExecutionDiagnosticCodes.HandlerFault,
                    "An observer condition evaluator must return Success or Failure.",
                    observerNodeIndex);
            }

            Emit(ReferenceTraceEventKind.ObserverEvaluated, observerNodeIndex, status: result);
            var previous = state.LastResult;
            var changed = state.HasLastResult && previous != result;
            state.HasLastResult = true;
            state.LastResult = result;
            if (changed) ApplyObserverTransition(observer, previous, result);
            return Envelope(ReferenceExecutionProgress.Suspended, null, 1);
        }

        private void ApplyObserverTransition(
            CompiledObserverRecord observer,
            NodeStatus previous,
            NodeStatus current)
        {
            if (_abortTargetDepth >= 0) return;
            var ownerNodeIndex = new RuntimeNodeIndex(observer.OwningReactiveCompositeIndex);
            var ownerDepth = FindActiveFrameDepth(ownerNodeIndex);
            RetainedObserverOwner retainedOwner = null;
            if (ownerDepth < 0 && !TryFindRetainedParallelObserverOwner(ownerNodeIndex, out retainedOwner)) return;
            var ownerFrames = retainedOwner == null ? _frames : retainedOwner.Frames;
            var ownerLocalDepth = retainedOwner == null ? ownerDepth : retainedOwner.OwnerDepth;
            if (ownerFrames.Count <= ownerLocalDepth + 1) return;
            var owner = ownerFrames[ownerLocalDepth];
            var ownerNode = _program.Nodes[(int)owner.NodeIndex.Value];
            if (!_reactiveCompositeRegistry.TryGet(ownerNode.NodeTypeId, ownerNode.NodeTypeVersion, out _)) return;

            var selfTriggered = (observer.Mode == CompiledObserverMode.Self || observer.Mode == CompiledObserverMode.Both)
                && previous == NodeStatus.Success
                && current == NodeStatus.Failure;
            var lowerTriggered = (observer.Mode == CompiledObserverMode.LowerPriority || observer.Mode == CompiledObserverMode.Both)
                && previous == NodeStatus.Failure
                && current == NodeStatus.Success
                && IsActiveLowerPriorityChild(ownerNode, observer.ObserverNodeIndex, ownerFrames[ownerLocalDepth + 1].NodeIndex.Value);
            if (!selfTriggered && !lowerTriggered) return;

            if (retainedOwner != null)
            {
                if (!RestoreRetainedParallelObserverOwner(retainedOwner, out ownerDepth)) return;
                owner = _frames[ownerDepth];
            }

            _waitingPending = false;
            _parallelSuspendPending = false;
            _terminalCompletionPending = false;
            _reactiveResetOwnerDepth = ownerDepth;
            owner.ReactiveResetPending = true;
            owner.HasPendingChildResult = false;
            owner.PendingChildNodeIndex = RuntimeNodeIndex.Invalid;
            BeginAbortTraversal(
                ownerDepth + 1,
                selfTriggered ? NodeAbortReason.ObserverSelf : NodeAbortReason.ObserverLowerPriority,
                new RuntimeNodeIndex(observer.ObserverNodeIndex),
                forReactiveReset: true);
        }

        private bool IsActiveLowerPriorityChild(
            CompiledNodeRecord owner,
            uint observerNodeIndex,
            uint activeChildNodeIndex)
        {
            var observerOrdinal = -1;
            var activeOrdinal = -1;
            for (uint ordinal = 0; ordinal < owner.Children.Count; ordinal++)
            {
                var child = _program.ChildIndices[checked((int)(owner.Children.Offset + ordinal))];
                if (child == observerNodeIndex) observerOrdinal = checked((int)ordinal);
                if (child == activeChildNodeIndex) activeOrdinal = checked((int)ordinal);
            }
            return observerOrdinal >= 0 && activeOrdinal > observerOrdinal;
        }

        private int FindActiveFrameDepth(RuntimeNodeIndex nodeIndex)
        {
            for (var depth = 0; depth < _frames.Count; depth++)
                if (_frames[depth].NodeIndex == nodeIndex) return depth;
            return -1;
        }

        private bool TryFindRetainedParallelObserverOwner(RuntimeNodeIndex ownerNodeIndex, out RetainedObserverOwner found)
        {
            for (var parallelDepth = _frames.Count - 1; parallelDepth >= 0; parallelDepth--)
            {
                var parallelFrame = _frames[parallelDepth];
                if (TryFindRetainedObserverOwner(parallelFrame, parallelDepth, null, ownerNodeIndex, out found)) return true;
            }
            found = null;
            return false;
        }

        private static bool TryFindRetainedObserverOwner(
            ReferenceFrame parallelFrame,
            int parallelDepth,
            RetainedObserverOwner parent,
            RuntimeNodeIndex ownerNodeIndex,
            out RetainedObserverOwner found)
        {
            if (parallelFrame.ParallelBranches != null)
            {
                for (var ordinal = 0; ordinal < parallelFrame.ParallelBranches.Length; ordinal++)
                {
                    var branch = parallelFrame.ParallelBranches[ordinal];
                    var retained = branch.SuspendedFrames;
                    if (branch.State != ReferenceParallelChildState.Running || retained == null) continue;
                    for (var index = 0; index < retained.Count; index++)
                    {
                        if (retained[index].NodeIndex == ownerNodeIndex)
                        {
                            found = new RetainedObserverOwner(parallelFrame, parallelDepth, ordinal, retained, index, parent);
                            return true;
                        }
                        if (retained[index].ParallelBranches != null)
                        {
                            var container = new RetainedObserverOwner(parallelFrame, parallelDepth, ordinal, retained, index, parent);
                            if (TryFindRetainedObserverOwner(retained[index], -1, container, ownerNodeIndex, out found)) return true;
                        }
                    }
                }
            }
            found = null;
            return false;
        }

        private bool RestoreRetainedParallelObserverOwner(RetainedObserverOwner retained, out int ownerDepth)
        {
            if (retained.Parent != null && !RestoreRetainedParallelObserverOwner(retained.Parent, out _))
            {
                ownerDepth = -1;
                return false;
            }
            var parallelDepth = retained.Parent == null
                ? retained.ParallelDepth
                : _frames.IndexOf(retained.ParallelFrame);
            if (parallelDepth < 0)
            {
                ownerDepth = -1;
                return false;
            }
            if (parallelDepth + 1 < _frames.Count)
            {
                var activeOrdinal = retained.ParallelFrame.ActiveParallelBranchOrdinal;
                if (activeOrdinal < 0)
                {
                    ownerDepth = -1;
                    return false;
                }
                var activeBranch = retained.ParallelFrame.ParallelBranches[activeOrdinal];
                activeBranch.State = ReferenceParallelChildState.Running;
                activeBranch.SuspendedFrames = _frames.GetRange(
                    parallelDepth + 1,
                    _frames.Count - parallelDepth - 1);
                _frames.RemoveRange(parallelDepth + 1, _frames.Count - parallelDepth - 1);
            }
            retained.ParallelFrame.ActiveParallelBranchOrdinal = retained.Ordinal;
            _frames.AddRange(retained.Frames);
            retained.ParallelFrame.ParallelBranches[retained.Ordinal].SuspendedFrames = null;
            ownerDepth = parallelDepth + 1 + retained.OwnerDepth;
            return true;
        }

        private sealed class RetainedObserverOwner
        {
            internal RetainedObserverOwner(ReferenceFrame parallelFrame, int parallelDepth, int ordinal, List<ReferenceFrame> frames, int ownerDepth, RetainedObserverOwner parent)
            { ParallelFrame = parallelFrame; ParallelDepth = parallelDepth; Ordinal = ordinal; Frames = frames; OwnerDepth = ownerDepth; Parent = parent; }
            internal ReferenceFrame ParallelFrame { get; }
            internal int ParallelDepth { get; }
            internal int Ordinal { get; }
            internal List<ReferenceFrame> Frames { get; }
            internal int OwnerDepth { get; }
            internal RetainedObserverOwner Parent { get; }
        }

        private bool HasActiveParallelOwner()
        {
            for (var depth = _frames.Count - 2; depth >= 0; depth--)
            {
                var frame = _frames[depth];
                var node = _program.Nodes[(int)frame.NodeIndex.Value];
                if (_parallelRegistry.Contains(node.NodeTypeId, node.NodeTypeVersion)
                    && frame.ActiveParallelBranchOrdinal >= 0)
                    return true;
            }
            return false;
        }

        private void SeedObserverResult(RuntimeNodeIndex nodeIndex, NodeStatus result)
        {
            for (var index = 0; index < _program.Observers.Count; index++)
            {
                if (_program.Observers[index].ObserverNodeIndex != nodeIndex.Value) continue;
                _observerStates[index].HasLastResult = true;
                _observerStates[index].LastResult = result;
                return;
            }
        }

        private void ClearObserverQueue()
        {
            _observerQueue.Clear();
            for (var index = 0; index < _observerStates.Length; index++)
                _observerStates[index].IsQueued = false;
        }

        private ReferenceExecutionEnvelope BlackboardFault()
        {
            var diagnostic = _pendingNodeServiceFault;
            _pendingNodeServiceFault = null;
            return Fault(diagnostic);
        }

        private ReferenceExecutionEnvelope HandlerFault(RuntimeNodeIndex nodeIndex, string callback, Exception exception)
        {
            return Fault(
                ReferenceExecutionDiagnosticCodes.HandlerFault,
                "Reference leaf " + callback + " callback threw " + exception.GetType().Name + ".",
                nodeIndex);
        }

        private ReferenceExecutionEnvelope InvalidCompositeCursor(RuntimeNodeIndex nodeIndex)
        {
            return Fault(
                ReferenceExecutionDiagnosticCodes.InvalidCompositeState,
                "The retained memory-composite child cursor is outside its child range.",
                nodeIndex);
        }

        private uint ReadCompositeCursor(CompiledNodeRecord node)
        {
            var offset = checked((int)node.InstanceMemoryOffset);
            return (uint)(_nodeMemory[offset]
                | (_nodeMemory[offset + 1] << 8)
                | (_nodeMemory[offset + 2] << 16)
                | (_nodeMemory[offset + 3] << 24));
        }

        private ReadOnlySpan<byte> GetConfiguration(CompiledNodeRecord node)
            => new ReadOnlySpan<byte>(_configuration, checked((int)node.ConfigOffset), checked((int)node.ConfigSize));

        private long ReadInt64(CompiledNodeRecord node)
        {
            var offset = checked((int)node.InstanceMemoryOffset);
            ulong value = 0;
            for (var index = 0; index < 8; index++) value |= (ulong)_nodeMemory[offset + index] << (index * 8);
            return unchecked((long)value);
        }

        private void WriteInt64(CompiledNodeRecord node, long value)
        {
            var offset = checked((int)node.InstanceMemoryOffset);
            var bits = unchecked((ulong)value);
            for (var index = 0; index < 8; index++) _nodeMemory[offset + index] = (byte)(bits >> (index * 8));
        }

        private static bool TryAddDuration(long timePoint, long duration, out long result)
        {
            if (duration <= 0 || timePoint > long.MaxValue - duration)
            {
                result = default;
                return false;
            }

            result = timePoint + duration;
            return true;
        }

        private void WriteCompositeCursor(CompiledNodeRecord node, uint value)
        {
            var offset = checked((int)node.InstanceMemoryOffset);
            _nodeMemory[offset] = (byte)value;
            _nodeMemory[offset + 1] = (byte)(value >> 8);
            _nodeMemory[offset + 2] = (byte)(value >> 16);
            _nodeMemory[offset + 3] = (byte)(value >> 24);
        }

        private void WriteUInt32(CompiledNodeRecord node, uint value)
        {
            var offset = checked((int)node.InstanceMemoryOffset);
            _nodeMemory[offset] = (byte)value;
            _nodeMemory[offset + 1] = (byte)(value >> 8);
            _nodeMemory[offset + 2] = (byte)(value >> 16);
            _nodeMemory[offset + 3] = (byte)(value >> 24);
        }

        private static bool IsValidDecoratorStorage(CompiledNodeRecord node, ReferenceDecoratorKind kind)
        {
            switch (kind)
            {
                case ReferenceDecoratorKind.Inverter:
                case ReferenceDecoratorKind.Succeeder:
                case ReferenceDecoratorKind.Failer:
                    return node.MemoryLifetime == NodeMemoryLifetime.Activation
                        && node.InstanceMemorySize == 0
                        && node.InstanceMemoryAlignment == 1
                        && node.ConfigSize == 0
                        && node.ConfigAlignment == 1;
                case ReferenceDecoratorKind.Repeater:
                    return node.MemoryLifetime == NodeMemoryLifetime.Activation
                        && node.InstanceMemorySize == 4
                        && node.InstanceMemoryAlignment == 4
                        && node.InstanceMemoryOffset % 4 == 0
                        && node.ConfigSize == 8
                        && node.ConfigAlignment == 4;
                case ReferenceDecoratorKind.Timeout:
                    return node.MemoryLifetime == NodeMemoryLifetime.Activation
                        && node.InstanceMemorySize == 8
                        && node.InstanceMemoryAlignment == 8
                        && node.InstanceMemoryOffset % 8 == 0
                        && node.ConfigSize == 16
                        && node.ConfigAlignment == 8;
                case ReferenceDecoratorKind.Cooldown:
                    return node.MemoryLifetime == NodeMemoryLifetime.Instance
                        && node.InstanceMemorySize == 8
                        && node.InstanceMemoryAlignment == 8
                        && node.InstanceMemoryOffset % 8 == 0
                        && node.ConfigSize == 16
                        && node.ConfigAlignment == 8;
                default:
                    return false;
            }
        }

        private static bool IsValidParallelStorage(CompiledNodeRecord node)
        {
            return node.MemoryLifetime == NodeMemoryLifetime.Activation
                && node.InstanceMemorySize == 8
                && node.InstanceMemoryAlignment == 4
                && node.InstanceMemoryOffset % 4 == 0
                && node.ConfigSize == 16
                && node.ConfigAlignment == 4;
        }

        private static bool IsValidMemoryCompositeStorage(CompiledNodeRecord node)
        {
            return node.MemoryLifetime == NodeMemoryLifetime.Activation
                && node.InstanceMemorySize == 4
                && node.InstanceMemoryAlignment == 4
                && node.InstanceMemoryOffset % 4 == 0;
        }

        private static bool IsValidAsyncActionStorage(CompiledNodeRecord node)
        {
            return node.MemoryLifetime == NodeMemoryLifetime.Activation
                && node.InstanceMemorySize == 16
                && node.InstanceMemoryAlignment == 8
                && node.InstanceMemoryOffset % 8 == 0;
        }

        private static uint CountActiveFrames(IReadOnlyList<ReferenceFrame> frames)
        {
            uint count = 0;
            for (var index = 0; index < frames.Count; index++)
            {
                var frame = frames[index];
                if (frame.State != ReferenceFrameState.Inactive) count = checked(count + 1);
                if (frame.ParallelBranches == null) continue;

                for (var branchIndex = 0; branchIndex < frame.ParallelBranches.Length; branchIndex++)
                {
                    var branch = frame.ParallelBranches[branchIndex];
                    if (branch == null) continue;
                    var suspended = branch.SuspendedFrames;
                    if (suspended != null) count = checked(count + CountActiveFrames(suspended));
                }
            }

            return count;
        }

        private ReferenceExecutionEnvelope Fault(DiagnosticCode code, string message, RuntimeNodeIndex nodeIndex)
        {
            var diagnostic = ReferenceExecutionDiagnostics.Create(code, message, _treeInstanceId, nodeIndex);
            return Fault(diagnostic, nodeIndex);
        }

        private ReferenceExecutionEnvelope Fault(Diagnostic diagnostic, RuntimeNodeIndex? traceNodeIndex = null)
        {
            _asyncCoordinator.FaultCancelAll((operationId, emitted, cancellationDiagnostic) =>
            {
                if (cancellationDiagnostic != null)
                {
                    AddAsyncDiagnostics(new DiagnosticCollection(new[] { cancellationDiagnostic }), false);
                }

                if (emitted) Emit(ReferenceTraceEventKind.CommandEmitted, operationId.NodeIndex);
                AddAsyncDiagnostics(_completionInbox.DiscardCancelled(operationId), true);
            });
            ClearActivationMemory();
            _frames.Clear();
            _terminalResult = null;
            _faulted = true;
            ClearObserverQueue();
            _waitingPending = false;
            _parallelSuspendPending = false;
            _terminalCompletionPending = false;
            _budgetSuspended = false;
            ClearAbortTraversal();
            Emit(ReferenceTraceEventKind.DiagnosticRaised, traceNodeIndex, diagnosticCode: diagnostic.Code);
            if (_hasOpenUpdate) FinishUpdate();
            return Envelope(
                ReferenceExecutionProgress.Faulted,
                null,
                0,
                new DiagnosticCollection(new[] { diagnostic }));
        }

        private ReferenceExecutionEnvelope Reject(string message, ReferenceUpdateContext? attemptedUpdate = null)
        {
            var diagnostic = ReferenceExecutionDiagnostics.Create(
                ReferenceExecutionDiagnosticCodes.InvalidOperation,
                message,
                _treeInstanceId);
            if (attemptedUpdate.HasValue)
            {
                Emit(attemptedUpdate.Value, ReferenceTraceEventKind.DiagnosticRaised, diagnosticCode: diagnostic.Code);
            }

            return Envelope(
                ReferenceExecutionProgress.Rejected,
                _terminalResult,
                0,
                new DiagnosticCollection(new[] { diagnostic }));
        }

        private bool TryEnterApi(
            out ReferenceExecutionEnvelope rejected,
            ReferenceUpdateContext? attemptedUpdate = null)
        {
            if (_isExecuting)
            {
                rejected = Reject("Reentrant execution of the same tree instance is forbidden.", attemptedUpdate);
                return false;
            }

            _isExecuting = true;
            rejected = default;
            return true;
        }

        private bool TryValidateAbort(
            NodeAbortReason reason,
            RuntimeNodeIndex sourceNodeIndex,
            out ReferenceExecutionEnvelope rejected,
            ReferenceUpdateContext? attemptedUpdate = null)
        {
            if (!Enum.IsDefined(typeof(NodeAbortReason), reason))
            {
                rejected = Reject("An abort reason from the accepted contract is required.", attemptedUpdate);
                return false;
            }

            if (!sourceNodeIndex.IsValid || sourceNodeIndex.Value >= _program.Nodes.Count)
            {
                rejected = Reject("An abort source node inside the compiled program is required.", attemptedUpdate);
                return false;
            }

            rejected = default;
            return true;
        }

        internal byte[] CopyNodeMemory(RuntimeNodeIndex nodeIndex)
        {
            if (!nodeIndex.IsValid || nodeIndex.Value >= _program.Nodes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeIndex));
            }

            var node = _program.Nodes[(int)nodeIndex.Value];
            var result = new byte[checked((int)node.InstanceMemorySize)];
            if (result.Length != 0)
            {
                Array.Copy(_nodeMemory, checked((int)node.InstanceMemoryOffset), result, 0, result.Length);
            }

            return result;
        }

        internal uint GetActivationGeneration(RuntimeNodeIndex nodeIndex)
        {
            if (!nodeIndex.IsValid || nodeIndex.Value >= _activationGenerations.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeIndex));
            }

            return _activationGenerations[nodeIndex.Value];
        }

        private ReferenceNodeContext CreateNodeContext(ReferenceFrame frame, CompiledNodeRecord node)
        {
            return new ReferenceNodeContext(
                _configuration,
                checked((int)node.ConfigOffset),
                checked((int)node.ConfigSize),
                _nodeMemory,
                checked((int)node.InstanceMemoryOffset),
                checked((int)node.InstanceMemorySize),
                _currentUpdate,
                _treeInstanceId,
                frame.NodeIndex,
                frame.ActivationGeneration,
                _nodeServices,
                _nodeServices);
        }

        private void ClearActivationMemory()
        {
            for (var index = 0; index < _program.Nodes.Count; index++)
            {
                var node = _program.Nodes[index];
                if (node.MemoryLifetime == NodeMemoryLifetime.Activation) ClearMemory(node);
            }
        }

        private void ClearMemory(CompiledNodeRecord node)
        {
            if (node.InstanceMemorySize != 0)
            {
                Array.Clear(_nodeMemory, checked((int)node.InstanceMemoryOffset), checked((int)node.InstanceMemorySize));
            }
        }

        private void FinishUpdate()
        {
            Emit(ReferenceTraceEventKind.UpdateCompleted);
            _hasOpenUpdate = false;
            _budgetSuspended = false;
        }

        private void Emit(
            ReferenceTraceEventKind kind,
            RuntimeNodeIndex? nodeIndex = null,
            NodeStatus? status = null,
            NodeExitReason? exitReason = null,
            NodeAbortReason? abortReason = null,
            RuntimeNodeIndex? sourceNodeIndex = null,
            DiagnosticCode diagnosticCode = default,
            ulong stableBlackboardKeyId = 0,
            ulong oldBlackboardVersion = 0,
            ulong newBlackboardVersion = 0)
        {
            Emit(
                _currentUpdate,
                kind,
                nodeIndex,
                status,
                exitReason,
                abortReason,
                sourceNodeIndex,
                diagnosticCode,
                stableBlackboardKeyId,
                oldBlackboardVersion,
                newBlackboardVersion);
        }

        private void Emit(
            ReferenceUpdateContext update,
            ReferenceTraceEventKind kind,
            RuntimeNodeIndex? nodeIndex = null,
            NodeStatus? status = null,
            NodeExitReason? exitReason = null,
            NodeAbortReason? abortReason = null,
            RuntimeNodeIndex? sourceNodeIndex = null,
            DiagnosticCode diagnosticCode = default,
            ulong stableBlackboardKeyId = 0,
            ulong oldBlackboardVersion = 0,
            ulong newBlackboardVersion = 0)
        {
            try
            {
                _traceSink.Record(new ReferenceTraceRecord(
                    update.UpdateId,
                    update.SnapshotRevision,
                    _program.Header.CanonicalSemanticHash,
                    _treeInstanceId,
                    checked(++_traceSequence),
                    kind,
                    nodeIndex ?? RuntimeNodeIndex.Invalid,
                    status,
                    exitReason,
                    abortReason,
                    sourceNodeIndex ?? RuntimeNodeIndex.Invalid,
                    diagnosticCode,
                    stableBlackboardKeyId,
                    oldBlackboardVersion,
                    newBlackboardVersion));
            }
            catch
            {
                // Trace is observational and must never alter execution semantics.
            }
        }

        private ReferenceExecutionEnvelope Envelope(
            ReferenceExecutionProgress progress,
            NodeStatus? rootResult,
            ulong steps,
            DiagnosticCollection diagnostics = null)
        {
            return new ReferenceExecutionEnvelope(
                progress,
                rootResult,
                steps,
                _cumulativeUpdateSteps,
                diagnostics ?? DiagnosticCollection.Empty);
        }

        private ReferenceExecutionEnvelope Deliver(ReferenceExecutionEnvelope envelope)
        {
            return new ReferenceExecutionEnvelope(
                envelope.Progress,
                envelope.RootResult,
                envelope.SegmentSteps,
                envelope.CumulativeSteps,
                CombineDiagnostics(envelope.Diagnostics),
                _commandBuffer.TakeBatch());
        }

        private DiagnosticCollection CombineDiagnostics(DiagnosticCollection diagnostics)
        {
            if (_asyncDiagnostics.Count == 0)
            {
                return diagnostics ?? DiagnosticCollection.Empty;
            }

            var values = new List<Diagnostic>(_asyncDiagnostics.Count + (diagnostics?.Count ?? 0));
            values.AddRange(_asyncDiagnostics);
            if (diagnostics != null)
            {
                for (var index = 0; index < diagnostics.Count; index++) values.Add(diagnostics[index]);
            }

            _asyncDiagnostics.Clear();
            return new DiagnosticCollection(values);
        }

        private void AddAsyncDiagnostics(DiagnosticCollection diagnostics, bool discarded)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                var diagnostic = diagnostics[index];
                _asyncDiagnostics.Add(diagnostic);
                Emit(discarded ? ReferenceTraceEventKind.CompletionDiscarded : ReferenceTraceEventKind.DiagnosticRaised,
                    diagnosticCode: diagnostic.Code);
            }
        }

        private sealed class ReferenceNodeServices : IReferenceNodeServices, IReferenceBlackboardServices
        {
            private readonly ReferenceExecutionMachine _owner;

            internal ReferenceNodeServices(ReferenceExecutionMachine owner)
            {
                _owner = owner;
            }

            public bool TryStart(
                RuntimeNodeIndex nodeIndex,
                uint activationGeneration,
                ReferenceAsyncCommandContract contract,
                ReadOnlySpan<byte> payload,
                out OperationId operationId)
            {
                var result = _owner._asyncCoordinator.TryStart(
                    nodeIndex,
                    activationGeneration,
                    contract,
                    payload,
                    out operationId,
                    out var diagnostic);
                if (diagnostic != null)
                {
                    _owner.AddAsyncDiagnostics(new DiagnosticCollection(new[] { diagnostic }), false);
                }

                if (result) _owner.Emit(ReferenceTraceEventKind.CommandEmitted, nodeIndex);
                return result;
            }

            public bool TryConsume(
                RuntimeNodeIndex nodeIndex,
                uint activationGeneration,
                OperationId operationId,
                ReferenceCompletionExpectation expectation,
                out ReferenceCompletionView completion)
            {
                if (!OwnsOperation(nodeIndex, activationGeneration, operationId))
                {
                    completion = default;
                    var diagnostic = CommandAsyncDiagnostics.Create(
                        CommandAsyncDiagnosticCodes.UnknownOperation,
                        "A node attempted to consume an operation owned by another activation.",
                        _owner._treeInstanceId);
                    _owner.AddAsyncDiagnostics(new DiagnosticCollection(new[] { diagnostic }), false);
                    return false;
                }

                var result = _owner._completionInbox.TryConsume(operationId, expectation, out completion, out var diagnostics);
                _owner.AddAsyncDiagnostics(diagnostics, true);
                if (result) _owner.Emit(ReferenceTraceEventKind.CompletionConsumed, nodeIndex);
                return result;
            }

            public bool TryCancel(
                RuntimeNodeIndex nodeIndex,
                uint activationGeneration,
                OperationId operationId,
                ReferenceAsyncCommandContract contract,
                ReadOnlySpan<byte> payload)
            {
                if (!OwnsOperation(nodeIndex, activationGeneration, operationId)) return false;
                var result = _owner._asyncCoordinator.TryCancel(
                    operationId,
                    contract,
                    payload,
                    out var emitted,
                    out var diagnostic);
                if (diagnostic != null)
                {
                    _owner.AddAsyncDiagnostics(new DiagnosticCollection(new[] { diagnostic }), false);
                }

                if (emitted) _owner.Emit(ReferenceTraceEventKind.CommandEmitted, nodeIndex);
                _owner.AddAsyncDiagnostics(_owner._completionInbox.DiscardCancelled(operationId), true);
                return result;
            }

            public bool TryReadBlackboard(
                RuntimeNodeIndex nodeIndex,
                uint declaredReadOrdinal,
                out BlackboardValue value)
            {
                if (_owner._blackboard == null)
                {
                    value = default;
                    return false;
                }
                var result = _owner._blackboard.TryRead(nodeIndex, declaredReadOrdinal, out value);
                if (!result.Success) _owner._pendingNodeServiceFault = result.Diagnostic;
                return result.Success;
            }

            public bool TryWriteBlackboard(
                RuntimeNodeIndex nodeIndex,
                uint declaredWriteOrdinal,
                BlackboardValue value)
            {
                if (_owner._blackboard == null) return false;
                var result = _owner._blackboard.TryWrite(nodeIndex, declaredWriteOrdinal, value);
                _owner.RecordBlackboardWrite(nodeIndex, result);
                return result.Success;
            }

            public bool TryReadRegisteredBlackboard(
                RuntimeNodeIndex nodeIndex,
                uint declaredReadOrdinal,
                ulong typeId,
                uint version,
                out byte[] value)
            {
                if (_owner._blackboard == null)
                {
                    value = null;
                    return false;
                }
                var result = _owner._blackboard.TryReadRegistered(
                    nodeIndex,
                    declaredReadOrdinal,
                    typeId,
                    version,
                    out value);
                if (!result.Success) _owner._pendingNodeServiceFault = result.Diagnostic;
                return result.Success;
            }

            public bool TryWriteRegisteredBlackboard(
                RuntimeNodeIndex nodeIndex,
                uint declaredWriteOrdinal,
                ulong typeId,
                uint version,
                ReadOnlySpan<byte> value)
            {
                if (_owner._blackboard == null) return false;
                var result = _owner._blackboard.TryWriteRegistered(
                    nodeIndex,
                    declaredWriteOrdinal,
                    typeId,
                    version,
                    value);
                _owner.RecordBlackboardWrite(nodeIndex, result);
                return result.Success;
            }

            private bool OwnsOperation(
                RuntimeNodeIndex nodeIndex,
                uint activationGeneration,
                OperationId operationId)
            {
                return operationId.IsValid
                    && operationId.TreeInstanceId == _owner._treeInstanceId
                    && operationId.NodeIndex == nodeIndex
                    && operationId.ActivationGeneration == activationGeneration;
            }
        }

        private sealed class ReferenceObserverReadServices : IReferenceObserverReadServices
        {
            private readonly ReferenceExecutionMachine _owner;
            internal ReferenceObserverReadServices(ReferenceExecutionMachine owner) { _owner = owner; }
            public void RecordRead(BlackboardStorageResult result)
            {
                if (!result.Success) _owner._pendingNodeServiceFault = result.Diagnostic;
            }
        }

        private static byte[] Copy(IReadOnlyList<byte> source)
        {
            var result = new byte[source.Count];
            for (var index = 0; index < source.Count; index++) result[index] = source[index];
            return result;
        }
    }
}
