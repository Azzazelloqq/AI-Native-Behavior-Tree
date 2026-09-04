using AIBT.Burst;

namespace AIBT.Authoring.BuiltInLeaves
{
    [AibtCatalogShard("aibt.stdlib.built-in-leaves", 1u)]
    public partial struct BuiltInLeafShard { }

    public partial struct EmptyLeafMemory { }

    public partial struct WaitConfig
    {
        [AibtConfigField("ticks", "UInt32", 1u)]
        public uint Ticks;
    }

    public partial struct WaitMemory
    {
        [AibtMemoryField("elapsed", "UInt32", 1u)]
        public uint Elapsed;
    }

    [AibtNodeDocumentation(
        "Remains running for a configured number of ticks, then succeeds.",
        "Core/Actions",
        "Use for a simple tick-counted delay that does not depend on wall-clock time.",
        "Do not use when a wall-clock duration is required -- use aibt.core.timeout/aibt.core.cooldown instead.",
        "stdlib-wait")]
    [AibtBurstNode(
        "aibt.stdlib.wait", 1u, BurstNodeKind.Action,
        typeof(WaitConfig), typeof(WaitMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Running)]
    public partial struct WaitNode
    {
        public static void Enter(
            in WaitConfig config,
            ref WaitMemory memory,
            ref BurstEnterContext context)
        {
            memory.Elapsed = 0;
        }

        public static NodeStatus Tick(
            in WaitConfig config,
            ref WaitMemory memory,
            ref BurstTickContext context)
        {
            memory.Elapsed++;
            return memory.Elapsed >= config.Ticks ? NodeStatus.Success : NodeStatus.Running;
        }

        public static void Abort(
            in WaitConfig config,
            ref WaitMemory memory,
            ref BurstAbortContext context,
            BurstNodeAbortReason reason) { }

        public static void Exit(
            in WaitConfig config,
            ref WaitMemory memory,
            ref BurstExitContext context,
            BurstNodeExitReason reason) { }
    }

    public partial struct RandomConditionConfig
    {
        [AibtConfigField("success-chance-percent", "UInt32", 1u)]
        public uint SuccessChancePercent;
    }

    // Non-deterministic (Deterministic=false in the [AibtBurstNode] declaration below): draws from
    // the per-instance Burst random stream ([AibtRandomStream] is required by
    // BurstNodeUsageAnalyzer/BurstNodeGenerator for any TryNextUInt32/TryNextFloat32 call, per
    // AIBT.CodeGen's own ABI enforcement) rather than a fixed injected-clock value.
    [AibtRandomStream]
    [AibtNodeDocumentation(
        "Succeeds with a configured percentage probability, drawn from the per-instance random stream.",
        "Core/Conditions",
        "Use for a lightweight probabilistic gate.",
        "Do not use when the same tree instance must reproduce an identical outcome sequence on the reference executor -- its managed System.Random draw is not bit-identical to this native stream.",
        "stdlib-random-condition")]
    [AibtBurstNode(
        "aibt.stdlib.random-condition", 1u, BurstNodeKind.Condition,
        typeof(RandomConditionConfig), typeof(EmptyLeafMemory), NodeMemoryLifetime.Activation,
        false, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct RandomConditionNode
    {
        public static void Enter(
            in RandomConditionConfig config,
            ref EmptyLeafMemory memory,
            ref BurstEnterContext context) { }

        public static NodeStatus Tick(
            in RandomConditionConfig config,
            ref EmptyLeafMemory memory,
            ref BurstTickContext context)
        {
            var result = context.TryNextFloat32(out var sample);
            return result == BurstContextResult.Success && sample * 100f < config.SuccessChancePercent
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }

        public static void Abort(
            in RandomConditionConfig config,
            ref EmptyLeafMemory memory,
            ref BurstAbortContext context,
            BurstNodeAbortReason reason) { }

        public static void Exit(
            in RandomConditionConfig config,
            ref EmptyLeafMemory memory,
            ref BurstExitContext context,
            BurstNodeExitReason reason) { }
    }
}
