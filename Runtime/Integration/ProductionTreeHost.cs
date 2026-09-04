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

        /// <summary>The instance-owned trace channel, available until destruction.</summary>
        public NativeTraceChannelOwnerV1 TraceChannelOwner { get; private set; }
        /// <summary>Terminal root result, retained without automatic restart.</summary>
        public NodeStatus? LastRootResult { get; private set; }
        /// <summary>The failure that stopped this host; None before any failure.</summary>
        public NativeRuntimeFailureV1 LastFailure { get; private set; }
        /// <summary>Logical updates started, excluding budget-resume frames.</summary>
        public ulong TotalUpdates => _updateId;
        /// <summary>Null selects Immediate; otherwise limits native steps per frame. Zero pauses progress.</summary>
        public uint? StepBudget { get; set; }

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
            var instanceId = (ulong)Interlocked.Increment(ref s_nextTreeInstanceId);
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
            _bootstrapped = true;
            _ready = true;
            failure = default;
            return true;
        }

        private static long ReadScaledTime() => checked((long)(Time.timeAsDouble * 1000000.0));

        private void Update()
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
                _clock = null;
            }
        }
    }
}
