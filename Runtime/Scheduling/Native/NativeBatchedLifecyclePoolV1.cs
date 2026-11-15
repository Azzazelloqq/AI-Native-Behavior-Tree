using System;
using System.Collections.Generic;
using AIBT.Burst;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AIBT
{
    /// <summary>
    /// P10-002: reusable storage for BatchedJobsSameFrame lifecycle rounds. Advances every bound
    /// machine by exactly one <see cref="NativeLifecycleMachineV1.TryAdvance"/> per round, as
    /// <see cref="NativeBatchedLifecycleOwnerV1"/> does, but keeps its machine list and result arrays
    /// across rounds and frames (grow-only) and runs one parallel-for job per round instead of one
    /// job per machine. The machines are independent, each is advanced once, and the results are
    /// read in bind order, so the outcome of a round is the same. Ownership checks and failure codes
    /// match <see cref="NativeBatchedLifecycleOwnerV1.TryCreate"/>: an empty round is
    /// NativeCapacityPlanInvalid, a zero or repeated owner id NativeLifetimeStateInvalid. Main thread only.
    /// </summary>
    internal sealed class NativeBatchedLifecyclePoolV1 : IDisposable
    {
        private readonly HashSet<ulong> _ownerIds = new HashSet<ulong>();
        private UnsafeList<NativeLifecycleMachineV1> _machines;
        private NativeArray<NativeLifecycleStepResultV1> _results;
        private NativeArray<NativeRuntimeFailureV1> _failures;
        private NativeArray<byte> _success;
        // Empty job-only storage attached to a machine whose tree has no cooldown or parallel node
        // (a job needs created arrays). Length 0, never indexed, shared by every bound machine copy.
        private NativeArray<byte> _emptyStorage;
        private NativeArray<NativeParallelBranchStateV1> _emptyParallelStorage;
        private JobHandle _dependency;
        private int _count;
        private bool _scheduled;
        private bool _disposed;

        internal int Count => _count;

        /// <summary>Binds the first <paramref name="count"/> machines (copies, as a lane does) for the next round.</summary>
        internal bool TryBind(NativeLifecycleMachineV1[] machines, int count, out NativeRuntimeFailureV1 failure)
        {
            if (_disposed || _scheduled)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid, NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
            if (machines == null || count <= 0 || count > machines.Length)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid, NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
            _ownerIds.Clear();
            for (var index = 0; index < count; index++)
            {
                var ownerId = machines[index].SchedulingOwnerId;
                if (ownerId == 0 || !_ownerIds.Add(ownerId))
                {
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid, NativeResourceKindV1.LifecycleBatchLanes);
                    return false;
                }
            }
            if (!TryEnsureCapacity(count, out failure)) return false;
            _machines.Clear();
            for (var index = 0; index < count; index++)
            {
                var machine = machines[index];
                machine.TryAttachCreatedEmptyJobStorage(_emptyStorage, _emptyParallelStorage);
                _machines.AddNoResize(machine);
            }
            _count = count;
            failure = default;
            return true;
        }

        /// <summary>Schedules one advance of every bound machine; <paramref name="batchSize"/> machines per work item.</summary>
        internal bool TrySchedule(uint batchSize, out JobHandle scheduled, out NativeRuntimeFailureV1 failure)
        {
            scheduled = default;
            if (_disposed || _scheduled || _count <= 0 || batchSize == 0 || batchSize > int.MaxValue)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid, NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
            _dependency = new AdvanceJob
            {
                Machines = _machines,
                Results = _results,
                Failures = _failures,
                Success = _success,
            }.ScheduleParallel(_count, (int)batchSize, default);
            _scheduled = true;
            scheduled = _dependency;
            failure = default;
            return true;
        }

        /// <summary>Completes the round; the first failed machine (in bind order) with a failure code gives the failure, as the owner does.</summary>
        internal bool TryComplete(out NativeRuntimeFailureV1 failure)
        {
            failure = default;
            if (!_scheduled)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid, NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
            _dependency.Complete();
            _dependency = default;
            _scheduled = false;
            for (var index = 0; index < _count; index++)
            {
                if (_success[index] == 0 && failure.Code == NativeRuntimeDiagnosticCodeV1.None)
                    failure = _failures[index];
            }
            return failure.Code == NativeRuntimeDiagnosticCodeV1.None;
        }

        /// <summary>The step the machine bound at <paramref name="index"/> produced in the completed round.</summary>
        internal NativeLifecycleStepResultV1 GetResult(int index) => _results[index];

        private bool TryEnsureCapacity(int count, out NativeRuntimeFailureV1 failure)
        {
            failure = default;
            if (_machines.IsCreated && _machines.Capacity >= count) return true;
            var capacity = Math.Max(count, _machines.IsCreated ? _machines.Capacity * 2 : 64);
            try
            {
                if (!_emptyStorage.IsCreated) _emptyStorage = new NativeArray<byte>(0, Allocator.Persistent);
                if (!_emptyParallelStorage.IsCreated)
                    _emptyParallelStorage = new NativeArray<NativeParallelBranchStateV1>(0, Allocator.Persistent);
                ReleaseRoundStorage();
                _machines = new UnsafeList<NativeLifecycleMachineV1>(capacity, Allocator.Persistent);
                _results = new NativeArray<NativeLifecycleStepResultV1>(capacity, Allocator.Persistent);
                _failures = new NativeArray<NativeRuntimeFailureV1>(capacity, Allocator.Persistent);
                _success = new NativeArray<byte>(capacity, Allocator.Persistent);
                return true;
            }
            catch
            {
                ReleaseRoundStorage();
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded, NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
        }

        private void ReleaseRoundStorage()
        {
            if (_machines.IsCreated) _machines.Dispose();
            if (_results.IsCreated) _results.Dispose();
            if (_failures.IsCreated) _failures.Dispose();
            if (_success.IsCreated) _success.Dispose();
            _machines = default;
            _results = default;
            _failures = default;
            _success = default;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_scheduled) _dependency.Complete();
            _scheduled = false;
            ReleaseRoundStorage();
            if (_emptyStorage.IsCreated) _emptyStorage.Dispose();
            if (_emptyParallelStorage.IsCreated) _emptyParallelStorage.Dispose();
            _count = 0;
        }

        [BurstCompile]
        private struct AdvanceJob : IJobFor
        {
            // The machines' own native arrays are reached through the list, not declared as job
            // fields (a container of containers); each index is touched by exactly one work item.
            [NativeDisableContainerSafetyRestriction] internal UnsafeList<NativeLifecycleMachineV1> Machines;
            internal NativeArray<NativeLifecycleStepResultV1> Results;
            internal NativeArray<NativeRuntimeFailureV1> Failures;
            internal NativeArray<byte> Success;

            public void Execute(int index)
            {
                Success[index] = Machines.ElementAt(index).TryAdvance(out var result, out var failure) ? (byte)1 : (byte)0;
                Results[index] = result;
                Failures[index] = failure;
            }
        }
    }
}
