using AIBT.Burst;

namespace AIBT.Samples.FirstAgent
{
    [AibtCatalogShard("aibt.samples.first-agent", 1u)]
    public partial struct FirstAgentShard { }

    public partial struct MoveConfig
    {
        [AibtConfigField("speed", "Float32", 1u)] public float Speed;
        [AibtConfigField("stop-distance", "Float32", 1u)] public float StopDistance;
        [AibtConfigField("distance", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("distance", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float32", 1u)]
        public BlackboardReadHandle<float> Distance;
        [AibtConfigField("move-speed", "GeneratedHandle", 1u)]
        [AibtCommandBinding("move-speed", "Float32", 1u)]
        public CommandHandle<float> MoveSpeed;
    }

    public partial struct MoveMemory { }

    [AibtNodeDocumentation("Requests movement until the supplied distance reaches the stopping radius.",
        "Samples/First Agent", "Use to learn external facts and committed effects.", "Not a navigation system.", "first-agent-move")]
    [AibtBurstNode("aibt.samples.first-agent.move", 1u, BurstNodeKind.Action,
        typeof(MoveConfig), typeof(MoveMemory), NodeMemoryLifetime.Activation, true,
        BurstCancellationMode.AbortOnly, BurstNodeCost.Low,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running)]
    public partial struct MoveNode
    {
        public static void Enter(in MoveConfig config, ref MoveMemory memory, ref BurstEnterContext context) { }
        public static NodeStatus Tick(in MoveConfig config, ref MoveMemory memory, ref BurstTickContext context)
        {
            if (config.Speed <= 0f || config.StopDistance < 0f) return NodeStatus.Failure;
            if (FirstAgentShard.BurstAccess.TryRead(ref context, config.Distance, out var distance) != BurstContextResult.Success)
                return NodeStatus.Failure;
            if (distance <= config.StopDistance) return NodeStatus.Success;
            return FirstAgentShard.BurstAccess.TryEmit(ref context, config.MoveSpeed, in config.Speed) == BurstContextResult.Success
                ? NodeStatus.Running : NodeStatus.Failure;
        }
        public static void Abort(in MoveConfig config, ref MoveMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in MoveConfig config, ref MoveMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }
}
