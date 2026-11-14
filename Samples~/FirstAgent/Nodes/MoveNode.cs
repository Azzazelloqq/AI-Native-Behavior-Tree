using AIBT.Burst;
using AIBT.Native;

namespace AIBT.Samples.FirstAgent
{
    [AibtCatalogShard("aibt.samples.first-agent", 1u)]
    public partial struct FirstAgentShard { }

    // Short AIBT.Native forms: value types come from the field types, the blackboard access from the
    // handle type (Tree scope by default).
    public partial struct MoveConfig
    {
        [AibtParam("speed")] public float Speed;
        [AibtParam("stop-distance")] public float StopDistance;
        [AibtKey("distance")] public BlackboardReadHandle<float> Distance;
        [AibtCommand("move-speed")] public CommandHandle<float> MoveSpeed;
    }

    public partial struct MoveMemory { }

    [AibtNodeDocumentation("Requests movement until the supplied distance reaches the stopping radius.",
        "Samples/First Agent", "Use to learn external facts and committed effects.", "Not a navigation system.", "first-agent-move")]
    // An Action defaults to version 1, abort-only cancellation, low cost and Success/Failure/Running;
    // its configuration and memory types are taken from Tick. Enter, Abort and Exit are optional.
    [AibtAction("aibt.samples.first-agent.move")]
    public partial struct MoveNode
    {
        public static NodeStatus Tick(in MoveConfig config, ref MoveMemory memory, ref BurstTickContext context)
        {
            if (config.Speed <= 0f || config.StopDistance < 0f) return NodeStatus.Failure;
            if (FirstAgentShard.BurstAccess.TryRead(ref context, config.Distance, out var distance) != BurstContextResult.Success)
                return NodeStatus.Failure;
            if (distance <= config.StopDistance) return NodeStatus.Success;
            return FirstAgentShard.BurstAccess.TryEmit(ref context, config.MoveSpeed, in config.Speed) == BurstContextResult.Success
                ? NodeStatus.Running : NodeStatus.Failure;
        }
    }
}
