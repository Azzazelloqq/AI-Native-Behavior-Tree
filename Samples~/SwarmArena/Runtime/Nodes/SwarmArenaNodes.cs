using AIBT.Burst;

namespace AIBT.Samples.SwarmArena
{
    /// <summary>Generated catalog shard containing only Swarm Arena sample nodes.</summary>
    [AibtCatalogShard("aibt.samples.swarm-arena", 1u)]
    public partial struct SwarmArenaShard { }

    public partial struct WithinRadiusConfig
    {
        [AibtConfigField("source", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("agent-position", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardReadHandle<Float3Value> Source;

        [AibtConfigField("target", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("goal-position", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardReadHandle<Float3Value> Target;

        // The public ABI deliberately accepts UInt32 (rather than Float32) configuration scalars.
        // Millimetres keep authored range values readable without adding a sample-only value type.
        [AibtConfigField("radius-millimeters", "UInt32", 1u)]
        public uint RadiusMillimeters;
    }

    public partial struct SwarmArenaEmptyMemory { }

    [AibtNodeDocumentation(
        "Succeeds when two externally supplied Float3 positions are within an authored radius.",
        "Samples/Swarm Arena/Conditions",
        "Use as the threat, sensing, or attack-range guard in the Swarm Arena priority tree.",
        "Do not use for navigation or a side effect.",
        "swarm-within-radius")]
    [AibtObserverCondition]
    [AibtBurstNode(
        "aibt.samples.swarm.within-radius", 1u, BurstNodeKind.Condition,
        typeof(WithinRadiusConfig), typeof(SwarmArenaEmptyMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct WithinRadiusNode
    {
        public static void Enter(
            in WithinRadiusConfig config,
            ref SwarmArenaEmptyMemory memory,
            ref BurstEnterContext context) { }

        public static NodeStatus Tick(
            in WithinRadiusConfig config,
            ref SwarmArenaEmptyMemory memory,
            ref BurstTickContext context)
        {
            if (SwarmArenaShard.BurstAccess.TryRead(ref context, config.Source, out var source) != BurstContextResult.Success
                || SwarmArenaShard.BurstAccess.TryRead(ref context, config.Target, out var target) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return IsWithin(in source, in target, config.RadiusMillimeters)
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }

        public static void Abort(
            in WithinRadiusConfig config,
            ref SwarmArenaEmptyMemory memory,
            ref BurstAbortContext context,
            BurstNodeAbortReason reason) { }

        public static void Exit(
            in WithinRadiusConfig config,
            ref SwarmArenaEmptyMemory memory,
            ref BurstExitContext context,
            BurstNodeExitReason reason) { }

        public static ConditionResult Evaluate(
            in WithinRadiusConfig config,
            ref BurstObserverContext context)
        {
            if (SwarmArenaShard.BurstAccess.TryRead(ref context, config.Source, out var source) != BurstContextResult.Success
                || SwarmArenaShard.BurstAccess.TryRead(ref context, config.Target, out var target) != BurstContextResult.Success)
                return ConditionResult.Failure;
            return IsWithin(in source, in target, config.RadiusMillimeters)
                ? ConditionResult.Success
                : ConditionResult.Failure;
        }

        private static bool IsWithin(in Float3Value source, in Float3Value target, uint radiusMillimeters)
        {
            var x = source.X - target.X;
            var y = source.Y - target.Y;
            var z = source.Z - target.Z;
            var radius = radiusMillimeters * 0.001f;
            return x * x + y * y + z * z <= radius * radius;
        }
    }

    public partial struct SteerConfig
    {
        [AibtConfigField("source", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("agent-position", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardReadHandle<Float3Value> Source;

        [AibtConfigField("target", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("goal-position", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardReadHandle<Float3Value> Target;

        [AibtConfigField("away", "Bool", 1u)]
        public bool Away;

        [AibtConfigField("visible-state", "UInt32", 1u)]
        public uint VisibleState;

        [AibtConfigField("move-intent", "GeneratedHandle", 1u)]
        [AibtCommandBinding("move-intent", "Float3", 1u)]
        public CommandHandle<Float3Value> MoveIntent;

        [AibtConfigField("state-intent", "GeneratedHandle", 1u)]
        [AibtCommandBinding("state-intent", "Int32", 1u)]
        public CommandHandle<int> StateIntent;
    }

    [AibtNodeDocumentation(
        "Emits one normalized move intent and one visible state intent toward or away from a goal.",
        "Samples/Swarm Arena/Actions",
        "Use for patrol, pursuit, and flee without duplicating action node types.",
        "Do not use for pathfinding or to mutate Unity transforms from a Burst kernel.",
        "swarm-steer")]
    [AibtBurstNode(
        "aibt.samples.swarm.steer", 1u, BurstNodeKind.Action,
        typeof(SteerConfig), typeof(SwarmArenaEmptyMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Low,
        BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running)]
    public partial struct SteerNode
    {
        public static void Enter(
            in SteerConfig config,
            ref SwarmArenaEmptyMemory memory,
            ref BurstEnterContext context) { }

        public static NodeStatus Tick(
            in SteerConfig config,
            ref SwarmArenaEmptyMemory memory,
            ref BurstTickContext context)
        {
            if (SwarmArenaShard.BurstAccess.TryRead(ref context, config.Source, out var source) != BurstContextResult.Success
                || SwarmArenaShard.BurstAccess.TryRead(ref context, config.Target, out var target) != BurstContextResult.Success)
                return NodeStatus.Failure;

            var x = target.X - source.X;
            var y = target.Y - source.Y;
            var z = target.Z - source.Z;
            if (config.Away)
            {
                x = -x;
                y = -y;
                z = -z;
            }
            // A Manhattan normalization stays entirely in the documented scalar Burst ABI.
            // The showcase needs a stable steering direction, not Euclidean speed preservation.
            var magnitude = Abs(x) + Abs(y) + Abs(z);
            if (magnitude > 0.000001f)
            {
                var inverseLength = 1f / magnitude;
                x *= inverseLength;
                y *= inverseLength;
                z *= inverseLength;
            }
            else
            {
                x = 0f;
                y = 0f;
                z = 0f;
            }

            var move = new Float3Value(x, y, z);
            var state = unchecked((int)config.VisibleState);
            return SwarmArenaShard.BurstAccess.TryEmit(ref context, config.MoveIntent, in move) == BurstContextResult.Success
                   && SwarmArenaShard.BurstAccess.TryEmit(ref context, config.StateIntent, in state) == BurstContextResult.Success
                ? NodeStatus.Running
                : NodeStatus.Failure;
        }

        public static void Abort(
            in SteerConfig config,
            ref SwarmArenaEmptyMemory memory,
            ref BurstAbortContext context,
            BurstNodeAbortReason reason) { }

        public static void Exit(
            in SteerConfig config,
            ref SwarmArenaEmptyMemory memory,
            ref BurstExitContext context,
            BurstNodeExitReason reason) { }

        private static float Abs(float value) => value < 0f ? -value : value;
    }

    public partial struct AttackWithCooldownConfig
    {
        [AibtConfigField("cooldown-microseconds", "UInt32", 1u)]
        public uint CooldownMicroseconds;

        [AibtConfigField("damage", "UInt32", 1u)]
        public uint Damage;

        [AibtConfigField("visible-state", "UInt32", 1u)]
        public uint VisibleState;

        [AibtConfigField("attack", "GeneratedHandle", 1u)]
        [AibtCommandBinding("attack", "Int32", 1u)]
        public CommandHandle<int> Attack;

        [AibtConfigField("state-intent", "GeneratedHandle", 1u)]
        [AibtCommandBinding("state-intent", "Int32", 1u)]
        public CommandHandle<int> StateIntent;
    }

    public partial struct AttackWithCooldownMemory
    {
        [AibtMemoryField("next-permitted-microseconds", "Int64", 1u)]
        public long NextPermittedMicroseconds;
    }

    [AibtNodeDocumentation(
        "Emits an attack and visible state only when the deterministic logical clock reaches its cooldown boundary.",
        "Samples/Swarm Arena/Actions",
        "Use to demonstrate instance-local cooldown memory and typed committed effects.",
        "Do not use for asynchronous damage resolution or Unity-side combat logic.",
        "swarm-attack-with-cooldown")]
    [AibtBurstNode(
        "aibt.samples.swarm.attack-with-cooldown", 1u, BurstNodeKind.Action,
        typeof(AttackWithCooldownConfig), typeof(AttackWithCooldownMemory), NodeMemoryLifetime.Instance,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Low,
        BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running)]
    public partial struct AttackWithCooldownNode
    {
        public static void Enter(
            in AttackWithCooldownConfig config,
            ref AttackWithCooldownMemory memory,
            ref BurstEnterContext context) { }

        public static NodeStatus Tick(
            in AttackWithCooldownConfig config,
            ref AttackWithCooldownMemory memory,
            ref BurstTickContext context)
        {
            if (context.TryGetTimeMicroseconds(out var now) != BurstContextResult.Success)
                return NodeStatus.Failure;
            if (now < memory.NextPermittedMicroseconds) return NodeStatus.Running;

            var visibleState = unchecked((int)config.VisibleState);
            var damage = unchecked((int)config.Damage);
            if (SwarmArenaShard.BurstAccess.TryEmit(ref context, config.StateIntent, in visibleState) != BurstContextResult.Success
                || SwarmArenaShard.BurstAccess.TryEmit(ref context, config.Attack, in damage) != BurstContextResult.Success)
                return NodeStatus.Failure;

            var cooldown = (long)config.CooldownMicroseconds;
            memory.NextPermittedMicroseconds = now > long.MaxValue - cooldown
                ? long.MaxValue
                : now + cooldown;
            return NodeStatus.Running;
        }

        public static void Abort(
            in AttackWithCooldownConfig config,
            ref AttackWithCooldownMemory memory,
            ref BurstAbortContext context,
            BurstNodeAbortReason reason) { }

        public static void Exit(
            in AttackWithCooldownConfig config,
            ref AttackWithCooldownMemory memory,
            ref BurstExitContext context,
            BurstNodeExitReason reason) { }
    }
}
