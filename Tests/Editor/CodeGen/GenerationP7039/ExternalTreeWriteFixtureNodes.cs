using AIBT.Burst;

namespace AIBT.Tests.CodeGen.GenerationP7039
{
    // P7-039 (ADR-P7-039) fixture, in its own assembly (a node assembly declares exactly one
    // shard -- this could not share GenerationShard's assembly without changing its own
    // hash-pinned metadata elsewhere). One Tree-scope key ("live-target") is Read-only across the
    // whole tree -- eligible for external write. One Tree-scope key ("owned-target") is written by
    // a node -- ineligible, the negative-case fixture.
    [AibtCatalogShard("aibt.tests.p7039.external-tree-write", 1u)]
    public partial struct ExternalTreeWriteFixtureShard { }

    public partial struct ExternalTreeReaderConfig
    {
        [AibtConfigField("target", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("live-target", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float32", 1u)]
        public BlackboardReadHandle<float> Target;
    }

    public partial struct ExternalTreeReaderMemory
    {
        [AibtMemoryField("last-read", "Float32", 1u)]
        public float LastRead;
    }

    [AibtNodeDocumentation(
        "Reads a Tree-scope value fed externally.", "Tests",
        "Verify a native ProductionTreeHost external write is observed by a node.",
        "Not production.", "external-tree-write-reader")]
    [AibtBurstNode(
        "aibt.tests.external-tree-write-reader", 1u, BurstNodeKind.Condition,
        typeof(ExternalTreeReaderConfig), typeof(ExternalTreeReaderMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct ExternalTreeReaderNode
    {
        public static void Enter(in ExternalTreeReaderConfig config, ref ExternalTreeReaderMemory memory, ref BurstEnterContext context) { }

        public static NodeStatus Tick(in ExternalTreeReaderConfig config, ref ExternalTreeReaderMemory memory, ref BurstTickContext context)
        {
            var result = ExternalTreeWriteFixtureShard.BurstAccess.TryRead(ref context, config.Target, out var value);
            if (result != BurstContextResult.Success) return NodeStatus.Failure;
            memory.LastRead = value;
            return value > 0f ? NodeStatus.Success : NodeStatus.Failure;
        }

        public static void Abort(in ExternalTreeReaderConfig config, ref ExternalTreeReaderMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in ExternalTreeReaderConfig config, ref ExternalTreeReaderMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }

    public partial struct ExternalTreeWriterConfig
    {
        [AibtConfigField("target", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("owned-target", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardWriteHandle<int> Target;

        [AibtConfigField("value", "UInt32", 1u)]
        public uint Value;
    }

    public partial struct ExternalTreeWriterMemory { }

    [AibtNodeDocumentation(
        "Writes a Tree-scope value a node owns.", "Tests",
        "Verify external write is refused for a key a node writes.",
        "Not production.", "external-tree-write-writer")]
    [AibtBurstNode(
        "aibt.tests.external-tree-write-writer", 1u, BurstNodeKind.Action,
        typeof(ExternalTreeWriterConfig), typeof(ExternalTreeWriterMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success)]
    public partial struct ExternalTreeWriterNode
    {
        public static void Enter(in ExternalTreeWriterConfig config, ref ExternalTreeWriterMemory memory, ref BurstEnterContext context) { }

        public static NodeStatus Tick(in ExternalTreeWriterConfig config, ref ExternalTreeWriterMemory memory, ref BurstTickContext context)
        {
            var value = unchecked((int)config.Value);
            ExternalTreeWriteFixtureShard.BurstAccess.TryWrite(ref context, config.Target, in value);
            return NodeStatus.Success;
        }

        public static void Abort(in ExternalTreeWriterConfig config, ref ExternalTreeWriterMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in ExternalTreeWriterConfig config, ref ExternalTreeWriterMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }
}
