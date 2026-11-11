using AIBT.Burst;

namespace AIBT.Tests.Consumer
{
    [AibtCatalogShard("aibt.tests.consumer", 1u)]
    public partial struct ConsumerShard { }

    public partial struct ConsumerConfig
    {
        [AibtConfigField("radius", "Float32", 1u)]
        public float Radius;

        [AibtConfigField("distance", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("distance", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float32", 1u)]
        public BlackboardReadHandle<float> Distance;

        [AibtConfigField("value", "GeneratedHandle", 1u)]
        [AibtCommandBinding("value", "Float32", 1u)]
        public CommandHandle<float> Value;
    }

    public partial struct ConsumerMemory { }

    [AibtNodeDocumentation("Tests finite float parameters and committed effects.", "Tests/Consumer",
        "Use for consumer integration tests.", "Not a gameplay node.", "consumer-radius")]
    [AibtBurstNode("aibt.tests.consumer.radius", 1u, BurstNodeKind.Action,
        typeof(ConsumerConfig), typeof(ConsumerMemory), NodeMemoryLifetime.Activation, true,
        BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running)]
    public partial struct ConsumerNode
    {
        public static void Enter(in ConsumerConfig config, ref ConsumerMemory memory, ref BurstEnterContext context) { }
        public static NodeStatus Tick(in ConsumerConfig config, ref ConsumerMemory memory, ref BurstTickContext context)
        {
            if (ConsumerShard.BurstAccess.TryRead(ref context, config.Distance, out var distance) != BurstContextResult.Success)
                return NodeStatus.Failure;
            if (ConsumerShard.BurstAccess.TryEmit(ref context, config.Value, in config.Radius) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return distance <= config.Radius ? NodeStatus.Success : NodeStatus.Running;
        }
        public static void Abort(in ConsumerConfig config, ref ConsumerMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in ConsumerConfig config, ref ConsumerMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }
}
