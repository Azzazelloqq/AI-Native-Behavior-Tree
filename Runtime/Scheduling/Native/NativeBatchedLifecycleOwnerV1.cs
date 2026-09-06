using AIBT.Burst;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT
{
    internal sealed class NativeBatchedLifecycleOwnerV1
    {
        private Lane[] _lanes;
        private JobHandle _dependency;
        private byte _state;

        private NativeBatchedLifecycleOwnerV1() { }

        // Completion can reject its inputs without consuming the scheduled operation.
        internal bool HasOutstandingOperation => _state == 2;

        internal static bool TryCreate(
            NativeLifecycleMachineV1[] machines,
            Allocator allocator,
            out NativeBatchedLifecycleOwnerV1 owner,
            out NativeRuntimeFailureV1 failure)
        {
            owner = null;
            if (machines == null || machines.Length == 0 || allocator != Allocator.Persistent)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
            for (var index = 0; index < machines.Length; index++)
            {
                var ownerId = machines[index].SchedulingOwnerId;
                if (ownerId == 0)
                {
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                        NativeResourceKindV1.LifecycleBatchLanes);
                    return false;
                }
                for (var previous = 0; previous < index; previous++)
                    if (machines[previous].SchedulingOwnerId == ownerId)
                    {
                        failure = new NativeRuntimeFailureV1(
                            NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                            NativeResourceKindV1.LifecycleBatchLanes);
                        return false;
                    }
            }
            var lanes = new Lane[machines.Length];
            try
            {
                for (var index = 0; index < machines.Length; index++)
                {
                    var optionalStorage = new NativeArray<byte>(0, allocator, NativeArrayOptions.ClearMemory);
                    var optionalParallelStorage = new NativeArray<NativeParallelBranchStateV1>(0, allocator, NativeArrayOptions.ClearMemory);
                    var machine = machines[index];
                    if (!machine.TryAttachCreatedEmptyJobStorage(optionalStorage, optionalParallelStorage))
                    {
                        optionalParallelStorage.Dispose();
                        optionalStorage.Dispose();
                    }
                    lanes[index] = new Lane
                    {
                        Machine = machine,
                        OptionalStorage = optionalStorage,
                        OptionalParallelStorage = optionalParallelStorage,
                        Result = new NativeArray<NativeLifecycleStepResultV1>(1, allocator, NativeArrayOptions.ClearMemory),
                        Failure = new NativeArray<NativeRuntimeFailureV1>(1, allocator, NativeArrayOptions.ClearMemory),
                        Success = new NativeArray<byte>(1, allocator, NativeArrayOptions.ClearMemory),
                    };
                }
                owner = new NativeBatchedLifecycleOwnerV1 { _lanes = lanes, _state = 1 };
                failure = default;
                return true;
            }
            catch
            {
                for (var index = lanes.Length - 1; index >= 0; index--) lanes[index].Dispose();
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
        }

        internal bool TrySchedule(
            uint batchSize,
            JobHandle dependency,
            out JobHandle scheduled,
            out NativeRuntimeFailureV1 failure)
            => TrySchedule(batchSize, null, _lanes == null ? 0 : _lanes.Length, dependency, out scheduled, out failure);

        /// <summary>
        /// Schedules one advance for each explicitly active lane. The lane array remains owned by
        /// this instance for its full lifetime; callers may change the active subset only between
        /// completed operations. This lets a production pipeline remove terminal or dispatch-bound
        /// machines without recreating their native storage every round.
        /// </summary>
        internal bool TrySchedule(
            uint batchSize,
            int[] activeLaneIds,
            int activeLaneCount,
            JobHandle dependency,
            out JobHandle scheduled,
            out NativeRuntimeFailureV1 failure)
        {
            scheduled = default;
            if (_state != 1 || batchSize == 0 || batchSize > int.MaxValue
                || !ValidateActiveLanes(activeLaneIds, activeLaneCount))
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                    NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
            var combined = dependency;
            for (var batchStart = 0; batchStart < activeLaneCount; batchStart += (int)batchSize)
            {
                var batchEnd = System.Math.Min(activeLaneCount, batchStart + (int)batchSize);
                for (var index = batchStart; index < batchEnd; index++)
                {
                    var laneId = activeLaneIds == null ? index : activeLaneIds[index];
                    var lane = _lanes[laneId];
                    lane.Success[0] = 0;
                    lane.Failure[0] = default;
                    var job = new AdvanceJob
                    {
                        Machine = lane.Machine,
                        Result = lane.Result,
                        Failure = lane.Failure,
                        Success = lane.Success,
                    };
                    combined = JobHandle.CombineDependencies(combined, job.Schedule(dependency));
                }
            }
            _dependency = combined;
            _state = 2;
            scheduled = combined;
            failure = default;
            return true;
        }

        internal bool TryComplete(
            NativeArray<NativeLifecycleStepResultV1> results,
            NativeArray<NativeRuntimeFailureV1> failures,
            out NativeRuntimeFailureV1 failure)
            => TryComplete(results, failures, null, _lanes == null ? 0 : _lanes.Length, out failure);

        /// <summary>Completes the currently scheduled active subset in the caller's active-lane order.</summary>
        internal bool TryComplete(
            NativeArray<NativeLifecycleStepResultV1> results,
            NativeArray<NativeRuntimeFailureV1> failures,
            int[] activeLaneIds,
            int activeLaneCount,
            out NativeRuntimeFailureV1 failure)
        {
            failure = default;
            if (_state != 2 || !results.IsCreated || !failures.IsCreated
                || (activeLaneIds == null
                    ? results.Length != activeLaneCount || failures.Length != activeLaneCount
                    : results.Length < activeLaneCount || failures.Length < activeLaneCount)
                || !ValidateActiveLanes(activeLaneIds, activeLaneCount))
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                    NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
            _dependency.Complete();
            for (var index = 0; index < activeLaneCount; index++)
            {
                var laneId = activeLaneIds == null ? index : activeLaneIds[index];
                var lane = _lanes[laneId];
                results[index] = lane.Result[0];
                failures[index] = lane.Failure[0];
                if (lane.Success[0] == 0 && failure.Code == NativeRuntimeDiagnosticCodeV1.None)
                    failure = failures[index];
            }
            _dependency = default;
            _state = 1;
            return failure.Code == NativeRuntimeDiagnosticCodeV1.None;
        }

        private bool ValidateActiveLanes(int[] activeLaneIds, int activeLaneCount)
        {
            if (_lanes == null || activeLaneCount <= 0 || activeLaneCount > _lanes.Length)
                return false;
            if (activeLaneIds == null) return activeLaneCount == _lanes.Length;
            if (activeLaneIds.Length < activeLaneCount) return false;
            for (var index = 0; index < activeLaneCount; index++)
            {
                var laneId = activeLaneIds[index];
                if (laneId < 0 || laneId >= _lanes.Length) return false;
                for (var previous = 0; previous < index; previous++)
                    if (activeLaneIds[previous] == laneId) return false;
            }
            return true;
        }

        internal bool TryDispose(out NativeRuntimeFailureV1 failure)
        {
            if (_state != 1)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation,
                    NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
            for (var index = _lanes.Length - 1; index >= 0; index--) _lanes[index].Dispose();
            _lanes = null;
            _state = 3;
            failure = default;
            return true;
        }

        [BurstCompile]
        private struct AdvanceJob : IJob
        {
            internal NativeLifecycleMachineV1 Machine;
            internal NativeArray<NativeLifecycleStepResultV1> Result;
            internal NativeArray<NativeRuntimeFailureV1> Failure;
            internal NativeArray<byte> Success;

            public void Execute()
            {
                Success[0] = Machine.TryAdvance(out var result, out var failure) ? (byte)1 : (byte)0;
                Result[0] = result;
                Failure[0] = failure;
            }
        }

        private struct Lane
        {
            internal NativeLifecycleMachineV1 Machine;
            internal NativeArray<NativeLifecycleStepResultV1> Result;
            internal NativeArray<NativeRuntimeFailureV1> Failure;
            internal NativeArray<byte> Success;
            internal NativeArray<byte> OptionalStorage;
            internal NativeArray<NativeParallelBranchStateV1> OptionalParallelStorage;
            internal void Dispose()
            {
                if (Success.IsCreated) Success.Dispose();
                if (Failure.IsCreated) Failure.Dispose();
                if (Result.IsCreated) Result.Dispose();
                if (OptionalStorage.IsCreated) OptionalStorage.Dispose();
                if (OptionalParallelStorage.IsCreated) OptionalParallelStorage.Dispose();
            }
        }
    }
}
