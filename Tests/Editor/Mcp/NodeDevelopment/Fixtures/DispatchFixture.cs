using AIBT;
using AIBT.Burst;

namespace AIBT.Tests.NodeDevelopment.Fixtures
{
    [AibtCatalogShard("aibt.tests.node-development-dispatch", 1u)]
    public partial struct DispatchFixtureShard { }
    public partial struct DispatchFixtureConfig
    {
        [AibtConfigField("current", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("current", BurstBlackboardAccess.Read, BlackboardScope.Tree, "UInt32", 1u)]
        public BlackboardReadHandle<uint> Current;
        [AibtConfigField("threshold", "UInt32", 1u)]
        public uint Minimum;
    }
    public partial struct DispatchFixtureMemory { }

    [AibtNodeDocumentation("Background dispatch regression fixture.", "Tests", "Tests only.", "Not production.", "background-dispatch")]
    [AibtBurstNode("aibt.tests.node-development-condition", 1u, BurstNodeKind.Condition,
        typeof(DispatchFixtureConfig), typeof(DispatchFixtureMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct DispatchFixtureNode
    {
        public static void Enter(in DispatchFixtureConfig config, ref DispatchFixtureMemory memory, ref BurstEnterContext context) { }
        public static NodeStatus Tick(in DispatchFixtureConfig config, ref DispatchFixtureMemory memory, ref BurstTickContext context)
        {
            var result = DispatchFixtureShard.BurstAccess.TryRead(ref context, config.Current, out var current);
            return result == BurstContextResult.Success && current >= config.Minimum ? NodeStatus.Success : NodeStatus.Failure;
        }
        public static void Abort(in DispatchFixtureConfig config, ref DispatchFixtureMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in DispatchFixtureConfig config, ref DispatchFixtureMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }
}
