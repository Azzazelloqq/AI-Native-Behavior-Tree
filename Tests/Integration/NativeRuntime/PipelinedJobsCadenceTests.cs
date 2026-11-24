using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-015 (D5): pins the cadence rule Documentation~/performance.md states for a forced
    /// <c>PipelinedJobs</c> group. The group advances one lifecycle step per scheduler frame and makes
    /// one logical update per (lifecycle steps of its slowest tree + 1) frames, and the whole group waits
    /// for its slowest member. Counted, not timed: frames between increments of
    /// <see cref="ProductionTreeHost.TotalUpdates"/>, and the steps from the scheduler's own frame
    /// entries (<see cref="SchedulerFrameEntry.ExecutedSteps"/>).
    /// </summary>
    public sealed class PipelinedJobsCadenceTests
    {
        // One leaf that stays Running: the io-wait1 shape of the performance table.
        private const string OneLeafTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11015.one-leaf"", ""name"": ""P11-015 one leaf"",
  ""root"": ""wait"",
  ""blackboard"": { ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 } },
  ""nodes"": { ""wait"": { ""type"": ""aibt.tests.external-tree-effect"", ""typeVersion"": 1, ""parameters"": { ""value"": 1 }, ""bindings"": { ""live-target"": ""live-target"" } } }
}";

        // A reactive sequence re-checks four conditions before the same Running leaf on every update,
        // so each logical update takes more lifecycle steps.
        private const string LongerTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11015.longer"", ""name"": ""P11-015 longer"",
  ""root"": ""guarded"",
  ""blackboard"": { ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 1.0 } },
  ""nodes"": {
    ""guarded"": { ""type"": ""aibt.core.reactive-sequence"", ""typeVersion"": 1, ""children"": [""a"", ""b"", ""c"", ""d"", ""wait""] },
    ""a"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""b"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""c"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""d"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""wait"": { ""type"": ""aibt.tests.external-tree-effect"", ""typeVersion"": 1, ""parameters"": { ""value"": 1 }, ""bindings"": { ""live-target"": ""live-target"" } }
  }
}";

        // One tree, two paths: with live-target > 0 the reactive sequence passes four conditions before
        // its Running leaf; with live-target 0 the first condition fails and the selector falls back to
        // the other Running leaf, in fewer steps. Agents of one asset share one program and catalog, so
        // with one profile they form one PipelinedJobs group.
        private const string TwoPathTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11015.two-path"", ""name"": ""P11-015 two paths"",
  ""root"": ""choose"",
  ""blackboard"": { ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 } },
  ""nodes"": {
    ""choose"": { ""type"": ""aibt.core.reactive-selector"", ""typeVersion"": 1, ""children"": [""long"", ""fallback""] },
    ""long"": { ""type"": ""aibt.core.reactive-sequence"", ""typeVersion"": 1, ""children"": [""a"", ""b"", ""c"", ""d"", ""wait""] },
    ""a"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""b"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""c"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""d"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""wait"": { ""type"": ""aibt.tests.external-tree-effect"", ""typeVersion"": 1, ""parameters"": { ""value"": 1 }, ""bindings"": { ""live-target"": ""live-target"" } },
    ""fallback"": { ""type"": ""aibt.tests.external-tree-effect"", ""typeVersion"": 1, ""parameters"": { ""value"": 2 }, ""bindings"": { ""live-target"": ""live-target"" } }
  }
}";

        private const int WarmUpFrames = 40;
        private const int MeasuredFrames = 120;

        private readonly List<Object> _created = new List<Object>();
        private readonly List<NativeTreeAgent> _agents = new List<NativeTreeAgent>();
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;

        [SetUp]
        public void SetUp()
        {
            var worldObject = Track(new GameObject("AIBT.Tests.P11015.World"));
            _scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            _world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(_world, "Awake");
            InvokePrivate(_world, "OnEnable");
            Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, true, out var capabilities, out var error),
                Is.True, error.ToString());
            _scheduler.SetJobsCapabilities(capabilities);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var agent in _agents) if (agent != null) agent.Deactivate();
            _agents.Clear();
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) Object.DestroyImmediate(_created[index]);
            _created.Clear();
        }

        [Test]
        public void OneWaitingLeaf_MakesOneLogicalUpdateEveryThreeFrames()
        {
            var agent = Spawn(Prepare(OneLeafTree), Pipelined("tests.p11015.one-leaf"));

            var measured = Measure(agent);

            Assert.That(measured.StepsPerUpdate.Distinct().ToArray(), Is.EqualTo(new[] { 2ul }), "Lifecycle steps per logical update.");
            Assert.That(measured.FramesPerUpdate.Distinct().ToArray(), Is.EqualTo(new[] { 3 }), "Frames per logical update = steps + 1.");
            Debug.Log("P11-015 D5: one waiting leaf: " + measured);
        }

        [Test]
        public void ATreeWithMoreSteps_MakesOneLogicalUpdatePerStepsPlusOneFrames()
        {
            var agent = Spawn(Prepare(LongerTree), Pipelined("tests.p11015.longer"));

            var measured = Measure(agent);

            var steps = measured.StepsPerUpdate.Distinct().ToArray();
            Assert.That(steps, Has.Length.EqualTo(1), "Every update takes the same number of steps: " + measured);
            Assert.That(steps[0], Is.GreaterThan(2ul), "The reactive sequence re-checks four conditions on every update.");
            Assert.That(measured.FramesPerUpdate.Distinct().ToArray(), Is.EqualTo(new[] { (int)steps[0] + 1 }),
                "Frames per logical update = lifecycle steps + 1: " + measured);
            Debug.Log("P11-015 D5: longer tree: " + measured);
        }

        [Test]
        public void TheWholeGroupWaitsForItsSlowestMember()
        {
            // Alone, each path keeps its own cadence (steps + 1)...
            var shortAlone = Measure(Spawn(Prepare(TwoPathTree), Pipelined("tests.p11015.short-alone"), 0f));
            var longAlone = Measure(Spawn(Prepare(TwoPathTree), Pipelined("tests.p11015.long-alone"), 1f));
            Assert.That(shortAlone.StepsPerUpdate.Max(), Is.LessThan(longAlone.StepsPerUpdate.Min()), "short " + shortAlone + "; long " + longAlone);
            Assert.That(shortAlone.FramesPerUpdate.Distinct().ToArray(), Is.EqualTo(new[] { (int)shortAlone.StepsPerUpdate.Max() + 1 }), "short alone: " + shortAlone);

            // ...and in one group (one asset, one profile) both update at the long path's cadence.
            var asset = Prepare(TwoPathTree);
            var profile = Pipelined("tests.p11015.group");
            var longer = Spawn(asset, profile, 1f);
            var shorter = Spawn(asset, profile, 0f);
            var both = Measure(longer, shorter);
            var slowest = both[0].StepsPerUpdate.Max();

            Assert.That(both[1].StepsPerUpdate.Max(), Is.LessThan(slowest), "The short path still takes fewer steps: " + both[1]);
            Assert.That(both[0].FramesPerUpdate.Distinct().ToArray(), Is.EqualTo(new[] { (int)slowest + 1 }), "long path: " + both[0]);
            Assert.That(both[1].FramesPerUpdate.Distinct().ToArray(), Is.EqualTo(new[] { (int)slowest + 1 }),
                "The short path in the same group waits for the long one: " + both[1]);
            Debug.Log("P11-015 D5: short alone " + shortAlone + "; long alone " + longAlone + "; group: long " + both[0] + "; short " + both[1]);
        }

        // The looping 16-node benchmark tree of Documentation~/performance.md (shared-loop16): memory
        // composites and waits, so the steps of an update depend on where the tree is. Each update still
        // takes its own steps + 1 frames; the numbers are logged for the performance page.
        [Test]
        public void TheSharedLoop16BenchmarkTree_TakesItsStepsPlusOneFramesPerUpdate()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PipelinedJobsCadenceTests).Assembly);
            var root = package == null ? System.IO.Path.Combine(Application.dataPath, "AIBT") : package.resolvedPath;
            var path = System.IO.Path.Combine(root, "Benchmarks~", "Phase10", "PlayerPerf", "Assets", "AibtPerf", "Trees", "shared-loop16.aibt.json");
            Assume.That(System.IO.File.Exists(path), Is.True, "The benchmark tree ships only in a source checkout.");
            var agent = Spawn(PrepareWithBuiltInLeaves(System.IO.File.ReadAllText(path)), Pipelined("tests.p11015.shared-loop16"), null);

            var measured = Measure(agent);

            var mean = measured.FramesPerUpdate.Average();
            Debug.Log("P11-015 D5: shared-loop16: " + measured + ", mean frames per update " + mean.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }

        // Two assets on one profile are two groups (one per program and catalog): no waiting.
        [Test]
        public void TreesOfDifferentAssets_AreSeparateGroups_EvenWithOneProfile()
        {
            var profile = Pipelined("tests.p11015.two-assets");
            var longer = Spawn(Prepare(LongerTree), profile);
            var oneLeaf = Spawn(Prepare(OneLeafTree), profile);

            var both = Measure(longer, oneLeaf);

            Assert.That(both[1].FramesPerUpdate.Distinct().ToArray(), Is.EqualTo(new[] { 3 }), "one leaf: " + both[1]);
            Assert.That(both[0].FramesPerUpdate.Distinct().ToArray(), Is.EqualTo(new[] { (int)both[0].StepsPerUpdate.Max() + 1 }), "longer: " + both[0]);
            Debug.Log("P11-015 D5: two assets, one profile: longer " + both[0] + "; one leaf " + both[1]);
        }

        private sealed class Cadence
        {
            internal readonly List<int> FramesPerUpdate = new List<int>();
            internal readonly List<ulong> StepsPerUpdate = new List<ulong>();
            internal ulong Updates;

            public override string ToString()
                => Updates + " updates in " + MeasuredFrames + " frames; frames per update {" + string.Join(",", FramesPerUpdate.Distinct())
                   + "}, steps per update {" + string.Join(",", StepsPerUpdate.Distinct()) + "}";
        }

        // A host alone in its group: every update takes its own lifecycle steps + 1 frames.
        private Cadence Measure(NativeTreeAgent agent)
        {
            var cadence = Measure(new[] { agent })[0];
            for (var update = 0; update < cadence.FramesPerUpdate.Count; update++)
                Assert.That((ulong)cadence.FramesPerUpdate[update], Is.EqualTo(cadence.StepsPerUpdate[update] + 1ul),
                    "Update " + update + ": frames = its lifecycle steps + 1. " + cadence);
            return cadence;
        }

        private Cadence[] Measure(params NativeTreeAgent[] agents)
        {
            Frames(WarmUpFrames);
            foreach (var agent in agents) Assert.That(agent.LastError, Is.Null);
            var result = agents.Select(_ => new Cadence()).ToArray();
            var lastUpdates = agents.Select(agent => agent.Host.TotalUpdates).ToArray();
            var framesSince = new int[agents.Length];
            var stepsSince = new ulong[agents.Length];
            var started = new bool[agents.Length];
            for (var frame = 0; frame < MeasuredFrames; frame++)
            {
                Frames(1);
                for (var index = 0; index < agents.Length; index++)
                {
                    var host = agents[index].Host;
                    framesSince[index]++;
                    stepsSince[index] += StepsThisFrame(host);
                    var updates = host.TotalUpdates;
                    if (updates == lastUpdates[index]) continue;
                    Assert.That(updates - lastUpdates[index], Is.EqualTo(1ul), "At most one logical update per frame.");
                    // The first increment closes a run that started before the window: not counted.
                    if (started[index])
                    {
                        result[index].FramesPerUpdate.Add(framesSince[index]);
                        result[index].StepsPerUpdate.Add(stepsSince[index]);
                        result[index].Updates++;
                    }
                    started[index] = true;
                    framesSince[index] = 0;
                    stepsSince[index] = 0;
                    lastUpdates[index] = updates;
                }
            }
            foreach (var agent in agents) Assert.That(agent.LastError, Is.Null);
            foreach (var cadence in result) Assert.That(cadence.Updates, Is.GreaterThan(2ul), "Enough whole updates in the window: " + cadence);
            return result;
        }

        private ulong StepsThisFrame(ProductionTreeHost host)
        {
            ulong steps = 0;
            for (var index = 0; index < _scheduler.FrameEntryCount; index++)
            {
                Assert.That(_scheduler.TryGetFrameEntry(index, out var entry), Is.True);
                if (entry.HostInstanceId != host.InstanceId) continue;
                Assert.That(entry.SelectedPolicy, Is.EqualTo(SchedulingPolicy.PipelinedJobs));
                steps += entry.ExecutedSteps;
            }
            return steps;
        }

        private static SchedulingProfile Pipelined(string id)
        {
            Assert.That(SchedulingProfile.TryCreate(id, 0, 1u, null, null, true, SchedulingPolicy.PipelinedJobs,
                out var profile, out var error), Is.True, error.ToString());
            return profile;
        }

        private NativeTreeAgent Spawn(ProductionTreeAsset asset, SchedulingProfile profile, float? liveTarget = 1f)
        {
            var gameObject = Track(new GameObject("AIBT.Tests.P11015.Agent" + _agents.Count));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = asset;
            agent.World = _world;
            agent.RestartMode = NativeTreeRestartMode.Never;
            agent.Profile = profile;
            _agents.Add(agent);
            agent.Activate();
            if (liveTarget.HasValue) agent.SetFact("live-target", liveTarget.Value);
            return agent;
        }

        private ProductionTreeAsset Prepare(string json)
        {
            var artifact = GeneratedShardMetadataMaterializer.MaterializeArtifact(
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ShardId,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ShardVersion,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.CanonicalDescriptorJson,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.DescriptorHash,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ManifestRegistryJson,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.NodeRegistryHash);
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P11015/tree.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, artifact.Nodes, artifact.RegisteredTypes,
                new ReferenceCompilerOptions("Tests/P11015/tree.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            var asset = ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(artifact.RegisteredTypes),
                ExternalTreeWriteFixtureCatalog.Fingerprint);
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(ExternalTreeWriteFixtureCatalog).FullName + ", " + typeof(ExternalTreeWriteFixtureCatalog).Assembly.GetName().Name);
            return Track(asset);
        }

        private ProductionTreeAsset PrepareWithBuiltInLeaves(string json)
        {
            var standard = Materialize(typeof(StandardNativeShard));
            var nodes = Materialize(typeof(BuiltInLeafShard)).Nodes.Concat(standard.Nodes)
                .Concat(Materialize(typeof(ExternalTreeWriteFixtureShard)).Nodes).ToArray();
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P11015/shared-loop16.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, standard.RegisteredTypes,
                new ReferenceCompilerOptions("Tests/P11015/shared-loop16.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            var asset = ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(standard.RegisteredTypes),
                JobsEquivalenceCatalog.Fingerprint);
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(JobsEquivalenceCatalog).FullName + ", " + typeof(JobsEquivalenceCatalog).Assembly.GetName().Name);
            return Track(asset);
        }

        private static GeneratedShardMetadataArtifact Materialize(System.Type shard)
        {
            var metadata = shard.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public);
            object Field(string name) => metadata.GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return GeneratedShardMetadataMaterializer.MaterializeArtifact(
                (string)Field("ShardId"), (uint)Field("ShardVersion"), (string)Field("CanonicalDescriptorJson"),
                (string)Field("DescriptorHash"), (string)Field("ManifestRegistryJson"), (string)Field("NodeRegistryHash"));
        }

        private void Frames(int count)
        {
            for (var frame = 0; frame < count; frame++)
            {
                InvokePrivate(_scheduler, "Update");
                foreach (var agent in _agents) if (agent != null) agent.ProcessRootResult();
            }
        }

        private T Track<T>(T value) where T : Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
