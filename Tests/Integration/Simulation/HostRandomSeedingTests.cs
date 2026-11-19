using System.Collections.Generic;
using System.Reflection;
using AIBT.Simulation;
using NUnit.Framework;
using UnityEngine;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// P11-002 stage 3 (ADR-P11-002 §7, owner decision 4): <see cref="ProductionTreeHost"/> and
    /// <see cref="NativeTreeAgent"/> seed the random stream of every node that declares one, from a root
    /// seed (a bootstrap argument / <see cref="NativeTreeAgent.RandomSeed"/>, default 0), the program's
    /// semantic hash, the host's tree instance id and the runtime node index — the derivation
    /// <see cref="TreeGroup"/> uses. The random trees are the container's own P10-009 trees
    /// (<c>aibt.stdlib.random-condition</c>, compiled without the deterministic-node policy).
    /// </summary>
    public sealed class HostRandomSeedingTests
    {
        private static readonly FieldInfo HostInstanceCounter =
            typeof(ProductionTreeHost).GetField("s_nextTreeInstanceId", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo ContainerInstanceCounter =
            typeof(TreeSimulation).GetField("_nextTreeInstanceId", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<Object> _objects = new List<Object>();
        private readonly List<ProductionTreeProgram> _programs = new List<ProductionTreeProgram>();

        [TearDown]
        public void TearDown()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
                if (_objects[index] is GameObject gameObject) DestroyHostObject(gameObject);
            foreach (var program in _programs) program.Dispose();
            _programs.Clear();
            foreach (var item in _objects) if (item != null) Object.DestroyImmediate(item);
            _objects.Clear();
        }

        private static void DestroyHostObject(GameObject gameObject)
        {
            if (gameObject == null) return;
            // EditMode sends no OnDestroy to a plain MonoBehaviour: release the host as Unity would.
            var host = gameObject.GetComponent<ProductionTreeHost>();
            if (host != null)
                typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
            Object.DestroyImmediate(gameObject);
        }

        private ProductionTreeProgram Program(string json)
        {
            var asset = PrepareAsset(json);
            _objects.Add(asset);
            var program = CreateProgram(asset);
            _programs.Add(program);
            return program;
        }

        private ProductionTreeHost Host(ProductionTreeProgram program, ulong seed, System.Func<long> clock)
        {
            var gameObject = new GameObject("AIBT.Tests.P11002.SeededHost");
            _objects.Add(gameObject);
            var host = gameObject.AddComponent<ProductionTreeHost>();
            Assert.That(program.TryBootstrap(host, seed, out var failure, clock: clock), Is.True, failure.ToString());
            return host;
        }

        private static void Drive(ProductionTreeHost host)
        {
            host.DriveOneUpdate();
            Assert.That(host.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None), "The host must not fail.");
        }

        /// <summary>Every node of <paramref name="program"/> that has a stream slot on <paramref name="adapter"/>.</summary>
        private static List<uint> RandomNodes(ProductionTreeProgram program, GeneratedTreeDispatchAdapterV2 adapter)
        {
            var nodes = new List<uint>();
            for (uint node = 0; node < program.Program.Nodes.Count; node++)
                if (adapter.TryReadRandomStream(node, out _, out _)) nodes.Add(node);
            return nodes;
        }

        private static void AssertDerived(ProductionTreeProgram program, GeneratedTreeDispatchAdapterV2 adapter, ulong seed, ulong instanceId, string what)
        {
            var hash = new NativeHash256V1(program.Program.Header.CanonicalSemanticHash);
            var nodes = RandomNodes(program, adapter);
            Assert.That(nodes, Is.Not.Empty, what + ": the tree has a random node");
            foreach (var node in nodes)
            {
                Assert.That(adapter.TryReadRandomStream(node, out var state, out var increment), Is.True);
                Assert.That(NativeRandomStreamDerivationV1.TryDerive(seed, hash, instanceId, node, out var expected), Is.True);
                Assert.That(state, Is.EqualTo(expected.State), what + ": state of node " + node);
                Assert.That(increment, Is.EqualTo(expected.Increment), what + ": increment of node " + node);
            }
        }

        /// <summary>How many values the stream of <paramref name="node"/> has given since it was derived (searches up to 100 000).</summary>
        private static int DrawsSinceDerivation(ProductionTreeProgram program, GeneratedTreeDispatchAdapterV2 adapter, ulong seed, ulong instanceId, uint node)
        {
            Assert.That(adapter.TryReadRandomStream(node, out var state, out _), Is.True);
            Assert.That(NativeRandomStreamDerivationV1.TryDerive(seed, new NativeHash256V1(program.Program.Header.CanonicalSemanticHash),
                instanceId, node, out var stream), Is.True);
            var walk = stream.State;
            for (var draws = 0; draws <= 100000; draws++)
            {
                if (walk == state) return draws;
                NativeRandomStreamDerivationV1.NextUInt32(ref walk, stream.Increment);
            }
            Assert.Fail("The committed state is not on the derived stream of node " + node + ".");
            return -1;
        }

        [TestCase(0UL)]
        [TestCase(1UL)]
        [TestCase(0xC0FFEEUL)]
        [TestCase(ulong.MaxValue)]
        public void HostStreamWords_EqualTheDerivation_ForTheSeedPassedAtBootstrap(ulong seed)
        {
            foreach (var json in new[] { CoinTree, RollerTree, ErrandTree })
            {
                var program = Program(json);
                var host = Host(program, seed, () => 0L);
                Assert.That(host.RandomSeed, Is.EqualTo(seed));
                AssertDerived(program, host.GeneratedDispatchAdapter, seed, host.InstanceId, "seed " + seed);
            }
        }

        [Test]
        public void TheSeedlessBootstrap_UsesTheDefaultSeedZero()
        {
            var program = Program(RollerTree);
            var gameObject = new GameObject("AIBT.Tests.P11002.DefaultSeedHost");
            _objects.Add(gameObject);
            var host = gameObject.AddComponent<ProductionTreeHost>();
            Assert.That(program.TryBootstrap(host, out var failure, clock: () => 0L), Is.True, failure.ToString());
            Assert.That(ProductionTreeHost.DefaultRandomSeed, Is.EqualTo(0UL));
            Assert.That(host.RandomSeed, Is.EqualTo(0UL));
            AssertDerived(program, host.GeneratedDispatchAdapter, 0UL, host.InstanceId, "default seed");
        }

        /// <summary>
        /// Same seed, two hosts: two tree instance ids, so two different streams; each host's committed
        /// state stays on its own derived stream while it rolls. Another seed gives other streams again.
        /// </summary>
        [Test]
        public void HostsWithOneSeed_DrawDifferentSequences_AnotherSeedDiffersToo_AndEachStaysOnItsStream()
        {
            const ulong Seed = 0x5EED5UL;
            var program = Program(RollerTree);
            long now = 0;
            var first = Host(program, Seed, () => now);
            var second = Host(program, Seed, () => now);
            var other = Host(program, Seed + 1, () => now);
            Assert.That(second.InstanceId, Is.Not.EqualTo(first.InstanceId));
            var node = RandomNodes(program, first.GeneratedDispatchAdapter)[0];

            first.GeneratedDispatchAdapter.TryReadRandomStream(node, out var firstState, out var firstIncrement);
            second.GeneratedDispatchAdapter.TryReadRandomStream(node, out var secondState, out var secondIncrement);
            other.GeneratedDispatchAdapter.TryReadRandomStream(node, out var otherState, out var otherIncrement);
            var a = Draws(firstState, firstIncrement, 32);
            var b = Draws(secondState, secondIncrement, 32);
            var c = Draws(otherState, otherIncrement, 32);
            Assert.That(b, Is.Not.EqualTo(a), "One seed, two instances: different sequences.");
            Assert.That(c, Is.Not.EqualTo(a), "Another seed: a different sequence.");

            var lastDraws = 0;
            for (var update = 1; update <= 120; update++)
            {
                now = update * (long)TickMicroseconds;
                Drive(first);
                Drive(second);
                Drive(other);
                var draws = DrawsSinceDerivation(program, first.GeneratedDispatchAdapter, Seed, first.InstanceId, node);
                Assert.That(draws, Is.GreaterThanOrEqualTo(lastDraws), "The stream only moves forward.");
                lastDraws = draws;
                DrawsSinceDerivation(program, second.GeneratedDispatchAdapter, Seed, second.InstanceId, node);
                DrawsSinceDerivation(program, other.GeneratedDispatchAdapter, Seed + 1, other.InstanceId, node);
            }
            Assert.That(lastDraws, Is.GreaterThan(5), "The roller rolled several times in 120 updates.");
        }

        private static uint[] Draws(ulong state, ulong increment, int count)
        {
            var values = new uint[count];
            for (var index = 0; index < count; index++) values[index] = NativeRandomStreamDerivationV1.NextUInt32(ref state, increment);
            return values;
        }

        /// <summary>
        /// Reproducibility across runs: a host's tree instance id is the process-wide bootstrap counter,
        /// which a domain reload resets. Emulating that (the counter set back to one value before each
        /// run; the first run's host is destroyed first, then the counter is restored), two runs with one
        /// seed give the same results and the same stream words update by update.
        /// </summary>
        [Test]
        public void TwoRunsWithTheSameSeedAndInstanceId_GiveTheSameResults()
        {
            const ulong Seed = 0xA1B7UL;
            var program = Program(RollerTree);
            var path = new List<uint>();
            var saved = (long)HostInstanceCounter.GetValue(null);
            var runs = new List<ulong>[2];
            ulong lastId = 0;
            try
            {
                for (var run = 0; run < 2; run++)
                {
                    HostInstanceCounter.SetValue(null, saved);
                    long now = 0;
                    var gameObject = new GameObject("AIBT.Tests.P11002.Rerun" + run);
                    try
                    {
                        var host = gameObject.AddComponent<ProductionTreeHost>();
                        Assert.That(program.TryBootstrap(host, Seed, out var failure, clock: () => now), Is.True, failure.ToString());
                        lastId = host.InstanceId;
                        runs[run] = new List<ulong>();
                        for (var update = 1; update <= 90; update++)
                        {
                            now = update * (long)TickMicroseconds;
                            Drive(host);
                            var digest = Fnv.Create();
                            AppendHost(host, ref digest, path);
                            runs[run].Add(digest.Value);
                        }
                    }
                    finally
                    {
                        DestroyHostObject(gameObject);
                    }
                }
            }
            finally
            {
                if ((long)HostInstanceCounter.GetValue(null) < (long)lastId) HostInstanceCounter.SetValue(null, (long)lastId);
            }
            Assert.That(runs[1], Is.EqualTo(runs[0]), "Same seed and same instance id: the same update-by-update results.");
        }

        /// <summary>Root, failure, update count, active path, every Tree byte and every stream word.</summary>
        private static void AppendHost(ProductionTreeHost host, ref Fnv digest, List<uint> path)
            => AppendObservation(host.LastRootResult, host.LastFailure, host.TotalUpdates, host.Machine, host.GeneratedDispatchAdapter,
                host.Program.Nodes.Count, ref digest, path);

        private static void AppendObservation(NodeStatus? root, NativeRuntimeFailureV1 failure, ulong updates,
            NativeLifecycleMachineV1 machine, GeneratedTreeDispatchAdapterV2 adapter, int nodeCount, ref Fnv digest, List<uint> path)
        {
            digest.Add(updates);
            digest.Add(root.HasValue ? 1ul + (ulong)root.Value : 0ul);
            digest.Add((ulong)failure.Code);
            machine.CopyActivePath(path);
            digest.Add((ulong)path.Count);
            for (var node = 0; node < path.Count; node++) digest.Add(path[node]);
            for (uint offset = 0; adapter.TryReadTreeValueByte(offset, out var value); offset++) digest.Add(value);
            for (uint node = 0; node < nodeCount; node++)
                if (adapter.TryReadRandomStream(node, out var state, out var increment))
                {
                    digest.Add(state);
                    digest.Add(increment);
                }
        }

        /// <summary>
        /// ADR-P11-002 §7 stage 3: a host with seed S and instance id I gives the same node results as a
        /// TreeSimulation agent with the same derivation inputs. The container's instance ids are
        /// container-local (1, 2, ...), the host's process-wide, so the container's next id is set to I
        /// before the agent is added; the container seeds it with S. Both run the same clock, in place and
        /// with Jobs; the root completion policy is Stop (a host keeps its root result).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AHostAndAContainerAgentWithTheSameDerivationInputs_GiveTheSameResults(bool useJobs)
        {
            foreach (var json in new[] { RollerTree, ErrandTree }) AssertHostMatchesContainer(json, useJobs);
        }

        /// <summary>
        /// P11-002 stage 3 Part C: the same for the five library random nodes (aibt.native.random-*,
        /// wait-random-microseconds) in one loop.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AHostAndAContainerAgent_RunTheFiveLibraryRandomNodesAlike(bool useJobs)
            => AssertHostMatchesContainer(RandomLibraryTree, useJobs);

        /// <summary>
        /// P11-002 stage 3 Part C: TreeSimulation runs the five library random nodes in place and with
        /// Jobs with the same per-tick digest (every agent's state, Tree bytes, root completions, faults
        /// and every stream word), for 16 agents of one seed over 240 ticks; every agent draws.
        /// </summary>
        [Test]
        public void TheFiveLibraryRandomNodes_GiveTheSamePerTickDigestsInPlaceAndWithJobs()
        {
            const ulong Seed = 0x11B7UL;
            const int Agents = 16;
            const uint Ticks = 240;
            var program = Program(RandomLibraryTree);
            var runs = new List<ulong>[2];
            var path = new List<uint>();
            for (var run = 0; run < 2; run++)
            {
                var settings = TreeSimulationSettings.Default(TickMicroseconds);
                settings.RandomSeed = Seed;
                settings.UseJobs = run == 1;
                settings.MinimumJobBatch = 1;
                runs[run] = new List<ulong>();
                using (var simulation = new TreeSimulation(settings))
                {
                    Assert.That(simulation.TryAddGroup(program, TreeGroupSettings.Default(Agents), out var group), Is.EqualTo(SimulationResult.Success));
                    for (var agent = 0; agent < Agents; agent++)
                        Assert.That(group.TryAdd(ThinkSchedule.Every(1, 0), out _), Is.EqualTo(SimulationResult.Success));
                    for (uint tick = 1; tick <= Ticks; tick++)
                    {
                        Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                        Assert.That(group.GetFaults().Length, Is.Zero, "tick " + tick);
                        var digest = Fnv.Create();
                        AppendDigest(group, ref digest, path);
                        for (var agent = 0; agent < Agents; agent++)
                        {
                            var adapter = group.CoreForTests(agent).GeneratedDispatch;
                            foreach (var node in RandomNodes(program, adapter))
                            {
                                Assert.That(adapter.TryReadRandomStream(node, out var state, out var increment), Is.True);
                                digest.Add(state);
                                digest.Add(increment);
                            }
                        }
                        runs[run].Add(digest.Value);
                    }
                    for (var agent = 0; agent < Agents; agent++)
                    {
                        var core = group.CoreForTests(agent);
                        foreach (var node in RandomNodes(program, core.GeneratedDispatch))
                            Assert.That(DrawsSinceDerivation(program, core.GeneratedDispatch, Seed, core.InstanceId, node), Is.GreaterThan(5),
                                "run " + run + ", agent " + agent + ", node " + node + " drew on its own derived stream");
                    }
                }
            }
            Assert.That(runs[1], Is.EqualTo(runs[0]), "In place and with Jobs: the same per-tick digests.");
        }

        private void AssertHostMatchesContainer(string json, bool useJobs)
        {
            const ulong Seed = 0xD1CEUL;
            var path = new List<uint>();
            {
                var program = Program(json);
                long now = 0;
                var host = Host(program, Seed, () => now);
                var settings = TreeSimulationSettings.Default(TickMicroseconds);
                settings.RandomSeed = Seed;
                settings.UseJobs = useJobs;
                settings.MinimumJobBatch = 1;
                var groupSettings = TreeGroupSettings.Default(1);
                groupSettings.RootCompletion = RootCompletionPolicy.Stop;
                using (var simulation = new TreeSimulation(settings))
                {
                    Assert.That(simulation.TryAddGroup(program, groupSettings, out var group), Is.EqualTo(SimulationResult.Success));
                    ContainerInstanceCounter.SetValue(simulation, host.InstanceId - 1);
                    Assert.That(group.TryAdd(ThinkSchedule.Every(1, 0), out _), Is.EqualTo(SimulationResult.Success));
                    var core = group.CoreForTests(0);
                    Assert.That(core.InstanceId, Is.EqualTo(host.InstanceId), "Same tree instance id.");
                    var rolled = false;
                    for (uint tick = 1; tick <= 90; tick++)
                    {
                        now = tick * (long)TickMicroseconds;
                        Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                        host.DriveOneUpdate();
                        var expected = Fnv.Create();
                        AppendHost(host, ref expected, path);
                        var actual = Fnv.Create();
                        AppendObservation(core.LastRootResult, core.LastFailure, core.TotalUpdates, core.Machine, core.GeneratedDispatch,
                            program.Program.Nodes.Count, ref actual, path);
                        Assert.That(actual.Value, Is.EqualTo(expected.Value), json.Substring(60, 40) + " differs at tick " + tick
                            + " (host root " + host.LastRootResult + ", failure " + host.LastFailure.Code
                            + "; container root " + core.LastRootResult + ", failure " + core.LastFailure.Code + ")");
                        var node = RandomNodes(program, host.GeneratedDispatchAdapter)[0];
                        if (DrawsSinceDerivation(program, host.GeneratedDispatchAdapter, Seed, host.InstanceId, node) > 0) rolled = true;
                    }
                    Assert.That(rolled, Is.True, "The tree rolled at least once.");
                    Assert.That(host.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
                }
            }
        }
    }
}
