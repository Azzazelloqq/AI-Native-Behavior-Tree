using System.Collections.Generic;
using System.Reflection;
using AIBT.Simulation;
using NUnit.Framework;
using UnityEngine;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// P10-009: the container runs the same per-agent core as <see cref="ProductionTreeHost"/>, so a
    /// container agent and a standalone host fed the same times agree on every observable result.
    /// Also establishes, by test, how random streams are seeded for hosts and for the container.
    /// The host side needs a GameObject (that is what is being compared against); the container
    /// side never does.
    /// </summary>
    public sealed class TreeSimulationHostEquivalenceTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private readonly List<ProductionTreeProgram> _programs = new List<ProductionTreeProgram>();

        [TearDown]
        public void TearDown()
        {
            // Hosts first, then programs, then the assets that created them.
            for (var index = _objects.Count - 1; index >= 0; index--)
                if (_objects[index] is GameObject gameObject)
                {
                    // EditMode sends no OnDestroy to a plain MonoBehaviour: release the host as Unity would.
                    var host = gameObject.GetComponent<ProductionTreeHost>();
                    if (host != null)
                        typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
                    Object.DestroyImmediate(gameObject);
                }
            foreach (var program in _programs) program.Dispose();
            _programs.Clear();
            foreach (var item in _objects) if (item != null) Object.DestroyImmediate(item);
            _objects.Clear();
        }

        private ProductionTreeProgram Program(string json)
        {
            var asset = PrepareAsset(json);
            _objects.Add(asset);
            var program = CreateProgram(asset);
            _programs.Add(program);
            return program;
        }

        private ProductionTreeHost Host(ProductionTreeProgram program, System.Func<long> clock)
        {
            var gameObject = new GameObject("AIBT.Tests.P10009.Host");
            _objects.Add(gameObject);
            var host = gameObject.AddComponent<ProductionTreeHost>();
            Assert.That(program.TryBootstrap(host, out var failure, clock: clock), Is.True, failure.ToString());
            return host;
        }

        private static void Drive(ProductionTreeHost host)
            => typeof(ProductionTreeHost).GetMethod("DriveOneUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);

        /// <summary>Everything observable except the instance id (process-wide for hosts, container-local in the container).</summary>
        private static ulong Observe(NodeStatus? root, NativeRuntimeFailureV1 failure, ulong updates,
            NativeLifecycleMachineV1 machine, GeneratedTreeDispatchAdapterV2 adapter, List<uint> path)
        {
            var digest = Fnv.Create();
            digest.Add(updates);
            digest.Add(root.HasValue ? 1ul + (ulong)root.Value : 0ul);
            digest.Add((ulong)failure.Code);
            machine.CopyActivePath(path);
            digest.Add((ulong)path.Count);
            for (var node = 0; node < path.Count; node++) digest.Add(path[node]);
            for (uint offset = 0; adapter.TryReadTreeValueByte(offset, out var value); offset++) digest.Add(value);
            return digest.Value;
        }

        [TestCase(false, 1)]
        [TestCase(true, 1)]
        public void ContainerAgents_MatchStandaloneHosts_AtTheSameTimes(bool useJobs, int minimumJobBatch)
        {
            const int Agents = 6;
            var trees = new[] { Program(DeterministicLoopTree), Program(DeterministicErrandTree) };
            var settings = TreeSimulationSettings.Default(TickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = minimumJobBatch;
            var groupSettings = TreeGroupSettings.Default(Agents);
            groupSettings.RootCompletion = RootCompletionPolicy.Stop; // hosts keep their root result too
            long now = 0;
            var path = new List<uint>();
            using (var simulation = new TreeSimulation(settings))
            {
                var groups = new TreeGroup[2];
                var hosts = new ProductionTreeHost[2, Agents];
                var schedules = new ThinkSchedule[Agents];
                for (var tree = 0; tree < 2; tree++)
                {
                    Assert.That(simulation.TryAddGroup(trees[tree], groupSettings, out groups[tree]), Is.EqualTo(SimulationResult.Success));
                    for (var agent = 0; agent < Agents; agent++)
                    {
                        schedules[agent] = ThinkSchedule.Every((uint)(1 + agent % 3), (uint)(agent % 2));
                        Assert.That(groups[tree].TryAdd(schedules[agent], out _), Is.EqualTo(SimulationResult.Success));
                        hosts[tree, agent] = Host(trees[tree], () => now);
                    }
                }
                var completions = 0;
                for (uint tick = 1; tick <= 90; tick++)
                {
                    now = tick * (long)TickMicroseconds;
                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    for (var tree = 0; tree < 2; tree++)
                    {
                        completions += groups[tree].GetRootCompletions().Length;
                        for (var agent = 0; agent < Agents; agent++)
                        {
                            var host = hosts[tree, agent];
                            if (tick % schedules[agent].Interval == schedules[agent].Phase) Drive(host);
                            var core = groups[tree].CoreForTests(agent);
                            var expected = Observe(host.LastRootResult, host.LastFailure, host.TotalUpdates,
                                host.Machine, host.GeneratedDispatchAdapter, path);
                            var actual = Observe(core.LastRootResult, core.LastFailure, core.TotalUpdates,
                                core.Machine, core.GeneratedDispatch, path);
                            if (expected != actual)
                                Assert.Fail("tree " + tree + " agent " + agent + " differs from its host at tick " + tick
                                    + " (host updates " + host.TotalUpdates + ", root " + host.LastRootResult + ", failure " + host.LastFailure.Code
                                    + "; container updates " + core.TotalUpdates + ", root " + core.LastRootResult + ", failure " + core.LastFailure.Code + ")");
                        }
                    }
                }
                Assert.That(completions, Is.EqualTo(Agents), "Every errand agent completed once and stopped, like its host.");
                Assert.That(hosts[0, 0].LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
            }
        }

        /// <summary>
        /// The finding P10-009 was asked to establish: hosts create random-capable generated leaves with
        /// zeroed stream words (state 0, increment 0). time-and-random-v1 requires an odd increment
        /// derived from the root seed, so the dispatch owner refuses the stream and the node never rolls.
        /// The host behaviour is recorded here, unchanged.
        /// </summary>
        [Test]
        public void Hosts_HaveZeroedRandomStreams_AndARandomNodeCannotRun()
        {
            var coin = Program(CoinTree);
            long now = 0;
            var host = Host(coin, () => now);
            var root = coin.Program.Header.RootNodeIndex;
            Assert.That(host.GeneratedDispatchAdapter.TryReadRandomStream(root, out var state, out var increment), Is.True,
                "The random condition has a stream slot.");
            Assert.That(state, Is.Zero, "Host stream state is never seeded.");
            Assert.That(increment, Is.Zero, "Host stream increment is never seeded (even; the spec requires odd).");
            now = TickMicroseconds;
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("AIBT ProductionTreeHost: .*Callback rejected: InvalidEncoding"));
            Drive(host);
            TestContext.WriteLine("host after one update: root=" + host.LastRootResult + " failure=" + host.LastFailure.Code
                + " updates=" + host.TotalUpdates);
            Assert.That(host.LastRootResult, Is.Null, "The roll never produced Success or Failure.");
            Assert.That(host.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid),
                "The host stops: the dispatch owner rejects the stream (InvalidEncoding).");
        }

        [Test]
        public void ContainerStreams_FollowTimeAndRandomV1_AndRollBothWays()
        {
            const ulong Seed = 0xC0FFEEUL;
            var coin = Program(CoinTree);
            var hash = new NativeHash256V1(coin.Program.Header.CanonicalSemanticHash);
            var root = coin.Program.Header.RootNodeIndex;
            var settings = TreeSimulationSettings.Default(TickMicroseconds);
            settings.RandomSeed = Seed;
            settings.UseJobs = false;
            using (var simulation = new TreeSimulation(settings))
            {
                simulation.TryAddGroup(coin, TreeGroupSettings.Default(64), out var group);
                for (var agent = 0; agent < 64; agent++) group.TryAdd(ThinkSchedule.Every(1, 0), out _);
                for (var agent = 0; agent < 64; agent++)
                {
                    var core = group.CoreForTests(agent);
                    Assert.That(core.InstanceId, Is.EqualTo((ulong)agent + 1));
                    Assert.That(core.GeneratedDispatch.TryReadRandomStream(root, out var state, out var increment), Is.True);
                    Assert.That(NativeRandomStreamDerivationV1.TryDerive(Seed, hash, core.InstanceId, root, out var expected), Is.True);
                    Assert.That(state, Is.EqualTo(expected.State), "agent " + agent);
                    Assert.That(increment, Is.EqualTo(expected.Increment), "agent " + agent);
                    Assert.That(increment & 1ul, Is.EqualTo(1ul), "odd increment");
                }
                var successes = 0;
                var failures = 0;
                for (uint tick = 1; tick <= 4; tick++)
                {
                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    Assert.That(group.GetFaults().Length, Is.Zero);
                    var completions = group.GetRootCompletions();
                    Assert.That(completions.Length, Is.EqualTo(64), "Every agent rolls every tick and restarts.");
                    for (var index = 0; index < completions.Length; index++)
                        if (completions[index].Result == NodeStatus.Success) successes++;
                        else failures++;
                }
                Assert.That(successes, Is.GreaterThan(64));
                Assert.That(failures, Is.GreaterThan(64));
                // A restarted instance gets the next container instance id and that id's stream.
                Assert.That(group.CoreForTests(0).InstanceId, Is.EqualTo(64ul * 3 + 1));
            }
        }
    }
}
