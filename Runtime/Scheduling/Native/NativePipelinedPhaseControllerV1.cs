using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;

namespace AIBT
{
    internal enum NativePipelinedPhaseV1 : byte
    {
        Idle = 0,
        Snapshot = 1,
        ExecuteReady = 2,
        ExecuteScheduled = 3,
        Reduce = 4,
        Publish = 5,
        Disposed = 6,
    }

    internal readonly struct NativePipelineMetricsV1
    {
        internal NativePipelineMetricsV1(
            ulong updateId,
            ulong snapshotRevision,
            uint laneCount,
            uint executeRounds,
            ulong executedAtomicSteps,
            ulong stagesElapsed)
        {
            UpdateId = updateId;
            SnapshotRevision = snapshotRevision;
            LaneCount = laneCount;
            ExecuteRounds = executeRounds;
            ExecutedAtomicSteps = executedAtomicSteps;
            StagesElapsed = stagesElapsed;
        }

        internal ulong UpdateId { get; }
        internal ulong SnapshotRevision { get; }
        internal uint LaneCount { get; }
        internal uint ExecuteRounds { get; }
        internal ulong ExecutedAtomicSteps { get; }

        /// <summary>
        /// Explicit, caller-queryable pipeline delay: the number of <see cref="NativePipelinedPhaseControllerV1.TryAdvanceStage"/>
        /// boundaries that elapsed between this update's first scheduled round and its last
        /// completed round. Always at least 1 for a published update -- see
        /// <see cref="NativePipelinedPhaseControllerV1.TryCompleteExecuteRound"/>.
        /// </summary>
        internal ulong StagesElapsed { get; }
    }

    /// <summary>
    /// Owns only scheduling/phase authority, the same division of responsibility as
    /// <see cref="NativeSameFramePhaseControllerV1"/> (Snapshot, Shared reduction, and output
    /// owners remain the sole authorities for their data and are completed by the caller at the
    /// corresponding explicit boundary). The only semantic difference from that type is timing: a
    /// round's <see cref="TryCompleteExecuteRound"/> is refused until at least one
    /// <see cref="TryAdvanceStage"/> call has happened since the matching
    /// <see cref="TryScheduleExecuteRound"/>, so a result can never be silently collapsed to
    /// same-frame latency (<c>Documentation~/execution-and-scheduling.md</c>: "Pipelined and
    /// budgeted latency is visible and never silently selected"). Everything else -- the
    /// ownership guard that one instance is never scheduled twice concurrently (inherited
    /// unchanged from <see cref="NativeBatchedLifecycleOwnerV1"/>, not reimplemented here),
    /// Snapshot/Reduce/Publish phase division, and structured-diagnostic-on-misuse behavior -- is
    /// identical to the same-frame controller by construction, since both wrap the same owner
    /// type and share its `_state` guard.
    /// </summary>
    internal sealed class NativePipelinedPhaseControllerV1
    {
        private static readonly ProfilerMarker s_ScheduleExecuteRoundMarker =
            new ProfilerMarker("AIBT.Native.Scheduling.Pipelined.ScheduleExecuteRound");

        private NativeBatchedLifecycleOwnerV1 _execution;
        private NativePipelinedPhaseV1 _phase;
        private JobHandle _dependency;
        private ulong _lastUpdateId;
        private ulong _updateId;
        private ulong _snapshotRevision;
        private uint _laneCount;
        private uint _rounds;
        private ulong _steps;
        private ulong _currentStage;
        private ulong _scheduledAtStage;
        private ulong _firstScheduledStage;
        private ulong _lastCompletedStage;

        private NativePipelinedPhaseControllerV1() { }

        internal NativePipelinedPhaseV1 Phase => _phase;

        /// <summary>The controller's own frame/stage counter, advanced only by <see cref="TryAdvanceStage"/>.</summary>
        internal ulong CurrentStage => _currentStage;

        internal static bool TryCreate(
            NativeLifecycleMachineV1[] machines,
            Allocator allocator,
            out NativePipelinedPhaseControllerV1 controller,
            out NativeRuntimeFailureV1 failure)
        {
            controller = null;
            if (!NativeBatchedLifecycleOwnerV1.TryCreate(machines, allocator, out var execution, out failure))
                return false;
            controller = new NativePipelinedPhaseControllerV1
            {
                _execution = execution,
                _phase = NativePipelinedPhaseV1.Idle,
                _laneCount = checked((uint)machines.Length),
            };
            return true;
        }

        /// <summary>
        /// Advances the pipeline's explicit stage counter by one. The caller drives this exactly
        /// once per frame (or once per explicit pipeline stage it defines) -- never from
        /// wall-clock time; AIBT does not own timing. Legal from any phase except
        /// <see cref="NativePipelinedPhaseV1.Disposed"/>: a frame boundary passes whether or not
        /// this controller currently has a round scheduled.
        /// </summary>
        internal bool TryAdvanceStage(out NativeRuntimeFailureV1 failure)
        {
            if (_phase == NativePipelinedPhaseV1.Disposed) return Fail(out failure);
            if (_currentStage == ulong.MaxValue)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
            _currentStage++;
            failure = default;
            return true;
        }

        internal bool TryBeginSnapshot(ulong updateId, out NativeRuntimeFailureV1 failure)
        {
            if (_phase != NativePipelinedPhaseV1.Idle || updateId == 0 || updateId <= _lastUpdateId)
                return Fail(out failure);
            _updateId = updateId;
            _snapshotRevision = 0;
            _rounds = 0;
            _steps = 0;
            _firstScheduledStage = 0;
            _lastCompletedStage = 0;
            _phase = NativePipelinedPhaseV1.Snapshot;
            failure = default;
            return true;
        }

        internal bool TryCompleteSnapshot(ulong snapshotRevision, out NativeRuntimeFailureV1 failure)
        {
            if (_phase != NativePipelinedPhaseV1.Snapshot || snapshotRevision == 0)
                return Fail(out failure);
            _snapshotRevision = snapshotRevision;
            _phase = NativePipelinedPhaseV1.ExecuteReady;
            failure = default;
            return true;
        }

        internal bool TryScheduleExecuteRound(
            uint batchSize,
            JobHandle dependency,
            out JobHandle scheduled,
            out NativeRuntimeFailureV1 failure)
        {
            using var _ = s_ScheduleExecuteRoundMarker.Auto();
            scheduled = default;
            if (_phase != NativePipelinedPhaseV1.ExecuteReady)
                return Fail(out failure);
            if (!_execution.TrySchedule(batchSize, dependency, out scheduled, out failure)) return false;
            _dependency = scheduled;
            _scheduledAtStage = _currentStage;
            if (_rounds == 0) _firstScheduledStage = _currentStage;
            _phase = NativePipelinedPhaseV1.ExecuteScheduled;
            return true;
        }

        /// <summary>
        /// Completes the in-flight round. Refuses -- with a structured diagnostic, never a silent
        /// same-frame fallback -- unless at least one <see cref="TryAdvanceStage"/> call has
        /// happened since the matching <see cref="TryScheduleExecuteRound"/>.
        /// </summary>
        internal bool TryCompleteExecuteRound(
            NativeArray<NativeLifecycleStepResultV1> results,
            NativeArray<NativeRuntimeFailureV1> failures,
            out NativeRuntimeFailureV1 failure)
        {
            if (_phase != NativePipelinedPhaseV1.ExecuteScheduled)
                return Fail(out failure);
            if (_currentStage <= _scheduledAtStage)
                return Fail(out failure);
            _dependency.Complete();
            if (!_execution.TryComplete(results, failures, out failure))
            {
                if (!_execution.HasOutstandingOperation)
                {
                    _dependency = default;
                    _phase = NativePipelinedPhaseV1.ExecuteReady;
                }
                return false;
            }
            if (_rounds == uint.MaxValue || ulong.MaxValue - _steps < _laneCount)
            {
                _dependency = default;
                _phase = NativePipelinedPhaseV1.ExecuteReady;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.InstanceBudgetState);
                return false;
            }
            _rounds++;
            _steps += _laneCount;
            _lastCompletedStage = _currentStage;
            _dependency = default;
            _phase = NativePipelinedPhaseV1.ExecuteReady;
            return true;
        }

        internal bool TryAbortUpdate(out NativeRuntimeFailureV1 failure)
        {
            if (_phase == NativePipelinedPhaseV1.Idle
                || _phase == NativePipelinedPhaseV1.ExecuteScheduled
                || _phase == NativePipelinedPhaseV1.Disposed)
                return Fail(out failure);
            _lastUpdateId = _updateId;
            _updateId = 0;
            _snapshotRevision = 0;
            _rounds = 0;
            _steps = 0;
            _firstScheduledStage = 0;
            _lastCompletedStage = 0;
            _phase = NativePipelinedPhaseV1.Idle;
            failure = default;
            return true;
        }

        internal bool TrySealExecute(out NativeRuntimeFailureV1 failure)
        {
            if (_phase != NativePipelinedPhaseV1.ExecuteReady)
                return Fail(out failure);
            _phase = NativePipelinedPhaseV1.Reduce;
            failure = default;
            return true;
        }

        internal bool TryCompleteReduce(out NativeRuntimeFailureV1 failure)
        {
            if (_phase != NativePipelinedPhaseV1.Reduce)
                return Fail(out failure);
            _phase = NativePipelinedPhaseV1.Publish;
            failure = default;
            return true;
        }

        internal bool TryCompletePublish(
            out NativePipelineMetricsV1 metrics,
            out NativeRuntimeFailureV1 failure)
        {
            metrics = default;
            if (_phase != NativePipelinedPhaseV1.Publish)
                return Fail(out failure);
            metrics = new NativePipelineMetricsV1(
                _updateId, _snapshotRevision, _laneCount, _rounds, _steps,
                _lastCompletedStage - _firstScheduledStage);
            _lastUpdateId = _updateId;
            _updateId = 0;
            _snapshotRevision = 0;
            _phase = NativePipelinedPhaseV1.Idle;
            failure = default;
            return true;
        }

        internal bool TryDispose(out NativeRuntimeFailureV1 failure)
        {
            if (_phase != NativePipelinedPhaseV1.Idle || _execution == null)
                return Fail(out failure);
            if (!_execution.TryDispose(out failure)) return false;
            _execution = null;
            _phase = NativePipelinedPhaseV1.Disposed;
            return true;
        }

        private static bool Fail(out NativeRuntimeFailureV1 failure)
        {
            failure = new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                NativeResourceKindV1.LifecycleBatchLanes);
            return false;
        }
    }
}
