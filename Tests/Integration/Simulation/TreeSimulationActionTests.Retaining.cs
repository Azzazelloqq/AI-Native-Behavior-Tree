using AIBT.Simulation;
using AIBT.Tests.CodeGen.GenerationP10011;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Integration.Simulation
{
    public sealed partial class TreeSimulationActionTests
    {
        // CrewTree with the retaining reactive selector (ADR-P10-013): the alarm guard is re-evaluated
        // every think, the running job is kept (not aborted and started again) while the alarm is down,
        // and the alarm still preempts it.
        internal const string RetainingCrewTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10013.crew"", ""name"": ""P10-013 retaining crew"",
  ""root"": ""loop"",
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""cycle""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""cycle"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""top"", ""hold""] },
    ""top"": { ""type"": ""aibt.core.reactive-selector-retaining"", ""typeVersion"": 1, ""children"": [""alarm"", ""job""] },
    ""alarm"": { ""type"": ""aibt.tests.p10011.alarm"", ""typeVersion"": 1 },
    ""job"": { ""type"": ""aibt.tests.p10011.job"", ""typeVersion"": 1, ""parameters"": { ""power"": 1.0, ""patience"": 0, ""forget"": false } },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } }
  }
}";

        /// <summary>
        /// The F2 scenario above with a retaining reactive selector: the job is started once and polled
        /// while the alarm is down, its completion is consumed (JobDone is emitted), the next cycle's job is
        /// preempted by the alarm with one Cancelled (Preempted), and when the game completes a job in the
        /// same step the alarm rises, the alarm guard runs first and wins: one Cancelled, no JobDone, the
        /// handle is stale afterwards.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UnderARetainingSelector_TheJobStartsOnce_ItsCompletionIsConsumed_AndTheAlarmPreemptsIt(bool useJobs)
        {
            using (var rig = NewRig(RetainingCrewTree, useJobs, TreeGroupSettings.Default(2), ThinkSchedule.Every(1, 0)))
            {
                uint tick = 1;
                Assert.That(rig.Simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                var first = rig.Single(ActionEventKind.Started).Action;
                for (tick = 2; tick <= 4; tick++)
                {
                    Assert.That(rig.Simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    Assert.That(rig.Group.GetActionEvents().Length, Is.Zero, "The retained job is polled, not restarted.");
                    Assert.That(rig.Group.LiveActionCountForTests(0), Is.EqualTo(1));
                }

                // Completion consumed: the node reads the result and emits its follow-up command.
                Assert.That(rig.Group.TryComplete(first, ActionOutcome.Succeeded, new JobResult { Score = 9f, Hits = 9 }), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.GetActionEvents().Length, Is.Zero, "A consumed completion ends the action without a Cancelled event.");
                var done = rig.Group.GetCommands(Done);
                Assert.That(done.Length, Is.EqualTo(1), "The completion was consumed.");
                Assert.That(done[0].Agent, Is.EqualTo(rig.Agent));
                Assert.That(done[0].Value.Ok, Is.True);
                Assert.That(rig.Group.TryComplete(first, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleAction));

                // The next cycle starts a new job; the alarm preempts it once.
                var second = NextStart(rig, ref tick);
                Assert.That(second, Is.Not.EqualTo(first));
                rig.Sense[0] = new CrewSense { Threat = 1f, Alarm = true };
                Assert.That(rig.Simulation.Step(++tick), Is.EqualTo(SimulationResult.Success));
                var cancelled = rig.Single(ActionEventKind.Cancelled);
                Assert.That(cancelled.Action, Is.EqualTo(second));
                Assert.That(cancelled.CancelReason, Is.EqualTo(ActionCancelReason.Preempted));
                Assert.That(cancelled.FaultCancel, Is.False, "The cancel value comes from the node's Abort.");
                Assert.That(rig.Group.TryGetCancelPayload(cancelled, out JobCancel cancel), Is.EqualTo(SimulationResult.Success));
                Assert.That(cancel.Progress, Is.EqualTo(0.5f));
                for (var quiet = 0; quiet < 4; quiet++)
                {
                    Assert.That(rig.Simulation.Step(++tick), Is.EqualTo(SimulationResult.Success));
                    Assert.That(rig.Group.GetActionEvents().Length, Is.Zero, "No job starts while the alarm is up.");
                }

                // Same-step completion and alarm: the guard pass runs the alarm first, so preemption wins.
                rig.Sense[0] = new CrewSense { Threat = 0f, Alarm = false };
                var third = NextStart(rig, ref tick);
                Assert.That(rig.Group.TryComplete(third, ActionOutcome.Succeeded, new JobResult { Score = 1f, Hits = 1 }), Is.EqualTo(SimulationResult.Success));
                rig.Sense[0] = new CrewSense { Threat = 1f, Alarm = true };
                Assert.That(rig.Simulation.Step(++tick), Is.EqualTo(SimulationResult.Success));
                cancelled = rig.Single(ActionEventKind.Cancelled);
                Assert.That(cancelled.Action, Is.EqualTo(third));
                Assert.That(cancelled.CancelReason, Is.EqualTo(ActionCancelReason.Preempted));
                Assert.That(rig.Group.GetCommands(Done).Length, Is.Zero, "The completion was not consumed: preemption wins.");
                Assert.That(rig.Group.TryComplete(third, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleAction));
                Assert.That(rig.Group.LiveActionCountForTests(0), Is.Zero);
                Assert.That(rig.Group.GetFaults().Length, Is.Zero);
            }
        }

        /// <summary>
        /// The active path between thinks runs through the retaining selector to its retained job (the
        /// branch is parked only inside a think), and DescribeAgent names the retaining kind, for a traced and
        /// an untraced agent alike.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UnderARetainingSelector_TheActivePathAndDescriptionShowTheRetainedJob(bool traced)
        {
            using (var rig = NewRig(RetainingCrewTree, false, TreeGroupSettings.Default(2), ThinkSchedule.Every(1, 0)))
            {
                Assert.That(rig.Group.TrySetTraced(rig.Agent, traced), Is.EqualTo(SimulationResult.Success));
                for (uint tick = 1; tick <= 3; tick++) Assert.That(rig.Simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.LiveActionCountForTests(0), Is.EqualTo(1));

                var path = new NativeList<ActivePathNode>(8, Allocator.Temp);
                try
                {
                    Assert.That(rig.Group.TryGetActivePath(rig.Agent, path, out _), Is.EqualTo(SimulationResult.Success));
                    var types = new ulong[path.Length];
                    for (var index = 0; index < path.Length; index++)
                    {
                        types[index] = path[index].NodeTypeId;
                        Assert.That(path[index].Status, Is.EqualTo(NodeStatus.Running));
                    }
                    Assert.That(types, Is.EqualTo(new[]
                    {
                        StableHash.Fnv1A64("aibt.core.repeater"),
                        StableHash.Fnv1A64("aibt.core.memory-sequence"),
                        StableHash.Fnv1A64("aibt.core.reactive-selector-retaining"),
                        StableHash.Fnv1A64("aibt.tests.p10011.job"),
                    }), "loop > cycle > top > job: the retained job, not the alarm guard.");
                    Assert.That(path[3].NodeIndex, Is.EqualTo(4u));
                }
                finally { path.Dispose(); }

                string Names(ulong id) => id == StableHash.Fnv1A64("aibt.tests.p10011.job") ? "aibt.tests.p10011.job" : null;
                var text = rig.Group.DescribeAgent(rig.Agent, Names);
                TestContext.WriteLine(text);
                Assert.That(text, Does.Contain("      top [aibt.core.reactive-selector-retaining, node 2] Running\n"));
                Assert.That(text, Does.Contain("        job [aibt.tests.p10011.job, node 4] Running\n"));
                var activePath = text.Substring(text.IndexOf("Active path", System.StringComparison.Ordinal));
                activePath = activePath.Substring(0, activePath.IndexOf("Last root result", System.StringComparison.Ordinal));
                Assert.That(activePath, Does.Not.Contain("alarm ["), "The guard is not on the active path between thinks.");
                if (traced)
                {
                    // The trace shows this think's guard pass: the alarm ran and failed, then the job was ticked.
                    Assert.That(text, Does.Contain(", traced."));
                    Assert.That(text, Does.Contain("  alarm [type 0x").And.Contain("] exited Failure at tick 3\n"));
                    Assert.That(text, Does.Contain("  job [aibt.tests.p10011.job] ticked Running at tick 3\n"));
                }
                else Assert.That(text, Does.Not.Contain(", traced."));
            }
        }

        private static ActionHandle NextStart(Rig rig, ref uint tick)
        {
            for (var guard = 0; guard < 8; guard++)
            {
                Assert.That(rig.Simulation.Step(++tick), Is.EqualTo(SimulationResult.Success));
                var events = rig.Group.GetActionEvents();
                if (events.Length == 0) continue;
                Assert.That(events.Length, Is.EqualTo(1));
                Assert.That(events[0].Kind, Is.EqualTo(ActionEventKind.Started));
                return events[0].Action;
            }
            Assert.Fail("No job started.");
            return default;
        }
    }
}
