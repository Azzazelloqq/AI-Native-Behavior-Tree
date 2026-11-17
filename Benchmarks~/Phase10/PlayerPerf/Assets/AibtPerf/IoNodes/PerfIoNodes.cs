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
}
