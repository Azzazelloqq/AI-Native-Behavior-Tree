using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    // A "project" catalog set that lists the package's standard native shard next to a shard from
    // another assembly (the P7-039 fixture shard stands in for a project's own nodes).
    [AibtCatalogSet("aibt.tests.p9002.standard-native-set", 1u, typeof(StandardNativeShard), typeof(ExternalTreeWriteFixtureShard))]
    public static partial class StandardNativeProbeCatalog { }

    /// <summary>
    /// P9-002 native part: the package-provided Burst shard (blackboard bool/float conditions and a
    /// float setter through generated bindings, a host-clock wait) is included in a project's
    /// [AibtCatalogSet] together with a project shard, compiled by the real generated-program
    /// compiler and run by the real ProductionTreeHost.
    /// </summary>
    public sealed class StandardNativeShardTests
    {
        private const string TreeJson = @"{
  ""format"": ""aibt.tree"",
  ""formatVersion"": 2,
  ""treeId"": ""tests.p9002.standard-native"",
  ""name"": ""P9-002 standard native shard probe"",
  ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""armed"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false },
    ""score"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""project-node"", ""armed-check"", ""wait"", ""set-score"", ""score-check""] },
    ""project-node"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""armed-check"": { ""type"": ""aibt.native.bool-is-true"", ""typeVersion"": 1, ""bindings"": { ""key"": ""armed"" } },
    ""wait"": { ""type"": ""aibt.native.wait-microseconds"", ""typeVersion"": 1, ""parameters"": { ""duration-microseconds"": 500000 } },
    ""set-score"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 2.5 }, ""bindings"": { ""key"": ""score"" } },
    ""score-check"": { ""type"": ""aibt.native.float-at-least"", ""typeVersion"": 1, ""parameters"": { ""threshold"": 2.0 }, ""bindings"": { ""key"": ""score"" } }
  }
}";

        private long _now;

        [Test]
        public void ThePackageShardRunsNativelyInAProjectCatalogSetNextToAProjectShard()
        {
            using (var scenario = Scenario.Create(() => _now))
            {
                Write(scenario.Host, "live-target", 1f, BuiltInBlackboardTypes.Float32);
                Write(scenario.Host, "armed", true, BuiltInBlackboardTypes.Bool);

                _now = 1_000_000;
                Update(scenario.Host);
                Assert.That(scenario.Host.LastRootResult, Is.Null, "Waiting: the host-clock wait has started.");

                _now = 1_300_000;
                Update(scenario.Host);
                Assert.That(scenario.Host.LastRootResult, Is.Null, "Still waiting: 0.3 s of a 0.5 s wait.");

                _now = 1_600_000;
                Update(scenario.Host);
                Assert.That(scenario.Host.LastRootResult, Is.EqualTo(NodeStatus.Success),
                    "After the wait, set-float writes 2.5 and float-at-least reads it back through its binding.");
            }
        }

        [Test]
        public void ABoolConditionThroughItsBindingFailsWhileTheFlagIsFalse()
        {
            using (var scenario = Scenario.Create(() => _now))
            {
                Write(scenario.Host, "live-target", 1f, BuiltInBlackboardTypes.Float32);
                Update(scenario.Host);
                Assert.That(scenario.Host.LastRootResult, Is.EqualTo(NodeStatus.Failure), "'armed' keeps its default false.");
            }
        }

        [Test]
        public void TheGeneratedCatalogDeclaresBothShardsNodes()
        {
            var ids = Materialize(typeof(StandardNativeShard)).Nodes.Select(node => node.Manifest.TypeId).ToArray();
            // P11-002 stage 1 grew the shard from the four P9-002 nodes to 24.
            Assert.That(ids, Is.EquivalentTo(StandardNativeLibraryIds.All));
            Assert.That(StandardNativeProbeCatalog.IsUsable, Is.True);
        }

        private static void Write<T>(ProductionTreeHost host, string key, T value, BlackboardTypeDescriptor type) where T : unmanaged
        {
            Assert.That(host.TryResolveExternalTreeWrite<T>(key, NativeBlackboardTypeIdV2.BuiltIn(type), out var handle, out var failure),
                Is.True, failure.Code.ToString());
            Assert.That(host.TryWriteExternalTreeValue(handle, value, out _, out failure), Is.True, failure.Code.ToString());
        }

        private static void Update(ProductionTreeHost host)
            => typeof(ProductionTreeHost).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(host, null);

        private static GeneratedShardMetadataArtifact Materialize(System.Type shard)
        {
            var metadata = shard.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public);
            object Field(string name) => metadata.GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return GeneratedShardMetadataMaterializer.MaterializeArtifact(
                (string)Field("ShardId"), (uint)Field("ShardVersion"), (string)Field("CanonicalDescriptorJson"),
                (string)Field("DescriptorHash"), (string)Field("ManifestRegistryJson"), (string)Field("NodeRegistryHash"));
        }

        private sealed class Scenario : System.IDisposable
        {
            private GameObject _gameObject;
            private GeneratedBurstCatalogV2 _catalog;

            internal ProductionTreeHost Host { get; private set; }

            internal static Scenario Create(System.Func<long> clock)
            {
                var standard = Materialize(typeof(StandardNativeShard));
                var project = Materialize(typeof(ExternalTreeWriteFixtureShard));
                var nodes = standard.Nodes.Concat(project.Nodes).ToArray();

                const string sourceId = "Tests/P9002/standard-native.aibt.json";
                var parsed = CanonicalTreeJson.Parse(TreeJson, sourceId);
                Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
                var options = new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 5));
                var compiled = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, standard.RegisteredTypes, options);
                Assert.That(compiled.Success, Is.True, string.Join("\n", compiled.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));

                Assert.That(StandardNativeProbeCatalog.TryCreateRuntimeCatalog(Allocator.Persistent, out var catalog, out var catalogFailure),
                    Is.True, catalogFailure.ToString());
                Assert.That(compiled.Program.TryCreateRuntimeDefinitionV2(catalog, standard.RegisteredTypes, out var definition, out var definitionFailure),
                    Is.True, definitionFailure.ToString());

                var gameObject = new GameObject("AIBT.Tests.P9002.StandardNativeHost");
                var host = gameObject.AddComponent<ProductionTreeHost>();
                var traceCapacity = new NativeTraceChannelCapacityV1(recordCapacity: 16, payloadCapacity: 0, maximumPayloadBytes: 0, emissionCapacity: 64);
                Assert.That(host.TryBootstrap(definition, catalog, traceCapacity, clock, out var bootstrapFailure), Is.True, bootstrapFailure.Code.ToString());
                return new Scenario { _gameObject = gameObject, _catalog = catalog, Host = host };
            }

            public void Dispose()
            {
                var host = _gameObject.GetComponent<ProductionTreeHost>();
                if (host != null)
                    typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(host, null);
                Object.DestroyImmediate(_gameObject);
                Assert.That(_catalog.TryDispose(out var failure), Is.True, failure.ToString());
            }
        }
    }
}
