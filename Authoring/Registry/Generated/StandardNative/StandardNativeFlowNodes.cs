using AIBT.Burst;

namespace AIBT.StandardNodes
{
    // P11-002 stage 1, group 3: flow leaves of the native node library (ADR-P11-002 §5.1). They read
    // and write nothing; they are placeholders and "hold" branches in native trees.

    public partial struct StandardNativeNoConfig { }

    [AibtNodeDocumentation(
        "Succeeds immediately.",
        "Native/Flow",
        "Use in a native (Burst) tree as a placeholder branch or as the last child of a selector that must not fail.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.succeed there. To force a child's result use aibt.core.succeeder.",
        "native-succeed")]
    [AibtBurstNode(
        "aibt.native.succeed", 1u, BurstNodeKind.Action,
        typeof(StandardNativeNoConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success)]
    public partial struct SucceedNode
    {
        public static NodeStatus Tick(in StandardNativeNoConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => NodeStatus.Success;
    }

    [AibtNodeDocumentation(
        "Fails immediately.",
        "Native/Flow",
        "Use in a native (Burst) tree as a placeholder branch, or to end a sequence on purpose.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.fail there. To force a child's result use aibt.core.failer.",
        "native-fail")]
    [AibtBurstNode(
        "aibt.native.fail", 1u, BurstNodeKind.Action,
        typeof(StandardNativeNoConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Failure)]
    public partial struct FailNode
    {
        public static NodeStatus Tick(in StandardNativeNoConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => NodeStatus.Failure;
    }

    [AibtNodeDocumentation(
        "Stays running until a parent aborts it.",
        "Native/Flow",
        "Use in a native (Burst) tree as the last child of a reactive selector, so the tree keeps running while nothing else applies.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.idle there. To pause for a time use aibt.native.wait-microseconds.",
        "native-idle")]
    [AibtBurstNode(
        "aibt.native.idle", 1u, BurstNodeKind.Action,
        typeof(StandardNativeNoConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Running)]
    public partial struct IdleNode
    {
        public static NodeStatus Tick(in StandardNativeNoConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
            => NodeStatus.Running;
    }
}
