using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.Simulation;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP12001;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Integration.Simulation
{
    // P12-001: the built-in leaves, the standard native nodes and the P12-001 reactive-pattern shard.
    [AibtCatalogSet("aibt.tests.p12001.reactive-pattern", 1u, typeof(BuiltInLeafShard), typeof(StandardNativeShard), typeof(ReactivePatternShard))]
    public static partial class ReactivePatternCatalog { }

    /// <summary>
    /// P12-001: the SweetKingdom reactive-interrupt pattern in the simulation container, exactly as the
    /// consumer uses it: <c>aibt.core.reactive-selector-retaining</c> with a guard (a Burst condition reading
    /// a trigger through <c>[AibtInput]</c>) before a long-running walk (an external action that reads the
    /// position at every think); a trigger channel holding only the flag with
    /// <see cref="TreeGroup.TrySetWakeOnChange{T}"/> on; a positions channel (16 bytes per agent) without wake,
    /// rewritten at every tick; ten agents with <see cref="ThinkSchedule.Spread"/>(5). Every scenario runs in
    /// place and in the agent-frame job and must give the same log. Evidence:
    /// <c>Planning~/Evidence/P12-001/README.md</c>.
    /// </summary>
    public sealed class TreeSimulationReactivePatternTests
    {
        internal const uint TickMicroseconds = 33333u;
        internal const int Agents = 10;
        internal static readonly InputChannel<TriggerRow> Trigger = InputChannel<TriggerRow>.Create("trigger");
        internal static readonly InputChannel<PositionRow> Position = InputChannel<PositionRow>.Create("position");
        internal static readonly CommandChannel<int> Signal = CommandChannel<int>.Create("aibt.native.signal");

        // The consumer's pattern: the alert guard (trigger raised: signal 9, then pause 3 ticks) before
        // the long walk, under a retaining reactive selector, repeated forever.
        internal const string SelectorTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p12001.selector"", ""name"": ""P12-001 retaining selector"",
  ""root"": ""loop"",
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""top""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""top"": { ""type"": ""aibt.core.reactive-selector-retaining"", ""typeVersion"": 1, ""children"": [""alert"", ""walk""] },
    ""alert"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""triggered"", ""flee"", ""pause""] },
    ""triggered"": { ""type"": ""aibt.tests.p12001.triggered"", ""typeVersion"": 1 },
    ""flee"": { ""type"": ""aibt.native.signal"", ""typeVersion"": 1, ""parameters"": { ""code"": 9 } },
    ""pause"": { ""type"": ""aibt.native.wait-microseconds"", ""typeVersion"": 1, ""parameters"": { ""duration-microseconds"": 99999 } },
    ""walk"": { ""type"": ""aibt.tests.p12001.walk"", ""typeVersion"": 1 }
  }
}";

        // The guard placed after the running child: it is never a guard of the walk.
        internal const string GuardAfterTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p12001.guard-after"", ""name"": ""P12-001 guard after the walk"",
  ""root"": ""loop"",
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""top""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""top"": { ""type"": ""aibt.core.reactive-selector-retaining"", ""typeVersion"": 1, ""children"": [""walk"", ""alert""] },
    ""alert"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""triggered"", ""flee"", ""pause""] },
    ""triggered"": { ""type"": ""aibt.tests.p12001.triggered"", ""typeVersion"": 1 },
    ""flee"": { ""type"": ""aibt.native.signal"", ""typeVersion"": 1, ""parameters"": { ""code"": 9 } },
    ""pause"": { ""type"": ""aibt.native.wait-microseconds"", ""typeVersion"": 1, ""parameters"": { ""duration-microseconds"": 99999 } },
    ""walk"": { ""type"": ""aibt.tests.p12001.walk"", ""typeVersion"": 1 }
  }
}";

        // The same arrangement with the retaining reactive sequence: "calm" (not triggered) guards the walk;
        // when it fails the patrol ends and the memory selector runs the alert.
        internal const string SequenceTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p12001.sequence"", ""name"": ""P12-001 retaining sequence"",
  ""root"": ""loop"",
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""top""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""top"": { ""type"": ""aibt.core.memory-selector"", ""typeVersion"": 1, ""children"": [""patrol"", ""alert""] },
    ""patrol"": { ""type"": ""aibt.core.reactive-sequence-retaining"", ""typeVersion"": 1, ""children"": [""calm"", ""walk""] },
    ""calm"": { ""type"": ""aibt.core.inverter"", ""typeVersion"": 1, ""children"": [""quiet""] },
    ""quiet"": { ""type"": ""aibt.tests.p12001.triggered"", ""typeVersion"": 1 },
    ""alert"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""triggered"", ""flee"", ""pause""] },
    ""triggered"": { ""type"": ""aibt.tests.p12001.triggered"", ""typeVersion"": 1 },
    ""flee"": { ""type"": ""aibt.native.signal"", ""typeVersion"": 1, ""parameters"": { ""code"": 9 } },
    ""pause"": { ""type"": ""aibt.native.wait-microseconds"", ""typeVersion"": 1, ""parameters"": { ""duration-microseconds"": 99999 } },
    ""walk"": { ""type"": ""aibt.tests.p12001.walk"", ""typeVersion"": 1 }
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

        // ---- programs ----------------------------------------------------------------------------

        private static GeneratedShardMetadataArtifact Materialize(Type shard)
        {
            var metadata = shard.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public);
            object Field(string name) => metadata.GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return GeneratedShardMetadataMaterializer.MaterializeArtifact(
                (string)Field("ShardId"), (uint)Field("ShardVersion"), (string)Field("CanonicalDescriptorJson"),
                (string)Field("DescriptorHash"), (string)Field("ManifestRegistryJson"), (string)Field("NodeRegistryHash"));
        }

        internal static ProductionTreeAsset PrepareAsset(string json)
        {
            var standard = Materialize(typeof(StandardNativeShard));
            var pattern = Materialize(typeof(ReactivePatternShard));
            var nodes = Materialize(typeof(BuiltInLeafShard)).Nodes.Concat(standard.Nodes).Concat(pattern.Nodes).ToArray();
            var registered = new RegisteredBlackboardTypeCatalog(standard.RegisteredTypes.Entries.Concat(pattern.RegisteredTypes.Entries));
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P12001/tree.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, registered,
                new ReferenceCompilerOptions("Tests/P12001/tree.aibt.json", new ReferenceCompilationPolicy(requireDeterministicNodes: false), new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            return ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(registered), ReactivePatternCatalog.Fingerprint);
        }

        private ProductionTreeProgram Program(string json)
        {
            var asset = PrepareAsset(json);
            _objects.Add(asset);
            Assert.That(asset.TryCreateProgram(ReactivePatternCatalog.TryCreateRuntimeCatalog, out var program, out var failure), Is.True, failure.ToString());
            _programs.Add(program);
            return program;
        }

        // ---- the rig: ten agents with Spread(5), trigger and positions bound -----------------------

        internal sealed class Rig : IDisposable
        {
            internal TreeSimulation Simulation;
            internal TreeGroup Group;
            internal NativeArray<TriggerRow> Triggers;
            internal NativeArray<PositionRow> Positions;
            internal AgentHandle[] Agents;
            internal readonly List<string> Log = new List<string>();
            private readonly ActivePathNode[] _path = new ActivePathNode[64];

            public void Dispose()
            {
                Simulation.Dispose();
                Triggers.Dispose();
                Positions.Dispose();
            }

            /// <summary>The game's per-tick write: every agent's position changes, the trigger row does not.</summary>
            internal void MovePositions(uint tick)
            {
                for (var index = 0; index < Positions.Length; index++)
                    Positions[index] = new PositionRow { X = tick + index, Y = 0.5f * index, Z = -tick, Heading = tick * 0.25f };
            }

            internal void Raise(int agent, bool raised) => Triggers[agent] = new TriggerRow { Raised = raised };

            /// <summary>One step; logs thinks, action events, signals and faults of this tick.</summary>
            internal void Step(uint tick)
            {
                var wokenBefore = Group.WokenByInputForTests;
                Assert.That(Simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                for (var agent = 0; agent < Agents.Length; agent++)
                {
                    Assert.That(Group.TryGetActivePath(Agents[agent], _path, out _, out var state), Is.EqualTo(SimulationResult.Success));
                    if (state.HasThought && state.LastThinkTick == tick)
                        Log.Add(tick + " think " + agent + (tick % 5u == (uint)agent % 5u ? "" : " off-schedule " + state.LastThinkReasons));
                }
                var events = Group.GetActionEvents();
                for (var item = 0; item < events.Length; item++)
                    Log.Add(tick + " " + events[item].Kind + " " + events[item].Action.Agent.Index
                            + (events[item].Kind == ActionEventKind.Cancelled ? " " + events[item].CancelReason : ""));
                var signals = Group.GetCommands(Signal);
                for (var item = 0; item < signals.Length; item++) Log.Add(tick + " signal " + signals[item].Agent.Index + " " + signals[item].Value);
                var faults = Group.GetFaults();
                for (var item = 0; item < faults.Length; item++) Log.Add(tick + " fault " + faults[item].Agent.Index);
                var woken = Group.WokenByInputForTests - wokenBefore;
                if (woken != 0) Log.Add(tick + " woken-by-input " + woken);
            }

            internal string[] At(uint tick) => Log.Where(line => line.StartsWith(tick + " ", StringComparison.Ordinal)).ToArray();
        }

        internal static Rig NewRig(ProductionTreeProgram program, bool useJobs, bool triggerWake, bool positionWake = false, int agents = Agents,
            bool addAgents = true)
        {
            var settings = TreeSimulationSettings.Default(TickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = 1;
            var rig = new Rig
            {
                Simulation = new TreeSimulation(settings),
                Triggers = new NativeArray<TriggerRow>(agents, Allocator.Persistent),
                Positions = new NativeArray<PositionRow>(agents, Allocator.Persistent),
                Agents = new AgentHandle[agents],
            };
            Assert.That(rig.Simulation.TryAddGroup(program, TreeGroupSettings.Default(agents), out rig.Group), Is.EqualTo(SimulationResult.Success));
            Assert.That(rig.Group.TryBindInput(Trigger, rig.Triggers), Is.EqualTo(SimulationResult.Success));
            Assert.That(rig.Group.TryBindInput(Position, rig.Positions), Is.EqualTo(SimulationResult.Success));
            if (triggerWake) Assert.That(rig.Group.TrySetWakeOnChange(Trigger, true), Is.EqualTo(SimulationResult.Success));
            if (positionWake) Assert.That(rig.Group.TrySetWakeOnChange(Position, true), Is.EqualTo(SimulationResult.Success));
            if (addAgents) AddAgents(rig);
            return rig;
        }

        internal static void AddAgents(Rig rig)
        {
            for (var agent = 0; agent < rig.Agents.Length; agent++)
                Assert.That(rig.Group.TryAdd(ThinkSchedule.Spread(5), out rig.Agents[agent]), Is.EqualTo(SimulationResult.Success));
        }

        /// <summary>Runs the scenario in place and in the job; the two logs must be equal. Returns the in-place log.</summary>
        private Rig[] Both(string tree, bool triggerWake, uint lastTick, Action<Rig, uint> beforeStep, bool addAgents = true,
            bool positionWake = false)
        {
            var program = Program(tree);
            var rigs = new Rig[2];
            for (var path = 0; path < 2; path++)
            {
                var rig = NewRig(program, useJobs: path == 1, triggerWake, positionWake, addAgents: addAgents);
                rigs[path] = rig;
                try
                {
                    for (uint tick = 1; tick <= lastTick; tick++)
                    {
                        rig.MovePositions(tick);
                        beforeStep?.Invoke(rig, tick);
                        rig.Step(tick);
                    }
                    if (path == 1) Assert.That(rig.Group.JobBatchesForTests, Is.GreaterThan(0), "UseJobs with MinimumJobBatch=1 ran the agent-frame job");
                    else Assert.That(rig.Group.JobBatchesForTests, Is.Zero);
                }
                finally { rig.Dispose(); }
            }
            Assert.That(rigs[1].Log, Is.EqualTo(rigs[0].Log), "The same thinks, events and signals in place and in the job");
            TestContext.WriteLine(string.Join("\n", rigs[0].Log));
            return rigs;
        }

        private static string[] Scheduled(uint tick) => Enumerable.Range(0, Agents).Where(a => tick % 5u == (uint)a % 5u).Select(a => tick + " think " + a).ToArray();

        // ---- the pattern ----------------------------------------------------------------------------

        /// <summary>
        /// Positions rewritten at every tick on a channel without wake wake no one: every agent thinks only
        /// on its schedule, starts its walk once and is never cancelled, though wake is on for the trigger.
        /// </summary>
        [Test]
        public void PositionsRewrittenEveryTick_WakeNoOne_ThinksFollowTheSchedule()
        {
            var log = Both(SelectorTree, triggerWake: true, lastTick: 30, beforeStep: null)[0].Log;
            Assert.That(log.Where(line => line.Contains(" think ")), Is.EqualTo(Enumerable.Range(1, 30).SelectMany(t => Scheduled((uint)t))),
                "Think counts equal the schedule: no off-schedule think");
            Assert.That(log.Count(line => line.Contains(" Started ")), Is.EqualTo(Agents), "Each walk started once");
            Assert.That(log.Where(line => line.Contains(" Cancelled ") || line.Contains(" signal ") || line.Contains(" fault ") || line.Contains("woken-by-input")),
                Is.Empty);
        }

        /// <summary>
        /// The other half: with wake on for the positions channel, rewritten at every tick, the whole row
        /// differs at every step, so every agent that has thought once thinks at every tick, off schedule with
        /// the reason InputChanged; before its first think an agent is not woken. Nothing is cancelled.
        /// </summary>
        [Test]
        public void PositionsWithWakeOn_RewrittenEveryTick_WakeEveryAgentEveryTick()
        {
            var log = Both(SelectorTree, triggerWake: true, lastTick: 30, beforeStep: null, positionWake: true)[0].Log;
            uint FirstThink(int agent) => agent % 5 == 0 ? 5u : (uint)(agent % 5);
            var expected = new List<string>();
            for (uint tick = 1; tick <= 30; tick++)
            {
                var woken = 0;
                for (var agent = 0; agent < Agents; agent++)
                {
                    if (tick % 5u == (uint)agent % 5u) expected.Add(tick + " think " + agent);
                    else if (tick > FirstThink(agent))
                    {
                        expected.Add(tick + " think " + agent + " off-schedule InputChanged");
                        woken++;
                    }
                }
                if (woken != 0) expected.Add(tick + " woken-by-input " + woken);
            }
            Assert.That(log.Where(line => line.Contains(" think ") || line.Contains("woken-by-input")), Is.EqualTo(expected),
                "every agent that has thought thinks at every tick, woken by the changed position row");
            Assert.That(log.Count(line => line.Contains(" Started ")), Is.EqualTo(Agents), "Each walk started once");
            Assert.That(log.Where(line => line.Contains(" Cancelled ") || line.Contains(" signal ") || line.Contains(" fault ")), Is.Empty);
        }

        /// <summary>
        /// A trigger row written before Step(13) preempts inside Step(13): agent 2 (phase 2, next scheduled think
        /// 17) is woken, its walk ends with Cancelled (Preempted) and its alert runs (signal 9) in that step; no
        /// other agent thinks off schedule or is cancelled.
        /// </summary>
        [Test]
        public void TriggerWrittenBeforeStep_PreemptsTheWalkInsideThatStep()
        {
            var rig = Both(SelectorTree, true, 13, (r, tick) => { if (tick == 13) r.Raise(2, true); })[0];
            Assert.That(rig.At(13), Is.EquivalentTo(Scheduled(13).Concat(new[]
            {
                "13 think 2 off-schedule InputChanged",
                "13 Cancelled 2 Preempted",
                "13 signal 2 9",
                "13 woken-by-input 1",
            })));
            Assert.That(rig.Log.Count(line => line.Contains(" Cancelled ")), Is.EqualTo(1));
        }

        /// <summary>The same arrangement with aibt.core.reactive-sequence-retaining: the calm guard fails, the patrol's walk is preempted in Step(13).</summary>
        [Test]
        public void RetainingSequence_TriggerWrittenBeforeStep_PreemptsInsideThatStep()
        {
            var rig = Both(SequenceTree, true, 13, (r, tick) => { if (tick == 13) r.Raise(2, true); })[0];
            Assert.That(rig.Log.Count(line => line.Contains(" Started ")), Is.EqualTo(Agents));
            Assert.That(rig.At(13), Is.EquivalentTo(Scheduled(13).Concat(new[]
            {
                "13 think 2 off-schedule InputChanged",
                "13 Cancelled 2 Preempted",
                "13 signal 2 9",
                "13 woken-by-input 1",
            })));
        }

        /// <summary>
        /// The alternative without wake on any channel: the game writes the trigger and calls TryWakeMany for the
        /// agents it knows; the walk is preempted inside that step. Without the call the alert waits for the
        /// agent's next scheduled think (17).
        /// </summary>
        [Test]
        public void TryWakeMany_WithoutWakeOnChange_PreemptsInsideThatStep()
        {
            var indices = new NativeArray<int>(new[] { 2 }, Allocator.Persistent);
            try
            {
                var rig = Both(SelectorTree, false, 17, (r, tick) =>
                {
                    if (tick != 13) return;
                    r.Raise(2, true);
                    Assert.That(r.Group.TryWakeMany(indices.AsReadOnly()), Is.EqualTo(SimulationResult.Success));
                })[0];
                Assert.That(rig.At(13), Is.EquivalentTo(Scheduled(13).Concat(new[] { "13 think 2 off-schedule WokenByGame", "13 Cancelled 2 Preempted", "13 signal 2 9" })));

                var control = Both(SelectorTree, false, 17, (r, tick) => { if (tick == 13) r.Raise(2, true); })[0];
                Assert.That(control.At(13), Is.EquivalentTo(Scheduled(13)), "No wake: nothing happens at 13");
                Assert.That(control.At(17), Is.SupersetOf(new[] { "17 Cancelled 2 Preempted", "17 signal 2 9" }), "The next scheduled think preempts");
            }
            finally { indices.Dispose(); }
        }

        // ---- the three conditions ----------------------------------------------------------------------

        /// <summary>
        /// An agent that has not thought since it was added is not woken: agent 0 (phase 0) gets its trigger
        /// before Step(1), does not think until its first scheduled think (5), where its alert runs first.
        /// An agent that has not thought since wake was enabled is not woken either: with wake enabled before
        /// Step(8), agent 0 (last think 5) raised before 8 is preempted only at 10; agent 1 (thought at 11 after
        /// wake was enabled) raised before 13 is preempted inside Step(13).
        /// </summary>
        [Test]
        public void NotWoken_BeforeTheAgentThoughtSinceItWasAdded_OrSinceWakeWasEnabled()
        {
            var added = Both(SelectorTree, true, 6, (r, tick) => { if (tick == 1) r.Raise(0, true); })[0];
            Assert.That(added.Log.Where(line => line.Contains(" think 0")),
                Is.EqualTo(new[] { "5 think 0" }), "Agent 0 first thinks on its schedule, not at tick 1");
            Assert.That(added.At(5), Has.Member("5 signal 0 9"), "Its alert runs at its first think");
            Assert.That(added.Log.Where(line => line.Contains("woken-by-input") || line.Contains("off-schedule")), Is.Empty);
            Assert.That(added.Log.Where(line => line.Contains(" Started 0")), Is.Empty, "Agent 0 never started a walk");

            var enabled = Both(SelectorTree, false, 13, (r, tick) =>
            {
                if (tick == 8)
                {
                    Assert.That(r.Group.TrySetWakeOnChange(Trigger, true), Is.EqualTo(SimulationResult.Success));
                    r.Raise(0, true);
                }
                if (tick == 13) r.Raise(1, true);
            })[0];
            Assert.That(enabled.At(8), Is.EquivalentTo(Scheduled(8)), "Agent 0 last thought before wake was enabled: no wake at 8");
            Assert.That(enabled.At(10), Has.Member("10 Cancelled 0 Preempted").And.Member("10 signal 0 9"), "Preempted at its next scheduled think");
            Assert.That(enabled.At(13), Is.EquivalentTo(Scheduled(13).Concat(new[]
            {
                "13 think 1 off-schedule InputChanged", "13 Cancelled 1 Preempted", "13 signal 1 9", "13 woken-by-input 1",
            })), "Agent 1 thought at 11, after wake was enabled: woken inside Step(13)");
        }

        /// <summary>
        /// A guard placed after the running child is not a guard: the changed trigger wakes agent 2 at 13 (the row
        /// changed), but the walk keeps running, nothing is cancelled and the alert never runs.
        /// </summary>
        [Test]
        public void GuardAfterTheRunningChild_DoesNotPreempt()
        {
            var rig = Both(GuardAfterTree, true, 20, (r, tick) => { if (tick == 13) r.Raise(2, true); })[0];
            Assert.That(rig.At(13), Is.EquivalentTo(Scheduled(13).Concat(new[] { "13 think 2 off-schedule InputChanged", "13 woken-by-input 1" })));
            Assert.That(rig.Log.Where(line => line.Contains(" Cancelled ") || line.Contains(" signal ")), Is.Empty);
        }

        /// <summary>
        /// A value that rises and falls between two steps is not seen: raised and lowered again before Step(13),
        /// agent 2 is not woken and keeps walking. Raised before 13 and lowered before 14 it is seen (woken at 13).
        /// Without wake, raised before 13 and lowered before 14 it is never seen (next think 17 reads it lowered).
        /// </summary>
        [Test]
        public void RiseAndFallBetweenTwoSteps_IsNotSeen()
        {
            var within = Both(SelectorTree, true, 20, (r, tick) =>
            {
                if (tick != 13) return;
                r.Raise(2, true);
                r.Raise(2, false);
            })[0];
            Assert.That(within.Log.Where(line => line.Contains(" Cancelled ") || line.Contains(" signal ") || line.Contains("off-schedule")), Is.Empty);

            var acrossWake = Both(SelectorTree, true, 20, (r, tick) =>
            {
                if (tick == 13) r.Raise(2, true);
                if (tick == 14) r.Raise(2, false);
            })[0];
            Assert.That(acrossWake.At(13), Has.Member("13 Cancelled 2 Preempted").And.Member("13 signal 2 9"));

            var acrossNoWake = Both(SelectorTree, false, 20, (r, tick) =>
            {
                if (tick == 13) r.Raise(2, true);
                if (tick == 14) r.Raise(2, false);
            })[0];
            Assert.That(acrossNoWake.Log.Where(line => line.Contains(" Cancelled ") || line.Contains(" signal ") || line.Contains("off-schedule")), Is.Empty);
        }
    }
}
