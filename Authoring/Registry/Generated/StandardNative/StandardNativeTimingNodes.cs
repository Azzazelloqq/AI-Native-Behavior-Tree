using AIBT.Burst;

namespace AIBT.StandardNodes
{
    // P11-002 stage 2, group 4: timing and signal nodes of the native node library (ADR-P11-002 §5.1).
    // "Now" is the update time point in microseconds that the node context gives (the host clock on
    // ProductionTreeHost and NativeTreeAgent, tick * TickMicroseconds in TreeSimulation), the same
    // point aibt.native.wait-microseconds and aibt.core.cooldown read. A deadline lives in an Int64
    // Tree-scope key, so several branches (and gameplay code) can share it. The signal node emits on
    // the one package effect channel aibt.native.signal (ADR-P11-002 §4.5, owner decision 6).

    public partial struct WaitUntilTrueConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Bool", 1u)]
        public BlackboardReadHandle<bool> Key;

        [AibtConfigField("timeout-microseconds", "UInt32", 1u)]
        public uint TimeoutMicroseconds;
    }

    public partial struct WaitUntilTrueMemory
    {
        [AibtMemoryField("started-at", "Int64", 1u)]
        public long StartedAt;
    }

    [AibtNodeDocumentation(
        "Stays running until a bool blackboard value is true, then succeeds; with a timeout (microseconds, 0 = none) it fails once that much host time has passed since it started.",
        "Native/Timing",
        "Use in a native (Burst) tree to pause a sequence until gameplay code (SetFact) or another branch sets a flag, such as 'door-open'.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.wait-until there. To cancel running work when a flag changes, guard the branch with aibt.native.bool-is-true as the first child of a reactive composite instead.",
        "native-wait-until-true")]
    [AibtBurstNode(
        "aibt.native.wait-until-true", 1u, BurstNodeKind.Action,
        typeof(WaitUntilTrueConfig), typeof(WaitUntilTrueMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Running | BurstNodeStatusMask.Failure)]
    public partial struct WaitUntilTrueNode
    {
        public static void Enter(in WaitUntilTrueConfig config, ref WaitUntilTrueMemory memory, ref BurstEnterContext context)
        {
            memory.StartedAt = context.TryGetTimeMicroseconds(out var now) == BurstContextResult.Success
                ? now
                : long.MinValue;
        }

        public static NodeStatus Tick(in WaitUntilTrueConfig config, ref WaitUntilTrueMemory memory, ref BurstTickContext context)
        {
            if (StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var value) != BurstContextResult.Success)
                return NodeStatus.Failure;
            // The key wins over the timeout when both hold in the same update (as aibt.unity.wait-until).
            if (value) return NodeStatus.Success;
            if (config.TimeoutMicroseconds == 0u) return NodeStatus.Running;
            if (memory.StartedAt == long.MinValue || context.TryGetTimeMicroseconds(out var now) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return StandardNativeTime.HasReached(now, memory.StartedAt, config.TimeoutMicroseconds)
                ? NodeStatus.Failure
                : NodeStatus.Running;
        }
    }

    public partial struct SetDeadlineConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Int64", 1u)]
        public BlackboardWriteHandle<long> Key;

        [AibtConfigField("duration-microseconds", "UInt32", 1u)]
        public uint DurationMicroseconds;
    }

    [AibtNodeDocumentation(
        "Writes now + a duration (microseconds of host time) to an Int64 blackboard key and succeeds. Pair it with aibt.native.deadline-passed.",
        "Native/Timing",
        "Use in a native (Burst) tree to start a cooldown that several branches, or gameplay code, share through one key.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.start-cooldown there. For a cooldown private to one branch use the aibt.core.cooldown decorator.",
        "native-set-deadline")]
    [AibtBurstNode(
        "aibt.native.set-deadline", 1u, BurstNodeKind.Action,
        typeof(SetDeadlineConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct SetDeadlineNode
    {
        public static NodeStatus Tick(in SetDeadlineConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            if (context.TryGetTimeMicroseconds(out var now) != BurstContextResult.Success) return NodeStatus.Failure;
            // A deadline past long.MaxValue cannot be stored: fail without writing.
            if (now > long.MaxValue - config.DurationMicroseconds) return NodeStatus.Failure;
            var deadline = now + config.DurationMicroseconds;
            return StandardNativeShard.BurstAccess.TryWrite(ref context, config.Key, in deadline) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    public partial struct DeadlinePassedConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Int64", 1u)]
        public BlackboardReadHandle<long> Key;
    }

    [AibtNodeDocumentation(
        "Succeeds once host time has reached the deadline stored in an Int64 blackboard key (now >= key), else fails. A key never set (0) has passed.",
        "Native/Timing",
        "Use in a native (Burst) tree to gate an ability on a cooldown started by aibt.native.set-deadline; wrap it in aibt.core.inverter for \"still cooling down\".",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.is-cooldown-ready there. It reads time, so it is not an observer condition: re-check it as the first child of a reactive composite.",
        "native-deadline-passed")]
    [AibtBurstNode(
        "aibt.native.deadline-passed", 1u, BurstNodeKind.Condition,
        typeof(DeadlinePassedConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct DeadlinePassedNode
    {
        public static NodeStatus Tick(in DeadlinePassedConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => context.TryGetTimeMicroseconds(out var now) == BurstContextResult.Success
               && StandardNativeShard.BurstAccess.TryRead(ref context, config.Key, out var deadline) == BurstContextResult.Success
               && now >= deadline
                ? NodeStatus.Success
                : NodeStatus.Failure;
    }

    public partial struct SignalConfig
    {
        [AibtConfigField("code", "UInt32", 1u)]
        public uint Code;

        [AibtConfigField("signal", "GeneratedHandle", 1u)]
        [AibtCommandBinding("aibt.native.signal", "Int32", 1u)]
        public CommandHandle<int> Signal;
    }

    [AibtNodeDocumentation(
        "Emits a code (0 to 2147483647) as an Int32 effect on the package channel aibt.native.signal and succeeds; a larger code fails and emits nothing.",
        "Native/Signals",
        "Use in a native (Burst) tree to tell gameplay code to do something: handle it with NativeTreeAgent.OnEffect(\"aibt.native.signal\", (int code) => ...), or read CommandChannel<int>.Create(\"aibt.native.signal\") in TreeSimulation.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.raise-event there. For a typed payload (a float or a position) write your own node with its own command binding.",
        "native-signal")]
    [AibtBurstNode(
        "aibt.native.signal", 1u, BurstNodeKind.Action,
        typeof(SignalConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct SignalNode
    {
        public static NodeStatus Tick(in SignalConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            if (config.Code > int.MaxValue) return NodeStatus.Failure;
            var code = (int)config.Code;
            return StandardNativeShard.BurstAccess.TryEmit(ref context, config.Signal, in code) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    internal static class StandardNativeTime
    {
        /// <summary>True when <paramref name="now"/> - <paramref name="start"/> &gt;= <paramref name="duration"/>, without overflow.</summary>
        internal static bool HasReached(long now, long start, uint duration)
            => now >= start && (ulong)(now - start) >= duration;
    }
}
