using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT
{
    /// <summary>
    /// Owns one production PipelinedJobs group across scheduler frames. Each lifecycle round is
    /// scheduled in one frame and may only be completed after the next explicit scheduler-stage
    /// advance. One controller owns stable lanes for the lifetime of the group; each round supplies
    /// only its active lane subset, so terminal and dispatch-bound hosts are never re-scheduled and
    /// no lane storage is recreated between rounds.
    /// </summary>
    internal sealed class ProductionPipelinedGroupDriverV1
    {
        private readonly List<ProductionTreeHost> _open;
        private readonly List<ProductionTreeHost> _next;
        private readonly ProductionTreeHost[] _laneHosts;
        private readonly int[] _activeLaneIds;
        private readonly int[] _nextLaneIds;
        private readonly List<ProductionTreeHost> _advancedThisFrame;
        private readonly List<GeneratedDispatchGroupExecutorV2.Member> _pendingDispatch;
        private readonly List<GeneratedDispatchGroupExecutorV2.Member> _dispatchGroupScratch;
        private readonly GeneratedDispatchGroupWorkspaceV2 _workspace;
        private NativePipelinedPhaseControllerV1 _controller;
        private NativeArray<NativeLifecycleStepResultV1> _results;
        private NativeArray<NativeRuntimeFailureV1> _failures;
        private readonly uint _batchSize;
        private ulong _nextUpdateId = 1;

        private ProductionPipelinedGroupDriverV1(
            List<ProductionTreeHost> open,
            uint batchSize,
            GeneratedDispatchGroupWorkspaceV2 workspace)
        {
            _open = open;
            _batchSize = batchSize;
            _workspace = workspace;
            _next = new List<ProductionTreeHost>(open.Count);
            _laneHosts = new ProductionTreeHost[open.Count];
            _activeLaneIds = new int[open.Count];
            _nextLaneIds = new int[open.Count];
            _advancedThisFrame = new List<ProductionTreeHost>(open.Count);
            _pendingDispatch = new List<GeneratedDispatchGroupExecutorV2.Member>(open.Count);
            _dispatchGroupScratch = new List<GeneratedDispatchGroupExecutorV2.Member>(open.Count);
            for (var index = 0; index < open.Count; index++)
            {
                _laneHosts[index] = open[index];
                _activeLaneIds[index] = index;
            }
        }

        internal bool IsComplete { get; private set; }
        internal ulong TotalSteps { get; private set; }
        internal ulong StagesElapsed { get; private set; }

        internal bool AdvancedThisFrame(ProductionTreeHost host)
        {
            for (var index = 0; index < _advancedThisFrame.Count; index++)
                if (object.ReferenceEquals(_advancedThisFrame[index], host)) return true;
            return false;
        }

        internal static bool TryStart(
            IReadOnlyList<ProductionTreeHost> members,
            uint batchSize,
            out ProductionPipelinedGroupDriverV1 driver,
            out NativeRuntimeFailureV1 failure)
            => TryStart(members, batchSize, null, out driver, out failure);

        internal static bool TryStart(
            IReadOnlyList<ProductionTreeHost> members,
            uint batchSize,
            GeneratedDispatchGroupWorkspaceV2 workspace,
            out ProductionPipelinedGroupDriverV1 driver,
            out NativeRuntimeFailureV1 failure)
        {
            driver = null;
            var open = new List<ProductionTreeHost>(members.Count);
            var machines = new NativeLifecycleMachineV1[members.Count];
            for (var index = 0; index < members.Count; index++)
            {
                if (members[index].TryBeginBatchedRound(out var included, out var machine) && included)
                {
                    open.Add(members[index]);
                    machines[open.Count - 1] = machine;
                }
            }
            if (open.Count == 0)
            {
                failure = default;
                return true;
            }
            var laneMachines = new NativeLifecycleMachineV1[open.Count];
            for (var index = 0; index < open.Count; index++) laneMachines[index] = machines[index];
            driver = new ProductionPipelinedGroupDriverV1(open, batchSize, workspace);
            if (!driver.TryInitialize(laneMachines, out failure))
            {
                ReleaseHosts(open);
                driver.DisposeController();
                driver = null;
                return false;
            }
            if (driver.TryScheduleRound(out failure)) return true;
            driver.FailAll(failure);
            driver.DisposeController();
            driver = null;
            return false;
        }

        internal bool TryAdvance(out NativeRuntimeFailureV1 failure)
            => TryAdvance(scheduleNextRound: true, out failure);

        internal bool TryDrain(out NativeRuntimeFailureV1 failure)
            => TryAdvance(scheduleNextRound: false, out failure);

        private bool TryAdvance(bool scheduleNextRound, out NativeRuntimeFailureV1 failure)
        {
            failure = default;
            if (IsComplete) return true;
            _advancedThisFrame.Clear();
            if (!_controller.TryAdvanceStage(out failure)
                || !_controller.TryCompleteExecuteRound(
                    _results, _failures, _activeLaneIds, _open.Count, out failure))
            {
                FailAll(failure);
                DisposeControllerAfterFailure();
                IsComplete = true;
                return false;
            }

            if (!_controller.TrySealExecute(out failure)
                || !_controller.TryCompleteReduce(out failure)
                || !_controller.TryCompletePublish(out var metrics, out failure))
            {
                FailAll(failure);
                DisposeControllerAfterFailure();
                IsComplete = true;
                return false;
            }
            StagesElapsed += metrics.StagesElapsed;
            TotalSteps += metrics.ExecutedAtomicSteps;

            _next.Clear();
            _pendingDispatch.Clear();
            for (var index = 0; index < _open.Count; index++)
            {
                var host = _open[index];
                _advancedThisFrame.Add(host);
                var outcome = host.TryHandleBatchedStepResult(_results[index], out var request);
                if (outcome == ProductionTreeHost.BatchedStepOutcome.Terminal)
                    host.ReleaseBatchedDrive();
                else if (outcome == ProductionTreeHost.BatchedStepOutcome.DispatchRequired)
                    _pendingDispatch.Add(new GeneratedDispatchGroupExecutorV2.Member(host, request.NodeIndex, request));
                else
                {
                    _next.Add(host);
                    _nextLaneIds[_next.Count - 1] = _activeLaneIds[index];
                }
            }
            if (_pendingDispatch.Count > 0)
                ProductionDispatchResolverV1.Resolve(_pendingDispatch, _next, _dispatchGroupScratch, _workspace);

            for (var index = 0; index < _next.Count; index++) _nextLaneIds[index] = FindLaneId(_next[index]);

            _open.Clear();
            _open.AddRange(_next);
            for (var index = 0; index < _open.Count; index++) _activeLaneIds[index] = _nextLaneIds[index];
            if (_open.Count == 0 || !scheduleNextRound)
            {
                if (!scheduleNextRound) ReleaseAll();
                IsComplete = true;
                DisposeController();
                return true;
            }
            if (TryScheduleRound(out failure)) return true;
            FailAll(failure);
            IsComplete = true;
            return false;
        }

        private bool TryScheduleRound(out NativeRuntimeFailureV1 failure)
        {
            if (_nextUpdateId == 0)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.InstanceBudgetState);
                return false;
            }
            if (!_controller.TryBeginSnapshot(_nextUpdateId, out failure)
                || !_controller.TryCompleteSnapshot(1, out failure)
                || !_controller.TryScheduleExecuteRound(
                    _batchSize, _activeLaneIds, _open.Count, default(JobHandle), out _, out failure))
            {
                DisposeControllerAfterFailure();
                return false;
            }
            _nextUpdateId++;
            return true;
        }

        private bool TryInitialize(NativeLifecycleMachineV1[] machines, out NativeRuntimeFailureV1 failure)
        {
            if (!NativePipelinedPhaseControllerV1.TryCreate(
                    machines, Allocator.Persistent, out _controller, out failure))
                return false;
            try
            {
                _results = new NativeArray<NativeLifecycleStepResultV1>(machines.Length, Allocator.Persistent);
                _failures = new NativeArray<NativeRuntimeFailureV1>(machines.Length, Allocator.Persistent);
                return true;
            }
            catch
            {
                DisposeController();
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    NativeResourceKindV1.LifecycleBatchLanes);
                return false;
            }
        }

        private int FindLaneId(ProductionTreeHost host)
        {
            for (var index = 0; index < _laneHosts.Length; index++)
                if (ReferenceEquals(_laneHosts[index], host)) return index;
            return -1;
        }

        private void DisposeControllerAfterFailure()
        {
            if (_controller != null && _controller.Phase == NativePipelinedPhaseV1.ExecuteReady)
                _controller.TryAbortUpdate(out _);
            DisposeController();
        }

        private void DisposeController()
        {
            DisposeControllerOnly();
            DisposeBuffers();
        }

        private void DisposeControllerOnly()
        {
            if (_controller != null)
            {
                _controller.TryDispose(out _);
                _controller = null;
            }
        }

        private void DisposeBuffers()
        {
            if (_results.IsCreated) _results.Dispose();
            if (_failures.IsCreated) _failures.Dispose();
            _results = default;
            _failures = default;
        }

        private void FailAll(NativeRuntimeFailureV1 failure)
        {
            for (var index = 0; index < _open.Count; index++)
            {
                _open[index].FailExternalScheduling(failure);
                _open[index].ReleaseBatchedDrive();
            }
            _open.Clear();
        }

        private void ReleaseAll()
        {
            ReleaseHosts(_open);
            _open.Clear();
        }

        private static void ReleaseHosts(List<ProductionTreeHost> hosts)
        {
            for (var index = 0; index < hosts.Count; index++) hosts[index].ReleaseBatchedDrive();
        }
    }
}
