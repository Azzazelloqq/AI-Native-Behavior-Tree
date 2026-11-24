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
            for (var frame = 0; frame < 20; frame++) Frame();
            Assert.That(agent.CompletedRuns, Is.EqualTo(1), kind);
            Assert.That(Counter(agent, "Never keeps the finished run"), Is.EqualTo(41), "RestartMode.Never: no new run, the finished run's values.");
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

        /// <summary>
        /// P11-014 item 10: a host whose run finished stays registered (RestartMode.Never). Under a profile
        /// that forces PipelinedJobs the scheduler found no eligible member in the group and failed it with
        /// NativeCapacityPlanInvalid one frame later: an error in the log, TryGetValue false, and the next
        /// Restart() reset the Agent memory. A group with nothing to run is not a failure.
        /// </summary>
        [TestCaseSource(nameof(Kinds))]
        public void AFinishedHostThatStaysRegistered_IsNotFailed_StaysReadable_AndKeepsTheAgentMemory(string kind)
        {
            var agent = Spawn(PrepareAsset(MixedTree), ProfileFor(kind));
            agent.RestartMode = NativeTreeRestartMode.Never;
            RunOnce(agent, kind + " run 1");
            var host = agent.Host;
            var failure = NativeRuntimeDiagnosticCodeV1.None;
            var failedEntries = 0;
            var entries = 0;
            for (var frame = 0; frame < 30; frame++)
            {
                Frame();
                if (failure == NativeRuntimeDiagnosticCodeV1.None) failure = host.LastFailure.Code;
                for (var index = 0; index < _scheduler.FrameEntryCount; index++)
                {
                    if (!_scheduler.TryGetFrameEntry(index, out var entry) || entry.HostInstanceId != host.InstanceId) continue;
                    entries++;
                    if (entry.Disposition == SchedulerFrameDisposition.Failed) failedEntries++;
                }
            }
            var readable = agent.TryGetValue("agent-counter", out int finished);
            var hostKept = ReferenceEquals(agent.Host, host);
            var lastError = agent.LastError;
            agent.Restart();
            var readableAfterRestart = agent.TryGetValue("agent-counter", out int afterRestart);
            var wrong = new System.Collections.Generic.List<string>();
            if (failure != NativeRuntimeDiagnosticCodeV1.None) wrong.Add("the finished host failed with " + failure);
            if (failedEntries != 0) wrong.Add(failedEntries + " of " + entries + " frame entries are Failed");
            if (!hostKept) wrong.Add("the agent replaced the finished host");
            if (lastError != null) wrong.Add("LastError " + lastError);
            if (!readable || finished != 41) wrong.Add("TryGetValue after the run gave " + readable + " (" + finished + ")");
            if (!readableAfterRestart || afterRestart != 41)
                wrong.Add("the Agent memory after Restart() is " + readableAfterRestart + " (" + afterRestart + "), not 41");
            Assert.That(wrong, Is.Empty, kind + ": LastError " + (lastError ?? "null") + "; " + string.Join("; ", wrong));
            RunOnce(agent, kind + " run 2");
            Assert.That(Counter(agent, "run 2"), Is.EqualTo(42));
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None), kind);
            LogAssert.NoUnexpectedReceived();
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

        // ------------------------------------------------------------------ P11-014 item 11

        /// <summary><see cref="MixedTree"/> with one more Agent key of type <paramref name="type"/>, same Agent contract id and version.</summary>
        private static string SpareAgentKeyTree(string type, string value) => MixedTree
            .Replace("\"treeId\": \"tests.p11009.mixed-scope\"", "\"treeId\": \"tests.p11014.spare-" + type.ToLowerInvariant() + "\"")
            .Replace("\"ready\": { \"type\"", "\"agent-spare\": { \"type\": \"" + type + "\", \"typeVersion\": 1, \"scope\": \"agent\", \"default\": " + value + " },\n    \"ready\": { \"type\"");

        /// <summary>Only Agent keys: the Agent range starts at offset 0 of the value buffer.</summary>
        private const string AgentOnlyTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11014.agent-only"", ""name"": ""P11-014 agent only"",
  ""root"": ""root"",
  ""blackboardContracts"": { ""agent"": { ""contractId"": ""tests.p11014.agent-only"", ""contractVersion"": 1 } },
  ""blackboard"": {
    ""agent-counter"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""agent"", ""default"": 40 }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""bump""] },
    ""bump"": { ""type"": ""aibt.tests.agent-bump"", ""typeVersion"": 1, ""bindings"": { ""agent-counter"": ""agent-counter"" } }
  }
}";

        /// <summary>An Agent contract of keys of 1, 4, 8 and 4 bytes; two of them count up, the others keep their defaults.</summary>
        private const string SizedAgentTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11014.agent-sizes"", ""name"": ""P11-014 agent sizes"",
  ""root"": ""root"",
  ""blackboardContracts"": { ""agent"": { ""contractId"": ""tests.p11014.agent-sizes"", ""contractVersion"": 1 } },
  ""blackboard"": {
    ""a-flag"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""agent"", ""default"": true },
    ""agent-counter"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""agent"", ""default"": 40 },
    ""b-wide"": { ""type"": ""Float64"", ""typeVersion"": 1, ""scope"": ""agent"", ""default"": 2.5 },
    ""c-second"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""agent"", ""default"": 100 },
    ""tree-flag"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": true }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""bump"", ""bump-second""] },
    ""bump"": { ""type"": ""aibt.tests.agent-bump"", ""typeVersion"": 1, ""bindings"": { ""agent-counter"": ""agent-counter"" } },
    ""bump-second"": { ""type"": ""aibt.tests.agent-bump"", ""typeVersion"": 1, ""bindings"": { ""agent-counter"": ""c-second"" } }
  }
}";

        /// <summary>
        /// 11a (review S2): a tree with the same Agent contract id and version but another Agent layout starts
        /// from the defaults. The Float32 case has the Int32 case's range size and slot count, so only the
        /// schema and layout hashes tell the two apart.
        /// </summary>
        [TestCaseSource(nameof(Kinds))]
        public void TheSameContractIdAndVersion_WithAnotherLayout_StartsFromTheDefaults(string kind)
        {
            var agent = Spawn(PrepareAsset(MixedTree), ProfileFor(kind));
            agent.RestartMode = NativeTreeRestartMode.Never;
            RunOnce(agent, kind + " run 1");
            Assert.That(Counter(agent, "run 1"), Is.EqualTo(41));

            agent.Tree = PrepareAsset(SpareAgentKeyTree("Int32", "7"));
            Assert.That(agent.LastError, Is.Null, kind);
            Assert.That(Counter(agent, "an extra Agent key"), Is.EqualTo(40), "An extra Agent key is another layout: defaults.");
            RunOnce(agent, kind + " extra key");
            Assert.That(Counter(agent, "extra key, run"), Is.EqualTo(41));
            agent.Restart();
            Assert.That(Counter(agent, "extra key, restart"), Is.EqualTo(41), "The same layout again keeps them.");
            RunOnce(agent, kind + " extra key, run 2");
            Assert.That(Counter(agent, "extra key, run 2"), Is.EqualTo(42));

            agent.Tree = PrepareAsset(SpareAgentKeyTree("Float32", "7.0"));
            Assert.That(agent.LastError, Is.Null, kind);
            Assert.That(Counter(agent, "another type"), Is.EqualTo(40),
                "The same range size and slot count with another key type is another layout: defaults.");
            Assert.That(agent.TryGetValue("agent-spare", out float spare) && spare == 7f, Is.True, kind + ": the Float32 default");
            RunOnce(agent, kind + " another type");
            Assert.That(Counter(agent, "another type, run"), Is.EqualTo(41));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>11b: which execution path each scheduling kind of these tests takes (frame entries of one run).</summary>
        [TestCaseSource(nameof(Kinds))]
        public void EachKind_TakesItsExecutionPath(string kind)
        {
            var agent = Spawn(PrepareAsset(MixedTree), ProfileFor(kind));
            agent.RestartMode = NativeTreeRestartMode.Never;
            var paths = new System.Collections.Generic.SortedSet<string>();
            var host = agent.Host;
            for (var frame = 0; frame < 80 && agent.CompletedRuns == 0; frame++)
            {
                Frame();
                for (var index = 0; index < _scheduler.FrameEntryCount; index++)
                {
                    if (!_scheduler.TryGetFrameEntry(index, out var entry) || entry.HostInstanceId != host.InstanceId) continue;
                    paths.Add(entry.SelectedPolicy + (entry.SelectedPolicy == SchedulingPolicy.JobsAgentFrame
                        ? (entry.AgentFrameRanInPlace ? " in place" : " job") : string.Empty));
                }
            }
            Assert.That(agent.CompletedRuns, Is.EqualTo(1), kind);
            Assert.That(string.Join(", ", paths), Is.EqualTo(ExpectedPath(kind)), kind);
        }

        private static string ExpectedPath(string kind)
        {
            switch (kind)
            {
                case "interactive":
                case "background": // one small agent: the automatic selection picks Immediate
                case "forced-immediate": return "Immediate";
                case "forced-budgeted": return "Budgeted";
                case "forced-batched-jobs": return "BatchedJobsSameFrame";
                case "forced-pipelined-jobs": return "PipelinedJobs";
                case "forced-agent-frame": return "JobsAgentFrame job";
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>
        /// 11c (review N11, N14): a start whose bootstrap fails consumes no run index and leaves the Agent
        /// memory as the stopped run left it. The bootstrap is made to fail with a program whose catalog was
        /// disposed.
        /// </summary>
        [Test]
        public void ABootstrapFailure_ConsumesNoRunIndex_AndLeavesTheAgentMemory()
        {
            var asset = PrepareAsset(MixedTree);
            var agent = Spawn(asset, ProfileFor("interactive"));
            agent.RestartMode = NativeTreeRestartMode.Never;
            RunOnce(agent, "run 0");
            agent.Restart();
            RunOnce(agent, "run 1");
            Assert.That(agent.RunIndex, Is.EqualTo(1UL));
            Assert.That(Counter(agent, "run 1"), Is.EqualTo(42));

            Assert.That(asset.TryCreateProgram(MixedScopeFixtureCatalog.TryCreateRuntimeCatalog, out var disposed, out var createFailure),
                Is.True, createFailure.ToString());
            disposed.Dispose();
            var programField = typeof(NativeTreeAgent).GetField("_program", BindingFlags.Instance | BindingFlags.NonPublic);
            var program = programField.GetValue(agent);
            programField.SetValue(agent, disposed);
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("could not start"));
                agent.Restart();
            }
            finally
            {
                programField.SetValue(agent, program);
            }
            Assert.That(agent.LastError, Does.Contain("could not start"));
            Assert.That(agent.Host, Is.Null);
            Assert.That(agent.RunIndex, Is.EqualTo(1UL), "The failed bootstrap did not take run 2.");

            agent.Restart();
            Assert.That(agent.RunIndex, Is.EqualTo(2UL), "The next start is run 2.");
            Assert.That(Counter(agent, "after the failed bootstrap"), Is.EqualTo(42), "The failed bootstrap left the Agent memory.");
            RunOnce(agent, "run 2");
            Assert.That(Counter(agent, "run 2"), Is.EqualTo(43));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Item 13 (ADR-P11-013 amendment): a start the world refuses after the bootstrap keeps the Agent
        /// memory; a host stopped by user code cannot be read, so the next run starts from the defaults.
        /// </summary>
        [Test]
        public void AWorldRefusalKeepsTheAgentMemory_AHostStoppedByUserCodeResetsIt()
        {
            var agent = Spawn(PrepareAsset(MixedTree), ProfileFor("interactive"));
            agent.RestartMode = NativeTreeRestartMode.Never;
            RunOnce(agent, "run 0");
            agent.Restart();
            RunOnce(agent, "run 1");
            Assert.That(Counter(agent, "run 1"), Is.EqualTo(42));

            var refusing = Track(new GameObject("AIBT.Tests.P11014.WorldWithoutScheduler")).AddComponent<ProductionTreeWorld>();
            agent.World = refusing;
            LogAssert.Expect(LogType.Error, new Regex("could not join its world"));
            agent.Restart();
            Assert.That(agent.LastError, Does.Contain("could not join its world"));
            Assert.That(agent.Host, Is.Null);
            agent.World = _world;
            agent.Restart();
            Assert.That(Counter(agent, "after the refused start"), Is.EqualTo(42), "The refused start re-captured the values unchanged.");

            RunOnce(agent, "run 2");
            Assert.That(Counter(agent, "run 2"), Is.EqualTo(43));
            var errorBefore = agent.LastError; // Restart() keeps the refused start's error
            agent.Host.Stop();
            agent.Restart();
            Assert.That(agent.LastError, Is.EqualTo(errorBefore), "No new fault.");
            Assert.That(agent.Host, Is.Not.Null);
            Assert.That(Counter(agent, "after Host.Stop()"), Is.EqualTo(40), "A host stopped by user code is not read: defaults.");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>P11-014 item 6 (W6): IsExecuting is public and true while a PipelinedJobs round is in flight.</summary>
        [Test]
        public void IsExecuting_IsPublic_AndTrueWhileAPipelinedRoundIsInFlight()
        {
            Assert.That(typeof(ProductionTreeHost).GetProperty(nameof(ProductionTreeHost.IsExecuting))?.GetMethod?.IsPublic ?? false, Is.True);
            var agent = Spawn(PrepareAsset(MixedTree), ProfileFor("forced-pipelined-jobs"));
            agent.RestartMode = NativeTreeRestartMode.Never;
            var host = agent.Host;
            Assert.That(host.IsExecuting, Is.False, "before the first frame");
            var sawExecuting = false;
            for (var frame = 0; frame < 80 && agent.CompletedRuns == 0; frame++)
            {
                Frame();
                if (host.IsExecuting)
                {
                    sawExecuting = true;
                    Assert.That(host.CanReadTreeValues, Is.False, "executing: not readable");
                }
            }
            Assert.That(sawExecuting, Is.True, "a pipelined round spans frames");
            Assert.That(agent.CompletedRuns, Is.EqualTo(1));
            Assert.That(host.IsExecuting, Is.False, "after the run");
        }

        /// <summary>11c: a tree with only Agent keys (the Agent range at offset 0) keeps them across runs.</summary>
        [TestCaseSource(nameof(Kinds))]
        public void AnAgentOnlyTree_KeepsItsAgentKeys(string kind)
        {
            var agent = Spawn(PrepareAsset(AgentOnlyTree), ProfileFor(kind));
            agent.RestartMode = NativeTreeRestartMode.Never;
            var offset = typeof(GeneratedTreeDispatchAdapterV2).GetField("_agentBaseOffset", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(offset.GetValue(agent.Host.GeneratedDispatchAdapter), Is.EqualTo(0u), "The Agent range starts at 0.");
            RunOnce(agent, kind + " run 1");
            agent.Restart();
            Assert.That(Counter(agent, "restart"), Is.EqualTo(41));
            RunOnce(agent, kind + " run 2");
            agent.Stop();
            agent.Restart();
            RunOnce(agent, kind + " run 3");
            Assert.That(Counter(agent, "run 3"), Is.EqualTo(43));
            agent.Restart(true);
            Assert.That(Counter(agent, "Restart(true)"), Is.EqualTo(40));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>11c: an Agent contract of keys of several sizes; the copy keeps each at its own offset.</summary>
        [TestCaseSource(nameof(Kinds))]
        public void AnAgentContractOfSeveralSizes_KeepsEveryKey(string kind)
        {
            var agent = Spawn(PrepareAsset(SizedAgentTree), ProfileFor(kind));
            agent.RestartMode = NativeTreeRestartMode.Never;
            // Item 15: the one Bool Tree key ends at 1 and the Agent range is not padded, so the Agent
            // values (an Int32, a Float64) sit at odd addresses; TryGetValue loads them typed.
            var offset = typeof(GeneratedTreeDispatchAdapterV2).GetField("_agentBaseOffset", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(offset.GetValue(agent.Host.GeneratedDispatchAdapter), Is.EqualTo(1u), "The Agent range starts at offset 1.");
            for (var run = 1; run <= 3; run++)
            {
                RunOnce(agent, kind + " run " + run);
                if (run == 2) agent.Stop();
                agent.Restart();
                Assert.That(Counter(agent, "run " + run), Is.EqualTo(40 + run), kind);
                Assert.That(Int(agent, "c-second", "run " + run), Is.EqualTo(100 + run), kind);
                Assert.That(agent.TryGetValue("a-flag", out bool flag) && flag, Is.True, kind + ": a-flag");
                Assert.That(agent.TryGetValue("b-wide", out double wide) && wide == 2.5, Is.True, kind + ": b-wide");
            }
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>11c: Restart(true) and a change of Tree while the agent is stopped.</summary>
        [TestCaseSource(nameof(Kinds))]
        public void WhileStopped_RestartTrueResets_AndATreeChangeKeepsOrResetsByContract(string kind)
        {
            var agent = Spawn(PrepareAsset(MixedTree), ProfileFor(kind));
            agent.RestartMode = NativeTreeRestartMode.Never;
            RunOnce(agent, kind + " run 1");
            agent.Stop();
            agent.Restart(true);
            Assert.That(Counter(agent, "Restart(true) while stopped"), Is.EqualTo(40), kind);
            RunOnce(agent, kind + " run 2");
            agent.Restart();
            RunOnce(agent, kind + " run 3");
            Assert.That(Counter(agent, "run 3"), Is.EqualTo(42), kind);
            agent.Stop();
            agent.Tree = PrepareAsset(WideTree);
            Assert.That(agent.Host, Is.Null, kind + ": a stopped agent stays stopped when Tree changes");
            agent.Restart();
            Assert.That(Counter(agent, "same contract while stopped"), Is.EqualTo(42), kind);
            agent.Stop();
            agent.Tree = PrepareAsset(OtherContractTree);
            Assert.That(agent.Host, Is.Null, kind);
            agent.Restart();
            Assert.That(Counter(agent, "another contract while stopped"), Is.EqualTo(40), kind);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// 11d (review): what a warmed restart allocates. Capture and restore alone reuse the agent's buffers
        /// and allocate nothing; a whole Restart() creates a host component and its dispatch, which do. The
        /// count is logged for the evidence, not bounded.
        /// </summary>
        [Test]
        public void AWarmedRestart_CaptureAndRestoreAllocateNothing_TheWholeRestartIsMeasured()
        {
            var agent = Spawn(PrepareAsset(MixedTree), ProfileFor("interactive"));
            agent.RestartMode = NativeTreeRestartMode.Never;
            RunOnce(agent, "run 0");
            for (var warm = 0; warm < 5; warm++) agent.Restart();
            var memory = new NativeAgentScopeMemory();
            var host = agent.Host;
            Assert.That(host.TryCaptureAgentMemory(memory), Is.True);
            Assert.That(host.TryRestoreAgentMemory(memory), Is.True);
            TestDelegate copy = () =>
            {
                host.TryCaptureAgentMemory(memory);
                host.TryRestoreAgentMemory(memory);
            };
            var copySamples = GcAllocations(copy);
            Assert.That(copySamples, Is.Zero, "capture + restore, GC.Alloc samples");
            Assert.That(copy, GcAllocIs.Not.AllocatingGCMemory());

            const int Restarts = 20;
            var restartSamples = GcAllocations(() => { for (var index = 0; index < Restarts; index++) agent.Restart(); });
            Debug.Log("P11-014 11d: a warmed Restart() makes " + (restartSamples / (double)Restarts)
                + " GC.Alloc samples on average (" + restartSamples + " over " + Restarts + "); capture + restore alone " + copySamples + ".");
            Assert.That(Counter(agent, "after the restarts"), Is.EqualTo(41));
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
