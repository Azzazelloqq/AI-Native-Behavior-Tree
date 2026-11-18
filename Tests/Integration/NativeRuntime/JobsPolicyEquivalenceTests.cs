using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    // Built-in tick leaves, the package's standard native nodes and the P7-039 fact/effect fixture
    // nodes in one project catalog, so one scheduler group can hold every tree below.
    [AibtCatalogSet("aibt.tests.p10002.jobs-equivalence", 1u, typeof(BuiltInLeafShard), typeof(StandardNativeShard), typeof(ExternalTreeWriteFixtureShard))]
    public static partial class JobsEquivalenceCatalog { }

    /// <summary>
    /// P10-002 safety net for the Jobs speed-up: the same population of <see cref="NativeTreeAgent"/>s
    /// with the same scripted facts runs under the default profile, a forced <c>Immediate</c> profile
    /// and a forced <c>BatchedJobsSameFrame</c> profile, and every frame's observable outcome must be
    /// identical: each agent's trace records (kind, update, sequence, node, status, exit and abort
    /// reason), root result, running state and completed-run count, and the order and payload of
    /// every delivered effect. The trees restart, abort reactively, run a parallel, loop, read facts
    /// written between frames and emit effects. Async completions are not covered: the facade does
    /// not wire them (generated group dispatch passes an empty completion set).
    /// </summary>
    public sealed partial class JobsPolicyEquivalenceTests
    {
        private const int FrameCount = 90;
        private const int AgentsPerTree = 4;

        // Completes every few updates and restarts; fails while the fact is not positive.
        private const string SequenceTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10002.sequence"", ""name"": ""P10-002 sequence"",
  ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""score"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""read"", ""wait"", ""set"", ""check""] },
    ""read"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""wait"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""set"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 2.5 }, ""bindings"": { ""key"": ""score"" } },
    ""check"": { ""type"": ""aibt.native.float-at-least"", ""typeVersion"": 1, ""parameters"": { ""threshold"": 2.0 }, ""bindings"": { ""key"": ""score"" } }
  }
}";

        // A reactive guard over a running effect emitter. The reactive sequence re-evaluates the guard
        // and resets the running branch every update (abort, then enter again); clearing 'armed' fails it.
        private const string ReactiveEffectTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10002.reactive-effect"", ""name"": ""P10-002 reactive effect"",
  ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""armed"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.reactive-sequence"", ""typeVersion"": 1, ""children"": [""armed"", ""emit""] },
    ""armed"": { ""type"": ""aibt.native.bool-is-true"", ""typeVersion"": 1, ""bindings"": { ""key"": ""armed"" } },
    ""emit"": { ""type"": ""aibt.tests.external-tree-effect"", ""typeVersion"": 1, ""parameters"": { ""value"": 17 }, ""bindings"": { ""live-target"": ""live-target"" } }
  }
}";

        // Never finishes: a repeater over a parallel whose branches complete at different updates.
        // Every iteration holds for at least one update (the reader is wrapped in a succeeder), so
        // the repeater never spins inside one update.
        private const string LoopTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10002.loop"", ""name"": ""P10-002 loop"",
  ""root"": ""loop"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""both""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""both"": { ""type"": ""aibt.core.parallel"", ""typeVersion"": 1, ""parameters"": { ""policy"": ""require-all-success"" }, ""children"": [""choose"", ""observe"", ""hold""] },
    ""choose"": { ""type"": ""aibt.core.memory-selector"", ""typeVersion"": 1, ""children"": [""gate"", ""patrol""] },
    ""gate"": { ""type"": ""aibt.core.failer"", ""typeVersion"": 1, ""children"": [""gate-wait""] },
    ""gate-wait"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 1 } },
    ""patrol"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 3 } },
    ""observe"": { ""type"": ""aibt.core.succeeder"", ""typeVersion"": 1, ""children"": [""read""] },
    ""read"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } }
  }
}";

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private int _baselinePrograms;

        [SetUp]
        public void SetUp() => _baselinePrograms = NativeTreeProgramCache.LiveProgramCount;

        [TearDown]
        public void TearDown()
        {
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) UnityEngine.Object.DestroyImmediate(_created[index]);
            _created.Clear();
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms), "A test leaked a shared program.");
        }

        [Test]
        public void BatchedJobsSameFrame_IsObservablyIdenticalToImmediate_FrameByFrame()
        {
            var trees = new[] { Prepare(SequenceTree), Prepare(ReactiveEffectTree), Prepare(LoopTree) };

            var normal = Run(trees, Mode.DefaultProfile);
            var immediate = Run(trees, Mode.ForcedImmediate);
            var batched = Run(trees, Mode.ForcedBatchedJobs);

            // The scenario must actually exercise what it claims to compare.
            Assert.That(normal.Effects, Is.GreaterThan(20), "Effects are delivered.");
            Assert.That(normal.Restarts, Is.GreaterThan(20), "Trees complete and restart.");
            Assert.That(normal.Lines.Count(l => l.Contains(" NodeAbortStarted ")), Is.GreaterThan(5), "Reactive aborts happen.");
            Assert.That(normal.Lines.Count(l => l.Contains(" result=Failure")), Is.GreaterThan(5), "Trees also fail.");

            AssertSame(normal, immediate, "forced Immediate");
            AssertSame(normal, batched, "forced BatchedJobsSameFrame");
        }

        [Test]
        public void JobsAgentFrame_IsObservablyIdenticalToImmediate_FrameByFrame()
        {
            // P10-004: the whole update of every agent runs inside one Burst job work item. Every
            // frame's trace records, root result, running state, run count, error, delivered effects
            // in order, and every byte of each agent's Tree/Agent blackboard storage must equal the
            // forced Immediate run. Run asserts every frame entry ran in the job with no fallback.
            var trees = new[] { Prepare(SequenceTree), Prepare(ReactiveEffectTree), Prepare(LoopTree) };

            var immediate = Run(trees, Mode.ForcedImmediate, withTreeValues: true);
            var agentFrame = Run(trees, Mode.ForcedAgentFrame, withTreeValues: true);

            Assert.That(immediate.Effects, Is.GreaterThan(20), "Effects are delivered.");
            Assert.That(immediate.Restarts, Is.GreaterThan(20), "Trees complete and restart.");
            Assert.That(immediate.Lines.Count(l => l.Contains(" NodeAbortStarted ")), Is.GreaterThan(5), "Reactive aborts happen.");
            Assert.That(agentFrame.AgentFrameEntries, Is.GreaterThan(FrameCount * 6), "Agents ran in the agent-frame job.");
            AssertSame(immediate, agentFrame, "JobsAgentFrame");
        }

        [Test]
        public void JobsAgentFrame_StepBudgetHosts_FallBackInTheirDuePosition_AndMatchImmediate()
        {
            // P10-004: every other agent carries a per-host step budget, which the job does not
            // implement. Those agents run DriveOneUpdate between job batches, in due order; the frame's
            // outcome must still equal a forced Immediate run of the same configuration.
            var trees = new[] { Prepare(SequenceTree), Prepare(ReactiveEffectTree), Prepare(LoopTree) };
            void Budget(List<NativeTreeAgent> agents)
            {
                for (var index = 0; index < agents.Count; index += 2) agents[index].Host.StepBudget = 1000000u;
            }

            var immediate = Run(trees, Mode.ForcedImmediate, withTreeValues: true, prepare: Budget);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(@"cannot run in a JobsAgentFrame job \(StepBudgetConfigured\)"));
            var agentFrame = Run(trees, Mode.ForcedAgentFrame, withTreeValues: true, prepare: Budget,
                allowedFallback: AgentFrameFallbackReason.StepBudgetConfigured);

            // A restart creates a new host without the budget, so fewer entries fall back as the run goes on.
            Assert.That(agentFrame.FallbackEntries, Is.GreaterThan(FrameCount), "Budgeted agents fell back.");
            Assert.That(agentFrame.AgentFrameEntries, Is.GreaterThan(FrameCount * 5), "The others ran in the job.");
            AssertSame(immediate, agentFrame, "JobsAgentFrame with step-budget fallbacks");
        }

        [Test]
        public void JobsAgentFrame_CatalogWithoutAgentFrameEntry_FallsBackForEveryAgent_AndMatchesImmediate()
        {
            // P10-004: a catalog generated before P10-004 has no agent-frame entry. Simulated by
            // wrapping the catalog's executor in one that implements only the V2 executor interface.
            var trees = new[] { Prepare(SequenceTree), Prepare(ReactiveEffectTree), Prepare(LoopTree) };
            var swapped = new List<(object Catalog, object Executor)>();
            var executorProperty = typeof(GeneratedBurstCatalogV2).GetProperty("Executor", BindingFlags.Instance | BindingFlags.NonPublic);
            void Swap(List<NativeTreeAgent> agents)
            {
                foreach (var agent in agents)
                {
                    var catalog = agent.Host.GeneratedDispatchAdapter.Catalog;
                    var executor = (IGeneratedBurstCatalogExecutorV2)executorProperty.GetValue(catalog);
                    if (executor is LegacyExecutor) continue;
                    swapped.Add((catalog, executor));
                    executorProperty.SetValue(catalog, new LegacyExecutor(executor));
                }
            }

            var immediate = Run(trees, Mode.ForcedImmediate, withTreeValues: true);
            Outcome agentFrame;
            try
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex(@"cannot run in a JobsAgentFrame job \(CatalogWithoutAgentFrameEntry\)"));
                agentFrame = Run(trees, Mode.ForcedAgentFrame, withTreeValues: true, prepare: Swap,
                    allowedFallback: AgentFrameFallbackReason.CatalogWithoutAgentFrameEntry);
            }
            finally
            {
                foreach (var (catalog, executor) in swapped) executorProperty.SetValue(catalog, executor);
            }

            Assert.That(swapped, Is.Not.Empty, "The catalog's executor was replaced.");
            Assert.That(agentFrame.AgentFrameEntries, Is.Zero);
            Assert.That(agentFrame.FallbackEntries, Is.GreaterThan(FrameCount * 10));
            AssertSame(immediate, agentFrame, "JobsAgentFrame on a catalog without an agent-frame entry");
        }

        [Test]
        public void JobsAgentFrame_BatchShorterThanTheMinimum_RunsInPlace_AndMatchesImmediate()
        {
            // P10-007 step 1: with the default minimum (64) a 12-agent JobsAgentFrame batch schedules
            // no job; every member runs on the main thread as Immediate does, its entry still reports
            // JobsAgentFrame and says it ran in place, and every frame equals a forced Immediate run.
            var trees = new[] { Prepare(SequenceTree), Prepare(ReactiveEffectTree), Prepare(LoopTree) };

            var immediate = Run(trees, Mode.ForcedImmediate, withTreeValues: true);
            var agentFrame = Run(trees, Mode.ForcedAgentFrame, withTreeValues: true,
                agentFrameMinimumBatch: ProductionTreeScheduler.DefaultAgentFrameMinimumBatchSize);

            Assert.That(ProductionTreeScheduler.DefaultAgentFrameMinimumBatchSize, Is.EqualTo(64));
            Assert.That(agentFrame.JobBatches, Is.Zero, "No job was scheduled.");
            Assert.That(agentFrame.AgentFrameEntries, Is.Zero);
            Assert.That(agentFrame.InPlaceEntries, Is.GreaterThan(FrameCount * 6), "Entries say the batch ran in place.");
            AssertSame(immediate, agentFrame, "JobsAgentFrame batches run in place");
        }

        [TestCase(63, false)]
        [TestCase(64, true)]
        public void JobsAgentFrame_MinimumBatchBoundary(int agents, bool expectJob)
        {
            // P10-007 step 1: 63 consecutive eligible members run in place, 64 run in one job per frame.
            var trees = new[] { Prepare(LoopTree) };

            var immediate = Run(trees, Mode.ForcedImmediate, agentCount: agents, restartMode: NativeTreeRestartMode.Never);
            var agentFrame = Run(trees, Mode.ForcedAgentFrame, agentCount: agents, restartMode: NativeTreeRestartMode.Never,
                agentFrameMinimumBatch: ProductionTreeScheduler.DefaultAgentFrameMinimumBatchSize);

            Assert.That(agentFrame.LastFrameEntries.Count, Is.EqualTo(agents));
            Assert.That(agentFrame.LastFrameEntries.All(e => e.SelectedPolicy == SchedulingPolicy.JobsAgentFrame), Is.True);
            Assert.That(agentFrame.LastFrameEntries.All(e => e.AgentFrameRanInPlace == !expectJob), Is.True,
                agents + " members: ran in place = " + !expectJob);
            Assert.That(agentFrame.JobBatches, Is.EqualTo(expectJob ? FrameCount : 0));
            AssertSame(immediate, agentFrame, agents + " members");
        }

        [TestCase(40, 40, false)]
        [TestCase(64, 10, true)]
        public void JobsAgentFrame_FallbackMemberSplitsTheBatch_ShortPiecesRunInPlace(int before, int after, bool firstInJob)
        {
            // P10-007 step 1: a step-budget member between two runs of eligible members closes the
            // batch. A piece shorter than 64 runs in place; a piece of 64 runs in the job.
            var trees = new[] { Prepare(LoopTree) };
            void Budget(List<NativeTreeAgent> agents) => agents[before].Host.StepBudget = 1000000u;

            var immediate = Run(trees, Mode.ForcedImmediate, withTreeValues: true, prepare: Budget,
                agentCount: before + 1 + after, restartMode: NativeTreeRestartMode.Never);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(@"cannot run in a JobsAgentFrame job \(StepBudgetConfigured\)"));
            var agentFrame = Run(trees, Mode.ForcedAgentFrame, withTreeValues: true, prepare: Budget,
                agentCount: before + 1 + after, restartMode: NativeTreeRestartMode.Never,
                allowedFallback: AgentFrameFallbackReason.StepBudgetConfigured,
                agentFrameMinimumBatch: ProductionTreeScheduler.DefaultAgentFrameMinimumBatchSize);

            var entries = agentFrame.LastFrameEntries;
            Assert.That(entries.Count, Is.EqualTo(before + 1 + after));
            for (var index = 0; index < entries.Count; index++)
            {
                if (index == before)
                {
                    Assert.That(entries[index].AgentFrameFallback, Is.EqualTo(AgentFrameFallbackReason.StepBudgetConfigured));
                    Assert.That(entries[index].AgentFrameRanInPlace, Is.False);
                    continue;
                }
                var inJob = index < before && firstInJob;
                Assert.That(entries[index].AgentFrameRanInPlace, Is.EqualTo(!inJob), "entry " + index);
            }
            Assert.That(agentFrame.JobBatches, Is.EqualTo(firstInJob ? FrameCount : 0));
            AssertSame(immediate, agentFrame, before + " + 1 + " + after);
        }

        // ---- P10-007 step 2: automatic selection for profiles that force no policy ----

        [Test]
        public void AutomaticSelection_IsObservablyIdenticalToImmediate_FrameByFrame()
        {
            // 70 agents per tree, each tree's agents next to each other: the default profile selects
            // JobsAgentFrame for the runs of 64+, and every frame equals a forced Immediate run.
            var trees = new[] { Prepare(SequenceTree), Prepare(ReactiveEffectTree), Prepare(LoopTree) };

            var immediate = Run(trees, Mode.ForcedImmediate, withTreeValues: true, agentCount: 210, groupedTrees: true);
            var automatic = Run(trees, Mode.DefaultProfile, withTreeValues: true, agentCount: 210, groupedTrees: true);

            Assert.That(immediate.Effects, Is.GreaterThan(20), "Effects are delivered.");
            Assert.That(immediate.Restarts, Is.GreaterThan(20), "Trees complete and restart.");
            Assert.That(immediate.Lines.Count(l => l.Contains(" NodeAbortStarted ")), Is.GreaterThan(5), "Reactive aborts happen.");
            Assert.That(immediate.JobBatches, Is.Zero);
            Assert.That(automatic.JobBatches, Is.GreaterThanOrEqualTo(FrameCount), "Runs were selected every frame.");
            Assert.That(automatic.AutomaticEntries, Is.GreaterThan(FrameCount * 70), "Entries report the automatic choice.");
            AssertSame(immediate, automatic, "automatic JobsAgentFrame");
        }

        [TestCase(63, false)]
        [TestCase(64, true)]
        public void AutomaticSelection_Boundary(int agents, bool expectSelected)
        {
            // 63 consecutive eligible members stay Immediate; 64 run as JobsAgentFrame in one job.
            var trees = new[] { Prepare(LoopTree) };

            var immediate = Run(trees, Mode.ForcedImmediate, withTreeValues: true, agentCount: agents, restartMode: NativeTreeRestartMode.Never);
            var automatic = Run(trees, Mode.DefaultProfile, withTreeValues: true, agentCount: agents, restartMode: NativeTreeRestartMode.Never);

            var entries = automatic.LastFrameEntries;
            Assert.That(entries.Count, Is.EqualTo(agents));
            foreach (var entry in entries)
            {
                Assert.That(entry.SelectedPolicy, Is.EqualTo(expectSelected ? SchedulingPolicy.JobsAgentFrame : SchedulingPolicy.Immediate));
                Assert.That(entry.SelectionReason, Is.EqualTo(expectSelected ? SchedulerSelectionReason.AutomaticAgentFrameRun : SchedulerSelectionReason.None));
                Assert.That(entry.SelectionSource, Is.EqualTo(SchedulerFrameSelectionSource.DirectHostPath));
                Assert.That(entry.AgentFrameRanInPlace, Is.False);
                Assert.That(entry.AgentFrameFallback, Is.EqualTo(AgentFrameFallbackReason.None));
            }
            Assert.That(automatic.JobBatches, Is.EqualTo(expectSelected ? FrameCount : 0));
            AssertSame(immediate, automatic, agents + " default-profile agents");
        }

        [TestCase(40, 40, false)]
        [TestCase(64, 10, true)]
        public void AutomaticSelection_IneligibleMemberBreaksTheRun_Silently(int before, int after, bool firstSelected)
        {
            // A step-budget member is not eligible: it stays Budgeted, logs nothing, and splits the
            // eligible members into two runs judged separately (40 + 1 + 40: none; 64 + 1 + 10: the first).
            var trees = new[] { Prepare(LoopTree) };
            void Budget(List<NativeTreeAgent> agents) => agents[before].Host.StepBudget = 1000000u;

            var immediate = Run(trees, Mode.ForcedImmediate, withTreeValues: true, prepare: Budget,
                agentCount: before + 1 + after, restartMode: NativeTreeRestartMode.Never);
            var automatic = Run(trees, Mode.DefaultProfile, withTreeValues: true, prepare: Budget,
                agentCount: before + 1 + after, restartMode: NativeTreeRestartMode.Never);
            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();

            var entries = automatic.LastFrameEntries;
            Assert.That(entries.Count, Is.EqualTo(before + 1 + after));
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                Assert.That(entry.AgentFrameFallback, Is.EqualTo(AgentFrameFallbackReason.None), "entry " + index);
                Assert.That(entry.AgentFrameRanInPlace, Is.False, "entry " + index);
                var expected = index == before ? SchedulingPolicy.Budgeted
                    : index < before && firstSelected ? SchedulingPolicy.JobsAgentFrame : SchedulingPolicy.Immediate;
                Assert.That(entry.SelectedPolicy, Is.EqualTo(expected), "entry " + index);
            }
            Assert.That(automatic.JobBatches, Is.EqualTo(firstSelected ? FrameCount : 0));
            AssertSame(immediate, automatic, before + " + 1 + " + after);
        }

        [Test]
        public void AutomaticSelection_CatalogWithoutAgentFrameEntry_StaysImmediate_WithoutWarnings()
        {
            // A catalog generated before P10-004 is never selected and never warns (the forced
            // profile warns once per host; the automatic choice must not, or old projects fill the log).
            var trees = new[] { Prepare(LoopTree) };
            var swapped = new List<(object Catalog, object Executor)>();
            var executorProperty = typeof(GeneratedBurstCatalogV2).GetProperty("Executor", BindingFlags.Instance | BindingFlags.NonPublic);
            void Swap(List<NativeTreeAgent> agents)
            {
                foreach (var agent in agents)
                {
                    var catalog = agent.Host.GeneratedDispatchAdapter.Catalog;
                    var executor = (IGeneratedBurstCatalogExecutorV2)executorProperty.GetValue(catalog);
                    if (executor is LegacyExecutor) continue;
                    swapped.Add((catalog, executor));
                    executorProperty.SetValue(catalog, new LegacyExecutor(executor));
                }
            }

            var immediate = Run(trees, Mode.ForcedImmediate, withTreeValues: true, agentCount: 80, restartMode: NativeTreeRestartMode.Never);
            Outcome automatic;
            try
            {
                automatic = Run(trees, Mode.DefaultProfile, withTreeValues: true, prepare: Swap, agentCount: 80,
                    restartMode: NativeTreeRestartMode.Never);
            }
            finally
            {
                foreach (var (catalog, executor) in swapped) executorProperty.SetValue(catalog, executor);
            }
            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();

            Assert.That(swapped, Is.Not.Empty, "The catalog's executor was replaced.");
            Assert.That(automatic.JobBatches, Is.Zero);
            Assert.That(automatic.AutomaticEntries, Is.Zero);
            Assert.That(automatic.LastFrameEntries.All(e => e.SelectedPolicy == SchedulingPolicy.Immediate
                && e.AgentFrameFallback == AgentFrameFallbackReason.None), Is.True);
            AssertSame(immediate, automatic, "automatic selection on a catalog without an agent-frame entry");
        }

        [Test]
        public void AutomaticSelection_DoesNotApplyUnderABoundedBudget_ShareAdmissionUnchanged()
        {
            // Under a fixed budget every default-profile host keeps the Immediate path and its
            // per-host admission: a tiny MaximumBudgetShare still defers all but the first host of
            // the profile each frame, exactly as before P10-007.
            var trees = new[] { Prepare(LoopTree) };
            Assert.That(SchedulingProfile.TryCreate("tests.p10007.share", 0, 1u, null, 1e-9, false, null,
                out var shareProfile, out var error), Is.True, error.ToString());

            var automatic = Run(trees, Mode.DefaultProfile, agentCount: 80, restartMode: NativeTreeRestartMode.Never,
                profileOverride: shareProfile, configure: scheduler => scheduler.SetFixedBudget(1000000.0));

            Assert.That(automatic.JobBatches, Is.Zero);
            Assert.That(automatic.AutomaticEntries, Is.Zero);
            var entries = automatic.LastFrameEntries;
            Assert.That(entries.Count, Is.EqualTo(80));
            Assert.That(entries.All(e => e.SelectedPolicy == SchedulingPolicy.Immediate), Is.True);
            Assert.That(entries.Count(e => e.Disposition == SchedulerFrameDisposition.Executed), Is.EqualTo(1),
                "The share cap admits one host per frame.");
            Assert.That(automatic.DeferredShareEntries, Is.EqualTo(FrameCount * 79));
        }

        [Test]
        public void AutomaticSelection_Off_RestoresThePreviousBehaviour()
        {
            // AutomaticAgentFrameSelection = false: the population that is selected above stays on the
            // direct Immediate path, with the entries it produced before P10-007.
            var trees = new[] { Prepare(SequenceTree), Prepare(ReactiveEffectTree), Prepare(LoopTree) };

            var immediate = Run(trees, Mode.ForcedImmediate, withTreeValues: true, agentCount: 210, groupedTrees: true);
            var off = Run(trees, Mode.DefaultProfile, withTreeValues: true, agentCount: 210, groupedTrees: true,
                configure: scheduler =>
                {
                    Assert.That(scheduler.AutomaticAgentFrameSelection, Is.True, "On by default.");
                    scheduler.AutomaticAgentFrameSelection = false;
                });

            Assert.That(off.JobBatches, Is.Zero);
            Assert.That(off.AutomaticEntries, Is.Zero);
            Assert.That(off.InPlaceEntries, Is.Zero);
            foreach (var entry in off.LastFrameEntries)
            {
                Assert.That(entry.SelectedPolicy, Is.EqualTo(SchedulingPolicy.Immediate));
                Assert.That(entry.SelectionReason, Is.EqualTo(SchedulerSelectionReason.None));
                Assert.That(entry.SelectionSource, Is.EqualTo(SchedulerFrameSelectionSource.DirectHostPath));
                Assert.That(entry.AgentFrameFallback, Is.EqualTo(AgentFrameFallbackReason.None));
            }
            AssertSame(immediate, off, "automatic selection off");
        }

        private sealed class LegacyExecutor : IGeneratedBurstCatalogExecutorV2
        {
            private readonly IGeneratedBurstCatalogExecutorV2 _inner;
            internal LegacyExecutor(IGeneratedBurstCatalogExecutorV2 inner) => _inner = inner;
            public BurstExecutionResult ExecuteImmediate(ref BurstExecutionBatch batch) => _inner.ExecuteImmediate(ref batch);
            public Unity.Jobs.JobHandle Schedule(ref BurstExecutionBatch batch, Unity.Jobs.JobHandle dependency) => _inner.Schedule(ref batch, dependency);
            public bool TryGetEffectDescriptor(uint catalogCaseIndex, uint bindingOrdinal, out ProductionTreeEffectDescriptor descriptor)
                => _inner.TryGetEffectDescriptor(catalogCaseIndex, bindingOrdinal, out descriptor);
        }

        // A repeater over an instant leaf never yields inside one update.
        private const string SpinTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10004.spin"", ""name"": ""P10-004 spin"",
  ""root"": ""loop"",
  ""blackboard"": {
    ""score"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""set""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""set"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 1.0 }, ""bindings"": { ""key"": ""score"" } }
  }
}";

        // The same shape, bounded: finishes inside one update well under the test's step limit.
        private const string BoundedRepeatTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10004.bounded"", ""name"": ""P10-004 bounded"",
  ""root"": ""loop"",
  ""blackboard"": {
    ""score"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""set""], ""parameters"": { ""count"": 50, ""stopOnFailure"": false } },
    ""set"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 1.0 }, ""bindings"": { ""key"": ""score"" } }
  }
}";

        [Test]
        public void StepLimit_StopsATreeThatNeverYields_UnderImmediateBatchedJobsAndJobsAgentFrame()
        {
            // P10-004: MaximumStepsPerUpdate fails the host with NativeUpdateStepLimitExceeded under
            // every policy (inside the Burst job for JobsAgentFrame), and leaves a tree that finishes
            // within the limit alone.
            var spin = Prepare(SpinTree);
            var bounded = Prepare(BoundedRepeatTree);
            const uint limit = 1000u;
            var ignoreFailingMessages = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                foreach (var mode in new[] { Mode.ForcedImmediate, Mode.ForcedBatchedJobs, Mode.ForcedAgentFrame })
                {
                    var worldObject = Track(new GameObject("AIBT.Tests.P10004.StepLimitWorld." + mode));
                    var scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
                    var world = worldObject.AddComponent<ProductionTreeWorld>();
                    InvokePrivate(world, "Awake");
                    InvokePrivate(world, "OnEnable");
                    var profile = ProfileFor(mode, scheduler);
                    // P10-007: two members would run in place by default; this test covers the job.
                    scheduler.AgentFrameMinimumBatchSize = 1;
                    var expectedPolicy = mode == Mode.ForcedImmediate ? SchedulingPolicy.Immediate
                        : mode == Mode.ForcedBatchedJobs ? SchedulingPolicy.BatchedJobsSameFrame : SchedulingPolicy.JobsAgentFrame;

                    NativeTreeAgent Create(ProductionTreeAsset tree, string name)
                    {
                        var agent = Track(new GameObject("AIBT.Tests.P10004." + name + "." + mode)).AddComponent<NativeTreeAgent>();
                        agent.Tree = tree;
                        agent.World = world;
                        agent.RestartMode = NativeTreeRestartMode.Never;
                        agent.Profile = profile;
                        agent.Activate();
                        agent.Host.MaximumStepsPerUpdate = limit;
                        return agent;
                    }
                    var spinning = Create(spin, "Spin");
                    var finishing = Create(bounded, "Bounded");
                    var spinHost = spinning.Host;
                    var finishingHost = finishing.Host;
                    try
                    {
                        InvokePrivate(scheduler, "Update");

                        Assert.That(spinHost.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeUpdateStepLimitExceeded), mode.ToString());
                        Assert.That(spinHost.StepsThisUpdate, Is.EqualTo((ulong)limit), mode + ": stopped exactly at the limit");
                        Assert.That(finishingHost.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None), mode.ToString());
                        Assert.That(finishingHost.LastRootResult, Is.EqualTo(NodeStatus.Success), mode.ToString());
                        Assert.That(finishingHost.StepsThisUpdate, Is.GreaterThan(50uL).And.LessThan((ulong)limit), mode.ToString());
                        Assert.That(scheduler.FrameEntryCount, Is.EqualTo(2), mode.ToString());
                        for (var index = 0; index < scheduler.FrameEntryCount; index++)
                        {
                            Assert.That(scheduler.TryGetFrameEntry(index, out var entry), Is.True);
                            Assert.That(entry.SelectedPolicy, Is.EqualTo(expectedPolicy), mode.ToString());
                            Assert.That(entry.AgentFrameFallback, Is.EqualTo(AgentFrameFallbackReason.None), mode.ToString());
                            // BatchedJobsSameFrame entries carry the group's disposition (unchanged by P10-004).
                            if (mode != Mode.ForcedBatchedJobs)
                                Assert.That(entry.Disposition, Is.EqualTo(entry.HostInstanceId == spinHost.InstanceId
                                    ? SchedulerFrameDisposition.Failed : SchedulerFrameDisposition.Executed), mode.ToString());
                        }
                    }
                    finally
                    {
                        spinning.Deactivate();
                        finishing.Deactivate();
                    }
                }
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = ignoreFailingMessages;
            }
        }

        private static SchedulingProfile ProfileFor(Mode mode, ProductionTreeScheduler scheduler)
        {
            switch (mode)
            {
                case Mode.ForcedImmediate:
                    return Forced(SchedulingPolicy.Immediate);
                case Mode.ForcedBatchedJobs:
                    Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, false, out var capabilities, out var error),
                        Is.True, error.ToString());
                    scheduler.SetJobsCapabilities(capabilities);
                    return Forced(SchedulingPolicy.BatchedJobsSameFrame);
                case Mode.ForcedAgentFrame:
                    return SchedulingProfile.JobsAgentFrame;
                default:
                    return null;
            }
        }

        // P10-003: per-frame SHA-256 (first 16 hex digits) of every observation of
        // AllPolicies_MatchTheOutcomeRecordedBeforeP10003_FrameByFrame, recorded on the runtime at
        // 4dee8ec (before any dispatch-framing change). One entry per frame.
        private static readonly string[] RecordedFrameDigests =
        {
            "c3c9c77fed8a3bc4",
            "e5a2a7c36ab1abc5",
            "f0ff5c40cd0c5c03",
            "c4171b60b0b9bfc4",
            "a9a8c91f446fb7ef",
            "1f79d53aaff309cb",
            "8b32f1691b63f96c",
            "cf66a06ae5a38651",
            "efd7d1289d2299fa",
            "fd5188ddc400c199",
            "fc153ed0b4660e44",
            "5f85ffaad9b6ac0b",
            "ab75f45088cc0dd6",
            "2da71c4f73b276b3",
            "fc898ca96975a0d8",
            "fe2e5c8987d92955",
            "d9e5da0c094538a7",
            "21f8b950b85510ed",
            "a4c38db82a779830",
            "65afc200546062f3",
            "9f0abbd5b3f7f9c8",
            "603c3da882e3094f",
            "be05332f4393d830",
            "8d0281b2405942e8",
            "61707468bce35c58",
            "01b84ddad2495ef0",
            "bda6d27d6264623b",
            "79bb4af1e082e4f7",
            "f38b42fe785fb202",
            "8eb78fdf212dc531",
            "49a98b2c0a0b385f",
            "2d5b8b992356a81e",
            "e1338d69415725d3",
            "933ef304dbfcce6f",
            "6c3edc5ebf11278b",
            "a6c09a78db6f9d77",
            "af6e05441a54d7ca",
            "fa0ca447bb1310b5",
            "97a83bade0612f92",
            "1f36bfe61f309934",
            "b4183933e6d18cfe",
            "903ccc0129d27a00",
            "c3ab1e936eb3f64d",
            "9ec66ba0e27670e3",
            "03a5f842afba1f81",
            "bf87f4781cd74a11",
            "2975a48d7a7a572a",
            "133d527894d8e348",
            "f71008c14b8ee209",
            "9ef6a9355280ef04",
            "2cc9183681aa8c04",
            "6411a3b99e60553b",
            "c30b1b06151e1142",
            "8692a56d4a4de39d",
            "421ff254f43d9e6c",
            "9fc723f76d2138d7",
            "09d7860986865c02",
            "ba515516901592bf",
            "b777d3e30cbcb218",
            "05c8740ea6c71085",
            "9069cd5e6378fdc5",
            "b65e89a9c6cd249c",
            "0815a04e09037016",
            "b4df66ff3b2bd716",
            "4afdffcdaebb99d3",
            "9fdb9ee1e92be6f5",
            "67b9744cff58c628",
            "e1c457e95b46bdf0",
            "9cede807208c73bf",
            "801d14dd5e58d83e",
            "a425c13338143a9b",
            "908bdeb07679e350",
            "89139f89a9e1c23a",
            "156f4ec97a10fe22",
            "07c81e69f6200966",
            "75436d7bf77269e8",
            "841eff6b0fc1e168",
            "31e028c6c36eb076",
            "455b00e73f513ec1",
            "af840e72c58dfbef",
            "56187fe463fe6fa8",
            "4a263aed90d635ad",
            "6cb326e213c84cdc",
            "c297280220561e87",
            "c67fdfd76351c0a7",
            "ccf58bb55dffd4d6",
            "dbed7e2c2ceef819",
            "a382baa71e6f8e94",
            "d9e8798d4bb4b948",
            "ee37d7c298f576aa"
        };

        [Test]
        public void AllPolicies_MatchTheOutcomeRecordedBeforeP10003_FrameByFrame()
        {
            // P10-003 guard for the dispatch-framing optimizations: the observable outcome of every
            // frame (trace records, root result, running state, run count, error, delivered effects
            // and every byte of each agent's Tree/Agent blackboard storage) must equal what the
            // unoptimized runtime produced, under all three profiles.
            var trees = new[] { Prepare(SequenceTree), Prepare(ReactiveEffectTree), Prepare(LoopTree) };
            foreach (var mode in new[] { Mode.DefaultProfile, Mode.ForcedImmediate, Mode.ForcedBatchedJobs, Mode.ForcedAgentFrame })
            {
                var outcome = Run(trees, mode, withTreeValues: true);
                var digests = FrameDigests(outcome.Lines);
                Assert.That(digests.Count, Is.EqualTo(FrameCount), mode + ": frames observed");
                if (RecordedFrameDigests.Length == 0)
                    Assert.Fail("No recorded digests. Recorded now (" + mode + "):\n"
                        + string.Join(",\n", digests.Select(d => "            \"" + d + "\"")));
                for (var frame = 0; frame < FrameCount; frame++)
                    if (digests[frame] != RecordedFrameDigests[frame])
                        Assert.Fail(mode + ": frame " + frame + " differs from the recorded outcome.\n"
                            + string.Join("\n", outcome.Lines.Where(l => l.StartsWith("f" + frame + " ", StringComparison.Ordinal)).Take(40)));
            }
        }

        private static List<string> FrameDigests(List<string> lines)
        {
            var digests = new List<string>();
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var frame = 0;
                var text = new StringBuilder();
                void Flush()
                {
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                    digests.Add(BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant());
                    text.Clear();
                }
                foreach (var line in lines)
                {
                    var space = line.IndexOf(' ');
                    var lineFrame = int.Parse(line.Substring(1, space - 1), System.Globalization.CultureInfo.InvariantCulture);
                    while (lineFrame > frame) { Flush(); frame++; }
                    text.Append(line).Append('\n');
                }
                Flush();
            }
            return digests;
        }

        [Test]
        public void WarmedUpImmediateFrames_DoNotAllocateManagedMemory()
        {
            // P10-003: the same population as the BatchedJobsSameFrame allocation test below, driven
            // through NativeTreeAgent under a forced Immediate profile (single-host dispatch path).
            var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            recorder.FilterToCurrentThread();
            object kept = null;
            Assert.That(CountAllocations(recorder, () => kept = new byte[64], 10), Is.EqualTo(10), "The recorder must see allocations.");
            Assert.That(kept, Is.Not.Null);

            var trees = new[] { Prepare(LoopTree), Prepare(ReactiveEffectTree) };
            var worldObject = Track(new GameObject("AIBT.Tests.P10003.AllocationWorld"));
            var scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            var world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(world, "Awake");
            InvokePrivate(world, "OnEnable");
            var profile = Forced(SchedulingPolicy.Immediate);
            var update = (Action)Delegate.CreateDelegate(typeof(Action), scheduler,
                typeof(ProductionTreeScheduler).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic));

            var agents = new List<NativeTreeAgent>();
            var effects = 0;
            try
            {
                for (var index = 0; index < 32; index++)
                {
                    var agent = Track(new GameObject("AIBT.Tests.P10003.AllocationAgent" + index)).AddComponent<NativeTreeAgent>();
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
                Assert.That(scheduler.TryGetFrameEntry(0, out var entry) && entry.SelectedPolicy == SchedulingPolicy.Immediate, Is.True,
                    "The frame ran under Immediate.");
                Assert.That(allocations, Is.Zero,
                    allocations + " managed allocation(s) in 200 warmed-up Immediate frames; the tolerance is zero.");
            }
            finally
            {
                foreach (var agent in agents) if (agent != null) agent.Deactivate();
            }
        }

        [Test]
        public void WarmedUpBatchedJobsFrames_DoNotAllocateManagedMemory()
        {
            // P10-002: a BatchedJobsSameFrame frame of trees that do not restart (a looping parallel
            // and a reactive effect emitter) allocates nothing once warmed up: round lists, machine
            // array, lane storage and dispatch scratch are reused. Allocations are counted with the
            // profiler's GC.Alloc recorder on this thread; the tolerance is zero.
            var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            recorder.FilterToCurrentThread();
            object kept = null;
            Assert.That(CountAllocations(recorder, () => kept = new byte[64], 10), Is.EqualTo(10), "The recorder must see allocations.");
            Assert.That(kept, Is.Not.Null);

            var trees = new[] { Prepare(LoopTree), Prepare(ReactiveEffectTree) };
            var worldObject = Track(new GameObject("AIBT.Tests.P10002.AllocationWorld"));
            var scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            var world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(world, "Awake");
            InvokePrivate(world, "OnEnable");
            Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, false, out var capabilities, out var error),
                Is.True, error.ToString());
            scheduler.SetJobsCapabilities(capabilities);
            var profile = Forced(SchedulingPolicy.BatchedJobsSameFrame);
            var update = (Action)Delegate.CreateDelegate(typeof(Action), scheduler,
                typeof(ProductionTreeScheduler).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic));

            var agents = new List<NativeTreeAgent>();
            var effects = 0;
            try
            {
                for (var index = 0; index < 32; index++)
                {
                    var agent = Track(new GameObject("AIBT.Tests.P10002.AllocationAgent" + index)).AddComponent<NativeTreeAgent>();
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
                Assert.That(scheduler.FrameEntryCount, Is.EqualTo(agents.Count));
                Assert.That(scheduler.TryGetFrameEntry(0, out var entry) && entry.SelectedPolicy == SchedulingPolicy.BatchedJobsSameFrame, Is.True,
                    "The frame ran under BatchedJobsSameFrame.");
                Assert.That(allocations, Is.Zero,
                    allocations + " managed allocation(s) in 200 warmed-up BatchedJobsSameFrame frames; the tolerance is zero.");
            }
            finally
            {
                foreach (var agent in agents) if (agent != null) agent.Deactivate();
            }
        }

        [Test]
        public void WarmedUpJobsAgentFrameFrames_DoNotAllocateManagedMemory()
        {
            // P10-004: the same population under JobsAgentFrame. The driver's agent table, each host's
            // trace and effect lists and the job storage are kept across frames. Zero tolerance.
            var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            recorder.FilterToCurrentThread();
            object kept = null;
            Assert.That(CountAllocations(recorder, () => kept = new byte[64], 10), Is.EqualTo(10), "The recorder must see allocations.");
            Assert.That(kept, Is.Not.Null);

            var trees = new[] { Prepare(LoopTree), Prepare(ReactiveEffectTree) };
            var worldObject = Track(new GameObject("AIBT.Tests.P10004.AllocationWorld"));
            var scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            var world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(world, "Awake");
            InvokePrivate(world, "OnEnable");
            // P10-007: 32 members would run in place by default; this test covers the job.
            scheduler.AgentFrameMinimumBatchSize = 1;
            var update = (Action)Delegate.CreateDelegate(typeof(Action), scheduler,
                typeof(ProductionTreeScheduler).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic));

            var agents = new List<NativeTreeAgent>();
            var effects = 0;
            try
            {
                for (var index = 0; index < 32; index++)
                {
                    var agent = Track(new GameObject("AIBT.Tests.P10004.AllocationAgent" + index)).AddComponent<NativeTreeAgent>();
                    agent.Tree = trees[index % trees.Length];
                    agent.World = world;
                    agent.RestartMode = NativeTreeRestartMode.Never;
                    agent.Profile = SchedulingProfile.JobsAgentFrame;
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
                Assert.That(scheduler.FrameEntryCount, Is.EqualTo(agents.Count));
                for (var index = 0; index < scheduler.FrameEntryCount; index++)
                {
                    Assert.That(scheduler.TryGetFrameEntry(index, out var entry), Is.True);
                    Assert.That(entry.SelectedPolicy, Is.EqualTo(SchedulingPolicy.JobsAgentFrame), "The frame ran in the agent-frame job.");
                    Assert.That(entry.AgentFrameFallback, Is.EqualTo(AgentFrameFallbackReason.None));
                }
                Assert.That(allocations, Is.Zero,
                    allocations + " managed allocation(s) in 200 warmed-up JobsAgentFrame frames; the tolerance is zero.");
            }
            finally
            {
                foreach (var agent in agents) if (agent != null) agent.Deactivate();
            }
        }

        [Test]
        public void WarmedUpAutomaticJobsAgentFrameFrames_DoNotAllocateManagedMemory()
        {
            // P10-007: 70 default-profile agents of one tree run as JobsAgentFrame automatically; the
            // run search and the batch allocate nothing once warmed up. Zero tolerance.
            var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            recorder.FilterToCurrentThread();
            object kept = null;
            Assert.That(CountAllocations(recorder, () => kept = new byte[64], 10), Is.EqualTo(10), "The recorder must see allocations.");
            Assert.That(kept, Is.Not.Null);

            var tree = Prepare(ReactiveEffectTree);
            var worldObject = Track(new GameObject("AIBT.Tests.P10007.AllocationWorld"));
            var scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            var world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(world, "Awake");
            InvokePrivate(world, "OnEnable");
            var update = (Action)Delegate.CreateDelegate(typeof(Action), scheduler,
                typeof(ProductionTreeScheduler).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic));

            var agents = new List<NativeTreeAgent>();
            var effects = 0;
            try
            {
                for (var index = 0; index < 70; index++)
                {
                    var agent = Track(new GameObject("AIBT.Tests.P10007.AllocationAgent" + index)).AddComponent<NativeTreeAgent>();
                    agent.Tree = tree;
                    agent.World = world;
                    agent.RestartMode = NativeTreeRestartMode.Never;
                    agent.OnEffect("hit", (int value) => effects++);
                    agent.Activate();
                    agent.SetFact("live-target", 1f);
                    agent.SetFact("armed", true);
                    agents.Add(agent);
                }
                for (var frame = 0; frame < 60; frame++) update();
                var effectsBefore = effects;

                var allocations = CountAllocations(recorder, update, 200);

                Assert.That(effects - effectsBefore, Is.EqualTo(200 * 70), "Every emitter delivers one effect per frame while measured.");
                Assert.That(scheduler.FrameEntryCount, Is.EqualTo(agents.Count));
                for (var index = 0; index < scheduler.FrameEntryCount; index++)
                {
                    Assert.That(scheduler.TryGetFrameEntry(index, out var entry), Is.True);
                    Assert.That(entry.SelectedPolicy, Is.EqualTo(SchedulingPolicy.JobsAgentFrame));
                    Assert.That(entry.SelectionReason, Is.EqualTo(SchedulerSelectionReason.AutomaticAgentFrameRun));
                    Assert.That(entry.AgentFrameRanInPlace, Is.False);
                }
                Assert.That(allocations, Is.Zero,
                    allocations + " managed allocation(s) in 200 warmed-up automatic JobsAgentFrame frames; the tolerance is zero.");
            }
            finally
            {
                foreach (var agent in agents) if (agent != null) agent.Deactivate();
            }
        }

        private static int CountAllocations(UnityEngine.Profiling.Recorder recorder, Action action, int calls)
        {
            recorder.enabled = false;
            recorder.enabled = true;
            for (var index = 0; index < calls; index++) action();
            recorder.enabled = false;
            return recorder.sampleBlockCount;
        }

        private enum Mode { DefaultProfile, ForcedImmediate, ForcedBatchedJobs, ForcedAgentFrame }

        private sealed class Outcome
        {
            internal readonly List<string> Lines = new List<string>();
            internal int Effects;
            internal int Restarts;
            internal int AgentFrameEntries;
            internal int FallbackEntries;
            // P10-007: entries whose JobsAgentFrame batch ran on the main thread without a job,
            // the job batches the run scheduled, and the last frame's entries.
            internal int InPlaceEntries;
            internal int AutomaticEntries;
            internal int DeferredShareEntries;
            internal long JobBatches;
            internal readonly List<SchedulerFrameEntry> LastFrameEntries = new List<SchedulerFrameEntry>();
        }

        private static void AssertSame(Outcome expected, Outcome actual, string what)
        {
            var count = Math.Min(expected.Lines.Count, actual.Lines.Count);
            for (var index = 0; index < count; index++)
            {
                if (expected.Lines[index] != actual.Lines[index])
                    Assert.Fail(what + ": first difference at line " + index + "\n expected: " + expected.Lines[index]
                        + "\n   actual: " + actual.Lines[index] + "\n" + Context(expected.Lines, index));
            }
            Assert.That(actual.Lines.Count, Is.EqualTo(expected.Lines.Count), what + ": number of observations");
        }

        private static string Context(List<string> lines, int index)
        {
            var text = new StringBuilder("preceding expected lines:\n");
            for (var line = Math.Max(0, index - 6); line < index; line++) text.Append("  ").Append(lines[line]).Append('\n');
            return text.ToString();
        }

        private Outcome Run(ProductionTreeAsset[] trees, Mode mode, bool withTreeValues = false,
            Action<List<NativeTreeAgent>> prepare = null, AgentFrameFallbackReason allowedFallback = AgentFrameFallbackReason.None,
            int agentFrameMinimumBatch = 1, int agentCount = 0, NativeTreeRestartMode? restartMode = null,
            Action<ProductionTreeScheduler> configure = null, bool groupedTrees = false,
            SchedulingProfile profileOverride = null)
        {
            var outcome = new Outcome();
            var worldObject = Track(new GameObject("AIBT.Tests.P10002.World." + mode));
            var scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            var world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(world, "Awake");
            InvokePrivate(world, "OnEnable");
            SchedulingProfile profile = null;
            if (mode == Mode.ForcedImmediate) profile = Forced(SchedulingPolicy.Immediate);
            if (mode == Mode.ForcedBatchedJobs)
            {
                Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, false, out var capabilities, out var error),
                    Is.True, error.ToString());
                scheduler.SetJobsCapabilities(capabilities);
                profile = Forced(SchedulingPolicy.BatchedJobsSameFrame);
            }
            if (mode == Mode.ForcedAgentFrame)
            {
                profile = SchedulingProfile.JobsAgentFrame;
                // P10-007: 12 members would run in place by default; most tests here cover the job.
                scheduler.AgentFrameMinimumBatchSize = agentFrameMinimumBatch;
            }
            if (profileOverride != null) profile = profileOverride;
            configure?.Invoke(scheduler);

            var agents = new List<NativeTreeAgent>();
            var frame = 0;
            var population = agentCount > 0 ? agentCount : trees.Length * AgentsPerTree;
            // P10-007: grouped puts each tree's agents next to each other in registration (and due)
            // order, so automatic selection can find runs on one catalog; otherwise trees alternate.
            int TreeOf(int index) => groupedTrees ? index * trees.Length / population : index % trees.Length;
            for (var index = 0; index < population; index++)
            {
                var gameObject = Track(new GameObject("AIBT.Tests.P10002.Agent" + index));
                var agent = gameObject.AddComponent<NativeTreeAgent>();
                agent.Tree = trees[TreeOf(index)];
                agent.World = world;
                agent.RestartMode = restartMode
                    ?? (TreeOf(index) == 2 ? NativeTreeRestartMode.Never : NativeTreeRestartMode.Always);
                if (profile != null) agent.Profile = profile;
                var label = index;
                agent.OnEffect("hit", (int value) =>
                {
                    outcome.Effects++;
                    outcome.Lines.Add("f" + frame + " effect a" + label + " " + value);
                });
                agent.Activate();
                agents.Add(agent);
            }

            prepare?.Invoke(agents);
            var batchesBefore = AgentFrameDiagnosticsV1.Batches;

            try
            {
                for (frame = 0; frame < FrameCount; frame++)
                {
                    for (var index = 0; index < agents.Count; index++)
                    {
                        // Facts change between frames, at a different phase per agent.
                        agents[index].SetFact("live-target", (frame + index) % 7 < 5 ? 1f : 0f);
                        if (TreeOf(index) == 1) agents[index].SetFact("armed", (frame / (3 + index % 4)) % 3 != 2);
                    }
                    InvokePrivate(scheduler, "Update");
                    outcome.LastFrameEntries.Clear();
                    for (var entryIndex = 0; entryIndex < scheduler.FrameEntryCount; entryIndex++)
                    {
                        Assert.That(scheduler.TryGetFrameEntry(entryIndex, out var entry), Is.True);
                        outcome.LastFrameEntries.Add(entry);
                        if (entry.AgentFrameRanInPlace)
                        {
                            Assert.That(entry.SelectedPolicy, Is.EqualTo(SchedulingPolicy.JobsAgentFrame), "frame " + frame + ": in-place policy");
                            Assert.That(entry.AgentFrameFallback, Is.EqualTo(AgentFrameFallbackReason.None), "frame " + frame + ": in-place fallback");
                            outcome.InPlaceEntries++;
                        }
                        if (entry.SelectionReason == SchedulerSelectionReason.AutomaticAgentFrameRun)
                        {
                            Assert.That(entry.SelectedPolicy, Is.EqualTo(SchedulingPolicy.JobsAgentFrame), "frame " + frame + ": automatic policy");
                            outcome.AutomaticEntries++;
                        }
                        if (entry.Disposition == SchedulerFrameDisposition.DeferredProfileShare) outcome.DeferredShareEntries++;
                    }
                    if (mode == Mode.ForcedAgentFrame)
                    {
                        for (var entryIndex = 0; entryIndex < scheduler.FrameEntryCount; entryIndex++)
                        {
                            Assert.That(scheduler.TryGetFrameEntry(entryIndex, out var entry), Is.True);
                            if (entry.AgentFrameFallback != AgentFrameFallbackReason.None)
                            {
                                Assert.That(entry.AgentFrameFallback, Is.EqualTo(allowedFallback), "frame " + frame + ": fallback reason");
                                Assert.That(entry.SelectedPolicy, Is.EqualTo(
                                    allowedFallback == AgentFrameFallbackReason.StepBudgetConfigured
                                        ? SchedulingPolicy.Budgeted : SchedulingPolicy.Immediate), "frame " + frame + ": fallback policy");
                                outcome.FallbackEntries++;
                                continue;
                            }
                            Assert.That(entry.SelectedPolicy, Is.EqualTo(SchedulingPolicy.JobsAgentFrame), "frame " + frame);
                            if (!entry.AgentFrameRanInPlace) outcome.AgentFrameEntries++;
                        }
                    }
                    for (var index = 0; index < agents.Count; index++)
                    {
                        var agent = agents[index];
                        var prefix = "f" + frame + " a" + index;
                        CaptureTrace(agent.Host, prefix, outcome.Lines);
                        if (withTreeValues) CaptureTreeValues(agent.Host, prefix, outcome.Lines);
                        outcome.Lines.Add(prefix + " result=" + (agent.LastResult?.ToString() ?? "none") + " running=" + agent.IsRunning
                            + " runs=" + agent.CompletedRuns + " error=" + (agent.LastError ?? "none"));
                        var runs = agent.CompletedRuns;
                        agent.ProcessRootResult();
                        if (agent.CompletedRuns != runs && agent.RestartMode == NativeTreeRestartMode.Always) outcome.Restarts++;
                    }
                }
                Assert.That(world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None), mode.ToString());
                outcome.JobBatches = AgentFrameDiagnosticsV1.Batches - batchesBefore;
            }
            finally
            {
                foreach (var agent in agents) if (agent != null) agent.Deactivate();
            }
            return outcome;
        }

        private static void CaptureTrace(ProductionTreeHost host, string prefix, List<string> lines)
        {
            if (host == null || host.TraceChannelOwner == null) { lines.Add(prefix + " no-host"); return; }
            var owner = host.TraceChannelOwner;
            Assert.That(owner.TryGetSnapshot(out var snapshot, out var failure), Is.True, failure.ToString());
            for (var index = 0; index < snapshot.RecordCount; index++)
            {
                var r = snapshot.Records[index];
                // Tree instance ids differ between runs (allocated per host); everything else is compared.
                lines.Add(prefix + " " + r.Kind + " u" + r.UpdateId + " s" + r.Sequence + " n" + r.RuntimeNodeIndex
                    + " o" + (int)r.OptionalFields + " st" + r.Status + " x" + r.ExitReason + " ab" + r.AbortReason
                    + " ph" + r.Phase + " w" + r.WorkerOrdinal);
            }
            Assert.That(snapshot.DroppedCount, Is.Zero, "The trace channel must hold one frame.");
            Assert.That(owner.TryReset(out failure), Is.True, failure.ToString());
        }

        private static void CaptureTreeValues(ProductionTreeHost host, string prefix, List<string> lines)
        {
            var adapter = host != null ? host.GeneratedDispatchAdapter : null;
            if (adapter == null) { lines.Add(prefix + " values none"); return; }
            var text = new StringBuilder(prefix).Append(" values ");
            for (uint offset = 0; adapter.TryReadTreeValueByte(offset, out var value); offset++)
                text.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            lines.Add(text.ToString());
        }

        private static SchedulingProfile Forced(SchedulingPolicy policy)
        {
            Assert.That(SchedulingProfile.TryCreate("tests.p10002." + policy, 0, 1u, null, null, false, policy,
                out var profile, out var error), Is.True, error.ToString());
            return profile;
        }

        private ProductionTreeAsset Prepare(string json)
        {
            var standard = Materialize(typeof(StandardNativeShard));
            var nodes = Materialize(typeof(BuiltInLeafShard)).Nodes
                .Concat(standard.Nodes)
                .Concat(Materialize(typeof(ExternalTreeWriteFixtureShard)).Nodes)
                .ToArray();
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P10002/tree.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, standard.RegisteredTypes,
                new ReferenceCompilerOptions("Tests/P10002/tree.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var asset = ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(standard.RegisteredTypes),
                JobsEquivalenceCatalog.Fingerprint);
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(JobsEquivalenceCatalog).FullName + ", " + typeof(JobsEquivalenceCatalog).Assembly.GetName().Name);
            return Track(asset);
        }

        private static GeneratedShardMetadataArtifact Materialize(Type shard)
        {
            var metadata = shard.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public);
            object Field(string name) => metadata.GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return GeneratedShardMetadataMaterializer.MaterializeArtifact(
                (string)Field("ShardId"), (uint)Field("ShardVersion"), (string)Field("CanonicalDescriptorJson"),
                (string)Field("DescriptorHash"), (string)Field("ManifestRegistryJson"), (string)Field("NodeRegistryHash"));
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
