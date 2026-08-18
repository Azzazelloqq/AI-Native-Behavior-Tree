using AIBT;
using AIBT.Burst;

namespace AIBT.BurstAbi.RuntimeBuiltins
{
    public partial struct RuntimeBuiltinConfig { }
    public partial struct RuntimeBuiltinMemory { }
    [AibtCatalogShard("runtime-builtins.fixture", 1u)] public partial struct RuntimeBuiltinsShard { }
    [AibtBurstNode("aibt.fixture.runtime.builtin", 1u, BurstNodeKind.Action, typeof(RuntimeBuiltinConfig), typeof(RuntimeBuiltinMemory),
        NodeMemoryLifetime.Activation, true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial, BurstNodeStatusMask.Success)]
    [AibtNodeDocumentation("Feasibility-only Runtime built-in metadata fixture.", "Tests", "P2-001 isolated registry hashing.", "Production behavior.", "runtime-fixture")]
    public partial struct RuntimeBuiltinNode
    {
        public static void Enter(in RuntimeBuiltinConfig config, ref RuntimeBuiltinMemory memory, ref BurstEnterContext context) { }
        public static NodeStatus Tick(in RuntimeBuiltinConfig config, ref RuntimeBuiltinMemory memory, ref BurstTickContext context) { return NodeStatus.Success; }
        public static void Abort(in RuntimeBuiltinConfig config, ref RuntimeBuiltinMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in RuntimeBuiltinConfig config, ref RuntimeBuiltinMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }
}
