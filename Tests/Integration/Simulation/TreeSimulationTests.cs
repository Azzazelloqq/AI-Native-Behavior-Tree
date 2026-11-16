using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Simulation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// P10-009 (ADR-P10-008 stage G1): the GameObject-free <see cref="TreeSimulation"/> stepped by a
    /// plain loop. EditMode, no scene, no GameObject, no Update. Proof items 1-4 and 6 of the ADR,
    /// adapted to trees that use blackboard keys only.
    /// </summary>
    public sealed class TreeSimulationTests
    {
        private const int AgentsPerProgram = 250;
        private const int ScenarioTicks = 300;
        private const ulong Seed = 0x5eed_0f_a1b7UL;

        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        private readonly List<ProductionTreeProgram> _programs = new List<ProductionTreeProgram>();

        [TearDown]
        public void TearDown()
        {
            foreach (var program in _programs) program.Dispose();
            _programs.Clear();
            foreach (var asset in _assets) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        private ProductionTreeProgram Program(string json)
        {
            var asset = PrepareAsset(json);
            _assets.Add(asset);
            var program = CreateProgram(asset);
            _programs.Add(program);
            return program;
        }

        private static TreeSimulationSettings Settings(bool useJobs, int minimumJobBatch, ulong seed = Seed)
        {
            var settings = TreeSimulationSettings.Default(TickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = minimumJobBatch;
            settings.RandomSeed = seed;
            return settings;
        }

        // ---- proof items 1-3: population, add/remove, digests ------------------------------------

        private sealed class ScenarioOutcome
        {
            internal readonly List<ulong> Digests = new List<ulong>();
            internal int Completions;
            internal int Successes;
            internal int Failures;
            internal int Faults;
            internal long JobBatches;
            internal readonly HashSet<string> ActivePaths = new HashSet<string>();
        }

        /// <summary>
        /// Two programs, 250 agents each with Spread(5), 300 ticks from a plain loop. At ticks 60 and 180
        /// 20 agents of each group are removed; at the next tick 20 are added, which reuse those slots with
        /// a newer generation; the old handles are refused. A digest of every agent is taken every tick.
        /// </summary>
        private ScenarioOutcome RunScenario(ProductionTreeProgram patrol, ProductionTreeProgram errand, in TreeSimulationSettings settings)
        {
            var outcome = new ScenarioOutcome();
            var path = new List<uint>();
            using (var simulation = new TreeSimulation(settings))
            {
                Assert.That(simulation.TryAddGroup(patrol, TreeGroupSettings.Default(AgentsPerProgram), out var patrols), Is.EqualTo(SimulationResult.Success));
                Assert.That(simulation.TryAddGroup(errand, TreeGroupSettings.Default(AgentsPerProgram), out var errands), Is.EqualTo(SimulationResult.Success));
                var groups = new[] { patrols, errands };
                var handles = new[] { new AgentHandle[AgentsPerProgram], new AgentHandle[AgentsPerProgram] };
                for (var g = 0; g < 2; g++)
                    for (var index = 0; index < AgentsPerProgram; index++)
                    {
                        Assert.That(groups[g].TryAdd(ThinkSchedule.Spread(5), out handles[g][index]), Is.EqualTo(SimulationResult.Success));
                        Assert.That(handles[g][index].Index, Is.EqualTo(index));
                        Assert.That(handles[g][index].Generation, Is.EqualTo(1u));
                    }
                Assert.That(patrols.TryAdd(out _), Is.EqualTo(SimulationResult.CapacityExceeded), "The group is full.");
                var removed = new[] { new List<AgentHandle>(), new List<AgentHandle>() };

                for (uint tick = 1; tick <= ScenarioTicks; tick++)
                {
                    if (tick == 60 || tick == 180)
                    {
                        for (var g = 0; g < 2; g++)
                        {
                            removed[g].Clear();
                            for (var n = 0; n < 20; n++)
                            {
                                var victim = handles[g][(n * 12 + (int)tick) % AgentsPerProgram];
                                Assert.That(groups[g].TryRemove(victim), Is.EqualTo(SimulationResult.Success));
                                Assert.That(groups[g].IsAlive(victim), Is.False, "Removed agents are not alive at once.");
                                Assert.That(groups[g].TryWake(victim), Is.EqualTo(SimulationResult.StaleHandle));
                                removed[g].Add(victim);
                            }
                            Assert.That(groups[g].Count, Is.EqualTo(AgentsPerProgram - 20));
                            Assert.That(groups[g].TryAdd(out _), Is.EqualTo(SimulationResult.CapacityExceeded),
                                "A removed slot is reusable only after its teardown step.");
                        }
                    }
                    if (tick == 61 || tick == 181)
                    {
                        for (var g = 0; g < 2; g++)
                        {
                            var expectedSlots = removed[g].Select(h => h.Index).OrderBy(i => i).ToList();
                            for (var n = 0; n < 20; n++)
                            {
                                Assert.That(groups[g].TryAdd(ThinkSchedule.Spread(5), out var added), Is.EqualTo(SimulationResult.Success));
                                Assert.That(added.Index, Is.EqualTo(expectedSlots[n]), "The lowest free slot is reused.");
                                var old = removed[g].Single(h => h.Index == added.Index);
                                Assert.That(added.Generation, Is.EqualTo(old.Generation + 1u));
                                handles[g][added.Index] = added;
                            }
                            foreach (var old in removed[g])
                            {
                                Assert.That(groups[g].IsAlive(old), Is.False);
                                Assert.That(groups[g].TryRemove(old), Is.EqualTo(SimulationResult.StaleHandle), "An old handle never addresses the new agent.");
                                Assert.That(groups[g].TryRestart(old), Is.EqualTo(SimulationResult.StaleHandle));
                            }
                            Assert.That(groups[g].Count, Is.EqualTo(AgentsPerProgram));
                        }
                    }
                    // A deterministic wake pattern: a few agents think outside their schedule.
                    if (tick % 7 == 3)
                    {
                        using (var wake = new NativeArray<int>(new[] { (int)(tick % 250), (int)((tick * 3) % 250), 9999, -1 }, Allocator.Temp))
                            Assert.That(errands.TryWakeMany(wake.AsReadOnly()), Is.EqualTo(SimulationResult.Success));
                        Assert.That(patrols.TryWake(handles[0][(int)(tick % 250)]), Is.EqualTo(SimulationResult.Success));
                    }

                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));

                    var digest = Fnv.Create();
                    digest.Add(tick);
                    foreach (var group in groups)
                    {
                        AppendDigest(group, ref digest, path);
                        var completions = group.GetRootCompletions();
                        outcome.Completions += completions.Length;
                        for (var index = 0; index < completions.Length; index++)
                            if (completions[index].Result == NodeStatus.Success) outcome.Successes++;
                            else outcome.Failures++;
                        outcome.Faults += group.GetFaults().Length;
                    }
                    for (var index = 0; index < 8; index++)
                    {
                        patrols.CoreForTests(index).Machine.CopyActivePath(path);
                        outcome.ActivePaths.Add(string.Join(",", path));
                    }
                    outcome.Digests.Add(digest.Value);
                }
                Assert.That(patrols.JobPathAvailable && errands.JobPathAvailable, Is.True, "The generated catalog has the agent-frame entry.");
                outcome.JobBatches = patrols.JobBatchesForTests + errands.JobBatchesForTests;
            }
            return outcome;
        }

        private static void AssertSameDigests(ScenarioOutcome expected, ScenarioOutcome actual, string what)
        {
            Assert.That(actual.Digests.Count, Is.EqualTo(expected.Digests.Count), what + ": ticks");
            for (var index = 0; index < expected.Digests.Count; index++)
                if (expected.Digests[index] != actual.Digests[index])
                    Assert.Fail(what + ": first digest difference at tick " + (index + 1));
        }

        [Test]
        public void Population_DigestsAreIdentical_AcrossRunsJobsAndWorkerCounts()
        {
            var patrol = Program(PatrolTree);
            var errand = Program(ErrandTree);

            var inPlaceA = RunScenario(patrol, errand, Settings(false, 1));
            var inPlaceB = RunScenario(patrol, errand, Settings(false, 1));
            var jobs = RunScenario(patrol, errand, Settings(true, 1));
            var defaults = RunScenario(patrol, errand, Settings(true, 64));
            var workers = JobsUtility.JobWorkerCount;
            ScenarioOutcome noWorkers;
            try
            {
                JobsUtility.JobWorkerCount = 0;
                noWorkers = RunScenario(patrol, errand, Settings(true, 1));
            }
            finally
            {
                JobsUtility.JobWorkerCount = workers;
            }

            // The scenario must exercise what it claims.
            Assert.That(inPlaceA.Faults, Is.Zero, "No agent faults.");
            Assert.That(inPlaceA.Successes, Is.GreaterThan(200), "Errands finish with Success and restart.");
            Assert.That(inPlaceA.Failures, Is.GreaterThan(50), "Errands also fail (random roll).");
            Assert.That(inPlaceA.ActivePaths.Count, Is.GreaterThanOrEqualTo(3), "Patrol agents move through several branches.");
            Assert.That(inPlaceA.JobBatches, Is.Zero, "UseJobs=false runs on the calling thread.");
            Assert.That(jobs.JobBatches, Is.GreaterThanOrEqualTo(ScenarioTicks), "UseJobs with MinimumJobBatch=1 runs the job.");
            Assert.That(noWorkers.JobBatches, Is.GreaterThanOrEqualTo(ScenarioTicks));

            AssertSameDigests(inPlaceA, inPlaceB, "second run in the same process");
            AssertSameDigests(inPlaceA, jobs, "UseJobs=true, MinimumJobBatch=1");
            AssertSameDigests(inPlaceA, defaults, "default settings (MinimumJobBatch=64)");
            AssertSameDigests(inPlaceA, noWorkers, "UseJobs=true with 0 job workers (default " + workers + ")");
        }

        [Test]
        public void RandomSeed_ChangesResults_AndProcessHistoryDoesNot()
        {
            var patrol = Program(PatrolTree);
            var errand = Program(ErrandTree);
            var first = RunScenario(patrol, errand, Settings(false, 1, seed: 1));
            // Unrelated work in between moves every process-wide counter.
            RunScenario(patrol, errand, Settings(true, 1, seed: 99));
            var again = RunScenario(patrol, errand, Settings(false, 1, seed: 1));
            var other = RunScenario(patrol, errand, Settings(false, 1, seed: 2));
            AssertSameDigests(first, again, "same seed after other containers ran");
            Assert.That(other.Digests.SequenceEqual(first.Digests), Is.False, "A different seed gives different results.");
        }

        // ---- proof item 4: zero GC -------------------------------------------------------------------

        [TestCase(false)]
        [TestCase(true)]
        public void Step_AllocatesNothing_After30WarmUpTicks(bool useJobs)
        {
            var patrol = Program(PatrolTree);
            var errand = Program(ErrandTree);
            using (var simulation = new TreeSimulation(Settings(useJobs, 1)))
            using (var wake = new NativeArray<int>(new[] { 3, 7, 11 }, Allocator.Persistent))
            {
                simulation.TryAddGroup(patrol, TreeGroupSettings.Default(AgentsPerProgram), out var patrols);
                simulation.TryAddGroup(errand, TreeGroupSettings.Default(AgentsPerProgram), out var errands);
                for (var index = 0; index < AgentsPerProgram; index++)
                {
                    patrols.TryAdd(ThinkSchedule.Spread(5), out _);
                    errands.TryAdd(ThinkSchedule.Spread(5), out _);
                }
                uint tick = 0;
                for (; tick < 30; tick++) Assert.That(simulation.Step(tick + 1), Is.EqualTo(SimulationResult.Success));

                var completions = 0;
                var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
                recorder.FilterToCurrentThread();
                recorder.enabled = false;
                recorder.enabled = true;
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var n = 0; n < 100; n++)
                {
                    tick++;
                    errands.TryWakeMany(wake.AsReadOnly());
                    simulation.Step(tick);
                    completions += errands.GetRootCompletions().Length;
                }
                var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                recorder.enabled = false;
                var blocks = recorder.sampleBlockCount;
                recorder.CollectFromAllThreads();

                Assert.That(completions, Is.GreaterThan(50), "Agents restart in place during the measured ticks.");
                Assert.That(bytes, Is.Zero, bytes + " managed bytes allocated in 100 warmed-up ticks.");
                Assert.That(blocks, Is.Zero, blocks + " GC.Alloc samples in 100 warmed-up ticks.");
            }
        }

        // ---- proof item 6: schedules, ticks, re-entrancy -----------------------------------------------

        [Test]
        public void Schedule_Interval5Phase2_ThinksExactlyOnTicksCongruentTo2_AndWakeAddsOneThink()
        {
            var loop = Program(DeterministicLoopTree);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            {
                simulation.TryAddGroup(loop, TreeGroupSettings.Default(16), out var group);
                Assert.That(group.TryAdd(ThinkSchedule.Every(5, 2), out var agent), Is.EqualTo(SimulationResult.Success));
                var spread = new AgentHandle[10];
                for (var index = 0; index < spread.Length; index++) group.TryAdd(ThinkSchedule.Spread(5), out spread[index]);

                var thinks = new List<uint>();
                var spreadThinks = new List<uint>[spread.Length];
                for (var index = 0; index < spread.Length; index++) spreadThinks[index] = new List<uint>();
                var core = group.CoreForTests(agent.Index);
                for (uint tick = 1; tick <= 40; tick++)
                {
                    if (tick == 13) Assert.That(group.TryWake(agent), Is.EqualTo(SimulationResult.Success));
                    if (tick == 22) Assert.That(group.TryWake(agent), Is.EqualTo(SimulationResult.Success), "Waking a due agent adds no think.");
                    var updates = core.TotalUpdates;
                    var spreadUpdates = spread.Select(h => group.CoreForTests(h.Index).TotalUpdates).ToArray();
                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    if (core.TotalUpdates != updates) thinks.Add(tick);
                    Assert.That(core.TotalUpdates - updates, Is.LessThanOrEqualTo(1ul), "At most one think per step.");
                    for (var index = 0; index < spread.Length; index++)
                        if (group.CoreForTests(spread[index].Index).TotalUpdates != spreadUpdates[index]) spreadThinks[index].Add(tick);
                }
                Assert.That(core.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
                var expected = Enumerable.Range(1, 40).Select(t => (uint)t).Where(t => t % 5 == 2 || t == 13).ToList();
                Assert.That(thinks, Is.EqualTo(expected), "Thinks on ticks = 2 (mod 5), plus one at tick 13 after the wake.");
                for (var index = 0; index < spread.Length; index++)
                {
                    var phase = (uint)(index % 5);
                    Assert.That(spreadThinks[index], Is.EqualTo(Enumerable.Range(1, 40).Select(t => (uint)t).Where(t => t % 5 == phase).ToList()),
                        "Spread agent " + index + " has phase " + phase);
                }
            }
        }

        [Test]
        public void Step_WithANonIncreasingTick_IsRefusedAndChangesNothing()
        {
            var loop = Program(DeterministicLoopTree);
            var path = new List<uint>();
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            {
                simulation.TryAddGroup(loop, TreeGroupSettings.Default(4), out var group);
                for (var index = 0; index < 4; index++) group.TryAdd(ThinkSchedule.Every(1, 0), out _);
                Assert.That(simulation.Step(5), Is.EqualTo(SimulationResult.Success));
                ulong Digest()
                {
                    var digest = Fnv.Create();
                    AppendDigest(group, ref digest, path);
                    return digest.Value;
                }
                var before = Digest();
                Assert.That(simulation.Step(5), Is.EqualTo(SimulationResult.TickNotIncreasing));
                Assert.That(simulation.Step(3), Is.EqualTo(SimulationResult.TickNotIncreasing));
                Assert.That(simulation.Step(0), Is.EqualTo(SimulationResult.TickNotIncreasing));
                Assert.That(simulation.LastTick, Is.EqualTo(5u));
                Assert.That(Digest(), Is.EqualTo(before), "A refused step changes nothing.");
                Assert.That(simulation.Step(6), Is.EqualTo(SimulationResult.Success));
                Assert.That(Digest(), Is.Not.EqualTo(before));
                Assert.That(simulation.Step(uint.MaxValue), Is.EqualTo(SimulationResult.Success), "The largest tick fits the time range.");
            }
        }

        [Test]
        public void CallsDuringStep_AreRefusedWithBusy_AndChangeNothing()
        {
            var loop = Program(DeterministicLoopTree);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            {
                simulation.TryAddGroup(loop, TreeGroupSettings.Default(4), out var group);
                group.TryAdd(out var agent);
                var probed = false;
                simulation.StepProbeForTests = () =>
                {
                    probed = true;
                    Assert.That(simulation.IsStepping, Is.True);
                    Assert.That(simulation.Step(100), Is.EqualTo(SimulationResult.Busy));
                    Assert.That(simulation.TryAddGroup(loop, TreeGroupSettings.Default(1), out _), Is.EqualTo(SimulationResult.Busy));
                    Assert.That(simulation.TryRemoveGroup(group), Is.EqualTo(SimulationResult.Busy));
                    Assert.That(group.TryAdd(out _), Is.EqualTo(SimulationResult.Busy));
                    Assert.That(group.TryRemove(agent), Is.EqualTo(SimulationResult.Busy));
                    Assert.That(group.TryRestart(agent), Is.EqualTo(SimulationResult.Busy));
                    Assert.That(group.TryWake(agent), Is.EqualTo(SimulationResult.Busy));
                    Assert.That(group.TrySetThinkSchedule(agent, ThinkSchedule.Every(2, 0)), Is.EqualTo(SimulationResult.Busy));
                    Assert.That(group.TryReserve(8), Is.EqualTo(SimulationResult.Busy));
                };
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                Assert.That(probed, Is.True);
                Assert.That(simulation.IsStepping, Is.False);
                Assert.That(group.Count, Is.EqualTo(1));
                Assert.That(group.Capacity, Is.EqualTo(4));
                Assert.That(simulation.LastTick, Is.EqualTo(1u));
            }
        }

        [Test]
        public void TwoContainers_SideBySide_DoNotAffectEachOther()
        {
            var patrol = Program(PatrolTree);
            var errand = Program(ErrandTree);
            var path = new List<uint>();
            List<ulong> Run(bool withNeighbour)
            {
                var digests = new List<ulong>();
                using (var subject = new TreeSimulation(Settings(true, 1)))
                using (var neighbour = new TreeSimulation(Settings(false, 1, seed: 7)))
                {
                    subject.TryAddGroup(patrol, TreeGroupSettings.Default(64), out var a);
                    subject.TryAddGroup(errand, TreeGroupSettings.Default(64), out var b);
                    neighbour.TryAddGroup(errand, TreeGroupSettings.Default(64), out var c);
                    for (var index = 0; index < 64; index++)
                    {
                        if (withNeighbour) c.TryAdd(ThinkSchedule.Spread(3), out _);
                        a.TryAdd(ThinkSchedule.Spread(5), out _);
                        if (withNeighbour) c.TryAdd(ThinkSchedule.Spread(2), out _);
                        b.TryAdd(ThinkSchedule.Spread(5), out _);
                    }
                    for (uint tick = 1; tick <= 120; tick++)
                    {
                        if (withNeighbour) neighbour.Step(tick * 2);
                        subject.Step(tick);
                        var digest = Fnv.Create();
                        AppendDigest(a, ref digest, path);
                        AppendDigest(b, ref digest, path);
                        digests.Add(digest.Value);
                    }
                }
                return digests;
            }
            var alone = Run(false);
            var together = Run(true);
            Assert.That(together, Is.EqualTo(alone), "A second container stepping in between changes nothing.");
        }

        [Test]
        public void TheContainer_CreatesNoGameObjectOrComponent()
        {
            var patrol = Program(PatrolTree);
            var errand = Program(ErrandTree);
            var gameObjects = Resources.FindObjectsOfTypeAll<GameObject>().Length;
            var components = Resources.FindObjectsOfTypeAll<Component>().Length;
            var objects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Length;
            using (var simulation = new TreeSimulation(Settings(true, 1)))
            {
                simulation.TryAddGroup(patrol, TreeGroupSettings.Default(32), out var a);
                simulation.TryAddGroup(errand, TreeGroupSettings.Default(32), out var b);
                for (var index = 0; index < 32; index++)
                {
                    a.TryAdd(ThinkSchedule.Spread(5), out _);
                    b.TryAdd(ThinkSchedule.Spread(5), out _);
                }
                for (uint tick = 1; tick <= 60; tick++) simulation.Step(tick);
                Assert.That(Resources.FindObjectsOfTypeAll<GameObject>().Length, Is.EqualTo(gameObjects));
                Assert.That(Resources.FindObjectsOfTypeAll<Component>().Length, Is.EqualTo(components));
                Assert.That(Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Length, Is.EqualTo(objects), "No Unity object of any kind.");
            }
        }

        // ---- lifecycle --------------------------------------------------------------------------------

        [Test]
        public void RootCompletion_Restart_RestartsInPlaceAsAFreshInstance_Stop_KeepsTheResult()
        {
            var errand = Program(DeterministicErrandTree);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            {
                simulation.TryAddGroup(errand, TreeGroupSettings.Default(1), out var restarting);
                var stopSettings = TreeGroupSettings.Default(1);
                stopSettings.RootCompletion = RootCompletionPolicy.Stop;
                simulation.TryAddGroup(errand, stopSettings, out var stopping);
                restarting.TryAdd(out var a);
                stopping.TryAdd(out var b);
                var coreA = restarting.CoreForTests(0);
                var coreB = stopping.CoreForTests(0);
                Assert.That(coreA.InstanceId, Is.EqualTo(1ul), "Instance ids are container-local, in add order.");
                Assert.That(coreB.InstanceId, Is.EqualTo(2ul));

                var completionTicks = new List<uint>();
                for (uint tick = 1; tick <= 30; tick++)
                {
                    simulation.Step(tick);
                    var completions = restarting.GetRootCompletions();
                    if (completions.Length == 1)
                    {
                        Assert.That(completions[0].Agent, Is.EqualTo(a));
                        Assert.That(completions[0].Result, Is.EqualTo(NodeStatus.Success));
                        completionTicks.Add(tick);
                    }
                }
                Assert.That(completionTicks.Count, Is.GreaterThanOrEqualTo(3), "The agent finished and restarted several times.");
                Assert.That(restarting.IsAlive(a), Is.True, "Same handle after restarts.");
                Assert.That(coreA.InstanceId, Is.GreaterThan(2ul), "Each restart is a new container-local instance.");
                Assert.That(coreB.LastRootResult, Is.EqualTo(NodeStatus.Success));
                Assert.That(coreB.Ready, Is.False, "Stop keeps the result and stops thinking.");
                var updates = coreB.TotalUpdates;
                simulation.Step(31);
                Assert.That(coreB.TotalUpdates, Is.EqualTo(updates));
                Assert.That(stopping.TryRestart(b), Is.EqualTo(SimulationResult.Success));
                simulation.Step(32);
                Assert.That(coreB.TotalUpdates, Is.EqualTo(1ul), "TryRestart: a fresh instance thinks at the next think.");
                Assert.That(coreB.LastRootResult, Is.Null);
            }
        }

        [Test]
        public void ResetInPlace_EqualsAFreshlyCreatedInstance_InStateAndBehaviour()
        {
            var patrol = Program(PatrolTree);
            var semanticHash = new NativeHash256V1(patrol.Program.Header.CanonicalSemanticHash);
            var used = new ProductionTreeAgentCore(null);
            var fresh = new ProductionTreeAgentCore(null);
            try
            {
                Assert.That(used.TryBootstrapGenerated(patrol.Definition, patrol.Catalog, null, new TreeInstanceId(5), false, out var failure), Is.True, failure.ToString());
                used.GeneratedDispatch.SeedRandomStreams(Seed, semanticHash);
                for (long update = 1; update <= 40; update++)
                {
                    Assert.That(used.TryBeginUpdateAt(update * TickMicroseconds), Is.True);
                    used.RunSegment(null, false);
                }
                Assert.That(used.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
                Assert.That(used.Ready, Is.True);
                used.RunTeardown();
                Assert.That(used.TryResetInstance(new TreeInstanceId(9), out failure), Is.True, failure.ToString());
                used.GeneratedDispatch.SeedRandomStreams(Seed, semanticHash);

                Assert.That(fresh.TryBootstrapGenerated(patrol.Definition, patrol.Catalog, null, new TreeInstanceId(9), false, out failure), Is.True, failure.ToString());
                fresh.GeneratedDispatch.SeedRandomStreams(Seed, semanticHash);

                var usedBytes = new List<byte>();
                var freshBytes = new List<byte>();
                used.Machine.CopyInstanceStateForTests(usedBytes);
                fresh.Machine.CopyInstanceStateForTests(freshBytes);
                Assert.That(usedBytes, Is.EqualTo(freshBytes), "Machine instance state equals a fresh machine's.");
                used.GeneratedDispatch.CopyInstanceStateForTests(usedBytes);
                fresh.GeneratedDispatch.CopyInstanceStateForTests(freshBytes);
                Assert.That(usedBytes, Is.EqualTo(freshBytes), "Blackboard and every generated leaf slot equal a fresh adapter's.");
                Assert.That(used.TotalUpdates, Is.EqualTo(fresh.TotalUpdates));
                Assert.That(used.InstanceId, Is.EqualTo(fresh.InstanceId));

                var path = new List<uint>();
                for (long update = 1; update <= 60; update++)
                {
                    var time = (update + 7) * TickMicroseconds;
                    Assert.That(used.TryBeginUpdateAt(time), Is.True);
                    Assert.That(fresh.TryBeginUpdateAt(time), Is.True);
                    used.RunSegment(null, false);
                    fresh.RunSegment(null, false);
                    var a = Fnv.Create();
                    var b = Fnv.Create();
                    AppendCore(used, ref a, path);
                    AppendCore(fresh, ref b, path);
                    Assert.That(a.Value, Is.EqualTo(b.Value), "update " + update);
                }
            }
            finally
            {
                if (used.BeginDispose()) used.ReleaseStorage();
                if (fresh.BeginDispose()) fresh.ReleaseStorage();
            }
        }

        [Test]
        public void Reserve_GrowsCapacity_KeepingHandles_AndRemovedGroupsAreReleasedAfterTeardown()
        {
            var loop = Program(DeterministicLoopTree);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            {
                simulation.TryAddGroup(loop, TreeGroupSettings.Default(2), out var group);
                group.TryAdd(out var first);
                group.TryAdd(out var second);
                Assert.That(group.TryAdd(out _), Is.EqualTo(SimulationResult.CapacityExceeded));
                for (uint tick = 1; tick <= 5; tick++) simulation.Step(tick);
                Assert.That(group.TryReserve(4), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.Capacity, Is.EqualTo(4));
                Assert.That(group.IsAlive(first) && group.IsAlive(second), Is.True);
                Assert.That(group.TryAdd(out var third), Is.EqualTo(SimulationResult.Success));
                Assert.That(third.Index, Is.EqualTo(2));
                for (uint tick = 6; tick <= 10; tick++) simulation.Step(tick);
                Assert.That(group.CoreForTests(0).TotalUpdates, Is.EqualTo(10ul), "Growing keeps running agents untouched.");

                Assert.That(simulation.TryRemoveGroup(group), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryAdd(out _), Is.EqualTo(SimulationResult.Disposed));
                Assert.That(simulation.GroupCount, Is.EqualTo(1));
                simulation.Step(11); // teardown
                Assert.That(simulation.GroupCount, Is.EqualTo(1));
                simulation.Step(12); // release
                Assert.That(simulation.GroupCount, Is.Zero);
                Assert.That(group.IsValid, Is.False);
            }
            Assert.That(_programs[0].TryDispose(out var failure), Is.True, "The released group no longer holds the program: " + failure);
            _programs.Clear();
        }

        [Test]
        public void InvalidSettingsAndArguments_AreRefused()
        {
            var loop = Program(DeterministicLoopTree);
            Assert.Throws<ArgumentOutOfRangeException>(() => new TreeSimulation(default(TreeSimulationSettings)));
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            {
                Assert.Throws<ArgumentNullException>(() => simulation.TryAddGroup(null, TreeGroupSettings.Default(1), out _));
                Assert.That(simulation.TryAddGroup(loop, TreeGroupSettings.Default(0), out _), Is.EqualTo(SimulationResult.InvalidArgument));
                var zeroInterval = TreeGroupSettings.Default(1);
                zeroInterval.DefaultThinkInterval = 0;
                Assert.That(simulation.TryAddGroup(loop, zeroInterval, out _), Is.EqualTo(SimulationResult.InvalidArgument));
                simulation.TryAddGroup(loop, TreeGroupSettings.Default(1), out var group);
                Assert.That(group.TryAdd(ThinkSchedule.Every(0, 0), out _), Is.EqualTo(SimulationResult.InvalidArgument));
                Assert.That(group.TryWake(default), Is.EqualTo(SimulationResult.StaleHandle), "A null handle is refused.");
                group.TryAdd(out var agent);
                using (var other = new TreeSimulation(Settings(false, 1)))
                {
                    other.TryAddGroup(loop, TreeGroupSettings.Default(1), out var otherGroup);
                    otherGroup.TryAddGroupHandleCheck(agent);
                }
            }
            var disposed = new TreeSimulation(Settings(false, 1));
            disposed.Dispose();
            Assert.That(disposed.Step(1), Is.EqualTo(SimulationResult.Disposed));
            Assert.Throws<ObjectDisposedException>(() => disposed.TryAddGroup(loop, TreeGroupSettings.Default(1), out _));
        }
    }

    internal static class TreeGroupTestExtensions
    {
        /// <summary>A handle of group 1 used on another container's group 1 resolves by index only if ids match; both are 1, so this checks generation/slot logic still refuses a free slot.</summary>
        internal static void TryAddGroupHandleCheck(this TreeGroup group, AgentHandle foreign)
        {
            Assert.That(group.TryWake(foreign), Is.EqualTo(SimulationResult.StaleHandle), "No agent in that slot of this group yet.");
        }
    }
}
