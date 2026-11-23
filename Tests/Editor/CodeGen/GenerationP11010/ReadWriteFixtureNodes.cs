using AIBT.Burst;

namespace AIBT.Tests.CodeGen.GenerationP11010
{
    // P11-010 fixture, in its own assembly (a node assembly declares exactly one shard). The shard holds
    // one node and nothing else, so that node is the catalog's largest case: "rw-increment" reads an Int32
    // Tree key and writes it back plus one through a single ReadWrite binding, the shape of the library's
    // increment-int, increment-float and toggle-bool. One tick opens two value sessions (the read and the
    // write) on one binding; before P11-010 the catalog sized the per-frame session pool at one session per
    // binding of its largest case, so this node stopped the host with CapacityExceeded.
    [AibtCatalogShard("aibt.tests.p11010.read-write", 1u)]
    public partial struct ReadWriteFixtureShard { }

    public partial struct ReadWriteIncrementConfig
    {
        [AibtConfigField("counter", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("counter", BurstBlackboardAccess.ReadWrite, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardReadWriteHandle<int> Counter;
    }

    public partial struct ReadWriteNoMemory { }

    [AibtNodeDocumentation(
        "Adds one to an int through one ReadWrite binding.", "Tests",
        "Verify a native node can read and write one key through a single ReadWrite binding in one tick.",
        "Not production.", "rw-increment")]
    [AibtBurstNode(
        "aibt.tests.rw-increment", 1u, BurstNodeKind.Action,
        typeof(ReadWriteIncrementConfig), typeof(ReadWriteNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct ReadWriteIncrementNode
    {
        public static NodeStatus Tick(in ReadWriteIncrementConfig config, ref ReadWriteNoMemory memory, ref BurstTickContext context)
        {
            if (ReadWriteFixtureShard.BurstAccess.TryRead(ref context, config.Counter, out var counter) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var next = unchecked(counter + 1);
            return ReadWriteFixtureShard.BurstAccess.TryWrite(ref context, config.Counter, in next) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }
}
