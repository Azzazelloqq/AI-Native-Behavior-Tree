using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-012 (ADR-P11-008, AIBT-052): <see cref="NativeTreeAgent.RandomStreamId"/> makes an agent's random
    /// draws repeatable. With a nonzero id, the stream identity of a run is a function of (id, run index)
    /// only; the run index is 0 on every enable and +1 for each run that started while the component stays
    /// enabled. Id 0 keeps the automatic identity (the host's process-wide instance id). Everything is
    /// observed through the public API: the values the tree's random nodes write, read with
    /// <see cref="NativeTreeAgent.TryGetValue{T}"/> when a run completes.
    /// </summary>
    public sealed partial class NativeTreeAgentRandomSeedTests
    {
        // Each run draws once from 'pick-first' into 'first' and three times from 'pick-last' into
        // 'last' (the last of the three stays), then succeeds.
        private const string DrawTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11012.draws"", ""name"": ""P11-012 draws"",
  ""root"": ""run"",
  ""blackboard"": {
    ""first"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": -1 },
    ""last"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": -1 }
  },
  ""nodes"": {
    ""run"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""pick-first"", ""again""] },
    ""pick-first"": { ""type"": ""aibt.native.random-int"", ""typeVersion"": 1, ""parameters"": { ""min"": 0, ""max"": 2147483647 }, ""bindings"": { ""key"": ""first"" } },
    ""again"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""pick-last""], ""parameters"": { ""count"": 3, ""stopOnFailure"": false } },
    ""pick-last"": { ""type"": ""aibt.native.random-int"", ""typeVersion"": 1, ""parameters"": { ""min"": 0, ""max"": 2147483647 }, ""bindings"": { ""key"": ""last"" } }
  }
}";

        private const int FrameCapForRuns = 4000;

        /// <summary>The values one run wrote, read when it completed, and the run index it ran under.</summary>
        private readonly struct Draw
        {
            internal Draw(ulong runIndex, int first, int last)
            {
                RunIndex = runIndex;
                First = first;
                Last = last;
            }

            internal ulong RunIndex { get; }
            internal int First { get; }
            internal int Last { get; }
            public override string ToString() => "run " + RunIndex + ": " + First + ", " + Last;
        }

        /// <summary>Records every completed run of <paramref name="agent"/>; stops it after <paramref name="runs"/>.</summary>
        private static List<Draw> Record(NativeTreeAgent agent, int runs)
        {
            var draws = new List<Draw>();
            agent.Completed += result =>
            {
                Assert.That(result, Is.EqualTo(NodeStatus.Success), agent.name);
                Assert.That(agent.TryGetValue("first", out int first), Is.True, agent.name + ": 'first' readable at completion");
                Assert.That(agent.TryGetValue("last", out int last), Is.True, agent.name + ": 'last' readable at completion");
                draws.Add(new Draw(agent.RunIndex, first, last));
                if (draws.Count >= runs) agent.Stop();
            };
            return draws;
        }

        private NativeTreeAgent SpawnWithId(ProductionTreeAsset asset, NativeTreeRestartMode restart, ulong seed, ulong streamId,
            SchedulingProfile profile = null, bool activate = true)
        {
            var agent = Spawn(asset, restart, activate: false, seed: seed, profile: profile);
            agent.RandomStreamId = streamId;
            if (activate) agent.Activate();
            return agent;
        }

        private void RunUntil(System.Func<bool> done, string what)
        {
            var frames = 0;
            while (!done() && frames < FrameCapForRuns)
            {
                Frames(1);
                frames++;
            }
            Assert.That(done(), Is.True, what + ": not done after " + frames + " frames");
        }

        private void Destroy(NativeTreeAgent agent)
        {
            agent.Deactivate();
            _agents.Remove(agent);
            Object.DestroyImmediate(agent.gameObject);
        }

        [Test]
        public void TheDefaultStreamIdIsZero_ItIsSerialized_AndTheRunIndexStartsAtZero()
        {
            var agent = Spawn(Track(PrepareAsset(DrawTree)), NativeTreeRestartMode.Never, activate: false);
            Assert.That(agent.RandomStreamId, Is.EqualTo(0UL));
            Assert.That(agent.RunIndex, Is.EqualTo(0UL));
            var field = typeof(NativeTreeAgent).GetField("_randomStreamId", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            Assert.That(field.GetCustomAttribute<SerializeField>(), Is.Not.Null, "A serialized field (Inspector).");
            agent.RandomStreamId = 0xFEEDUL;
            Assert.That(field.GetValue(agent), Is.EqualTo(0xFEEDUL));
            Assert.That(typeof(NativeTreeAgent).GetProperty(nameof(NativeTreeAgent.RunIndex)).SetMethod?.IsPublic ?? false, Is.False,
                "The run index is read-only.");
        }

        /// <summary>
        /// The ADR's reproducibility test. Session 1: agents A (id 11) and B (id 22), one seed, three runs
        /// each (automatic restarts), recorded. Then both are destroyed, unrelated hosts are bootstrapped
        /// with a failed bootstrap between them (it consumes an instance id too), and session 2 starts B
        /// before A. Every (id, run) pair draws the same values in both sessions. A control agent with id 0
        /// draws other values in session 2: the automatic identity is not repeatable, which is what the
        /// harness would have caught before P11-012.
        /// </summary>
        [TestCase("immediate")]
        [TestCase("agent-frame")]
        [TestCase("batched-jobs")]
        [TestCase("pipelined-jobs")]
        public void RunsWithAStreamId_DrawTheSameInAnotherSession_InAnyStartOrder_AfterOtherHosts(string policy)
        {
            const ulong Seed = 0x9E5EEDUL;
            const int Runs = 3;
            var asset = Track(PrepareAsset(DrawTree));
            var profile = ProfileFor(policy);

            var a = SpawnWithId(asset, NativeTreeRestartMode.Always, Seed, 11UL, profile);
            var b = SpawnWithId(asset, NativeTreeRestartMode.Always, Seed, 22UL, profile, activate: false);
            var control = SpawnWithId(asset, NativeTreeRestartMode.Always, Seed, 0UL, profile, activate: false);
            var firstA = Record(a, Runs);
            var firstB = Record(b, Runs);
            var firstControl = Record(control, Runs);
            b.Activate();
            control.Activate();
            RunUntil(() => firstA.Count >= Runs && firstB.Count >= Runs && firstControl.Count >= Runs, policy + " session 1");
            Destroy(a);
            Destroy(b);
            Destroy(control);

            BootstrapUnrelatedHostsWithAFailureBetween();

            b = SpawnWithId(asset, NativeTreeRestartMode.Always, Seed, 22UL, profile);
            a = SpawnWithId(asset, NativeTreeRestartMode.Always, Seed, 11UL, profile, activate: false);
            control = SpawnWithId(asset, NativeTreeRestartMode.Always, Seed, 0UL, profile, activate: false);
            var secondB = Record(b, Runs);
            var secondA = Record(a, Runs);
            var secondControl = Record(control, Runs);
            control.Activate();
            a.Activate();
            RunUntil(() => secondA.Count >= Runs && secondB.Count >= Runs && secondControl.Count >= Runs, policy + " session 2");

            TestContext.WriteLine(policy + " A: " + string.Join(" | ", firstA));
            TestContext.WriteLine(policy + " B: " + string.Join(" | ", firstB));
            TestContext.WriteLine(policy + " control 1: " + string.Join(" | ", firstControl) + "; control 2: " + string.Join(" | ", secondControl));
            for (var run = 0; run < Runs; run++)
            {
                Assert.That(firstA[run].RunIndex, Is.EqualTo((ulong)run), policy + ": A's run index");
                Assert.That(firstB[run].RunIndex, Is.EqualTo((ulong)run), policy + ": B's run index");
                Assert.That(secondA[run].RunIndex, Is.EqualTo((ulong)run), policy + ": A's run index, session 2");
                Assert.That(secondB[run].RunIndex, Is.EqualTo((ulong)run), policy + ": B's run index, session 2");
                AssertSameDraw(secondA[run], firstA[run], policy + ": A run " + run);
                AssertSameDraw(secondB[run], firstB[run], policy + ": B run " + run);
                Assert.That(firstB[run].First, Is.Not.EqualTo(firstA[run].First), policy + ": A and B differ in run " + run);
                if (run > 0) Assert.That(firstA[run].First, Is.Not.EqualTo(firstA[run - 1].First), policy + ": each run of A draws anew");
            }
            Assert.That(secondControl[0].First == firstControl[0].First && secondControl[0].Last == firstControl[0].Last, Is.False,
                policy + ": id 0 uses the process-wide instance id, which moved, so its first run differs");
        }

        /// <summary>
        /// A run started after a disable/enable is run 0 again; <see cref="NativeTreeAgent.Restart"/>, Stop
        /// then Restart and a change of <see cref="NativeTreeAgent.Tree"/> take the next run index. Each is
        /// compared with a reference agent with the same id that restarts automatically.
        /// </summary>
        [TestCase("immediate")]
        [TestCase("agent-frame")]
        [TestCase("batched-jobs")]
        [TestCase("pipelined-jobs")]
        public void RestartStopAndTreeChangeTakeTheNextRun_DisableEnableRepeatsRunZero(string policy)
        {
            const ulong Seed = 0x7E57UL, Id = 9UL;
            const int Runs = 5;
            var asset = Track(PrepareAsset(DrawTree));
            var sameTree = Track(PrepareAsset(DrawTree));
            var profile = ProfileFor(policy);

            var reference = SpawnWithId(asset, NativeTreeRestartMode.Always, Seed, Id, profile);
            var expected = Record(reference, Runs);
            RunUntil(() => expected.Count >= Runs, policy + " reference");
            Destroy(reference);

            var agent = SpawnWithId(asset, NativeTreeRestartMode.Never, Seed, Id, profile);
            var draws = new List<Draw>();
            agent.Completed += result =>
            {
                Assert.That(agent.TryGetValue("first", out int first), Is.True);
                Assert.That(agent.TryGetValue("last", out int last), Is.True);
                draws.Add(new Draw(agent.RunIndex, first, last));
            };
            void Next(string what, ulong run)
            {
                var count = draws.Count;
                RunUntil(() => draws.Count > count, policy + " " + what);
                Assert.That(agent.RunIndex, Is.EqualTo(run), policy + " " + what + ": run index");
                AssertSameDraw(draws[draws.Count - 1], expected[(int)run], policy + " " + what);
            }

            Next("first run", 0);
            agent.Restart();
            Next("Restart()", 1);
            agent.Stop();
            agent.Restart();
            Next("Stop then Restart()", 2);
            agent.Tree = sameTree;
            Next("a change of Tree", 3);
            agent.Deactivate();
            Assert.That(agent.RunIndex, Is.EqualTo(3UL), "A disabled agent keeps the index of its last run.");
            agent.Activate();
            Assert.That(agent.RunIndex, Is.EqualTo(0UL), "Enable starts a new agent life at run 0.");
            Next("disable/enable", 0);
            agent.Restart();
            Next("Restart() after enable", 1);
        }

        /// <summary>
        /// A start that fails does not consume a run index: the world refuses the first start (it has no
        /// scheduler), the agent faults; with a working world, Restart starts run 0, which draws what run 0
        /// of a reference agent with the same id draws.
        /// </summary>
        [Test]
        public void AStartThatFails_DoesNotConsumeARunIndex()
        {
            const ulong Seed = 0xFA11UL, Id = 31UL;
            var asset = Track(PrepareAsset(DrawTree));
            var reference = SpawnWithId(asset, NativeTreeRestartMode.Never, Seed, Id);
            var expected = Record(reference, 1);
            RunUntil(() => expected.Count >= 1, "reference");

            var brokenWorld = Track(new GameObject("AIBT.Tests.P11012.WorldWithoutScheduler")).AddComponent<ProductionTreeWorld>();
            var agent = SpawnWithId(asset, NativeTreeRestartMode.Never, Seed, Id, activate: false);
            agent.World = brokenWorld;
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("could not join its world"));
            agent.Activate();
            Assert.That(agent.LastError, Does.Contain("could not join its world"));
            Assert.That(agent.Host, Is.Null);
            Assert.That(agent.RunIndex, Is.EqualTo(0UL));

            agent.World = _world;
            var draws = Record(agent, 1);
            agent.Restart();
            RunUntil(() => draws.Count >= 1, "after the failed start");
            Assert.That(draws[0].RunIndex, Is.EqualTo(0UL), "The failed start did not consume run 0.");
            AssertSameDraw(draws[0], expected[0], "run 0 after a failed start");
        }

        /// <summary>Two agents with one id and one seed draw the same values; with id 0 they do not (automatic identity).</summary>
        [Test]
        public void TwoAgentsWithOneStreamIdAndSeed_DrawTheSame()
        {
            const ulong Seed = 0x5A3EUL;
            var asset = Track(PrepareAsset(DrawTree));
            var a = SpawnWithId(asset, NativeTreeRestartMode.Never, Seed, 77UL);
            var b = SpawnWithId(asset, NativeTreeRestartMode.Never, Seed, 77UL);
            var c = SpawnWithId(asset, NativeTreeRestartMode.Never, Seed, 0UL);
            var d = SpawnWithId(asset, NativeTreeRestartMode.Never, Seed, 0UL);
            var drawsA = Record(a, 1);
            var drawsB = Record(b, 1);
            var drawsC = Record(c, 1);
            var drawsD = Record(d, 1);
            RunUntil(() => drawsA.Count + drawsB.Count + drawsC.Count + drawsD.Count == 4, "four agents");
            AssertSameDraw(drawsB[0], drawsA[0], "one id, one seed");
            Assert.That(drawsC[0].First == drawsD[0].First && drawsC[0].Last == drawsD[0].Last, Is.False, "id 0: each agent its own stream");
            Assert.That(drawsC[0].First == drawsA[0].First, Is.False, "id 0 is not id 77");
            Assert.That(a.Host, Is.Null, "stopped after its run");
        }

        /// <summary>
        /// With a stream id, every run's host derives its streams from the combined identity of (id, run),
        /// which the host reports; its process-wide instance id stays unique and is not used.
        /// </summary>
        [Test]
        public void EveryRunsHost_DerivesItsStreamsFromTheCombinedIdentity()
        {
            const ulong Seed = 0xC0DEUL, Id = 0x1234UL;
            var asset = Track(PrepareAsset(DrawTree));
            var agent = SpawnWithId(asset, NativeTreeRestartMode.Never, Seed, Id);
            for (ulong run = 0; run < 3; run++)
            {
                var host = agent.Host;
                Assert.That(agent.RunIndex, Is.EqualTo(run));
                Assert.That(host.RandomSeed, Is.EqualTo(Seed));
                Assert.That(host.RandomStreamIdentity, Is.EqualTo(RandomStreamSeeding.CombineStreamIdentity(Id, run)));
                Assert.That(host.RandomStreamIdentity, Is.Not.EqualTo(host.InstanceId));
                var hash = new NativeHash256V1(agent.Program.Header.CanonicalSemanticHash);
                var found = 0;
                for (uint node = 0; node < agent.Program.Nodes.Count; node++)
                {
                    if (!host.GeneratedDispatchAdapter.TryReadRandomStream(node, out var state, out var increment)) continue;
                    found++;
                    Assert.That(NativeRandomStreamDerivationV1.TryDerive(Seed, hash, host.RandomStreamIdentity, node, out var stream), Is.True);
                    Assert.That(state, Is.EqualTo(stream.State), "run " + run + ", node " + node);
                    Assert.That(increment, Is.EqualTo(stream.Increment), "run " + run + ", node " + node);
                }
                Assert.That(found, Is.EqualTo(2), "two random nodes");
                agent.Restart();
            }
        }

        /// <summary>
        /// P11-014 item 11c (review N14): a change of <see cref="NativeTreeAgent.RandomStreamId"/> leaves the
        /// running host's streams alone and applies from the next run.
        /// </summary>
        [Test]
        public void ARandomStreamIdChange_AppliesFromTheNextRun()
        {
            const ulong Seed = 0xBEEFUL;
            var agent = SpawnWithId(Track(PrepareAsset(DrawTree)), NativeTreeRestartMode.Never, Seed, 5UL);
            var first = agent.Host;
            Assert.That(first.RandomStreamIdentity, Is.EqualTo(RandomStreamSeeding.CombineStreamIdentity(5UL, 0UL)));
            agent.RandomStreamId = 9UL;
            Assert.That(agent.Host, Is.SameAs(first), "Setting the id does not restart.");
            Assert.That(first.RandomStreamIdentity, Is.EqualTo(RandomStreamSeeding.CombineStreamIdentity(5UL, 0UL)), "The running host keeps its streams.");
            agent.Restart();
            Assert.That(agent.RunIndex, Is.EqualTo(1UL));
            Assert.That(agent.Host.RandomStreamIdentity, Is.EqualTo(RandomStreamSeeding.CombineStreamIdentity(9UL, 1UL)), "The next run uses the new id.");
            agent.RandomStreamId = 0UL;
            agent.Restart();
            Assert.That(agent.Host.RandomStreamIdentity, Is.EqualTo(agent.Host.InstanceId), "Id 0 is automatic again from the next run.");
        }

        private static void AssertSameDraw(Draw actual, Draw expected, string what)
        {
            Assert.That(actual.First, Is.EqualTo(expected.First), what + ": 'first'");
            Assert.That(actual.Last, Is.EqualTo(expected.Last), what + ": 'last'");
        }

        /// <summary>
        /// Moves the process-wide instance counter the way unrelated code would: three host bootstraps of
        /// another program, the second of which fails after it took its instance id (its trace capacity
        /// cannot be allocated), so the third host's id is two above the first.
        /// </summary>
        private void BootstrapUnrelatedHostsWithAFailureBetween()
        {
            var coin = Track(PrepareAsset(CoinTree));
            var program = CreateProgram(coin);
            var hosts = new List<ProductionTreeHost>();
            try
            {
                ProductionTreeHost NewHost()
                {
                    var host = Track(new GameObject("AIBT.Tests.P11012.UnrelatedHost")).AddComponent<ProductionTreeHost>();
                    hosts.Add(host);
                    return host;
                }
                var first = NewHost();
                Assert.That(program.TryBootstrap(first, 5UL, out var failure), Is.True, failure.ToString());
                var failed = NewHost();
                Assert.That(program.TryBootstrap(failed, 5UL, out failure, traceCapacity: new NativeTraceChannelCapacityV1(uint.MaxValue, 0, 0, 0)),
                    Is.False, "an impossible trace capacity fails the bootstrap");
                var third = NewHost();
                Assert.That(program.TryBootstrap(third, 5UL, out failure), Is.True, failure.ToString());
                Assert.That(third.InstanceId, Is.EqualTo(first.InstanceId + 2UL), "The failed bootstrap consumed an instance id.");
            }
            finally
            {
                foreach (var host in hosts)
                {
                    host.Stop(); // what OnDestroy does; EditMode sends it no OnDestroy
                    Object.DestroyImmediate(host.gameObject);
                }
                program.Dispose();
            }
        }
    }
}
