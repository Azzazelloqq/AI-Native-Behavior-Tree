using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Simulation;
using AIBT.Tests.CodeGen.GenerationP12003;
using NUnit.Framework;
using Unity.Collections;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.Simulation
{
    // P12-003: the squad-rows shard alone. Its largest node (compare-squads: two inputs and one command) sets
    // the value sessions of one callback at three for every node of this catalog.
    [AibtCatalogSet("aibt.tests.p12003.squad-rows", 1u, typeof(SquadRowsShard))]
    public static partial class SquadRowsCatalog { }

    /// <summary>
    /// P12-003, recipe "another squad's row through a second channel" of
    /// <c>Documentation~/simulation-container.md</c> ("Game data that is not per agent"): one rows array (one row
    /// per squad) bound to two input channels of one row type, "own" and "target", each with its own index
    /// array, and agents that read both rows in one think. Also what a node that reads one input more than once
    /// in one callback gets. Squad r's row is <see cref="Squad"/>(r): Members 10 + r, Strength 1 + r, so an aim
    /// command shows which two rows the agent read. Evidence: <c>Planning~/Tasks/P12/P12-003-*.md</c>.
    /// </summary>
    public sealed class TreeSimulationSquadRowsTests
    {
        private const ulong Seed = 0x12_0003UL;
        private static readonly InputChannel<SquadRow> Own = InputChannel<SquadRow>.Create("own");
        private static readonly InputChannel<SquadRow> Target = InputChannel<SquadRow>.Create("target");
        private static readonly CommandChannel<AimCommand> Aim = CommandChannel<AimCommand>.Create("aim");

        private static string OneNodeTree(string nodeType, string treeId) => @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": """ + treeId + @""", ""name"": ""P12-003 squad rows"",
  ""root"": ""node"",
  ""nodes"": {
    ""node"": { ""type"": """ + nodeType + @""", ""typeVersion"": 1 }
  }
}";

        private static readonly string CompareTree = OneNodeTree("aibt.tests.p12003.compare-squads", "tests.p12003.compare");

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

        // ---- programs ------------------------------------------------------------------------------------

        private static GeneratedShardMetadataArtifact Materialize(Type shard)
        {
            var metadata = shard.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public);
            object Field(string name) => metadata.GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return GeneratedShardMetadataMaterializer.MaterializeArtifact(
                (string)Field("ShardId"), (uint)Field("ShardVersion"), (string)Field("CanonicalDescriptorJson"),
                (string)Field("DescriptorHash"), (string)Field("ManifestRegistryJson"), (string)Field("NodeRegistryHash"));
        }

        private ProductionTreeProgram Program(string json)
        {
            var squads = Materialize(typeof(SquadRowsShard));
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P12003/tree.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, squads.Nodes.ToArray(), squads.RegisteredTypes,
                new ReferenceCompilerOptions("Tests/P12003/tree.aibt.json", new ReferenceCompilationPolicy(requireDeterministicNodes: false), new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var asset = ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(squads.RegisteredTypes), SquadRowsCatalog.Fingerprint);
            _objects.Add(asset);
            Assert.That(asset.TryCreateProgram(SquadRowsCatalog.TryCreateRuntimeCatalog, out var program, out var failure), Is.True, failure.ToString());
            _programs.Add(program);
            return program;
        }

        private static TreeSimulationSettings Settings(bool useJobs)
        {
            var settings = TreeSimulationSettings.Default(TickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = 1;
            settings.RandomSeed = Seed;
            return settings;
        }

        private static SquadRow Squad(int squad) => new SquadRow { Strength = 1 + squad, Members = 10 + squad };

        // ---- the rig: one rows array, two channels -------------------------------------------------------

        private sealed class Rig : IDisposable
        {
            internal TreeSimulation Simulation;
            internal TreeGroup Group;
            internal NativeArray<SquadRow> Squads;
            internal NativeArray<int> OwnOf;
            internal NativeArray<int> TargetOf;
            internal readonly List<AgentHandle> Agents = new List<AgentHandle>();
            private readonly ActivePathNode[] _path = new ActivePathNode[16];

            public void Dispose()
            {
                Simulation.Dispose();
                if (Squads.IsCreated) Squads.Dispose();
                if (OwnOf.IsCreated) OwnOf.Dispose();
                if (TargetOf.IsCreated) TargetOf.Dispose();
            }

            internal void Step(uint tick) => Assert.That(Simulation.Step(tick), Is.EqualTo(SimulationResult.Success), "Step(" + tick + ")");

            /// <summary>This step's aims by slot: (own members, target members).</summary>
            internal Dictionary<int, (int Own, int Target)> Aims()
            {
                var aims = Group.GetCommands(Aim);
                var result = new Dictionary<int, (int, int)>();
                for (var index = 0; index < aims.Length; index++) result.Add(aims[index].Agent.Index, (aims[index].Value.Own, aims[index].Value.Target));
                return result;
            }

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
        /// <paramref name="agents"/> agents; <paramref name="squads"/> rows (row r = <see cref="Squad"/>(r)) bound
        /// to "own" with ownOf[s] = slot % squads and, when <paramref name="bindTarget"/>, to "target" with
        /// targetOf[s] = (slot + 1) % squads: the game assigned each agent the next squad as its target.
        /// </summary>
        private static Rig NewRig(ProductionTreeProgram program, bool useJobs, int agents, int squads,
            Func<int, ThinkSchedule> schedule = null, bool wakeOnTarget = false, bool bindTarget = true)
        {
            var rig = new Rig
            {
                Simulation = new TreeSimulation(Settings(useJobs)),
                Squads = new NativeArray<SquadRow>(squads, Allocator.Persistent),
                OwnOf = new NativeArray<int>(agents, Allocator.Persistent),
                TargetOf = new NativeArray<int>(agents, Allocator.Persistent),
            };
            for (var squad = 0; squad < squads; squad++) rig.Squads[squad] = Squad(squad);
            for (var slot = 0; slot < agents; slot++)
            {
                rig.OwnOf[slot] = slot % squads;
                rig.TargetOf[slot] = (slot + 1) % squads;
            }
            Assert.That(rig.Simulation.TryAddGroup(program, TreeGroupSettings.Default(agents), out rig.Group), Is.EqualTo(SimulationResult.Success));
            Assert.That(rig.Group.TryBindInput(Own, rig.Squads, rig.OwnOf), Is.EqualTo(SimulationResult.Success));
            if (bindTarget) Assert.That(rig.Group.TryBindInput(Target, rig.Squads, rig.TargetOf), Is.EqualTo(SimulationResult.Success));
            if (wakeOnTarget) Assert.That(rig.Group.TrySetWakeOnChange(Target, true), Is.EqualTo(SimulationResult.Success));
            for (var agent = 0; agent < agents; agent++)
            {
                Assert.That(rig.Group.TryAdd(schedule?.Invoke(agent) ?? ThinkSchedule.Every(1, 0), out var handle), Is.EqualTo(SimulationResult.Success));
                rig.Agents.Add(handle);
            }
            return rig;
        }

        private static void AssertPath(Rig rig, bool useJobs)
        {
            if (useJobs) Assert.That(rig.Group.JobBatchesForTests, Is.GreaterThan(0), "UseJobs with MinimumJobBatch=1 ran the agent-frame job");
            else Assert.That(rig.Group.JobBatchesForTests, Is.Zero);
        }

        // ---- 1. one rows array, two channels -------------------------------------------------------------

        /// <summary>
        /// Six agents, three squads: each agent's aim carries its own squad's row and its target squad's row,
        /// read in one think from one rows array through two channels, in place and in jobs.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OneRowsArray_TwoChannels_EachAgentReadsItsOwnAndItsTargetRow(bool useJobs)
        {
            using (var rig = NewRig(Program(CompareTree), useJobs, agents: 6, squads: 3))
            {
                rig.Step(1);
                var expected = Enumerable.Range(0, 6).ToDictionary(slot => slot, slot => (10 + slot % 3, 10 + (slot + 1) % 3));
                Assert.That(rig.Aims(), Is.EquivalentTo(expected));
                var aims = rig.Group.GetCommands(Aim);
                for (var index = 0; index < aims.Length; index++)
                {
                    var slot = aims[index].Agent.Index;
                    Assert.That(aims[index].Value.Edge, Is.EqualTo(Squad(slot % 3).Strength - Squad((slot + 1) % 3).Strength), "slot " + slot);
                }
                Assert.That(rig.Group.GetFaults().Length, Is.Zero);
                AssertPath(rig, useJobs);
            }
        }

        /// <summary>
        /// 30 agents with Spread(3), five squads rewritten at every tick, targets reassigned every fifth tick,
        /// wake on "target": the log of every aim, think (with reasons) and fault over 40 ticks is the same in
        /// place and in jobs.
        /// </summary>
        [Test]
        public void TwoChannels_LogIsTheSameInPlaceAndInJobs()
        {
            var program = Program(CompareTree);
            var inPlace = RunLog(program, useJobs: false, out var inPlaceBatches);
            var jobs = RunLog(program, useJobs: true, out var jobBatches);
            Assert.That(inPlace.Count(line => line.Contains(" aim ")), Is.GreaterThan(300));
            Assert.That(inPlace.Count(line => line.Contains("InputChanged")), Is.GreaterThan(0), "target changes wake agents between their thinks");
            Assert.That(inPlace.Where(line => line.Contains(" fault ")), Is.Empty);
            Assert.That(inPlaceBatches, Is.Zero);
            Assert.That(jobBatches, Is.GreaterThan(0));
            Assert.That(jobs, Is.EqualTo(inPlace));
        }

        private List<string> RunLog(ProductionTreeProgram program, bool useJobs, out long jobBatches)
        {
            const int agents = 30;
            const int squads = 5;
            var log = new List<string>();
            using (var rig = NewRig(program, useJobs, agents, squads, _ => ThinkSchedule.Spread(3), wakeOnTarget: true))
            {
                for (uint tick = 1; tick <= 40; tick++)
                {
                    for (var squad = 0; squad < squads; squad++)
                        rig.Squads[squad] = new SquadRow { Strength = (squad * 3 + tick / 2) % 7, Members = (int)(squad * 11 + tick / 4) % 13 };
                    if (tick % 5 == 0)
                        for (var slot = 0; slot < agents; slot++) rig.TargetOf[slot] = (int)((slot + tick / 5) % squads);
                    rig.Step(tick);
                    var aims = rig.Group.GetCommands(Aim);
                    for (var index = 0; index < aims.Length; index++)
                        log.Add(tick + " aim " + aims[index].Agent.Index + " " + aims[index].Value.Own + " " + aims[index].Value.Target + " "
                                + BitConverter.SingleToInt32Bits(aims[index].Value.Edge));
                    foreach (var pair in rig.Thinks(tick).OrderBy(pair => pair.Key)) log.Add(tick + " think " + pair.Key + " " + pair.Value);
                    var faults = rig.Group.GetFaults();
                    for (var index = 0; index < faults.Length; index++) log.Add(tick + " fault " + faults[index].Agent.Index + " " + faults[index].Cause);
                }
                jobBatches = rig.Group.JobBatchesForTests;
            }
            return log;
        }

        // ---- 2. wake on one channel only -----------------------------------------------------------------

        /// <summary>
        /// Ten agents with Spread(5), three squads, wake on "target" only. Squad 2's row changes before tick 8.
        /// The agents whose target is squad 2 (slots 1, 4, 7) are woken with InputChanged; the agents whose own
        /// squad is squad 2 (slots 2, 5, 8) are not, though they read the same row through "own": slot 8 thinks
        /// at 8 on schedule, slots 2 and 5 do not think. Wake is per channel, not per rows array.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void WakeOnChange_OnOneChannel_WakesOnlyTheReadersThroughThatChannel(bool useJobs)
        {
            using (var rig = NewRig(Program(CompareTree), useJobs, agents: 10, squads: 3, schedule: _ => ThinkSchedule.Spread(5), wakeOnTarget: true))
            {
                for (uint tick = 1; tick <= 7; tick++) rig.Step(tick);
                rig.Squads[2] = new SquadRow { Strength = 9f, Members = 77 };
                var before = rig.Group.WokenByInputForTests;
                rig.Step(8);
                var thinks = rig.Thinks(8);
                Assert.That(thinks.Keys, Is.EquivalentTo(new[] { 1, 3, 4, 7, 8 }), "1, 4, 7 woken through \"target\"; 3 and 8 due");
                foreach (var slot in new[] { 1, 4, 7 })
                {
                    Assert.That(thinks[slot] & ThinkReasons.InputChanged, Is.EqualTo(ThinkReasons.InputChanged), "slot " + slot + ": " + thinks[slot]);
                    Assert.That(thinks[slot] & ThinkReasons.Scheduled, Is.EqualTo(ThinkReasons.None), "slot " + slot + ": " + thinks[slot]);
                }
                foreach (var slot in new[] { 3, 8 }) Assert.That(thinks[slot] & ThinkReasons.InputChanged, Is.EqualTo(ThinkReasons.None), "slot " + slot);
                Assert.That(rig.Group.WokenByInputForTests - before, Is.EqualTo(3));
                Assert.That(rig.Aims(), Is.EquivalentTo(new Dictionary<int, (int, int)>
                {
                    [1] = (11, 77), [3] = (10, 11), [4] = (11, 77), [7] = (11, 77), [8] = (77, 10),
                }), "every think reads the new row 2, through either channel");
                AssertPath(rig, useJobs);
            }
        }

        // ---- 3. rebinding one channel --------------------------------------------------------------------

        /// <summary>
        /// Rebinding "target" to another rows array with its own indices leaves "own" as it was; rebinding "own"
        /// with a new index array over the same rows leaves "target" as it was; unbinding "target" refuses the
        /// step with InputMissing while "own" stays bound, and binding it again steps.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RebindingOneChannel_LeavesTheOtherAsItWas(bool useJobs)
        {
            using (var rig = NewRig(Program(CompareTree), useJobs, agents: 4, squads: 3))
            using (var othersOwner = new NativeArray<SquadRow>(2, Allocator.Persistent))
            using (var otherOfOwner = new NativeArray<int>(4, Allocator.Persistent))
            using (var allTwoOwner = new NativeArray<int>(4, Allocator.Persistent))
            {
                var others = othersOwner;
                var otherOf = otherOfOwner;
                var allTwo = allTwoOwner;
                for (var row = 0; row < 2; row++) others[row] = new SquadRow { Strength = 0f, Members = 50 + row };
                for (var slot = 0; slot < 4; slot++)
                {
                    otherOf[slot] = slot % 2;
                    allTwo[slot] = 2;
                }

                rig.Step(1);
                Assert.That(rig.Aims(), Is.EquivalentTo(new Dictionary<int, (int, int)> { [0] = (10, 11), [1] = (11, 12), [2] = (12, 10), [3] = (10, 11) }));

                Assert.That(rig.Group.TryBindInput(Target, others, otherOf), Is.EqualTo(SimulationResult.Success));
                rig.Step(2);
                Assert.That(rig.Aims(), Is.EquivalentTo(new Dictionary<int, (int, int)> { [0] = (10, 50), [1] = (11, 51), [2] = (12, 50), [3] = (10, 51) }),
                    "\"own\" still reads the squads through its own indices");

                Assert.That(rig.Group.TryBindInput(Own, rig.Squads, allTwo), Is.EqualTo(SimulationResult.Success));
                rig.Step(3);
                Assert.That(rig.Aims(), Is.EquivalentTo(new Dictionary<int, (int, int)> { [0] = (12, 50), [1] = (12, 51), [2] = (12, 50), [3] = (12, 51) }),
                    "\"target\" keeps the other rows array");

                Assert.That(rig.Group.TryUnbindInput(Target), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(4), Is.EqualTo(SimulationResult.InputMissing), "the node reads \"target\"");
                Assert.That(rig.Group.TryBindInput(Target, rig.Squads, rig.TargetOf), Is.EqualTo(SimulationResult.Success));
                rig.Step(4);
                Assert.That(rig.Aims(), Is.EquivalentTo(new Dictionary<int, (int, int)> { [0] = (12, 11), [1] = (12, 12), [2] = (12, 10), [3] = (12, 11) }));
                AssertPath(rig, useJobs);
            }
        }

        // ---- 4. an index out of range in the second channel ----------------------------------------------

        /// <summary>
        /// An index at rows.Length in the "target" index array of a live agent refuses the whole step with
        /// InvalidArgument before anything runs, although "own" is in range; once fixed, the step runs. Each
        /// channel's indices are checked on their own.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OutOfRangeIndexInTheSecondChannel_RefusesTheStep_NothingRuns(bool useJobs)
        {
            using (var rig = NewRig(Program(CompareTree), useJobs, agents: 4, squads: 3))
            {
                rig.TargetOf[2] = 3;
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.InvalidArgument), "target index == rows.Length");
                Assert.That(rig.Simulation.HasStepped, Is.False);
                for (var slot = 0; slot < 4; slot++) Assert.That(rig.Group.CoreForTests(slot).TotalUpdates, Is.Zero, "Nothing ran.");

                rig.TargetOf[2] = 0;
                rig.Step(1);
                Assert.That(rig.Aims(), Is.EquivalentTo(new Dictionary<int, (int, int)> { [0] = (10, 11), [1] = (11, 12), [2] = (12, 10), [3] = (10, 11) }));
                AssertPath(rig, useJobs);
            }
        }

        // ---- 5. reading one input more than once in one callback -----------------------------------------

        /// <summary>
        /// A node that reads "own" twice in one callback (and aims once) needs three value sessions and 28 staging
        /// bytes (8 + 8 + 12). This catalog has that room because compare-squads needs the same (two inputs, one
        /// command), so the second read succeeds. The aim carries the first read's members as Own and the second
        /// read's as Target, so the expected pairs show that the second read returned the agent's row. Here the
        /// room comes from another node; the next test shows the refusal when there is none.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ReadingOneInputTwice_WithRoomInTheCatalog_ReadsTheSameRowTwice(bool useJobs)
        {
            using (var rig = NewRig(Program(OneNodeTree("aibt.tests.p12003.read-own-twice", "tests.p12003.read-twice")), useJobs,
                       agents: 4, squads: 3, bindTarget: false))
            {
                rig.Step(1);
                Assert.That(rig.Aims(), Is.EquivalentTo(new Dictionary<int, (int, int)> { [0] = (10, 10), [1] = (11, 11), [2] = (12, 12), [3] = (10, 10) }),
                    "(first read, second read) members per agent");
                Assert.That(rig.Group.GetFaults().Length, Is.Zero);
                AssertPath(rig, useJobs);
            }
        }

        /// <summary>
        /// A node that aims and then reads "own" three times asks for a fourth value session where the catalog
        /// has three. The third read returns CapacityExceeded, the refusal is latched, and the agent faults with
        /// cause CapacityExceeded; the aim it published in that callback is not kept. The step itself succeeds.
        /// A node with one fewer binding in a catalog whose largest node it is would be refused at its second
        /// read in the same way (P11-014, MixedScopeNativeTreeTests.ValueSessionLimit_*).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ReadingBeyondTheCatalogsValueSessions_FaultsTheAgentWithCapacityExceeded(bool useJobs)
        {
            using (var rig = NewRig(Program(OneNodeTree("aibt.tests.p12003.read-own-thrice", "tests.p12003.read-thrice")), useJobs,
                       agents: 4, squads: 3, bindTarget: false))
            {
                rig.Step(1);
                var faults = rig.Group.GetFaults();
                Assert.That(faults.Length, Is.EqualTo(4));
                for (var index = 0; index < faults.Length; index++)
                {
                    Assert.That(faults[index].Cause, Is.EqualTo(BurstContextResult.CapacityExceeded), "agent " + faults[index].Agent.Index);
                    Assert.That(faults[index].Failure.Code, Is.Not.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
                }
                Assert.That(rig.Group.GetCommands(Aim).Length, Is.Zero, "the refused callback's aim is not published");
                AssertPath(rig, useJobs);
            }
        }
    }
}
