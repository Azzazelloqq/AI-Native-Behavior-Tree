using AIBT.Burst;

namespace AIBT.Tests.CodeGen.GenerationP11009
{
    // P11-009 (review F9) fixture, in its own assembly (a node assembly declares exactly one shard).
    // Two nodes use Tree-scope and Agent-scope keys in one tree: "agent-bump" reads and increments an
    // Int32 Agent key; "mixed-report" reads a Bool and an Int32 Tree key and the Agent key, and writes
    // a Tree key that encodes both (agent * 1000 + tree), so a test can check every scope's value after
    // a run. The two nodes stay apart on purpose: the Agent value written by one node is read by the next
    // (P11-010 lifted the session-capacity limit that first caused the split: a ReadWrite binding read and
    // written in one tick now counts as two sessions, so a single node would fit as well).
    [AibtCatalogShard("aibt.tests.p11009.mixed-scope", 1u)]
    public partial struct MixedScopeFixtureShard { }

    public partial struct AgentBumpConfig
    {
        [AibtConfigField("agent-counter", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("agent-counter", BurstBlackboardAccess.ReadWrite, BlackboardScope.Agent, "Int32", 1u)]
        public BlackboardReadWriteHandle<int> AgentCounter;
    }

    public partial struct MixedScopeNoMemory { }

    [AibtNodeDocumentation(
        "Adds one to an Agent-scope int.", "Tests",
        "Verify a native tree reads and writes an Agent-scope key next to Tree-scope keys.",
        "Not production.", "agent-bump")]
    [AibtBurstNode(
        "aibt.tests.agent-bump", 1u, BurstNodeKind.Action,
        typeof(AgentBumpConfig), typeof(MixedScopeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct AgentBumpNode
    {
        public static NodeStatus Tick(in AgentBumpConfig config, ref MixedScopeNoMemory memory, ref BurstTickContext context)
        {
            if (MixedScopeFixtureShard.BurstAccess.TryRead(ref context, config.AgentCounter, out var counter) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var next = unchecked(counter + 1);
            return MixedScopeFixtureShard.BurstAccess.TryWrite(ref context, config.AgentCounter, in next) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    public partial struct MixedReportConfig
    {
        [AibtConfigField("ready", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("ready", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Bool", 1u)]
        public BlackboardReadHandle<bool> Ready;

        [AibtConfigField("tree-in", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("tree-in", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardReadHandle<int> TreeIn;

        [AibtConfigField("agent-counter", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("agent-counter", BurstBlackboardAccess.Read, BlackboardScope.Agent, "Int32", 1u)]
        public BlackboardReadHandle<int> AgentCounter;

        [AibtConfigField("tree-out", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("tree-out", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardWriteHandle<int> TreeOut;
    }

    [AibtNodeDocumentation(
        "Writes agent * 1000 + tree-in to a Tree key when ready.", "Tests",
        "Verify a native tree reads Tree and Agent keys and writes a Tree key in one tick.",
        "Not production.", "mixed-report")]
    [AibtBurstNode(
        "aibt.tests.mixed-report", 1u, BurstNodeKind.Action,
        typeof(MixedReportConfig), typeof(MixedScopeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct MixedReportNode
    {
        public static NodeStatus Tick(in MixedReportConfig config, ref MixedScopeNoMemory memory, ref BurstTickContext context)
        {
            if (MixedScopeFixtureShard.BurstAccess.TryRead(ref context, config.Ready, out var ready) != BurstContextResult.Success
                || !ready
                || MixedScopeFixtureShard.BurstAccess.TryRead(ref context, config.TreeIn, out var treeIn) != BurstContextResult.Success
                || MixedScopeFixtureShard.BurstAccess.TryRead(ref context, config.AgentCounter, out var counter) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var encoded = unchecked(counter * 1000 + treeIn);
            return MixedScopeFixtureShard.BurstAccess.TryWrite(ref context, config.TreeOut, in encoded) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }
}
