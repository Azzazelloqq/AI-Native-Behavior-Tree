using System;
using AIBT.Burst;
using AIBT.Runtime.Scheduling;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace AIBT
{
    /// <summary>
    /// P10-009 (ADR-P10-008 decision D2): why <see cref="ProductionTreeAgentCore"/> stopped an agent.
    /// The text of each value is built only when a log context is set, so a container that does not
    /// log builds no string on a fault.
    /// </summary>
    internal enum ProductionTreeAgentFailureDetail : byte
    {
        Text = 0,
        Reentrant,
        Clock,
        CannotAdvance,
        CallbackRejected,
        StepLimit,
        UpdateIdOverflow,
        CannotBeginUpdate,
        CannotCancelDuringDestruction,
        AgentFrameNotRun,
    }

    /// <summary>
    /// P10-009 (ADR-P10-008 decision D2): one native tree instance and its update bookkeeping, free of
    /// <c>MonoBehaviour</c>. <see cref="ProductionTreeHost"/> owns one and delegates to it; the host keeps
    /// Unity's object model (Update, OnDestroy, enabled state), its clock, its drive lock and its
    /// scheduler registration. <c>AIBT.Simulation.TreeGroup</c> owns one per agent slot. Every method
    /// here is the code the host ran before the extraction, moved without change of order or result.
    /// Main thread only; never re-entered by itself (callers hold their own lock).
    /// </summary>
    internal sealed class ProductionTreeAgentCore
    {
        private readonly UnityEngine.Object _logContext;
        private readonly bool _logs;
        private SchedulingAgent[] _agents;
        private ProductionTreeHost.DispatchLifecycle _dispatch;
        private GeneratedTreeDispatchAdapterV2 _generatedDispatch;
        private NativeTraceRecorderV1 _recorder;
        private ulong _updateId;
        private long _timeMicroseconds;
        private bool _ready;
        private bool _bootstrapped;
        private bool _updateOpen;
        private bool _suspended;
        private bool _hasExecuted;
        private bool _destroyRequested;
        private bool _disposed;
        private NativeLifecycleStepResultV1 _pendingStep;
        private bool _hasPendingStep;
        // P11-014 review item 2: the cause a pending dispatch's effect reservation recorded, for the message.
        private string _pendingCapacityCause;
        private int _configuredEffectQueueCapacity;
        private ProductionTreeEffectQueue _effectQueue;
        // P10-010: simulation-container agents only.
        private AIBT.Simulation.SimulationAgentBridgeV1 _simulation;
        private ulong _stepsThisUpdate;
        private uint _maximumStepsPerUpdate = ProductionTreeHost.DefaultMaximumStepsPerUpdate;
        // P10-004: JobsAgentFrame per-agent logs and job-transport storage, created on first use.
        private UnsafeList<AgentFrameTraceEventV1> _agentFrameTrace;
        private UnsafeList<ProductionTreeEffect> _agentFrameEffects;
        private NativeArray<byte> _agentFrameEmptyBytes;
        private NativeArray<NativeParallelBranchStateV1> _agentFrameEmptyParallel;

        /// <param name="logContext">
        /// The host whose failures are logged with <c>Debug.LogError</c>; null logs nothing (the
        /// simulation container reports failures as data).
        /// </param>
        internal ProductionTreeAgentCore(UnityEngine.Object logContext)
        {
            _logContext = logContext;
            _logs = !ReferenceEquals(logContext, null);
        }

        internal uint MaximumStepsPerUpdate
        {
            get => _maximumStepsPerUpdate;
            set => _maximumStepsPerUpdate = value;
        }

        internal ulong StepsThisUpdate => _stepsThisUpdate;
        internal NativeTraceChannelOwnerV1 TraceChannelOwner { get; private set; }
        internal CompiledProgram Program { get; private set; }
        internal NodeStatus? LastRootResult { get; private set; }
        internal NativeRuntimeFailureV1 LastFailure { get; private set; }
        /// <summary>P10-010: the callback result that faulted the agent (CallbackRejected), else Success.</summary>
        internal BurstContextResult LastRejectedResult { get; private set; }
        internal ulong TotalUpdates => _updateId;
        internal ulong InstanceId { get; private set; }
        internal uint? StepBudget { get; set; }
        internal bool Ready => _ready;
        internal bool Bootstrapped => _bootstrapped;
        internal bool UpdateOpen => _updateOpen;
        internal bool DestroyRequested => _destroyRequested;
        internal bool Disposed => _disposed;
        internal bool HasPendingDispatch => _hasPendingStep;
        internal bool HasExecuted => _hasExecuted;
        internal long TimeMicroseconds => _timeMicroseconds;
        internal GeneratedTreeDispatchAdapterV2 GeneratedDispatch => _generatedDispatch;
        internal ProductionTreeEffectQueue EffectQueue => _effectQueue;
        internal NativeLifecycleMachineV1 Machine => _agents[0].Machine;
        internal bool HasAgents => _agents != null;

        internal bool TryConfigureEffectDelivery(int capacity, bool driving, out NativeRuntimeFailureV1 failure)
        {
            if (_bootstrapped || _disposed || driving)
            {
                failure = InvalidLifetime();
                return false;
            }
            _configuredEffectQueueCapacity = capacity;
            failure = default;
            return true;
        }

        internal bool TryBootstrapGenerated(
            GeneratedTreeRuntimeDefinitionV2 definition,
            GeneratedBurstCatalogV2 catalog,
            NativeTraceChannelCapacityV1? traceCapacity,
            TreeInstanceId treeInstanceId,
            bool driving,
            out NativeRuntimeFailureV1 failure)
            => TryBootstrapGenerated(definition, catalog, traceCapacity, treeInstanceId, driving, null, null, out failure);

        /// <summary>P10-010: the simulation container's bootstrap: inputs and commands through <paramref name="simulation"/>.</summary>
        internal bool TryBootstrapGenerated(
            GeneratedTreeRuntimeDefinitionV2 definition,
            GeneratedBurstCatalogV2 catalog,
            NativeTraceChannelCapacityV1? traceCapacity,
            TreeInstanceId treeInstanceId,
            bool driving,
            AIBT.Simulation.SimulationProgramMapV1 simulationMap,
            AIBT.Simulation.SimulationAgentBridgeV1 simulation,
            out NativeRuntimeFailureV1 failure)
        {
            if (_bootstrapped || _disposed || driving)
            {
                failure = InvalidLifetime();
                return false;
            }
            ProductionTreeEffectQueue effectQueue = null;
            if (_configuredEffectQueueCapacity != 0)
                effectQueue = new ProductionTreeEffectQueue(_configuredEffectQueueCapacity);
            if (!GeneratedTreeDispatchAdapterV2.TryCreate(
                    definition, catalog, treeInstanceId, simulationMap, simulation, out var adapter, out var contextFailure))
            {
                failure = new NativeRuntimeFailureV1(
                    contextFailure == BurstContextResult.CapacityExceeded
                        ? NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded
                        : NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid);
                return false;
            }
            if (!TryBootstrapCore(
                    definition.Binding.SemanticProgram, adapter.Dispatch,
                    traceCapacity, treeInstanceId, driving, out failure))
            {
                adapter.Dispose();
                return false;
            }
            _generatedDispatch = adapter;
            _effectQueue = effectQueue;
            _simulation = simulation;
            return true;
        }

        /// <param name="traceCapacity">Null creates no trace channel and records nothing (container agents).</param>
        internal bool TryBootstrapCore(
            CompiledProgram program,
            ProductionTreeHost.DispatchLifecycle dispatch,
            NativeTraceChannelCapacityV1? traceCapacity,
            TreeInstanceId treeInstanceId,
            bool driving,
            out NativeRuntimeFailureV1 failure)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (dispatch == null) throw new ArgumentNullException(nameof(dispatch));
            if (_bootstrapped || _disposed || driving)
            {
                failure = InvalidLifetime();
                return false;
            }
            var kinds = new NativeLifecycleNodeKindV1[program.Nodes.Count];
            for (var index = 0; index < kinds.Length; index++)
                kinds[index] = NativeHotReloadInstance.ClassifyKind(program.Nodes[index].NodeTypeId);
            if (!SchedulingPolicyDriver.TryCreateAgents(program, kinds, 1, Allocator.Persistent, out _agents, out failure))
                return false;
            var instanceId = treeInstanceId.Value;
            if (traceCapacity.HasValue)
            {
                // BudgetYielded/ExecutionResumed are Detailed events in the existing trace contract.
                if (!NativeTraceChannelOwnerV1.TryCreate(traceCapacity.Value, NativeTraceLevelV1.Detailed,
                        new TreeInstanceId(instanceId), 0, Allocator.Persistent, out var owner, out var traceFailure))
                {
                    _agents[0].Dispose();
                    _agents = null;
                    failure = new NativeRuntimeFailureV1(traceFailure.Code);
                    return false;
                }
                TraceChannelOwner = owner;
                _recorder = new NativeTraceRecorderV1(owner, new NativeHash256V1(program.Header.CompiledContentHash), instanceId);
            }
            _dispatch = dispatch;
            Program = program;
            InstanceId = instanceId;
            _bootstrapped = true;
            _ready = true;
            failure = default;
            return true;
        }

        /// <summary>
        /// P10-012 (simulation container only): starts recording this agent's lifecycle into
        /// <paramref name="owner"/> through <paramref name="recorder"/>, both owned by the caller (a group's
        /// trace pool), from the next update on. Refused while a dispatch is pending or a channel
        /// created at bootstrap is attached (hosts); the container calls it only between steps, when no
        /// update runs. Nothing else changes: the recorder only observes.
        /// </summary>
        internal bool TryAttachTrace(NativeTraceChannelOwnerV1 owner, NativeTraceRecorderV1 recorder)
        {
            if (owner == null || recorder == null || !_bootstrapped || _disposed || _hasPendingStep
                || _recorder != null || TraceChannelOwner != null)
                return false;
            TraceChannelOwner = owner;
            _recorder = recorder;
            return true;
        }

        /// <summary>P10-012: stops recording (the caller keeps the channel). Refused while a dispatch is pending.</summary>
        internal bool TryDetachTrace()
        {
            if (_hasPendingStep) return false;
            _recorder?.ReleaseWriter();
            _recorder = null;
            TraceChannelOwner = null;
            return true;
        }

        /// <summary>
        /// Counts one step against <see cref="MaximumStepsPerUpdate"/> before it is taken. Returns
        /// false, after failing this agent, when the update has already used its whole allowance.
        /// </summary>
        private bool TryCountStep()
        {
            if (_stepsThisUpdate >= _maximumStepsPerUpdate)
            {
                FailStepLimit();
                return false;
            }
            _stepsThisUpdate++;
            return true;
        }

        internal void FailStepLimit()
            => Fail(
                new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeUpdateStepLimitExceeded),
                ProductionTreeAgentFailureDetail.StepLimit);

        /// <summary>
        /// The clock check and update begin every drive entry point runs when no update is open:
        /// fails on a negative or decreasing time, otherwise begins the next logical update.
        /// </summary>
        internal bool TryBeginUpdateAt(long now)
        {
            if (now < 0 || (_updateId != 0 && now < _timeMicroseconds))
            {
                Fail(InvalidLifetime(), ProductionTreeAgentFailureDetail.Clock);
                return false;
            }
            return BeginUpdate(now);
        }

        /// <summary>Resumes a budget-suspended update (records ExecutionResumed).</summary>
        internal void ResumeIfSuspended()
        {
            if (!_suspended) return;
            _recorder?.RecordExecutionResumed(_updateId);
            _suspended = false;
        }

        private bool BeginUpdate(long timeMicroseconds)
        {
            if (_updateId == ulong.MaxValue)
            {
                Fail(new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow),
                    ProductionTreeAgentFailureDetail.UpdateIdOverflow);
                return false;
            }
            var nextId = _updateId + 1;
            if (!_agents[0].Machine.TryBeginUpdate(nextId, timeMicroseconds, out var failure))
            {
                Fail(failure, ProductionTreeAgentFailureDetail.CannotBeginUpdate);
                return false;
            }
            _updateId = nextId;
            _timeMicroseconds = timeMicroseconds;
            _updateOpen = true;
            _stepsThisUpdate = 0;
            _recorder?.RecordUpdateStarted(_updateId);
            return true;
        }

        private void EndUpdate(in NativeLifecycleStepResultV1 step)
        {
            _updateOpen = false;
            _recorder?.RecordUpdateEnded(_updateId, step.HasRootStatus, step.RootStatus);
            if (step.Kind == NativeLifecycleStepKindV1.Completed)
            {
                if (step.HasRootStatus) LastRootResult = step.RootStatus;
                _ready = false;
            }
        }

        internal bool AdvanceOneStepForGroupedDispatch(out ProductionTreeHost.DispatchRequest request)
        {
            // NativeLifecycleStepKindV1 has several internal-progress kinds (CompositeEntered,
            // ChildSelected, ...) that carry nothing for a caller to act on -- exactly what
            // RunSegment's own while loop already keeps calling TryAdvance through. A wave barrier
            // is reached only at DispatchRequired (return it unresolved) or Completed/Waiting
            // (nothing pending this update); every other kind must keep advancing right here.
            request = default;
            while (_ready)
            {
                if (!TryCountStep()) return false;
                if (!_agents[0].Machine.TryAdvance(out var step, out var failure))
                {
                    Fail(failure, ProductionTreeAgentFailureDetail.CannotAdvance);
                    return false;
                }
                _hasExecuted = true;
                _recorder?.RecordStep(_updateId, step);
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    _pendingStep = step;
                    _hasPendingStep = true;
                    request = new ProductionTreeHost.DispatchRequest(step, _updateId, _timeMicroseconds);
                    return true;
                }
                if (step.Kind == NativeLifecycleStepKindV1.Completed || step.Kind == NativeLifecycleStepKindV1.Waiting)
                {
                    EndUpdate(in step);
                    break;
                }
            }
            return false;
        }

        internal ProductionTreeHost.BatchedStepOutcome TryHandleBatchedStepResult(
            NativeLifecycleStepResultV1 step, bool enforceStepLimit, out ProductionTreeHost.DispatchRequest request)
        {
            request = default;
            if (enforceStepLimit)
            {
                if (!TryCountStep()) return ProductionTreeHost.BatchedStepOutcome.Terminal;
            }
            else _stepsThisUpdate++;
            _hasExecuted = true;
            _recorder?.RecordStep(_updateId, step);
            if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
            {
                _pendingStep = step;
                _hasPendingStep = true;
                request = new ProductionTreeHost.DispatchRequest(step, _updateId, _timeMicroseconds);
                return ProductionTreeHost.BatchedStepOutcome.DispatchRequired;
            }
            if (step.Kind == NativeLifecycleStepKindV1.Completed || step.Kind == NativeLifecycleStepKindV1.Waiting)
            {
                EndUpdate(in step);
                return ProductionTreeHost.BatchedStepOutcome.Terminal;
            }
            return ProductionTreeHost.BatchedStepOutcome.Continue;
        }

        /// <summary>Calls the dispatch adapter for the pending step; the caller completes it.</summary>
        internal bool TryResolveSinglePendingDispatch(out BurstContextResult result, out NodeStatus status)
        {
            result = default;
            status = default;
            if (!_hasPendingStep) return false;
            var request = new ProductionTreeHost.DispatchRequest(_pendingStep, _updateId, _timeMicroseconds);
            SchedulerFrameProfileV1.Add(SchedulerFrameProfileV1.Counter.SingleDispatches, 1);
            if (TryReserveGeneratedEffectCapacity(request.NodeIndex, out var reserveFailure))
            {
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Reserve);
                result = _dispatch(in request, out status);
            }
            else
            {
                result = reserveFailure;
                status = NodeStatus.Failure;
                _pendingCapacityCause = ReservationCause();
            }
            SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Return);
            return true;
        }

        internal AgentFrameFallbackReason GetAgentFrameFallback()
        {
            if (_generatedDispatch == null) return AgentFrameFallbackReason.ManagedDispatch;
            if (StepBudget.HasValue) return AgentFrameFallbackReason.StepBudgetConfigured;
            return _generatedDispatch.GetAgentFrameFallback();
        }

        /// <summary>
        /// P10-004: fills <paramref name="agent"/> for the agent-frame job from this instance's open
        /// update. The caller has already begun (or resumed) the update and checked eligibility.
        /// </summary>
        internal void FillAgentFrame(ref AgentFrameAgentV1 agent)
        {
            EnsureAgentFrameStorage();
            agent.Machine = _agents[0].Machine;
            agent.Machine.TryAttachCreatedEmptyJobStorage(_agentFrameEmptyBytes, _agentFrameEmptyParallel);
            agent.Slots = _generatedDispatch.GetAgentFrameSlots();
            agent.TreeValues = _generatedDispatch.TreeValues;
            agent.TreeVersions = _generatedDispatch.TreeVersions;
            agent.Trace = _agentFrameTrace;
            agent.Effects = _agentFrameEffects;
            PrepareAgentFrame(ref agent);
        }

        /// <summary>
        /// P10-009: the per-update half of <see cref="FillAgentFrame"/> for an entry whose storage
        /// handles were already written by an earlier fill (a container's persistent agent table).
        /// </summary>
        internal void PrepareAgentFrame(ref AgentFrameAgentV1 agent)
        {
            agent.TimeMicroseconds = _timeMicroseconds;
            agent.StepsThisUpdate = _stepsThisUpdate;
            agent.MaximumSteps = _maximumStepsPerUpdate;
            agent.EffectFree = _effectQueue != null ? _effectQueue.Free : -1;
            agent.SimulationMode = _simulation != null ? (byte)1 : (byte)0;
            agent.TraceEnabled = _recorder != null && _recorder.IsRecording ? (byte)1 : (byte)0;
            agent.Outcome = AgentFrameOutcomeV1.None;
        }

        private void EnsureAgentFrameStorage()
        {
            if (_agentFrameTrace.IsCreated) return;
            _agentFrameTrace = new UnsafeList<AgentFrameTraceEventV1>(64, Allocator.Persistent);
            _agentFrameEffects = new UnsafeList<ProductionTreeEffect>(_effectQueue != null ? 8 : 1, Allocator.Persistent);
            _agentFrameEmptyBytes = new NativeArray<byte>(0, Allocator.Persistent);
            _agentFrameEmptyParallel = new NativeArray<NativeParallelBranchStateV1>(0, Allocator.Persistent);
        }

        /// <summary>
        /// P10-004: after the agent-frame job ran this instance's update: replays its trace calls and
        /// effects in step order, then applies the same terminal or failure bookkeeping
        /// <see cref="RunSegment"/> applies.
        /// </summary>
        internal void FinishAgentFrame(ref AgentFrameAgentV1 agent)
        {
            // The job may have grown the logs; keep the current allocations.
            _agentFrameTrace = agent.Trace;
            _agentFrameEffects = agent.Effects;
            _stepsThisUpdate = agent.StepsThisUpdate;
            if (agent.Executed != 0) _hasExecuted = true;
            if (_recorder != null)
            {
                for (var index = 0; index < _agentFrameTrace.Length; index++)
                {
                    var record = _agentFrameTrace[index];
                    if (record.IsCompletion != 0) _recorder.RecordDispatchCompletion(_updateId, record.Step, record.Status);
                    else _recorder.RecordStep(_updateId, record.Step);
                }
            }
            if (_effectQueue != null)
            {
                for (var index = 0; index < _agentFrameEffects.Length; index++)
                    _effectQueue.TryEnqueue(in _agentFrameEffects.ElementAt(index));
            }
            switch (agent.Outcome)
            {
                case AgentFrameOutcomeV1.Terminal:
                    var step = agent.TerminalStep;
                    EndUpdate(in step);
                    break;
                case AgentFrameOutcomeV1.AdvanceFailed:
                    Fail(agent.Failure, ProductionTreeAgentFailureDetail.CannotAdvance);
                    break;
                case AgentFrameOutcomeV1.CallbackRejected:
                    Fail(agent.Failure, ProductionTreeAgentFailureDetail.CallbackRejected, agent.RejectedResult,
                        CapacityCause((AgentFrameRejectionSiteV1)agent.RejectedSite), agent.RejectedNodeIndex);
                    break;
                case AgentFrameOutcomeV1.StepLimit:
                    FailStepLimit();
                    break;
                default:
                    Fail(InvalidLifetime(), ProductionTreeAgentFailureDetail.AgentFrameNotRun);
                    break;
            }
        }

        internal bool TryReserveGeneratedEffectCapacity(uint nodeIndex, out BurstContextResult failure)
        {
            if (_simulation != null)
            {
                failure = _generatedDispatch != null ? _generatedDispatch.TryReserveSimulationCommands(nodeIndex) : BurstContextResult.InvalidHandle;
                return failure == BurstContextResult.Success;
            }
            if (_effectQueue == null)
            {
                failure = BurstContextResult.Success;
                return true;
            }
            if (_generatedDispatch != null && _generatedDispatch.TryReserveEffectCapacity(nodeIndex, _effectQueue))
            {
                failure = BurstContextResult.Success;
                return true;
            }
            failure = BurstContextResult.CapacityExceeded;
            return false;
        }

        /// <summary>
        /// Resolves the pending dispatch requirement with the callback's result. Returns false,
        /// without mutating the machine, if none is pending or the agent already faulted. The
        /// caller releases its own drive lock.
        /// </summary>
        internal bool CompletePendingDispatch(BurstContextResult result, NodeStatus status, string capacityCause = null)
        {
            if (!_hasPendingStep) return false;
            var step = _pendingStep;
            _hasPendingStep = false;
            var cause = capacityCause ?? _pendingCapacityCause;
            _pendingCapacityCause = null;
            if (!_ready) return false; // A reentrant call may already have faulted the host.
            if (result == BurstContextResult.Success
                && !TryCaptureGeneratedEffects(step.NodeIndex, out result))
            {
                status = NodeStatus.Failure;
                cause = CaptureCause();
            }
            SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.EffectCapture);
            if (!_agents[0].Machine.TryCompleteDispatch(step.DispatchToken, result, status, out var failure))
            {
                Fail(failure, ProductionTreeAgentFailureDetail.CallbackRejected, result, cause, step.NodeIndex);
                return false;
            }
            SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.MachineComplete);
            _recorder?.RecordDispatchCompletion(_updateId, step, status);
            SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.TraceComplete);
            return true;
        }

        internal void RunSegment(uint? limit, bool destroying)
        {
            // The machine owns persistent cursors. This budget counts just this frame segment.
            // A callback and its acknowledgement always complete atomically.
            var budget = default(NativeBudgetStateV1);
            if (limit.HasValue) NativeLifecycleBudgetDriverV1.TryBeginSegment(limit.Value, ref budget);
            while (_ready && (destroying || !_destroyRequested))
            {
                NativeLifecycleStepResultV1 step;
                NativeRuntimeFailureV1 failure;
                bool advanced;
                if (limit.HasValue)
                {
                    advanced = NativeLifecycleBudgetDriverV1.TryAdvance(ref _agents[0].Machine, ref budget, out var kind, out step, out failure);
                    if (advanced && kind == NativeBudgetAdvanceKindV1.Suspended)
                    {
                        _suspended = true;
                        _recorder?.RecordBudgetYielded(_updateId);
                        return;
                    }
                }
                else
                {
                    // P10-004: Immediate (and teardown) is bounded per update; Budgeted yields.
                    if (!TryCountStep()) return;
                    advanced = _agents[0].Machine.TryAdvance(out step, out failure);
                }
                SchedulerFrameProfileV1.Lap(SchedulerFrameProfileV1.Stage.HostAdvance);
                if (!advanced)
                {
                    Fail(failure, ProductionTreeAgentFailureDetail.CannotAdvance);
                    return;
                }
                _hasExecuted = true;
                _recorder?.RecordStep(_updateId, step);
                SchedulerFrameProfileV1.Lap(SchedulerFrameProfileV1.Stage.HostRecord);
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    var request = new ProductionTreeHost.DispatchRequest(step, _updateId, _timeMicroseconds);
                    NodeStatus status;
                    BurstContextResult result;
                    SchedulerFrameProfileV1.StepStart();
                    SchedulerFrameProfileV1.Add(SchedulerFrameProfileV1.Counter.SingleDispatches, 1);
                    string capacityCause = null;
                    if (TryReserveGeneratedEffectCapacity(request.NodeIndex, out var reserveFailure))
                    {
                        SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Reserve);
                        result = _dispatch(in request, out status);
                    }
                    else
                    {
                        result = reserveFailure;
                        status = NodeStatus.Failure;
                        capacityCause = ReservationCause();
                    }
                    SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Return);
                    SchedulerFrameProfileV1.Lap(SchedulerFrameProfileV1.Stage.HostDispatch);
                    if (!_ready) return; // A reentrant call may already have faulted the host.
                    if (result == BurstContextResult.Success
                        && !TryCaptureGeneratedEffects(step.NodeIndex, out result))
                    {
                        status = NodeStatus.Failure;
                        capacityCause = CaptureCause();
                    }
                    SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.EffectCapture);
                    if (!_agents[0].Machine.TryCompleteDispatch(step.DispatchToken, result, status, out failure))
                    {
                        Fail(failure, ProductionTreeAgentFailureDetail.CallbackRejected, result, capacityCause, step.NodeIndex);
                        return;
                    }
                    SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.MachineComplete);
                    _recorder?.RecordDispatchCompletion(_updateId, step, status);
                    SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.TraceComplete);
                    SchedulerFrameProfileV1.Lap(SchedulerFrameProfileV1.Stage.HostComplete);
                }
                else if (step.Kind == NativeLifecycleStepKindV1.Completed || step.Kind == NativeLifecycleStepKindV1.Waiting)
                {
                    EndUpdate(in step);
                    SchedulerFrameProfileV1.Lap(SchedulerFrameProfileV1.Stage.HostRecord);
                    return;
                }
            }
        }

        private bool TryCaptureGeneratedEffects(uint nodeIndex, out BurstContextResult failure)
        {
            if (_simulation != null)
            {
                failure = _generatedDispatch != null ? _generatedDispatch.TryCaptureSimulationCommands(nodeIndex) : BurstContextResult.InvalidHandle;
                return failure == BurstContextResult.Success;
            }
            if (_effectQueue == null)
            {
                failure = BurstContextResult.Success;
                return true;
            }
            if (_generatedDispatch == null)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }
            return _generatedDispatch.TryConsumePublishedEffects(nodeIndex, _effectQueue, out failure);
        }

        internal static NativeRuntimeFailureV1 InvalidLifetime()
            => new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid);

        internal void Fail(NativeRuntimeFailureV1 failure, string detail)
            => Fail(failure, ProductionTreeAgentFailureDetail.Text, default, detail);

        internal void Fail(
            NativeRuntimeFailureV1 failure,
            ProductionTreeAgentFailureDetail detail,
            BurstContextResult rejected = default,
            string text = null,
            uint nodeIndex = uint.MaxValue)
        {
            if (LastFailure.Code != NativeRuntimeDiagnosticCodeV1.None) return;
            LastFailure = failure;
            LastRejectedResult = detail == ProductionTreeAgentFailureDetail.CallbackRejected ? rejected : BurstContextResult.Success;
            _ready = false;
            _recorder?.ReleaseWriter();
            if (_logs)
                Debug.LogError("AIBT ProductionTreeHost: " + failure.Code + ": " + Describe(detail, rejected, text, nodeIndex), _logContext);
        }

        private string Describe(ProductionTreeAgentFailureDetail detail, BurstContextResult rejected, string text, uint nodeIndex)
        {
            if (detail == ProductionTreeAgentFailureDetail.CallbackRejected && rejected == BurstContextResult.CapacityExceeded)
                return DescribeCapacityExceeded(nodeIndex, text);
            switch (detail)
            {
                case ProductionTreeAgentFailureDetail.Reentrant: return "Reentrant execution is not supported.";
                case ProductionTreeAgentFailureDetail.Clock: return "Clock must return nonnegative, nondecreasing microseconds.";
                case ProductionTreeAgentFailureDetail.CannotAdvance: return "Cannot advance execution.";
                case ProductionTreeAgentFailureDetail.CallbackRejected: return "Callback rejected: " + rejected;
                case ProductionTreeAgentFailureDetail.StepLimit:
                    return "The update needed more than MaximumStepsPerUpdate (" + _maximumStepsPerUpdate + ") lifecycle steps.";
                case ProductionTreeAgentFailureDetail.UpdateIdOverflow: return "Update ID overflow.";
                case ProductionTreeAgentFailureDetail.CannotBeginUpdate: return "Cannot begin update.";
                case ProductionTreeAgentFailureDetail.CannotCancelDuringDestruction: return "Cannot cancel active work during destruction.";
                case ProductionTreeAgentFailureDetail.AgentFrameNotRun: return "The agent-frame job did not run this host's update.";
                default: return text;
            }
        }

        /// <summary>
        /// P11-014 item 7, corrected by the review follow-up (item 2): a CapacityExceeded refusal names the
        /// node. It names the limit only where the failing site knows it (<paramref name="cause"/>: the
        /// effect or command reservation before the callback, the effect or command capture after it, a
        /// failed group); a refusal returned by the callback itself lists the possible limits. The failure
        /// code is unchanged.
        /// </summary>
        private string DescribeCapacityExceeded(uint nodeIndex, string cause)
        {
            var node = "a native node (this path did not record which)";
            if (nodeIndex != uint.MaxValue && Program != null)
            {
                node = "runtime node " + nodeIndex;
                for (var index = 0; index < Program.DebugMap.Count; index++)
                    if (Program.DebugMap[index].RuntimeNodeIndex == nodeIndex)
                        node = "node '" + Program.DebugMap[index].AuthoringNodeId.Value + "'";
            }
            return "Callback rejected: CapacityExceeded at " + node + ". " + (cause ?? CallbackCapacityLimits);
        }

        /// <summary>The possible limits of a CapacityExceeded the callback returned; the site does not know which one.</summary>
        internal const string CallbackCapacityLimits =
            "The node's callback was refused because one of its fixed capacities was too small; this site does not record "
            + "which. The possible limits: the callback's value sessions or their staging bytes (the catalog sizes them for its "
            + "largest node: one session per Read or Write binding, two per ReadWrite binding, so reading or writing one binding "
            + "more often in one callback is refused; read a value once into a local, burst-node-authoring.md, 'Read a binding "
            + "once per callback'), and the node's command, command payload or operation capacity (the effects, simulation "
            + "commands or action changes it publishes in one callback).";

        /// <summary>The text for a failed batched group (<see cref="ProductionDispatchResolverV1"/>): every member stops.</summary>
        internal static string GroupCapacityCause(int members)
            => "The batched dispatch of a group of " + members + " nodes of one catalog was refused, so every host in the group "
               + "stopped; the group does not record which member or which limit. The possible limits: a member's effect queue "
               + "or command quota (reserved before the callbacks), the group's workspace, and every limit of a member's "
               + "callback (its value sessions and their staging bytes, its command, command payload or operation capacity; "
               + "burst-node-authoring.md, 'Read a binding once per callback').";

        /// <summary>The reservation before a node's callback failed: the node was not called.</summary>
        private string ReservationCause()
            => _simulation != null
                ? "The simulation container's command quota had no room for the commands this node may publish in one callback, "
                  + "so the node was not called."
                : "The host's effect queue had fewer free entries than the effects this node may publish in one callback, so "
                  + "the node was not called. Drain the effects more often or give the host a larger effect capacity.";

        /// <summary>The capture after a node's callback failed: the node ran, its output did not fit.</summary>
        private string CaptureCause()
            => _simulation != null
                ? "The node ran, but the simulation container did not accept the commands or action changes it published "
                  + "(a command channel, action or event capacity)."
                : "The node ran, but the effects it published did not fit the host's effect queue.";

        private string CapacityCause(AgentFrameRejectionSiteV1 site)
        {
            switch (site)
            {
                case AgentFrameRejectionSiteV1.Reservation: return ReservationCause();
                case AgentFrameRejectionSiteV1.Capture: return CaptureCause();
                default: return null;
            }
        }

        /// <summary>Marks the instance for teardown. Returns false when it already was.</summary>
        internal bool RequestDestroy()
        {
            if (_destroyRequested) return false;
            _destroyRequested = true;
            return true;
        }

        /// <summary>
        /// Cancels active work with <see cref="BurstNodeAbortReason.TreeStopped"/>: opens an update at
        /// the last update's time when none is open, promotes any pending subtree cancellation to a
        /// whole-tree stop and runs the abort to completion. Does nothing when the tree never ran or
        /// already stopped. The caller holds its drive lock.
        /// </summary>
        internal void RunTeardown()
        {
            if (!_ready || !_hasExecuted) return;
            if (!_updateOpen && !BeginUpdate(_timeMicroseconds)) return;
            ResumeIfSuspended();
            // Beginning an eligible update can already queue reactive/timeout cancellation.
            // Teardown promotes that pending subtree cancellation to a whole-tree stop.
            if (_agents[0].Machine.TryRequestAbort(BurstNodeAbortReason.TreeStopped, out var failure, replacePendingForTreeStop: true))
                RunSegment(null, true);
            else Fail(failure, ProductionTreeAgentFailureDetail.CannotCancelDuringDestruction);
        }

        /// <summary>Marks disposal started. Returns false when it already was.</summary>
        internal bool BeginDispose()
        {
            if (_disposed) return false;
            _disposed = true;
            return true;
        }

        /// <summary>Releases every native allocation; the instance is stopped for good.</summary>
        internal void ReleaseStorage()
        {
            _ready = false;
            _recorder?.ReleaseWriter();
            if (TraceChannelOwner != null && TraceChannelOwner.State != NativeOwnerStateV1.Disposed)
                TraceChannelOwner.TryDispose(out _);
            if (_agents != null)
            {
                _agents[0].Dispose();
                _agents = null;
            }
            _dispatch = null;
            _generatedDispatch?.Dispose();
            _generatedDispatch = null;
            _effectQueue = null;
            if (_agentFrameTrace.IsCreated) _agentFrameTrace.Dispose();
            if (_agentFrameEffects.IsCreated) _agentFrameEffects.Dispose();
            if (_agentFrameEmptyBytes.IsCreated) _agentFrameEmptyBytes.Dispose();
            if (_agentFrameEmptyParallel.IsCreated) _agentFrameEmptyParallel.Dispose();
            _agentFrameTrace = default;
            _agentFrameEffects = default;
        }

        /// <summary>
        /// P10-009: turns this generated instance into a fresh instance of the same program with
        /// <paramref name="treeInstanceId"/>, reusing every allocation: machine instance state as
        /// created, blackboard defaults, every generated leaf's memory, ledger and activation state
        /// as created, update bookkeeping as after bootstrap. Random streams are left zeroed, as a
        /// fresh adapter leaves them; the caller seeds them. Refused while an update or dispatch is
        /// open; the caller tears active work down first (<see cref="RunTeardown"/>). P10-012: an attached
        /// trace (<see cref="TryAttachTrace"/>) stays attached; the container patches instance ids when it
        /// copies records. Only the simulation container calls this (hosts never reset an instance).
        /// </summary>
        internal bool TryResetInstance(TreeInstanceId treeInstanceId, out NativeRuntimeFailureV1 failure)
        {
            if (!_bootstrapped || _disposed || _generatedDispatch == null || _agents == null || !treeInstanceId.IsValid
                || _hasPendingStep)
            {
                failure = InvalidLifetime();
                return false;
            }
            if (!_agents[0].Machine.TryResetInstance(out failure)) return false;
            if (!_generatedDispatch.TryResetInstance(treeInstanceId))
            {
                failure = InvalidLifetime();
                return false;
            }
            _updateId = 0;
            _timeMicroseconds = 0;
            _updateOpen = false;
            _suspended = false;
            _hasExecuted = false;
            _stepsThisUpdate = 0;
            _pendingStep = default;
            _recorder?.ReleaseWriter();
            LastRootResult = null;
            LastFailure = default;
            LastRejectedResult = BurstContextResult.Success;
            InstanceId = treeInstanceId.Value;
            _destroyRequested = false;
            _ready = true;
            failure = default;
            return true;
        }
    }
}
