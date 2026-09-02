using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;

namespace AIBT
{
    internal enum NativeSameFramePhaseV1 : byte
    {
        Idle = 0,
        Snapshot = 1,
        ExecuteReady = 2,
        ExecuteScheduled = 3,
        Reduce = 4,
        Publish = 5,
        Disposed = 6,
    }

    internal readonly struct NativeSameFrameMetricsV1
    {
        internal NativeSameFrameMetricsV1(
            ulong updateId,
            ulong snapshotRevision,
            uint laneCount,
            uint executeRounds,
            ulong executedAtomicSteps)
        {
            UpdateId = updateId;
            SnapshotRevision = snapshotRevision;
            LaneCount = laneCount;
            ExecuteRounds = executeRounds;
            ExecutedAtomicSteps = executedAtomicSteps;
        }

        internal ulong UpdateId { get; }
        internal ulong SnapshotRevision { get; }
        internal uint LaneCount { get; }
        internal uint ExecuteRounds { get; }
        internal ulong ExecutedAtomicSteps { get; }
    }

    // Owns only scheduling/phase authority. Snapshot, Shared reduction, and output owners
    // remain the sole authorities for their data and are completed by the caller at the
    // corresponding explicit boundary.
    internal sealed class NativeSameFramePhaseControllerV1
    {
        private static readonly ProfilerMarker s_ScheduleExecuteRoundMarker =
            new ProfilerMarker("AIBT.Native.Scheduling.SameFrame.ScheduleExecuteRound");

        private NativeBatchedLifecycleOwnerV1 _execution;
        private NativeSameFramePhaseV1 _phase;
        private JobHandle _dependency;
        private ulong _lastUpdateId;
        private ulong _updateId;
        private ulong _snapshotRevision;
        private uint _laneCount;
        private uint _rounds;
        private ulong _steps;

        private NativeSameFramePhaseControllerV1() { }

        internal NativeSameFramePhaseV1 Phase => _phase;

        internal static bool TryCreate(
            NativeLifecycleMachineV1[] machines,
            Allocator allocator,
            out NativeSameFramePhaseControllerV1 controller,
            out NativeRuntimeFailureV1 failure)
        {
            controller = null;
            if (!NativeBatchedLifecycleOwnerV1.TryCreate(machines, allocator, out var execution, out failure))
                return false;
            controller = new NativeSameFramePhaseControllerV1
            {
                _execution = execution,
                _phase = NativeSameFramePhaseV1.Idle,
                _laneCount = checked((uint)machines.Length),
            };
            return true;
        }

        internal bool TryBeginSnapshot(ulong updateId, out NativeRuntimeFailureV1 failure)
        {
            if (_phase != NativeSameFramePhaseV1.Idle || updateId == 0 || updateId <= _lastUpdateId)
                return Fail(out failure);
            _updateId = updateId;
            _snapshotRevision = 0;
            _rounds = 0;
            _steps = 0;
            _phase = NativeSameFramePhaseV1.Snapshot;
            failure = default;
            return true;
        }

        internal bool TryCompleteSnapshot(ulong snapshotRevision, out NativeRuntimeFailureV1 failure)
        {
            if (_phase != NativeSameFramePhaseV1.Snapshot || snapshotRevision == 0)
                return Fail(out failure);
            _snapshotRevision = snapshotRevision;
            _phase = NativeSameFramePhaseV1.ExecuteReady;
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
            if (_phase != NativeSameFramePhaseV1.ExecuteReady)
                return Fail(out failure);
            if (!_execution.TrySchedule(batchSize, dependency, out scheduled, out failure)) return false;
            _dependency = scheduled;
            _phase = NativeSameFramePhaseV1.ExecuteScheduled;
            return true;
        }

        internal bool TryCompleteExecuteRound(
            NativeArray<NativeLifecycleStepResultV1> results,
            NativeArray<NativeRuntimeFailureV1> failures,
            out NativeRuntimeFailureV1 failure)
        {
            if (_phase != NativeSameFramePhaseV1.ExecuteScheduled)
                return Fail(out failure);
            _dependency.Complete();
            if (!_execution.TryComplete(results, failures, out failure))
            {
                _dependency = default;
                _phase = NativeSameFramePhaseV1.ExecuteReady;
                return false;
            }
            if (_rounds == uint.MaxValue || ulong.MaxValue - _steps < _laneCount)
            {
                _dependency = default;
                _phase = NativeSameFramePhaseV1.ExecuteReady;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.InstanceBudgetState);
                return false;
            }
            _rounds++;
            _steps += _laneCount;
            _dependency = default;
            _phase = NativeSameFramePhaseV1.ExecuteReady;
            return true;
        }

        internal bool TryAbortUpdate(out NativeRuntimeFailureV1 failure)
        {
            if (_phase == NativeSameFramePhaseV1.Idle
                || _phase == NativeSameFramePhaseV1.ExecuteScheduled
                || _phase == NativeSameFramePhaseV1.Disposed)
                return Fail(out failure);
            _lastUpdateId = _updateId;
            _updateId = 0;
            _snapshotRevision = 0;
            _rounds = 0;
            _steps = 0;
            _phase = NativeSameFramePhaseV1.Idle;
            failure = default;
            return true;
        }

        internal bool TrySealExecute(out NativeRuntimeFailureV1 failure)
        {
            if (_phase != NativeSameFramePhaseV1.ExecuteReady)
                return Fail(out failure);
            _phase = NativeSameFramePhaseV1.Reduce;
            failure = default;
            return true;
        }

        internal bool TryCompleteReduce(out NativeRuntimeFailureV1 failure)
        {
            if (_phase != NativeSameFramePhaseV1.Reduce)
                return Fail(out failure);
            _phase = NativeSameFramePhaseV1.Publish;
            failure = default;
            return true;
        }

        internal bool TryCompletePublish(
            out NativeSameFrameMetricsV1 metrics,
            out NativeRuntimeFailureV1 failure)
        {
            metrics = default;
            if (_phase != NativeSameFramePhaseV1.Publish)
                return Fail(out failure);
            metrics = new NativeSameFrameMetricsV1(
                _updateId, _snapshotRevision, _laneCount, _rounds, _steps);
            _lastUpdateId = _updateId;
            _updateId = 0;
            _snapshotRevision = 0;
            _phase = NativeSameFramePhaseV1.Idle;
            failure = default;
            return true;
        }

        internal bool TryDispose(out NativeRuntimeFailureV1 failure)
        {
            if (_phase != NativeSameFramePhaseV1.Idle || _execution == null)
                return Fail(out failure);
            if (!_execution.TryDispose(out failure)) return false;
            _execution = null;
            _phase = NativeSameFramePhaseV1.Disposed;
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
