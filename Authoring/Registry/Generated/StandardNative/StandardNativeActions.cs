using AIBT.Burst;

namespace AIBT.StandardNodes
{
    // P11-002 stage 1, group 2: blackboard actions of the native node library (ADR-P11-002 §5.1).
    // Each action finishes in the tick it starts: Success after its write, Failure when a key cannot
    // be read or the value cannot be written. A value a Tree-scope slot would refuse (a non-finite
    // float, an int constant above 2^31-1) is never written: the node fails and the key keeps its value.

    public partial struct SetBoolConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Bool", 1u)]
        public BlackboardWriteHandle<bool> Key;

        [AibtConfigField("value", "Bool", 1u)]
        public bool Value;
    }

    [AibtNodeDocumentation(
        "Writes a constant to a bool blackboard key and succeeds.",
        "Native/Blackboard",
        "Use in a native (Burst) tree to raise or clear a flag for gameplay code or later nodes.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.set-bool there.",
        "native-set-bool")]
    [AibtBurstNode(
        "aibt.native.set-bool", 1u, BurstNodeKind.Action,
        typeof(SetBoolConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct SetBoolNode
    {
        public static NodeStatus Tick(in SetBoolConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            var value = config.Value;
            return StandardNativeShard.BurstAccess.TryWrite(ref context, config.Key, in value) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    public partial struct SetIntConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardWriteHandle<int> Key;

        [AibtConfigField("value", "UInt32", 1u)]
        public uint Value;
    }

    [AibtNodeDocumentation(
        "Writes a constant (0 to 2147483647) to an int blackboard key and succeeds; a larger value fails without writing.",
        "Native/Blackboard",
        "Use in a native (Burst) tree to record a state code or reset a counter.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.set-int there. For a negative value copy it from a key with aibt.native.copy-int.",
        "native-set-int")]
    [AibtBurstNode(
        "aibt.native.set-int", 1u, BurstNodeKind.Action,
        typeof(SetIntConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct SetIntNode
    {
        public static NodeStatus Tick(in SetIntConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            if (config.Value > int.MaxValue) return NodeStatus.Failure;
            var value = (int)config.Value;
            return StandardNativeShard.BurstAccess.TryWrite(ref context, config.Key, in value) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    public partial struct SetFloat3Config
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardWriteHandle<Float3Value> Key;

        [AibtConfigField("x", "Float32", 1u)]
        public float X;

        [AibtConfigField("y", "Float32", 1u)]
        public float Y;

        [AibtConfigField("z", "Float32", 1u)]
        public float Z;
    }

    [AibtNodeDocumentation(
        "Writes a constant (x, y, z) to a Float3 blackboard key and succeeds.",
        "Native/Blackboard",
        "Use in a native (Burst) tree to set a fixed position or direction, or to clear a position to (0, 0, 0).",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.set-vector3 there.",
        "native-set-float3")]
    [AibtBurstNode(
        "aibt.native.set-float3", 1u, BurstNodeKind.Action,
        typeof(SetFloat3Config), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct SetFloat3Node
    {
        public static NodeStatus Tick(in SetFloat3Config config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            if (!StandardNativeMath.IsFinite(config.X) || !StandardNativeMath.IsFinite(config.Y) || !StandardNativeMath.IsFinite(config.Z))
                return NodeStatus.Failure;
            var value = new Float3Value(config.X, config.Y, config.Z);
            return StandardNativeShard.BurstAccess.TryWrite(ref context, config.Key, in value) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    public partial struct CopyBoolConfig
    {
        [AibtConfigField("from", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("from", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Bool", 1u)]
        public BlackboardReadHandle<bool> From;

        [AibtConfigField("to", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("to", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Bool", 1u)]
        public BlackboardWriteHandle<bool> To;
    }

    [AibtNodeDocumentation(
        "Copies one bool blackboard value into another key and succeeds.",
        "Native/Blackboard",
        "Use in a native (Burst) tree to latch a fact gameplay code writes into a key the tree owns.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.copy-bool there.",
        "native-copy-bool")]
    [AibtBurstNode(
        "aibt.native.copy-bool", 1u, BurstNodeKind.Action,
        typeof(CopyBoolConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct CopyBoolNode
    {
        public static NodeStatus Tick(in CopyBoolConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.From, out var value) == BurstContextResult.Success
               && StandardNativeShard.BurstAccess.TryWrite(ref context, config.To, in value) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
    }

    public partial struct CopyIntConfig
    {
        [AibtConfigField("from", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("from", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardReadHandle<int> From;

        [AibtConfigField("to", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("to", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardWriteHandle<int> To;
    }

    [AibtNodeDocumentation(
        "Copies one int blackboard value into another key and succeeds.",
        "Native/Blackboard",
        "Use in a native (Burst) tree to remember a value, or to write a negative constant held in a key.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.copy-int there.",
        "native-copy-int")]
    [AibtBurstNode(
        "aibt.native.copy-int", 1u, BurstNodeKind.Action,
        typeof(CopyIntConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct CopyIntNode
    {
        public static NodeStatus Tick(in CopyIntConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.From, out var value) == BurstContextResult.Success
               && StandardNativeShard.BurstAccess.TryWrite(ref context, config.To, in value) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
    }

    public partial struct CopyFloatConfig
    {
        [AibtConfigField("from", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("from", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float32", 1u)]
        public BlackboardReadHandle<float> From;

        [AibtConfigField("to", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("to", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Float32", 1u)]
        public BlackboardWriteHandle<float> To;
    }

    [AibtNodeDocumentation(
        "Copies one float blackboard value into another key and succeeds.",
        "Native/Blackboard",
        "Use in a native (Burst) tree to remember a value, for example the health when a fight started.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.copy-float there.",
        "native-copy-float")]
    [AibtBurstNode(
        "aibt.native.copy-float", 1u, BurstNodeKind.Action,
        typeof(CopyFloatConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct CopyFloatNode
    {
        public static NodeStatus Tick(in CopyFloatConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.From, out var value) == BurstContextResult.Success
               && StandardNativeShard.BurstAccess.TryWrite(ref context, config.To, in value) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
    }

    public partial struct CopyFloat3Config
    {
        [AibtConfigField("from", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("from", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardReadHandle<Float3Value> From;

        [AibtConfigField("to", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("to", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardWriteHandle<Float3Value> To;
    }

    [AibtNodeDocumentation(
        "Copies one Float3 blackboard value into another key and succeeds.",
        "Native/Blackboard",
        "Use in a native (Burst) tree to remember a position, for example the last place the player was seen.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.copy-vector3 there.",
        "native-copy-float3")]
    [AibtBurstNode(
        "aibt.native.copy-float3", 1u, BurstNodeKind.Action,
        typeof(CopyFloat3Config), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct CopyFloat3Node
    {
        public static NodeStatus Tick(in CopyFloat3Config config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => StandardNativeShard.BurstAccess.TryRead(ref context, config.From, out var value) == BurstContextResult.Success
               && StandardNativeShard.BurstAccess.TryWrite(ref context, config.To, in value) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
    }

    public partial struct IncrementIntConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.ReadWrite, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardReadWriteHandle<int> Key;

        [AibtConfigField("amount", "UInt32", 1u)]
        public uint Amount;
    }

    [AibtNodeDocumentation(
        "Adds an amount to an int blackboard value and succeeds; the sum wraps around like unchecked 32-bit arithmetic.",
        "Native/Blackboard",
        "Use in a native (Burst) tree for counters such as patrol steps or attempts. An amount of 4294967295 subtracts one.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.increment-int there.",
        "native-increment-int")]
    [AibtBurstNode(
        "aibt.native.increment-int", 1u, BurstNodeKind.Action,
        typeof(IncrementIntConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct IncrementIntNode
    {
        public static NodeStatus Tick(in IncrementIntConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            if (StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var value) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var sum = unchecked((int)((uint)value + config.Amount));
            return StandardNativeShard.BurstAccess.TryWrite(ref context, config.Key, in sum) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    public partial struct IncrementFloatConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.ReadWrite, BlackboardScope.Tree, "Float32", 1u)]
        public BlackboardReadWriteHandle<float> Key;

        [AibtConfigField("amount", "Float32", 1u)]
        public float Amount;
    }

    [AibtNodeDocumentation(
        "Adds an amount to a float blackboard value and succeeds; a negative amount decrements. A sum too large for a float fails without writing.",
        "Native/Blackboard",
        "Use in a native (Burst) tree for meters such as heat, fatigue or alertness.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.increment-float there.",
        "native-increment-float")]
    [AibtBurstNode(
        "aibt.native.increment-float", 1u, BurstNodeKind.Action,
        typeof(IncrementFloatConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct IncrementFloatNode
    {
        public static NodeStatus Tick(in IncrementFloatConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            if (StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var value) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var sum = value + config.Amount;
            if (!StandardNativeMath.IsFinite(sum)) return NodeStatus.Failure;
            return StandardNativeShard.BurstAccess.TryWrite(ref context, config.Key, in sum) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    public partial struct ToggleBoolConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.ReadWrite, BlackboardScope.Tree, "Bool", 1u)]
        public BlackboardReadWriteHandle<bool> Key;
    }

    [AibtNodeDocumentation(
        "Inverts a bool blackboard value and succeeds.",
        "Native/Blackboard",
        "Use in a native (Burst) tree to alternate between two branches on each pass.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.toggle-bool there.",
        "native-toggle-bool")]
    [AibtBurstNode(
        "aibt.native.toggle-bool", 1u, BurstNodeKind.Action,
        typeof(ToggleBoolConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct ToggleBoolNode
    {
        public static NodeStatus Tick(in ToggleBoolConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            if (StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var value) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var inverted = !value;
            return StandardNativeShard.BurstAccess.TryWrite(ref context, config.Key, in inverted) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    public partial struct Float3DistanceConfig
    {
        [AibtConfigField("a", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("a", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardReadHandle<Float3Value> A;

        [AibtConfigField("b", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("b", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardReadHandle<Float3Value> B;

        [AibtConfigField("result", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("result", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Float32", 1u)]
        public BlackboardWriteHandle<float> Result;

        [AibtConfigField("flat", "Bool", 1u)]
        public bool Flat;
    }

    [AibtNodeDocumentation(
        "Writes the distance between two Float3 blackboard values to a float key and succeeds; flat ignores the height (Y) difference.",
        "Native/Blackboard",
        "Use in a native (Burst) tree when later nodes need the number itself, for example to compare it with a key that changes.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.distance there. Use aibt.native.float3-within-distance to just test a range.",
        "native-float3-distance")]
    [AibtBurstNode(
        "aibt.native.float3-distance", 1u, BurstNodeKind.Action,
        typeof(Float3DistanceConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct Float3DistanceNode
    {
        public static NodeStatus Tick(in Float3DistanceConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            if (StandardNativeShard.BurstAccess.TryRead(ref context, config.A, out var a) != BurstContextResult.Success
                || StandardNativeShard.BurstAccess.TryRead(ref context, config.B, out var b) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var squared = StandardNativeMath.SquaredDistance(in a, in b, config.Flat);
            if (!StandardNativeMath.IsFinite(squared)) return NodeStatus.Failure;
            var distance = (float)System.Math.Sqrt(squared);
            return StandardNativeShard.BurstAccess.TryWrite(ref context, config.Result, in distance) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }
}
