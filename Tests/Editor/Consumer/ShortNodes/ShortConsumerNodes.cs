using AIBT.Burst;
using AIBT.Native;

// P9-005: the consumer fixture node of AIBT.Consumer.Nodes, declared with the AIBT.Native short
// attributes and without the empty Enter/Abort/Exit. Same namespace, type names and identities on
// purpose: ShortFormDeclarationTests requires byte-identical generated metadata. This assembly is
// never part of a catalog, so the duplicate identities do not meet.
namespace AIBT.Tests.Consumer
{
    [AibtCatalogShard("aibt.tests.consumer", 1u)]
    public partial struct ConsumerShard { }

    public partial struct ConsumerConfig
    {
        [AibtParam("radius")] public float Radius;
        [AibtKey("distance")] public BlackboardReadHandle<float> Distance;
        [AibtCommand("value")] public CommandHandle<float> Value;
    }

    public partial struct ConsumerMemory { }

    [AibtNodeDocumentation("Tests finite float parameters and committed effects.", "Tests/Consumer",
        "Use for consumer integration tests.", "Not a gameplay node.", "consumer-radius")]
    [AibtAction("aibt.tests.consumer.radius", Cost = BurstNodeCost.Trivial)]
    public partial struct ConsumerNode
    {
        public static NodeStatus Tick(in ConsumerConfig config, ref ConsumerMemory memory, ref BurstTickContext context)
        {
            if (ConsumerShard.BurstAccess.TryRead(ref context, config.Distance, out var distance) != BurstContextResult.Success)
                return NodeStatus.Failure;
            if (ConsumerShard.BurstAccess.TryEmit(ref context, config.Value, in config.Radius) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return distance <= config.Radius ? NodeStatus.Success : NodeStatus.Running;
        }
    }
}
