using AIBT.Burst;
using AIBT.Native;

// P10-010: benchmark nodes for the simulation container's inputs and commands. In their own
// assembly: a shard's generated authority is consumed by PerfCatalog in AIBT.Perf.Catalog.
namespace AIBT.Perf.IoNodes
{
    [AibtCatalogShard("aibt.perf.io", 1u)]
    public partial struct PerfIoShard { }

    /// <summary>What an agent senses, one row per agent slot, written by the game every tick.</summary>
    [AibtBurstValue("aibt.perf.sense", 1u, "aibt.perf.sense.schema")]
    public partial struct PerfSense
    {
        [AibtValueField("threat", "Float32", 1u)] public float Threat;
        [AibtValueField("target", "Int32", 1u)] public int Target;
    }

    /// <summary>An order an agent gives the game.</summary>
    [AibtBurstValue("aibt.perf.order", 1u, "aibt.perf.order.schema")]
    public partial struct PerfOrder
    {
        [AibtValueField("strength", "Float32", 1u)] public float Strength;
        [AibtValueField("target", "Int32", 1u)] public int Target;
    }

    public partial struct PerfSenseConfig
    {
        [AibtParam("threshold")] public float Threshold;
        [AibtInput("sense")] public SnapshotReadHandle<PerfSense> Sense;
    }

    public partial struct PerfOrderConfig
    {
        [AibtInput("sense")] public SnapshotReadHandle<PerfSense> Sense;
        [AibtCommand("order")] public CommandHandle<PerfOrder> Order;
    }

    public partial struct PerfIoMemory { }

    [AibtNodeDocumentation("Succeeds while the sensed threat is at least the threshold.", "Benchmarks/Perf",
        "Benchmark input read.", "Not a gameplay node.", "perf-sense")]
    [AibtCondition("aibt.perf.sense-at-least")]
    public partial struct PerfSenseAtLeastNode
    {
        public static NodeStatus Tick(in PerfSenseConfig config, ref PerfIoMemory memory, ref BurstTickContext context)
        {
            if (PerfIoShard.BurstAccess.TryReadSnapshot(ref context, config.Sense, out var sense) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return sense.Threat >= config.Threshold ? NodeStatus.Success : NodeStatus.Failure;
        }
    }

    [AibtNodeDocumentation("Gives an order built from the sensed row on every tick and keeps running.", "Benchmarks/Perf",
        "Benchmark command per think.", "Not a gameplay node.", "perf-order")]
    [AibtAction("aibt.perf.order-hold", Statuses = BurstNodeStatusMask.Running | BurstNodeStatusMask.Failure)]
    public partial struct PerfOrderHoldNode
    {
        public static NodeStatus Tick(in PerfOrderConfig config, ref PerfIoMemory memory, ref BurstTickContext context)
        {
            if (PerfIoShard.BurstAccess.TryReadSnapshot(ref context, config.Sense, out var sense) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var order = new PerfOrder { Strength = sense.Threat * 2f, Target = sense.Target };
            return PerfIoShard.BurstAccess.TryEmit(ref context, config.Order, in order) == BurstContextResult.Success
                ? NodeStatus.Running
                : NodeStatus.Failure;
        }
    }

    [AibtNodeDocumentation("Gives one order built from the sensed row and succeeds.", "Benchmarks/Perf",
        "Benchmark command in a sequence.", "Not a gameplay node.", "perf-order-once")]
    [AibtAction("aibt.perf.order-once", Statuses = BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct PerfOrderOnceNode
    {
        public static NodeStatus Tick(in PerfOrderConfig config, ref PerfIoMemory memory, ref BurstTickContext context)
        {
            if (PerfIoShard.BurstAccess.TryReadSnapshot(ref context, config.Sense, out var sense) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var order = new PerfOrder { Strength = sense.Threat * 2f, Target = sense.Target };
            return PerfIoShard.BurstAccess.TryEmit(ref context, config.Order, in order) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    // P10-011: an external action the game carries out (stage G3).
    /// <summary>What the game is asked to do when a job starts.</summary>
    [AibtBurstValue("aibt.perf.job-start", 1u, "aibt.perf.job-start.schema")]
    public partial struct PerfJobStart
    {
        [AibtValueField("target", "Int32", 1u)] public int Target;
        [AibtValueField("effort", "Float32", 1u)] public float Effort;
    }

    /// <summary>What the game reports when a job ends.</summary>
    [AibtBurstValue("aibt.perf.job-result", 1u, "aibt.perf.job-result.schema")]
    public partial struct PerfJobResult
    {
        [AibtValueField("score", "Float32", 1u)] public float Score;
    }

    public partial struct PerfJobConfig
    {
        [AibtConfigField("sense", "GeneratedHandle", 1u)]
        [AibtSnapshotBinding("sense", "aibt.perf.sense", 1u)]
        public SnapshotReadHandle<PerfSense> Sense;

        [AibtConfigField("job", "GeneratedHandle", 1u)]
        [AibtAsyncOperationBinding("job", "aibt.perf.job-start", 1u, "Int32", 1u)]
        public AsyncOperationHandle<PerfJobStart, int> Job;

        [AibtConfigField("result", "GeneratedHandle", 1u)]
        [AibtCompletionBinding("result", "aibt.perf.job-result", 1u)]
        public CompletionHandle<PerfJobResult> Result;

        [AibtConfigField("order", "GeneratedHandle", 1u)]
        [AibtCommandBinding("order", "aibt.perf.order", 1u)]
        public CommandHandle<PerfOrder> Order;
    }

    public partial struct PerfJobMemory
    {
        [AibtMemoryField("operation", "OperationId", 1u)] public OperationId Operation;
        [AibtMemoryField("started", "Bool", 1u)] public bool Started;
    }

    [AibtNodeDocumentation("Starts an external job from the sensed row, waits for the game's completion, then gives an order.", "Benchmarks/Perf",
        "Benchmark external action.", "Not a gameplay node.", "perf-job")]
    [AibtBurstNode(
        "aibt.perf.job", 1u, BurstNodeKind.Action,
        typeof(PerfJobConfig), typeof(PerfJobMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.Command, BurstNodeCost.Low,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running)]
    public partial struct PerfJobNode
    {
        public static void Enter(in PerfJobConfig config, ref PerfJobMemory memory, ref BurstEnterContext context)
        {
            memory.Operation = default;
            memory.Started = false;
        }

        public static NodeStatus Tick(in PerfJobConfig config, ref PerfJobMemory memory, ref BurstTickContext context)
        {
            if (!memory.Started)
            {
                if (PerfIoShard.BurstAccess.TryReadSnapshot(ref context, config.Sense, out var sense) != BurstContextResult.Success)
                    return NodeStatus.Failure;
                var start = new PerfJobStart { Target = sense.Target, Effort = sense.Threat };
                var fault = -1;
                if (PerfIoShard.BurstAccess.TryStart(ref context, config.Job, in start, in fault, out memory.Operation) != BurstContextResult.Success)
                    return NodeStatus.Failure;
                memory.Started = true;
                return NodeStatus.Running;
            }
            var consume = PerfIoShard.BurstAccess.TryConsume(ref context, config.Result, memory.Operation, out var outcome, out var result);
            if (consume == BurstContextResult.StaleCompletion) return NodeStatus.Running;
            if (consume != BurstContextResult.Success) return NodeStatus.Failure;
            memory.Started = false;
            var order = new PerfOrder { Strength = result.Score, Target = outcome == BurstCompletionOutcome.Succeeded ? 1 : 0 };
            return PerfIoShard.BurstAccess.TryEmit(ref context, config.Order, in order) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }

        public static void Abort(in PerfJobConfig config, ref PerfJobMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason)
        {
            if (!memory.Started) return;
            var cancel = (int)reason;
            PerfIoShard.BurstAccess.TryCancel(ref context, config.Job, memory.Operation, in cancel);
        }

        public static void Exit(in PerfJobConfig config, ref PerfJobMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }
}