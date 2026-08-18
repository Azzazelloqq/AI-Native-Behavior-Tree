using AIBT;
using AIBT.Burst;

namespace AIBT.Tests.CodeGen.Generation
{
    [AibtCatalogShard("aibt.tests.generation", 1u)]
    public partial struct GenerationShard { }

    public partial struct GenerationConfig
    {
        [AibtConfigField("target", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("agent-target", BurstBlackboardAccess.Read, BlackboardScope.Agent, "Int32", 1u)]
        public BlackboardReadHandle<int> Target;

        [AibtConfigField("enabled", "Bool", 1u)]
        public bool Enabled;
    }

    public partial struct GenerationMemory
    {
        [AibtMemoryField("count", "UInt32", 1u)]
        public uint Count;

        [AibtMemoryField("payload", "aibt.tests.registered-value", 1u)]
        public GenerationRegisteredValue Payload;
    }

    [AibtBurstValue("aibt.tests.registered-value", 1u, "aibt.tests.registered-value.schema")]
    public partial struct GenerationRegisteredValue
    {
        [AibtValueField("asset", "AssetId", 1u)]
        public AssetId Asset;

        [AibtValueField("count", "Int32", 1u)]
        public int Count;
    }

    [AibtNodeDocumentation("Generated fixture", "Tests", "Verify generated metadata", "Not production", "generation-success")]
    [AibtObserverCondition]
    [AibtBurstNode(
        "aibt.tests.generated-node", 1u, BurstNodeKind.Condition,
        typeof(GenerationConfig), typeof(GenerationMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct GenerationNode
    {
        public static void Enter(
            in GenerationConfig config,
            ref GenerationMemory memory,
            ref BurstEnterContext context) { }

        public static NodeStatus Tick(
            in GenerationConfig config,
            ref GenerationMemory memory,
            ref BurstTickContext context)
        {
            var value = 0;
            for (var index = 0; index < 32; index++)
            {
                var result = GenerationShard.BurstAccess.TryRead(ref context, config.Target, out value);
                if (result != BurstContextResult.Success)
                {
                    return NodeStatus.Failure;
                }
            }

            memory.Count = (uint)value + (config.Enabled ? 1u : 0u);
            return NodeStatus.Success;
        }

        public static void Abort(
            in GenerationConfig config,
            ref GenerationMemory memory,
            ref BurstAbortContext context,
            BurstNodeAbortReason reason) { }

        public static void Exit(
            in GenerationConfig config,
            ref GenerationMemory memory,
            ref BurstExitContext context,
            BurstNodeExitReason reason) { }

        public static ConditionResult Evaluate(
            in GenerationConfig config,
            ref BurstObserverContext context)
            => ConditionResult.Success;
    }
}
