using System;
using System.Threading;
using AIBT.Burst;
using AIBT.Runtime.Scheduling;
using Unity.Collections;
using UnityEngine;

namespace AIBT
{
    /// <summary>
    /// Drives one native tree in Unity Update with an optional per-frame step budget.
    /// Completion stops execution; disabling pauses; destruction cancels active work.
    /// Generated-node dispatch adapters remain caller-owned.
    /// </summary>
    public sealed class ProductionTreeHost : MonoBehaviour
    {
        /// <summary>Legacy Tick-only adapter. Use DispatchLifecycle for Actions with lifecycle effects.</summary>
        public delegate NodeStatus DispatchLeaf(uint runtimeNodeIndex);
        /// <summary>Executes a complete callback; status is consumed only for Tick.</summary>
        public delegate BurstContextResult DispatchLifecycle(in DispatchRequest request, out NodeStatus status);

        /// <summary>Callback identity and frozen update inputs. Reasons apply to their matching phase.</summary>
        public readonly struct DispatchRequest
        {
            internal DispatchRequest(NativeLifecycleStepResultV1 step, ulong updateId, long timeMicroseconds)
            {
                NodeIndex = step.NodeIndex;
                Phase = step.Phase;
                UpdateId = updateId;
                TimeMicroseconds = timeMicroseconds;
                ExitReason = step.ExitReason;
                AbortReason = step.AbortReason;
            }
            public uint NodeIndex { get; }
            public BurstCallbackPhase Phase { get; }
            public ulong UpdateId { get; }
            public long TimeMicroseconds { get; }
            public BurstNodeExitReason ExitReason { get; }
            public BurstNodeAbortReason AbortReason { get; }
        }

        private static long s_nextTreeInstanceId;
        private SchedulingAgent[] _agents;
        private DispatchLifecycle _dispatch;
        private GeneratedTreeDispatchAdapterV2 _generatedDispatch;
        private Func<long> _clock;
        private NativeTraceRecorderV1 _recorder;
        private ulong _updateId;
        private long _timeMicroseconds;
        private bool _ready;
        private bool _bootstrapped;
        private bool _updateOpen;
        private bool _suspended;
        private bool _hasExecuted;
        private bool _driving;
        private bool _destroyRequested;
        private bool _disposed;
        private ProductionTreeScheduler _owner;
        private NativeLifecycleStepResultV1 _pendingStep;
        private bool _hasPendingStep;

        /// <summary>The instance-owned trace channel, available until destruction.</summary>
        public NativeTraceChannelOwnerV1 TraceChannelOwner { get; private set; }
        /// <summary>Terminal root result, retained without automatic restart.</summary>
        public NodeStatus? LastRootResult { get; private set; }
        /// <summary>The failure that stopped this host; None before any failure.</summary>
        public NativeRuntimeFailureV1 LastFailure { get; private set; }
        /// <summary>Logical updates started, excluding budget-resume frames.</summary>
        public ulong TotalUpdates => _updateId;
        /// <summary>Stable per-instance identity assigned at bootstrap; 0 before bootstrap. Used for deterministic coordinator ordering (<see cref="ProductionTreeScheduler"/>) -- never reused across instances within a process.</summary>
        public ulong InstanceId { get; private set; }
        /// <summary>Null selects Immediate; otherwise limits native steps per frame. Zero pauses progress.</summary>
        public uint? StepBudget { get; set; }

        /// <summary>
        /// This host's own generated-catalog dispatch bridge, if bootstrapped through the generated
        /// overload; null for the legacy delegate-based overloads. A <see cref="ProductionTreeScheduler"/>
        /// group wave uses this to fold this host's own pending request into a shared batch -- never
        /// exposed for any other purpose.
        /// </summary>
        internal GeneratedTreeDispatchAdapterV2 GeneratedDispatchAdapter => _generatedDispatch;

        /// <summary>
        /// This host's own machine value -- a bundle of <c>NativeArray</c> handles, not a snapshot;
        /// any copy observes the same underlying native state. For a
        /// <see cref="ProductionTreeScheduler"/> group driver to schedule this host's own
        /// <c>TryAdvance</c> as part of a shared <c>NativeBatchedLifecycleOwnerV1</c> Job, between
        /// a <see cref="TryBeginBatchedRound"/> and this host's next barrier.
        /// </summary>
        internal NativeLifecycleMachineV1 Machine => _agents[0].Machine;

        /// <summary>Bootstraps a Tick-only integration with scaled Unity time and no-op lifecycle callbacks.</summary>
        public bool TryBootstrap(CompiledProgram program, DispatchLeaf dispatch,
            NativeTraceChannelCapacityV1 traceCapacity, out NativeRuntimeFailureV1 failure)
        {
            if (dispatch == null) throw new ArgumentNullException(nameof(dispatch));
            BurstContextResult Adapter(in DispatchRequest request, out NodeStatus status)
            {
                status = request.Phase == BurstCallbackPhase.Tick ? dispatch(request.NodeIndex) : NodeStatus.Running;
                return BurstContextResult.Success;
            }
            return TryBootstrap(program, Adapter, traceCapacity, null, out failure);
        }

        /// <summary>
        /// Bootstraps once with full lifecycle dispatch. A null clock uses scaled Unity time.
        /// Custom clocks return nonnegative, nondecreasing microseconds, read once per logical
        /// update. Clock and dispatch adapters execute synchronously on the main thread.
        /// </summary>
        public bool TryBootstrap(CompiledProgram program, DispatchLifecycle dispatch,
            NativeTraceChannelCapacityV1 traceCapacity, Func<long> clock, out NativeRuntimeFailureV1 failure)
            => TryBootstrapCore(
                program, dispatch, traceCapacity, clock,
                new TreeInstanceId((ulong)Interlocked.Increment(ref s_nextTreeInstanceId)), out failure);

        /// <summary>
        /// Bootstraps production-owned generated dispatch. The catalog supplies every callback
        /// and layout; no caller-authored dispatch delegate or byte offset is accepted.
        /// </summary>
        public bool TryBootstrap(
            GeneratedTreeRuntimeDefinitionV2 definition,
            GeneratedBurstCatalogV2 catalog,
            NativeTraceChannelCapacityV1 traceCapacity,
            Func<long> clock,
            out NativeRuntimeFailureV1 failure)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (_bootstrapped || _disposed || _driving)
            {
                failure = InvalidLifetime();
                return false;
            }
            var treeInstanceId = new TreeInstanceId((ulong)Interlocked.Increment(ref s_nextTreeInstanceId));
            if (!GeneratedTreeDispatchAdapterV2.TryCreate(
                    definition, catalog, treeInstanceId, out var adapter, out var contextFailure))
            {
                failure = new NativeRuntimeFailureV1(
                    contextFailure == BurstContextResult.CapacityExceeded
                        ? NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded
                        : NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid);
                return false;
            }
            if (!TryBootstrapCore(
                    definition.Binding.SemanticProgram, adapter.Dispatch,
                    traceCapacity, clock, treeInstanceId, out failure))
            {
                adapter.Dispose();
                return false;
            }
            _generatedDispatch = adapter;
            return true;
        }

        private bool TryBootstrapCore(
            CompiledProgram program,
            DispatchLifecycle dispatch,
            NativeTraceChannelCapacityV1 traceCapacity,
            Func<long> clock,
            TreeInstanceId treeInstanceId,
            out NativeRuntimeFailureV1 failure)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (dispatch == null) throw new ArgumentNullException(nameof(dispatch));
            if (_bootstrapped || _disposed || _driving)
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
            // BudgetYielded/ExecutionResumed are Detailed events in the existing trace contract.
            if (!NativeTraceChannelOwnerV1.TryCreate(traceCapacity, NativeTraceLevelV1.Detailed,
                    new TreeInstanceId(instanceId), 0, Allocator.Persistent, out var owner, out var traceFailure))
            {
                _agents[0].Dispose();
                _agents = null;
                failure = new NativeRuntimeFailureV1(traceFailure.Code);
                return false;
            }
            _dispatch = dispatch;
            _clock = clock ?? ReadScaledTime;
            TraceChannelOwner = owner;
            _recorder = new NativeTraceRecorderV1(owner, new NativeHash256V1(program.Header.CompiledContentHash), instanceId);
            InstanceId = instanceId;
            _bootstrapped = true;
            _ready = true;
            failure = default;
            return true;
        }

        private static long ReadScaledTime() => checked((long)(Time.timeAsDouble * 1000000.0));

        /// <summary>
        /// True once a <see cref="ProductionTreeScheduler"/> owns this host's drive loop. While
        /// owned, Unity's own per-frame <c>Update</c> message no-ops; the coordinator calls
        /// <see cref="DriveOneUpdate"/> itself. Ownership never duplicates or transfers native
        /// state -- this host remains the sole owner of its machine, trace channel and disposal.
        /// </summary>
        public bool IsCoordinatorOwned => _owner != null;

        internal bool TryRegisterOwner(ProductionTreeScheduler owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (_owner != null && _owner != owner) return false;
            _owner = owner;
            return true;
        }

        internal void ClearOwner(ProductionTreeScheduler owner)
        {
            if (_owner == owner) _owner = null;
        }

        private void Update()
        {
            if (_owner != null) return; // driven by the coordinator instead, see DriveOneUpdate
            DriveOneUpdate();
        }

        /// <summary>
        /// One drive step -- identical body to the standalone per-frame <c>Update</c> message, and
        /// the only entry point <see cref="ProductionTreeScheduler"/> uses once this host is
        /// registered. Never called directly by application code; use a coordinator or let this
        /// host drive itself standalone.
        /// </summary>
        internal void DriveOneUpdate()
        {
            if (!_ready || !isActiveAndEnabled || _disposed) return;
            if (_driving)
            {
                Fail(InvalidLifetime(), "Reentrant execution is not supported.");
                return;
            }
            _driving = true;
            try
            {
                if (!_updateOpen)
                {
                    var now = _clock();
                    if (!_ready || _destroyRequested) return;
                    if (now < 0 || (_updateId != 0 && now < _timeMicroseconds))
                    {
                        Fail(InvalidLifetime(), "Clock must return nonnegative, nondecreasing microseconds.");
                        return;
                    }
                    if (!BeginUpdate(now)) return;
                }
                else if (_suspended)
                {
                    _recorder.RecordExecutionResumed(_updateId);
                    _suspended = false;
                }
                RunSegment(StepBudget, false);
            }
            catch (Exception exception)
            {
                Fail(InvalidLifetime(), exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                _driving = false;
                if (_destroyRequested) DisposeHost();
            }
        }

        /// <summary>
        /// True while this host has advanced to a dispatch requirement not yet resolved via
        /// <see cref="CompletePendingDispatch"/> -- a <see cref="ProductionTreeScheduler"/> wave in
        /// flight. A second overlapping wave step is refused, matching the existing single-driver
        /// invariant this host has always held (<see cref="_driving"/>).
        /// </summary>
        internal bool HasPendingDispatch => _hasPendingStep;

        /// <summary>
        /// Advances until the next dispatch requirement -- returned unresolved for a caller-owned
        /// wave batch, never auto-resolved -- or the update segment reaches Completed/Waiting.
        /// Reserved for <see cref="ProductionTreeScheduler"/>'s own generated-catalog group
        /// dispatch; unlike <see cref="DriveOneUpdate"/>, the caller alone decides how the returned
        /// request is resolved and must call <see cref="CompletePendingDispatch"/> exactly once
        /// before advancing this host again. Never mixed with <see cref="DriveOneUpdate"/> on the
        /// same host in the same update -- both share the identical reentrancy guard.
        /// </summary>
        internal bool TryAdvanceToNextDispatch(out DispatchRequest request)
        {
            request = default;
            if (!_ready || !isActiveAndEnabled || _disposed || _destroyRequested || _hasPendingStep) return false;
            if (_driving)
            {
                Fail(InvalidLifetime(), "Reentrant execution is not supported.");
                return false;
            }
            _driving = true;
            var resolvedWithinThisCall = false;
            try
            {
                if (!_updateOpen)
                {
                    var now = _clock();
                    if (!_ready || _destroyRequested) return false;
                    if (now < 0 || (_updateId != 0 && now < _timeMicroseconds))
                    {
                        Fail(InvalidLifetime(), "Clock must return nonnegative, nondecreasing microseconds.");
                        return false;
                    }
                    if (!BeginUpdate(now)) return false;
                }
                else if (_suspended)
                {
                    _recorder.RecordExecutionResumed(_updateId);
                    _suspended = false;
                }
                var found = AdvanceOneStepForGroupedDispatch(out request);
                resolvedWithinThisCall = !_hasPendingStep;
                return found;
            }
            catch (Exception exception)
            {
                Fail(InvalidLifetime(), exception.GetType().Name + ": " + exception.Message);
                resolvedWithinThisCall = true;
                return false;
            }
            finally
            {
                // A pending step deliberately keeps _driving held across this call's own return --
                // released only once CompletePendingDispatch resolves it, mirroring DriveOneUpdate's
                // own hold for the whole (synchronous, here caller-paced) duration of one dispatch.
                if (resolvedWithinThisCall)
                {
                    _driving = false;
                    if (_destroyRequested) DisposeHost();
                }
            }
        }

        private bool AdvanceOneStepForGroupedDispatch(out DispatchRequest request)
        {
            // NativeLifecycleStepKindV1 has several internal-progress kinds (CompositeEntered,
            // ChildSelected, ...) that carry nothing for a caller to act on -- exactly what
            // RunSegment's own while loop already keeps calling TryAdvance through. A wave barrier
            // is reached only at DispatchRequired (return it unresolved) or Completed/Waiting
            // (nothing pending this update); every other kind must keep advancing right here.
            request = default;
            while (_ready)
            {
                if (!_agents[0].Machine.TryAdvance(out var step, out var failure))
                {
                    Fail(failure, "Cannot advance execution.");
                    return false;
                }
                _hasExecuted = true;
                _recorder.RecordStep(_updateId, step);
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    _pendingStep = step;
                    _hasPendingStep = true;
                    request = new DispatchRequest(step, _updateId, _timeMicroseconds);
                    return true;
                }
                if (step.Kind == NativeLifecycleStepKindV1.Completed || step.Kind == NativeLifecycleStepKindV1.Waiting)
                {
                    _updateOpen = false;
                    _recorder.RecordUpdateEnded(_updateId, step.HasRootStatus, step.RootStatus);
                    if (step.Kind == NativeLifecycleStepKindV1.Completed)
                    {
                        if (step.HasRootStatus) LastRootResult = step.RootStatus;
                        _ready = false;
                    }
                    break;
                }
            }
            return false;
        }

        /// <summary>What one externally-supplied lifecycle step meant for a <see cref="ProductionTreeScheduler"/>'s own BatchedJobsSameFrame/PipelinedJobs group round.</summary>
        internal enum BatchedStepOutcome : byte
        {
            /// <summary>An internal-progress step; this host stays open for the group driver's next round.</summary>
            Continue = 0,
            /// <summary>A dispatch requirement was reached; resolve it via <see cref="TryResolveSinglePendingDispatch"/> or a <see cref="GeneratedDispatchGroupExecutorV2"/> batch, then this host is open again.</summary>
            DispatchRequired = 1,
            /// <summary>The update reached Completed/Waiting; call <see cref="ReleaseBatchedDrive"/> and drop this host from the round.</summary>
            Terminal = 2,
        }

        /// <summary>
        /// Opens this host's next logical update (or resumes a suspended one) and holds its drive
        /// lock across every subsequent <see cref="TryHandleBatchedStepResult"/> call until a
        /// barrier is reached -- for a <see cref="ProductionTreeScheduler"/> group driver that
        /// advances this host's own machine externally through a <c>NativeBatchedLifecycleOwnerV1</c>
        /// Job shared with other agents, rather than this host calling <c>TryAdvance</c> itself.
        /// <paramref name="included"/> is false, with no failure, when this host is simply not
        /// eligible right now (already terminal, disabled, destroying); a genuine misuse (already
        /// driving, a bad clock) fails and returns false. The caller must schedule the returned
        /// <paramref name="machine"/> for exactly one <c>TryAdvance</c> per round until a barrier.
        /// </summary>
        internal bool TryBeginBatchedRound(out bool included, out NativeLifecycleMachineV1 machine)
        {
            included = false;
            machine = default;
            if (_disposed || _destroyRequested || !_ready || !isActiveAndEnabled || _hasPendingStep)
                return true; // not eligible this round -- not a caller error.
            if (_driving)
            {
                Fail(InvalidLifetime(), "Reentrant execution is not supported.");
                return false;
            }
            _driving = true;
            try
            {
                if (!_updateOpen)
                {
                    var now = _clock();
                    if (!_ready || _destroyRequested) { included = false; return true; }
                    if (now < 0 || (_updateId != 0 && now < _timeMicroseconds))
                    {
                        Fail(InvalidLifetime(), "Clock must return nonnegative, nondecreasing microseconds.");
                        return false;
                    }
                    if (!BeginUpdate(now)) return false;
                }
                else if (_suspended)
                {
                    _recorder.RecordExecutionResumed(_updateId);
                    _suspended = false;
                }
                machine = _agents[0].Machine;
                included = true;
                return true;
            }
            catch (Exception exception)
            {
                Fail(InvalidLifetime(), exception.GetType().Name + ": " + exception.Message);
                return false;
            }
            finally
            {
                if (!included) _driving = false;
            }
        }

        /// <summary>
        /// Applies one externally-advanced lifecycle step (already produced by the group driver's
        /// own batched Job) to this host's own recorder/terminal bookkeeping -- the exact same
        /// per-step handling <see cref="AdvanceOneStepForGroupedDispatch"/> already applies, minus
        /// the loop and the <c>TryAdvance</c> call itself, since the step was advanced elsewhere.
        /// Must only be called between a <see cref="TryBeginBatchedRound"/> that returned
        /// <c>included: true</c> (or a prior round's own <see cref="BatchedStepOutcome.Continue"/>)
        /// and this host's next barrier.
        /// </summary>
        internal BatchedStepOutcome TryHandleBatchedStepResult(NativeLifecycleStepResultV1 step, out DispatchRequest request)
        {
            request = default;
            _hasExecuted = true;
            _recorder.RecordStep(_updateId, step);
            if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
            {
                _pendingStep = step;
                _hasPendingStep = true;
                request = new DispatchRequest(step, _updateId, _timeMicroseconds);
                return BatchedStepOutcome.DispatchRequired;
            }
            if (step.Kind == NativeLifecycleStepKindV1.Completed || step.Kind == NativeLifecycleStepKindV1.Waiting)
            {
                _updateOpen = false;
                _recorder.RecordUpdateEnded(_updateId, step.HasRootStatus, step.RootStatus);
                if (step.Kind == NativeLifecycleStepKindV1.Completed)
                {
                    if (step.HasRootStatus) LastRootResult = step.RootStatus;
                    _ready = false;
                }
                return BatchedStepOutcome.Terminal;
            }
            return BatchedStepOutcome.Continue;
        }

        /// <summary>
        /// Resolves this host's own currently-pending dispatch requirement through its normal
        /// single-instance callback (the same call <see cref="RunSegment"/> already makes) --
        /// for a group driver's round with no other same-catalog member to batch with this frame.
        /// </summary>
        internal bool TryResolveSinglePendingDispatch()
        {
            if (!_hasPendingStep) return false;
            var request = new DispatchRequest(_pendingStep, _updateId, _timeMicroseconds);
            var result = _dispatch(in request, out var status);
            if (!_ready) return false; // A reentrant call may already have faulted the host.
            return CompletePendingDispatch(result, status);
        }

        /// <summary>
        /// Reacquires the externally-driven lifecycle lock after a grouped dispatch completion
        /// released it. This makes the cross-round ownership invariant explicit for both the
        /// same-frame and pipelined production drivers.
        /// </summary>
        internal bool TryResumeBatchedDriveAfterDispatch()
        {
            if (_disposed || _destroyRequested || !_ready || !isActiveAndEnabled || _hasPendingStep)
                return false;
            if (_driving)
            {
                Fail(InvalidLifetime(), "Reentrant execution is not supported.");
                return false;
            }
            _driving = true;
            return true;
        }

        /// <summary>Releases the drive lock <see cref="TryBeginBatchedRound"/> took for a host whose round ended at <see cref="BatchedStepOutcome.Terminal"/> (no dispatch was pending, so <see cref="CompletePendingDispatch"/> never runs to release it).</summary>
        internal void ReleaseBatchedDrive()
        {
            _driving = false;
            if (_destroyRequested) DisposeHost();
        }

        /// <summary>
        /// Structured, never-silent rejection for a <see cref="SchedulingProfile.ForcedPolicy"/> the
        /// coordinator cannot honor this frame (no generated catalog to batch, no
        /// <see cref="SchedulerJobsCapabilities"/> configured, or <c>NativeAutoSelectionV1.TrySelect</c>
        /// itself rejected the forced policy) -- never a silent fallback to a different policy.
        /// </summary>
        internal void FailUnsupportedForcedPolicy()
        {
            Fail(
                new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid),
                "Forced scheduling policy is not supported by this coordinator's own current configuration.");
        }

        internal void FailExternalScheduling(NativeRuntimeFailureV1 failure)
        {
            Fail(failure, "The coordinator's externally-driven scheduling round failed.");
        }

        /// <summary>
        /// Resolves the single dispatch requirement <see cref="TryAdvanceToNextDispatch"/> most
        /// recently returned. Returns false, without mutating the machine, if no wave step is
        /// currently pending (a caller error) or the host already faulted reentrantly.
        /// </summary>
        internal bool CompletePendingDispatch(BurstContextResult result, NodeStatus status)
        {
            if (!_hasPendingStep) return false;
            var step = _pendingStep;
            _hasPendingStep = false;
            try
            {
                if (!_ready) return false; // A reentrant call may already have faulted the host.
                if (!_agents[0].Machine.TryCompleteDispatch(step.DispatchToken, result, status, out var failure))
                {
                    Fail(failure, "Callback rejected: " + result);
                    return false;
                }
                _recorder.RecordDispatchCompletion(_updateId, step, status);
                return true;
            }
            finally
            {
                _driving = false;
                if (_destroyRequested) DisposeHost();
            }
        }

        private bool BeginUpdate(long timeMicroseconds)
        {
            if (_updateId == ulong.MaxValue)
            {
                Fail(new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow), "Update ID overflow.");
                return false;
            }
            var nextId = _updateId + 1;
            if (!_agents[0].Machine.TryBeginUpdate(nextId, timeMicroseconds, out var failure))
            {
                Fail(failure, "Cannot begin update.");
                return false;
            }
            _updateId = nextId;
            _timeMicroseconds = timeMicroseconds;
            _updateOpen = true;
            _recorder.RecordUpdateStarted(_updateId);
            return true;
        }

        private void RunSegment(uint? limit, bool destroying)
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
                        _recorder.RecordBudgetYielded(_updateId);
                        return;
                    }
                }
                else advanced = _agents[0].Machine.TryAdvance(out step, out failure);
                if (!advanced)
                {
                    Fail(failure, "Cannot advance execution.");
                    return;
                }
                _hasExecuted = true;
                _recorder.RecordStep(_updateId, step);
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    var request = new DispatchRequest(step, _updateId, _timeMicroseconds);
                    var result = _dispatch(in request, out var status);
                    if (!_ready) return; // A reentrant call may already have faulted the host.
                    if (!_agents[0].Machine.TryCompleteDispatch(step.DispatchToken, result, status, out failure))
                    {
                        Fail(failure, "Callback rejected: " + result);
                        return;
                    }
                    _recorder.RecordDispatchCompletion(_updateId, step, status);
                }
                else if (step.Kind == NativeLifecycleStepKindV1.Completed || step.Kind == NativeLifecycleStepKindV1.Waiting)
                {
                    _updateOpen = false;
                    _recorder.RecordUpdateEnded(_updateId, step.HasRootStatus, step.RootStatus);
                    if (step.Kind == NativeLifecycleStepKindV1.Completed)
                    {
                        if (step.HasRootStatus) LastRootResult = step.RootStatus;
                        _ready = false;
                    }
                    return;
                }
            }
        }

        private static NativeRuntimeFailureV1 InvalidLifetime()
            => new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid);

        private void Fail(NativeRuntimeFailureV1 failure, string detail)
        {
            if (LastFailure.Code != NativeRuntimeDiagnosticCodeV1.None) return;
            LastFailure = failure;
            _ready = false;
            _recorder?.ReleaseWriter();
            Debug.LogError("AIBT ProductionTreeHost: " + failure.Code + ": " + detail, this);
        }

        private void OnDestroy()
        {
            _destroyRequested = true;
            _owner?.TryUnregister(this);
            if (!_driving) DisposeHost();
        }

        private void DisposeHost()
        {
            if (_disposed) return;
            _disposed = true;
            _driving = true;
            try
            {
                if (_ready && _hasExecuted)
                {
                    if (!_updateOpen && !BeginUpdate(_timeMicroseconds)) return;
                    if (_suspended)
                    {
                        _recorder.RecordExecutionResumed(_updateId);
                        _suspended = false;
                    }
                    // Beginning an eligible update can already queue reactive/timeout cancellation.
                    // Teardown promotes that pending subtree cancellation to a whole-tree stop.
                    if (_agents[0].Machine.TryRequestAbort(BurstNodeAbortReason.TreeStopped, out var failure, replacePendingForTreeStop: true))
                        RunSegment(null, true);
                    else Fail(failure, "Cannot cancel active work during destruction.");
                }
            }
            catch (Exception exception)
            {
                Fail(InvalidLifetime(), exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                _ready = false;
                _driving = false;
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
                _clock = null;
            }
        }
    }
}
