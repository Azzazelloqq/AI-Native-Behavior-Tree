using AIBT;
using AIBT.Burst;

namespace AIBT.BurstAbi.NodesA
{
    public partial struct CanaryConfig
    {
        [AibtConfigField("a-count", "UInt32", 1u)] public uint Count;
        [AibtConfigField("b-limit", "UInt64", 1u)] public ulong Limit;
        [AibtConfigField("c-enabled", "Bool", 1u)] public bool Enabled;
    }

    public partial struct CanaryMemory
    {
        [AibtMemoryField("a-count", "Int32", 1u)] public int Count;
        [AibtMemoryField("b-flags", "UInt32", 1u)] public uint Flags;
        [AibtMemoryField("c-total", "UInt64", 1u)] public ulong Total;
    }

    [AibtCatalogShard("canary.nodes", 1u)] public partial struct CanaryShard { }
    [AibtBurstNode("aibt.canary.action", 1u, BurstNodeKind.Action, typeof(CanaryConfig), typeof(CanaryMemory), NodeMemoryLifetime.Activation, true, BurstCancellationMode.NotApplicable, BurstNodeCost.Low, BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    [AibtNodeDocumentation("Canary", "Tests", "Use", "Avoid", "canary")]
    public partial struct CanaryNode
    {
        public static void Enter(in CanaryConfig config, ref CanaryMemory memory, ref BurstEnterContext context) { memory.Count = unchecked((int)config.Count); }
        public static NodeStatus Tick(in CanaryConfig config, ref CanaryMemory memory, ref BurstTickContext context) { memory.Count += unchecked((int)config.Count); memory.Flags++; memory.Total += config.Limit; return config.Enabled ? NodeStatus.Success : NodeStatus.Failure; }
        public static void Abort(in CanaryConfig config, ref CanaryMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { memory.Flags++; }
        public static void Exit(in CanaryConfig config, ref CanaryMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { memory.Total++; }
    }
}
