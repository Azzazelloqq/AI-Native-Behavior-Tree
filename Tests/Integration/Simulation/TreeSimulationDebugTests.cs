using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AIBT.Runtime.Scheduling;
using AIBT.Simulation;
using AIBT.Tests.CodeGen.GenerationP10011;
using NUnit.Framework;
using Unity.Collections;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// P10-012 (ADR-P10-008 stage G4): debugging the simulation container. Traces of selected agents
    /// (<see cref="TreeGroup.TrySetTraced"/>), the active-path query for any agent
    /// (<see cref="TreeGroup.TryGetActivePath(AgentHandle, NativeList{ActivePathNode}, out AgentDebugState)"/>,
    /// <see cref="TreeGroup.DescribeAgent(AgentHandle)"/>) and the opt-in registry for tools
    /// (<see cref="TreeSimulationDebug"/>). The population proofs (tracing changes no digest on any path,
    /// zero GC while tracing) are in <see cref="TreeSimulationActionTests"/>. Proof table:
    /// <c>Planning~/Evidence/P10-012/README.md</c>.
    /// </summary>
    public sealed class TreeSimulationDebugTests
    {
        private static readonly InputChannel<CrewSense> Sense = InputChannel<CrewSense>.Create("sense");
        private static readonly CommandChannel<JobDone> Done = CommandChannel<JobDone>.Create("done");

        // A job the game completes, then a reactive selector that prefers "alarm" over a second job; the root
        // succeeds once the alarm is up (the second job is preempted).
        internal const string DebugTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10012.debug"", ""name"": ""P10-012 debug"",
  ""root"": ""both"",
  ""nodes"": {
    ""both"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""first"", ""top""] },
    ""first"": { ""type"": ""aibt.tests.p10011.job"", ""typeVersion"": 1, ""parameters"": { ""power"": 1.0, ""patience"": 0, ""forget"": false } },
    ""top"": { ""type"": ""aibt.core.reactive-selector"", ""typeVersion"": 1, ""children"": [""alarm"", ""second""] },
    ""alarm"": { ""type"": ""aibt.tests.p10011.alarm"", ""typeVersion"": 1 },
    ""second"": { ""type"": ""aibt.tests.p10011.job"", ""typeVersion"": 1, ""parameters"": { ""power"": 2.0, ""patience"": 0, ""forget"": false } }
  }
}";

        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        private readonly List<ProductionTreeProgram> _programs = new List<ProductionTreeProgram>();

        [TearDown]
        public void TearDown()
        {
            foreach (var program in _programs) program.Dispose();
            _programs.Clear();
            foreach (var item in _objects) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            _objects.Clear();
        }

        private ProductionTreeProgram Program(string json)
        {
            var asset = TreeSimulationActionTests.PrepareActionAsset(json);
            _objects.Add(asset);
            Assert.That(asset.TryCreateProgram(SimulationActionCatalog.TryCreateRuntimeCatalog, out var program, out var failure), Is.True, failure.ToString());
            _programs.Add(program);
            return program;
        }

        private static TreeSimulationSettings Settings(bool useJobs)
        {
            var settings = TreeSimulationSettings.Default(TickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = 1;
            settings.RandomSeed = 0x10_0112UL;
            return settings;
        }

        private static TreeGroupSettings Stopping(int capacity)
        {
            var settings = TreeGroupSettings.Default(capacity);
            settings.RootCompletion = RootCompletionPolicy.Stop;
            return settings;
        }

        private sealed class Rig : IDisposable
        {
            internal TreeSimulation Simulation;
            internal TreeGroup Group;
            internal NativeArray<CrewSense> Sense;
            internal AgentHandle Agent;

            public void Dispose()
            {
                Simulation.Dispose();
                Sense.Dispose();
            }
        }

        private Rig NewRig(string tree, bool useJobs, in TreeGroupSettings groupSettings, in ThinkSchedule schedule, bool wake = false,
            TreeSimulationSettings? settings = null)
        {
            var rig = new Rig
            {
                Simulation = new TreeSimulation(settings ?? Settings(useJobs)),
                Sense = new NativeArray<CrewSense>(groupSettings.Capacity, Allocator.Persistent),
            };
            Assert.That(rig.Simulation.TryAddGroup(Program(tree), groupSettings, out rig.Group), Is.EqualTo(SimulationResult.Success));
            Assert.That(rig.Group.TryBindInput(Sense, rig.Sense), Is.EqualTo(SimulationResult.Success));
            if (wake) Assert.That(rig.Group.TrySetWakeOnChange(Sense, true), Is.EqualTo(SimulationResult.Success));
            Assert.That(rig.Group.TryAdd(schedule, out rig.Agent), Is.EqualTo(SimulationResult.Success));
            return rig;
        }

        private static ActionEvent SingleEvent(TreeGroup group, ActionEventKind kind)
        {
            var events = group.GetActionEvents();
            Assert.That(events.Length, Is.EqualTo(1), "one action event");
            Assert.That(events[0].Kind, Is.EqualTo(kind));
            return events[0];
        }

        /// <summary>One line per trace record: tick, fact, and the fields that fact uses.</summary>
        private static string Format(TreeGroup group, in AgentTraceRecord item)
        {
            var invariant = CultureInfo.InvariantCulture;
            var text = new StringBuilder();
            text.Append('t').Append(item.Tick.ToString(invariant)).Append(' ');
            var record = item.Record;
            string Node(uint node) => node == uint.MaxValue ? "-" : group.NodeLabel(node);
            switch (item.Fact)
            {
                case AgentTraceFact.Lifecycle:
                    text.Append(record.Kind);
                    if ((record.OptionalFields & NativeTraceOptionalFieldsV1.RuntimeNode) != 0) text.Append(' ').Append(Node(record.RuntimeNodeIndex));
                    if ((record.OptionalFields & NativeTraceOptionalFieldsV1.Status) != 0) text.Append(' ').Append(record.Status);
                    if ((record.OptionalFields & NativeTraceOptionalFieldsV1.ExitReason) != 0) text.Append(" exit:").Append(record.ExitReason);
                    if ((record.OptionalFields & NativeTraceOptionalFieldsV1.AbortReason) != 0) text.Append(" abort:").Append(record.AbortReason);
                    if ((record.OptionalFields & NativeTraceOptionalFieldsV1.DiagnosticCode) != 0) text.Append(" code:").Append(record.DiagnosticCodeNumber.ToString(invariant));
                    break;
                case AgentTraceFact.Think:
                    text.Append("Think ").Append(item.Reasons);
                    break;
                case AgentTraceFact.Input:
                case AgentTraceFact.Command:
                    text.Append(item.Fact).Append(' ').Append(item.ValueHex());
                    break;
                case AgentTraceFact.ActionStarted:
                    text.Append("ActionStarted ").Append(Node(item.Operation.NodeIndex.Value)).Append(" #").Append(item.Operation.Sequence.ToString(invariant))
                        .Append(' ').Append(item.ValueHex());
                    break;
                case AgentTraceFact.ActionCancelled:
                    text.Append("ActionCancelled ").Append(Node(item.Operation.NodeIndex.Value)).Append(" #").Append(item.Operation.Sequence.ToString(invariant))
                        .Append(' ').Append(item.CancelReason).Append(' ').Append(item.ValueHex());
                    break;
                case AgentTraceFact.CompletionConsumed:
                    text.Append("CompletionConsumed ").Append(Node(item.Operation.NodeIndex.Value)).Append(" #").Append(item.Operation.Sequence.ToString(invariant));
                    break;
                case AgentTraceFact.Fault:
                    text.Append("Fault ").Append(item.Failure.Code);
                    break;
                case AgentTraceFact.Teardown:
                    text.Append("Teardown ").Append(item.CancelReason);
                    break;
            }
            return text.ToString();
        }

        private static List<string> Lines(TreeGroup group, AgentHandle agent, out AgentTraceInfo info)
        {
            using (var records = new NativeList<AgentTraceRecord>(256, Allocator.Temp))
            {
                Assert.That(group.TryCopyTrace(agent, records, out info), Is.EqualTo(SimulationResult.Success));
                var lines = new List<string>(records.Length);
                for (var index = 0; index < records.Length; index++)
                {
                    Assert.That(records[index].Record.Sequence, Is.EqualTo(info.Recorded - (ulong)info.Count + (ulong)index + 1), "Sequences count up without gaps.");
                    lines.Add(Format(group, records[index]));
                }
                return lines;
            }
        }

        // ---- the scripted agent ------------------------------------------------------------------------

        // The scripted agent's trace (see ScriptedAgent_TraceIsExactlyTheExpectedEventList).
        private static readonly string[] ExpectedTrace =
        {
            // Tick 1: the scheduled first think of a fresh instance; the first job starts (flag 0, kind 33333 % 7, power 1).
            "t1 Think Scheduled, FreshInstance",
            "t1 Input 0000000000000000",
            "t1 UpdateStarted",
            "t1 NodeEntered both Running",
            "t1 NodeEntered first Running",
            "t1 NodeTicked first Running",
            "t1 UpdateCompleted",
            "t1 ActionStarted first #1 00000000060000000000803f",
            // Tick 3: woken by the game (TryWake); the job polls.
            "t3 Think WokenByGame",
            "t3 Input 0000000000000000",
            "t3 UpdateStarted",
            "t3 NodeTicked first Running",
            "t3 UpdateCompleted",
            // Tick 4: woken by the completion; the job reads it (JobDone ok, 2.5 + 3 hits), the reactive selector starts the second job.
            "t4 Think ActionCompleted",
            "t4 Input 0000000000000000",
            "t4 UpdateStarted",
            "t4 NodeTicked first Success",
            "t4 NodeExited first exit:Success",
            "t4 NodeEntered top Running",
            "t4 NodeEntered alarm Running",
            "t4 NodeTicked alarm Failure",
            "t4 NodeExited alarm exit:Failure",
            "t4 NodeEntered second Running",
            "t4 NodeTicked second Running",
            "t4 UpdateCompleted",
            "t4 Command 010000000000b040",
            "t4 ActionStarted second #1 000000000300000000000040",
            "t4 CompletionConsumed first #1",
            // Tick 5: woken by the raised alarm; the reactive selector aborts the second job (Preempted, its Abort value) and the root succeeds.
            "t5 Think InputChanged",
            "t5 Input 010000000000003f",
            "t5 UpdateStarted",
            "t5 NodeAbortStarted second abort:Explicit",
            "t5 NodeExited second exit:Aborted",
            "t5 NodeEntered alarm Running",
            "t5 NodeTicked alarm Success",
            "t5 NodeExited alarm exit:Success",
            "t5 NodeExited top",
            "t5 NodeExited both exit:Success",
            "t5 UpdateCompleted Success",
            "t5 ActionCancelled second #1 Preempted 0000003f00000000",
            // Tick 7: restarted (the finished instance has nothing to tear down) and woken: a fresh instance starts the first job.
            "t7 Teardown AgentRestarted",
            "t7 Think WokenByGame, FreshInstance",
            "t7 Input 010000000000003f",
            "t7 UpdateStarted",
            "t7 NodeEntered both Running",
            "t7 NodeEntered first Running",
            "t7 NodeTicked first Running",
            "t7 UpdateCompleted",
            "t7 ActionStarted first #1 01000000000000000000c03f",
            // Tick 8: removed: the teardown aborts the running job (TreeStopped), which ends with Cancelled (AgentRemoved).
            "t8 Teardown AgentRemoved",
            "t8 UpdateStarted",
            "t8 NodeAbortStarted first abort:TreeStopped",
            "t8 NodeExited first exit:Aborted",
            "t8 NodeAbortStarted both",
            "t8 UpdateCompleted",
            "t8 ActionCancelled first #1 AgentRemoved 0000003f03000000",
        };

        /// <summary>
        /// One agent (thinks every 50 ticks at phase 1, wake on changed "sense", Stop at root completion),
        /// traced from the start. Tick 1: scheduled first think, the first job starts. Tick 3: woken by the
        /// game, the job polls. Tick 4: woken by the completion the game gave, the job reads it, emits JobDone
        /// and succeeds; the reactive selector's second job starts. Tick 5: woken by the raised alarm, the
        /// reactive selector preempts the second job (Cancelled with the abort value) and the root succeeds.
        /// Tick 7: restarted and woken: teardown, then a fresh instance whose first job starts. Tick 8: removed:
        /// teardown, the job is cancelled (AgentRemoved). The trace is the exact list below on both paths, and
        /// stays readable by the stale handle until the agent is untraced.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ScriptedAgent_TraceIsExactlyTheExpectedEventList(bool useJobs)
        {
            using (var rig = NewRig(DebugTree, useJobs, Stopping(2), ThinkSchedule.Every(50, 1), wake: true))
            {
                var group = rig.Group;
                Assert.That(group.TrySetTraced(rig.Agent, true), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.IsTraced(rig.Agent), Is.True);

                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var first = SingleEvent(group, ActionEventKind.Started);
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryWake(rig.Agent), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(3), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryComplete(first.Action, ActionOutcome.Succeeded, new JobResult { Score = 2.5f, Hits = 3 }), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(4), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.GetCommands(Done).Length, Is.EqualTo(1));
                SingleEvent(group, ActionEventKind.Started);
                rig.Sense[0] = new CrewSense { Threat = 0.5f, Alarm = true };
                Assert.That(rig.Simulation.Step(5), Is.EqualTo(SimulationResult.Success));
                SingleEvent(group, ActionEventKind.Cancelled);
                Assert.That(group.GetRootCompletions().Length, Is.EqualTo(1));
                Assert.That(rig.Simulation.Step(6), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryRestart(rig.Agent), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryWake(rig.Agent), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(7), Is.EqualTo(SimulationResult.Success));
                SingleEvent(group, ActionEventKind.Started);
                Assert.That(group.TryRemove(rig.Agent), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(8), Is.EqualTo(SimulationResult.Success));
                SingleEvent(group, ActionEventKind.Cancelled);

                Assert.That(group.IsTraced(rig.Agent), Is.False, "A removed agent is not alive.");
                Assert.That(group.TracedCount, Is.EqualTo(1), "Its trace is kept.");
                var lines = Lines(group, rig.Agent, out var info);
                TestContext.WriteLine(string.Join("\n", lines));
                Assert.That(info.Dropped, Is.Zero);
                Assert.That(info.Recorded, Is.EqualTo((ulong)lines.Count));
                if (!lines.SequenceEqual(ExpectedTrace)) Assert.Fail("The trace differs from the expected list. Actual:\n" + string.Join("\n", lines));

                Assert.That(group.TrySetTraced(rig.Agent, false), Is.EqualTo(SimulationResult.Success), "The stale handle untraces the kept trace.");
                Assert.That(group.TracedCount, Is.Zero);
                using (var records = new NativeList<AgentTraceRecord>(4, Allocator.Temp))
                    Assert.That(group.TryCopyTrace(rig.Agent, records, out _), Is.EqualTo(SimulationResult.StaleHandle));
            }
        }

        /// <summary>
        /// The ring keeps the newest records: with room for 8, the oldest are dropped and counted, sequences stay
        /// contiguous, and a clear empties the ring and its counters while tracing goes on.
        /// </summary>
        [Test]
        public void Ring_Overflow_DropsTheOldest_AndCountsThem()
        {
            using (var rig = NewRig(TreeSimulationActionTests.CrewTree, false, TreeGroupSettings.Default(2), ThinkSchedule.Every(1, 0)))
            {
                var settings = AgentTraceSettings.Default;
                settings.RecordsPerAgent = 8;
                Assert.That(rig.Group.TryConfigureTracing(settings), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.TraceSettings.RecordsPerAgent, Is.EqualTo(8));
                Assert.That(rig.Group.TrySetTraced(rig.Agent, true), Is.EqualTo(SimulationResult.Success));
                for (uint tick = 1; tick <= 10; tick++) Assert.That(rig.Simulation.Step(tick), Is.EqualTo(SimulationResult.Success));

                using (var records = new NativeList<AgentTraceRecord>(8, Allocator.Temp))
                {
                    Assert.That(rig.Group.TryCopyTrace(rig.Agent, records, out var info), Is.EqualTo(SimulationResult.Success));
                    Assert.That(info.Count, Is.EqualTo(8));
                    Assert.That(records.Length, Is.EqualTo(8));
                    Assert.That(info.Recorded, Is.GreaterThan(80ul), "Ten thinks write more than 8 records each.");
                    Assert.That(info.Dropped, Is.EqualTo(info.Recorded - 8));
                    for (var index = 0; index < 8; index++)
                        Assert.That(records[index].Record.Sequence, Is.EqualTo(info.Recorded - 7 + (ulong)index), "The newest records, oldest first.");
                    Assert.That(records[7].Tick, Is.EqualTo(10u));

                    Assert.That(rig.Group.TryClearTrace(rig.Agent), Is.EqualTo(SimulationResult.Success));
                    Assert.That(rig.Group.TryCopyTrace(rig.Agent, records, out info), Is.EqualTo(SimulationResult.Success));
                    Assert.That(info.Count, Is.Zero);
                    Assert.That(info.Recorded, Is.Zero);
                    Assert.That(info.Dropped, Is.Zero);
                    Assert.That(rig.Simulation.Step(11), Is.EqualTo(SimulationResult.Success));
                    Assert.That(rig.Group.TryCopyTrace(rig.Agent, records, out info), Is.EqualTo(SimulationResult.Success));
                    Assert.That(info.Count, Is.GreaterThan(0), "Tracing goes on after a clear.");
                    Assert.That(records[0].Tick, Is.EqualTo(11u));
                }
            }
        }

        /// <summary>
        /// A think that writes more lifecycle records than <see cref="AgentTraceSettings.RecordsPerThink"/> (a
        /// runaway tree spinning to the step limit) keeps the first ones, counts the rest as dropped, still
        /// records the fault, and the agent faults exactly as untraced.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Think_BeyondRecordsPerThink_CountsTheDrops_AndRecordsTheFault(bool useJobs)
        {
            var simulationSettings = Settings(useJobs);
            simulationSettings.MaximumStepsPerThink = 1000;
            using (var rig = NewRig(TreeSimulationActionTests.RunawayTree, useJobs, Stopping(2), ThinkSchedule.Every(1, 0), settings: simulationSettings))
            {
                var settings = AgentTraceSettings.Default;
                settings.RecordsPerThink = 32;
                Assert.That(rig.Group.TryConfigureTracing(settings), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.TrySetTraced(rig.Agent, true), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                rig.Sense[0] = new CrewSense { Threat = 1f, Alarm = true };
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                var faults = rig.Group.GetFaults();
                Assert.That(faults.Length, Is.EqualTo(1));
                Assert.That(faults[0].Failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeUpdateStepLimitExceeded));

                var lines = Lines(rig.Group, rig.Agent, out var info);
                Assert.That(info.Dropped, Is.GreaterThanOrEqualTo(96ul), "A thousand steps write far more than 32 records.");
                Assert.That(info.DroppedIsLowerBound, Is.True, "Past four times RecordsPerThink the scratch channel stops counting.");
                Assert.That(lines.Count(line => line.StartsWith("t2 ", StringComparison.Ordinal) && !line.StartsWith("t2 Think", StringComparison.Ordinal)
                    && !line.StartsWith("t2 Input", StringComparison.Ordinal) && !line.StartsWith("t2 Action", StringComparison.Ordinal)
                    && !line.StartsWith("t2 Fault", StringComparison.Ordinal)), Is.LessThanOrEqualTo(32), "At most RecordsPerThink lifecycle records in the think.");
                Assert.That(lines.Last(), Is.EqualTo("t2 Fault NativeUpdateStepLimitExceeded"));
                Assert.That(lines.Any(line => line.StartsWith("t2 ActionCancelled job #1 Fault ", StringComparison.Ordinal)), Is.True, "The fault-cancel of the live job.");
            }
        }

        [Test]
        public void Settings_AndSelection_AreValidated()
        {
            using (var rig = NewRig(TreeSimulationActionTests.CrewTree, false, TreeGroupSettings.Default(4), ThinkSchedule.Every(1, 0)))
            {
                var group = rig.Group;
                Assert.That(group.TraceSettings.MaximumTracedAgents, Is.EqualTo(8));
                Assert.That(group.TraceSettings.RecordsPerAgent, Is.EqualTo(1024));
                Assert.That(group.TraceSettings.RecordsPerThink, Is.EqualTo(256));
                var bad = AgentTraceSettings.Default;
                bad.RecordsPerAgent = 0;
                Assert.That(group.TryConfigureTracing(bad), Is.EqualTo(SimulationResult.InvalidArgument));
                bad = AgentTraceSettings.Default;
                bad.RecordsPerThink = 65537;
                Assert.That(group.TryConfigureTracing(bad), Is.EqualTo(SimulationResult.InvalidArgument));
                var one = AgentTraceSettings.Default;
                one.MaximumTracedAgents = 1;
                Assert.That(group.TryConfigureTracing(one), Is.EqualTo(SimulationResult.Success));

                Assert.That(group.TryAdd(ThinkSchedule.Every(1, 0), out var other), Is.EqualTo(SimulationResult.Success));
                using (var records = new NativeList<AgentTraceRecord>(4, Allocator.Temp))
                    Assert.That(group.TryCopyTrace(rig.Agent, records, out _), Is.EqualTo(SimulationResult.NotTraced));
                Assert.That(group.TrySetTraced(rig.Agent, false), Is.EqualTo(SimulationResult.Success), "Untracing an untraced agent changes nothing.");
                Assert.That(group.TrySetTraced(rig.Agent, true), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TrySetTraced(rig.Agent, true), Is.EqualTo(SimulationResult.Success), "Tracing twice changes nothing.");
                Assert.That(group.TracedCount, Is.EqualTo(1));
                Assert.That(group.TrySetTraced(other, true), Is.EqualTo(SimulationResult.CapacityExceeded));
                Assert.That(group.TryConfigureTracing(AgentTraceSettings.Default), Is.EqualTo(SimulationResult.InvalidArgument), "Not while agents are traced.");
                Assert.That(group.TrySetTraced(rig.Agent, false), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TrySetTraced(other, true), Is.EqualTo(SimulationResult.Success), "The pool entry is reused.");
                Assert.That(group.TrySetTraced(new AgentHandle(3, 1, group.Id), true), Is.EqualTo(SimulationResult.StaleHandle));
            }
        }

        // ---- active path ---------------------------------------------------------------------------------

        private static List<string> Path(TreeGroup group, AgentHandle agent, out AgentDebugState state)
        {
            using (var path = new NativeList<ActivePathNode>(8, Allocator.Temp))
            {
                Assert.That(group.TryGetActivePath(agent, path, out state), Is.EqualTo(SimulationResult.Success));
                Assert.That(state.PathDepth, Is.EqualTo(path.Length));
                var labels = new List<string>();
                for (var index = 0; index < path.Length; index++)
                {
                    Assert.That(path[index].Status, Is.EqualTo(NodeStatus.Running));
                    Assert.That(path[index].StableNodeId, Is.Not.Zero);
                    Assert.That(path[index].NodeTypeId, Is.Not.Zero);
                    labels.Add(group.NodeLabel(path[index].NodeIndex));
                }
                return labels;
            }
        }

        /// <summary>
        /// The path and state of an agent that has not thought, is running, waits on its completion, finished
        /// under Stop, restarted, and was removed (stale). Untraced: the query works for any agent.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ActivePath_RunningFinishedRestartedRemoved(bool useJobs)
        {
            using (var rig = NewRig(DebugTree, useJobs, Stopping(2), ThinkSchedule.Every(50, 1), wake: true))
            {
                var group = rig.Group;
                Assert.That(Path(group, rig.Agent, out var state), Is.Empty);
                Assert.That(state.State, Is.EqualTo(AgentRunState.NotStarted));
                Assert.That(state.HasThought, Is.False);
                Assert.That(state.HasNextThink, Is.True);
                Assert.That(state.NextThinkTick, Is.EqualTo(1u));
                Assert.That(state.FreshInstancePending, Is.False);

                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var first = SingleEvent(group, ActionEventKind.Started);
                Assert.That(Path(group, rig.Agent, out state), Is.EqualTo(new[] { "both", "first" }));
                Assert.That(state.State, Is.EqualTo(AgentRunState.Running));
                Assert.That(state.Agent, Is.EqualTo(rig.Agent));
                Assert.That(state.Updates, Is.EqualTo(1ul));
                Assert.That(state.HasThought, Is.True);
                Assert.That(state.LastThinkTick, Is.EqualTo(1u));
                Assert.That(state.LastThinkReasons, Is.EqualTo(ThinkReasons.Scheduled | ThinkReasons.FreshInstance));
                Assert.That(state.NextThinkTick, Is.EqualTo(51u));
                Assert.That(state.ThinkInterval, Is.EqualTo(50u));
                Assert.That(state.ThinkPhase, Is.EqualTo(1u));
                Assert.That(state.LiveActionCount, Is.EqualTo(1));
                Assert.That(state.HasRootResult, Is.False);
                Assert.That(state.IsTraced, Is.False);
                var instance = state.TreeInstanceId;

                using (var actions = new NativeList<LiveAction>(4, Allocator.Temp))
                {
                    Assert.That(group.TryCopyLiveActions(rig.Agent, actions), Is.EqualTo(SimulationResult.Success));
                    Assert.That(actions.Length, Is.EqualTo(1));
                    Assert.That(actions[0].Action, Is.EqualTo(first.Action));
                    Assert.That(actions[0].Completed, Is.False);
                    Assert.That(group.TryComplete(first.Action, ActionOutcome.Failed), Is.EqualTo(SimulationResult.Success));
                    Assert.That(group.TryCopyLiveActions(rig.Agent, actions), Is.EqualTo(SimulationResult.Success));
                    Assert.That(actions[0].Completed, Is.True);
                    Assert.That(actions[0].Outcome, Is.EqualTo(ActionOutcome.Failed));
                }
                Path(group, rig.Agent, out state);
                Assert.That(state.WakePending, Is.True, "The completion wakes the agent.");
                Assert.That(state.NextThinkTick, Is.EqualTo(2u), "A woken agent thinks in the next step.");

                // The job reads the failure: the memory sequence fails, the root finishes (Stop).
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                Assert.That(Path(group, rig.Agent, out state), Is.Empty);
                Assert.That(state.State, Is.EqualTo(AgentRunState.Finished));
                Assert.That(state.HasRootResult, Is.True);
                Assert.That(state.RootResult, Is.EqualTo(NodeStatus.Failure));
                Assert.That(state.LastThinkReasons, Is.EqualTo(ThinkReasons.ActionCompleted));
                Assert.That(state.HasNextThink, Is.False, "A finished agent thinks again only when restarted.");
                Assert.That(state.LiveActionCount, Is.Zero);

                Assert.That(group.TryRestart(rig.Agent), Is.EqualTo(SimulationResult.Success));
                Path(group, rig.Agent, out state);
                Assert.That(state.RestartPending, Is.True);
                Assert.That(state.HasNextThink, Is.True);
                Assert.That(state.NextThinkTick, Is.EqualTo(51u));
                for (uint tick = 3; tick <= 51; tick++) Assert.That(rig.Simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                Assert.That(Path(group, rig.Agent, out state), Is.EqualTo(new[] { "both", "first" }));
                Assert.That(state.State, Is.EqualTo(AgentRunState.Running));
                Assert.That(state.Updates, Is.EqualTo(1ul), "A fresh instance.");
                Assert.That(state.TreeInstanceId, Is.Not.EqualTo(instance));
                Assert.That(state.LastThinkReasons, Is.EqualTo(ThinkReasons.Scheduled | ThinkReasons.FreshInstance));
                Assert.That(state.NextThinkTick, Is.EqualTo(101u));

                Span<ActivePathNode> small = stackalloc ActivePathNode[1];
                Assert.That(group.TryGetActivePath(rig.Agent, small, out var needed, out _), Is.EqualTo(SimulationResult.CapacityExceeded));
                Assert.That(needed, Is.EqualTo(2));
                Span<ActivePathNode> room = stackalloc ActivePathNode[4];
                Assert.That(group.TryGetActivePath(rig.Agent, room, out var count, out state), Is.EqualTo(SimulationResult.Success));
                Assert.That(count, Is.EqualTo(2));
                Assert.That(group.NodeLabel(room[1].NodeIndex), Is.EqualTo("first"));
                Assert.That(room[1].StableNodeId, Is.EqualTo(AIBT.StableHash.Fnv1A64("first")));

                Assert.That(group.TryRemove(rig.Agent), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(52), Is.EqualTo(SimulationResult.Success));
                using (var path = new NativeList<ActivePathNode>(4, Allocator.Temp))
                    Assert.That(group.TryGetActivePath(rig.Agent, path, out _), Is.EqualTo(SimulationResult.StaleHandle));
                Assert.That(group.DescribeAgent(rig.Agent), Does.EndWith(": StaleHandle"));
            }
        }

        /// <summary>A faulted agent: its state carries the fault and its cause; the path is the machine's at the fault.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ActivePath_Faulted(bool useJobs)
        {
            var simulationSettings = Settings(useJobs);
            simulationSettings.MaximumStepsPerThink = 1000;
            using (var rig = NewRig(TreeSimulationActionTests.RunawayTree, useJobs, Stopping(2), ThinkSchedule.Every(1, 0), settings: simulationSettings))
            {
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                rig.Sense[0] = new CrewSense { Threat = 1f, Alarm = true };
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                var path = Path(rig.Group, rig.Agent, out var state);
                Assert.That(state.State, Is.EqualTo(AgentRunState.Faulted));
                Assert.That(state.Fault.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeUpdateStepLimitExceeded));
                Assert.That(state.Fault.Code, Is.EqualTo(rig.Group.GetFaults()[0].Failure.Code));
                Assert.That(state.FaultCause, Is.EqualTo(rig.Group.GetFaults()[0].Cause));
                Assert.That(state.HasNextThink, Is.False);
                Assert.That(state.LiveActionCount, Is.Zero, "The fault cancelled the live job.");
                TestContext.WriteLine(string.Join(" > ", path));
                Assert.That(path.First(), Is.EqualTo("both"));
                Assert.That(rig.Group.DescribeAgent(rig.Agent), Does.Contain("is faulted").And.Contain("Fault: NativeUpdateStepLimitExceeded (AIBT"));
            }
        }

        // ---- DescribeAgent ---------------------------------------------------------------------------------

        private const string ExpectedDescription =
            "Agent 1:0#1 (Tests/P10011/tree.aibt.json) is running, instance 1, 1 update, traced.\n"
            + "Thinks every 50 ticks at phase 1; last think at tick 1 (scheduled, fresh instance); next think at tick 3 (woken: thinks in the next step).\n"
            + "Active path (root first):\n"
            + "  both [aibt.core.memory-sequence, node 0] Running\n"
            + "    first [aibt.tests.p10011.job, node 1] Running\n"
            + "Last root result: none. Fault: none.\n"
            + "Live actions: 1 - first (channel 0x<channel>, sequence 1, completed Succeeded, read at the next think).\n"
            + "Trace: 8 records kept, 8 written, 0 dropped.\n"
            + "Last results in the trace:\n"
            + "  first [aibt.tests.p10011.job] ticked Running at tick 1\n";

        /// <summary>The text for a traced agent waiting on its completed job, pinned (node type names from the caller).</summary>
        [Test]
        public void DescribeAgent_TextIsPinned()
        {
            using (var rig = NewRig(DebugTree, false, Stopping(2), ThinkSchedule.Every(50, 1), wake: true))
            {
                var group = rig.Group;
                Assert.That(group.TrySetTraced(rig.Agent, true), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var first = SingleEvent(group, ActionEventKind.Started);
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryComplete(first.Action, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.Success));
                string Names(ulong id) => id == AIBT.StableHash.Fnv1A64("aibt.tests.p10011.job") ? "aibt.tests.p10011.job" : null;
                var text = group.DescribeAgent(rig.Agent, Names).Replace(first.Action.Channel.ToString("x16", CultureInfo.InvariantCulture), "<channel>");
                TestContext.WriteLine(text);
                if (text != ExpectedDescription) Assert.Fail("The text differs. Actual:\n" + text);
            }
        }

        // ---- registry ----------------------------------------------------------------------------------------

        private static List<RegisteredSimulation> Registered()
        {
            var list = new List<RegisteredSimulation>();
            TreeSimulationDebug.CopyRegistered(list);
            return list;
        }

        [Test]
        public void Registry_IsOptIn_HoldsTwoContainers_AndForgetsDisposedOnes()
        {
            Assert.That(TreeSimulationDebug.IsAvailable, Is.True, "The Editor has the registry.");
            using (var a = new TreeSimulation(Settings(false)))
            using (var b = new TreeSimulation(Settings(false)))
            {
                Assert.That(Registered().Any(item => item.Simulation == a || item.Simulation == b), Is.False, "Nothing is registered by default.");
                Assert.Throws<ArgumentNullException>(() => TreeSimulationDebug.Register(null, "x"));
                Assert.Throws<ArgumentException>(() => TreeSimulationDebug.Register(a, " "));
                Assert.That(TreeSimulationDebug.Register(a, "crew"), Is.True);
                Assert.That(TreeSimulationDebug.Register(b, "shift"), Is.True);
                var list = Registered().Where(item => item.Simulation == a || item.Simulation == b).ToList();
                Assert.That(list.Select(item => item.Name), Is.EqualTo(new[] { "crew", "shift" }));
                Assert.That(TreeSimulationDebug.Register(a, "crew-2"), Is.True, "Registering again renames.");
                Assert.That(Registered().Single(item => item.Simulation == a).Name, Is.EqualTo("crew-2"));

                Assert.That(TreeSimulationDebug.Unregister(a), Is.True);
                Assert.That(TreeSimulationDebug.Unregister(a), Is.False);
                Assert.That(Registered().Any(item => item.Simulation == a), Is.False);
                b.Dispose();
                Assert.That(Registered().Any(item => item.Simulation == b), Is.False, "A disposed container leaves the list.");
                Assert.That(TreeSimulationDebug.Register(b, "late"), Is.False, "A disposed container is refused.");
            }
        }

        [Test]
        public void Registry_PrunesACollectedContainer()
        {
            // Deterministic: the entry's weak target is cleared through a test seam, exactly what the GC does
            // to a container only the registry holds; the next read prunes it and lists the live one.
            var name = "collected-" + Guid.NewGuid().ToString("N");
            var live = new TreeSimulation(Settings(false));
            var gone = new TreeSimulation(Settings(false));
            try
            {
                Assert.That(TreeSimulationDebug.Register(live, name + "-live"), Is.True);
                Assert.That(TreeSimulationDebug.Register(gone, name), Is.True);
                Assert.That(TreeSimulationDebug.EntriesNamedForTests(name), Is.EqualTo(1));
                Assert.That(TreeSimulationDebug.ClearTargetForTests(gone), Is.True);
                Assert.That(TreeSimulationDebug.EntriesNamedForTests(name), Is.EqualTo(1), "Nothing is pruned before a read.");

                var list = Registered();
                Assert.That(list.Any(item => item.Name == name), Is.False, "A collected container is not listed.");
                Assert.That(TreeSimulationDebug.EntriesNamedForTests(name), Is.EqualTo(0), "A collected container's entry is pruned.");
                Assert.That(list.Single(item => item.Name == name + "-live").Simulation, Is.SameAs(live));
                Assert.That(TreeSimulationDebug.ClearTargetForTests(gone), Is.False, "Its entry is gone.");
            }
            finally
            {
                TreeSimulationDebug.Unregister(live);
                live.Dispose();
                gone.Dispose();
            }
        }

        [Test]
        public void Registry_HoldsContainersWeakly()
        {
            // The registry must not keep a container alive. Whether GC.Collect actually collects it depends on
            // the runtime (Mono/Boehm scans the stack conservatively), so a container that survives makes the
            // test inconclusive, not failed; the prune path itself is pinned by Registry_PrunesACollectedContainer.
            var name = "dropped-" + Guid.NewGuid().ToString("N");
            var probe = RegisterAndDrop(name);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            if (probe.TryGetTarget(out _))
                Assert.Inconclusive("The GC did not collect the dropped container in this run; weak holding is not observable here.");
            Assert.That(Registered().Any(item => item.Name == name), Is.False);
            Assert.That(TreeSimulationDebug.EntriesNamedForTests(name), Is.EqualTo(0), "A container only the registry holds is collected and pruned.");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static WeakReference<TreeSimulation> RegisterAndDrop(string name)
        {
            // No groups: the container holds no native memory, so leaving it undisposed leaks nothing.
            var simulation = new TreeSimulation(Settings(false));
            Assert.That(TreeSimulationDebug.Register(simulation, name), Is.True);
            return new WeakReference<TreeSimulation>(simulation);
        }

        // ---- busy -------------------------------------------------------------------------------------------

        [Test]
        public void DebugCalls_DuringStep_ReturnBusy()
        {
            using (var rig = NewRig(DebugTree, false, Stopping(2), ThinkSchedule.Every(1, 0)))
            {
                var group = rig.Group;
                Assert.That(group.TrySetTraced(rig.Agent, true), Is.EqualTo(SimulationResult.Success));
                var seen = new List<SimulationResult>();
                string text = null;
                rig.Simulation.StepProbeForTests = () =>
                {
                    using (var records = new NativeList<AgentTraceRecord>(4, Allocator.Temp))
                    using (var path = new NativeList<ActivePathNode>(4, Allocator.Temp))
                    using (var actions = new NativeList<LiveAction>(4, Allocator.Temp))
                    {
                        seen.Add(group.TrySetTraced(rig.Agent, false));
                        seen.Add(group.TrySetTraced(rig.Agent, true));
                        seen.Add(group.TryConfigureTracing(AgentTraceSettings.Default));
                        seen.Add(group.TryCopyTrace(rig.Agent, records, out _));
                        seen.Add(group.TryClearTrace(rig.Agent));
                        seen.Add(group.TryGetActivePath(rig.Agent, path, out _));
                        seen.Add(group.TryCopyLiveActions(rig.Agent, actions));
                        text = group.DescribeAgent(rig.Agent);
                    }
                };
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                rig.Simulation.StepProbeForTests = null;
                Assert.That(seen, Is.EqualTo(Enumerable.Repeat(SimulationResult.Busy, 7)));
                Assert.That(text, Does.EndWith(": Busy"));
                Assert.That(group.IsTraced(rig.Agent), Is.True, "Nothing changed during the step.");
            }
        }
    }
}
