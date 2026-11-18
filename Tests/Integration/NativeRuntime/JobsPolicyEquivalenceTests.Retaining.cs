using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIBT.Burst;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// ADR-P10-013 stage 2: the retaining reactive composites under every scheduling policy. The
    /// same population as the trees above, rebuilt around retaining composites, must produce the
    /// same observable outcome frame by frame under the default profile, forced Immediate, forced
    /// BatchedJobsSameFrame and forced JobsAgentFrame (inside the Burst job), and warmed-up frames
    /// must allocate no managed memory.
    /// </summary>
    public sealed partial class JobsPolicyEquivalenceTests
    {
        // A retaining guard over a v1 memory sequence that completes every few updates: the guard
        // fails while the fact is not positive (the retained branch is aborted), else the branch keeps
        // its place between updates instead of restarting.
        private const string RetainingSequenceTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10013.retaining-sequence"", ""name"": ""P10-013 retaining sequence"",
  ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""score"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.reactive-sequence-retaining"", ""typeVersion"": 1, ""children"": [""read"", ""work""] },
    ""read"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""work"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""wait"", ""set"", ""check""] },
    ""wait"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""set"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 2.5 }, ""bindings"": { ""key"": ""score"" } },
    ""check"": { ""type"": ""aibt.native.float-at-least"", ""typeVersion"": 1, ""parameters"": { ""threshold"": 2.0 }, ""bindings"": { ""key"": ""score"" } }
  }
}";

        // The reactive effect emitter with a retaining sequence: the guard is re-evaluated every
        // update, the running emitter is resumed (not aborted and entered again); clearing 'armed'
        // aborts it.
        private const string RetainingEffectTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10013.retaining-effect"", ""name"": ""P10-013 retaining effect"",
  ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""armed"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.reactive-sequence-retaining"", ""typeVersion"": 1, ""children"": [""armed"", ""emit""] },
    ""armed"": { ""type"": ""aibt.native.bool-is-true"", ""typeVersion"": 1, ""bindings"": { ""key"": ""armed"" } },
    ""emit"": { ""type"": ""aibt.tests.external-tree-effect"", ""typeVersion"": 1, ""parameters"": { ""value"": 17 }, ""bindings"": { ""live-target"": ""live-target"" } }
  }
}";

        // Never finishes: a repeater over a parallel whose first branch is a retaining selector. A
        // running low-priority patrol is retained; when the fact turns positive the higher-priority
        // branch runs and takes over (the retained patrol is aborted).
        private const string RetainingLoopTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10013.retaining-loop"", ""name"": ""P10-013 retaining loop"",
  ""root"": ""loop"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""both""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""both"": { ""type"": ""aibt.core.parallel"", ""typeVersion"": 1, ""parameters"": { ""policy"": ""require-all-success"" }, ""children"": [""choose"", ""hold""] },
    ""choose"": { ""type"": ""aibt.core.reactive-selector-retaining"", ""typeVersion"": 1, ""children"": [""flee"", ""patrol""] },
    ""flee"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""read"", ""run""] },
    ""read"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""run"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 1 } },
    ""patrol"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 3 } },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } }
  }
}";

        [Test]
        public void RetainingTrees_AreObservablyIdenticalUnderEveryPolicy_FrameByFrame()
        {
            var trees = new[] { Prepare(RetainingSequenceTree), Prepare(RetainingEffectTree), Prepare(RetainingLoopTree) };

            var immediate = Run(trees, Mode.ForcedImmediate, withTreeValues: true);
            var normal = Run(trees, Mode.DefaultProfile, withTreeValues: true);
            var batched = Run(trees, Mode.ForcedBatchedJobs, withTreeValues: true);
            var agentFrame = Run(trees, Mode.ForcedAgentFrame, withTreeValues: true);

            // The scenario must exercise what it compares: effects, restarts, guard aborts of a
            // retained branch and takeovers, all inside the agent-frame job for that run.
            Assert.That(immediate.Effects, Is.GreaterThan(20), "Effects are delivered.");
            Assert.That(immediate.Restarts, Is.GreaterThan(5), "Trees complete and restart.");
            Assert.That(immediate.Lines.Count(l => l.Contains(" NodeAbortStarted ")), Is.GreaterThan(5), "Retained branches are aborted.");
            Assert.That(immediate.Lines.Count(l => l.Contains(" result=Failure")), Is.GreaterThan(5), "Trees also fail.");
            Assert.That(agentFrame.AgentFrameEntries, Is.GreaterThan(FrameCount * 6), "Agents ran in the agent-frame job.");

            AssertSame(immediate, normal, "retaining, default profile");
            AssertSame(immediate, batched, "retaining, forced BatchedJobsSameFrame");
            AssertSame(immediate, agentFrame, "retaining, JobsAgentFrame");
        }

        [Test]
        public void RetainingEmitter_IsEnteredOnce_WhileAV1EmitterIsEnteredEveryUpdate()
        {
            // The difference the composite exists for, observed through the production trace: with
            // 'armed' held true the v1 reactive sequence aborts and enters its emitter every update,
            // the retaining one enters it once and then only ticks it.
            var v1 = Prepare(ReactiveEffectTree);
            var retaining = Prepare(RetainingEffectTree);
            void Arm(List<NativeTreeAgent> agents)
            {
                foreach (var agent in agents) agent.SetFact("armed", true);
            }

            var v1Outcome = Run(new[] { v1 }, Mode.ForcedImmediate, agentCount: 1,
                restartMode: NativeTreeRestartMode.Never, prepare: Arm);
            var retainingOutcome = Run(new[] { retaining }, Mode.ForcedImmediate, agentCount: 1,
                restartMode: NativeTreeRestartMode.Never, prepare: Arm);

            // Run rewrites 'armed' only for tree slot 1; with one tree every agent is slot 0, so the
            // fact stays as armed above.
            int Entered(Outcome outcome) => outcome.Lines.Count(l => l.Contains(" NodeEntered ") && l.Contains(" n2 "));
            int Aborted(Outcome outcome) => outcome.Lines.Count(l => l.Contains(" NodeAbortStarted "));
            Assert.That(Entered(v1Outcome), Is.EqualTo(FrameCount), "v1: the emitter is entered every update.");
            Assert.That(Aborted(v1Outcome), Is.EqualTo(FrameCount - 1), "v1: and aborted every update after the first.");
            Assert.That(Entered(retainingOutcome), Is.EqualTo(1), "Retaining: the emitter is entered once.");
            Assert.That(Aborted(retainingOutcome), Is.Zero, "Retaining: and never aborted.");
            Assert.That(retainingOutcome.Effects, Is.EqualTo(v1Outcome.Effects), "Both emit on the same frames.");
        }

        [TestCase(SchedulingPolicy.Immediate)]
        [TestCase(SchedulingPolicy.BatchedJobsSameFrame)]
        [TestCase(SchedulingPolicy.JobsAgentFrame)]
        public void WarmedUpRetainingFrames_DoNotAllocateManagedMemory(SchedulingPolicy policy)
        {
            // Guard passes, parking and resuming the retained branch reuse the instance's own frames
            // and memory: 200 warmed-up frames of retaining emitters and retaining-selector loops
            // allocate nothing on this thread. Zero tolerance.
            var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            recorder.FilterToCurrentThread();
            object kept = null;
            Assert.That(CountAllocations(recorder, () => kept = new byte[64], 10), Is.EqualTo(10), "The recorder must see allocations.");
            Assert.That(kept, Is.Not.Null);

            var trees = new[] { Prepare(RetainingLoopTree), Prepare(RetainingEffectTree) };
            var worldObject = Track(new GameObject("AIBT.Tests.P10013.AllocationWorld." + policy));
            var scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            var world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(world, "Awake");
            InvokePrivate(world, "OnEnable");
            SchedulingProfile profile;
            switch (policy)
            {
                case SchedulingPolicy.BatchedJobsSameFrame:
                    Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, false, out var capabilities, out var error),
                        Is.True, error.ToString());
                    scheduler.SetJobsCapabilities(capabilities);
                    profile = Forced(SchedulingPolicy.BatchedJobsSameFrame);
                    break;
                case SchedulingPolicy.JobsAgentFrame:
                    scheduler.AgentFrameMinimumBatchSize = 1;
                    profile = SchedulingProfile.JobsAgentFrame;
                    break;
                default:
                    profile = Forced(SchedulingPolicy.Immediate);
                    break;
            }
            var update = (Action)Delegate.CreateDelegate(typeof(Action), scheduler,
                typeof(ProductionTreeScheduler).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic));

            var agents = new List<NativeTreeAgent>();
            var effects = 0;
            try
            {
                for (var index = 0; index < 32; index++)
                {
                    var agent = Track(new GameObject("AIBT.Tests.P10013.AllocationAgent" + index)).AddComponent<NativeTreeAgent>();
                    agent.Tree = trees[index % trees.Length];
                    agent.World = world;
                    agent.RestartMode = NativeTreeRestartMode.Never;
                    agent.Profile = profile;
                    agent.OnEffect("hit", (int value) => effects++);
                    agent.Activate();
                    agent.SetFact("live-target", 1f);
                    if (index % trees.Length == 1) agent.SetFact("armed", true);
                    agents.Add(agent);
                }
                for (var frame = 0; frame < 60; frame++) update();
                var effectsBefore = effects;

                var allocations = CountAllocations(recorder, update, 200);

                Assert.That(effects - effectsBefore, Is.EqualTo(200 * 16), "Every emitter delivers one effect per frame while measured.");
                foreach (var agent in agents)
                {
                    Assert.That(agent.LastError, Is.Null);
                    Assert.That(agent.IsRunning, Is.True, "The trees keep running; nothing restarts.");
                }
                Assert.That(scheduler.TryGetFrameEntry(0, out var entry), Is.True);
                Assert.That(entry.SelectedPolicy, Is.EqualTo(policy), "The frame ran under the forced policy.");
                Assert.That(entry.AgentFrameFallback, Is.EqualTo(AgentFrameFallbackReason.None));
                Assert.That(allocations, Is.Zero,
                    allocations + " managed allocation(s) in 200 warmed-up " + policy + " frames of retaining trees; the tolerance is zero.");
            }
            finally
            {
                foreach (var agent in agents) if (agent != null) agent.Deactivate();
            }
        }
    }
}
