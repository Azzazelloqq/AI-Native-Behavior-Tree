using System.Collections.Generic;
using System.Reflection;
using AIBT.Tests.Integration.Simulation;
using NUnit.Framework;
using UnityEngine;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-002 stage 3 (owner decision 4): <see cref="NativeTreeAgent.RandomSeed"/> seeds the random
    /// streams of every run's host. Each start or restart is a new host, so a new tree instance id and
    /// freshly derived streams. The trees are the container's P10-009 random trees on its own catalog.
    /// </summary>
    public sealed partial class NativeTreeAgentRandomSeedTests
    {
        private readonly List<Object> _created = new List<Object>();
        private readonly List<NativeTreeAgent> _agents = new List<NativeTreeAgent>();
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;
        private int _baselinePrograms;

        [SetUp]
        public void SetUp()
        {
            _baselinePrograms = NativeTreeProgramCache.LiveProgramCount;
            var worldObject = Track(new GameObject("AIBT.Tests.P11002.SeedWorld"));
            _scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            _world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(_world, "Awake");
            InvokePrivate(_world, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var agent in _agents) if (agent != null) agent.Deactivate();
            _agents.Clear();
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) Object.DestroyImmediate(_created[index]);
            _created.Clear();
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms), "A test leaked a shared program.");
        }

        [Test]
        public void TheDefaultSeed_IsZero_AndTheSeedIsSerialized()
        {
            var agent = Spawn(Track(PrepareAsset(CoinTree)), NativeTreeRestartMode.Never, activate: false);
            Assert.That(agent.RandomSeed, Is.EqualTo(ProductionTreeHost.DefaultRandomSeed));
            var field = typeof(NativeTreeAgent).GetField("_randomSeed", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            Assert.That(field.GetCustomAttribute<SerializeField>(), Is.Not.Null, "A serialized field (Inspector).");
            agent.RandomSeed = 77UL;
            Assert.That(field.GetValue(agent), Is.EqualTo(77UL));
        }

        /// <summary>
        /// Every run (the first start, each automatic restart, an explicit Restart) gets a host whose
        /// streams derive from the agent's seed and that host's own instance id; a seed set between runs
        /// applies from the next run.
        /// </summary>
        [Test]
        public void EveryRunsHost_IsSeededFromTheAgentsSeed_AndItsOwnInstanceId()
        {
            const ulong Seed = 0xA6E47UL;
            var asset = Track(PrepareAsset(CoinTree));
            var agent = Spawn(asset, NativeTreeRestartMode.Always, seed: Seed);
            var ids = new HashSet<ulong>();
            for (var frame = 0; frame < 6; frame++)
            {
                var host = agent.Host;
                Assert.That(host, Is.Not.Null, "frame " + frame);
                Assert.That(host.RandomSeed, Is.EqualTo(Seed));
                AssertDerived(agent, Seed, "frame " + frame);
                Assert.That(ids.Add(host.InstanceId), Is.True, "Each run is a new tree instance.");
                Frames(1);
                Assert.That(agent.LastError, Is.Null, "The coin tree runs on the agent.");
            }
            Assert.That(agent.CompletedRuns, Is.EqualTo(6), "The coin finishes in one update and restarts.");

            agent.RandomSeed = Seed + 1;
            Assert.That(agent.Host.RandomSeed, Is.EqualTo(Seed), "A run keeps the seed it started with.");
            agent.Restart();
            Assert.That(agent.Host.RandomSeed, Is.EqualTo(Seed + 1), "The next run uses the new seed.");
            AssertDerived(agent, Seed + 1, "after Restart");
        }

        [Test]
        public void TwoAgentsWithOneTreeAndOneSeed_HaveDifferentStreams()
        {
            const ulong Seed = 42UL;
            var asset = Track(PrepareAsset(RollerTree));
            var a = Spawn(asset, NativeTreeRestartMode.Never, seed: Seed);
            var b = Spawn(asset, NativeTreeRestartMode.Never, seed: Seed);
            Assert.That(b.Host.InstanceId, Is.Not.EqualTo(a.Host.InstanceId));
            AssertDerived(a, Seed, "a");
            AssertDerived(b, Seed, "b");
            var node = RandomNode(a);
            a.Host.GeneratedDispatchAdapter.TryReadRandomStream(node, out var stateA, out var incrementA);
            b.Host.GeneratedDispatchAdapter.TryReadRandomStream(node, out var stateB, out var incrementB);
            Assert.That(stateB == stateA && incrementB == incrementA, Is.False, "Different instance ids give different streams.");
            Frames(60);
            Assert.That(a.LastError, Is.Null);
            Assert.That(b.LastError, Is.Null);
        }

        /// <summary>
        /// Four agents of one catalog under each scheduling policy: every agent's committed stream state
        /// stays on its own derived stream and moves forward as the roller rolls. Forced
        /// BatchedJobsSameFrame and PipelinedJobs run the scheduler's group dispatch (owner decision 8 of
        /// ADR-P11-002: the group commit now writes the advanced stream state back). A pipelined update
        /// spans many scheduler frames, so the test runs until every agent has rolled, not a fixed count.
        /// </summary>
        [TestCase("immediate")]
        [TestCase("agent-frame")]
        [TestCase("batched-jobs")]
        [TestCase("pipelined-jobs")]
        public void UnderEveryPolicy_EachAgentsStreamAdvancesOnItsOwnDerivedSequence(string policy)
        {
            const ulong Seed = 0xB47CUL;
            const int Rolls = 12, FrameCap = 4000;
            var asset = Track(PrepareAsset(RollerTree));
            var profile = ProfileFor(policy);
            var agents = new List<NativeTreeAgent>();
            for (var index = 0; index < 4; index++) agents.Add(Spawn(asset, NativeTreeRestartMode.Never, seed: Seed, profile: profile));
            var node = RandomNode(agents[0]);
            var draws = new int[agents.Count];
            var frames = 0;
            while (frames < FrameCap && System.Array.Exists(draws, count => count < Rolls))
            {
                Frames(1);
                frames++;
                for (var index = 0; index < agents.Count; index++)
                {
                    Assert.That(agents[index].LastError, Is.Null, policy + " agent " + index);
                    var now = DrawsSinceDerivation(agents[index], Seed, node);
                    Assert.That(now, Is.GreaterThanOrEqualTo(draws[index]), policy + ": the stream only moves forward");
                    Assert.That(now - draws[index], Is.LessThanOrEqualTo(1), policy + ": at most one roll per frame");
                    draws[index] = now;
                }
            }
            TestContext.WriteLine(policy + ": " + frames + " frames, draws per agent " + string.Join(", ", draws));
            for (var index = 0; index < agents.Count; index++)
                Assert.That(draws[index], Is.GreaterThanOrEqualTo(Rolls), policy + ": agent " + index + " rolled and its stream advanced");
        }

        /// <summary>
        /// One tree, one seed and the same four tree instance ids give the same roll sequence under all
        /// four policies: each agent's committed stream states, in the order they appear, are the same,
        /// and each new state is exactly one draw after the last (no roll lost or repeated). The instance
        /// ids are pinned by setting the host's private id counter before each run (test-only reflection).
        /// </summary>
        [Test]
        public void OneTreeSeedAndInstanceIds_RollTheSameSequenceUnderEveryPolicy()
        {
            const ulong Seed = 0x5EED7UL;
            const int Agents = 4, Rolls = 12, FrameCap = 4000;
            var counter = typeof(ProductionTreeHost).GetField("s_nextTreeInstanceId", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(counter, Is.Not.Null);
            var restore = (long)counter.GetValue(null);
            var firstId = restore + 1000000L;
            var asset = Track(PrepareAsset(RollerTree));
            var policies = new[] { "immediate", "agent-frame", "batched-jobs", "pipelined-jobs" };
            var runs = new List<List<ulong>[]>();
            try
            {
                foreach (var policy in policies)
                {
                    counter.SetValue(null, firstId);
                    var profile = ProfileFor(policy);
                    var agents = new List<NativeTreeAgent>();
                    for (var index = 0; index < Agents; index++) agents.Add(Spawn(asset, NativeTreeRestartMode.Never, seed: Seed, profile: profile));
                    var node = RandomNode(agents[0]);
                    var sequences = new List<ulong>[Agents];
                    var increments = new ulong[Agents];
                    for (var index = 0; index < Agents; index++)
                    {
                        Assert.That(agents[index].Host.InstanceId, Is.EqualTo((ulong)(firstId + 1 + index)), policy + ": pinned instance id");
                        Assert.That(agents[index].Host.GeneratedDispatchAdapter.TryReadRandomStream(node, out var start, out increments[index]), Is.True);
                        sequences[index] = new List<ulong> { start };
                    }
                    var frames = 0;
                    while (frames < FrameCap && System.Array.Exists(sequences, sequence => sequence.Count <= Rolls))
                    {
                        Frames(1);
                        frames++;
                        for (var index = 0; index < Agents; index++)
                        {
                            Assert.That(agents[index].LastError, Is.Null, policy + " agent " + index);
                            Assert.That(agents[index].Host.GeneratedDispatchAdapter.TryReadRandomStream(node, out var state, out _), Is.True);
                            var sequence = sequences[index];
                            if (state == sequence[sequence.Count - 1]) continue;
                            var expected = sequence[sequence.Count - 1];
                            NativeRandomStreamDerivationV1.NextUInt32(ref expected, increments[index]);
                            Assert.That(state, Is.EqualTo(expected), policy + " agent " + index + ": exactly one draw per new state");
                            sequence.Add(state);
                        }
                    }
                    TestContext.WriteLine(policy + ": " + Rolls + " rolls per agent in " + frames + " frames");
                    for (var index = 0; index < Agents; index++)
                        Assert.That(sequences[index].Count, Is.GreaterThan(Rolls), policy + " agent " + index + " rolled");
                    runs.Add(sequences);
                    foreach (var agent in agents)
                    {
                        agent.Deactivate();
                        _agents.Remove(agent);
                    }
                }
            }
            finally
            {
                counter.SetValue(null, System.Math.Max(restore, firstId + Agents));
            }
            for (var run = 1; run < runs.Count; run++)
                for (var index = 0; index < Agents; index++)
                    for (var roll = 0; roll <= Rolls; roll++)
                        Assert.That(runs[run][index][roll], Is.EqualTo(runs[0][index][roll]),
                            policies[run] + " vs immediate: agent " + index + ", roll " + roll);
        }

        private static int DrawsSinceDerivation(NativeTreeAgent agent, ulong seed, uint node)
        {
            var host = agent.Host;
            Assert.That(host.GeneratedDispatchAdapter.TryReadRandomStream(node, out var state, out _), Is.True);
            Assert.That(NativeRandomStreamDerivationV1.TryDerive(seed, new NativeHash256V1(agent.Program.Header.CanonicalSemanticHash),
                host.InstanceId, node, out var stream), Is.True);
            var walk = stream.State;
            for (var count = 0; count <= 100000; count++)
            {
                if (walk == state) return count;
                NativeRandomStreamDerivationV1.NextUInt32(ref walk, stream.Increment);
            }
            Assert.Fail("The committed state is not on the derived stream.");
            return -1;
        }

        private SchedulingProfile ProfileFor(string policy)
        {
            switch (policy)
            {
                case "immediate": return Forced(SchedulingPolicy.Immediate, false);
                case "batched-jobs":
                    _scheduler.SetJobsCapabilities(JobsCapabilities(false));
                    return Forced(SchedulingPolicy.BatchedJobsSameFrame, false);
                case "pipelined-jobs":
                    _scheduler.SetJobsCapabilities(JobsCapabilities(true));
                    return Forced(SchedulingPolicy.PipelinedJobs, true);
                case "agent-frame":
                    _scheduler.AgentFrameMinimumBatchSize = 1;
                    return SchedulingProfile.JobsAgentFrame;
                default: throw new System.ArgumentOutOfRangeException(nameof(policy), policy, null);
            }
        }

        private static SchedulingProfile Forced(SchedulingPolicy policy, bool pipelining)
        {
            Assert.That(SchedulingProfile.TryCreate("tests.p11002." + policy, 0, 1u, null, null, pipelining, policy,
                out var profile, out var error), Is.True, error.ToString());
            return profile;
        }

        private static SchedulerJobsCapabilities JobsCapabilities(bool pipelined)
        {
            Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, pipelined, out var capabilities, out var error),
                Is.True, error.ToString());
            return capabilities;
        }

        private static uint RandomNode(NativeTreeAgent agent)
        {
            for (uint node = 0; node < agent.Program.Nodes.Count; node++)
                if (agent.Host.GeneratedDispatchAdapter.TryReadRandomStream(node, out _, out _)) return node;
            Assert.Fail("The tree has no random node.");
            return 0;
        }

        private static void AssertDerived(NativeTreeAgent agent, ulong seed, string what)
        {
            var host = agent.Host;
            var hash = new NativeHash256V1(agent.Program.Header.CanonicalSemanticHash);
            var found = 0;
            for (uint node = 0; node < agent.Program.Nodes.Count; node++)
            {
                if (!host.GeneratedDispatchAdapter.TryReadRandomStream(node, out var state, out var increment)) continue;
                found++;
                Assert.That(NativeRandomStreamDerivationV1.TryDerive(seed, hash, host.InstanceId, node, out var expected), Is.True);
                Assert.That(state, Is.EqualTo(expected.State), what + ": state of node " + node);
                Assert.That(increment, Is.EqualTo(expected.Increment), what + ": increment of node " + node);
            }
            Assert.That(found, Is.GreaterThan(0), what + ": a random node");
        }

        // ------------------------------------------------------------------ helpers

        private NativeTreeAgent Spawn(ProductionTreeAsset asset, NativeTreeRestartMode restart, bool activate = true,
            ulong seed = 0UL, SchedulingProfile profile = null)
        {
            var gameObject = Track(new GameObject("AIBT.Tests.P11002.SeedAgent" + _agents.Count));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = asset;
            agent.World = _world;
            agent.RestartMode = restart;
            agent.RandomSeed = seed;
            agent.CatalogFactory = SimulationTestCatalog.TryCreateRuntimeCatalog;
            if (profile != null) agent.Profile = profile;
            _agents.Add(agent);
            if (activate) agent.Activate();
            return agent;
        }

        private void Frames(int count)
        {
            for (var frame = 0; frame < count; frame++)
            {
                InvokePrivate(_scheduler, "Update");
                foreach (var agent in _agents) if (agent != null) agent.ProcessRootResult();
            }
        }

        private T Track<T>(T value) where T : Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
