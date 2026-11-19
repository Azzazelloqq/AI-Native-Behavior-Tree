using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Simulation;
using AIBT.Tests.Integration.NativeRuntime;
using NUnit.Framework;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// P11-002 stage 2 (ADR-P11-002 §7): the timing and signal nodes in TreeSimulation groups, where
    /// "now" is tick * <see cref="SimulationTestPrograms.TickMicroseconds"/> (33333 µs). Each tree runs
    /// as its own group of three agents (thinking every 1, 2 and 3 ticks), once in place and once with
    /// Jobs; every tick's digest (agent state, every Tree-scope byte, completions, faults and the
    /// aibt.native.signal commands) must be identical. Agent 0 thinks every tick; its signals,
    /// completions and final key values are checked against values computed from TickMicroseconds:
    /// deadlines before, exactly at and after a tick's time point, and the wait's timeout likewise.
    /// </summary>
    public sealed class NativeLibraryTimingSimulationTests
    {
        private const string S = "aibt.native.";
        private const int AgentsPerGroup = 3;
        private const uint Ticks = 24;
        private static readonly CommandChannel<int> Signal = CommandChannel<int>.Create("aibt.native.signal");

        private sealed class SimTree
        {
            internal string Name;
            internal string Json;
            internal bool EmitsSignals = true;
        }

        private sealed class Outcome
        {
            internal readonly List<ulong> Digests = new List<ulong>();
            internal readonly Dictionary<string, List<(uint Tick, int Agent, int Code)>> Signals = new Dictionary<string, List<(uint, int, int)>>();
            internal readonly Dictionary<string, List<(uint Tick, int Agent, NodeStatus Result)>> Completions = new Dictionary<string, List<(uint, int, NodeStatus)>>();
            internal readonly Dictionary<string, Dictionary<string, byte[]>> Agent0Values = new Dictionary<string, Dictionary<string, byte[]>>();
            internal int Faults;
            internal long JobBatches;
        }

        // Fires (set-deadline, count, signal 7) whenever the shared deadline has passed, then holds.
        // The v1 reactive composites re-check deadline-passed at every update.
        private static SimTree CooldownSignal(uint duration) => new SimTree
        {
            Name = "cooldown-signal-" + duration,
            Json = Tree("tests.p11002.sim.cooldown-" + duration, "root", new[] { Long("cd"), Int("n") },
                Composite("root", "aibt.core.reactive-selector", "fire", "rest"),
                Composite("fire", "aibt.core.reactive-sequence", "ready", "go"),
                Leaf("ready", S + "deadline-passed", null, "\"key\": \"cd\""),
                Composite("go", "aibt.core.memory-sequence", "start", "count", "say", "hold"),
                Leaf("start", S + "set-deadline", "\"duration-microseconds\": " + duration, "\"key\": \"cd\""),
                Leaf("count", S + "increment-int", "\"amount\": 1", "\"key\": \"n\""),
                Leaf("say", S + "signal", "\"code\": 7"),
                Leaf("hold", S + "idle"),
                Leaf("rest", S + "idle")),
        };

        private static SimTree WaitTimeout(uint timeout) => new SimTree
        {
            Name = "wait-timeout-" + timeout,
            EmitsSignals = false,
            Json = Tree("tests.p11002.sim.wait-timeout-" + timeout, "w", new[] { Bool("flag") },
                Leaf("w", S + "wait-until-true", "\"timeout-microseconds\": " + timeout, "\"key\": \"flag\"")),
        };

        private static readonly SimTree SignalBounds = new SimTree
        {
            Name = "signal-0-and-max",
            Json = Tree("tests.p11002.sim.signal-bounds", "seq", Array.Empty<string>(),
                Composite("seq", "aibt.core.memory-sequence", "zero", "max", "hold"),
                Leaf("zero", S + "signal", "\"code\": 0"),
                Leaf("max", S + "signal", "\"code\": 2147483647"),
                Leaf("hold", S + "idle")),
        };

        private static readonly SimTree SignalTooLarge = new SimTree
        {
            Name = "signal-too-large",
            Json = Tree("tests.p11002.sim.signal-too-large", "a", Array.Empty<string>(),
                Leaf("a", S + "signal", "\"code\": 4294967295")),
        };

        // The writer (wait 66666 µs, then set the flag) runs after the waiter in the parallel.
        private static readonly SimTree NodeWrite = new SimTree
        {
            Name = "wait-node-write",
            EmitsSignals = false,
            Json = Tree("tests.p11002.sim.wait-node-write", "both", new[] { Bool("flag"), Int("done") },
                CompositeWith("both", "aibt.core.parallel", "\"policy\": \"require-all-success\"", "waiter", "writer"),
                Composite("waiter", "aibt.core.memory-sequence", "wait", "mark"),
                Leaf("wait", S + "wait-until-true", "\"timeout-microseconds\": 0", "\"key\": \"flag\""),
                Leaf("mark", S + "set-int", "\"value\": 1", "\"key\": \"done\""),
                Composite("writer", "aibt.core.memory-sequence", "pause", "raise"),
                Leaf("pause", S + "wait-microseconds", "\"duration-microseconds\": 66666"),
                Leaf("raise", S + "set-bool", "\"value\": true", "\"key\": \"flag\"")),
        };

        private static readonly SimTree[] Trees =
        {
            CooldownSignal(100000), CooldownSignal(99999), CooldownSignal(99998),
            WaitTimeout(100000), WaitTimeout(99999),
            SignalBounds, SignalTooLarge, NodeWrite,
        };

        private Outcome Run(IReadOnlyList<NativeLibraryKit.Prepared> prepared, bool useJobs)
        {
            var outcome = new Outcome();
            var settings = TreeSimulationSettings.Default(TickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = 1;
            var path = new List<uint>();
            using (var simulation = new TreeSimulation(settings))
            {
                var groups = new TreeGroup[prepared.Count];
                for (var index = 0; index < prepared.Count; index++)
                {
                    var name = Trees[index].Name;
                    Assert.That(simulation.TryAddGroup(prepared[index].Program, TreeGroupSettings.Default(AgentsPerGroup), out groups[index]),
                        Is.EqualTo(SimulationResult.Success), name);
                    for (var agent = 0; agent < AgentsPerGroup; agent++)
                        Assert.That(groups[index].TryAdd(ThinkSchedule.Every((uint)(1 + agent), 0u), out _), Is.EqualTo(SimulationResult.Success));
                    outcome.Signals[name] = new List<(uint, int, int)>();
                    outcome.Completions[name] = new List<(uint, int, NodeStatus)>();
                }
                for (uint tick = 1; tick <= Ticks; tick++)
                {
                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    var digest = Fnv.Create();
                    for (var index = 0; index < groups.Length; index++)
                    {
                        var name = Trees[index].Name;
                        AppendDigest(groups[index], ref digest, path);
                        var completions = groups[index].GetRootCompletions();
                        for (var item = 0; item < completions.Length; item++)
                            outcome.Completions[name].Add((tick, completions[item].Agent.Index, completions[item].Result));
                        outcome.Faults += groups[index].GetFaults().Length;

                        var result = groups[index].TryGetCommands(Signal, out var commands);
                        if (!Trees[index].EmitsSignals)
                        {
                            Assert.That(result, Is.EqualTo(SimulationResult.UnknownChannel),
                                name + ": a tree without aibt.native.signal has no such channel");
                            continue;
                        }
                        Assert.That(result, Is.EqualTo(SimulationResult.Success), name);
                        digest.Add((ulong)commands.Length);
                        for (var item = 0; item < commands.Length; item++)
                        {
                            digest.Add((ulong)commands[item].Agent.Index);
                            digest.Add((ulong)(uint)commands[item].Value);
                            outcome.Signals[name].Add((tick, commands[item].Agent.Index, commands[item].Value));
                        }
                    }
                    outcome.Digests.Add(digest.Value);
                }
                for (var index = 0; index < groups.Length; index++)
                {
                    var values = new Dictionary<string, byte[]>();
                    var adapter = groups[index].CoreForTests(0).GeneratedDispatch;
                    foreach (var slot in prepared[index].Slots)
                    {
                        var bytes = new byte[slot.Value.DefaultBytes.Length];
                        for (var at = 0; at < bytes.Length; at++)
                            Assert.That(adapter.TryReadTreeValueByte(slot.Value.Offset + (uint)at, out bytes[at]), Is.True, slot.Key);
                        values[slot.Key] = bytes;
                    }
                    outcome.Agent0Values[Trees[index].Name] = values;
                }
                outcome.JobBatches = groups.Sum(group => group.JobBatchesForTests);
            }
            return outcome;
        }

        [Test]
        public void TimingAndSignalTrees_GiveIdenticalPerTickDigestsInPlaceAndWithJobs_AndTheTickTimeResults()
        {
            using (var kit = new NativeLibraryKit())
            {
                var prepared = Trees.Select(tree => kit.Prepare(tree.Json)).ToArray();
                var inPlace = Run(prepared, useJobs: false);
                var jobs = Run(prepared, useJobs: true);

                Assert.That(inPlace.Faults, Is.Zero, "No agent faulted.");
                Assert.That(jobs.Faults, Is.Zero, "No agent faulted with Jobs.");
                Assert.That(inPlace.JobBatches, Is.Zero, "UseJobs=false runs on the calling thread.");
                Assert.That(jobs.JobBatches, Is.GreaterThanOrEqualTo(Ticks), "UseJobs with MinimumJobBatch=1 runs the job.");
                for (var tick = 0; tick < inPlace.Digests.Count; tick++)
                    Assert.That(jobs.Digests[tick], Is.EqualTo(inPlace.Digests[tick]), "Jobs digest differs from in place at tick " + (tick + 1));
                foreach (var tree in Trees)
                {
                    Assert.That(jobs.Signals[tree.Name], Is.EqualTo(inPlace.Signals[tree.Name]), tree.Name + ": signals with Jobs");
                    Assert.That(jobs.Completions[tree.Name], Is.EqualTo(inPlace.Completions[tree.Name]), tree.Name + ": completions with Jobs");
                }

                // Deadlines against tick time. Agent 0 first fires at tick 1 (now 33333) and stores
                // 33333 + duration. 99999 = 3 ticks: the deadline is exactly tick 4's time point, so it
                // fires at tick 4. 100000: tick 4 is 1 µs before the deadline, so it fires at tick 5.
                // 99998: tick 4 is 1 µs after the deadline.
                AssertCooldown(inPlace, 100000, new uint[] { 1, 5, 9, 13, 17, 21 });
                AssertCooldown(inPlace, 99999, new uint[] { 1, 4, 7, 10, 13, 16, 19, 22 });
                AssertCooldown(inPlace, 99998, new uint[] { 1, 4, 7, 10, 13, 16, 19, 22 });

                // The wait's timeout against tick time. Started at tick 1; 99999 is reached exactly at
                // tick 4. 100000 is not reached at tick 4 (99999 µs) and is passed at tick 5. After each
                // Failure the agent restarts as a fresh instance at its next think.
                Assert.That(Agent0(inPlace.Completions["wait-timeout-99999"]), Is.EqualTo(new[]
                {
                    (4u, NodeStatus.Failure), (8u, NodeStatus.Failure), (12u, NodeStatus.Failure),
                    (16u, NodeStatus.Failure), (20u, NodeStatus.Failure), (24u, NodeStatus.Failure),
                }));
                Assert.That(Agent0(inPlace.Completions["wait-timeout-100000"]), Is.EqualTo(new[]
                {
                    (5u, NodeStatus.Failure), (10u, NodeStatus.Failure), (15u, NodeStatus.Failure), (20u, NodeStatus.Failure),
                }));

                // Signals: 0 and 2147483647 once each at each agent's first think; nothing above 2^31-1.
                var bounds = inPlace.Signals["signal-0-and-max"];
                Assert.That(bounds, Is.EqualTo(new[] { (1u, 0, 0), (1u, 0, int.MaxValue), (2u, 1, 0), (2u, 1, int.MaxValue), (3u, 2, 0), (3u, 2, int.MaxValue) }));
                Assert.That(inPlace.Signals["signal-too-large"], Is.Empty, "A code above 2147483647 emits nothing.");
                Assert.That(inPlace.Completions["signal-too-large"].Select(c => c.Result), Is.Not.Empty.And.All.EqualTo(NodeStatus.Failure));

                // The waited key is written by a node: the writer sets it at tick 3 (now 99999 =
                // 33333 + 66666); the waiter, visited before the writer, sees it at tick 4.
                Assert.That(Agent0(inPlace.Completions["wait-node-write"]).First(), Is.EqualTo((4u, NodeStatus.Success)));
            }
        }

        private static void AssertCooldown(Outcome outcome, uint duration, uint[] fireTicks)
        {
            var name = "cooldown-signal-" + duration;
            var agent0 = outcome.Signals[name].Where(s => s.Agent == 0).ToArray();
            Assert.That(agent0.Select(s => s.Tick), Is.EqualTo(fireTicks), name + ": the ticks agent 0 fires at, one signal each");
            Assert.That(agent0.Select(s => s.Code), Is.All.EqualTo(7), name);
            var values = outcome.Agent0Values[name];
            Assert.That(BitConverter.ToInt32(values["n"], 0), Is.EqualTo(fireTicks.Length), name + ": n counts the firings");
            Assert.That(BitConverter.ToInt64(values["cd"], 0), Is.EqualTo((long)fireTicks.Last() * TickMicroseconds + duration),
                name + ": cd = the last firing's time + duration");
            Assert.That(outcome.Completions[name], Is.Empty, name + " keeps running");
        }

        private static (uint, NodeStatus)[] Agent0(List<(uint Tick, int Agent, NodeStatus Result)> completions)
            => completions.Where(c => c.Agent == 0).Select(c => (c.Tick, c.Result)).ToArray();
    }
}
