using AIBT.Burst;
using AIBT.Native;

// P10-011 (ADR-P10-008 stage G3): nodes that start external long-running actions
// (AsyncOperationHandle), read the game's completion (CompletionHandle), read a simulation input and
// emit a command in the same node. In their own assembly, consumed by a catalog set in
// AIBT.Integration.Tests (SimulationActionCatalog), as for the P10-010 fixture.
namespace AIBT.Tests.CodeGen.GenerationP10011
{
    [AibtCatalogShard("aibt.tests.p10011.simulation-actions", 1u)]
    public partial struct SimulationActionShard { }

    /// <summary>What a crew member senses. Declared out of stable-id order (raw: threat 0, alarm 4; canonical: alarm 0, threat 4).</summary>
    [AibtBurstValue("aibt.tests.p10011.crew-sense", 1u, "aibt.tests.p10011.crew-sense.schema")]
    public partial struct CrewSense
    {
        [AibtValueField("threat", "Float32", 1u)] public float Threat;
        [AibtValueField("alarm", "Bool", 1u)] public bool Alarm;
    }

    /// <summary>The start value of a job (raw: power, kind, flag; canonical: flag, kind, power).</summary>
    [AibtBurstValue("aibt.tests.p10011.job-start", 1u, "aibt.tests.p10011.job-start.schema")]
    public partial struct JobStart
    {
        [AibtValueField("power", "Float32", 1u)] public float Power;
        [AibtValueField("kind", "Int32", 1u)] public int Kind;
        [AibtValueField("flag", "Bool", 1u)] public bool Flag;
    }

    /// <summary>The cancel value of a job (raw: reason, progress; canonical: progress, reason).</summary>
    [AibtBurstValue("aibt.tests.p10011.job-cancel", 1u, "aibt.tests.p10011.job-cancel.schema")]
    public partial struct JobCancel
    {
        [AibtValueField("reason", "Int32", 1u)] public int Reason;
        [AibtValueField("progress", "Float32", 1u)] public float Progress;
    }

    /// <summary>What the game reports when a job ends (raw: score, hits; canonical: hits, score).</summary>
    [AibtBurstValue("aibt.tests.p10011.job-result", 1u, "aibt.tests.p10011.job-result.schema")]
    public partial struct JobResult
    {
        [AibtValueField("score", "Float32", 1u)] public float Score;
        [AibtValueField("hits", "Int32", 1u)] public int Hits;
    }

    /// <summary>The command a job node emits when it reads its completion (raw: score, ok; canonical: ok, score).</summary>
    [AibtBurstValue("aibt.tests.p10011.job-done", 1u, "aibt.tests.p10011.job-done.schema")]
    public partial struct JobDone
    {
        [AibtValueField("score", "Float32", 1u)] public float Score;
        [AibtValueField("ok", "Bool", 1u)] public bool Ok;
    }

    public partial struct AlarmConfig
    {
        [AibtInput("sense")] public SnapshotReadHandle<CrewSense> Sense;
    }

    public partial struct EmptyActionMemory { }

    [AibtNodeDocumentation("Succeeds while the sensed alarm is raised.", "Tests/Simulation",
        "Use for container action preemption tests.", "Not a gameplay node.", "p10011-alarm")]
    [AibtCondition("aibt.tests.p10011.alarm")]
    public partial struct AlarmNode
    {
        public static NodeStatus Tick(in AlarmConfig config, ref EmptyActionMemory memory, ref BurstTickContext context)
        {
            if (SimulationActionShard.BurstAccess.TryReadSnapshot(ref context, config.Sense, out var sense) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return sense.Alarm ? NodeStatus.Success : NodeStatus.Failure;
        }
    }

    public partial struct JobConfig
    {
        [AibtConfigField("power", "Float32", 1u)]
        public float Power;

        /// <summary>Microseconds the node waits for the completion before it gives up (Failure, action left running); 0 = forever.</summary>
        [AibtConfigField("patience", "UInt64", 1u)]
        public ulong Patience;

        /// <summary>True: Success right after starting, never cancels (the action ends with NodeEnded).</summary>
        [AibtConfigField("forget", "Bool", 1u)]
        public bool Forget;

        [AibtConfigField("sense", "GeneratedHandle", 1u)]
        [AibtSnapshotBinding("sense", "aibt.tests.p10011.crew-sense", 1u)]
        public SnapshotReadHandle<CrewSense> Sense;

        [AibtConfigField("job", "GeneratedHandle", 1u)]
        [AibtAsyncOperationBinding("job", "aibt.tests.p10011.job-start", 1u, "aibt.tests.p10011.job-cancel", 1u)]
        public AsyncOperationHandle<JobStart, JobCancel> Job;

        [AibtConfigField("result", "GeneratedHandle", 1u)]
        [AibtCompletionBinding("result", "aibt.tests.p10011.job-result", 1u)]
        public CompletionHandle<JobResult> Result;

        [AibtConfigField("done", "GeneratedHandle", 1u)]
        [AibtCommandBinding("done", "aibt.tests.p10011.job-done", 1u)]
        public CommandHandle<JobDone> Done;
    }

    public partial struct JobMemory
    {
        [AibtMemoryField("operation", "OperationId", 1u)]
        public OperationId Operation;

        [AibtMemoryField("started", "Bool", 1u)]
        public bool Started;

        [AibtMemoryField("started-at", "Int64", 1u)]
        public long StartedAt;
    }

    /// <summary>
    /// Starts a job built from the sensed row (fault-cancel value: reason 99, progress -1), then waits
    /// for the game's completion: on it emits <see cref="JobDone"/> and returns Success (game success) or
    /// Failure. Gives up (Failure) once <c>patience</c> has passed, checking time before polling. Abort
    /// cancels with the abort reason and progress 0.5 unless <c>forget</c>.
    /// </summary>
    [AibtNodeDocumentation("Starts an external job and waits for the game's completion.", "Tests/Simulation",
        "Use for container external-action tests.", "Not a gameplay node.", "p10011-job")]
    [AibtBurstNode(
        "aibt.tests.p10011.job", 1u, BurstNodeKind.Action,
        typeof(JobConfig), typeof(JobMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.Command, BurstNodeCost.Low,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running)]
    public partial struct JobNode
    {
        public static void Enter(in JobConfig config, ref JobMemory memory, ref BurstEnterContext context)
        {
            memory.Operation = default;
            memory.Started = false;
            memory.StartedAt = 0;
        }

        public static NodeStatus Tick(in JobConfig config, ref JobMemory memory, ref BurstTickContext context)
        {
            if (context.TryGetTimeMicroseconds(out var now) != BurstContextResult.Success) return NodeStatus.Failure;
            if (!memory.Started)
            {
                if (SimulationActionShard.BurstAccess.TryReadSnapshot(ref context, config.Sense, out var sense) != BurstContextResult.Success)
                    return NodeStatus.Failure;
                var start = new JobStart { Power = config.Power + sense.Threat, Kind = (int)(now % 7), Flag = sense.Alarm };
                var fault = new JobCancel { Reason = 99, Progress = -1f };
                if (SimulationActionShard.BurstAccess.TryStart(ref context, config.Job, in start, in fault, out memory.Operation)
                    != BurstContextResult.Success)
                    return NodeStatus.Failure;
                memory.Started = true;
                memory.StartedAt = now;
                return config.Forget ? NodeStatus.Success : NodeStatus.Running;
            }
            // Give up before polling: a poll without a completion ends the frame (container policy).
            if (config.Patience != 0 && (ulong)(now - memory.StartedAt) >= config.Patience) return NodeStatus.Failure;
            var consume = SimulationActionShard.BurstAccess.TryConsume(ref context, config.Result, memory.Operation, out var outcome, out var result);
            if (consume == BurstContextResult.StaleCompletion) return NodeStatus.Running;
            if (consume != BurstContextResult.Success) return NodeStatus.Failure;
            memory.Started = false;
            var ok = outcome == BurstCompletionOutcome.Succeeded;
            var done = new JobDone { Score = result.Score + result.Hits, Ok = ok };
            if (SimulationActionShard.BurstAccess.TryEmit(ref context, config.Done, in done) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return ok ? NodeStatus.Success : NodeStatus.Failure;
        }

        public static void Abort(in JobConfig config, ref JobMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason)
        {
            if (!memory.Started || config.Forget) return;
            var cancel = new JobCancel { Reason = (int)reason, Progress = 0.5f };
            SimulationActionShard.BurstAccess.TryCancel(ref context, config.Job, memory.Operation, in cancel);
        }

        public static void Exit(in JobConfig config, ref JobMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }
}
