using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-016 item 1: every generated leaf's dispatch workspace reads the catalog's own metadata
    /// arrays (cases, field tables, bindings, canonical ranges and rules) instead of a private copy.
    /// The catalog stays the only owner: a borrowing workspace never frees them, the catalog refuses
    /// disposal while a host that borrows lives, and a catalog that does not lend gives copying
    /// workspaces with the same behaviour. Tree and catalog are the P9-002 standard-native probe
    /// (<see cref="StandardNativeShardTests"/>): five generated leaves under one memory sequence.
    /// </summary>
    public sealed class BorrowedCatalogMetadataTests
    {
        private const string TreeJson = @"{
  ""format"": ""aibt.tree"",
  ""formatVersion"": 2,
  ""treeId"": ""tests.p11016.borrowed-metadata"",
  ""name"": ""P11-016 borrowed catalog metadata"",
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

        private const int LeafCount = 5;
        private long _now;
        private NativeLeakDetectionMode _leakMode;

        [SetUp]
        public void EnableLeakDetection()
        {
            _leakMode = NativeLeakDetection.Mode;
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
        }

        [TearDown]
        public void RestoreLeakDetection() => NativeLeakDetection.Mode = _leakMode;

        [Test]
        public void EveryLeafWorkspaceReadsTheCatalogsOwnMetadataArrays()
        {
            var catalog = CreateCatalog();
            var host = CreateHost(catalog, out var gameObject);
            try
            {
                Assert.That(catalog.TryLendWorkspaceMetadata(out var metadata), Is.True);
                var workspaces = host.GeneratedDispatchAdapter.LeafWorkspacesForTests().Where(w => w != null).ToArray();
                Assert.That(workspaces.Length, Is.EqualTo(LeafCount));
                foreach (var workspace in workspaces)
                {
                    Assert.That(workspace.BorrowsCatalogMetadata, Is.True);
                    AssertSameArrays(workspace.MetadataForTests, metadata);
                }

                RunToSuccess(host);
            }
            finally
            {
                DestroyHost(gameObject);
                Assert.That(catalog.TryDispose(out var failure), Is.True, failure.ToString());
            }
        }

        [Test]
        public void TheCatalogRefusesDisposalWhileABorrowingHostLivesAndStaysUsableAfterIt()
        {
            var catalog = CreateCatalog();
            var host = CreateHost(catalog, out var gameObject);
            Assert.That(host.GeneratedDispatchAdapter.LeafWorkspacesForTests().Any(w => w != null && w.BorrowsCatalogMetadata), Is.True);

            Assert.That(catalog.TryDispose(out var refused), Is.False, "A host borrows the catalog's arrays.");
            Assert.That(refused, Is.EqualTo(BurstContextResult.PhaseViolation));

            DestroyHost(gameObject);

            // Every borrowing workspace is disposed with the host's adapter; the catalog's arrays are intact.
            Assert.That(catalog.IsCreated, Is.True);
            Assert.That(catalog.TryLendWorkspaceMetadata(out var metadata), Is.True);
            Assert.That(metadata.Cases.IsCreated && metadata.Cases.Length > 0, Is.True);
            var first = metadata.Cases[0];
            Assert.That(catalog.TryFindCase(first.TypeNumericId, first.TypeVersion, out var caseIndex), Is.True);
            Assert.That(caseIndex, Is.EqualTo(0u));
            Assert.That(metadata.ConfigurationFields.ToArray().Length, Is.EqualTo(metadata.ConfigurationFields.Length));

            // A second host on the same catalog borrows the same arrays and runs.
            var second = CreateHost(catalog, out var secondObject);
            try
            {
                RunToSuccess(second);
            }
            finally
            {
                DestroyHost(secondObject);
            }

            Assert.That(catalog.TryDispose(out var failure), Is.True, failure.ToString());
        }

        [Test]
        public void ACatalogThatDoesNotLendGivesCopyingWorkspacesThatRunTheSame()
        {
            var catalog = CreateCatalog();
            catalog.DisableWorkspaceMetadataLendingForTests();
            Assert.That(catalog.TryLendWorkspaceMetadata(out _), Is.False);
            var host = CreateHost(catalog, out var gameObject);
            try
            {
                var workspaces = host.GeneratedDispatchAdapter.LeafWorkspacesForTests().Where(w => w != null).ToArray();
                Assert.That(workspaces.Length, Is.EqualTo(LeafCount));
                Assert.That(workspaces.All(w => !w.BorrowsCatalogMetadata), Is.True);
                Assert.That(workspaces.Select(w => w.CasesForTests).Distinct().Count(), Is.EqualTo(LeafCount),
                    "Copying workspaces each hold their own cases array.");
                catalog.EnableWorkspaceMetadataLendingForTests();
                Assert.That(catalog.TryLendWorkspaceMetadata(out var lent), Is.True);
                foreach (var workspace in workspaces)
                {
                    var own = workspace.MetadataForTests;
                    Assert.That(own.Cases.Equals(lent.Cases) || own.ConfigurationFields.Equals(lent.ConfigurationFields)
                        || own.MemoryFields.Equals(lent.MemoryFields) || own.CanonicalRules.Equals(lent.CanonicalRules), Is.False,
                        "A copying workspace reads none of the catalog's arrays.");
                    Assert.That(own.Cases.ToArray(), Is.EqualTo(lent.Cases.ToArray()), "Its copy holds the same cases.");
                }
                RunToSuccess(host);
            }
            finally
            {
                DestroyHost(gameObject);
                Assert.That(catalog.TryDispose(out var failure), Is.True, failure.ToString());
            }
        }

        [Test]
        public void BorrowingIsRefusedWhenTheMetadataDoesNotMatchTheShape()
        {
            var catalog = CreateCatalog();
            try
            {
                var shape = catalog.CreateWorkspaceShape();
                var capacity = catalog.CreateWorkspaceCapacity();
                Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreateBorrowing(
                    in shape, default, in capacity, Allocator.Persistent, out var none, out var failure), Is.False);
                Assert.That(none, Is.Null);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));

                Assert.That(catalog.TryLendWorkspaceMetadata(out var metadata), Is.True);

                // A real, created metadata set of another catalog: its tables have other lengths.
                Assert.That(JobsEquivalenceCatalog.TryCreateRuntimeCatalog(Allocator.Persistent, out var other, out var otherFailure),
                    Is.True, otherFailure.ToString());
                try
                {
                    Assert.That(other.TryLendWorkspaceMetadata(out var foreign), Is.True);
                    Assert.That(foreign.Cases.Length, Is.Not.EqualTo(metadata.Cases.Length), "The other catalog has more cases.");
                    Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreateBorrowing(
                        in shape, in foreign, in capacity, Allocator.Persistent, out none, out failure), Is.False);
                    Assert.That(none, Is.Null);
                    Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
                }
                finally
                {
                    Assert.That(other.TryDispose(out var disposeFailure), Is.True, disposeFailure.ToString());
                }

                // The catalog's own set with one array one element short is refused too.
                var shortCases = new NativeBurstDispatchWorkspaceMetadataV2(
                    metadata.Cases.GetSubArray(0, metadata.Cases.Length - 1), metadata.ConfigurationFields, metadata.MemoryFields,
                    metadata.Bindings, metadata.ValueFields, metadata.CaseCanonicalRanges, metadata.BindingCanonicalRanges,
                    metadata.CanonicalRules);
                Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreateBorrowing(
                    in shape, in shortCases, in capacity, Allocator.Persistent, out none, out failure), Is.False);
                Assert.That(none, Is.Null);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
                Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreateBorrowing(
                    in shape, in metadata, in capacity, Allocator.Persistent, out var workspace, out failure), Is.True, failure.ToString());
                Assert.That(workspace.TryDispose(out failure), Is.True, failure.ToString());
                Assert.That(metadata.Cases.IsCreated && metadata.Cases[0].TypeNumericId != 0, Is.True,
                    "Disposing a borrowing workspace leaves the lender's arrays alone.");
            }
            finally
            {
                Assert.That(catalog.TryDispose(out var failure), Is.True, failure.ToString());
            }
        }

        private static void AssertSameArrays(NativeBurstDispatchWorkspaceMetadataV2 read, NativeBurstDispatchWorkspaceMetadataV2 lent)
        {
            // NativeArray equality is the same buffer and length: the workspace reads the catalog's array.
            Assert.That(read.Cases.Equals(lent.Cases), Is.True, "cases");
            Assert.That(read.ConfigurationFields.Equals(lent.ConfigurationFields), Is.True, "configuration fields");
            Assert.That(read.MemoryFields.Equals(lent.MemoryFields), Is.True, "memory fields");
            Assert.That(read.Bindings.Equals(lent.Bindings), Is.True, "bindings");
            Assert.That(read.ValueFields.Equals(lent.ValueFields), Is.True, "value fields");
            Assert.That(read.CaseCanonicalRanges.Equals(lent.CaseCanonicalRanges), Is.True, "case canonical ranges");
            Assert.That(read.BindingCanonicalRanges.Equals(lent.BindingCanonicalRanges), Is.True, "binding canonical ranges");
            Assert.That(read.CanonicalRules.Equals(lent.CanonicalRules), Is.True, "canonical rules");
        }

        private static GeneratedBurstCatalogV2 CreateCatalog()
        {
            Assert.That(StandardNativeProbeCatalog.TryCreateRuntimeCatalog(Allocator.Persistent, out var catalog, out var failure),
                Is.True, failure.ToString());
            return catalog;
        }

        private ProductionTreeHost CreateHost(GeneratedBurstCatalogV2 catalog, out GameObject gameObject)
        {
            var standard = Materialize(typeof(StandardNativeShard));
            var project = Materialize(typeof(ExternalTreeWriteFixtureShard));
            var nodes = standard.Nodes.Concat(project.Nodes).ToArray();
            const string sourceId = "Tests/P11016/borrowed-metadata.aibt.json";
            var parsed = CanonicalTreeJson.Parse(TreeJson, sourceId);
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var options = new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 5));
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, standard.RegisteredTypes, options);
            Assert.That(compiled.Success, Is.True, string.Join("\n", compiled.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            Assert.That(compiled.Program.TryCreateRuntimeDefinitionV2(catalog, standard.RegisteredTypes, out var definition, out var definitionFailure),
                Is.True, definitionFailure.ToString());

            gameObject = new GameObject("AIBT.Tests.P11016.BorrowedMetadataHost");
            var host = gameObject.AddComponent<ProductionTreeHost>();
            var traceCapacity = new NativeTraceChannelCapacityV1(recordCapacity: 16, payloadCapacity: 0, maximumPayloadBytes: 0, emissionCapacity: 64);
            Assert.That(host.TryBootstrap(definition, catalog, traceCapacity, () => _now, out var bootstrapFailure), Is.True, bootstrapFailure.Code.ToString());
            return host;
        }

        private void RunToSuccess(ProductionTreeHost host)
        {
            Write(host, "live-target", 1f, BuiltInBlackboardTypes.Float32);
            Write(host, "armed", true, BuiltInBlackboardTypes.Bool);
            _now = 1_000_000;
            Update(host);
            Assert.That(host.LastRootResult, Is.Null, "Waiting: the host-clock wait has started.");
            _now = 1_600_000;
            Update(host);
            Assert.That(host.LastRootResult, Is.EqualTo(NodeStatus.Success),
                "After the wait, set-float writes 2.5 and float-at-least reads it back through its binding.");
        }

        private static void DestroyHost(GameObject gameObject)
        {
            var host = gameObject.GetComponent<ProductionTreeHost>();
            if (host != null)
                typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(host, null);
            Object.DestroyImmediate(gameObject);
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
    }
}
