using AIBT.Burst;
using Unity.Burst;

namespace AIBT.Tests.CodeGen.GenerationP11014Probe
{
    // P11-014 review follow-up (item 1) fixture, in its own assembly (a node assembly declares exactly one
    // shard). "burst-probe" tells, from inside a node body, whether that body runs Burst-compiled:
    // Burst removes the call to a [BurstDiscard] method, managed code runs it. Each tick adds to one
    // Int32 binding: 1 when the body ran as managed code, 1000 when it ran Burst-compiled. The change of
    // the key over one update therefore says both that the node ran and how.
    [AibtCatalogShard("aibt.tests.p11014.burst-probe", 1u)]
    public partial struct BurstProbeFixtureShard { }

    public partial struct BurstProbeConfig
    {
        [AibtConfigField("tally", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("tally", BurstBlackboardAccess.ReadWrite, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardReadWriteHandle<int> Tally;
    }

    public partial struct BurstProbeNoMemory { }

    /// <summary>The managed-code marker the probe calls; Burst compiles the call away.</summary>
    public static class BurstProbeMarker
    {
        /// <summary>Managed code only: Burst removes every call to this method.</summary>
        [BurstDiscard]
        public static void MarkManaged(ref int managed) => managed = 1;
    }

    [AibtNodeDocumentation(
        "Adds 1 to an Int32 binding when its body runs as managed code, 1000 when it runs Burst-compiled.", "Tests",
        "Find out which execution paths run node bodies Burst-compiled.",
        "Not production.", "burst-probe")]
    [AibtBurstNode(
        "aibt.tests.burst-probe", 1u, BurstNodeKind.Action,
        typeof(BurstProbeConfig), typeof(BurstProbeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct BurstProbeNode
    {
        public static NodeStatus Tick(in BurstProbeConfig config, ref BurstProbeNoMemory memory, ref BurstTickContext context)
        {
            var managed = 0;
            BurstProbeMarker.MarkManaged(ref managed);
            if (BurstProbeFixtureShard.BurstAccess.TryRead(ref context, config.Tally, out var tally) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var next = unchecked(tally + (managed != 0 ? 1 : 1000));
            return BurstProbeFixtureShard.BurstAccess.TryWrite(ref context, config.Tally, in next) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }
}
