using System.Collections.Generic;

namespace AIBT
{
    /// <summary>Shared generated-dispatch resolution for same-frame and pipelined lifecycle rounds.</summary>
    internal static class ProductionDispatchResolverV1
    {
        internal static void Resolve(
            List<GeneratedDispatchGroupExecutorV2.Member> pending,
            List<ProductionTreeHost> next,
            List<GeneratedDispatchGroupExecutorV2.Member> groupScratch,
            GeneratedDispatchGroupWorkspaceV2 workspace = null)
        {
            for (var index = 0; index < pending.Count; index++)
            {
                var member = pending[index];
                var catalog = member.Host.GeneratedDispatchAdapter?.Catalog;
                if (catalog == null)
                {
                    ResolveSingle(member, next);
                    continue;
                }

                var seen = false;
                for (var previous = 0; previous < index; previous++)
                {
                    if (ReferenceEquals(pending[previous].Host.GeneratedDispatchAdapter?.Catalog, catalog))
                    {
                        seen = true;
                        break;
                    }
                }
                if (seen) continue;

                groupScratch.Clear();
                for (var candidate = index; candidate < pending.Count; candidate++)
                {
                    var candidateMember = pending[candidate];
                    if (ReferenceEquals(candidateMember.Host.GeneratedDispatchAdapter?.Catalog, catalog))
                        groupScratch.Add(candidateMember);
                }
                if (groupScratch.Count == 1)
                {
                    ResolveSingle(groupScratch[0], next);
                    continue;
                }

                var groupSucceeded = workspace == null
                    ? GeneratedDispatchGroupExecutorV2.TryExecuteGroup(
                        catalog, groupScratch, scheduled: false, out var groupFailure)
                    : GeneratedDispatchGroupExecutorV2.TryExecuteGroup(
                        catalog, workspace, groupScratch, scheduled: false, out groupFailure);
                if (groupSucceeded)
                {
                    for (var memberIndex = 0; memberIndex < groupScratch.Count; memberIndex++)
                    {
                        var groupedMember = groupScratch[memberIndex];
                        if (groupedMember.Host.TryResumeBatchedDriveAfterDispatch()) next.Add(groupedMember.Host);
                    }
                    continue;
                }

                for (var memberIndex = 0; memberIndex < groupScratch.Count; memberIndex++)
                    groupScratch[memberIndex].Host.CompletePendingDispatch(groupFailure, NodeStatus.Failure);
            }
        }

        private static void ResolveSingle(
            GeneratedDispatchGroupExecutorV2.Member member,
            List<ProductionTreeHost> next)
        {
            if (member.Host.TryResolveSinglePendingDispatch()
                && member.Host.TryResumeBatchedDriveAfterDispatch())
            {
                next.Add(member.Host);
            }
        }
    }
}
