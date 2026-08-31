using AIBT.Burst;

namespace AIBT.Spikes.P6022NativeDispatchHarness
{
    // Disposable P6-022 spike node/shard, entirely private to this spike -- never referenced from
    // production, Samples~/BurstNodes, or Tests/. A single-node shard so the real Roslyn-generated
    // dispatch catalog assigns it CatalogCaseIndex 0, letting GenericNativeDispatchSpikeTests prove
    // GenericNativeDispatchTranslatorV1 against a genuine single-case workspace shape (see
    // ADR-P6-022's own single-case scope) without needing the real, two-node
    // Samples~/BurstNodes/Catalog/PublicBurstNodeCatalog, whose ThresholdConditionNode sits at real
    // dispatch index 1 -- that index is only reachable by translating the whole catalog, a
    // materially different problem this card's own decided scope does not cover.
    [AibtCatalogShard("aibt.spikes.p6022-native-dispatch-shard", 1u)]
    public partial struct SpikeCatalogShard { }

    public partial struct SpikeThresholdConditionConfig
    {
        [AibtConfigField("current", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("current", BurstBlackboardAccess.Read, BlackboardScope.Tree, "UInt32", 1u)]
        public BlackboardReadHandle<uint> Current;

        [AibtConfigField("minimum", "UInt32", 1u)]
        public uint Minimum;
    }

    public partial struct SpikeEmptyNodeMemory { }

    [AibtNodeDocumentation(
        "P6-022 spike copy of the sample ThresholdCondition node: succeeds when a typed blackboard value reaches a configured threshold.",
        "Spikes/P6-022",
        "Disposable spike fixture only.",
        "Never use outside this spike.",
        "p6022-spike-threshold-condition")]
    [AibtObserverCondition]
    [AibtBurstNode(
        "aibt.spikes.p6022-threshold-condition", 1u, BurstNodeKind.Condition,
        typeof(SpikeThresholdConditionConfig), typeof(SpikeEmptyNodeMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct SpikeThresholdConditionNode
    {
        public static void Enter(
            in SpikeThresholdConditionConfig config,
            ref SpikeEmptyNodeMemory memory,
            ref BurstEnterContext context) { }

        public static NodeStatus Tick(
            in SpikeThresholdConditionConfig config,
            ref SpikeEmptyNodeMemory memory,
            ref BurstTickContext context)
        {
            var result = SpikeCatalogShard.BurstAccess.TryRead(ref context, config.Current, out var current);
            return result == BurstContextResult.Success && current >= config.Minimum
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }

        public static void Abort(
            in SpikeThresholdConditionConfig config,
            ref SpikeEmptyNodeMemory memory,
            ref BurstAbortContext context,
            BurstNodeAbortReason reason) { }

        public static void Exit(
            in SpikeThresholdConditionConfig config,
            ref SpikeEmptyNodeMemory memory,
            ref BurstExitContext context,
            BurstNodeExitReason reason) { }

        public static ConditionResult Evaluate(
            in SpikeThresholdConditionConfig config,
            ref BurstObserverContext context)
        {
            var result = SpikeCatalogShard.BurstAccess.TryRead(ref context, config.Current, out var current);
            return result == BurstContextResult.Success && current >= config.Minimum
                ? ConditionResult.Success
                : ConditionResult.Failure;
        }
    }
}
