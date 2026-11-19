using AIBT.Burst;

namespace AIBT.StandardNodes
{
    // P11-002 stage 1, group 1: blackboard conditions of the native node library (ADR-P11-002 §5.1).
    // Every condition only reads Tree-scope keys, so each is an observer condition and can guard a
    // memory composite. A key that cannot be read makes the condition fail (as the P9-002 nodes do).

    public partial struct IntAtLeastConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardReadHandle<int> Key;

        [AibtConfigField("threshold", "UInt32", 1u)]
        public uint Threshold;
    }

    [AibtNodeDocumentation(
        "Succeeds while an int blackboard value is at least a threshold.",
        "Native/Blackboard",
        "Use in a native (Burst) tree for counters, ammo or state levels against a constant; wrap it in aibt.core.inverter for \"below\".",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.compare-int there. The threshold is not negative; compare with a key (aibt.native.int-keys-at-least) for negative bounds.",
        "native-int-at-least")]
    [AibtObserverCondition]
    [AibtBurstNode(
        "aibt.native.int-at-least", 1u, BurstNodeKind.Condition,
        typeof(IntAtLeastConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct IntAtLeastNode
    {
        public static NodeStatus Tick(in IntAtLeastConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var value) == BurstContextResult.Success
               && (long)value >= config.Threshold
                ? NodeStatus.Success
                : NodeStatus.Failure;

        public static ConditionResult Evaluate(in IntAtLeastConfig config, ref BurstObserverContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var value) == BurstContextResult.Success
               && (long)value >= config.Threshold
                ? ConditionResult.Success
                : ConditionResult.Failure;
    }

    public partial struct IntEqualsConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardReadHandle<int> Key;

        [AibtConfigField("value", "UInt32", 1u)]
        public uint Value;
    }

    [AibtNodeDocumentation(
        "Succeeds while an int blackboard value equals a constant.",
        "Native/Blackboard",
        "Use in a native (Burst) tree to branch on a state code; wrap it in aibt.core.inverter for \"not equal\".",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.compare-int there. A value above 2147483647 never matches.",
        "native-int-equals")]
    [AibtObserverCondition]
    [AibtBurstNode(
        "aibt.native.int-equals", 1u, BurstNodeKind.Condition,
        typeof(IntEqualsConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct IntEqualsNode
    {
        public static NodeStatus Tick(in IntEqualsConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var value) == BurstContextResult.Success
               && (long)value == config.Value
                ? NodeStatus.Success
                : NodeStatus.Failure;

        public static ConditionResult Evaluate(in IntEqualsConfig config, ref BurstObserverContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var value) == BurstContextResult.Success
               && (long)value == config.Value
                ? ConditionResult.Success
                : ConditionResult.Failure;
    }

    public partial struct IntKeysAtLeastConfig
    {
        [AibtConfigField("left", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("left", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardReadHandle<int> Left;

        [AibtConfigField("right", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("right", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardReadHandle<int> Right;
    }

    [AibtNodeDocumentation(
        "Succeeds while one int blackboard value is at least another.",
        "Native/Blackboard",
        "Use in a native (Burst) tree when both sides change at runtime, such as ammo against ammo-needed; either side may be negative.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.compare-int-keys there. Use aibt.native.int-at-least when one side is a constant.",
        "native-int-keys-at-least")]
    [AibtObserverCondition]
    [AibtBurstNode(
        "aibt.native.int-keys-at-least", 1u, BurstNodeKind.Condition,
        typeof(IntKeysAtLeastConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct IntKeysAtLeastNode
    {
        public static NodeStatus Tick(in IntKeysAtLeastConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.Left, out var left) == BurstContextResult.Success
               && StandardNativeShard.BurstAccess.TryRead(ref context, config.Right, out var right) == BurstContextResult.Success
               && left >= right
                ? NodeStatus.Success
                : NodeStatus.Failure;

        public static ConditionResult Evaluate(in IntKeysAtLeastConfig config, ref BurstObserverContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.Left, out var left) == BurstContextResult.Success
               && StandardNativeShard.BurstAccess.TryRead(ref context, config.Right, out var right) == BurstContextResult.Success
               && left >= right
                ? ConditionResult.Success
                : ConditionResult.Failure;
    }

    public partial struct FloatKeysAtLeastConfig
    {
        [AibtConfigField("left", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("left", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float32", 1u)]
        public BlackboardReadHandle<float> Left;

        [AibtConfigField("right", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("right", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float32", 1u)]
        public BlackboardReadHandle<float> Right;
    }

    [AibtNodeDocumentation(
        "Succeeds while one float blackboard value is at least another.",
        "Native/Blackboard",
        "Use in a native (Burst) tree when both sides change at runtime, such as health against flee-health; wrap it in aibt.core.inverter for \"below\".",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.compare-float-keys there. Use aibt.native.float-at-least when one side is a constant.",
        "native-float-keys-at-least")]
    [AibtObserverCondition]
    [AibtBurstNode(
        "aibt.native.float-keys-at-least", 1u, BurstNodeKind.Condition,
        typeof(FloatKeysAtLeastConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct FloatKeysAtLeastNode
    {
        public static NodeStatus Tick(in FloatKeysAtLeastConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.Left, out var left) == BurstContextResult.Success
               && StandardNativeShard.BurstAccess.TryRead(ref context, config.Right, out var right) == BurstContextResult.Success
               && left >= right
                ? NodeStatus.Success
                : NodeStatus.Failure;

        public static ConditionResult Evaluate(in FloatKeysAtLeastConfig config, ref BurstObserverContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.Left, out var left) == BurstContextResult.Success
               && StandardNativeShard.BurstAccess.TryRead(ref context, config.Right, out var right) == BurstContextResult.Success
               && left >= right
                ? ConditionResult.Success
                : ConditionResult.Failure;
    }

    public partial struct Float3WithinDistanceConfig
    {
        [AibtConfigField("a", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("a", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardReadHandle<Float3Value> A;

        [AibtConfigField("b", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("b", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardReadHandle<Float3Value> B;

        [AibtConfigField("distance", "Float32", 1u)]
        public float Distance;

        [AibtConfigField("flat", "Bool", 1u)]
        public bool Flat;
    }

    [AibtNodeDocumentation(
        "Succeeds while two Float3 blackboard positions are at most a distance apart; flat ignores the height (Y) difference.",
        "Native/Blackboard",
        "Use in a native (Burst) tree for range checks between positions gameplay code writes as facts, such as self-position and enemy-position.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.is-within-distance there (it reads the agent's transform). A negative distance never succeeds.",
        "native-float3-within-distance")]
    [AibtObserverCondition]
    [AibtBurstNode(
        "aibt.native.float3-within-distance", 1u, BurstNodeKind.Condition,
        typeof(Float3WithinDistanceConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct Float3WithinDistanceNode
    {
        public static NodeStatus Tick(in Float3WithinDistanceConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.A, out var a) == BurstContextResult.Success
               && StandardNativeShard.BurstAccess.TryRead(ref context, config.B, out var b) == BurstContextResult.Success
               && StandardNativeMath.IsWithin(in a, in b, config.Distance, config.Flat)
                ? NodeStatus.Success
                : NodeStatus.Failure;

        public static ConditionResult Evaluate(in Float3WithinDistanceConfig config, ref BurstObserverContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.A, out var a) == BurstContextResult.Success
               && StandardNativeShard.BurstAccess.TryRead(ref context, config.B, out var b) == BurstContextResult.Success
               && StandardNativeMath.IsWithin(in a, in b, config.Distance, config.Flat)
                ? ConditionResult.Success
                : ConditionResult.Failure;
    }

    public partial struct Float3IsSetConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardReadHandle<Float3Value> Key;
    }

    [AibtNodeDocumentation(
        "Succeeds while a Float3 blackboard value is not (0, 0, 0), the value of a key nothing has written.",
        "Native/Blackboard",
        "Use in a native (Burst) tree to check that a position fact such as last-seen or target has been filled in.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.is-vector3-set there. When the world origin is a valid position, track it with a separate bool key instead.",
        "native-float3-is-set")]
    [AibtObserverCondition]
    [AibtBurstNode(
        "aibt.native.float3-is-set", 1u, BurstNodeKind.Condition,
        typeof(Float3IsSetConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct Float3IsSetNode
    {
        public static NodeStatus Tick(in Float3IsSetConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var value) == BurstContextResult.Success
               && StandardNativeMath.IsSet(in value)
                ? NodeStatus.Success
                : NodeStatus.Failure;

        public static ConditionResult Evaluate(in Float3IsSetConfig config, ref BurstObserverContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var value) == BurstContextResult.Success
               && StandardNativeMath.IsSet(in value)
                ? ConditionResult.Success
                : ConditionResult.Failure;
    }

    /// <summary>Burst-safe arithmetic shared by the library nodes (comparisons only, no managed calls).</summary>
    internal static class StandardNativeMath
    {
        /// <summary>False for NaN and both infinities; a blackboard Float32 slot accepts only finite values.</summary>
        internal static bool IsFinite(float value) => value >= -float.MaxValue && value <= float.MaxValue;

        internal static bool IsSet(in Float3Value value) => value.X != 0f || value.Y != 0f || value.Z != 0f;

        internal static float SquaredDistance(in Float3Value a, in Float3Value b, bool flat)
        {
            var x = a.X - b.X;
            var y = flat ? 0f : a.Y - b.Y;
            var z = a.Z - b.Z;
            return x * x + y * y + z * z;
        }

        internal static bool IsWithin(in Float3Value a, in Float3Value b, float distance, bool flat)
            => distance >= 0f && SquaredDistance(in a, in b, flat) <= distance * distance;
    }
}
