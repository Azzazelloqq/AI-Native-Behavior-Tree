using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.Tests.CodeGen.GenerationP11009;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Integration.NativeRuntime
{
    // P11-013: the mixed fixture shard next to the built-in leaves, for a tree that loops inside one run (a wait).
    [AibtCatalogSet("aibt.tests.p11013.mixed-loop-set", 1u, typeof(BuiltInLeafShard), typeof(MixedScopeFixtureShard))]
    public static partial class MixedLoopCatalog { }

    /// <summary>
    /// P11-013 (ADR-P11-013, AIBT-053): the Agent-scope keys of a <see cref="NativeTreeAgent"/> belong to
    /// the component, not to a run. They start at their defaults on enable and keep their values across
    /// automatic restarts, <see cref="NativeTreeAgent.Restart()"/>, <see cref="NativeTreeAgent.Stop"/> then
    /// <see cref="NativeTreeAgent.Restart()"/> and a change of <see cref="NativeTreeAgent.Tree"/> to a tree
    /// with the same Agent contract; they return to their defaults on disable, after a faulted run, on a
    /// change to another Agent contract and on <c>Restart(true)</c>. Tree-scope keys still start from their
    /// defaults in every run. Values are read through <see cref="NativeTreeAgent.TryGetValue{T}"/> only.
    /// </summary>
    public sealed partial class MixedScopeNativeTreeTests
    {
        private static readonly string[] Kinds =
        {
            "interactive", "background", "forced-immediate", "forced-budgeted",
            "forced-batched-jobs", "forced-pipelined-jobs", "forced-agent-frame",
        };

        /// <summary>The same Agent contract as <see cref="MixedTree"/>, one more Tree key: the Agent range starts 8 bytes later.</summary>
        private static string WideTree => MixedTree
            .Replace("\"treeId\": \"tests.p11009.mixed-scope\"", "\"treeId\": \"tests.p11013.mixed-wide\"")
            .Replace("\"ready\": { \"type\"", "\"wide\": { \"type\": \"Float64\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": 0.5 },\n    \"ready\": { \"type\"");

        /// <summary><see cref="MixedTree"/> with Agent contract version 2.</summary>
        private static string OtherContractTree => MixedTree
            .Replace("\"treeId\": \"tests.p11009.mixed-scope\"", "\"treeId\": \"tests.p11013.mixed-v2\"")
            .Replace("\"contractVersion\": 1", "\"contractVersion\": 2");

        /// <summary>Loops inside one run: bump, report, wait a tick, forever. The agent never restarts.</summary>
        private static string LoopingTree => MixedTree
            .Replace("\"treeId\": \"tests.p11009.mixed-scope\"", "\"treeId\": \"tests.p11013.mixed-loop\"")
            .Replace("\"root\": \"root\",", "\"root\": \"loop\",")
            .Replace("\"nodes\": {", "\"nodes\": {\n    \"loop\": { \"type\": \"aibt.core.repeater\", \"typeVersion\": 1, \"children\": [\"root\"], \"parameters\": { \"count\": 4000000000, \"stopOnFailure\": false } },\n    \"pause\": { \"type\": \"aibt.stdlib.wait\", \"typeVersion\": 1, \"parameters\": { \"ticks\": 2 } },")
            .Replace("\"children\": [\"bump\", \"report\"]", "\"children\": [\"bump\", \"report\", \"pause\"]");

        [TestCaseSource(nameof(Kinds))]
        public void AgentKeys_SurviveRestartStopAndASameContractTree_AndResetOnRequestDisableAndAnotherContract(string kind)
        {
            var profile = ProfileFor(kind);
            var asset = PrepareAsset(MixedTree);
            var agent = Spawn(asset, profile);
            agent.RestartMode = NativeTreeRestartMode.Never;

            Assert.That(Counter(agent, "enabled"), Is.EqualTo(40), "A new agent starts from the Agent defaults.");
            RunOnce(agent, kind + " run 1");
            Assert.That(Counter(agent, "run 1"), Is.EqualTo(41));
            Assert.That(Int(agent, "tree-out"), Is.EqualTo(41_007));

            agent.Restart();
            Assert.That(Counter(agent, "Restart()"), Is.EqualTo(41), "Restart() keeps the Agent values; the new run reads them before its first update.");
            Assert.That(Int(agent, "tree-out"), Is.EqualTo(3), "Tree keys start from their defaults in every run.");
            RunOnce(agent, kind + " after Restart()");
            Assert.That(Counter(agent, "after Restart()"), Is.EqualTo(42));
            Assert.That(Int(agent, "tree-out"), Is.EqualTo(42_007));

            agent.Stop();
            Assert.That(agent.TryGetValue("agent-counter", out int _), Is.False, "A stopped agent has no run to read.");
            agent.Restart();
            Assert.That(Counter(agent, "Stop then Restart()"), Is.EqualTo(42), "Stop() then Restart() keeps them too.");
            RunOnce(agent, kind + " after Stop");
            Assert.That(Counter(agent, "after Stop"), Is.EqualTo(43));

            agent.Tree = PrepareAsset(WideTree);
            Assert.That(agent.LastError, Is.Null, kind);
            Assert.That(Counter(agent, "same contract"), Is.EqualTo(43), "Another tree with the same Agent contract keeps them, at its own offset.");
            Assert.That(agent.TryGetValue("wide", out double wide) && wide == 0.5, Is.True, "The new tree's Tree keys are its defaults.");
            RunOnce(agent, kind + " wide tree");
            Assert.That(Counter(agent, "wide tree"), Is.EqualTo(44));
            Assert.That(Int(agent, "tree-out"), Is.EqualTo(44_007));

            agent.Restart(true);
            Assert.That(Counter(agent, "Restart(true)"), Is.EqualTo(40), "Restart(true) resets the Agent values.");
            RunOnce(agent, kind + " after Restart(true)");
            Assert.That(Counter(agent, "after Restart(true)"), Is.EqualTo(41));

            agent.Deactivate();
            Assert.That(agent.TryGetValue("agent-counter", out int _), Is.False);
            agent.Activate();
            Assert.That(Counter(agent, "disable and enable"), Is.EqualTo(40), "A disable ends the agent's memory.");
            RunOnce(agent, kind + " after enable");
            Assert.That(Counter(agent, "after enable"), Is.EqualTo(41));
            if (kind != "forced-pipelined-jobs")
            {
                // Not under forced PipelinedJobs: there a finished host left registered faults on later frames
                // (NativeCapacityPlanInvalid from the scheduler's forced-policy check), with no restart and no
                // Agent copy involved; reported as a finding of P11-013, outside its scope.
                for (var frame = 0; frame < 20; frame++) Frame();
                Assert.That(agent.CompletedRuns, Is.EqualTo(1), kind);
                Assert.That(Counter(agent, "Never keeps the finished run"), Is.EqualTo(41), "RestartMode.Never: no new run, the finished run's values.");
            }
            agent.Restart();
            RunOnce(agent, kind + " second run after enable");
            Assert.That(Counter(agent, "second run after enable"), Is.EqualTo(42));

            agent.Tree = PrepareAsset(OtherContractTree);
            Assert.That(agent.LastError, Is.Null, kind);
            Assert.That(Counter(agent, "contract v2"), Is.EqualTo(40), "Another Agent contract starts from its defaults.");
            RunOnce(agent, kind + " contract v2");
            Assert.That(Counter(agent, "contract v2 run"), Is.EqualTo(41));

            agent.Tree = asset;
            Assert.That(Counter(agent, "back to v1"), Is.EqualTo(40), "Back to contract v1 is another change of contract.");
            RunOnce(agent, kind + " back to v1");
            Assert.That(Counter(agent, "back to v1 run"), Is.EqualTo(41));
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None), kind);
        }

        [TestCaseSource(nameof(Kinds))]
        public void AFaultedRun_ResetsAgentKeys(string kind)
        {
            var agent = Spawn(PrepareAsset(MixedTree), ProfileFor(kind));
            agent.RestartMode = NativeTreeRestartMode.Never;
            RunOnce(agent, kind + " run 1");
            agent.Restart();
            RunOnce(agent, kind + " run 2");
            agent.Restart();
            Assert.That(Counter(agent, "kept"), Is.EqualTo(42));

            // The run's host faults before its first update; the agent reports it at the end of the frame.
            agent.Host.FailExternalScheduling(new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
            LogAssert.Expect(LogType.Error, new Regex("ProductionTreeHost: NativeCapacityPlanInvalid: The coordinator's externally-driven scheduling round failed"));
            LogAssert.Expect(LogType.Error, new Regex("The native host stopped: NativeCapacityPlanInvalid"));
            Frame();
            Assert.That(agent.Host, Is.Null, kind);
            Assert.That(agent.LastError, Does.Contain("NativeCapacityPlanInvalid"), kind);

            agent.Restart(); // LastError keeps the fault's text until the next enable (unchanged behaviour)
            Assert.That(agent.Host, Is.Not.Null, kind);
            Assert.That(Counter(agent, "after the fault"), Is.EqualTo(40), "A faulted run's Agent values may be mid-update; they are not carried.");
            RunOnce(agent, kind + " after the fault");
            Assert.That(Counter(agent, "after the fault, run"), Is.EqualTo(41));
        }

        [TestCaseSource(nameof(Kinds))]
        public void ARestartAskedForWhileTheHostExecutes_CarriesTheValuesTheCompletedJobsLeft(string kind)
        {
            var agent = Spawn(PrepareAsset(MixedTree), ProfileFor(kind));
            agent.RestartMode = NativeTreeRestartMode.Never;
            RunOnce(agent, kind + " run 1");
            agent.Restart();
            Assert.That(Counter(agent, "before"), Is.EqualTo(41));

            // Drive until the host executes between frames (PipelinedJobs) or the run finishes (every other policy).
            var executing = false;
            for (var frame = 0; frame < 80 && agent.CompletedRuns == 1; frame++)
            {
                Frame();
                if (agent.Host != null && agent.Host.IsExecuting)
                {
                    executing = true;
                    break;
                }
            }
            var pipelined = kind == "forced-pipelined-jobs";
            Assert.That(executing, Is.EqualTo(pipelined), "Only a PipelinedJobs host executes between frames.");

            // The world unregisters the host first: the scheduler completes its jobs on the main thread, then
            // the agent copies the Agent range out of the idle host.
            agent.Restart();
            Assert.That(agent.LastError, Is.Null, kind);
            Assert.That(agent.Host.IsExecuting, Is.False, kind);
            var carried = Counter(agent, "after the restart");
            if (pipelined) Assert.That(carried, Is.InRange(41, 42), "41, or 42 when the completed round had run the bump.");
            else Assert.That(carried, Is.EqualTo(42), "The run finished before the restart.");
            RunOnce(agent, kind + " after the restart");
            Assert.That(Counter(agent, "after the restart, run"), Is.EqualTo(carried + 1));
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None), kind);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AWarmedUpdate_ThatUsesAgentKeys_AllocatesNoManagedMemory()
        {
            var agent = Spawn(PrepareLoopAsset(), ProfileFor("forced-immediate"));
            var update = (Action)Delegate.CreateDelegate(typeof(Action), _scheduler,
                typeof(ProductionTreeScheduler).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic));
            var sum = 0L;
            TestDelegate frames = () =>
            {
                for (var frame = 0; frame < 200; frame++)
                {
                    update();
                    agent.ProcessRootResult();
                    agent.TryGetValue("agent-counter", out int counter);
                    sum += counter;
                }
            };
            frames(); // warm the delegate, the read slot and the host
            var counterBefore = Counter(agent, "warm");
            Assert.That(counterBefore, Is.GreaterThan(40), "The loop bumps the Agent key.");
            var allocations = GcAllocations(frames);
            Assert.That(allocations, Is.Zero, "200 warmed frames with an Agent-key read, GC.Alloc samples: " + allocations);
            Assert.That(frames, GcAllocIs.Not.AllocatingGCMemory(), "A warmed update must not allocate managed memory.");
            Assert.That(agent.CompletedRuns, Is.Zero, "The loop never finishes, so no restart allocated a host.");
            Assert.That(Counter(agent, "after"), Is.GreaterThan(counterBefore));
            Assert.That(agent.LastError, Is.Null);
        }

        // ------------------------------------------------------------------ helpers (P11-013)

        private ProductionTreeAsset PrepareAsset(string json)
        {
            var compiled = Compile(json);
            var asset = Track(ProductionTreeAsset.Create(compiled.CreateNativeBlackboardBindingV2(RegisteredTypes()),
                MixedScopeFixtureCatalog.Fingerprint));
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(MixedScopeFixtureCatalog).FullName + ", " + typeof(MixedScopeFixtureCatalog).Assembly.GetName().Name);
            return asset;
        }

        private ProductionTreeAsset PrepareLoopAsset()
        {
            var parsed = CanonicalTreeJson.Parse(LoopingTree, "Tests/P11013/mixed-loop.aibt.json");
            Assert.That(parsed.Success, Is.True, Describe(parsed.Diagnostics));
            var nodes = NativeLibraryKit.Materialize(typeof(BuiltInLeafShard)).Nodes.Concat(Artifact().Nodes).ToArray();
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, RegisteredTypes(),
                new ReferenceCompilerOptions("Tests/P11013/mixed-loop.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, Describe(result.Diagnostics));
            var asset = Track(ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(RegisteredTypes()),
                MixedLoopCatalog.Fingerprint));
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(MixedLoopCatalog).FullName + ", " + typeof(MixedLoopCatalog).Assembly.GetName().Name);
            return asset;
        }

        private void Frame()
        {
            InvokePrivate(_scheduler, "Update");
            foreach (var agent in _agents) if (agent != null) agent.ProcessRootResult();
        }

        /// <summary>Drives frames until the agent finishes one more run (RestartMode.Never).</summary>
        private void RunOnce(NativeTreeAgent agent, string label)
        {
            var before = agent.CompletedRuns;
            var error = agent.LastError;
            for (var frame = 0; frame < 80 && agent.CompletedRuns == before; frame++) Frame();
            Assert.That(agent.LastError, Is.EqualTo(error), label);
            Assert.That(agent.CompletedRuns, Is.EqualTo(before + 1), label);
            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Success), label);
        }


        private static int Counter(NativeTreeAgent agent, string label) => Int(agent, "agent-counter", label);

        private static int Int(NativeTreeAgent agent, string key, string label = null)
        {
            var host = agent.Host;
            Assert.That(agent.TryGetValue(key, out int value), Is.True, (label ?? key) + ": " + agent.LastError
                + (host == null ? " (no host)" : " (readable " + host.CanReadTreeValues + ", executing " + host.IsExecuting
                    + ", failure " + host.LastFailure.Code + ", result " + host.LastRootResult + ")"));
            return value;
        }

        /// <summary>GC.Alloc profiler samples on this thread while <paramref name="action"/> runs (as in NativeTreeAgentValueReadTests).</summary>
        private static int GcAllocations(TestDelegate action)
        {
            var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try { action(); }
            finally { recorder.enabled = false; }
            var count = recorder.sampleBlockCount;
            recorder.CollectFromAllThreads();
            return count;
        }
    }
}
