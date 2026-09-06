using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT
{
    /// <summary>
    /// Drives one <see cref="ProductionTreeScheduler"/> group under real <c>BatchedJobsSameFrame</c>
    /// semantics: every member's own lifecycle machine is advanced together through one
    /// <see cref="NativeBatchedLifecycleOwnerV1"/> Job per round -- the exact same lifecycle-step
    /// batching <c>SchedulingPolicyDriver.TryRunBatchedJobsSameFrame</c> already uses and
    /// <c>NativeWorkEstimatorV1</c>'s own calibration is measured against -- but a round's
    /// <c>DispatchRequired</c> members are resolved through real generated dispatch instead of a
    /// caller-supplied status array: through <see cref="GeneratedDispatchGroupExecutorV2"/> when two
    /// or more members share one catalog this exact round, otherwise each host's own single-instance
    /// callback. A member whose dispatch is rejected is dropped from later rounds (matches
    /// <see cref="ProductionTreeHost.RunSegment"/>'s own single-instance rejection contract: a
    /// faulted host's own machine is never advanced again); every other member is unaffected.
    /// </summary>
    internal static class ProductionBatchedGroupDriverV1
    {
        internal static bool TryRun(
            IReadOnlyList<ProductionTreeHost> members,
            uint batchSize,
            out ulong totalSteps,
            out NativeRuntimeFailureV1 failure)
        {
            totalSteps = 0;
            failure = default;
            if (members == null || members.Count == 0) return true;

            var open = new List<ProductionTreeHost>(members.Count);
            for (var index = 0; index < members.Count; index++)
            {
                // A per-host begin failure already recorded that host's own failure internally
                // (Fail was called, its own _driving released) -- it simply never joins this group.
                if (members[index].TryBeginBatchedRound(out var included, out _) && included)
                    open.Add(members[index]);
            }

            var pendingDispatch = new List<GeneratedDispatchGroupExecutorV2.Member>();
            while (open.Count > 0)
            {
                var machines = new NativeLifecycleMachineV1[open.Count];
                for (var index = 0; index < open.Count; index++) machines[index] = open[index].Machine;

                if (!NativeBatchedLifecycleOwnerV1.TryCreate(machines, Allocator.Persistent, out var owner, out failure))
                {
                    ReleaseAll(open);
                    return false;
                }

                var results = new NativeArray<NativeLifecycleStepResultV1>(machines.Length, Allocator.Temp);
                var stepFailures = new NativeArray<NativeRuntimeFailureV1>(machines.Length, Allocator.Temp);
                bool ok;
                try
                {
                    ok = owner.TrySchedule(batchSize, default(JobHandle), out var handle, out failure);
                    if (ok)
                    {
                        handle.Complete();
                        ok = owner.TryComplete(results, stepFailures, out failure);
                    }
                }
                finally
                {
                    owner.TryDispose(out _);
                }

                if (!ok)
                {
                    results.Dispose();
                    stepFailures.Dispose();
                    ReleaseAll(open);
                    return false;
                }

                totalSteps += (ulong)open.Count;
                pendingDispatch.Clear();
                var next = new List<ProductionTreeHost>(open.Count);
                for (var index = 0; index < open.Count; index++)
                {
                    var host = open[index];
                    var outcome = host.TryHandleBatchedStepResult(results[index], out var request);
                    if (outcome == ProductionTreeHost.BatchedStepOutcome.Terminal)
                    {
                        host.ReleaseBatchedDrive();
                    }
                    else if (outcome == ProductionTreeHost.BatchedStepOutcome.DispatchRequired)
                    {
                        pendingDispatch.Add(new GeneratedDispatchGroupExecutorV2.Member(host, request.NodeIndex, request));
                    }
                    else
                    {
                        next.Add(host);
                    }
                }

                results.Dispose();
                stepFailures.Dispose();

                if (pendingDispatch.Count > 0) ResolvePendingDispatches(pendingDispatch, next);
                open = next;
            }

            failure = default;
            return true;
        }

        private static void ResolvePendingDispatches(
            List<GeneratedDispatchGroupExecutorV2.Member> pending,
            List<ProductionTreeHost> next)
        {
            var byCatalog = new Dictionary<GeneratedBurstCatalogV2, List<GeneratedDispatchGroupExecutorV2.Member>>();
            var singles = new List<GeneratedDispatchGroupExecutorV2.Member>();
            foreach (var member in pending)
            {
                var catalog = member.Host.GeneratedDispatchAdapter?.Catalog;
                if (catalog == null)
                {
                    singles.Add(member);
                    continue;
                }
                if (!byCatalog.TryGetValue(catalog, out var list))
                {
                    list = new List<GeneratedDispatchGroupExecutorV2.Member>();
                    byCatalog[catalog] = list;
                }
                list.Add(member);
            }

            foreach (var pair in byCatalog)
            {
                if (pair.Value.Count == 1)
                {
                    singles.Add(pair.Value[0]);
                    continue;
                }
                if (GeneratedDispatchGroupExecutorV2.TryExecuteGroup(pair.Key, pair.Value, scheduled: false, out var groupFailure))
                {
                    foreach (var member in pair.Value) next.Add(member.Host);
                }
                else
                {
                    // TryExecuteGroup's own contract: on failure the caller completes every
                    // member's pending dispatch with the returned failure. That host is now
                    // terminal/faulted and does not rejoin the next round.
                    foreach (var member in pair.Value)
                        member.Host.CompletePendingDispatch(groupFailure, NodeStatus.Failure);
                }
            }

            foreach (var member in singles)
            {
                if (member.Host.TryResolveSinglePendingDispatch()) next.Add(member.Host);
            }
        }

        private static void ReleaseAll(List<ProductionTreeHost> hosts)
        {
            for (var index = 0; index < hosts.Count; index++) hosts[index].ReleaseBatchedDrive();
        }
    }
}
