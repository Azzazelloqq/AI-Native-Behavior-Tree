using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AIBT.Burst;
using AIBT.Mcp;
using AIBT.Simulation;
using AIBT.Tests.CodeGen.GenerationP10010;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine.TestTools;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// P12-002: the indexed input channel, <c>TreeGroup.TryBindInput(channel, rows, rowOfSlot)</c>. The agent in
    /// slot s reads <c>rows[rowOfSlot[s]]</c> through its ordinary <c>SnapshotReadHandle</c>. Each test names the
    /// item of "Semantics" in <c>Planning~/Evidence/P12-002/decision.md</c> it pins (S1-S9). Rows of the
    /// "sense" channel carry their row number in <c>Ally</c> (100 + row), so the order a node gives shows the
    /// row it read. Evidence: <c>Planning~/Evidence/P12-002/README.md</c>.
    /// </summary>
    public sealed class TreeSimulationIndexedInputTests
    {
        private const ulong Seed = 0x12_0002UL;
        private static readonly InputChannel<SenseInput> Sense = InputChannel<SenseInput>.Create("sense");
        private static readonly InputChannel<float> Speed = InputChannel<float>.Create("speed");
        private static readonly CommandChannel<OrderCommand> Order = CommandChannel<OrderCommand>.Create("order");

        // One order per think, built from the "sense" row the agent reads (Target = ally, Strength = threat);
        // the root succeeds and restarts at the next think (RootCompletionPolicy.Restart).
        internal const string ReportTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p12002.report"", ""name"": ""P12-002 report"",
  ""root"": ""order"",
  ""nodes"": {
    ""order"": { ""type"": ""aibt.tests.p10010.give-order"", ""typeVersion"": 1, ""parameters"": { ""scale"": 1.0 } }
  }
}";

        // An order only while the "speed" row the agent reads is above 1: two channels read by one agent.
        internal const string SpeedReportTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p12002.speed-report"", ""name"": ""P12-002 speed report"",
  ""root"": ""both"",
  ""nodes"": {
    ""both"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""fast"", ""order""] },
    ""fast"": { ""type"": ""aibt.tests.p10010.speed-above"", ""typeVersion"": 1, ""parameters"": { ""threshold"": 1.0 } },
    ""order"": { ""type"": ""aibt.tests.p10010.give-order"", ""typeVersion"": 1, ""parameters"": { ""scale"": 1.0 } }
  }
}";

        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        private readonly List<ProductionTreeProgram> _programs = new List<ProductionTreeProgram>();

        [TearDown]
        public void TearDown()
        {
            foreach (var program in _programs) program.Dispose();
            _programs.Clear();
            foreach (var item in _objects) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            _objects.Clear();
        }

        private ProductionTreeProgram Program(string json)
        {
            var asset = TreeSimulationIoTests.PrepareIoAsset(json);
            _objects.Add(asset);
            Assert.That(asset.TryCreateProgram(SimulationIoCatalog.TryCreateRuntimeCatalog, out var program, out var failure), Is.True, failure.ToString());
            _programs.Add(program);
            return program;
        }

        private static TreeSimulationSettings Settings(bool useJobs, int minimumJobBatch = 1)
        {
            var settings = TreeSimulationSettings.Default(TickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = minimumJobBatch;
            settings.RandomSeed = Seed;
            return settings;
        }

        /// <summary>The value of row <paramref name="row"/>: Ally 100 + row, Threat row / 8, Flag for odd rows.</summary>
        private static SenseInput Row(int row) => new SenseInput { Threat = row / 8f, Ally = 100 + row, Flag = (row & 1) != 0 };

        // ---- the rig ---------------------------------------------------------------------------------

        private sealed class Rig : IDisposable
        {
            internal TreeSimulation Simulation;
            internal TreeGroup Group;
            internal NativeArray<SenseInput> Rows;
            internal NativeArray<int> RowOf;
            internal readonly List<AgentHandle> Agents = new List<AgentHandle>();
            private readonly ActivePathNode[] _path = new ActivePathNode[64];

            public void Dispose()
            {
                Simulation.Dispose();
                if (Rows.IsCreated) Rows.Dispose();
                if (RowOf.IsCreated) RowOf.Dispose();
            }

            internal void Step(uint tick) => Assert.That(Simulation.Step(tick), Is.EqualTo(SimulationResult.Success), "Step(" + tick + ")");

            /// <summary>This step's orders: the ally each slot's order carries (absent when the slot gave none).</summary>
            internal Dictionary<int, int> Allies()
            {
                var orders = Group.GetCommands(Order);
                var allies = new Dictionary<int, int>();
                for (var index = 0; index < orders.Length; index++) allies.Add(orders[index].Agent.Index, orders[index].Value.Target);
                return allies;
            }

            internal Dictionary<int, OrderCommand> Orders()
            {
                var orders = Group.GetCommands(Order);
                var result = new Dictionary<int, OrderCommand>();
                for (var index = 0; index < orders.Length; index++) result.Add(orders[index].Agent.Index, orders[index].Value);
                return result;
            }

            /// <summary>The agents that thought in the step of <paramref name="tick"/>, with their reasons.</summary>
            internal Dictionary<int, ThinkReasons> Thinks(uint tick)
            {
                var thinks = new Dictionary<int, ThinkReasons>();
                foreach (var agent in Agents)
                {
                    if (Group.TryGetActivePath(agent, _path, out _, out var state) != SimulationResult.Success) continue;
                    if (state.HasThought && state.LastThinkTick == tick) thinks.Add(agent.Index, state.LastThinkReasons);
                }
                return thinks;
            }
        }

        /// <summary>
        /// A group of <paramref name="agents"/> agents (capacity at least that) reading "sense" through an
        /// indexed channel of <paramref name="rows"/> rows (row r = <see cref="Row"/>(r)) with
        /// <c>rowOfSlot[s] = rowOf(s)</c>.
        /// </summary>
        private static Rig NewRig(ProductionTreeProgram program, bool useJobs, int agents, int rows, Func<int, int> rowOf,
            Func<int, ThinkSchedule> schedule = null, int capacity = 0, bool wake = false)
        {
            capacity = Math.Max(capacity, agents);
            var rig = new Rig
            {
                Simulation = new TreeSimulation(Settings(useJobs)),
                Rows = new NativeArray<SenseInput>(rows, Allocator.Persistent),
                RowOf = new NativeArray<int>(capacity, Allocator.Persistent),
            };
            for (var row = 0; row < rows; row++) rig.Rows[row] = Row(row);
            for (var slot = 0; slot < capacity; slot++) rig.RowOf[slot] = rowOf(slot);
            Assert.That(rig.Simulation.TryAddGroup(program, TreeGroupSettings.Default(capacity), out rig.Group), Is.EqualTo(SimulationResult.Success));
            Assert.That(rig.Group.TryBindInput(Sense, rig.Rows, rig.RowOf), Is.EqualTo(SimulationResult.Success));
            if (wake) Assert.That(rig.Group.TrySetWakeOnChange(Sense, true), Is.EqualTo(SimulationResult.Success));
            for (var agent = 0; agent < agents; agent++)
            {
                Assert.That(rig.Group.TryAdd(schedule?.Invoke(agent) ?? ThinkSchedule.Every(1, 0), out var handle), Is.EqualTo(SimulationResult.Success));
                rig.Agents.Add(handle);
            }
            return rig;
        }

        /// <summary>
        /// A think woken by an input change: InputChanged set, Scheduled not. (ReportTree's root succeeds every
        /// think and restarts under RootCompletionPolicy.Restart, so FreshInstance comes with every think.)
        /// </summary>
        private static void AssertWokenByInput(ThinkReasons reasons, string label)
        {
            Assert.That(reasons & ThinkReasons.InputChanged, Is.EqualTo(ThinkReasons.InputChanged), label + ": " + reasons);
            Assert.That(reasons & ThinkReasons.Scheduled, Is.EqualTo(ThinkReasons.None), label + ": " + reasons);
        }

        private static void AssertPath(Rig rig, bool useJobs)
        {
            if (useJobs) Assert.That(rig.Group.JobBatchesForTests, Is.GreaterThan(0), "UseJobs with MinimumJobBatch=1 ran the agent-frame job");
            else Assert.That(rig.Group.JobBatchesForTests, Is.Zero);
        }

        // ---- 1. members read their row ----------------------------------------------------------------

        /// <summary>
        /// S1, S6: 12 agents in a group of capacity 16 read a rows array of 3 rows (shorter than the capacity)
        /// through rowOfSlot = slot % 3: each agent's order carries row slot % 3, not its slot row, in place and
        /// in jobs.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MembersReadTheirRow_NotTheirSlotRow(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 12, rows: 3, rowOf: slot => slot % 3, capacity: 16))
            {
                rig.Step(1);
                var orders = rig.Orders();
                Assert.That(orders.Keys, Is.EquivalentTo(Enumerable.Range(0, 12)));
                foreach (var pair in orders)
                {
                    var row = Row(pair.Key % 3);
                    Assert.That(pair.Value.Target, Is.EqualTo(row.Ally), "slot " + pair.Key);
                    Assert.That(pair.Value.Strength, Is.EqualTo(row.Threat), "slot " + pair.Key);
                    Assert.That(pair.Value.Urgent, Is.EqualTo(row.Flag), "slot " + pair.Key);
                }
                AssertPath(rig, useJobs);
            }
        }

        // ---- 2. digests -----------------------------------------------------------------------------------

        private const int PopulationAgents = 200;
        private const int PopulationRows = 20;
        private const int PopulationTicks = 150;

        /// <summary>
        /// 200 agents with Spread(5), wake on "sense", 20 rows rewritten every third tick, membership changing
        /// every tick (every 13th slot "no row"), 15 agents removed at tick 60 and added again at 61. The log of
        /// every order (slot, ally, threat bits), every think with its reasons and every fault per tick.
        /// </summary>
        private List<string> RunPopulation(ProductionTreeProgram program, in TreeSimulationSettings settings, out long jobBatches, out int woken)
        {
            var log = new List<string>();
            using (var simulation = new TreeSimulation(settings))
            using (var rowsOwner = new NativeArray<SenseInput>(PopulationRows, Allocator.Persistent))
            using (var rowOfOwner = new NativeArray<int>(PopulationAgents, Allocator.Persistent))
            {
                var rows = rowsOwner;
                var rowOf = rowOfOwner;
                Assert.That(simulation.TryAddGroup(program, TreeGroupSettings.Default(PopulationAgents), out var group), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryBindInput(Sense, rows, rowOf), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TrySetWakeOnChange(Sense, true), Is.EqualTo(SimulationResult.Success));
                var handles = new AgentHandle[PopulationAgents];
                for (var slot = 0; slot < PopulationAgents; slot++) group.TryAdd(ThinkSchedule.Spread(5), out handles[slot]);
                var path = new ActivePathNode[64];
                for (uint tick = 1; tick <= PopulationTicks; tick++)
                {
                    for (var row = 0; row < PopulationRows; row++)
                        rows[row] = new SenseInput { Threat = ((row * 7 + tick / 3) % 10) / 10f, Ally = (int)(row * 31 + tick / 3) % 97, Flag = ((row + tick / 3) & 1) != 0 };
                    for (var slot = 0; slot < PopulationAgents; slot++)
                        rowOf[slot] = slot % 13 == 0 ? -1 : (int)((slot * 7 + tick / 4) % PopulationRows);
                    if (tick == 60)
                        for (var slot = 10; slot < 25; slot++) Assert.That(group.TryRemove(handles[slot]), Is.EqualTo(SimulationResult.Success));
                    if (tick == 61)
                        for (var slot = 10; slot < 25; slot++) Assert.That(group.TryAdd(ThinkSchedule.Spread(5), out handles[slot]), Is.EqualTo(SimulationResult.Success));
                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    var orders = group.GetCommands(Order);
                    for (var index = 0; index < orders.Length; index++)
                        log.Add(tick + " order " + orders[index].Agent.Index + " " + orders[index].Value.Target + " "
                                + BitConverter.SingleToInt32Bits(orders[index].Value.Strength));
                    for (var slot = 0; slot < PopulationAgents; slot++)
                    {
                        if (group.TryGetActivePath(handles[slot], path, out _, out var state) != SimulationResult.Success) continue;
                        if (state.HasThought && state.LastThinkTick == tick) log.Add(tick + " think " + slot + " " + state.LastThinkReasons);
                    }
                    var faults = group.GetFaults();
                    for (var index = 0; index < faults.Length; index++) log.Add(tick + " fault " + faults[index].Agent.Index + " " + faults[index].Cause);
                }
                jobBatches = group.JobBatchesForTests;
                woken = (int)group.WokenByInputForTests;
            }
            return log;
        }

        /// <summary>
        /// S6: the same log in place (twice), in jobs with MinimumJobBatch 1 and 64, and with 0 and 1 job
        /// workers: one routine chooses the row on every path.
        /// </summary>
        [Test]
        public void Population_LogsAreIdentical_AcrossPathsWorkerCountsAndRuns()
        {
            var program = Program(ReportTree);
            var inPlace = RunPopulation(program, Settings(false), out var inPlaceBatches, out var woken);
            var again = RunPopulation(program, Settings(false), out _, out _);
            var jobs = RunPopulation(program, Settings(true), out var jobBatches, out _);
            var defaults = RunPopulation(program, Settings(true, 64), out _, out _);
            var workers = JobsUtility.JobWorkerCount;
            List<string> noWorkers, oneWorker;
            try
            {
                JobsUtility.JobWorkerCount = 0;
                noWorkers = RunPopulation(program, Settings(true), out _, out _);
                JobsUtility.JobWorkerCount = 1;
                oneWorker = RunPopulation(program, Settings(true), out _, out _);
            }
            finally
            {
                JobsUtility.JobWorkerCount = workers;
            }

            Assert.That(inPlace.Count(line => line.Contains(" order ")), Is.GreaterThan(5000), "Agents give orders.");
            Assert.That(inPlace.Where(line => line.Contains(" fault ")), Is.Empty);
            Assert.That(woken, Is.GreaterThan(500), "Changed rows and changed indices wake agents between their thinks.");
            Assert.That(inPlaceBatches, Is.Zero);
            Assert.That(jobBatches, Is.GreaterThanOrEqualTo(PopulationTicks));
            Assert.That(again, Is.EqualTo(inPlace), "second run in place");
            Assert.That(jobs, Is.EqualTo(inPlace), "UseJobs, MinimumJobBatch=1");
            Assert.That(defaults, Is.EqualTo(inPlace), "MinimumJobBatch=64");
            Assert.That(noWorkers, Is.EqualTo(inPlace), "0 job workers");
            Assert.That(oneWorker, Is.EqualTo(inPlace), "1 job worker");
        }

        // ---- 3. bind refusals -------------------------------------------------------------------------------

        /// <summary>
        /// S1, S2: rows must be created (any length, zero included); rowOfSlot must be created and at least
        /// Capacity long; an unknown channel or a wrong row type is refused as by the slot-row overload; a
        /// refused bind leaves the channel unbound. A rows array of length 0 with every index negative steps
        /// and reads zeros.
        /// </summary>
        [Test]
        public void Bind_RefusalsAndAnEmptyRowsArray()
        {
            var program = Program(ReportTree);
            using (var simulation = new TreeSimulation(Settings(false)))
            using (var rows = new NativeArray<SenseInput>(2, Allocator.Persistent))
            using (var empty = new NativeArray<SenseInput>(0, Allocator.Persistent))
            using (var rowOfOwner = new NativeArray<int>(4, Allocator.Persistent))
            using (var shortRowOf = new NativeArray<int>(3, Allocator.Persistent))
            using (var floats = new NativeArray<float>(2, Allocator.Persistent))
            {
                var rowOf = rowOfOwner;
                Assert.That(simulation.TryAddGroup(program, TreeGroupSettings.Default(4), out var group), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryAdd(out _), Is.EqualTo(SimulationResult.Success));

                Assert.That(group.TryBindInput(Sense, default(NativeArray<SenseInput>), rowOf), Is.EqualTo(SimulationResult.InvalidArgument), "rows not created");
                Assert.That(group.TryBindInput(Sense, rows, default(NativeArray<int>)), Is.EqualTo(SimulationResult.InvalidArgument), "rowOfSlot not created");
                Assert.That(group.TryBindInput(Sense, rows, shortRowOf), Is.EqualTo(SimulationResult.InvalidArgument), "rowOfSlot shorter than Capacity");
                Assert.That(group.TryBindInput(InputChannel<SenseInput>.Create("no-such-input"), rows, rowOf), Is.EqualTo(SimulationResult.UnknownChannel));
                Assert.That(group.TryBindInput(InputChannel<float>.Create("sense"), floats, rowOf), Is.EqualTo(SimulationResult.TypeMismatch));
                Assert.That(group.TryBindInput(default(InputChannel<SenseInput>), rows, rowOf), Is.EqualTo(SimulationResult.InvalidArgument));
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.InputMissing), "Refused binds leave the channel unbound.");

                for (var slot = 0; slot < 4; slot++) rowOf[slot] = -1;
                Assert.That(group.TryBindInput(Sense, empty, rowOf), Is.EqualTo(SimulationResult.Success), "a rows array of length 0");
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var orders = group.GetCommands(Order);
                Assert.That(orders.Length, Is.EqualTo(1));
                Assert.That(orders[0].Value.Target, Is.Zero, "no row: zeros");
                Assert.That(orders[0].Value.Strength, Is.Zero);
            }
        }

        // ---- 4 and 6. index out of range ------------------------------------------------------------------

        /// <summary>
        /// S4: an index at or beyond rows.Length in a live agent's slot refuses the whole step with
        /// InvalidArgument before anything runs (no update, the tick not taken), on the first step and on a
        /// later one; once the index is fixed the step runs.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OutOfRangeIndexOfALiveAgent_RefusesTheStep_NothingRuns(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 4, rows: 3, rowOf: slot => slot % 3))
            {
                rig.RowOf[2] = 3;
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.InvalidArgument), "index == rows.Length");
                Assert.That(rig.Simulation.LastTick, Is.Zero);
                for (var slot = 0; slot < 4; slot++) Assert.That(rig.Group.CoreForTests(slot).TotalUpdates, Is.Zero, "Nothing ran.");

                rig.RowOf[2] = 2;
                rig.Step(1);
                rig.RowOf[1] = int.MaxValue;
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.InvalidArgument));
                Assert.That(rig.Simulation.LastTick, Is.EqualTo(1u));
                for (var slot = 0; slot < 4; slot++) Assert.That(rig.Group.CoreForTests(slot).TotalUpdates, Is.EqualTo(1ul), "Nothing ran in the refused step.");
                rig.RowOf[1] = 1;
                rig.Step(2);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 100, [1] = 101, [2] = 102, [3] = 100 }));
            }
        }

        /// <summary>
        /// S4: slots without a live agent are not checked: a removed agent's slot (in its teardown step and
        /// after it), a free slot below the high-water index and the slots above it may hold any index. A slot
        /// reused by TryAdd keeps the index the game left there, so the step is refused until the game writes
        /// the newcomer's index.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OutOfRangeIndexOfASlotWithoutALiveAgent_DoesNotRefuse(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 4, rows: 2, rowOf: slot => slot % 2, capacity: 6))
            {
                rig.Step(1);
                rig.RowOf[4] = 99;
                rig.RowOf[5] = -7;
                Assert.That(rig.Group.TryRemove(rig.Agents[1]), Is.EqualTo(SimulationResult.Success));
                rig.RowOf[1] = 50;
                rig.Step(2);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 100, [2] = 100, [3] = 101 }), "teardown step");
                rig.RowOf[1] = 77;
                rig.Step(3);
                Assert.That(rig.Allies().Keys, Is.EquivalentTo(new[] { 0, 2, 3 }), "free slot 1 below the high-water index");

                Assert.That(rig.Group.TryAdd(ThinkSchedule.Every(1, 0), out var newcomer), Is.EqualTo(SimulationResult.Success));
                Assert.That(newcomer.Index, Is.EqualTo(1), "the lowest free slot is reused");
                Assert.That(rig.Simulation.Step(4), Is.EqualTo(SimulationResult.InvalidArgument), "the reused slot keeps the old index (77)");
                rig.RowOf[1] = 0;
                rig.Step(4);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 100, [1] = 100, [2] = 100, [3] = 101 }));
                AssertPath(rig, useJobs);
            }
        }

        // ---- 5. negative index ------------------------------------------------------------------------------

        /// <summary>S3: a negative index means "no row": the agent's node reads zeros, as from an unbound channel.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void NegativeIndex_ReadsZeros(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 4, rows: 3, rowOf: slot => slot == 1 ? -1 : slot == 2 ? int.MinValue : slot % 3))
            {
                rig.Step(1);
                var orders = rig.Orders();
                Assert.That(orders[0].Target, Is.EqualTo(100));
                Assert.That(orders[3].Target, Is.EqualTo(100));
                foreach (var slot in new[] { 1, 2 })
                {
                    Assert.That(orders[slot].Target, Is.Zero, "slot " + slot);
                    Assert.That(orders[slot].Strength, Is.Zero, "slot " + slot);
                    Assert.That(orders[slot].Urgent, Is.False, "slot " + slot);
                }
                AssertPath(rig, useJobs);
            }
        }

        // ---- 7. copy at step entry ----------------------------------------------------------------------------

        /// <summary>
        /// S2: a step sees the rows and the indices as they were at its entry. Writes the game makes during
        /// the step (here from inside it, after the copy and before the thinks) are seen only by the next step.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void WritesDuringAStep_AreSeenOnlyByTheNextStep(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 3, rows: 3, rowOf: slot => slot))
            {
                rig.Simulation.StepProbeForTests = () =>
                {
                    rig.Rows[0] = new SenseInput { Ally = 900 };
                    rig.RowOf[1] = 0;
                    rig.RowOf[2] = -1;
                };
                rig.Step(1);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 100, [1] = 101, [2] = 102 }), "the values at the step's entry");
                rig.Simulation.StepProbeForTests = null;
                rig.Step(2);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 900, [1] = 900, [2] = 0 }), "the next step");
                AssertPath(rig, useJobs);
            }
        }

        // ---- 8 and 9. wake on change ----------------------------------------------------------------------------

        /// <summary>
        /// S5: ten agents with Spread(5), rows 0 and 1 (rowOfSlot = slot % 2), wake on. Row 1 changes before
        /// tick 8: exactly the members of row 1 that are not due think with InputChanged (1, 5, 7, 9); the due
        /// agents (3, 8) think on schedule; the members of row 0 do not wake. Every think reads the new row.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void WakeOnChange_ARowChange_WakesTheMembersOfThatRowOnly(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 10, rows: 2, rowOf: slot => slot % 2, schedule: _ => ThinkSchedule.Spread(5), wake: true))
            {
                for (uint tick = 1; tick <= 7; tick++) rig.Step(tick);
                rig.Rows[1] = new SenseInput { Threat = 0.5f, Ally = 555, Flag = true };
                var before = rig.Group.WokenByInputForTests;
                rig.Step(8);
                var thinks = rig.Thinks(8);
                Assert.That(thinks.Keys, Is.EquivalentTo(new[] { 1, 3, 5, 7, 8, 9 }));
                foreach (var slot in new[] { 1, 5, 7, 9 }) AssertWokenByInput(thinks[slot], "slot " + slot);
                foreach (var slot in new[] { 3, 8 }) Assert.That(thinks[slot] & ThinkReasons.InputChanged, Is.EqualTo(ThinkReasons.None), "slot " + slot);
                Assert.That(rig.Group.WokenByInputForTests - before, Is.EqualTo(4));
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [1] = 555, [3] = 555, [5] = 555, [7] = 555, [8] = 100, [9] = 555 }));
                AssertPath(rig, useJobs);
            }
        }

        /// <summary>
        /// S5: an index change wakes an agent only when the value it would read differs. Rows 0 and 1 are
        /// equal, row 2 differs. Before tick 8 agent 1 moves 0 → 1 (equal value: no wake, it next thinks at
        /// 11 on schedule) and agent 2 moves 0 → 2 (different value: woken at 8 with InputChanged).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void WakeOnChange_AnIndexChange_WakesOnlyWhenTheValueDiffers(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 5, rows: 3, rowOf: _ => 0, schedule: _ => ThinkSchedule.Spread(5), wake: true))
            {
                rig.Rows[1] = rig.Rows[0];
                for (uint tick = 1; tick <= 7; tick++) rig.Step(tick);
                rig.RowOf[1] = 1;
                rig.RowOf[2] = 2;
                rig.Step(8);
                var thinks = rig.Thinks(8);
                Assert.That(thinks.Keys, Is.EquivalentTo(new[] { 2, 3 }), "agent 3 is due; agent 2 is woken; agent 1 is not");
                AssertWokenByInput(thinks[2], "agent 2");
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [2] = 102, [3] = 100 }));
                for (uint tick = 9; tick <= 11; tick++) rig.Step(tick);
                Assert.That(rig.Thinks(11).Keys, Is.EquivalentTo(new[] { 1 }));
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [1] = 100 }), "row 1 holds the same value as row 0");
                AssertPath(rig, useJobs);
            }
        }

        // ---- 10. one index array, two channels ----------------------------------------------------------------

        /// <summary>
        /// Two indexed channels of different row types share one rowOfSlot array: each agent reads row
        /// slot % 3 of both ("speed" decides whether it gives an order, "sense" what the order says).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OneIndexArray_SharedByTwoChannels(bool useJobs)
        {
            var program = Program(SpeedReportTree);
            using (var simulation = new TreeSimulation(Settings(useJobs)))
            using (var senseOwner = new NativeArray<SenseInput>(3, Allocator.Persistent))
            using (var speed = new NativeArray<float>(new[] { 0.5f, 2f, 3f }, Allocator.Persistent))
            using (var rowOfOwner = new NativeArray<int>(9, Allocator.Persistent))
            {
                var sense = senseOwner;
                var rowOf = rowOfOwner;
                for (var row = 0; row < 3; row++) sense[row] = Row(row);
                for (var slot = 0; slot < 9; slot++) rowOf[slot] = slot % 3;
                Assert.That(simulation.TryAddGroup(program, TreeGroupSettings.Default(9), out var group), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryBindInput(Sense, sense, rowOf), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryBindInput(Speed, speed, rowOf), Is.EqualTo(SimulationResult.Success));
                for (var slot = 0; slot < 9; slot++) group.TryAdd(out _);
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var orders = group.GetCommands(Order);
                var allies = new Dictionary<int, int>();
                for (var index = 0; index < orders.Length; index++) allies.Add(orders[index].Agent.Index, orders[index].Value.Target);
                Assert.That(allies, Is.EquivalentTo(new Dictionary<int, int> { [1] = 101, [2] = 102, [4] = 101, [5] = 102, [7] = 101, [8] = 102 }),
                    "row 0 (speed 0.5) gives no order; rows 1 and 2 do, with their own sense row");
                if (useJobs) Assert.That(group.JobBatchesForTests, Is.GreaterThan(0));
            }
        }

        // ---- 11. TryReserve -----------------------------------------------------------------------------------

        /// <summary>
        /// S2, S1: after TryReserve the indices must be rebound (they must be at least Capacity long), the rows
        /// need not (their length is not tied to Capacity).
        /// </summary>
        [Test]
        public void TryReserve_NeedsTheIndicesRebound_NotTheRows()
        {
            using (var rig = NewRig(Program(ReportTree), false, agents: 4, rows: 2, rowOf: slot => slot % 2))
            using (var grownOwner = new NativeArray<int>(8, Allocator.Persistent))
            {
                var grown = grownOwner;
                rig.Step(1);
                Assert.That(rig.Group.TryReserve(8), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.InvalidArgument), "rowOfSlot shorter than the new Capacity");
                Assert.That(rig.Simulation.LastTick, Is.EqualTo(1u));
                for (var slot = 0; slot < 8; slot++) grown[slot] = 1 - slot % 2;
                Assert.That(rig.Group.TryBindInput(Sense, rig.Rows, grown), Is.EqualTo(SimulationResult.Success), "the same 2 rows");
                rig.Step(2);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 101, [1] = 100, [2] = 101, [3] = 100 }));
            }
        }

        // ---- 12. switching modes ---------------------------------------------------------------------------

        /// <summary>
        /// S7: binding the same channel again with the other overload switches the mode: indexed (squad
        /// rows), then slot rows, then indexed again; unbinding refuses steps with InputMissing as before.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void BindingAgain_SwitchesBetweenSlotRowsAndIndexedRows(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 4, rows: 2, rowOf: slot => slot % 2))
            using (var slotRowsOwner = new NativeArray<SenseInput>(4, Allocator.Persistent))
            {
                var slotRows = slotRowsOwner;
                for (var slot = 0; slot < 4; slot++) slotRows[slot] = new SenseInput { Ally = 200 + slot };
                rig.Step(1);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 100, [1] = 101, [2] = 100, [3] = 101 }), "indexed");
                Assert.That(rig.Group.TryBindInput(Sense, slotRows), Is.EqualTo(SimulationResult.Success));
                rig.RowOf[0] = 99;
                rig.Step(2);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 200, [1] = 201, [2] = 202, [3] = 203 }), "slot rows; the index array is no longer read");
                rig.RowOf[0] = 1;
                Assert.That(rig.Group.TryBindInput(Sense, rig.Rows, rig.RowOf), Is.EqualTo(SimulationResult.Success));
                rig.Step(3);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 101, [1] = 101, [2] = 100, [3] = 101 }), "indexed again");
                Assert.That(rig.Group.TryUnbindInput(Sense), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(4), Is.EqualTo(SimulationResult.InputMissing));
                AssertPath(rig, useJobs);
            }
        }

        // ---- 13. a game job still writing ------------------------------------------------------------------

        private struct FillJob : IJob
        {
            public NativeArray<int> Target;
            public int Value;

            public void Execute()
            {
                for (var index = 0; index < Target.Length; index++) Target[index] = Value;
            }
        }

        private struct FillRowsJob : IJob
        {
            public NativeArray<SenseInput> Target;

            public void Execute()
            {
                for (var index = 0; index < Target.Length; index++) Target[index] = new SenseInput { Ally = 300 + index };
            }
        }

        /// <summary>
        /// S2: membership written by a game job. While a job that writes the indices (or the rows) has not
        /// completed, Step returns InputMissing and nothing runs (with collection safety checks); after
        /// Complete the step reads what the job wrote.
        /// </summary>
        [Test]
        public void IndicesOrRowsWrittenByARunningGameJob_RefuseTheStepWithInputMissing()
        {
            using (var rig = NewRig(Program(ReportTree), false, agents: 3, rows: 3, rowOf: slot => slot))
            {
                var handle = new FillJob { Target = rig.RowOf, Value = 2 }.Schedule();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.InputMissing), "the indices are being written");
                Assert.That(rig.Group.CoreForTests(0).TotalUpdates, Is.Zero);
#endif
                handle.Complete();
                rig.Step(1);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 102, [1] = 102, [2] = 102 }));

                handle = new FillRowsJob { Target = rig.Rows }.Schedule();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.InputMissing), "the rows are being written");
#endif
                handle.Complete();
                rig.Step(2);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 302, [1] = 302, [2] = 302 }));
            }
        }

        // ---- 14. non-canonical row -----------------------------------------------------------------------------

        /// <summary>
        /// A non-canonical value (NaN) in one row faults every agent that reads that row with InvalidEncoding
        /// and no other agent; the step goes on.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void NonCanonicalRow_FaultsEveryReaderOfThatRowOnly(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 9, rows: 3, rowOf: slot => slot % 3))
            {
                rig.Rows[1] = new SenseInput { Threat = float.NaN, Ally = 101 };
                rig.Step(1);
                var faults = rig.Group.GetFaults();
                Assert.That(Enumerable.Range(0, faults.Length).Select(index => faults[index].Agent.Index), Is.EquivalentTo(new[] { 1, 4, 7 }));
                Assert.That(Enumerable.Range(0, faults.Length).Select(index => faults[index].Cause), Is.All.EqualTo(BurstContextResult.InvalidEncoding));
                Assert.That(rig.Allies().Keys, Is.EquivalentTo(new[] { 0, 2, 3, 5, 6, 8 }));
                AssertPath(rig, useJobs);
            }
        }

        // ---- 15. no allocation -----------------------------------------------------------------------------------

        /// <summary>
        /// S1, S2: 500 agents with Spread(5), 50 rows, wake on, every row and every index rewritten before
        /// every step: no managed allocation in 100 warmed-up steps, counted by the GC.Alloc profiler recorder
        /// (review follow-up: GC.GetAllocatedBytesForCurrentThread reports 0 in this Editor, so it is not used).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Step_WithIndexedInputAndWake_AllocatesNothing_AfterWarmUp(bool useJobs)
        {
            const int agents = 500;
            const int squads = 50;
            using (var rig = NewRig(Program(ReportTree), useJobs, agents, squads, slot => slot / 10, _ => ThinkSchedule.Spread(5), wake: true))
            {
                void Write(uint tick)
                {
                    for (var row = 0; row < squads; row++) rig.Rows[row] = new SenseInput { Threat = (row + tick / 4) % 7 / 7f, Ally = row, Flag = (tick & 4) != 0 };
                    for (var slot = 0; slot < agents; slot++) rig.RowOf[slot] = (int)((slot / 10 + tick / 9) % squads);
                }

                // The warm-up makes every call the measured loop makes (GetCommands included: its first call JITs).
                var orders = 0;
                uint tick = 0;
                for (; tick < 30; tick++)
                {
                    Write(tick + 1);
                    rig.Step(tick + 1);
                    orders += rig.Group.GetCommands(Order).Length;
                }
                var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
                recorder.FilterToCurrentThread();
                recorder.enabled = false;
                recorder.enabled = true;
                orders = 0;
                for (var n = 0; n < 100; n++)
                {
                    tick++;
                    Write(tick);
                    rig.Simulation.Step(tick);
                    orders += rig.Group.GetCommands(Order).Length;
                }
                recorder.enabled = false;
                var blocks = recorder.sampleBlockCount;
                Assert.That(rig.Simulation.LastTick, Is.EqualTo(tick), "Every measured step ran.");
                Assert.That(orders, Is.GreaterThan(100 * agents / 5), "Agents read rows and give orders, woken ones included.");
                Assert.That(blocks, Is.Zero, blocks + " GC.Alloc samples in 100 warmed-up steps.");
                AssertPath(rig, useJobs);
            }
        }

        // ---- 16. busy -------------------------------------------------------------------------------------------

        /// <summary>The indexed bind during a step returns Busy, as the other calls do.</summary>
        [Test]
        public void IndexedBind_DuringAStep_ReturnsBusy()
        {
            using (var rig = NewRig(Program(ReportTree), false, agents: 2, rows: 1, rowOf: _ => 0))
            {
                var seen = new List<SimulationResult>();
                rig.Simulation.StepProbeForTests = () => seen.Add(rig.Group.TryBindInput(Sense, rig.Rows, rig.RowOf));
                rig.Step(1);
                Assert.That(seen, Is.EqualTo(new[] { SimulationResult.Busy }));
                rig.Simulation.StepProbeForTests = null;
                Assert.That(rig.Group.TryBindInput(Sense, rig.Rows, rig.RowOf), Is.EqualTo(SimulationResult.Success), "after the step");
            }
        }

        // ---- 17. trace and MCP ------------------------------------------------------------------------------------

        private static List<AgentTraceRecord> InputFacts(TreeGroup group, AgentHandle agent)
        {
            using (var records = new NativeList<AgentTraceRecord>(Allocator.Temp))
            {
                Assert.That(group.TryCopyTrace(agent, records, out _), Is.EqualTo(SimulationResult.Success));
                var result = new List<AgentTraceRecord>();
                for (var index = 0; index < records.Length; index++)
                    if (records[index].Fact == AgentTraceFact.Input) result.Add(records[index]);
                return result;
            }
        }

        /// <summary>
        /// S8: the Input fact of a think says which row was read (<see cref="AgentTraceRecord.InputRow"/>):
        /// the index for an indexed channel, -1 for "no row" (a negative index; the value is zeros), the slot
        /// for a slot-row channel. The value is that row's canonical bytes (ally first).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Trace_TheInputFactCarriesTheRowAndItsValue(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 3, rows: 3, rowOf: slot => slot == 0 ? 2 : slot == 1 ? -1 : 0))
            using (var slotRowsOwner = new NativeArray<SenseInput>(3, Allocator.Persistent))
            {
                var slotRows = slotRowsOwner;
                foreach (var agent in rig.Agents) Assert.That(rig.Group.TrySetTraced(agent, true), Is.EqualTo(SimulationResult.Success));
                rig.Step(1);
                var expected = new[] { (Row: 2, Ally: 102), (Row: -1, Ally: 0), (Row: 0, Ally: 100) };
                for (var slot = 0; slot < 3; slot++)
                {
                    var input = InputFacts(rig.Group, rig.Agents[slot]).Single();
                    Assert.That(input.Tick, Is.EqualTo(1u));
                    Assert.That(input.InputRow, Is.EqualTo(expected[slot].Row), "slot " + slot);
                    Assert.That((int)(uint)input.ValueLow, Is.EqualTo(expected[slot].Ally), "slot " + slot + ": ally, canonical bytes 0-3");
                }

                for (var slot = 0; slot < 3; slot++) slotRows[slot] = new SenseInput { Ally = 200 + slot };
                Assert.That(rig.Group.TryBindInput(Sense, slotRows), Is.EqualTo(SimulationResult.Success));
                rig.Step(2);
                for (var slot = 0; slot < 3; slot++)
                {
                    var input = InputFacts(rig.Group, rig.Agents[slot]).Last();
                    Assert.That(input.Tick, Is.EqualTo(2u));
                    Assert.That(input.InputRow, Is.EqualTo(slot), "slot rows: the row is the slot");
                    Assert.That((int)(uint)input.ValueLow, Is.EqualTo(200 + slot));
                }
            }
        }

        /// <summary>
        /// S8: MCP <c>read_agent_trace</c> reports the row of an Input fact as "row" (an added field; the other
        /// fields are unchanged).
        /// </summary>
        [Test]
        public void McpReadAgentTrace_ReportsTheRowOfAnInputFact()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aibt-p12002-" + Guid.NewGuid().ToString("N"), "Assets");
            using (var rig = NewRig(Program(ReportTree), false, agents: 2, rows: 4, rowOf: slot => slot == 0 ? 3 : -1))
            {
                Assert.That(rig.Group.TrySetTraced(rig.Agents[0], true), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.TrySetTraced(rig.Agents[1], true), Is.EqualTo(SimulationResult.Success));
                rig.Step(1);
                Assert.That(TreeSimulationDebug.Register(rig.Simulation, "p12002-indexed"), Is.True);
                try
                {
                    foreach (var (slot, row) in new[] { (0, 3), (1, -1) })
                    {
                        var request = "{\"tool\":\"read_agent_trace\",\"args\":{\"simulation\":\"p12002-indexed\",\"group\":" + rig.Group.Id
                                      + ",\"agent\":" + slot + "},\"grantedCategories\":[\"Read\"]}";
                        var response = McpToolDispatcher.Dispatch(request, root);
                        var input = Regex.Match(response, "\\{[^{}]*\"fact\"\\s*:\\s*\"Input\"[^{}]*\\}");
                        Assert.That(input.Success, Is.True, response);
                        Assert.That(Regex.IsMatch(input.Value, "\"row\"\\s*:\\s*" + row + "\\b"), Is.True, input.Value);
                        Assert.That(Regex.IsMatch(input.Value, "\"valueSize\"\\s*:\\s*12\\b"), Is.True, input.Value);
                    }
                }
                finally
                {
                    TreeSimulationDebug.Unregister(rig.Simulation);
                }
            }
        }

        // ---- 18. review follow-up S1: the agent-frame job runs the indexed routine under Burst ----------------

        private const int BurstSettledSteps = 10;
        private const int BurstMinimumSteps = 120;
        private const int BurstMaximumSteps = 6000;
        // As P11-014's BurstExecutionPathTests: the first asynchronous compilation of a catalog's job can take long.
        private const double BurstWaitSeconds = 90.0;

        /// <summary>
        /// Review follow-up S1, built as P11-014's <c>BurstExecutionPathTests</c>: the agent-frame job runs the
        /// indexed routine Burst-compiled. Twelve agents (Capacity 16) read 5 rows through indices that are never
        /// their slot (slot s reads (s + 2) % 5; slot 11 reads -1, "no row"); the rows are rewritten before every
        /// step (Ally = 1000 × tick + row), so only the indexed routine reading this step's copy gives the expected
        /// orders. One step per Editor frame, every step checked, until every agent's last job run is Burst
        /// (<c>AgentFrameAgentV1.RanManaged</c> is set by a <c>[BurstDiscard]</c> call) for
        /// <see cref="BurstSettledSteps"/> steps in a row. Burst off, or not settled within
        /// <see cref="BurstMinimumSteps"/> steps and <see cref="BurstWaitSeconds"/> seconds (at most
        /// <see cref="BurstMaximumSteps"/>): Inconclusive, not passed. A job compiled against a stale runtime
        /// (Planning~/Evidence/P12-002/README.md, "The Burst finding") gives wrong orders and fails.
        /// </summary>
        [UnityTest]
        public IEnumerator AgentFrameJob_RunsTheIndexedRoutineBurstCompiled()
        {
            Assume.That(BurstCompiler.IsEnabled, Is.True, "Burst compilation is off in this Editor (Jobs > Burst > Enable Compilation)");
            const int agents = 12;
            const int rows = 5;
            using (var rig = NewRig(Program(ReportTree), true, agents, rows, slot => slot == 11 ? -1 : (slot + 2) % rows, capacity: 16))
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var settled = 0;
                var firstBurstTick = 0u;
                var managedSteps = 0;
                var burstSteps = 0;
                uint tick = 0;
                while (tick < BurstMaximumSteps)
                {
                    tick++;
                    for (var row = 0; row < rows; row++)
                        rig.Rows[row] = new SenseInput { Threat = row / 8f, Ally = (int)(1000 * tick) + row, Flag = (row & 1) != 0 };
                    var batches = rig.Group.JobBatchesForTests;
                    rig.Step(tick);
                    Assert.That(rig.Group.JobBatchesForTests, Is.EqualTo(batches + 1), "tick " + tick + ": the agent-frame job ran");
                    var orders = rig.Orders();
                    Assert.That(orders.Keys, Is.EquivalentTo(Enumerable.Range(0, agents)), "tick " + tick);
                    for (var slot = 0; slot < agents; slot++)
                    {
                        var row = slot == 11 ? -1 : (slot + 2) % rows;
                        var expectedAlly = row < 0 ? 0 : (int)(1000 * tick) + row;
                        Assert.That(orders[slot].Target, Is.EqualTo(expectedAlly), "tick " + tick + " slot " + slot);
                        Assert.That(orders[slot].Strength, Is.EqualTo(row < 0 ? 0f : row / 8f), "tick " + tick + " slot " + slot);
                    }

                    var managed = ManagedAgentsInTable(rig.Group, agents);
                    if (managed == 0)
                    {
                        burstSteps++;
                        settled++;
                        if (firstBurstTick == 0) firstBurstTick = tick;
                    }
                    else
                    {
                        managedSteps++;
                        settled = 0;
                    }
                    if (settled >= BurstSettledSteps) break;
                    if (tick >= BurstMinimumSteps && clock.Elapsed.TotalSeconds >= BurstWaitSeconds) break;
                    // One Editor frame between steps, so the asynchronous Burst compilation can finish.
                    yield return null;
                }
                var summary = "P12-002 review S1: " + tick + " steps, " + clock.Elapsed.TotalSeconds.ToString("0.0") + " s, first Burst step "
                              + (firstBurstTick == 0 ? "none" : firstBurstTick.ToString()) + ", managed steps " + managedSteps + ", Burst steps " + burstSteps;
                UnityEngine.Debug.Log(summary);
                if (settled < BurstSettledSteps)
                    Assert.Inconclusive("The agent-frame job did not run Burst-compiled for " + BurstSettledSteps + " steps in a row within the bound ("
                                        + summary + "): Burst may still be compiling.");
            }
        }

        /// <summary>How many of the first <paramref name="count"/> agent-frame entries ran managed code in their last job run.</summary>
        private static int ManagedAgentsInTable(TreeGroup group, int count)
        {
            var field = typeof(TreeGroup).GetField("_table", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "TreeGroup._table");
            var table = (UnsafeList<AgentFrameAgentV1>)field.GetValue(group);
            var managed = 0;
            for (var index = 0; index < Math.Min(table.Length, count); index++)
                if (table[index].RanManaged != 0) managed++;
            return managed;
        }

        // ---- 19. review follow-up: the cheap untested cases of the independent review's section 4 -------------

        /// <summary>
        /// A rows array of length 0 with a live agent's index at 0 or above: the index is at or beyond the rows'
        /// length, so the step is refused with InvalidArgument and nothing runs; with -1 ("no row") it runs.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void EmptyRowsArray_WithALiveIndexAtZeroOrAbove_RefusesTheStep(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 2, rows: 0, rowOf: slot => slot == 0 ? 0 : -1))
            {
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.InvalidArgument), "index 0 of a rows array of length 0");
                Assert.That(rig.Simulation.LastTick, Is.Zero);
                for (var slot = 0; slot < 2; slot++) Assert.That(rig.Group.CoreForTests(slot).TotalUpdates, Is.Zero, "Nothing ran.");
                rig.RowOf[0] = -1;
                rig.Step(1);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 0, [1] = 0 }), "no row: zeros");
                AssertPath(rig, useJobs);
            }
        }

        /// <summary>
        /// A group with no agent and an indexed channel steps: no index is checked (every slot holds 7, beyond
        /// the 3 rows), no job runs. An agent added later is checked: its slot's 7 refuses the step until the
        /// game writes its index, then it reads its row.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void NoAgents_WithAnIndexedChannel_Steps_AndALaterAgentIsChecked(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 0, rows: 3, rowOf: _ => 7, capacity: 4))
            {
                rig.Step(1);
                rig.Step(2);
                Assert.That(rig.Simulation.LastTick, Is.EqualTo(2u));
                Assert.That(rig.Group.GetCommands(Order).Length, Is.Zero);
                Assert.That(rig.Group.JobBatchesForTests, Is.Zero, "no agent, no job");
                Assert.That(rig.Group.TryAdd(ThinkSchedule.Every(1, 0), out var agent), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(3), Is.EqualTo(SimulationResult.InvalidArgument), "its slot holds 7");
                rig.RowOf[agent.Index] = 2;
                rig.Step(3);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [agent.Index] = 102 }));
                AssertPath(rig, useJobs);
            }
        }

        /// <summary>
        /// Unbinding an indexed channel and binding it indexed again, to new rows and new indices: steps read the
        /// new arrays; the old index array is no longer read (an index there beyond the new rows refuses nothing).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UnbindThenBindIndexedAgain_ReadsTheNewArrays(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 3, rows: 2, rowOf: slot => slot % 2))
            using (var newRowsOwner = new NativeArray<SenseInput>(4, Allocator.Persistent))
            using (var newRowOfOwner = new NativeArray<int>(3, Allocator.Persistent))
            {
                var newRows = newRowsOwner;
                var newRowOf = newRowOfOwner;
                rig.Step(1);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 100, [1] = 101, [2] = 100 }));
                Assert.That(rig.Group.TryUnbindInput(Sense), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.InputMissing));
                for (var row = 0; row < 4; row++) newRows[row] = new SenseInput { Ally = 300 + row };
                newRowOf[0] = 3;
                newRowOf[1] = -1;
                newRowOf[2] = 0;
                Assert.That(rig.Group.TryBindInput(Sense, newRows, newRowOf), Is.EqualTo(SimulationResult.Success));
                rig.RowOf[0] = 99;
                rig.Step(2);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 303, [1] = 0, [2] = 300 }));
                AssertPath(rig, useJobs);
            }
        }

        /// <summary>
        /// Binding indexed again with a rows array of another length (2, then 6, then 3): the group's row copy
        /// follows the new length. An index valid only for the longer array is read; after the shorter array is
        /// bound the same index refuses the step until the game fixes it.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void IndexedToIndexed_WithARowsArrayOfAnotherLength(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 3, rows: 2, rowOf: slot => slot % 2))
            using (var longOwner = new NativeArray<SenseInput>(6, Allocator.Persistent))
            using (var shortOwner = new NativeArray<SenseInput>(3, Allocator.Persistent))
            {
                var longRows = longOwner;
                var shortRows = shortOwner;
                for (var row = 0; row < 6; row++) longRows[row] = new SenseInput { Ally = 400 + row };
                for (var row = 0; row < 3; row++) shortRows[row] = new SenseInput { Ally = 500 + row };
                rig.Step(1);
                Assert.That(rig.Group.TryBindInput(Sense, longRows, rig.RowOf), Is.EqualTo(SimulationResult.Success));
                rig.RowOf[0] = 5;
                rig.RowOf[1] = 0;
                rig.RowOf[2] = 3;
                rig.Step(2);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 405, [1] = 400, [2] = 403 }), "6 rows");
                Assert.That(rig.Group.TryBindInput(Sense, shortRows, rig.RowOf), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(3), Is.EqualTo(SimulationResult.InvalidArgument), "indices 5 and 3 are beyond 3 rows");
                Assert.That(rig.Simulation.LastTick, Is.EqualTo(2u));
                rig.RowOf[0] = 2;
                rig.RowOf[2] = 1;
                rig.Step(3);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 502, [1] = 500, [2] = 501 }), "3 rows");
                AssertPath(rig, useJobs);
            }
        }

        /// <summary>
        /// TryReserve with wake on keeps what each agent last saw. After the reserve (and the indices rebound, at
        /// least Capacity long) nothing unchanged wakes anyone; a row change wakes exactly the members of that row
        /// that are not due; an agent added in a grown slot reads its row.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TryReserve_WithWakeOn_KeepsWhatEachAgentLastSaw(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 4, rows: 2, rowOf: slot => slot % 2, schedule: _ => ThinkSchedule.Spread(5), wake: true))
            using (var grownOwner = new NativeArray<int>(8, Allocator.Persistent))
            {
                var grown = grownOwner;
                for (uint tick = 1; tick <= 5; tick++) rig.Step(tick);
                Assert.That(rig.Group.TryReserve(8), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(6), Is.EqualTo(SimulationResult.InvalidArgument), "rowOfSlot shorter than the new Capacity");
                for (var slot = 0; slot < 8; slot++) grown[slot] = slot < 4 ? slot % 2 : -1;
                grown[4] = 1;
                Assert.That(rig.Group.TryBindInput(Sense, rig.Rows, grown), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.TryAdd(ThinkSchedule.Every(1, 0), out var newcomer), Is.EqualTo(SimulationResult.Success));
                Assert.That(newcomer.Index, Is.EqualTo(4), "the first grown slot");
                rig.Agents.Add(newcomer);
                rig.Step(6);
                Assert.That(rig.Thinks(6).Keys, Is.EquivalentTo(new[] { 1, 4 }), "agent 1 is due, the newcomer thinks every tick; nothing changed, nobody wakes");
                rig.Step(7);
                rig.Rows[1] = new SenseInput { Threat = 0.5f, Ally = 555, Flag = true };
                rig.Step(8);
                var thinks = rig.Thinks(8);
                Assert.That(thinks.Keys, Is.EquivalentTo(new[] { 1, 3, 4 }), "agent 1 is woken; agent 3 and the newcomer are due");
                AssertWokenByInput(thinks[1], "agent 1");
                Assert.That(thinks[3] & ThinkReasons.InputChanged, Is.EqualTo(ThinkReasons.None), "agent 3 is due");
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [1] = 555, [3] = 555, [4] = 555 }));
                AssertPath(rig, useJobs);
            }
        }

        /// <summary>
        /// Switching modes with wake on: what each agent last saw is kept, so the first compare after a switch
        /// sets the value the agent now reads (its slot row, or the row its index selects) against the value it
        /// read at its last think under the old mode. An agent that is not due wakes when, and only when, the two
        /// differ; the switch itself wakes no one.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SwitchingModes_WithWakeOn_TheFirstCompareIsAgainstTheValueReadUnderTheOldMode(bool useJobs)
        {
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 4, rows: 2, rowOf: slot => slot % 2, schedule: _ => ThinkSchedule.Spread(5), wake: true))
            using (var slotRowsOwner = new NativeArray<SenseInput>(4, Allocator.Persistent))
            {
                var slotRows = slotRowsOwner;
                for (uint tick = 1; tick <= 5; tick++) rig.Step(tick);
                slotRows[0] = Row(0);
                slotRows[1] = Row(1);
                slotRows[2] = new SenseInput { Ally = 202 };
                slotRows[3] = new SenseInput { Ally = 203 };
                Assert.That(rig.Group.TryBindInput(Sense, slotRows), Is.EqualTo(SimulationResult.Success));
                rig.Step(6);
                var thinks = rig.Thinks(6);
                Assert.That(thinks.Keys, Is.EquivalentTo(new[] { 1, 2, 3 }), "agent 1 is due; agents 2 and 3 read another value; agent 0 reads the value it read before");
                AssertWokenByInput(thinks[2], "agent 2, indexed to slot rows");
                AssertWokenByInput(thinks[3], "agent 3, indexed to slot rows");
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [1] = 101, [2] = 202, [3] = 203 }));

                Assert.That(rig.Group.TryBindInput(Sense, rig.Rows, rig.RowOf), Is.EqualTo(SimulationResult.Success));
                rig.Step(7);
                thinks = rig.Thinks(7);
                Assert.That(thinks.Keys, Is.EquivalentTo(new[] { 2, 3 }), "agent 2 is due; agent 3 reads row 1 after 203; agents 0 and 1 read what they read before");
                AssertWokenByInput(thinks[3], "agent 3, slot rows to indexed");
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [2] = 100, [3] = 101 }));
                AssertPath(rig, useJobs);
            }
        }

        /// <summary>
        /// A rows array longer than Capacity (10 rows, Capacity 4): agents read rows beyond Capacity, in place and
        /// in the agent-frame job, and every bound row is copied at step entry.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RowsLongerThanCapacity_AreReadBeyondCapacity(bool useJobs)
        {
            var indices = new[] { 9, 7, 4, 0 };
            using (var rig = NewRig(Program(ReportTree), useJobs, agents: 4, rows: 10, rowOf: slot => indices[slot]))
            {
                rig.Step(1);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 109, [1] = 107, [2] = 104, [3] = 100 }));
                rig.Rows[9] = new SenseInput { Ally = 999 };
                rig.Step(2);
                Assert.That(rig.Allies(), Is.EquivalentTo(new Dictionary<int, int> { [0] = 999, [1] = 107, [2] = 104, [3] = 100 }), "row 9 is copied at step entry");
                AssertPath(rig, useJobs);
            }
        }
    }
}
