using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using AIBT.Simulation;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP10010;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// P10-010 (ADR-P10-008 stage G2): game inputs bound to a group and read by Burst nodes through
    /// <c>SnapshotReadHandle</c>, commands emitted through <c>CommandHandle</c> and collected per step,
    /// opt-in wake on changed input. EditMode, no GameObject on the container side. Proof table:
    /// <c>Planning~/Evidence/P10-010/README.md</c>.
    /// </summary>
    public sealed class TreeSimulationIoTests
    {
        private const ulong Seed = 0x10_0102UL;
        private static readonly InputChannel<SenseInput> Sense = InputChannel<SenseInput>.Create("sense");
        private static readonly InputChannel<float> Speed = InputChannel<float>.Create("speed");
        private static readonly CommandChannel<OrderCommand> Order = CommandChannel<OrderCommand>.Create("order");
        private static readonly CommandChannel<int> Ping = CommandChannel<int>.Create("ping");

        // Repeats: a reactive selector prefers "alarm" (threat >= 0.5, then an order) over a long
        // idle wait, then holds for one update. One order per think at most while the threat stays high;
        // a rising threat preempts the idle wait at the agent's next think.
        internal const string GuardTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10010.guard"", ""name"": ""P10-010 guard"",
  ""root"": ""loop"",
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""cycle""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""cycle"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""top"", ""hold""] },
    ""top"": { ""type"": ""aibt.core.reactive-selector"", ""typeVersion"": 1, ""children"": [""alarm"", ""idle""] },
    ""alarm"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""threat"", ""order""] },
    ""threat"": { ""type"": ""aibt.tests.p10010.threat-at-least"", ""typeVersion"": 1, ""parameters"": { ""threshold"": 0.5 } },
    ""order"": { ""type"": ""aibt.tests.p10010.give-order"", ""typeVersion"": 1, ""parameters"": { ""scale"": 2.0 } },
    ""idle"": { ""type"": ""aibt.native.wait-microseconds"", ""typeVersion"": 1, ""parameters"": { ""duration-microseconds"": 1000000000 } },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } }
  }
}";

        // Repeats: hold, then while the sensed speed is above 1 give an order (a second input channel).
        internal const string ScoutTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10010.scout"", ""name"": ""P10-010 scout"",
  ""root"": ""loop"",
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""cycle""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""cycle"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""hold"", ""fast"", ""order""] },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""fast"": { ""type"": ""aibt.tests.p10010.speed-above"", ""typeVersion"": 1, ""parameters"": { ""threshold"": 1.0 } },
    ""order"": { ""type"": ""aibt.tests.p10010.give-order"", ""typeVersion"": 1, ""parameters"": { ""scale"": 1.0 } }
  }
}";

        // Two orders in one think: needs a command quota of 2.
        internal const string DoubleOrderTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10010.double"", ""name"": ""P10-010 double order"",
  ""root"": ""both"",
  ""nodes"": {
    ""both"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""first"", ""second""] },
    ""first"": { ""type"": ""aibt.tests.p10010.give-order"", ""typeVersion"": 1, ""parameters"": { ""scale"": 1.0 } },
    ""second"": { ""type"": ""aibt.tests.p10010.give-order"", ""typeVersion"": 1, ""parameters"": { ""scale"": 3.0 } }
  }
}";

        // Commands only (no input): a fixed order, then Success.
        internal const string FixedOrderTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10010.fixed"", ""name"": ""P10-010 fixed order"",
  ""root"": ""order"",
  ""nodes"": {
    ""order"": { ""type"": ""aibt.tests.p10010.fixed-order"", ""typeVersion"": 1, ""parameters"": { ""strength"": 1.5 } }
  }
}";

        internal const string ExternalTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10010.external"", ""name"": ""P10-010 external action"",
  ""root"": ""act"",
  ""nodes"": {
    ""act"": { ""type"": ""aibt.tests.p10010.external-action"", ""typeVersion"": 1 }
  }
}";

        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        private readonly List<ProductionTreeProgram> _programs = new List<ProductionTreeProgram>();

        [TearDown]
        public void TearDown()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
                if (_objects[index] is GameObject gameObject)
                {
                    var host = gameObject.GetComponent<ProductionTreeHost>();
                    if (host != null)
                        typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            foreach (var program in _programs) program.Dispose();
            _programs.Clear();
            foreach (var item in _objects) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            _objects.Clear();
        }

        // ---- programs ----------------------------------------------------------------------------

        private static GeneratedShardMetadataArtifact Materialize(Type shard)
        {
            var metadata = shard.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public);
            object Field(string name) => metadata.GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return GeneratedShardMetadataMaterializer.MaterializeArtifact(
                (string)Field("ShardId"), (uint)Field("ShardVersion"), (string)Field("CanonicalDescriptorJson"),
                (string)Field("DescriptorHash"), (string)Field("ManifestRegistryJson"), (string)Field("NodeRegistryHash"));
        }

        internal static ProductionTreeAsset PrepareIoAsset(string json)
        {
            var standard = Materialize(typeof(StandardNativeShard));
            var io = Materialize(typeof(SimulationIoShard));
            var nodes = Materialize(typeof(BuiltInLeafShard)).Nodes.Concat(standard.Nodes).Concat(io.Nodes).ToArray();
            var registered = new RegisteredBlackboardTypeCatalog(standard.RegisteredTypes.Entries.Concat(io.RegisteredTypes.Entries));
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P10010/tree.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, registered,
                new ReferenceCompilerOptions("Tests/P10010/tree.aibt.json", new ReferenceCompilationPolicy(requireDeterministicNodes: false), new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            return ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(registered), SimulationIoCatalog.Fingerprint);
        }

        private ProductionTreeProgram Program(string json, ProductionTreeCatalogFactory factory = null)
        {
            var asset = PrepareIoAsset(json);
            _objects.Add(asset);
            Assert.That(asset.TryCreateProgram(factory ?? SimulationIoCatalog.TryCreateRuntimeCatalog, out var program, out var failure),
                Is.True, failure.ToString());
            _programs.Add(program);
            return program;
        }

        private static TreeSimulationSettings Settings(bool useJobs, int minimumJobBatch)
        {
            var settings = TreeSimulationSettings.Default(TickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = minimumJobBatch;
            settings.RandomSeed = Seed;
            return settings;
        }

        private static uint Mix(uint tick, int slot, uint salt)
        {
            unchecked
            {
                var h = tick * 0x9E3779B1u ^ (uint)slot * 0x85EBCA77u ^ salt * 0xC2B2AE3Du;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                return h;
            }
        }

        /// <summary>The game's rows for one tick: deterministic per (tick, slot); every 23rd threat is -0.0.</summary>
        private static void WriteInputs(uint tick, NativeArray<SenseInput> sense, NativeArray<float> speed)
        {
            for (var slot = 0; slot < sense.Length; slot++)
            {
                var h = Mix(tick / 3, slot, 1);
                sense[slot] = new SenseInput
                {
                    Threat = (h + slot) % 23 == 0 ? -0f : (h % 100) / 100f,
                    Ally = (int)(h >> 8) % 1000,
                    Flag = (h & 1) != 0,
                };
            }
            for (var slot = 0; slot < speed.Length; slot++)
                speed[slot] = (Mix(tick / 2, slot, 2) % 300) / 100f;
        }

        private static void AppendCommands(TreeGroup group, ref Fnv digest, ref int orders, ref int pings)
        {
            Assert.That(group.TryGetCommands(Order, out var orderCommands), Is.EqualTo(SimulationResult.Success));
            digest.Add((ulong)orderCommands.Length);
            for (var index = 0; index < orderCommands.Length; index++)
            {
                var command = orderCommands[index];
                digest.Add((ulong)command.Agent.Index);
                digest.Add(command.Agent.Generation);
                digest.Add(Bits(command.Value.Strength));
                digest.Add((ulong)(uint)command.Value.Target);
                digest.Add(command.Value.Urgent ? 1ul : 0ul);
                digest.Add(Bits(command.Value.Heading.X));
                digest.Add(Bits(command.Value.Heading.Y));
            }
            orders += orderCommands.Length;
            Assert.That(group.TryGetCommands(Ping, out var pingCommands), Is.EqualTo(SimulationResult.Success));
            digest.Add((ulong)pingCommands.Length);
            for (var index = 0; index < pingCommands.Length; index++)
            {
                digest.Add((ulong)pingCommands[index].Agent.Index);
                digest.Add((ulong)(uint)pingCommands[index].Value);
            }
            pings += pingCommands.Length;
            var faults = group.GetFaults();
            for (var index = 0; index < faults.Length; index++) digest.Add((ulong)faults[index].Cause);
        }

        private static ulong Bits(float value) => (ulong)(uint)BitConverter.SingleToInt32Bits(value);

        // ---- population, determinism ----------------------------------------------------------------

        private const int Agents = 260;
        private const int Ticks = 300;

        private sealed class Outcome
        {
            internal readonly List<ulong> Digests = new List<ulong>();
            internal int Orders;
            internal int Pings;
            internal int Faults;
            internal long JobBatches;
            internal int Woken;
        }

        /// <summary>
        /// 2 × 260 agents (guard Spread(5) with wake on changed "sense", scout Spread(3)), 300 ticks,
        /// both input channels written every tick, 20 agents removed and re-added per group at ticks
        /// 60/61 and 180/181. Each tick's digest covers every agent's state, the commands of both
        /// channels with their agents, and the faults with their causes.
        /// </summary>
        private Outcome RunPopulation(ProductionTreeProgram guard, ProductionTreeProgram scout, in TreeSimulationSettings settings)
        {
            var outcome = new Outcome();
            var path = new List<uint>();
            using (var simulation = new TreeSimulation(settings))
            using (var guardSense = new NativeArray<SenseInput>(Agents, Allocator.Persistent))
            using (var scoutSense = new NativeArray<SenseInput>(Agents, Allocator.Persistent))
            using (var scoutSpeed = new NativeArray<float>(Agents, Allocator.Persistent))
            using (var unusedSpeed = new NativeArray<float>(0, Allocator.Persistent))
            {
                Assert.That(simulation.TryAddGroup(guard, TreeGroupSettings.Default(Agents), out var guards), Is.EqualTo(SimulationResult.Success));
                Assert.That(simulation.TryAddGroup(scout, TreeGroupSettings.Default(Agents), out var scouts), Is.EqualTo(SimulationResult.Success));
                Assert.That(guards.TryBindInput(Sense, guardSense), Is.EqualTo(SimulationResult.Success));
                Assert.That(scouts.TryBindInput(Sense, scoutSense), Is.EqualTo(SimulationResult.Success));
                Assert.That(scouts.TryBindInput(Speed, scoutSpeed), Is.EqualTo(SimulationResult.Success));
                Assert.That(guards.TrySetWakeOnChange(Sense, true), Is.EqualTo(SimulationResult.Success));
                var groups = new[] { guards, scouts };
                var handles = new[] { new AgentHandle[Agents], new AgentHandle[Agents] };
                for (var index = 0; index < Agents; index++)
                {
                    Assert.That(guards.TryAdd(ThinkSchedule.Spread(5), out handles[0][index]), Is.EqualTo(SimulationResult.Success));
                    Assert.That(scouts.TryAdd(ThinkSchedule.Spread(3), out handles[1][index]), Is.EqualTo(SimulationResult.Success));
                }
                var removed = new[] { new List<AgentHandle>(), new List<AgentHandle>() };
                for (uint tick = 1; tick <= Ticks; tick++)
                {
                    if (tick == 60 || tick == 180)
                        for (var g = 0; g < 2; g++)
                        {
                            removed[g].Clear();
                            for (var n = 0; n < 20; n++)
                            {
                                var victim = handles[g][(n * 13 + (int)tick) % Agents];
                                Assert.That(groups[g].TryRemove(victim), Is.EqualTo(SimulationResult.Success));
                                removed[g].Add(victim);
                            }
                        }
                    if (tick == 61 || tick == 181)
                        for (var g = 0; g < 2; g++)
                            for (var n = 0; n < 20; n++)
                            {
                                Assert.That(groups[g].TryAdd(ThinkSchedule.Spread(g == 0 ? 5u : 3u), out var added), Is.EqualTo(SimulationResult.Success));
                                handles[g][added.Index] = added;
                            }

                    WriteInputs(tick, guardSense, scoutSpeed);
                    WriteInputs(tick + 1000, scoutSense, unusedSpeed);
                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));

                    var digest = Fnv.Create();
                    digest.Add(tick);
                    foreach (var group in groups)
                    {
                        AppendDigest(group, ref digest, path);
                        AppendCommands(group, ref digest, ref outcome.Orders, ref outcome.Pings);
                        outcome.Faults += group.GetFaults().Length;
                    }
                    outcome.Digests.Add(digest.Value);
                }
                Assert.That(guards.JobPathAvailable && scouts.JobPathAvailable, Is.True, "The generated catalog has the agent-frame entry.");
                outcome.JobBatches = guards.JobBatchesForTests + scouts.JobBatchesForTests;
                outcome.Woken = (int)guards.WokenByInputForTests;
                Assert.That(scouts.WokenByInputForTests, Is.Zero, "Wake on change is off by default.");
            }
            return outcome;
        }

        private static void AssertSame(Outcome expected, Outcome actual, string what)
        {
            Assert.That(actual.Digests.Count, Is.EqualTo(expected.Digests.Count), what + ": ticks");
            for (var index = 0; index < expected.Digests.Count; index++)
                if (expected.Digests[index] != actual.Digests[index])
                    Assert.Fail(what + ": first digest difference at tick " + (index + 1));
        }

        [Test]
        public void Population_InputsAndCommands_DigestsAreIdentical_AcrossPathsWorkerCountsAndRuns()
        {
            var guard = Program(GuardTree);
            var scout = Program(ScoutTree);

            var inPlaceA = RunPopulation(guard, scout, Settings(false, 1));
            var inPlaceB = RunPopulation(guard, scout, Settings(false, 1));
            var jobs = RunPopulation(guard, scout, Settings(true, 1));
            var defaults = RunPopulation(guard, scout, Settings(true, 64));
            var workers = JobsUtility.JobWorkerCount;
            Outcome noWorkers, oneWorker;
            try
            {
                JobsUtility.JobWorkerCount = 0;
                noWorkers = RunPopulation(guard, scout, Settings(true, 1));
                JobsUtility.JobWorkerCount = 1;
                oneWorker = RunPopulation(guard, scout, Settings(true, 1));
            }
            finally
            {
                JobsUtility.JobWorkerCount = workers;
            }

            Assert.That(inPlaceA.Faults, Is.Zero, "No agent faults.");
            Assert.That(inPlaceA.Orders, Is.GreaterThan(2000), "Agents give orders.");
            Assert.That(inPlaceA.Pings, Is.EqualTo(inPlaceA.Orders), "Every order is followed by a ping.");
            Assert.That(inPlaceA.Woken, Is.GreaterThan(500), "Wake on changed input wakes guards between their thinks.");
            Assert.That(inPlaceA.JobBatches, Is.Zero);
            Assert.That(jobs.JobBatches, Is.GreaterThanOrEqualTo(Ticks));
            Assert.That(noWorkers.JobBatches, Is.GreaterThanOrEqualTo(Ticks));

            AssertSame(inPlaceA, inPlaceB, "second run in the same process");
            AssertSame(inPlaceA, jobs, "UseJobs=true, MinimumJobBatch=1");
            AssertSame(inPlaceA, defaults, "default settings (MinimumJobBatch=64)");
            AssertSame(inPlaceA, noWorkers, "0 job workers");
            AssertSame(inPlaceA, oneWorker, "1 job worker");
        }

        // ---- reactive interrupt and wake on change -----------------------------------------------------

        /// <summary>
        /// One guard thinking every 10 ticks (phase 0) sits in its idle wait. The threat rises at tick 13:
        /// without wake on change the alarm preempts the wait at the next think (tick 20); with it, in
        /// the very next step (tick 13). Same on both paths.
        /// </summary>
        [TestCase(false, false, 20u)]
        [TestCase(true, false, 13u)]
        [TestCase(false, true, 20u)]
        [TestCase(true, true, 13u)]
        public void ReactiveInterrupt_RisingInput_PreemptsAtNextThink_OrNextStepWithWake(bool wake, bool useJobs, uint expectedTick)
        {
            var guard = Program(GuardTree);
            var sense = new NativeArray<SenseInput>(2, Allocator.Persistent);
            using (var simulation = new TreeSimulation(Settings(useJobs, 1)))
            using (sense)
            {
                Assert.That(simulation.TryAddGroup(guard, TreeGroupSettings.Default(2), out var group), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryBindInput(Sense, sense), Is.EqualTo(SimulationResult.Success));
                if (wake) Assert.That(group.TrySetWakeOnChange(Sense, true), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryAdd(ThinkSchedule.Every(10, 0), out var agent), Is.EqualTo(SimulationResult.Success));
                var firstOrder = 0u;
                var updates = new List<ulong>();
                for (uint tick = 1; tick <= 30; tick++)
                {
                    if (tick == 13) sense[0] = new SenseInput { Threat = 0.9f, Ally = 42, Flag = true };
                    else if (tick == 15) sense[0] = new SenseInput { Threat = 0.9f, Ally = 42, Flag = true }; // unchanged: no wake
                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    updates.Add(group.CoreForTests(0).TotalUpdates);
                    var orders = group.GetCommands(Order);
                    if (orders.Length > 0 && firstOrder == 0)
                    {
                        firstOrder = tick;
                        Assert.That(orders.Length, Is.EqualTo(1));
                        Assert.That(orders[0].Agent, Is.EqualTo(agent));
                        Assert.That(orders[0].Value.Target, Is.EqualTo(42));
                        Assert.That(orders[0].Value.Strength, Is.EqualTo(1.8f));
                        Assert.That(orders[0].Value.Urgent, Is.True);
                    }
                    Assert.That(group.GetFaults().Length, Is.Zero);
                }
                Assert.That(firstOrder, Is.EqualTo(expectedTick));
                // The agent thought at 10 and 20 (and 30); with wake once more at 13 and never at 14-19.
                Assert.That(updates[9], Is.EqualTo(1ul));
                Assert.That(updates[18], Is.EqualTo(wake ? 2ul : 1ul), "No wake on an unchanged row.");
                Assert.That(updates[29], Is.EqualTo(wake ? 4ul : 3ul));
            }
        }

        // ---- zero GC -----------------------------------------------------------------------------------

        [TestCase(false)]
        [TestCase(true)]
        public void Step_WithInputsCommandsAndWake_AllocatesNothing_AfterWarmUp(bool useJobs)
        {
            var guard = Program(GuardTree);
            var scout = Program(ScoutTree);
            using (var simulation = new TreeSimulation(Settings(useJobs, 1)))
            using (var guardSense = new NativeArray<SenseInput>(Agents, Allocator.Persistent))
            using (var scoutSense = new NativeArray<SenseInput>(Agents, Allocator.Persistent))
            using (var scoutSpeed = new NativeArray<float>(Agents, Allocator.Persistent))
            using (var unusedSpeed = new NativeArray<float>(0, Allocator.Persistent))
            {
                simulation.TryAddGroup(guard, TreeGroupSettings.Default(Agents), out var guards);
                simulation.TryAddGroup(scout, TreeGroupSettings.Default(Agents), out var scouts);
                guards.TryBindInput(Sense, guardSense);
                scouts.TryBindInput(Sense, scoutSense);
                scouts.TryBindInput(Speed, scoutSpeed);
                guards.TrySetWakeOnChange(Sense, true);
                for (var index = 0; index < Agents; index++)
                {
                    guards.TryAdd(ThinkSchedule.Spread(5), out _);
                    scouts.TryAdd(ThinkSchedule.Spread(3), out _);
                }
                var orders = 0;
                uint tick = 0;
                for (; tick < 30; tick++)
                {
                    WriteInputs(tick + 1, guardSense, scoutSpeed);
                    WriteInputs(tick + 1001, scoutSense, unusedSpeed);
                    Assert.That(simulation.Step(tick + 1), Is.EqualTo(SimulationResult.Success));
                    orders += guards.GetCommands(Order).Length + scouts.GetCommands(Order).Length;
                    orders += guards.GetCommands(Ping).Length + scouts.GetCommands(Ping).Length;
                }

                var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
                recorder.FilterToCurrentThread();
                recorder.enabled = false;
                recorder.enabled = true;
                var measured = 0;
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var n = 0; n < 100; n++)
                {
                    tick++;
                    WriteInputs(tick, guardSense, scoutSpeed);
                    WriteInputs(tick + 1000, scoutSense, unusedSpeed);
                    simulation.Step(tick);
                    measured += guards.GetCommands(Order).Length + scouts.GetCommands(Order).Length;
                    measured += guards.GetCommands(Ping).Length + scouts.GetCommands(Ping).Length;
                }
                var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                recorder.enabled = false;
                var blocks = recorder.sampleBlockCount;

                Assert.That(measured, Is.GreaterThan(1000), "Commands are emitted and read during the measured ticks.");
                Assert.That(bytes, Is.Zero, bytes + " managed bytes allocated in 100 warmed-up ticks.");
                Assert.That(blocks, Is.Zero, blocks + " GC.Alloc samples in 100 warmed-up ticks.");
            }
        }

        // ---- command buffer overflow -------------------------------------------------------------------

        /// <summary>
        /// Two orders in one think with a quota of 1 order per agent per think: the second node does not
        /// run, the agent faults with cause CapacityExceeded, the first node's commands are kept, Step
        /// succeeds and nothing throws. With a quota of 2 both orders arrive. Same on both paths.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CommandQuota_Overflow_FaultsTheAgentWithCapacityExceeded_NeverThrows(bool useJobs)
        {
            var program = Program(DoubleOrderTree);
            var sense = new NativeArray<SenseInput>(4, Allocator.Persistent);
            using (var simulation = new TreeSimulation(Settings(useJobs, 1)))
            using (sense)
            {
                var tight = TreeGroupSettings.Default(4);
                tight.CommandQuotaPerAgent = 1;
                Assert.That(simulation.TryAddGroup(program, tight, out var tightGroup), Is.EqualTo(SimulationResult.Success));
                Assert.That(simulation.TryAddGroup(program, TreeGroupSettings.Default(4), out var roomy), Is.EqualTo(SimulationResult.Success));
                foreach (var group in new[] { tightGroup, roomy })
                {
                    Assert.That(group.TryBindInput(Sense, sense), Is.EqualTo(SimulationResult.Success));
                    for (var index = 0; index < 4; index++) group.TryAdd(out _);
                }
                for (var index = 0; index < 4; index++) sense[index] = new SenseInput { Threat = 0.5f, Ally = index, Flag = false };

                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var faults = tightGroup.GetFaults();
                Assert.That(faults.Length, Is.EqualTo(4));
                for (var index = 0; index < 4; index++)
                {
                    Assert.That(faults[index].Agent.Index, Is.EqualTo(index));
                    Assert.That(faults[index].Cause, Is.EqualTo(BurstContextResult.CapacityExceeded));
                    Assert.That(faults[index].Failure.Code, Is.Not.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
                }
                var kept = tightGroup.GetCommands(Order);
                Assert.That(kept.Length, Is.EqualTo(4), "The first node's order of each agent is kept.");
                for (var index = 0; index < 4; index++) Assert.That(kept[index].Value.Strength, Is.EqualTo(0.5f));

                Assert.That(roomy.GetFaults().Length, Is.Zero);
                var orders = roomy.GetCommands(Order);
                Assert.That(orders.Length, Is.EqualTo(8));
                for (var index = 0; index < 8; index++)
                {
                    Assert.That(orders[index].Agent.Index, Is.EqualTo(index / 2), "Slot order, then emission order.");
                    Assert.That(orders[index].Value.Strength, Is.EqualTo(index % 2 == 0 ? 0.5f : 1.5f));
                }
                Assert.That(roomy.GetRootCompletions().Length, Is.EqualTo(4));

                Assert.That(simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                Assert.That(tightGroup.GetCommands(Order).Length, Is.Zero, "Faulted agents stay stopped; last step's commands are gone.");
                Assert.That(roomy.GetCommands(Order).Length, Is.EqualTo(8), "Restarted agents order again.");
            }
        }

        [Test]
        public void CommandQuota_Negative_IsRefused()
        {
            var program = Program(FixedOrderTree);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            {
                var settings = TreeGroupSettings.Default(2);
                settings.CommandQuotaPerAgent = -1;
                Assert.That(simulation.TryAddGroup(program, settings, out _), Is.EqualTo(SimulationResult.InvalidArgument));
                settings.CommandQuotaPerAgent = 0; // the default quota
                Assert.That(simulation.TryAddGroup(program, settings, out var group), Is.EqualTo(SimulationResult.Success));
                group.TryAdd(out _);
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.GetCommands(Order).Length, Is.EqualTo(1));
            }
        }

        // ---- input refusals ------------------------------------------------------------------------------

        [Test]
        public void Inputs_UnboundStaleWrongLengthAndWrongType_AreRefusedWithResultCodes()
        {
            var guard = Program(GuardTree);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            {
                Assert.That(simulation.TryAddGroup(guard, TreeGroupSettings.Default(4), out var group), Is.EqualTo(SimulationResult.Success));
                group.TryAdd(out _);

                // Unbound: Step refuses before anything runs.
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.InputMissing));
                Assert.That(simulation.LastTick, Is.Zero);
                Assert.That(group.CoreForTests(0).TotalUpdates, Is.Zero, "Nothing ran.");

                // Wrong length, unknown channel, wrong types.
                using (var shortRows = new NativeArray<SenseInput>(3, Allocator.Persistent))
                    Assert.That(group.TryBindInput(Sense, shortRows), Is.EqualTo(SimulationResult.InvalidArgument));
                Assert.That(group.TryBindInput(Sense, default(NativeArray<SenseInput>)), Is.EqualTo(SimulationResult.InvalidArgument));
                using (var unknownRows = new NativeArray<SenseInput>(4, Allocator.Persistent))
                    Assert.That(group.TryBindInput(InputChannel<SenseInput>.Create("no-such-input"), unknownRows), Is.EqualTo(SimulationResult.UnknownChannel));
                using (var floats = new NativeArray<float>(4, Allocator.Persistent))
                    Assert.That(group.TryBindInput(InputChannel<float>.Create("sense"), floats), Is.EqualTo(SimulationResult.TypeMismatch));
                using (var lookalike = new NativeArray<SenseLookalike>(4, Allocator.Persistent))
                    Assert.That(group.TryBindInput(InputChannel<SenseLookalike>.Create("sense"), lookalike), Is.EqualTo(SimulationResult.TypeMismatch),
                        "Same layout, but not the registered value type.");
                Assert.That(group.TryGetCommands(CommandChannel<int>.Create("order"), out _), Is.EqualTo(SimulationResult.TypeMismatch));
                Assert.That(group.TryGetCommands(CommandChannel<int>.Create("no-such-command"), out _), Is.EqualTo(SimulationResult.UnknownChannel));
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.InputMissing), "Refused binds leave the channel unbound.");

                // Stale: a bound array disposed before the step (detected with collection safety checks).
                var stale = new NativeArray<SenseInput>(4, Allocator.Persistent);
                Assert.That(group.TryBindInput(Sense, stale), Is.EqualTo(SimulationResult.Success));
                stale.Dispose();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.InputMissing));
                Assert.That(group.CoreForTests(0).TotalUpdates, Is.Zero, "Nothing ran.");
#endif
                using (var rows = new NativeArray<SenseInput>(4, Allocator.Persistent))
                {
                    Assert.That(group.TryBindInput(Sense, rows), Is.EqualTo(SimulationResult.Success));
                    Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                    Assert.That(group.CoreForTests(0).TotalUpdates, Is.EqualTo(1ul));

                    // Grown past the bound array: refused until rebound.
                    Assert.That(group.TryReserve(8), Is.EqualTo(SimulationResult.Success));
                    Assert.That(simulation.Step(2), Is.EqualTo(SimulationResult.InvalidArgument));
                    Assert.That(simulation.LastTick, Is.EqualTo(1u));
                    Assert.That(group.TryUnbindInput(Sense), Is.EqualTo(SimulationResult.Success));
                    Assert.That(simulation.Step(2), Is.EqualTo(SimulationResult.InputMissing));
                }
                using (var grownRows = new NativeArray<SenseInput>(8, Allocator.Persistent))
                {
                    var rows = grownRows;
                    Assert.That(group.TryBindInput(Sense, rows), Is.EqualTo(SimulationResult.Success));
                    Assert.That(simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                    Assert.That(group.TryUnbindInput(Sense), Is.EqualTo(SimulationResult.Success));
                }
            }
        }

        /// <summary>Same fields as <see cref="SenseInput"/> but not a registered value type.</summary>
        private struct SenseLookalike
        {
            public float Threat;
            public int Ally;
            public bool Flag;
        }

        /// <summary>An input value that is not canonical (NaN) faults the agent that reads it; the step and other agents go on.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Inputs_NonCanonicalValue_FaultsOnlyTheReadingAgent(bool useJobs)
        {
            var guard = Program(GuardTree);
            var sense = new NativeArray<SenseInput>(2, Allocator.Persistent);
            using (var simulation = new TreeSimulation(Settings(useJobs, 1)))
            using (sense)
            {
                simulation.TryAddGroup(guard, TreeGroupSettings.Default(2), out var group);
                group.TryBindInput(Sense, sense);
                group.TryAdd(out _);
                group.TryAdd(out _);
                sense[0] = new SenseInput { Threat = float.NaN, Ally = 1 };
                sense[1] = new SenseInput { Threat = 0.75f, Ally = 2 };
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var faults = group.GetFaults();
                Assert.That(faults.Length, Is.EqualTo(1));
                Assert.That(faults[0].Agent.Index, Is.Zero);
                Assert.That(faults[0].Cause, Is.EqualTo(BurstContextResult.InvalidEncoding));
                var orders = group.GetCommands(Order);
                Assert.That(orders.Length, Is.EqualTo(1));
                Assert.That(orders[0].Agent.Index, Is.EqualTo(1));
            }
        }

        // ---- busy, unsupported, old catalogs ----------------------------------------------------------

        [Test]
        public void CallsDuringStep_ReturnBusy()
        {
            var guard = Program(GuardTree);
            var sense = new NativeArray<SenseInput>(2, Allocator.Persistent);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            using (sense)
            {
                simulation.TryAddGroup(guard, TreeGroupSettings.Default(2), out var group);
                group.TryBindInput(Sense, sense);
                group.TryAdd(out _);
                var seen = new List<SimulationResult>();
                simulation.StepProbeForTests = () =>
                {
                    seen.Add(group.TryBindInput(Sense, sense));
                    seen.Add(group.TryUnbindInput(Sense));
                    seen.Add(group.TrySetWakeOnChange(Sense, true));
                    seen.Add(group.TryGetCommands(Order, out _));
                };
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                Assert.That(seen, Is.EqualTo(Enumerable.Repeat(SimulationResult.Busy, 4)));
                Assert.That(group.TryGetCommands(Order, out var after), Is.EqualTo(SimulationResult.Success), "After the step the calls work again.");
                Assert.That(after.Length, Is.Zero);
            }
        }

        [Test]
        public void ExternalActions_AreRefusedWithUnsupported()
        {
            var program = Program(ExternalTree);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            {
                Assert.That(simulation.TryAddGroup(program, TreeGroupSettings.Default(2), out var group), Is.EqualTo(SimulationResult.Unsupported));
                Assert.That(group, Is.Null);
                Assert.That(simulation.GroupCount, Is.Zero);
            }
        }

        /// <summary>
        /// An executor that is not the generated one: it forwards dispatch, but implements neither the
        /// agent-frame entry nor the P10-010 binding ids, as a catalog generated before this stage.
        /// </summary>
        private sealed class OldExecutor : IGeneratedBurstCatalogExecutorV2
        {
            private readonly IGeneratedBurstCatalogExecutorV2 _inner;
            internal OldExecutor(IGeneratedBurstCatalogExecutorV2 inner) { _inner = inner; }
            public BurstExecutionResult ExecuteImmediate(ref BurstExecutionBatch batch) => _inner.ExecuteImmediate(ref batch);
            public Unity.Jobs.JobHandle Schedule(ref BurstExecutionBatch batch, Unity.Jobs.JobHandle dependency) => _inner.Schedule(ref batch, dependency);
            public bool TryGetEffectDescriptor(uint catalogCaseIndex, uint bindingOrdinal, out ProductionTreeEffectDescriptor descriptor)
                => _inner.TryGetEffectDescriptor(catalogCaseIndex, bindingOrdinal, out descriptor);
        }

        private static bool TryCreateOldCatalog(Allocator allocator, out GeneratedBurstCatalogV2 catalog, out BurstContextResult failure)
        {
            if (!SimulationIoCatalog.TryCreateRuntimeCatalog(allocator, out catalog, out failure)) return false;
            var property = typeof(GeneratedBurstCatalogV2).GetProperty("Executor", BindingFlags.Instance | BindingFlags.NonPublic);
            property.SetValue(catalog, new OldExecutor((IGeneratedBurstCatalogExecutorV2)property.GetValue(catalog)));
            return true;
        }

        /// <summary>
        /// A catalog without binding ids (generated before P10-010): a blackboard-only program and a
        /// commands-only program run in the container in place (no agent-frame entry), the commands are
        /// discarded as in G1 (<see cref="SimulationResult.CatalogTooOld"/> when read), and the program
        /// gives the same agent states as on the current catalog; a program with inputs is refused with
        /// CatalogTooOld. The generated catalog does implement the binding ids.
        /// </summary>
        [Test]
        public void OldCatalog_RunsInPlace_CommandsDiscarded_InputsRefused()
        {
            Assert.That(typeof(SimulationIoCatalog).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Any(type => typeof(AIBT.AgentFrame.IGeneratedBindingIdentityV1).IsAssignableFrom(type)), Is.True,
                "The regenerated catalog's executor has binding ids.");

            var oldGuard = Program(GuardTree, TryCreateOldCatalog);
            var oldFixed = Program(FixedOrderTree, TryCreateOldCatalog);
            var newFixed = Program(FixedOrderTree);
            var oldLoop = Program(DeterministicLoopIoTree, TryCreateOldCatalog);
            var newLoop = Program(DeterministicLoopIoTree);
            using (var simulation = new TreeSimulation(Settings(true, 1)))
            {
                Assert.That(simulation.TryAddGroup(oldGuard, TreeGroupSettings.Default(2), out _), Is.EqualTo(SimulationResult.CatalogTooOld));
                Assert.That(simulation.TryAddGroup(oldFixed, TreeGroupSettings.Default(4), out var oldFixedGroup), Is.EqualTo(SimulationResult.Success));
                Assert.That(simulation.TryAddGroup(newFixed, TreeGroupSettings.Default(4), out var newFixedGroup), Is.EqualTo(SimulationResult.Success));
                Assert.That(simulation.TryAddGroup(oldLoop, TreeGroupSettings.Default(4), out var oldLoopGroup), Is.EqualTo(SimulationResult.Success));
                Assert.That(simulation.TryAddGroup(newLoop, TreeGroupSettings.Default(4), out var newLoopGroup), Is.EqualTo(SimulationResult.Success));
                Assert.That(oldFixedGroup.JobPathAvailable || oldLoopGroup.JobPathAvailable, Is.False, "Old catalogs run in place.");
                Assert.That(newLoopGroup.JobPathAvailable, Is.True);
                foreach (var group in new[] { oldFixedGroup, newFixedGroup, oldLoopGroup, newLoopGroup })
                    for (var index = 0; index < 4; index++) group.TryAdd(ThinkSchedule.Spread(2), out _);
                var path = new List<uint>();
                for (uint tick = 1; tick <= 40; tick++)
                {
                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    Assert.That(oldFixedGroup.TryGetCommands(Order, out _), Is.EqualTo(SimulationResult.CatalogTooOld));
                    Assert.That(newFixedGroup.GetCommands(Order).Length, Is.EqualTo(2), "Two agents think per tick and each orders once.");
                    Assert.That(oldFixedGroup.GetFaults().Length + oldLoopGroup.GetFaults().Length, Is.Zero);
                    var oldDigest = Fnv.Create();
                    var newDigest = Fnv.Create();
                    AppendDigestWithoutGroup(oldLoopGroup, ref oldDigest, path);
                    AppendDigestWithoutGroup(newLoopGroup, ref newDigest, path);
                    AppendDigestWithoutGroup(oldFixedGroup, ref oldDigest, path);
                    AppendDigestWithoutGroup(newFixedGroup, ref newDigest, path);
                    Assert.That(oldDigest.Value, Is.EqualTo(newDigest.Value), "tick " + tick);
                }
            }
        }

        // The patrol loop of G1 (blackboard keys only), compiled against the P10-010 catalog. Instance ids
        // differ between groups, so the comparison leaves them out.
        private const string DeterministicLoopIoTree = SimulationTestPrograms.DeterministicLoopTree;

        private static void AppendDigestWithoutGroup(TreeGroup group, ref Fnv digest, List<uint> path)
        {
            for (var index = 0; index < group.HighWaterIndex; index++)
            {
                var core = group.CoreForTests(index);
                digest.Add(core.TotalUpdates);
                digest.Add(core.LastRootResult.HasValue ? 1ul + (ulong)core.LastRootResult.Value : 0ul);
                digest.Add((ulong)core.LastFailure.Code);
                core.Machine.CopyActivePath(path);
                foreach (var node in path) digest.Add(node);
                for (uint offset = 0; core.GeneratedDispatch.TryReadTreeValueByte(offset, out var value); offset++) digest.Add(value);
            }
            digest.Add((ulong)group.GetRootCompletions().Length);
        }

        // ---- hosts are unchanged -------------------------------------------------------------------------

        private ProductionTreeHost NewHost(out GameObject gameObject)
        {
            gameObject = new GameObject("AIBT.Tests.P10010.Host");
            _objects.Add(gameObject);
            return gameObject.AddComponent<ProductionTreeHost>();
        }

        /// <summary>
        /// Pinned host behaviour, unchanged by P10-010: a program with a snapshot (input) binding is
        /// refused at bootstrap (the adapter refuses SnapshotRead with PhaseViolation, reported as
        /// NativeCapacityPlanInvalid); a program whose command has a registered payload type bootstraps
        /// and runs with effects off, and faults with CallbackRejected (TypeMismatch: no effect
        /// descriptor for the type) when effect delivery is on.
        /// </summary>
        [Test]
        public void Hosts_StillRefuseSnapshotBindings_AndRegisteredCommandPayloads_AsBefore()
        {
            var guard = Program(GuardTree);
            var host = NewHost(out _);
            Assert.That(guard.TryBootstrap(host, out var failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));

            var fixedOrder = Program(FixedOrderTree);
            var quiet = NewHost(out _);
            long now = 0;
            Assert.That(fixedOrder.TryBootstrap(quiet, out failure, clock: () => now), Is.True, failure.ToString());
            Drive(quiet);
            Assert.That(quiet.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
            Assert.That(quiet.LastRootResult, Is.EqualTo(NodeStatus.Success));

            var loud = NewHost(out _);
            Assert.That(fixedOrder.TryBootstrap(loud, out failure, effectCapacity: 8, clock: () => now), Is.True, failure.ToString());
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("NativeLifetimeStateInvalid: Callback rejected: TypeMismatch"));
            Drive(loud);
            Assert.That(loud.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
            Assert.That(Core(loud).LastRejectedResult, Is.EqualTo(BurstContextResult.TypeMismatch));
        }

        private static void Drive(ProductionTreeHost host)
            => typeof(ProductionTreeHost).GetMethod("DriveOneUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);

        private static ProductionTreeAgentCore Core(ProductionTreeHost host)
            => (ProductionTreeAgentCore)typeof(ProductionTreeHost).GetField("_core", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);

        // ---- the canonical formats (findings F1/F2 of the evidence) ---------------------------------------

        /// <summary>
        /// Pins the canonical value layout the catalog records and the container translates to and from:
        /// fields ordered by stable field id (UTF-8 ordinal), natural alignment, little-endian, size
        /// rounded to the largest alignment. Raw C# layout differs (declaration order).
        /// </summary>
        [Test]
        public void CanonicalLayout_IsOrderedByFieldId_AndTranslatedBothWays()
        {
            Assert.That(SimulationIoCatalog.TryCreateRuntimeCatalog(Allocator.Persistent, out var catalog, out var failure), Is.True, failure.ToString());
            try
            {
                var senseId = StableHash.Fnv1A64("aibt.tests.p10010.sense");
                var orderId = StableHash.Fnv1A64("aibt.tests.p10010.order");
                var sense = FindBinding(catalog, NativeBurstDispatchBindingKindV2.SnapshotRead, senseId);
                var order = FindBinding(catalog, NativeBurstDispatchBindingKindV2.EffectCommand, orderId);
                Assert.That(Layout(catalog, sense), Is.EqualTo("Int32@0x1/Boolean@4x1/Float32@8x1 size 12"));
                Assert.That(Layout(catalog, order), Is.EqualTo("Float32@0x2/Float32@8x1/Int32@12x1/Boolean@16x1 size 20"));
                var identity = (AIBT.AgentFrame.IGeneratedBindingIdentityV1)catalog.Executor;
                Assert.That(identity.BindingIdCount, Is.EqualTo(catalog.Bindings.Length));
            }
            finally
            {
                catalog.TryDispose(out _);
            }

            // End to end: raw row -> canonical input -> node -> canonical command -> raw command.
            var guard = Program(GuardTree);
            var rows = new NativeArray<SenseInput>(1, Allocator.Persistent);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            using (rows)
            {
                simulation.TryAddGroup(guard, TreeGroupSettings.Default(1), out var group);
                group.TryBindInput(Sense, rows);
                group.TryAdd(out _);
                rows[0] = new SenseInput { Threat = 0.625f, Ally = -7, Flag = true };
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var orders = group.GetCommands(Order);
                Assert.That(orders.Length, Is.EqualTo(1));
                Assert.That(orders[0].Value.Strength, Is.EqualTo(1.25f));
                Assert.That(orders[0].Value.Target, Is.EqualTo(-7));
                Assert.That(orders[0].Value.Urgent, Is.True);
                Assert.That(orders[0].Value.Heading.X, Is.EqualTo(0.625f));
                Assert.That(orders[0].Value.Heading.Y, Is.EqualTo(-0.625f));
                var pings = group.GetCommands(Ping);
                Assert.That(pings.Length, Is.EqualTo(1));
                Assert.That(pings[0].Value, Is.EqualTo(-6));
            }
        }

        /// <summary>-0.0 in a game row reaches the node as +0.0 (canonical form), so a command built from it carries +0.0.</summary>
        [Test]
        public void NegativeZeroInput_IsNormalized()
        {
            var program = Program(DoubleOrderTree);
            var rows = new NativeArray<SenseInput>(1, Allocator.Persistent);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            using (rows)
            {
                simulation.TryAddGroup(program, TreeGroupSettings.Default(1), out var group);
                group.TryBindInput(Sense, rows);
                group.TryAdd(out _);
                rows[0] = new SenseInput { Threat = -0f, Ally = 3 };
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var orders = group.GetCommands(Order);
                Assert.That(orders.Length, Is.EqualTo(2));
                Assert.That(BitConverter.SingleToInt32Bits(orders[0].Value.Strength), Is.Zero);
                Assert.That(BitConverter.SingleToInt32Bits(orders[0].Value.Heading.Y), Is.Zero,
                    "The node computed -(+0.0) = -0.0; the command write stores it as +0.0.");
            }
        }

        private static uint FindBinding(GeneratedBurstCatalogV2 catalog, NativeBurstDispatchBindingKindV2 kind, ulong typeId)
        {
            for (var index = 0; index < catalog.Bindings.Length; index++)
                if (catalog.Bindings[index].Kind == kind && catalog.Bindings[index].PrimaryTypeNumericId == typeId) return (uint)index;
            Assert.Fail("No " + kind + " binding of the type.");
            return 0;
        }

        private static string Layout(GeneratedBurstCatalogV2 catalog, uint bindingIndex)
        {
            var binding = catalog.Bindings[(int)bindingIndex];
            var parts = new List<string>();
            for (uint field = 0; field < binding.PrimaryValueFieldCount; field++)
            {
                var entry = catalog.ValueFields[(int)(binding.FirstPrimaryValueField + field)];
                parts.Add(entry.Encoding + "@" + entry.ByteOffset + "x" + entry.ElementCount);
            }
            return string.Join("/", parts) + " size " + binding.PrimaryValueSize;
        }
    }
}
