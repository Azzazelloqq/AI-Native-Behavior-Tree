using AIBT.Burst;
using AIBT.Native;

// P12-001: the nodes of the SweetKingdom reactive-interrupt pattern. A guard condition reads a trigger
// from its own input channel (one Bool per agent), a long-running walk reads the agent's position from a
// second channel (16 bytes per agent) at every think and runs as an external action, so a preemption is
// visible as its Cancelled (Preempted) event. In their own assembly, consumed by a catalog set in
// AIBT.Integration.Tests (ReactivePatternCatalog), as for the P10-010 and P10-011 fixtures.
namespace AIBT.Tests.CodeGen.GenerationP12001
{
    [AibtCatalogShard("aibt.tests.p12001.reactive-pattern", 1u)]
    public partial struct ReactivePatternShard { }

    /// <summary>The trigger row: only the flag, so the row changes only when the trigger does.</summary>
    [AibtBurstValue("aibt.tests.p12001.trigger", 1u, "aibt.tests.p12001.trigger.schema")]
    public partial struct TriggerRow
    {
        [AibtValueField("raised", "Bool", 1u)] public bool Raised;
    }

    /// <summary>The position row (16 bytes), rewritten by the game at every tick.</summary>
    [AibtBurstValue("aibt.tests.p12001.position", 1u, "aibt.tests.p12001.position.schema")]
    public partial struct PositionRow
    {
        [AibtValueField("x", "Float32", 1u)] public float X;
        [AibtValueField("y", "Float32", 1u)] public float Y;
        [AibtValueField("z", "Float32", 1u)] public float Z;
        [AibtValueField("heading", "Float32", 1u)] public float Heading;
    }

    [AibtBurstValue("aibt.tests.p12001.walk-start", 1u, "aibt.tests.p12001.walk-start.schema")]
    public partial struct WalkStart
    {
        [AibtValueField("x", "Float32", 1u)] public float X;
        [AibtValueField("z", "Float32", 1u)] public float Z;
    }

    [AibtBurstValue("aibt.tests.p12001.walk-cancel", 1u, "aibt.tests.p12001.walk-cancel.schema")]
    public partial struct WalkCancel
    {
        [AibtValueField("reason", "Int32", 1u)] public int Reason;
        [AibtValueField("last-x", "Float32", 1u)] public float LastX;
    }

    [AibtBurstValue("aibt.tests.p12001.walk-result", 1u, "aibt.tests.p12001.walk-result.schema")]
    public partial struct WalkResult
    {
        [AibtValueField("arrived", "Bool", 1u)] public bool Arrived;
    }

    public partial struct TriggeredConfig
    {
        [AibtInput("trigger")] public SnapshotReadHandle<TriggerRow> Trigger;
    }

    public partial struct EmptyPatternMemory { }

    [AibtNodeDocumentation("Succeeds while the agent's trigger is raised.", "Tests/Simulation",
        "Use for the P12-001 reactive interrupt tests.", "Not a gameplay node.", "p12001-triggered")]
    [AibtCondition("aibt.tests.p12001.triggered")]
    public partial struct TriggeredNode
    {
        public static NodeStatus Tick(in TriggeredConfig config, ref EmptyPatternMemory memory, ref BurstTickContext context)
        {
            if (ReactivePatternShard.BurstAccess.TryReadSnapshot(ref context, config.Trigger, out var trigger) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return trigger.Raised ? NodeStatus.Success : NodeStatus.Failure;
        }
    }

    public partial struct WalkConfig
    {
        [AibtInput("position")] public SnapshotReadHandle<PositionRow> Position;

        [AibtConfigField("walk", "GeneratedHandle", 1u)]
        [AibtAsyncOperationBinding("walk", "aibt.tests.p12001.walk-start", 1u, "aibt.tests.p12001.walk-cancel", 1u)]
        public AsyncOperationHandle<WalkStart, WalkCancel> Walk;

        [AibtConfigField("arrived", "GeneratedHandle", 1u)]
        [AibtCompletionBinding("arrived", "aibt.tests.p12001.walk-result", 1u)]
        public CompletionHandle<WalkResult> Arrived;
    }

    public partial struct WalkMemory
    {
        [AibtMemoryField("operation", "OperationId", 1u)]
        public OperationId Operation;

        [AibtMemoryField("started", "Bool", 1u)]
        public bool Started;

        [AibtMemoryField("last-x", "Float32", 1u)]
        public float LastX;
    }

    /// <summary>
    /// Reads the position at every think, starts one walk action, then polls for the game's completion
    /// (Success when it arrived). Abort cancels the walk with the abort reason and the last x it read.
    /// </summary>
    [AibtNodeDocumentation("Walks as an external action, reading the position at every think.", "Tests/Simulation",
        "Use for the P12-001 reactive interrupt tests.", "Not a gameplay node.", "p12001-walk")]
    [AibtBurstNode(
        "aibt.tests.p12001.walk", 1u, BurstNodeKind.Action,
        typeof(WalkConfig), typeof(WalkMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.Command, BurstNodeCost.Low,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running)]
    public partial struct WalkNode
    {
        public static void Enter(in WalkConfig config, ref WalkMemory memory, ref BurstEnterContext context)
        {
            memory.Operation = default;
            memory.Started = false;
            memory.LastX = 0f;
        }

        public static NodeStatus Tick(in WalkConfig config, ref WalkMemory memory, ref BurstTickContext context)
        {
            if (ReactivePatternShard.BurstAccess.TryReadSnapshot(ref context, config.Position, out var position) != BurstContextResult.Success)
                return NodeStatus.Failure;
            memory.LastX = position.X;
            if (!memory.Started)
            {
                var start = new WalkStart { X = position.X, Z = position.Z };
                var fault = new WalkCancel { Reason = 99, LastX = -1f };
                if (ReactivePatternShard.BurstAccess.TryStart(ref context, config.Walk, in start, in fault, out memory.Operation)
                    != BurstContextResult.Success)
                    return NodeStatus.Failure;
                memory.Started = true;
                return NodeStatus.Running;
            }
            var consume = ReactivePatternShard.BurstAccess.TryConsume(ref context, config.Arrived, memory.Operation, out var outcome, out var result);
            if (consume == BurstContextResult.StaleCompletion) return NodeStatus.Running;
            if (consume != BurstContextResult.Success) return NodeStatus.Failure;
            memory.Started = false;
            return outcome == BurstCompletionOutcome.Succeeded && result.Arrived ? NodeStatus.Success : NodeStatus.Failure;
        }

        public static void Abort(in WalkConfig config, ref WalkMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason)
        {
            if (!memory.Started) return;
            var cancel = new WalkCancel { Reason = (int)reason, LastX = memory.LastX };
            ReactivePatternShard.BurstAccess.TryCancel(ref context, config.Walk, memory.Operation, in cancel);
        }

        public static void Exit(in WalkConfig config, ref WalkMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }
}
