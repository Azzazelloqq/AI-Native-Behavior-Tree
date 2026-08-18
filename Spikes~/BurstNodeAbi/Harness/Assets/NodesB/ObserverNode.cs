using AIBT;
using AIBT.Burst;

namespace AIBT.BurstAbi.NodesB
{
    public partial struct ObserverConfig { [AibtConfigField("a-threshold", "UInt32", 1u)] public uint Threshold; }
    public partial struct ObserverMemory { [AibtMemoryField("a-last", "Int32", 1u)] public int Last; }
    [AibtCatalogShard("observer.nodes", 1u)] public partial struct ObserverShard { }
    [AibtBurstNode("aibt.observer.condition", 1u, BurstNodeKind.Condition, typeof(ObserverConfig), typeof(ObserverMemory), NodeMemoryLifetime.Activation, true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial, BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    [AibtNodeDocumentation("Observer", "Tests", "Use", "Avoid", "observer")]
    [AibtObserverCondition]
    public partial struct ObserverNode
    {
        public static void Enter(in ObserverConfig config, ref ObserverMemory memory, ref BurstEnterContext context) { }
        public static NodeStatus Tick(in ObserverConfig config, ref ObserverMemory memory, ref BurstTickContext context) { return NodeStatus.Success; }
        public static void Abort(in ObserverConfig config, ref ObserverMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in ObserverConfig config, ref ObserverMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
        public static ConditionResult Evaluate(in ObserverConfig config, ref BurstObserverContext context) { return config.Threshold > 0 ? ConditionResult.Success : ConditionResult.Failure; }
    }
}
