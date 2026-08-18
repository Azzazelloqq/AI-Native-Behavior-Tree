using AIBT.Burst;

namespace AIBT.Samples.BurstNodes
{
    [AibtCatalogShard("aibt.samples.public-burst-nodes", 1u)]
    public partial struct PublicBurstNodeShard { }

    public partial struct ThresholdConditionConfig
    {
        [AibtConfigField("current", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("current", BurstBlackboardAccess.Read, BlackboardScope.Tree, "UInt32", 1u)]
        public BlackboardReadHandle<uint> Current;

        [AibtConfigField("minimum", "UInt32", 1u)]
        public uint Minimum;
    }

    public partial struct EmptyNodeMemory { }

    [AibtNodeDocumentation(
        "Succeeds when a typed blackboard value reaches a configured threshold.",
        "Samples/Conditions",
        "Use for a deterministic typed blackboard condition.",
        "Do not use when the value must be changed.",
        "public-threshold-condition")]
    [AibtObserverCondition]
    [AibtBurstNode(
        "aibt.samples.threshold-condition", 1u, BurstNodeKind.Condition,
        typeof(ThresholdConditionConfig), typeof(EmptyNodeMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct ThresholdConditionNode
    {
        public static void Enter(
            in ThresholdConditionConfig config,
            ref EmptyNodeMemory memory,
            ref BurstEnterContext context) { }

        public static NodeStatus Tick(
            in ThresholdConditionConfig config,
            ref EmptyNodeMemory memory,
            ref BurstTickContext context)
        {
            var result = PublicBurstNodeShard.BurstAccess.TryRead(ref context, config.Current, out var current);
            return result == BurstContextResult.Success && current >= config.Minimum
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }

        public static void Abort(
            in ThresholdConditionConfig config,
            ref EmptyNodeMemory memory,
            ref BurstAbortContext context,
            BurstNodeAbortReason reason) { }

        public static void Exit(
            in ThresholdConditionConfig config,
            ref EmptyNodeMemory memory,
            ref BurstExitContext context,
            BurstNodeExitReason reason) { }

        public static ConditionResult Evaluate(
            in ThresholdConditionConfig config,
            ref BurstObserverContext context)
        {
            var result = PublicBurstNodeShard.BurstAccess.TryRead(ref context, config.Current, out var current);
            return result == BurstContextResult.Success && current >= config.Minimum
                ? ConditionResult.Success
                : ConditionResult.Failure;
        }
    }

    public partial struct AsyncWriteActionConfig
    {
        [AibtConfigField("source", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("source", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardReadHandle<int> Source;

        [AibtConfigField("destination", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("destination", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardWriteHandle<int> Destination;

        [AibtConfigField("effect", "GeneratedHandle", 1u)]
        [AibtCommandBinding("effect", "Int32", 1u)]
        public CommandHandle<int> Effect;

        [AibtConfigField("operation", "GeneratedHandle", 1u)]
        [AibtAsyncOperationBinding("operation", "Int32", 1u, "Int32", 1u)]
        public AsyncOperationHandle<int, int> Operation;

        [AibtConfigField("completion", "GeneratedHandle", 1u)]
        [AibtCompletionBinding("completion", "Int32", 1u)]
        public CompletionHandle<int> Completion;
    }

    public partial struct AsyncWriteActionMemory
    {
        [AibtMemoryField("operation-id", "OperationId", 1u)]
        public OperationId OperationId;

        [AibtMemoryField("started", "Bool", 1u)]
        public bool Started;

        [AibtMemoryField("value", "Int32", 1u)]
        public int Value;
    }

    [AibtNodeDocumentation(
        "Copies a typed value, emits a command, and waits for an asynchronous completion.",
        "Samples/Actions",
        "Use to demonstrate Running, completion, and cancellation with public Burst APIs.",
        "Do not use when no asynchronous operation is required.",
        "public-async-write-action")]
    [AibtBurstNode(
        "aibt.samples.async-write-action", 1u, BurstNodeKind.Action,
        typeof(AsyncWriteActionConfig), typeof(AsyncWriteActionMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.Command, BurstNodeCost.Low,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running)]
    public partial struct AsyncWriteActionNode
    {
        public static void Enter(
            in AsyncWriteActionConfig config,
            ref AsyncWriteActionMemory memory,
            ref BurstEnterContext context)
        {
            memory.Started = false;
            memory.OperationId = default;
        }

        public static NodeStatus Tick(
            in AsyncWriteActionConfig config,
            ref AsyncWriteActionMemory memory,
            ref BurstTickContext context)
        {
            if (!memory.Started)
            {
                var read = PublicBurstNodeShard.BurstAccess.TryRead(ref context, config.Source, out memory.Value);
                if (read != BurstContextResult.Success)
                    return NodeStatus.Failure;

                var write = PublicBurstNodeShard.BurstAccess.TryWrite(ref context, config.Destination, in memory.Value);
                var emit = PublicBurstNodeShard.BurstAccess.TryEmit(ref context, config.Effect, in memory.Value);
                var start = PublicBurstNodeShard.BurstAccess.TryStart(
                    ref context, config.Operation, in memory.Value, in memory.Value, out memory.OperationId);
                if (write != BurstContextResult.Success
                    || emit != BurstContextResult.Success
                    || start != BurstContextResult.Success)
                    return NodeStatus.Failure;

                memory.Started = true;
                return NodeStatus.Running;
            }

            var consume = PublicBurstNodeShard.BurstAccess.TryConsume(
                ref context, config.Completion, memory.OperationId, out var outcome, out var completionValue);
            if (consume == BurstContextResult.StaleCompletion)
                return NodeStatus.Running;
            if (consume != BurstContextResult.Success || outcome != BurstCompletionOutcome.Succeeded)
                return NodeStatus.Failure;

            memory.Value = completionValue;
            return PublicBurstNodeShard.BurstAccess.TryWrite(
                ref context, config.Destination, in memory.Value) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }

        public static void Abort(
            in AsyncWriteActionConfig config,
            ref AsyncWriteActionMemory memory,
            ref BurstAbortContext context,
            BurstNodeAbortReason reason)
        {
            if (memory.Started)
                PublicBurstNodeShard.BurstAccess.TryCancel(
                    ref context, config.Operation, memory.OperationId, in memory.Value);
        }

        public static void Exit(
            in AsyncWriteActionConfig config,
            ref AsyncWriteActionMemory memory,
            ref BurstExitContext context,
            BurstNodeExitReason reason) { }
    }
}
