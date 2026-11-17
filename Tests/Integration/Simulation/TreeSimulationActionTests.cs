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
using AIBT.Tests.CodeGen.GenerationP10011;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// P10-011 (ADR-P10-008 stage G3): external long-running actions in the simulation container. Nodes
    /// start actions (<c>AsyncOperationHandle</c>), the game sees Started/Cancelled events per step and
    /// completes actions between steps (<see cref="TreeGroup.TryComplete{TResult}"/>), nodes read the
    /// completion (<c>CompletionHandle</c>). EditMode, no GameObject on the container side. Proof table:
    /// <c>Planning~/Evidence/P10-011/README.md</c>.
    /// </summary>
    public sealed class TreeSimulationActionTests
    {
        private const ulong Seed = 0x10_0111UL;
        private static readonly InputChannel<CrewSense> Sense = InputChannel<CrewSense>.Create("sense");
        private static readonly CommandChannel<JobDone> Done = CommandChannel<JobDone>.Create("done");

        // Repeats: a reactive selector prefers "alarm" (input) over a job (external action) that waits
        // for the game forever. The reactive selector aborts its running job at every think (normative), so
        // the job is cancelled (Preempted) and restarted each think while the alarm is down; once the alarm
        // is up no new job starts, and the cycle holds for one more update (wait 2: a wait of 1 succeeds at
        // once, and the repeater would spin within one update).
        internal const string CrewTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10011.crew"", ""name"": ""P10-011 crew"",
  ""root"": ""loop"",
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""cycle""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""cycle"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""top"", ""hold""] },
    ""top"": { ""type"": ""aibt.core.reactive-selector"", ""typeVersion"": 1, ""children"": [""alarm"", ""job""] },
    ""alarm"": { ""type"": ""aibt.tests.p10011.alarm"", ""typeVersion"": 1 },
    ""job"": { ""type"": ""aibt.tests.p10011.job"", ""typeVersion"": 1, ""parameters"": { ""power"": 1.0, ""patience"": 0, ""forget"": false } },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } }
  }
}";

        // Repeats: a fire-and-forget job (ends with NodeEnded at once), a one-update hold, then a job the node gives
        // up on after 5 ticks of time (NodeEnded) unless the game completes it sooner.
        internal const string ShiftTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10011.shift"", ""name"": ""P10-011 shift"",
  ""root"": ""loop"",
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""cycle""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""cycle"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""forget"", ""hold"", ""patient""] },
    ""forget"": { ""type"": ""aibt.tests.p10011.job"", ""typeVersion"": 1, ""parameters"": { ""power"": 2.0, ""patience"": 0, ""forget"": true } },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""patient"": { ""type"": ""aibt.tests.p10011.job"", ""typeVersion"": 1, ""parameters"": { ""power"": 3.0, ""patience"": 166665, ""forget"": false } }
  }
}";

        // One job as the root: Success/Failure is the game's outcome.
        internal const string SingleJobTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10011.single"", ""name"": ""P10-011 single job"",
  ""root"": ""job"",
  ""nodes"": {
    ""job"": { ""type"": ""aibt.tests.p10011.job"", ""typeVersion"": 1, ""parameters"": { ""power"": 1.0, ""patience"": 0, ""forget"": false } }
  }
}";

        // One job as the root that gives up after 3 ticks of time.
        internal const string ImpatientTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10011.impatient"", ""name"": ""P10-011 impatient job"",
  ""root"": ""job"",
  ""nodes"": {
    ""job"": { ""type"": ""aibt.tests.p10011.job"", ""typeVersion"": 1, ""parameters"": { ""power"": 1.0, ""patience"": 99999, ""forget"": false } }
  }
}";

        // Two fire-and-forget jobs in one think: needs an action capacity of 2.
        internal const string DoubleForgetTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10011.double"", ""name"": ""P10-011 double start"",
  ""root"": ""both"",
  ""nodes"": {
    ""both"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""first"", ""second""] },
    ""first"": { ""type"": ""aibt.tests.p10011.job"", ""typeVersion"": 1, ""parameters"": { ""power"": 1.0, ""patience"": 0, ""forget"": true } },
    ""second"": { ""type"": ""aibt.tests.p10011.job"", ""typeVersion"": 1, ""parameters"": { ""power"": 2.0, ""patience"": 0, ""forget"": true } }
  }
}";

        // A job runs in parallel with a guard. While the alarm is down the guard's repeater fails at once and the
        // guard holds; once the alarm is up the repeater's child succeeds at once every time, so the guard never
        // yields within one think (a runaway tree). The job is still live when the step limit stops the agent.
        internal const string RunawayTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10011.runaway"", ""name"": ""P10-011 runaway"",
  ""root"": ""both"",
  ""nodes"": {
    ""both"": { ""type"": ""aibt.core.parallel"", ""typeVersion"": 1, ""parameters"": { ""policy"": ""require-all-success"" }, ""children"": [""job"", ""guard""] },
    ""job"": { ""type"": ""aibt.tests.p10011.job"", ""typeVersion"": 1, ""parameters"": { ""power"": 1.0, ""patience"": 0, ""forget"": false } },
    ""guard"": { ""type"": ""aibt.core.reactive-selector"", ""typeVersion"": 1, ""children"": [""spin"", ""idle""] },
    ""spin"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""alarm""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": true } },
    ""alarm"": { ""type"": ""aibt.tests.p10011.alarm"", ""typeVersion"": 1 },
    ""idle"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 1000000 } }
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

        internal static ProductionTreeAsset PrepareActionAsset(string json)
        {
            var standard = Materialize(typeof(StandardNativeShard));
            var io = Materialize(typeof(SimulationIoShard));
            var actions = Materialize(typeof(SimulationActionShard));
            var nodes = Materialize(typeof(BuiltInLeafShard)).Nodes.Concat(standard.Nodes).Concat(io.Nodes).Concat(actions.Nodes).ToArray();
            var registered = new RegisteredBlackboardTypeCatalog(
                standard.RegisteredTypes.Entries.Concat(io.RegisteredTypes.Entries).Concat(actions.RegisteredTypes.Entries));
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P10011/tree.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, registered,
                new ReferenceCompilerOptions("Tests/P10011/tree.aibt.json", new ReferenceCompilationPolicy(requireDeterministicNodes: false), new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            return ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(registered), SimulationActionCatalog.Fingerprint);
        }

        private ProductionTreeProgram Program(string json, ProductionTreeCatalogFactory factory = null)
        {
            var asset = PrepareActionAsset(json);
            _objects.Add(asset);
            Assert.That(asset.TryCreateProgram(factory ?? SimulationActionCatalog.TryCreateRuntimeCatalog, out var program, out var failure),
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

        private static TreeGroupSettings Stopping(int capacity)
        {
            var settings = TreeGroupSettings.Default(capacity);
            settings.RootCompletion = RootCompletionPolicy.Stop;
            return settings;
        }

        private static uint Mix(uint a, int b, uint salt)
        {
            unchecked
            {
                var h = a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ salt * 0xC2B2AE3Du;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                return h;
            }
        }

        /// <summary>The game's rows for one tick: an alarm on about one row in five, changing every 8 ticks.</summary>
        private static void WriteSense(uint tick, NativeArray<CrewSense> sense, uint salt)
        {
            for (var slot = 0; slot < sense.Length; slot++)
            {
                var h = Mix(tick / 8, slot, salt);
                sense[slot] = new CrewSense { Threat = (h % 64) / 64f, Alarm = (h >> 8) % 5 == 0 };
            }
        }

        private static ulong Bits(float value) => (ulong)(uint)BitConverter.SingleToInt32Bits(value);

        private static void AddOperation(ref Fnv digest, OperationId operation)
        {
            digest.Add(operation.TreeInstanceId.Value);
            digest.Add(operation.NodeIndex.Value);
            digest.Add(operation.ActivationGeneration);
            digest.Add(operation.Sequence);
        }

        /// <summary>Every action event of the step with its handle and payload, then the step's JobDone commands.</summary>
        private static void AppendActions(TreeGroup group, ref Fnv digest)
        {
            var events = group.GetActionEvents();
            digest.Add((ulong)events.Length);
            for (var index = 0; index < events.Length; index++)
            {
                var item = events[index];
                digest.Add((ulong)item.Kind);
                digest.Add((ulong)item.CancelReason);
                digest.Add(item.FaultCancel ? 1ul : 0ul);
                digest.Add((ulong)item.Action.Agent.Index);
                digest.Add(item.Action.Agent.Generation);
                digest.Add(item.Action.Channel);
                AddOperation(ref digest, item.Action.Operation);
                if (item.Kind == ActionEventKind.Started)
                {
                    Assert.That(group.TryGetStartPayload(item, out JobStart start), Is.EqualTo(SimulationResult.Success));
                    digest.Add(Bits(start.Power));
                    digest.Add((ulong)(uint)start.Kind);
                    digest.Add(start.Flag ? 1ul : 0ul);
                }
                else
                {
                    Assert.That(group.TryGetCancelPayload(item, out JobCancel cancel), Is.EqualTo(SimulationResult.Success));
                    digest.Add((ulong)(uint)cancel.Reason);
                    digest.Add(Bits(cancel.Progress));
                }
            }
            var done = group.GetCommands(Done);
            digest.Add((ulong)done.Length);
            for (var index = 0; index < done.Length; index++)
            {
                digest.Add((ulong)done[index].Agent.Index);
                digest.Add(Bits(done[index].Value.Score));
                digest.Add(done[index].Value.Ok ? 1ul : 0ul);
            }
        }

        // ---- population: determinism and the exactly-one-Cancelled invariant -----------------------

        private const int Agents = 260;
        private const int Ticks = 300;

        private struct Pending
        {
            internal ActionHandle Handle;
            internal uint Due;
            internal int Mode; // 0..2 failure, 3 success without a value, else success with a value
            internal JobResult Result;
        }

        /// <summary>What happened to every started action, checked as events, completions and JobDone commands arrive.</summary>
        private sealed class Ledger
        {
            private enum State { Live, Completed, Cancelled, Consumed }
            private readonly Dictionary<ActionHandle, State> _states = new Dictionary<ActionHandle, State>();
            private readonly Dictionary<AgentHandle, ActionHandle> _completedByAgent = new Dictionary<AgentHandle, ActionHandle>();
            internal int Started;
            internal int Consumed;
            internal int CancelledAfterCompletion;
            internal readonly int[] Reasons = new int[7];
            internal int FaultCancels;

            internal void OnStarted(ActionHandle handle)
            {
                Assert.That(_states.ContainsKey(handle), Is.False, "A started action's handle is new: " + handle);
                _states.Add(handle, State.Live);
                Started++;
            }

            internal void OnCompleted(ActionHandle handle)
            {
                Assert.That(_states[handle], Is.EqualTo(State.Live));
                _states[handle] = State.Completed;
                Assert.That(_completedByAgent.ContainsKey(handle.Agent), Is.False, "At most one completed, unconsumed action per agent here.");
                _completedByAgent.Add(handle.Agent, handle);
            }

            internal void OnCancelled(in ActionEvent item)
            {
                var handle = item.Action;
                Assert.That(_states.TryGetValue(handle, out var state), Is.True, "Cancelled only after Started: " + handle);
                Assert.That(state, Is.Not.EqualTo(State.Cancelled), "Never two Cancelled events: " + handle);
                Assert.That(state, Is.Not.EqualTo(State.Consumed), "Never a Cancelled after a consumed completion: " + handle);
                if (state == State.Completed)
                {
                    CancelledAfterCompletion++;
                    _completedByAgent.Remove(handle.Agent);
                }
                _states[handle] = State.Cancelled;
                Reasons[(int)item.CancelReason]++;
                if (item.FaultCancel) FaultCancels++;
            }

            internal void OnDone(AgentHandle agent)
            {
                Assert.That(_completedByAgent.TryGetValue(agent, out var handle), Is.True, "A JobDone consumes a completion the game gave: " + agent);
                _completedByAgent.Remove(agent);
                _states[handle] = State.Consumed;
                Consumed++;
            }

            /// <summary>After every group was removed: each started action was cancelled once or consumed once.</summary>
            internal void AssertSettled()
            {
                foreach (var pair in _states)
                    Assert.That(pair.Value == State.Cancelled || pair.Value == State.Consumed, Is.True,
                        "Every started action ends with exactly one Cancelled or one consumed completion: " + pair.Key + " is " + pair.Value);
            }
        }

        private sealed class Outcome
        {
            internal readonly List<ulong> Digests = new List<ulong>();
            internal Ledger Ledger;
            internal int Faults;
            internal int Succeeded;
            internal int Failed;
            internal int Never;
            internal int Done;
            internal long JobBatches;
        }

        /// <summary>
        /// 2 × 260 agents (crew Spread(5) with wake on changed "sense", shift Spread(3)), 300 ticks, the
        /// input written every tick, 20 agents removed and re-added per group at ticks 60/61 and 180/181,
        /// 15 restarted per group at ticks 100 and 220, both groups removed after the last tick. The game
        /// completes each started action 1-6 ticks later (success with or without a value, failure) or
        /// never (one in eight). Each tick's digest covers every agent's state, root completions, faults,
        /// action events with payloads and JobDone commands.
        /// </summary>
        private Outcome RunPopulation(ProductionTreeProgram crew, ProductionTreeProgram shift, in TreeSimulationSettings settings)
        {
            var outcome = new Outcome { Ledger = new Ledger() };
            var ledger = outcome.Ledger;
            var path = new List<uint>();
            var pending = new List<Pending>();
            using (var simulation = new TreeSimulation(settings))
            using (var crewSense = new NativeArray<CrewSense>(Agents, Allocator.Persistent))
            using (var shiftSense = new NativeArray<CrewSense>(Agents, Allocator.Persistent))
            {
                Assert.That(simulation.TryAddGroup(crew, TreeGroupSettings.Default(Agents), out var crews), Is.EqualTo(SimulationResult.Success));
                Assert.That(simulation.TryAddGroup(shift, TreeGroupSettings.Default(Agents), out var shifts), Is.EqualTo(SimulationResult.Success));
                Assert.That(crews.TryBindInput(Sense, crewSense), Is.EqualTo(SimulationResult.Success));
                Assert.That(shifts.TryBindInput(Sense, shiftSense), Is.EqualTo(SimulationResult.Success));
                Assert.That(crews.TrySetWakeOnChange(Sense, true), Is.EqualTo(SimulationResult.Success));
                Assert.That(crews.ActionEventCapacity, Is.EqualTo(Agents * 3 * TreeGroupSettings.DefaultActionCapacityPerAgent));
                var groups = new[] { crews, shifts };
                var handles = new[] { new AgentHandle[Agents], new AgentHandle[Agents] };
                for (var index = 0; index < Agents; index++)
                {
                    Assert.That(crews.TryAdd(ThinkSchedule.Spread(5), out handles[0][index]), Is.EqualTo(SimulationResult.Success));
                    Assert.That(shifts.TryAdd(ThinkSchedule.Spread(3), out handles[1][index]), Is.EqualTo(SimulationResult.Success));
                }
                for (uint tick = 1; tick <= Ticks + 2; tick++)
                {
                    // The game: completions due now, in the order the actions started (before this tick's
                    // removals and restarts, which then cancel some completed actions).
                    if (tick <= Ticks)
                    {
                        var kept = 0;
                        for (var index = 0; index < pending.Count; index++)
                        {
                            var item = pending[index];
                            if (item.Due != tick)
                            {
                                pending[kept++] = item;
                                continue;
                            }
                            var group = item.Handle.Agent.Group == crews.Id ? crews : shifts;
                            var result = item.Mode < 3 ? group.TryComplete(item.Handle, ActionOutcome.Failed, item.Result)
                                : item.Mode == 3 ? group.TryComplete(item.Handle, ActionOutcome.Succeeded)
                                : group.TryComplete(item.Handle, ActionOutcome.Succeeded, item.Result);
                            Assert.That(result, Is.EqualTo(SimulationResult.Success), "tick " + tick + " " + item.Handle);
                            Assert.That(group.TryComplete(item.Handle, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.AlreadyCompleted));
                            ledger.OnCompleted(item.Handle);
                            if (item.Mode < 3) outcome.Failed++;
                            else outcome.Succeeded++;
                        }
                        pending.RemoveRange(kept, pending.Count - kept);
                        WriteSense(tick, crewSense, 1);
                        WriteSense(tick, shiftSense, 2);
                    }
                    if (tick == 60 || tick == 180)
                        for (var g = 0; g < 2; g++)
                            for (var n = 0; n < 20; n++)
                                Assert.That(groups[g].TryRemove(handles[g][(n * 13 + (int)tick) % Agents]), Is.EqualTo(SimulationResult.Success));
                    if (tick == 61 || tick == 181)
                        for (var g = 0; g < 2; g++)
                            for (var n = 0; n < 20; n++)
                            {
                                Assert.That(groups[g].TryAdd(ThinkSchedule.Spread(g == 0 ? 5u : 3u), out var added), Is.EqualTo(SimulationResult.Success));
                                handles[g][added.Index] = added;
                            }
                    if (tick == 100 || tick == 220)
                        for (var g = 0; g < 2; g++)
                            for (var n = 0; n < 15; n++)
                                Assert.That(groups[g].TryRestart(handles[g][(n * 17 + 5) % Agents]), Is.EqualTo(SimulationResult.Success));
                    if (tick == Ticks + 1)
                        for (var g = 0; g < 2; g++) Assert.That(simulation.TryRemoveGroup(groups[g]), Is.EqualTo(SimulationResult.Success));
                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));

                    var digest = Fnv.Create();
                    digest.Add(tick);
                    foreach (var group in groups)
                    {
                        if (tick <= Ticks) AppendDigest(group, ref digest, path);
                        AppendActions(group, ref digest);
                        outcome.Faults += group.GetFaults().Length;
                        var done = group.GetCommands(Done);
                        for (var index = 0; index < done.Length; index++) ledger.OnDone(done[index].Agent);
                        outcome.Done += done.Length;
                        var events = group.GetActionEvents();
                        for (var index = 0; index < events.Length; index++)
                        {
                            var item = events[index];
                            if (item.Kind == ActionEventKind.Started)
                            {
                                ledger.OnStarted(item.Action);
                                var h = Mix(tick, item.Action.Agent.Index, (uint)item.Action.Operation.Sequence + 7u);
                                if (h % 8 == 0)
                                {
                                    outcome.Never++;
                                    continue;
                                }
                                pending.Add(new Pending
                                {
                                    Handle = item.Action,
                                    Due = tick + 1 + (h >> 4) % 6,
                                    Mode = (int)((h >> 8) % 10),
                                    Result = new JobResult { Score = ((h >> 12) % 100) / 10f, Hits = (int)((h >> 20) % 5) },
                                });
                            }
                            else
                            {
                                ledger.OnCancelled(item);
                                for (var p = 0; p < pending.Count; p++)
                                    if (pending[p].Handle == item.Action)
                                    {
                                        pending.RemoveAt(p);
                                        break;
                                    }
                            }
                        }
                    }
                    outcome.Digests.Add(digest.Value);
                }
                Assert.That(simulation.GroupCount, Is.Zero, "Both removed groups are released.");
                Assert.That(crews.JobPathAvailable && shifts.JobPathAvailable, Is.True, "The generated catalog has the agent-frame entry.");
                outcome.JobBatches = crews.JobBatchesForTests + shifts.JobBatchesForTests;
            }
            ledger.AssertSettled();
            return outcome;
        }

        private static void AssertSame(Outcome expected, Outcome actual, string what)
        {
            Assert.That(actual.Digests.Count, Is.EqualTo(expected.Digests.Count), what + ": ticks");
            for (var index = 0; index < expected.Digests.Count; index++)
                if (expected.Digests[index] != actual.Digests[index])
                    Assert.Fail(what + ": first digest difference at tick " + (index + 1));
            Assert.That(actual.Ledger.Started, Is.EqualTo(expected.Ledger.Started), what);
        }

        [Test]
        public void Population_Actions_DigestsAreIdentical_AcrossPathsWorkerCountsAndRuns_AndEveryActionEndsOnce()
        {
            var crew = Program(CrewTree);
            var shift = Program(ShiftTree);

            var inPlaceA = RunPopulation(crew, shift, Settings(false, 1));
            var inPlaceB = RunPopulation(crew, shift, Settings(false, 1));
            var jobs = RunPopulation(crew, shift, Settings(true, 1));
            var defaults = RunPopulation(crew, shift, Settings(true, 64));
            var workers = JobsUtility.JobWorkerCount;
            Outcome noWorkers, oneWorker;
            try
            {
                JobsUtility.JobWorkerCount = 0;
                noWorkers = RunPopulation(crew, shift, Settings(true, 1));
                JobsUtility.JobWorkerCount = 1;
                oneWorker = RunPopulation(crew, shift, Settings(true, 1));
            }
            finally
            {
                JobsUtility.JobWorkerCount = workers;
            }

            var ledger = inPlaceA.Ledger;
            TestContext.WriteLine("started " + ledger.Started + ", consumed " + ledger.Consumed + ", game success " + inPlaceA.Succeeded
                + ", failure " + inPlaceA.Failed + ", never " + inPlaceA.Never + ", cancelled after completion " + ledger.CancelledAfterCompletion
                + ", reasons " + string.Join("/", ledger.Reasons) + ", fault-cancels " + ledger.FaultCancels + ", batches " + jobs.JobBatches);
            Assert.That(inPlaceA.Faults, Is.Zero, "No agent faults.");
            Assert.That(ledger.Started, Is.GreaterThan(5000), "Agents start actions.");
            Assert.That(ledger.Consumed, Is.EqualTo(inPlaceA.Done));
            Assert.That(ledger.Consumed, Is.GreaterThan(1000), "Nodes consume completions.");
            Assert.That(inPlaceA.Succeeded, Is.GreaterThan(500));
            Assert.That(inPlaceA.Failed, Is.GreaterThan(300));
            Assert.That(inPlaceA.Never, Is.GreaterThan(300));
            Assert.That(ledger.Reasons[(int)ActionCancelReason.Preempted], Is.GreaterThan(100), "Raised alarms preempt jobs.");
            Assert.That(ledger.Reasons[(int)ActionCancelReason.NodeEnded], Is.GreaterThan(1000), "Forgotten and given-up jobs end with their node.");
            Assert.That(ledger.Reasons[(int)ActionCancelReason.AgentRemoved], Is.GreaterThan(10));
            Assert.That(ledger.Reasons[(int)ActionCancelReason.AgentRestarted], Is.GreaterThan(10));
            Assert.That(ledger.Reasons[(int)ActionCancelReason.GroupRemoved], Is.GreaterThan(100));
            Assert.That(ledger.Reasons[(int)ActionCancelReason.Fault] + ledger.Reasons[(int)ActionCancelReason.Timeout], Is.Zero);
            Assert.That(ledger.CancelledAfterCompletion, Is.GreaterThan(0), "Some completions lose to preemption or removal.");
            Assert.That(ledger.Started, Is.EqualTo(ledger.Consumed + ledger.Reasons.Sum()), "Exactly one end per started action.");
            Assert.That(inPlaceA.JobBatches, Is.Zero);
            Assert.That(jobs.JobBatches, Is.GreaterThanOrEqualTo(Ticks));
            Assert.That(noWorkers.JobBatches, Is.GreaterThanOrEqualTo(Ticks));

            AssertSame(inPlaceA, inPlaceB, "second run in the same process");
            AssertSame(inPlaceA, jobs, "UseJobs=true, MinimumJobBatch=1");
            AssertSame(inPlaceA, defaults, "default settings (MinimumJobBatch=64)");
            AssertSame(inPlaceA, noWorkers, "0 job workers");
            AssertSame(inPlaceA, oneWorker, "1 job worker");
        }

        // ---- life cycle ----------------------------------------------------------------------------

        private sealed class Rig : IDisposable
        {
            internal TreeSimulation Simulation;
            internal TreeGroup Group;
            internal NativeArray<CrewSense> Sense;
            internal AgentHandle Agent;

            public void Dispose()
            {
                Simulation.Dispose();
                Sense.Dispose();
            }

            internal ActionEvent Single(ActionEventKind kind)
            {
                var events = Group.GetActionEvents();
                Assert.That(events.Length, Is.EqualTo(1), "one action event");
                Assert.That(events[0].Kind, Is.EqualTo(kind));
                return events[0];
            }
        }

        private Rig NewRig(string tree, bool useJobs, in TreeGroupSettings groupSettings, in ThinkSchedule schedule, bool wake = false)
        {
            var rig = new Rig
            {
                Simulation = new TreeSimulation(Settings(useJobs, 1)),
                Sense = new NativeArray<CrewSense>(groupSettings.Capacity, Allocator.Persistent),
            };
            Assert.That(rig.Simulation.TryAddGroup(Program(tree), groupSettings, out rig.Group), Is.EqualTo(SimulationResult.Success));
            Assert.That(rig.Group.TryBindInput(Sense, rig.Sense), Is.EqualTo(SimulationResult.Success));
            if (wake) Assert.That(rig.Group.TrySetWakeOnChange(Sense, true), Is.EqualTo(SimulationResult.Success));
            Assert.That(rig.Group.TryAdd(schedule, out rig.Agent), Is.EqualTo(SimulationResult.Success));
            return rig;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StartThenSuccess_NodeReadsTheValue_EmitsItsCommand_AndTheHandleGoesStale(bool useJobs)
        {
            using (var rig = NewRig(SingleJobTree, useJobs, Stopping(2), ThinkSchedule.Every(1, 0)))
            {
                rig.Sense[0] = new CrewSense { Threat = 0.25f, Alarm = true };
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var started = rig.Single(ActionEventKind.Started);
                Assert.That(started.Action.Agent, Is.EqualTo(rig.Agent));
                Assert.That(started.Action.Operation.IsValid, Is.True);
                Assert.That(rig.Group.TryGetStartPayload(started, out JobStart start), Is.EqualTo(SimulationResult.Success));
                Assert.That(start.Power, Is.EqualTo(1.25f));
                Assert.That(start.Kind, Is.EqualTo((int)(TickMicroseconds % 7)));
                Assert.That(start.Flag, Is.True);
                Assert.That(rig.Group.TryGetCancelPayload(started, out JobCancel _), Is.EqualTo(SimulationResult.StaleAction), "A Started event has no cancel value.");
                Assert.That(rig.Group.TryGetStartPayload(started, out int _), Is.EqualTo(SimulationResult.TypeMismatch));
                Assert.That(rig.Group.LiveActionCountForTests(0), Is.EqualTo(1));

                for (uint tick = 2; tick <= 4; tick++)
                {
                    Assert.That(rig.Simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    Assert.That(rig.Group.GetActionEvents().Length, Is.Zero, "Polling without a completion keeps the node running.");
                    Assert.That(rig.Group.GetFaults().Length, Is.Zero);
                    Assert.That(rig.Group.GetRootCompletions().Length, Is.Zero);
                }
                Assert.That(rig.Group.TryGetStartPayload(started, out start), Is.EqualTo(SimulationResult.StaleAction), "Events are valid until the next step.");

                Assert.That(rig.Group.TryComplete(started.Action, ActionOutcome.Succeeded, new JobResult { Score = 2.5f, Hits = 3 }), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.TryComplete(started.Action, ActionOutcome.Failed), Is.EqualTo(SimulationResult.AlreadyCompleted), "Double completion changes nothing.");
                Assert.That(rig.Simulation.Step(5), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.GetActionEvents().Length, Is.Zero, "A consumed completion ends the action without a Cancelled event.");
                var done = rig.Group.GetCommands(Done);
                Assert.That(done.Length, Is.EqualTo(1));
                Assert.That(done[0].Agent, Is.EqualTo(rig.Agent));
                Assert.That(done[0].Value.Score, Is.EqualTo(5.5f));
                Assert.That(done[0].Value.Ok, Is.True);
                var completions = rig.Group.GetRootCompletions();
                Assert.That(completions.Length, Is.EqualTo(1));
                Assert.That(completions[0].Result, Is.EqualTo(NodeStatus.Success));
                Assert.That(rig.Group.LiveActionCountForTests(0), Is.Zero);
                Assert.That(rig.Group.TryComplete(started.Action, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleAction));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StartThenFailure_WithoutAValue_NodeReadsZeros(bool useJobs)
        {
            using (var rig = NewRig(SingleJobTree, useJobs, Stopping(2), ThinkSchedule.Every(1, 0)))
            {
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var started = rig.Single(ActionEventKind.Started);
                Assert.That(rig.Group.TryComplete(started.Action, ActionOutcome.Failed), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.GetActionEvents().Length, Is.Zero);
                var done = rig.Group.GetCommands(Done);
                Assert.That(done.Length, Is.EqualTo(1));
                Assert.That(done[0].Value.Score, Is.EqualTo(0f));
                Assert.That(done[0].Value.Ok, Is.False);
                Assert.That(rig.Group.GetRootCompletions()[0].Result, Is.EqualTo(NodeStatus.Failure));
            }
        }

        /// <summary>
        /// A completion wakes its agent: an agent thinking every 10 ticks gets the completion given after
        /// tick 3 in step 4, not at its next scheduled think (10).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Completion_WakesTheAgent_ForTheNextStep(bool useJobs)
        {
            using (var rig = NewRig(SingleJobTree, useJobs, Stopping(2), ThinkSchedule.Every(10, 1)))
            {
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var started = rig.Single(ActionEventKind.Started);
                for (uint tick = 2; tick <= 3; tick++) rig.Simulation.Step(tick);
                Assert.That(rig.Group.CoreForTests(0).TotalUpdates, Is.EqualTo(1ul));
                Assert.That(rig.Group.TryComplete(started.Action, ActionOutcome.Succeeded, new JobResult { Score = 1f, Hits = 1 }), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(4), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.CoreForTests(0).TotalUpdates, Is.EqualTo(2ul));
                Assert.That(rig.Group.GetCommands(Done).Length, Is.EqualTo(1));
                Assert.That(rig.Group.GetRootCompletions().Length, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// A runaway tree (a repeater whose child always succeeds at once) stops at
        /// <see cref="TreeSimulationSettings.MaximumStepsPerThink"/> in the think where it starts spinning: the agent
        /// faults with NativeUpdateStepLimitExceeded after exactly that many steps, its live action ends with one
        /// Cancelled (Fault, the start's fault-cancel value), the calm agent beside it keeps running, and Step
        /// returns. Both paths (in place, and the agent-frame job with MinimumJobBatch 1).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RunawayTree_FaultsTheAgentAtTheStepLimit_AndCancelsItsLiveAction(bool useJobs)
        {
            const uint limit = 1000u;
            var settings = Settings(useJobs, 1);
            settings.MaximumStepsPerThink = limit;
            var sense = new NativeArray<CrewSense>(2, Allocator.Persistent);
            using (sense)
            using (var simulation = new TreeSimulation(settings))
            {
                Assert.That(simulation.TryAddGroup(Program(RunawayTree), Stopping(2), out var group), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryBindInput(Sense, sense), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryAdd(ThinkSchedule.Every(1, 0), out var runaway), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryAdd(ThinkSchedule.Every(1, 0), out var calm), Is.EqualTo(SimulationResult.Success));

                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var events = group.GetActionEvents();
                Assert.That(events.Length, Is.EqualTo(2), "Both agents start their job.");
                var runawayAction = events[0].Action.Agent == runaway ? events[0].Action : events[1].Action;
                Assert.That(group.GetFaults().Length, Is.Zero);

                sense[0] = new CrewSense { Threat = 1f, Alarm = true };
                Assert.That(simulation.Step(2), Is.EqualTo(SimulationResult.Success), "Step returns; only the runaway agent stops.");
                var faults = group.GetFaults();
                Assert.That(faults.Length, Is.EqualTo(1));
                Assert.That(faults[0].Agent, Is.EqualTo(runaway));
                Assert.That(faults[0].Failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeUpdateStepLimitExceeded));
                Assert.That(group.CoreForTests(runaway.Index).StepsThisUpdate, Is.EqualTo((ulong)limit), "stopped exactly at the limit");

                var cancelled = SingleEvent(group, ActionEventKind.Cancelled);
                Assert.That(cancelled.Action, Is.EqualTo(runawayAction));
                Assert.That(cancelled.CancelReason, Is.EqualTo(ActionCancelReason.Fault));
                Assert.That(cancelled.FaultCancel, Is.True);
                Assert.That(group.TryGetCancelPayload(cancelled, out JobCancel cancel), Is.EqualTo(SimulationResult.Success));
                Assert.That(cancel.Reason, Is.EqualTo(99));
                Assert.That(cancel.Progress, Is.EqualTo(-1f));
                Assert.That(group.LiveActionCountForTests(runaway.Index), Is.Zero);
                Assert.That(group.LiveActionCountForTests(calm.Index), Is.EqualTo(1), "The calm agent's job is untouched.");
                Assert.That(group.CoreForTests(calm.Index).Ready, Is.True);

                Assert.That(simulation.Step(3), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.GetFaults().Length, Is.Zero, "A stopped agent does not think again.");
                Assert.That(group.GetActionEvents().Length, Is.Zero);
            }
        }

        /// <summary>The container's default step limit is the host's (G1, ADR-P10-008); see the P10-011 evidence, finding F1.</summary>
        [Test]
        public void DefaultStepLimit_IsTheHostDefault()
            => Assert.That(TreeSimulationSettings.Default(TickMicroseconds).MaximumStepsPerThink, Is.EqualTo(ProductionTreeHost.DefaultMaximumStepsPerUpdate));

        private static ActionEvent SingleEvent(TreeGroup group, ActionEventKind kind)
        {
            var events = group.GetActionEvents();
            Assert.That(events.Length, Is.EqualTo(1), "one action event");
            Assert.That(events[0].Kind, Is.EqualTo(kind));
            return events[0];
        }

        /// <summary>
        /// A job waits; the alarm rises at tick 13. Without wake on change the reactive selector preempts the
        /// job at the next think (tick 20), with it in the very next step (tick 13): one Cancelled event,
        /// reason Preempted, with the value the node wrote in Abort. The handle is stale afterwards.
        /// </summary>
        [TestCase(false, false, 20u)]
        [TestCase(true, false, 13u)]
        [TestCase(false, true, 20u)]
        [TestCase(true, true, 13u)]
        public void ReactiveInterrupt_PreemptsTheJob_CancelledWithTheAbortValue(bool wake, bool useJobs, uint expectedTick)
        {
            using (var rig = NewRig(CrewTree, useJobs, TreeGroupSettings.Default(2), ThinkSchedule.Every(10, 0), wake))
            {
                ActionEvent started = default;
                var cancelledAt = 0u;
                for (uint tick = 1; tick <= 30; tick++)
                {
                    if (tick == 13) rig.Sense[0] = new CrewSense { Threat = 0.5f, Alarm = true };
                    Assert.That(rig.Simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    var events = rig.Group.GetActionEvents();
                    for (var index = 0; index < events.Length; index++)
                    {
                        if (events[index].Kind == ActionEventKind.Started)
                        {
                            Assert.That(tick, Is.EqualTo(10u), "The job starts at the first think and never again while the alarm is up.");
                            started = events[index];
                            continue;
                        }
                        Assert.That(cancelledAt, Is.Zero, "One Cancelled event.");
                        cancelledAt = tick;
                        Assert.That(events[index].Action, Is.EqualTo(started.Action));
                        Assert.That(events[index].CancelReason, Is.EqualTo(ActionCancelReason.Preempted));
                        Assert.That(events[index].FaultCancel, Is.False);
                        Assert.That(rig.Group.TryGetCancelPayload(events[index], out JobCancel cancel), Is.EqualTo(SimulationResult.Success));
                        Assert.That(cancel.Progress, Is.EqualTo(0.5f));
                        Assert.That(cancel.Reason, Is.Not.EqualTo(99));
                        Assert.That(cancel.Reason, Is.Not.EqualTo((int)BurstNodeAbortReason.TreeStopped));
                    }
                    Assert.That(rig.Group.GetFaults().Length, Is.Zero);
                }
                Assert.That(cancelledAt, Is.EqualTo(expectedTick));
                Assert.That(rig.Group.TryComplete(started.Action, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleAction), "Complete after cancel.");
                Assert.That(rig.Group.LiveActionCountForTests(0), Is.Zero);
            }
        }

        /// <summary>
        /// Pins the normative reactive-selector rule (execution-semantics-v1, "Reactive selector": a running
        /// child is aborted and exited before every reevaluation) for external actions: a job under a
        /// reactive selector is cancelled (Preempted) and started again at every think, so a completion
        /// the game gives is never consumed. Step 2: the first action is cancelled and a new one starts.
        /// Then the game completes the second one and raises the alarm before step 3: the job is aborted
        /// before it could read the completion (one Cancelled, no JobDone, no new start: the alarm wins).
        /// Finding F2 in the P10-011 evidence.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UnderAReactiveSelector_TheJobRestartsEveryThink_AndACompletionIsNeverConsumed(bool useJobs)
        {
            using (var rig = NewRig(CrewTree, useJobs, TreeGroupSettings.Default(2), ThinkSchedule.Every(1, 0)))
            {
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var first = rig.Single(ActionEventKind.Started);
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                var events = rig.Group.GetActionEvents();
                Assert.That(events.Length, Is.EqualTo(2), "Cancelled, then Started again.");
                Assert.That(events[0].Kind, Is.EqualTo(ActionEventKind.Cancelled));
                Assert.That(events[0].Action, Is.EqualTo(first.Action));
                Assert.That(events[0].CancelReason, Is.EqualTo(ActionCancelReason.Preempted));
                Assert.That(events[0].FaultCancel, Is.False, "The cancel value comes from the node's Abort.");
                Assert.That(events[1].Kind, Is.EqualTo(ActionEventKind.Started));
                var second = events[1].Action;
                Assert.That(second, Is.Not.EqualTo(first.Action));
                Assert.That(rig.Group.TryComplete(first.Action, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleAction));
                Assert.That(rig.Group.GetFaults().Length, Is.Zero);

                Assert.That(rig.Group.TryComplete(second, ActionOutcome.Succeeded, new JobResult { Score = 9f, Hits = 9 }), Is.EqualTo(SimulationResult.Success));
                rig.Sense[0] = new CrewSense { Threat = 1f, Alarm = true };
                Assert.That(rig.Simulation.Step(3), Is.EqualTo(SimulationResult.Success));
                var cancelled = rig.Single(ActionEventKind.Cancelled);
                Assert.That(cancelled.Action, Is.EqualTo(second));
                Assert.That(cancelled.CancelReason, Is.EqualTo(ActionCancelReason.Preempted));
                Assert.That(rig.Group.GetCommands(Done).Length, Is.Zero, "The completion was not consumed.");
                Assert.That(rig.Group.TryComplete(second, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleAction));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AgentRemoved_CancelsItsAction_InTheTeardownStep(bool useJobs)
        {
            using (var rig = NewRig(SingleJobTree, useJobs, Stopping(2), ThinkSchedule.Every(1, 0)))
            {
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var started = rig.Single(ActionEventKind.Started);
                Assert.That(rig.Group.TryComplete(started.Action, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.Success), "Completed but not yet consumed.");
                Assert.That(rig.Group.TryRemove(rig.Agent), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.TryComplete(started.Action, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleAction), "The agent is gone.");
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                var cancelled = rig.Single(ActionEventKind.Cancelled);
                Assert.That(cancelled.Action, Is.EqualTo(started.Action));
                Assert.That(cancelled.CancelReason, Is.EqualTo(ActionCancelReason.AgentRemoved));
                Assert.That(cancelled.FaultCancel, Is.False);
                Assert.That(rig.Group.TryGetCancelPayload(cancelled, out JobCancel cancel), Is.EqualTo(SimulationResult.Success));
                Assert.That(cancel.Reason, Is.EqualTo((int)BurstNodeAbortReason.TreeStopped));
                Assert.That(cancel.Progress, Is.EqualTo(0.5f));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AgentRestarted_CancelsTheOldInstancesAction_BeforeTheNewInstanceStartsAgain(bool useJobs)
        {
            using (var rig = NewRig(SingleJobTree, useJobs, Stopping(2), ThinkSchedule.Every(1, 0)))
            {
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var started = rig.Single(ActionEventKind.Started);
                Assert.That(rig.Group.TryRestart(rig.Agent), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                var events = rig.Group.GetActionEvents();
                Assert.That(events.Length, Is.EqualTo(2));
                Assert.That(events[0].Kind, Is.EqualTo(ActionEventKind.Cancelled));
                Assert.That(events[0].Action, Is.EqualTo(started.Action));
                Assert.That(events[0].CancelReason, Is.EqualTo(ActionCancelReason.AgentRestarted));
                Assert.That(events[1].Kind, Is.EqualTo(ActionEventKind.Started));
                Assert.That(events[1].Action.Agent, Is.EqualTo(rig.Agent), "Restart keeps the agent handle.");
                Assert.That(events[1].Action, Is.Not.EqualTo(started.Action), "The new instance's action has a new operation id.");
                Assert.That(events[1].Action.Operation.TreeInstanceId, Is.Not.EqualTo(started.Action.Operation.TreeInstanceId));
                Assert.That(rig.Group.TryComplete(started.Action, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleAction));
                Assert.That(rig.Group.TryComplete(events[1].Action, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.Success));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GroupRemoved_CancelsEveryAction_EventsReadableUntilRelease(bool useJobs)
        {
            using (var rig = NewRig(SingleJobTree, useJobs, Stopping(3), ThinkSchedule.Every(1, 0)))
            {
                Assert.That(rig.Group.TryAdd(ThinkSchedule.Every(1, 0), out var second), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var started = rig.Group.GetActionEvents().ToArray();
                Assert.That(started.Length, Is.EqualTo(2));
                Assert.That(rig.Simulation.TryRemoveGroup(rig.Group), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.TryComplete(started[0].Action, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.Disposed));
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                var events = rig.Group.GetActionEvents();
                Assert.That(events.Length, Is.EqualTo(2));
                for (var index = 0; index < 2; index++)
                {
                    Assert.That(events[index].Kind, Is.EqualTo(ActionEventKind.Cancelled));
                    Assert.That(events[index].Action, Is.EqualTo(started[index].Action), "Slot order.");
                    Assert.That(events[index].CancelReason, Is.EqualTo(ActionCancelReason.GroupRemoved));
                    Assert.That(rig.Group.TryGetCancelPayload(events[index], out JobCancel cancel), Is.EqualTo(SimulationResult.Success));
                    Assert.That(cancel.Reason, Is.EqualTo((int)BurstNodeAbortReason.TreeStopped));
                }
                Assert.That(events[1].Action.Agent, Is.EqualTo(second));
                Assert.That(rig.Simulation.Step(3), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.GroupCount, Is.Zero, "Released in the step after the teardown.");
                Assert.That(rig.Group.GetActionEvents().Length, Is.Zero);
            }
        }

        /// <summary>A node that ends with its action live (fire and forget, or giving up) ends it: Cancelled, NodeEnded, the start's fault-cancel value.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void NodeEnded_WithALiveAction_FaultCancelsIt(bool useJobs)
        {
            using (var forget = NewRig(DoubleForgetTree, useJobs, Stopping(2), ThinkSchedule.Every(1, 0)))
            {
                Assert.That(forget.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var events = forget.Group.GetActionEvents();
                Assert.That(events.Length, Is.EqualTo(4), "Started, Cancelled for each of the two nodes, in emission order.");
                for (var index = 0; index < 4; index++)
                    Assert.That(events[index].Kind, Is.EqualTo(index % 2 == 0 ? ActionEventKind.Started : ActionEventKind.Cancelled));
                Assert.That(events[1].Action, Is.EqualTo(events[0].Action));
                Assert.That(events[3].Action, Is.EqualTo(events[2].Action));
                Assert.That(events[1].CancelReason, Is.EqualTo(ActionCancelReason.NodeEnded));
                Assert.That(events[1].FaultCancel, Is.True);
                Assert.That(forget.Group.TryGetCancelPayload(events[3], out JobCancel cancel), Is.EqualTo(SimulationResult.Success));
                Assert.That(cancel.Reason, Is.EqualTo(99));
                Assert.That(cancel.Progress, Is.EqualTo(-1f));
                Assert.That(forget.Group.TryGetStartPayload(events[2], out JobStart start), Is.EqualTo(SimulationResult.Success));
                Assert.That(start.Power, Is.EqualTo(2f));
                Assert.That(forget.Group.GetRootCompletions()[0].Result, Is.EqualTo(NodeStatus.Success));
                Assert.That(forget.Group.LiveActionCountForTests(0), Is.Zero);
            }

            using (var impatient = NewRig(ImpatientTree, useJobs, Stopping(2), ThinkSchedule.Every(1, 0)))
            {
                Assert.That(impatient.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var started = impatient.Single(ActionEventKind.Started);
                for (uint tick = 2; tick <= 3; tick++)
                {
                    Assert.That(impatient.Simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    Assert.That(impatient.Group.GetActionEvents().Length, Is.Zero);
                }
                Assert.That(impatient.Simulation.Step(4), Is.EqualTo(SimulationResult.Success));
                var cancelled = impatient.Single(ActionEventKind.Cancelled);
                Assert.That(cancelled.Action, Is.EqualTo(started.Action));
                Assert.That(cancelled.CancelReason, Is.EqualTo(ActionCancelReason.NodeEnded));
                Assert.That(cancelled.FaultCancel, Is.True);
                Assert.That(impatient.Group.GetRootCompletions()[0].Result, Is.EqualTo(NodeStatus.Failure));
            }
        }

        [Test]
        public void TryComplete_RefusesStaleWrongGroupWrongTypeAndInvalidValues_ChangingNothing()
        {
            using (var rig = NewRig(SingleJobTree, false, TreeGroupSettings.Default(2), ThinkSchedule.Every(1, 0)))
            using (var other = NewRig(SingleJobTree, false, TreeGroupSettings.Default(2), ThinkSchedule.Every(1, 0)))
            {
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                Assert.That(other.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var started = rig.Single(ActionEventKind.Started);
                var foreign = other.Single(ActionEventKind.Started);
                var group = rig.Group;

                Assert.That(group.TryComplete(default(ActionHandle), ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleHandle), "Null handle.");
                Assert.That(group.TryComplete(started.Action, (ActionOutcome)7), Is.EqualTo(SimulationResult.InvalidArgument));
                Assert.That(group.TryComplete(started.Action, ActionOutcome.Succeeded, 5), Is.EqualTo(SimulationResult.TypeMismatch), "Not a registered value type.");
                Assert.That(group.TryComplete(started.Action, ActionOutcome.Succeeded, new JobStart()), Is.EqualTo(SimulationResult.TypeMismatch), "Not the completion type.");
                Assert.That(group.TryComplete(started.Action, ActionOutcome.Succeeded, new JobResult { Score = float.NaN }), Is.EqualTo(SimulationResult.InvalidArgument));
                Assert.That(group.TryComplete(started.Action, ActionOutcome.Succeeded, new JobResult { Score = float.PositiveInfinity }), Is.EqualTo(SimulationResult.InvalidArgument));
                // A handle of the same slot in another container's group with the same id: its operation is unknown here.
                Assert.That(foreign.Action.Agent.Group, Is.EqualTo(started.Action.Agent.Group));
                var wrongOperation = new ActionHandle(started.Action.Agent,
                    new OperationId(started.Action.Operation.TreeInstanceId, started.Action.Operation.NodeIndex, started.Action.Operation.ActivationGeneration, started.Action.Operation.Sequence + 1),
                    started.Action.Channel);
                Assert.That(group.TryComplete(wrongOperation, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleAction));
                var wrongChannel = new ActionHandle(started.Action.Agent, started.Action.Operation, started.Action.Channel ^ 1);
                Assert.That(group.TryComplete(wrongChannel, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleAction));
                var wrongGroup = new ActionHandle(new AgentHandle(0, started.Action.Agent.Generation, (ushort)(group.Id + 1)), started.Action.Operation, started.Action.Channel);
                Assert.That(group.TryComplete(wrongGroup, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleHandle));

                var before = new List<byte>();
                group.AppendActionStateForTests(0, before);
                Assert.That(before[0], Is.EqualTo(1), "Still one live action, not completed.");
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.GetCommands(Done).Length, Is.Zero, "No refused call completed the action.");

                // -0.0 is canonicalised to +0.0; then the valid call works.
                Assert.That(group.TryComplete(started.Action, ActionOutcome.Succeeded, new JobResult { Score = -0f, Hits = 2 }), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(3), Is.EqualTo(SimulationResult.Success));
                var done = group.GetCommands(Done);
                Assert.That(done.Length, Is.EqualTo(1));
                Assert.That(Bits(done[0].Value.Score), Is.EqualTo(Bits(2f)));
            }
        }

        /// <summary>
        /// A handle kept after its agent was removed stays stale when a new agent reuses the slot and starts
        /// its own action there.
        /// </summary>
        [Test]
        public void StaleHandle_AfterSlotReuse_IsRefused()
        {
            using (var rig = NewRig(SingleJobTree, false, Stopping(1), ThinkSchedule.Every(1, 0)))
            {
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var old = rig.Single(ActionEventKind.Started);
                Assert.That(rig.Group.TryRemove(rig.Agent), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.TryAdd(ThinkSchedule.Every(1, 0), out var reused), Is.EqualTo(SimulationResult.Success));
                Assert.That(reused.Index, Is.EqualTo(old.Action.Agent.Index));
                Assert.That(reused.Generation, Is.Not.EqualTo(old.Action.Agent.Generation));
                Assert.That(rig.Simulation.Step(3), Is.EqualTo(SimulationResult.Success));
                var fresh = rig.Single(ActionEventKind.Started);
                Assert.That(rig.Group.TryComplete(old.Action, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleAction));
                var oldAgentNewOperation = new ActionHandle(old.Action.Agent, fresh.Action.Operation, fresh.Action.Channel);
                Assert.That(rig.Group.TryComplete(oldAgentNewOperation, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.StaleAction));
                Assert.That(rig.Group.TryComplete(fresh.Action, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.Success));
            }
        }

        // ---- restart in place equals a fresh instance ------------------------------------------------

        /// <summary>
        /// Extends G1's reset-equals-fresh test to the action ledger: an agent restarted while its action is
        /// live (and completed, unconsumed) has, after teardown, the ledger of a freshly added agent with the same
        /// tree instance id, and from its first think on is byte-identical to it (machine, adapter with
        /// operations and completions, ledger) and behaves identically for 30 steps under the same game.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RestartInPlace_WithLiveActions_EqualsAFreshAgent(bool useJobs)
        {
            var crew = Program(CrewTree);
            var usedSense = new NativeArray<CrewSense>(1, Allocator.Persistent);
            var freshSense = new NativeArray<CrewSense>(1, Allocator.Persistent);
            using (var usedSimulation = new TreeSimulation(Settings(useJobs, 1)))
            using (var freshSimulation = new TreeSimulation(Settings(useJobs, 1)))
            using (usedSense)
            using (freshSense)
            using (var dummySense = new NativeArray<CrewSense>(1, Allocator.Persistent))
            {
                usedSimulation.TryAddGroup(crew, TreeGroupSettings.Default(1), out var usedGroup);
                usedGroup.TryBindInput(Sense, usedSense);
                usedGroup.TryAdd(ThinkSchedule.Every(1, 0), out var used);
                // The fresh container: one dummy agent first, so the fresh agent gets tree instance id 2 too.
                freshSimulation.TryAddGroup(crew, TreeGroupSettings.Default(1), out var dummyGroup);
                dummyGroup.TryBindInput(Sense, dummySense);
                dummyGroup.TryAdd(ThinkSchedule.Every(1000, 999), out _);
                freshSimulation.TryAddGroup(crew, TreeGroupSettings.Default(1), out var freshGroup);
                freshGroup.TryBindInput(Sense, freshSense);

                ActionHandle live = default;
                for (uint tick = 1; tick <= 10; tick++)
                {
                    usedSense[0] = new CrewSense { Threat = tick / 10f, Alarm = tick == 4 };
                    Assert.That(usedSimulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    var seen = usedGroup.GetActionEvents();
                    for (var index = 0; index < seen.Length; index++)
                        if (seen[index].Kind == ActionEventKind.Started) live = seen[index].Action;
                }
                Assert.That(usedGroup.LiveActionCountForTests(0), Is.EqualTo(1), "A job is live.");
                Assert.That(usedGroup.TryComplete(live, ActionOutcome.Succeeded, new JobResult { Score = 4f, Hits = 4 }), Is.EqualTo(SimulationResult.Success),
                    "Completed, not yet consumed.");
                Assert.That(usedGroup.TryRestart(used), Is.EqualTo(SimulationResult.Success));
                Assert.That(freshGroup.TryAdd(ThinkSchedule.Every(1, 0), out var fresh), Is.EqualTo(SimulationResult.Success));
                Assert.That(fresh.Index, Is.EqualTo(used.Index));

                // The probe runs after teardown and before the think that resets the instance (TreeGroup.Think),
                // so it can compare only the ledger: the teardown emptied it. Machine and adapter are compared
                // after every step, from the first think of the reset instance on.
                var usedLedger = new List<byte>();
                var freshLedger = new List<byte>();
                usedSimulation.StepProbeForTests = () => usedGroup.AppendActionStateForTests(0, usedLedger);
                freshSimulation.StepProbeForTests = () => freshGroup.AppendActionStateForTests(0, freshLedger);
                var usedBytes = new List<byte>();
                var freshBytes = new List<byte>();
                var path = new List<uint>();
                for (uint tick = 11; tick <= 40; tick++)
                {
                    var row = new CrewSense { Threat = (tick % 5) / 5f, Alarm = tick % 9 == 0 };
                    usedSense[0] = row;
                    freshSense[0] = row;
                    Assert.That(usedSimulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    Assert.That(freshSimulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    if (tick == 11)
                    {
                        Assert.That(usedLedger.Count, Is.GreaterThan(1));
                        // StartsLeft (second-last byte) is the per-think start allowance: the reset at the next
                        // think zeroes it, and every think sets it; the comparisons after each step cover it.
                        usedLedger.RemoveAt(usedLedger.Count - 2);
                        freshLedger.RemoveAt(freshLedger.Count - 2);
                        Assert.That(usedLedger, Is.EqualTo(freshLedger), "The teardown emptied the ledger (no live action, payloads zero).");
                        var restart = usedGroup.GetActionEvents();
                        Assert.That(restart[0].Kind, Is.EqualTo(ActionEventKind.Cancelled));
                        Assert.That(restart[0].CancelReason, Is.EqualTo(ActionCancelReason.AgentRestarted));
                        usedSimulation.StepProbeForTests = null;
                        freshSimulation.StepProbeForTests = null;
                    }
                    usedBytes.Clear();
                    freshBytes.Clear();
                    Capture(usedGroup, usedBytes);
                    Capture(freshGroup, freshBytes);
                    Assert.That(usedBytes, Is.EqualTo(freshBytes), "Machine, adapter and ledger equal the fresh agent's after tick " + tick);
                    var a = Fnv.Create();
                    var b = Fnv.Create();
                    AppendCore(usedGroup.CoreForTests(0), ref a, path);
                    AppendCore(freshGroup.CoreForTests(0), ref b, path);
                    var usedEvents = usedGroup.GetActionEvents();
                    var skip = tick == 11 ? 1 : 0; // the old instance's Cancelled
                    var freshEvents = freshGroup.GetActionEvents();
                    Assert.That(usedEvents.Length - skip, Is.EqualTo(freshEvents.Length), "tick " + tick);
                    for (var index = 0; index < freshEvents.Length; index++)
                    {
                        var x = usedEvents[index + skip];
                        var y = freshEvents[index];
                        a.Add((ulong)x.Kind); a.Add((ulong)x.CancelReason); AddOperation(ref a, x.Action.Operation);
                        b.Add((ulong)y.Kind); b.Add((ulong)y.CancelReason); AddOperation(ref b, y.Action.Operation);
                        // The same game for both: complete every start seen on a tick divisible by 3.
                        if (x.Kind == ActionEventKind.Started && tick % 3 == 0)
                        {
                            Assert.That(usedGroup.TryComplete(x.Action, ActionOutcome.Succeeded, new JobResult { Score = tick, Hits = 1 }), Is.EqualTo(SimulationResult.Success));
                            Assert.That(freshGroup.TryComplete(y.Action, ActionOutcome.Succeeded, new JobResult { Score = tick, Hits = 1 }), Is.EqualTo(SimulationResult.Success));
                        }
                    }
                    a.Add((ulong)usedGroup.GetCommands(Done).Length);
                    b.Add((ulong)freshGroup.GetCommands(Done).Length);
                    Assert.That(a.Value, Is.EqualTo(b.Value), "tick " + tick);
                }
            }
        }

        private static void Capture(TreeGroup group, List<byte> into)
        {
            var core = group.CoreForTests(0);
            var part = new List<byte>();
            core.Machine.CopyInstanceStateForTests(part);
            into.AddRange(part);
            core.GeneratedDispatch.CopyInstanceStateForTests(part);
            into.AddRange(part);
            group.AppendActionStateForTests(0, into);
        }

        // ---- capacity ----------------------------------------------------------------------------------

        /// <summary>
        /// Two starts in one think with an action capacity of 1: the second start is refused, the agent
        /// faults with CapacityExceeded, the first action's Started and Cancelled events are kept, Step
        /// succeeds and nothing throws. With a capacity of 2 all four events arrive. Same on both paths.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ActionCapacity_Overflow_FaultsTheAgentWithCapacityExceeded_NeverThrows(bool useJobs)
        {
            var program = Program(DoubleForgetTree);
            using (var simulation = new TreeSimulation(Settings(useJobs, 1)))
            using (var sense = new NativeArray<CrewSense>(3, Allocator.Persistent))
            {
                var tight = Stopping(3);
                tight.ActionCapacityPerAgent = 1;
                Assert.That(simulation.TryAddGroup(program, tight, out var tightGroup), Is.EqualTo(SimulationResult.Success));
                Assert.That(simulation.TryAddGroup(program, Stopping(3), out var roomy), Is.EqualTo(SimulationResult.Success));
                Assert.That(tightGroup.ActionEventCapacity, Is.EqualTo(3 * 3 * 1));
                Assert.That(roomy.ActionEventCapacity, Is.EqualTo(3 * 3 * 2));
                foreach (var group in new[] { tightGroup, roomy })
                {
                    Assert.That(group.TryBindInput(Sense, sense), Is.EqualTo(SimulationResult.Success));
                    for (var index = 0; index < 3; index++) group.TryAdd(out _);
                }
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));

                var faults = tightGroup.GetFaults();
                Assert.That(faults.Length, Is.EqualTo(3));
                for (var index = 0; index < 3; index++)
                {
                    Assert.That(faults[index].Agent.Index, Is.EqualTo(index));
                    Assert.That(faults[index].Cause, Is.EqualTo(BurstContextResult.CapacityExceeded));
                }
                var kept = tightGroup.GetActionEvents();
                Assert.That(kept.Length, Is.EqualTo(6), "Each agent's first action: Started then Cancelled (NodeEnded).");
                for (var index = 0; index < 6; index++)
                {
                    Assert.That(kept[index].Action.Agent.Index, Is.EqualTo(index / 2));
                    Assert.That(kept[index].Kind, Is.EqualTo(index % 2 == 0 ? ActionEventKind.Started : ActionEventKind.Cancelled));
                }
                for (var index = 0; index < 3; index++) Assert.That(tightGroup.LiveActionCountForTests(index), Is.Zero);

                Assert.That(roomy.GetFaults().Length, Is.Zero);
                Assert.That(roomy.GetActionEvents().Length, Is.EqualTo(12));
                Assert.That(roomy.GetRootCompletions().Length, Is.EqualTo(3));
            }
        }

        [Test]
        public void ActionCapacity_NegativeIsRefused_ZeroIsTheDefault_AndProgramsWithoutActionsHaveNone()
        {
            var program = Program(SingleJobTree);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            {
                var settings = TreeGroupSettings.Default(4);
                Assert.That(settings.ActionCapacityPerAgent, Is.EqualTo(TreeGroupSettings.DefaultActionCapacityPerAgent));
                settings.ActionCapacityPerAgent = -1;
                Assert.That(simulation.TryAddGroup(program, settings, out _), Is.EqualTo(SimulationResult.InvalidArgument));
                settings.ActionCapacityPerAgent = 0;
                Assert.That(simulation.TryAddGroup(program, settings, out var group), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.ActionEventCapacity, Is.EqualTo(4 * 3 * TreeGroupSettings.DefaultActionCapacityPerAgent));
                Assert.That(group.TryReserve(8), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.ActionEventCapacity, Is.EqualTo(8 * 3 * TreeGroupSettings.DefaultActionCapacityPerAgent), "Grows with the group.");

            }
            // A program without actions, in its own container (the job group above has an unbound input,
            // which would make that container's Step return InputMissing).
            var loop = Program(DeterministicLoopTree);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            {
                Assert.That(simulation.TryAddGroup(loop, TreeGroupSettings.Default(4), out var plain), Is.EqualTo(SimulationResult.Success));
                Assert.That(plain.ActionEventCapacity, Is.Zero);
                plain.TryAdd(out _);
                Assert.That(simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                Assert.That(plain.GetActionEvents().Length, Is.Zero);
            }
        }

        // ---- zero GC -----------------------------------------------------------------------------------

        [TestCase(false)]
        [TestCase(true)]
        public void Step_WithActionsCompletionsAndEvents_AllocatesNothing_AfterWarmUp(bool useJobs)
        {
            var crew = Program(CrewTree);
            var shift = Program(ShiftTree);
            using (var simulation = new TreeSimulation(Settings(useJobs, 1)))
            using (var crewSense = new NativeArray<CrewSense>(Agents, Allocator.Persistent))
            using (var shiftSense = new NativeArray<CrewSense>(Agents, Allocator.Persistent))
            {
                simulation.TryAddGroup(crew, TreeGroupSettings.Default(Agents), out var crews);
                simulation.TryAddGroup(shift, TreeGroupSettings.Default(Agents), out var shifts);
                crews.TryBindInput(Sense, crewSense);
                shifts.TryBindInput(Sense, shiftSense);
                crews.TrySetWakeOnChange(Sense, true);
                for (var index = 0; index < Agents; index++)
                {
                    crews.TryAdd(ThinkSchedule.Spread(5), out _);
                    shifts.TryAdd(ThinkSchedule.Spread(3), out _);
                }
                var groups = new[] { crews, shifts };
                var pending = new ActionHandle[8192];
                var due = new uint[8192];
                var count = 0;
                var started = 0;
                var done = 0;
                var cancelled = 0;

                void Tick(uint tick)
                {
                    var kept = 0;
                    for (var index = 0; index < count; index++)
                    {
                        if (due[index] != tick)
                        {
                            pending[kept] = pending[index];
                            due[kept] = due[index];
                            kept++;
                            continue;
                        }
                        var group = pending[index].Agent.Group == crews.Id ? crews : shifts;
                        var result = (tick & 1) == 0
                            ? group.TryComplete(pending[index], ActionOutcome.Succeeded, new JobResult { Score = 1.5f, Hits = 2 })
                            : group.TryComplete(pending[index], ActionOutcome.Failed);
                        if (result != SimulationResult.Success) throw new InvalidOperationException(result.ToString());
                    }
                    count = kept;
                    WriteSense(tick, crewSense, 1);
                    WriteSense(tick, shiftSense, 2);
                    simulation.Step(tick);
                    for (var g = 0; g < groups.Length; g++)
                    {
                        var group = groups[g];
                        done += group.GetCommands(Done).Length;
                        var events = group.GetActionEvents();
                        for (var index = 0; index < events.Length; index++)
                        {
                            var item = events[index];
                            if (item.Kind == ActionEventKind.Started)
                            {
                                started++;
                                group.TryGetStartPayload(item, out JobStart _);
                                pending[count] = item.Action;
                                due[count] = tick + 2;
                                count++;
                                continue;
                            }
                            cancelled++;
                            group.TryGetCancelPayload(item, out JobCancel _);
                            for (var p = 0; p < count; p++)
                                if (pending[p] == item.Action)
                                {
                                    pending[p] = pending[count - 1];
                                    due[p] = due[count - 1];
                                    count--;
                                    break;
                                }
                        }
                    }
                }

                uint tick = 0;
                for (; tick < 40; tick++) Tick(tick + 1);
                started = done = cancelled = 0;

                var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
                recorder.FilterToCurrentThread();
                recorder.enabled = false;
                recorder.enabled = true;
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var n = 0; n < 100; n++) Tick(++tick);
                var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                recorder.enabled = false;
                var blocks = recorder.sampleBlockCount;

                Assert.That(started, Is.GreaterThan(1000), "Actions start during the measured ticks.");
                Assert.That(done, Is.GreaterThan(200), "Completions are consumed during the measured ticks.");
                Assert.That(cancelled, Is.GreaterThan(500), "Actions are cancelled during the measured ticks.");
                Assert.That(bytes, Is.Zero, bytes + " managed bytes allocated in 100 warmed-up ticks.");
                Assert.That(blocks, Is.Zero, blocks + " GC.Alloc samples in 100 warmed-up ticks.");
            }
        }

        // ---- busy, old catalogs -------------------------------------------------------------------------

        [Test]
        public void CallsDuringStep_ReturnBusy()
        {
            using (var rig = NewRig(SingleJobTree, false, TreeGroupSettings.Default(2), ThinkSchedule.Every(1, 0)))
            {
                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var started = rig.Single(ActionEventKind.Started);
                var seen = new List<SimulationResult>();
                rig.Simulation.StepProbeForTests = () =>
                {
                    seen.Add(rig.Group.TryComplete(started.Action, ActionOutcome.Succeeded));
                    seen.Add(rig.Group.TryComplete(started.Action, ActionOutcome.Succeeded, new JobResult()));
                    seen.Add(rig.Group.TryGetStartPayload(started, out JobStart _));
                    seen.Add(rig.Group.TryGetCancelPayload(started, out JobCancel _));
                };
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                Assert.That(seen, Is.EqualTo(Enumerable.Repeat(SimulationResult.Busy, 4)));
                rig.Simulation.StepProbeForTests = null;
                Assert.That(rig.Group.TryComplete(started.Action, ActionOutcome.Succeeded), Is.EqualTo(SimulationResult.Success), "After the step the calls work again.");
            }
        }

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
            if (!SimulationActionCatalog.TryCreateRuntimeCatalog(allocator, out catalog, out failure)) return false;
            var property = typeof(GeneratedBurstCatalogV2).GetProperty("Executor", BindingFlags.Instance | BindingFlags.NonPublic);
            property.SetValue(catalog, new OldExecutor((IGeneratedBurstCatalogExecutorV2)property.GetValue(catalog)));
            return true;
        }

        /// <summary>A catalog without binding ids (generated before P10-010) cannot map action channels: programs with actions are refused with CatalogTooOld.</summary>
        [Test]
        public void OldCatalog_ProgramsWithActions_AreRefusedWithCatalogTooOld()
        {
            var old = Program(SingleJobTree, TryCreateOldCatalog);
            var current = Program(SingleJobTree);
            using (var simulation = new TreeSimulation(Settings(false, 1)))
            {
                Assert.That(simulation.TryAddGroup(old, TreeGroupSettings.Default(2), out var group), Is.EqualTo(SimulationResult.CatalogTooOld));
                Assert.That(group, Is.Null);
                Assert.That(simulation.TryAddGroup(current, TreeGroupSettings.Default(2), out group), Is.EqualTo(SimulationResult.Success));
            }
        }
    }
}
