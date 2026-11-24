using AIBT.Burst;

namespace AIBT.Tests.CodeGen.GenerationP11014
{
    // P11-014 (item 7) fixture, in its own assembly (a node assembly declares exactly one shard).
    // The catalog sizes one callback's value sessions for its largest node: one per Read or Write
    // binding, two per ReadWrite binding. Both nodes here have one Read binding, so a catalog of this
    // shard alone (with the built-in leaves, which bind nothing) gives one session per callback.
    // "read-twice" reads its binding twice in one callback and is refused (CapacityExceeded);
    // "read-once" reads it once and runs. A catalog that also holds a node with more bindings
    // (MixedScopeFixtureShard's mixed-report, four) has room for the second read and hides the fault.
    [AibtCatalogShard("aibt.tests.p11014.session-limit", 1u)]
    public partial struct SessionLimitFixtureShard { }

    public partial struct SessionLimitReadConfig
    {
        [AibtConfigField("value", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("value", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardReadHandle<int> Value;
    }

    public partial struct SessionLimitNoMemory { }

    [AibtNodeDocumentation(
        "Reads one Int32 binding twice in one callback.", "Tests",
        "Pin the value-session limit: a second read of one Read binding is refused.",
        "Not production: read a value once into a local instead.", "read-twice")]
    [AibtBurstNode(
        "aibt.tests.read-twice", 1u, BurstNodeKind.Action,
        typeof(SessionLimitReadConfig), typeof(SessionLimitNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct ReadTwiceNode
    {
        public static NodeStatus Tick(in SessionLimitReadConfig config, ref SessionLimitNoMemory memory, ref BurstTickContext context)
        {
            if (SessionLimitFixtureShard.BurstAccess.TryRead(ref context, config.Value, out var first) != BurstContextResult.Success)
                return NodeStatus.Failure;
            if (SessionLimitFixtureShard.BurstAccess.TryRead(ref context, config.Value, out var second) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return first == second ? NodeStatus.Success : NodeStatus.Failure;
        }
    }

    [AibtNodeDocumentation(
        "Reads one Int32 binding once.", "Tests",
        "The well-behaved neighbour of read-twice: one read per callback.",
        "Not production.", "read-once")]
    [AibtBurstNode(
        "aibt.tests.read-once", 1u, BurstNodeKind.Action,
        typeof(SessionLimitReadConfig), typeof(SessionLimitNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct ReadOnceNode
    {
        public static NodeStatus Tick(in SessionLimitReadConfig config, ref SessionLimitNoMemory memory, ref BurstTickContext context)
            => SessionLimitFixtureShard.BurstAccess.TryRead(ref context, config.Value, out _) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
    }
}
