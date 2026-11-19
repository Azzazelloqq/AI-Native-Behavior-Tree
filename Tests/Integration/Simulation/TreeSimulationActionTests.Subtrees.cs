using System.Linq;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.Simulation;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP10010;
using AIBT.Tests.CodeGen.GenerationP10011;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// ADR-P11-001 stage 2: a subtree with an input and an external action, used twice in one tree,
    /// in a <see cref="TreeGroup"/>. Both instances read the group's one input channel and start
    /// actions on the same action channel; each instance's action completes on its own.
    /// </summary>
    public sealed partial class TreeSimulationActionTests
    {
        private const string WorkerSource = "Tests/P11/worker.aibt";

        // Unless the alarm is up, start a job and wait for the game.
        private const string WorkerTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.worker"", ""name"": ""P11 worker"",
  ""root"": ""root"",
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-selector"", ""typeVersion"": 1, ""children"": [""alarm"", ""job""] },
    ""alarm"": { ""type"": ""aibt.tests.p10011.alarm"", ""typeVersion"": 1 },
    ""job"": { ""type"": ""aibt.tests.p10011.job"", ""typeVersion"": 1, ""parameters"": { ""power"": 1.0, ""patience"": 0, ""forget"": false } }
  }
}";

        private const string TwoWorkersTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.two-workers"", ""name"": ""P11 two workers"",
  ""root"": ""both"",
  ""nodes"": {
    ""both"": { ""type"": ""aibt.core.parallel"", ""typeVersion"": 1, ""parameters"": { ""policy"": ""require-all-success"" }, ""children"": [""a"", ""b""] },
    ""a"": { ""subtree"": { ""treeId"": ""tests.p11.worker"", ""interfaceVersion"": 1, ""keys"": { } } },
    ""b"": { ""subtree"": { ""treeId"": ""tests.p11.worker"", ""interfaceVersion"": 1, ""keys"": { } } }
  }
}";

        [TestCase(false)]
        [TestCase(true)]
        public void SubtreeUsedTwice_SharesTheGroupsChannels_AndCompletesPerInstance(bool useJobs)
        {
            var compiled = CompileWithSubtrees(TwoWorkersTree, out var registered);
            var debug = compiled.Program.SemanticProgram.DebugMap.ToDictionary(entry => entry.RuntimeNodeIndex, entry => entry.AuthoringNodeId.Value);
            var asset = ProductionTreeAsset.Create(compiled.Program.CreateNativeBlackboardBindingV2(registered), SimulationActionCatalog.Fingerprint);
            _objects.Add(asset);
            Assert.That(asset.TryCreateProgram(SimulationActionCatalog.TryCreateRuntimeCatalog, out var program, out var failure), Is.True, failure.ToString());
            _programs.Add(program);

            var groupSettings = Stopping(2);
            using (var rig = new Rig
            {
                Simulation = new TreeSimulation(Settings(useJobs, 1)),
                Sense = new NativeArray<CrewSense>(groupSettings.Capacity, Allocator.Persistent),
            })
            {
                Assert.That(rig.Simulation.TryAddGroup(program, groupSettings, out rig.Group), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.TryBindInput(Sense, rig.Sense), Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Group.TryAdd(ThinkSchedule.Every(1, 0), out rig.Agent), Is.EqualTo(SimulationResult.Success));
                rig.Sense[0] = new CrewSense { Threat = 0.5f, Alarm = false };

                Assert.That(rig.Simulation.Step(1), Is.EqualTo(SimulationResult.Success));
                var events = rig.Group.GetActionEvents();
                Assert.That(events.Length, Is.EqualTo(2), "Each instance's job starts its own action.");
                Assert.That(events.ToArray().All(item => item.Kind == ActionEventKind.Started && item.Action.Agent.Equals(rig.Agent)), Is.True);
                Assert.That(events[0].Action.Channel, Is.EqualTo(events[1].Action.Channel), "Both instances use the one action channel.");
                var byNode = events.ToArray().ToDictionary(item => debug[item.Action.Operation.NodeIndex.Value], item => item);
                Assert.That(byNode.Keys.OrderBy(id => id), Is.EqualTo(new[] { "a:job", "b:job" }));
                foreach (var started in byNode.Values)
                {
                    Assert.That(rig.Group.TryGetStartPayload(started, out JobStart start), Is.EqualTo(SimulationResult.Success));
                    Assert.That(start.Power, Is.EqualTo(1.5f), "Both instances read the same input row.");
                }
                Assert.That(rig.Group.LiveActionCountForTests(0), Is.EqualTo(2));

                Assert.That(rig.Group.TryComplete(byNode["b:job"].Action, ActionOutcome.Succeeded, new JobResult { Score = 1f, Hits = 1 }),
                    Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(2), Is.EqualTo(SimulationResult.Success));
                var done = rig.Group.GetCommands(Done);
                Assert.That(done.Length, Is.EqualTo(1), "Only instance b's job read a completion.");
                Assert.That(done[0].Value.Score, Is.EqualTo(2f));
                Assert.That(rig.Group.GetActionEvents().Length, Is.Zero, "Instance a's action stays live; nothing is cancelled.");
                Assert.That(rig.Group.GetRootCompletions().Length, Is.Zero);
                Assert.That(rig.Group.LiveActionCountForTests(0), Is.EqualTo(1));

                Assert.That(rig.Group.TryComplete(byNode["a:job"].Action, ActionOutcome.Succeeded, new JobResult { Score = 10f, Hits = 0 }),
                    Is.EqualTo(SimulationResult.Success));
                Assert.That(rig.Simulation.Step(3), Is.EqualTo(SimulationResult.Success));
                done = rig.Group.GetCommands(Done);
                Assert.That(done.Length, Is.EqualTo(1));
                Assert.That(done[0].Value.Score, Is.EqualTo(10f));
                var completions = rig.Group.GetRootCompletions();
                Assert.That(completions.Length, Is.EqualTo(1));
                Assert.That(completions[0].Result, Is.EqualTo(NodeStatus.Success));
                Assert.That(rig.Group.LiveActionCountForTests(0), Is.Zero);
                Assert.That(rig.Group.GetFaults().Length, Is.Zero);
            }
        }

        private static GeneratedCompiledProgramV2Result CompileWithSubtrees(string json, out RegisteredBlackboardTypeCatalog registered)
        {
            var standard = Materialize(typeof(StandardNativeShard));
            var io = Materialize(typeof(SimulationIoShard));
            var actions = Materialize(typeof(SimulationActionShard));
            var nodes = Materialize(typeof(BuiltInLeafShard)).Nodes.Concat(standard.Nodes).Concat(io.Nodes).Concat(actions.Nodes).ToArray();
            registered = new RegisteredBlackboardTypeCatalog(
                standard.RegisteredTypes.Entries.Concat(io.RegisteredTypes.Entries).Concat(actions.RegisteredTypes.Entries));
            var worker = CanonicalTreeJson.Parse(WorkerTree, WorkerSource);
            Assert.That(worker.Success, Is.True, string.Join("\n", worker.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var resolver = new InMemorySubtreeResolver();
            resolver.Add(worker.Document, WorkerSource);
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P11/two-workers.aibt");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, registered,
                new ReferenceCompilerOptions("Tests/P11/two-workers.aibt", new ReferenceCompilationPolicy(requireDeterministicNodes: false), new CompiledCompilerVersion(1, 0, 0, 6)),
                resolver);
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + " " + d.Location.DocumentId + d.Location.JsonPointer + ": " + d.Message)));
            return result;
        }
    }
}
