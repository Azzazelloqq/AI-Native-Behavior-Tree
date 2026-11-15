using System;
using System.Collections.Generic;

namespace AIBT
{
    /// <summary>
    /// Drives one <see cref="ProductionTreeScheduler"/> group under real <c>BatchedJobsSameFrame</c>
    /// semantics: every member's own lifecycle machine is advanced one step per round together through
    /// <see cref="NativeBatchedLifecyclePoolV1"/> (one parallel-for Job per round) -- the exact same lifecycle-step
    /// batching <c>SchedulingPolicyDriver.TryRunBatchedJobsSameFrame</c> already uses and
    /// <c>NativeWorkEstimatorV1</c>'s own calibration is measured against -- but a round's
    /// <c>DispatchRequired</c> members are resolved through real generated dispatch instead of a
    /// caller-supplied status array: through <see cref="GeneratedDispatchGroupExecutorV2"/> when two
    /// or more members share one catalog this exact round, otherwise each host's own single-instance
    /// callback. A member whose dispatch is rejected is dropped from later rounds (matches
    /// <see cref="ProductionTreeHost.RunSegment"/>'s own single-instance rejection contract: a
    /// faulted host's own machine is never advanced again); every other member is unaffected.
    /// </summary>
    /// <remarks>
    /// P10-002: one driver per scheduler keeps its round lists, machine array and round storage
    /// (<see cref="NativeBatchedLifecyclePoolV1"/>: one parallel-for job per round instead of one job
    /// per machine) across rounds and frames, growing only, so a warmed-up round allocates nothing.
    /// Each machine still advances exactly one step per round and every call on every host happens in
    /// the same order as before. A reentrant run (a round started from inside another round) uses a
    /// temporary driver.
    /// </remarks>
    internal sealed class ProductionBatchedGroupDriverV1 : IDisposable
    {
        private readonly List<GeneratedDispatchGroupExecutorV2.Member> _pendingDispatch = new List<GeneratedDispatchGroupExecutorV2.Member>();
        private readonly List<GeneratedDispatchGroupExecutorV2.Member> _dispatchGroupScratch = new List<GeneratedDispatchGroupExecutorV2.Member>();
        private List<ProductionTreeHost> _open = new List<ProductionTreeHost>();
        private List<ProductionTreeHost> _next = new List<ProductionTreeHost>();
        private NativeLifecycleMachineV1[] _machines = new NativeLifecycleMachineV1[0];
        private readonly NativeBatchedLifecyclePoolV1 _pool = new NativeBatchedLifecyclePoolV1();
        private bool _running;
        private bool _disposed;

        internal bool TryRun(
            IReadOnlyList<ProductionTreeHost> members,
            uint batchSize,
            out ulong totalSteps,
            out NativeRuntimeFailureV1 failure,
            GeneratedDispatchGroupWorkspaceV2 workspace = null)
        {
            totalSteps = 0;
            failure = default;
            if (members == null || members.Count == 0) return true;
            if (_running || _disposed)
            {
                using (var temporary = new ProductionBatchedGroupDriverV1())
                    return temporary.TryRun(members, batchSize, out totalSteps, out failure, workspace);
            }
            _running = true;
            try
            {
                return TryRunRounds(members, batchSize, ref totalSteps, out failure, workspace);
            }
            finally
            {
                _open.Clear();
                _next.Clear();
                _pendingDispatch.Clear();
                _dispatchGroupScratch.Clear();
                _running = false;
            }
        }

        private bool TryRunRounds(
            IReadOnlyList<ProductionTreeHost> members,
            uint batchSize,
            ref ulong totalSteps,
            out NativeRuntimeFailureV1 failure,
            GeneratedDispatchGroupWorkspaceV2 workspace)
        {
            _open.Clear();
            for (var index = 0; index < members.Count; index++)
            {
                // A per-host begin failure already recorded that host's own failure internally
                // (Fail was called, its own _driving released) -- it simply never joins this group.
                if (members[index].TryBeginBatchedRound(out var included, out _) && included)
                    _open.Add(members[index]);
            }
            SchedulerFrameProfileV1.Lap(SchedulerFrameProfileV1.Stage.RoundBegin);

            while (_open.Count > 0)
            {
                var count = _open.Count;
                if (_machines.Length < count) _machines = new NativeLifecycleMachineV1[Math.Max(count, _machines.Length * 2)];
                for (var index = 0; index < count; index++) _machines[index] = _open[index].Machine;

                if (!_pool.TryBind(_machines, count, out failure))
                {
                    ReleaseAll(_open);
                    return false;
                }
                SchedulerFrameProfileV1.Add(SchedulerFrameProfileV1.Counter.Rounds, 1);
                SchedulerFrameProfileV1.Add(SchedulerFrameProfileV1.Counter.Lanes, count);
                SchedulerFrameProfileV1.Lap(SchedulerFrameProfileV1.Stage.RoundAllocate);

                var ok = _pool.TrySchedule(batchSize, out var handle, out failure);
                SchedulerFrameProfileV1.Lap(SchedulerFrameProfileV1.Stage.RoundSchedule);
                if (ok)
                {
                    handle.Complete();
                    ok = _pool.TryComplete(out failure);
                }
                SchedulerFrameProfileV1.Lap(SchedulerFrameProfileV1.Stage.RoundComplete);

                if (!ok)
                {
                    ReleaseAll(_open);
                    return false;
                }

                totalSteps += (ulong)count;
                _pendingDispatch.Clear();
                _next.Clear();
                for (var index = 0; index < count; index++)
                {
                    var host = _open[index];
                    var outcome = host.TryHandleBatchedStepResult(_pool.GetResult(index), out var request);
                    if (outcome == ProductionTreeHost.BatchedStepOutcome.Terminal)
                    {
                        host.ReleaseBatchedDrive();
                    }
                    else if (outcome == ProductionTreeHost.BatchedStepOutcome.DispatchRequired)
                    {
                        _pendingDispatch.Add(new GeneratedDispatchGroupExecutorV2.Member(host, request.NodeIndex, request));
                    }
                    else
                    {
                        _next.Add(host);
                    }
                }
                SchedulerFrameProfileV1.Lap(SchedulerFrameProfileV1.Stage.RoundHandle);

                SchedulerFrameProfileV1.Add(SchedulerFrameProfileV1.Counter.DispatchMembers, _pendingDispatch.Count);
                if (_pendingDispatch.Count > 0)
                    ProductionDispatchResolverV1.Resolve(_pendingDispatch, _next, _dispatchGroupScratch, workspace);
                SchedulerFrameProfileV1.Lap(SchedulerFrameProfileV1.Stage.RoundResolve);

                var finished = _open;
                _open = _next;
                _next = finished;
            }

            failure = default;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_running) _pool.Dispose();
        }

        private static void ReleaseAll(List<ProductionTreeHost> hosts)
        {
            for (var index = 0; index < hosts.Count; index++) hosts[index].ReleaseBatchedDrive();
        }
    }
}
