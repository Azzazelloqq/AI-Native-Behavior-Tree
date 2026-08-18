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
        {
            scheduled = default;
            if (_state != 1 || batchSize == 0 || batchSize > int.MaxValue)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                    NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
            var combined = dependency;
            for (var batchStart = 0; batchStart < _lanes.Length; batchStart += (int)batchSize)
            {
                var batchEnd = System.Math.Min(_lanes.Length, batchStart + (int)batchSize);
                for (var index = batchStart; index < batchEnd; index++)
                {
                    var lane = _lanes[index];
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
        {
            failure = default;
            if (_state != 2 || !results.IsCreated || !failures.IsCreated
                || results.Length != _lanes.Length || failures.Length != _lanes.Length)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                    NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
            _dependency.Complete();
            for (var index = 0; index < _lanes.Length; index++)
            {
                results[index] = _lanes[index].Result[0];
                failures[index] = _lanes[index].Failure[0];
                if (_lanes[index].Success[0] == 0 && failure.Code == NativeRuntimeDiagnosticCodeV1.None)
                    failure = failures[index];
            }
            _dependency = default;
            _state = 1;
            return failure.Code == NativeRuntimeDiagnosticCodeV1.None;
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
