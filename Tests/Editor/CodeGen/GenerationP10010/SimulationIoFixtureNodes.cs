using AIBT.Burst;
using AIBT.Native;

// P10-010: nodes that read simulation inputs (SnapshotReadHandle) and emit commands (CommandHandle).
// In their own assembly: a shard's generated authority is consumed by a catalog set in another
// assembly (AIBT.Integration.Tests, SimulationIoCatalog), as for the P7-039 fixture.
namespace AIBT.Tests.CodeGen.GenerationP10010
{
    [AibtCatalogShard("aibt.tests.p10010.simulation-io", 1u)]
    public partial struct SimulationIoShard { }

    /// <summary>
    /// What an agent senses, one row per agent slot. Fields are declared in a different order from
    /// their stable ids (ally, flag, threat), so the raw C# layout (threat 0, ally 4, flag 8) differs
    /// from the canonical one (ally 0, flag 4, threat 8): the container must translate, not copy.
    /// </summary>
    [AibtBurstValue("aibt.tests.p10010.sense", 1u, "aibt.tests.p10010.sense.schema")]
    public partial struct SenseInput
    {
        [AibtValueField("threat", "Float32", 1u)] public float Threat;
        [AibtValueField("ally", "Int32", 1u)] public int Ally;
        [AibtValueField("flag", "Bool", 1u)] public bool Flag;
    }

    /// <summary>An order a node gives the game. Declared out of id order as well (raw: strength 0, target 4, urgent 8, heading 12).</summary>
    [AibtBurstValue("aibt.tests.p10010.order", 1u, "aibt.tests.p10010.order.schema")]
    public partial struct OrderCommand
    {
        [AibtValueField("strength", "Float32", 1u)] public float Strength;
        [AibtValueField("target", "Int32", 1u)] public int Target;
        [AibtValueField("urgent", "Bool", 1u)] public bool Urgent;
        [AibtValueField("heading", "Float2", 1u)] public Float2Value Heading;
    }

    public partial struct ThreatConfig
    {
        [AibtParam("threshold")] public float Threshold;
        [AibtInput("sense")] public SnapshotReadHandle<SenseInput> Sense;
    }

    public partial struct EmptyIoMemory { }

    [AibtNodeDocumentation("Succeeds while the sensed threat is at least the threshold.", "Tests/Simulation",
        "Use for container input tests.", "Not a gameplay node.", "p10010-threat")]
    [AibtCondition("aibt.tests.p10010.threat-at-least")]
    public partial struct ThreatAtLeastNode
    {
        public static NodeStatus Tick(in ThreatConfig config, ref EmptyIoMemory memory, ref BurstTickContext context)
        {
            if (SimulationIoShard.BurstAccess.TryReadSnapshot(ref context, config.Sense, out var sense) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return sense.Threat >= config.Threshold ? NodeStatus.Success : NodeStatus.Failure;
        }
    }

    public partial struct SpeedConfig
    {
        [AibtParam("threshold")] public float Threshold;
        [AibtInput("speed")] public SnapshotReadHandle<float> Speed;
    }

    [AibtNodeDocumentation("Succeeds while the sensed speed is above the threshold.", "Tests/Simulation",
        "Use for container input tests.", "Not a gameplay node.", "p10010-speed")]
    [AibtCondition("aibt.tests.p10010.speed-above")]
    public partial struct SpeedAboveNode
    {
        public static NodeStatus Tick(in SpeedConfig config, ref EmptyIoMemory memory, ref BurstTickContext context)
        {
            if (SimulationIoShard.BurstAccess.TryReadSnapshot(ref context, config.Speed, out var speed) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return speed > config.Threshold ? NodeStatus.Success : NodeStatus.Failure;
        }
    }

    public partial struct OrderConfig
    {
        [AibtParam("scale")] public float Scale;
        [AibtInput("sense")] public SnapshotReadHandle<SenseInput> Sense;
        [AibtCommand("order")] public CommandHandle<OrderCommand> Order;
        [AibtCommand("ping")] public CommandHandle<int> Ping;
    }

    [AibtNodeDocumentation("Gives an order built from the sensed row, and pings.", "Tests/Simulation",
        "Use for container command tests.", "Not a gameplay node.", "p10010-order")]
    [AibtAction("aibt.tests.p10010.give-order", Statuses = BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct GiveOrderNode
    {
        public static NodeStatus Tick(in OrderConfig config, ref EmptyIoMemory memory, ref BurstTickContext context)
        {
            if (SimulationIoShard.BurstAccess.TryReadSnapshot(ref context, config.Sense, out var sense) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var order = new OrderCommand
            {
                Strength = sense.Threat * config.Scale,
                Target = sense.Ally,
                Urgent = sense.Flag,
                Heading = new Float2Value(sense.Threat, -sense.Threat),
            };
            if (SimulationIoShard.BurstAccess.TryEmit(ref context, config.Order, in order) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var ping = sense.Ally + 1;
            if (SimulationIoShard.BurstAccess.TryEmit(ref context, config.Ping, in ping) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return NodeStatus.Success;
        }
    }

    public partial struct FixedOrderConfig
    {
        [AibtParam("strength")] public float Strength;
        [AibtCommand("order")] public CommandHandle<OrderCommand> Order;
    }

    [AibtNodeDocumentation("Gives a fixed order (no input).", "Tests/Simulation",
        "Use for command tests without inputs.", "Not a gameplay node.", "p10010-fixed-order")]
    [AibtAction("aibt.tests.p10010.fixed-order", Statuses = BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct FixedOrderNode
    {
        public static NodeStatus Tick(in FixedOrderConfig config, ref EmptyIoMemory memory, ref BurstTickContext context)
        {
            var order = new OrderCommand { Strength = config.Strength, Target = 7, Urgent = true, Heading = new Float2Value(1f, 2f) };
            return SimulationIoShard.BurstAccess.TryEmit(ref context, config.Order, in order) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    // An external action (stage G3): the container refuses programs that use it.
    public partial struct ExternalActionConfig
    {
        [AibtConfigField("operation", "GeneratedHandle", 1u)]
        [AibtAsyncOperationBinding("operation", "Int32", 1u, "Int32", 1u)]
        public AsyncOperationHandle<int, int> Operation;

        [AibtConfigField("completion", "GeneratedHandle", 1u)]
        [AibtCompletionBinding("completion", "Int32", 1u)]
        public CompletionHandle<int> Completion;
    }

    public partial struct ExternalActionMemory
    {
        [AibtMemoryField("operation-id", "OperationId", 1u)]
        public OperationId OperationId;

        [AibtMemoryField("started", "Bool", 1u)]
        public bool Started;
    }

    [AibtNodeDocumentation("Starts an external operation and waits for its completion.", "Tests/Simulation",
        "Use for the container's external-action refusal.", "Not a gameplay node.", "p10010-external")]
    [AibtBurstNode(
        "aibt.tests.p10010.external-action", 1u, BurstNodeKind.Action,
        typeof(ExternalActionConfig), typeof(ExternalActionMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.Command, BurstNodeCost.Low,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running)]
    public partial struct ExternalActionNode
    {
        public static void Enter(in ExternalActionConfig config, ref ExternalActionMemory memory, ref BurstEnterContext context)
        {
            memory.Started = false;
            memory.OperationId = default;
        }

        public static NodeStatus Tick(in ExternalActionConfig config, ref ExternalActionMemory memory, ref BurstTickContext context)
        {
            if (!memory.Started)
            {
                var value = 1;
                if (SimulationIoShard.BurstAccess.TryStart(ref context, config.Operation, in value, in value, out memory.OperationId)
                    != BurstContextResult.Success)
                    return NodeStatus.Failure;
                memory.Started = true;
                return NodeStatus.Running;
            }
            var consume = SimulationIoShard.BurstAccess.TryConsume(ref context, config.Completion, memory.OperationId, out var outcome, out _);
            if (consume == BurstContextResult.StaleCompletion) return NodeStatus.Running;
            return consume == BurstContextResult.Success && outcome == BurstCompletionOutcome.Succeeded ? NodeStatus.Success : NodeStatus.Failure;
        }

        public static void Abort(in ExternalActionConfig config, ref ExternalActionMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason)
        {
            if (memory.Started)
            {
                var value = 0;
                SimulationIoShard.BurstAccess.TryCancel(ref context, config.Operation, memory.OperationId, in value);
            }
        }

        public static void Exit(in ExternalActionConfig config, ref ExternalActionMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }
}

