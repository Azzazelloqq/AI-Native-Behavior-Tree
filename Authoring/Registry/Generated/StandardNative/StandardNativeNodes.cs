using AIBT.Burst;

namespace AIBT.StandardNodes
{
    // P9-002 native standard shard: blackboard conditions and setters through generated bindings,
    // and a clock-based wait. Native-only: the reference executor has no GeneratedHandle support,
    // so these run in a generated catalog (ProductionTreeHost), not in a BehaviorTreeAgent. A
    // project includes them by listing typeof(StandardNativeShard) in its own [AibtCatalogSet].
    // Naming (owner decision, P9-004): aibt.native.* is for native-only nodes; aibt.stdlib.* stays
    // reserved for nodes with byte-identical manifests on both execution paths (P7-028).
    [AibtCatalogShard("aibt.native.standard", 1u)]
    public partial struct StandardNativeShard { }

    public partial struct StandardNativeNoMemory { }

    public partial struct BoolIsTrueConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Bool", 1u)]
        public BlackboardReadHandle<bool> Key;
    }

    [AibtNodeDocumentation(
        "Succeeds while a bool blackboard value is true.",
        "Native/Blackboard",
        "Use in a native (Burst) tree to gate a branch on a flag gameplay code writes.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.is-true there.",
        "native-bool-is-true")]
    [AibtBurstNode(
        "aibt.native.bool-is-true", 1u, BurstNodeKind.Condition,
        typeof(BoolIsTrueConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct BoolIsTrueNode
    {
        public static void Enter(in BoolIsTrueConfig config, ref StandardNativeNoMemory memory, ref BurstEnterContext context) { }

        public static NodeStatus Tick(in BoolIsTrueConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            return StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var value) == BurstContextResult.Success && value
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }

        public static void Abort(in BoolIsTrueConfig config, ref StandardNativeNoMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }

        public static void Exit(in BoolIsTrueConfig config, ref StandardNativeNoMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }

    public partial struct FloatAtLeastConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float32", 1u)]
        public BlackboardReadHandle<float> Key;

        [AibtConfigField("threshold", "Float32", 1u)]
        public float Threshold;
    }

    [AibtNodeDocumentation(
        "Succeeds while a float blackboard value is at least a threshold.",
        "Native/Blackboard",
        "Use in a native (Burst) tree for checks such as health against a constant.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.compare-float there.",
        "native-float-at-least")]
    [AibtBurstNode(
        "aibt.native.float-at-least", 1u, BurstNodeKind.Condition,
        typeof(FloatAtLeastConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct FloatAtLeastNode
    {
        public static void Enter(in FloatAtLeastConfig config, ref StandardNativeNoMemory memory, ref BurstEnterContext context) { }

        public static NodeStatus Tick(in FloatAtLeastConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            return StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var value) == BurstContextResult.Success
                && value >= config.Threshold
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }

        public static void Abort(in FloatAtLeastConfig config, ref StandardNativeNoMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }

        public static void Exit(in FloatAtLeastConfig config, ref StandardNativeNoMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }

    public partial struct SetFloatConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Float32", 1u)]
        public BlackboardWriteHandle<float> Key;

        [AibtConfigField("value", "Float32", 1u)]
        public float Value;
    }

    [AibtNodeDocumentation(
        "Writes a constant to a float blackboard key and succeeds.",
        "Native/Blackboard",
        "Use in a native (Burst) tree to record a state for gameplay code or later nodes.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.set-float there.",
        "native-set-float")]
    [AibtBurstNode(
        "aibt.native.set-float", 1u, BurstNodeKind.Action,
        typeof(SetFloatConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct SetFloatNode
    {
        public static void Enter(in SetFloatConfig config, ref StandardNativeNoMemory memory, ref BurstEnterContext context) { }

        public static NodeStatus Tick(in SetFloatConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            var value = config.Value;
            return StandardNativeShard.BurstAccess.TryWrite(ref context, config.Key, in value) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }

        public static void Abort(in SetFloatConfig config, ref StandardNativeNoMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }

        public static void Exit(in SetFloatConfig config, ref StandardNativeNoMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }

    public partial struct WaitMicrosecondsConfig
    {
        [AibtConfigField("duration-microseconds", "UInt32", 1u)]
        public uint DurationMicroseconds;
    }

    public partial struct WaitMicrosecondsMemory
    {
        [AibtMemoryField("deadline", "Int64", 1u)]
        public long Deadline;
    }

    [AibtNodeDocumentation(
        "Stays running until a duration of host time has passed since it started, then succeeds.",
        "Native/Flow",
        "Use in a native (Burst) tree for pauses in host time; aibt.stdlib.wait counts ticks instead.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.wait-seconds there.",
        "native-wait-microseconds")]
    [AibtBurstNode(
        "aibt.native.wait-microseconds", 1u, BurstNodeKind.Action,
        typeof(WaitMicrosecondsConfig), typeof(WaitMicrosecondsMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Running | BurstNodeStatusMask.Failure)]
    public partial struct WaitMicrosecondsNode
    {
        public static void Enter(in WaitMicrosecondsConfig config, ref WaitMicrosecondsMemory memory, ref BurstEnterContext context)
        {
            memory.Deadline = context.TryGetTimeMicroseconds(out var now) == BurstContextResult.Success
                ? now + config.DurationMicroseconds
                : long.MinValue;
        }

        public static NodeStatus Tick(in WaitMicrosecondsConfig config, ref WaitMicrosecondsMemory memory, ref BurstTickContext context)
        {
            if (memory.Deadline == long.MinValue || context.TryGetTimeMicroseconds(out var now) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return now >= memory.Deadline ? NodeStatus.Success : NodeStatus.Running;
        }

        public static void Abort(in WaitMicrosecondsConfig config, ref WaitMicrosecondsMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }

        public static void Exit(in WaitMicrosecondsConfig config, ref WaitMicrosecondsMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }
}
