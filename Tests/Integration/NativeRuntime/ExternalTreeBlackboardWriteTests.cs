using System.IO;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Integration.NativeRuntime
{
    [AibtCatalogSet("aibt.tests.p7039.external-tree-write-set", 1u, typeof(ExternalTreeWriteFixtureShard))]
    public static partial class ExternalTreeWriteFixtureCatalog
    {
        internal static BurstCatalogHandshake HandshakeForTests()
            => new BurstCatalogHandshake(
                2u,
                Fingerprint,
                NodeRegistryFingerprint,
                1u,
                1u,
                ConfigurationLayoutFingerprint,
                MemoryLayoutFingerprint,
                AccessLayoutFingerprint);
    }

    /// <summary>
    /// P7-039 (ADR-P7-039) live proof: an external, non-node C# write into a running native tree
    /// instance's Tree-scope blackboard, through the real public <see cref="ProductionTreeHost"/>
    /// surface. The fixture tree declares two Tree-scope keys: "live-target" (read only by
    /// <see cref="ExternalTreeReaderNode"/>, never written by any node -- eligible) and
    /// "owned-target" (written by <see cref="ExternalTreeWriterNode"/> -- ineligible, the negative
    /// case). Every layout/offset/binding ordinal comes from the real generator and compiler; no
    /// dispatch shape is hand-authored here.
    /// </summary>
    public sealed class ExternalTreeBlackboardWriteTests
    {
        private const string TreeJson = @"{
  ""format"": ""aibt.tree"",
  ""formatVersion"": 2,
  ""treeId"": ""tests.p7039.external-tree-write"",
  ""name"": ""P7-039 External Tree Write Fixture"",
  ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""owned-target"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0 }
  },
  ""nodes"": {
    ""reader"": {
      ""type"": ""aibt.tests.external-tree-write-reader"",
      ""typeVersion"": 1,
      ""bindings"": { ""live-target"": ""live-target"" }
    },
    ""writer"": {
      ""type"": ""aibt.tests.external-tree-write-writer"",
      ""typeVersion"": 1,
      ""parameters"": { ""value"": 7 },
      ""bindings"": { ""owned-target"": ""owned-target"" }
    },
    ""root"": {
      ""type"": ""aibt.core.reactive-sequence"",
      ""typeVersion"": 1,
      ""children"": [""reader"", ""writer""]
    }
  }
}";

        [Test]
        public void ExternalWrite_ResolvedForEligibleKey_ObservedByNodeOnFirstTickAfterWrite()
        {
            using (var scenario = Scenario.Create())
            {
                var expectedType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
                Assert.That(scenario.Host.TryResolveExternalTreeWrite<float>(
                    "live-target", expectedType, out var handle, out var resolveFailure),
                    Is.True, resolveFailure.Code.ToString());
                Assert.That(scenario.Host.TryWriteExternalTreeValue(handle, 3.5f, out var changed, out var writeFailure),
                    Is.True, writeFailure.Code.ToString());
                Assert.That(changed, Is.True);

                InvokeUpdate(scenario.Host);

                Assert.That(scenario.Host.LastRootResult, Is.EqualTo(NodeStatus.Success),
                    "The reader node must observe the externally-written value on its very next tick.");
            }
        }

        [Test]
        public void WithoutExternalWrite_DefaultZeroValueLeavesReaderFailing()
        {
            using (var scenario = Scenario.Create())
            {
                InvokeUpdate(scenario.Host);
                Assert.That(scenario.Host.LastRootResult, Is.EqualTo(NodeStatus.Failure),
                    "Without an external write, the declared Float32 default (0.0) must leave the reader failing -- "
                    + "the control proving the positive test's success genuinely comes from the write.");
            }
        }

        [Test]
        public void ResolveExternalTreeWrite_UnknownKey_RejectsWithNoStateChange()
        {
            using (var scenario = Scenario.Create())
            {
                var expectedType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
                Assert.That(scenario.Host.TryResolveExternalTreeWrite<float>(
                    "no-such-key", expectedType, out _, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardUndeclaredAccess));
            }
        }

        [Test]
        public void ResolveExternalTreeWrite_TypeMismatch_Rejects()
        {
            using (var scenario = Scenario.Create())
            {
                var wrongType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32);
                Assert.That(scenario.Host.TryResolveExternalTreeWrite<int>(
                    "live-target", wrongType, out _, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardTypeMismatch));
            }
        }

        [Test]
        public void ResolveExternalTreeWrite_KeyANodeWrites_RejectsAsNodeOwned()
        {
            using (var scenario = Scenario.Create())
            {
                var expectedType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32);
                Assert.That(scenario.Host.TryResolveExternalTreeWrite<int>(
                    "owned-target", expectedType, out _, out var failure), Is.False,
                    "A key some node in the tree writes must never be externally writable.");
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch));
            }
        }

        [Test]
        public void ExternalWrite_MidRound_RefusedStructurally_LikeEveryOtherReentrantEntryPoint()
        {
            using (var scenario = Scenario.Create())
            {
                var expectedType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
                Assert.That(scenario.Host.TryResolveExternalTreeWrite<float>(
                    "live-target", expectedType, out var handle, out var resolveFailure),
                    Is.True, resolveFailure.Code.ToString());

                SetDriving(scenario.Host, true);
                try
                {
                    Assert.That(scenario.Host.TryWriteExternalTreeValue(handle, 1.0f, out var changed, out var failure),
                        Is.False, "A write attempted while the host is mid-round must be refused, never race.");
                    Assert.That(changed, Is.False);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                }
                finally
                {
                    SetDriving(scenario.Host, false);
                }

                InvokeUpdate(scenario.Host);
                Assert.That(scenario.Host.LastRootResult, Is.EqualTo(NodeStatus.Failure),
                    "The refused write must never have touched memory -- the reader still sees the declared default.");
            }
        }

        [Test]
        public void ExternalWrite_HandleFromDifferentHost_RefusedStructurally()
        {
            using (var scenarioA = Scenario.Create())
            using (var scenarioB = Scenario.Create())
            {
                var expectedType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
                Assert.That(scenarioA.Host.TryResolveExternalTreeWrite<float>(
                    "live-target", expectedType, out var handleFromA, out var resolveFailure),
                    Is.True, resolveFailure.Code.ToString());

                Assert.That(scenarioB.Host.TryWriteExternalTreeValue(handleFromA, 2.0f, out var changed, out var failure),
                    Is.False, "A handle resolved against a different host must never be trusted.");
                Assert.That(changed, Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
            }
        }

        [Test]
        public void DelegateBasedHost_ExternalWrite_RefusedStructurally_NoBlackboardStorageExists()
        {
            var gameObject = new GameObject("AIBT.Tests.P7039.DelegateHost");
            try
            {
                var host = gameObject.AddComponent<ProductionTreeHost>();
                Assert.That(host.TryBootstrap(
                    MinimalDelegateProgram(), _ => NodeStatus.Success,
                    new NativeTraceChannelCapacityV1(recordCapacity: 8, payloadCapacity: 0, maximumPayloadBytes: 0, emissionCapacity: 32),
                    out var bootstrapFailure), Is.True, bootstrapFailure.Code.ToString());

                var expectedType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
                Assert.That(host.TryResolveExternalTreeWrite<float>(
                    "anything", expectedType, out _, out var failure), Is.False,
                    "A delegate-based host has no blackboard storage to write into and must refuse structurally.");
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
            }
            finally
            {
                InvokeOnDestroy(gameObject);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ExternalWrite_RepeatedAfterWarmup_AllocatesNoManagedMemory()
        {
            using (var scenario = Scenario.Create())
            {
                var expectedType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
                Assert.That(scenario.Host.TryResolveExternalTreeWrite<float>(
                    "live-target", expectedType, out var handle, out var resolveFailure),
                    Is.True, resolveFailure.Code.ToString());

                // Warm-up call, outside the measured allocation window.
                Assert.That(scenario.Host.TryWriteExternalTreeValue(handle, 1.0f, out _, out _), Is.True);

                var value = 2.0f;
                Assert.That(() =>
                {
                    scenario.Host.TryWriteExternalTreeValue(handle, value, out _, out _);
                    value += 1.0f;
                },
                GcAllocIs.Not.AllocatingGCMemory(),
                "A warmed external-write handle must not allocate managed memory per call.");
            }
        }

        private static CompiledProgram MinimalDelegateProgram()
        {
            const string sourceId = "Tests/P7039/minimal-delegate.aibt.json";
            const string json = @"{
  ""format"": ""aibt.tree"",
  ""formatVersion"": 1,
  ""treeId"": ""tests.p7039.minimal-delegate"",
  ""name"": ""P7-039 Minimal Delegate Fixture"",
  ""root"": ""root"",
  ""nodes"": { ""root"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 1 } } }
}";
            var parsed = CanonicalTreeJson.Parse(json, sourceId);
            Assert.That(parsed.Success, Is.True, Diagnostics(parsed.Diagnostics));
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var options = new ReferenceCompilerOptions(
                sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 5));
            var result = ReferenceCompiler.Compile(parsed.Document, registry, options);
            Assert.That(result.Success, Is.True, Diagnostics(result.Diagnostics));
            return result.Program;
        }

        private static string Diagnostics(DiagnosticCollection diagnostics)
            => string.Join("\n", System.Linq.Enumerable.Select(diagnostics, value => value.Code + ": " + value.Message));

        private static void InvokeUpdate(ProductionTreeHost host)
            => typeof(ProductionTreeHost).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(host, null);

        private static void InvokeOnDestroy(GameObject gameObject)
        {
            var host = gameObject.GetComponent<ProductionTreeHost>();
            if (host == null) return;
            typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(host, null);
        }

        private static void SetDriving(ProductionTreeHost host, bool value)
            => typeof(ProductionTreeHost).GetField("_driving", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(host, value);

        private static GeneratedCompiledProgramV2 CompileFixture(GeneratedShardMetadataArtifact artifact)
        {
            const string sourceId = "Tests/P7039/external-tree-write.aibt.json";
            var parsed = CanonicalTreeJson.Parse(TreeJson, sourceId);
            Assert.That(parsed.Success, Is.True, Diagnostics(parsed.Diagnostics));
            var options = new ReferenceCompilerOptions(
                sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 5));
            var result = GeneratedCompiledProgramV2Compiler.Compile(
                parsed.Document, artifact.Nodes, artifact.RegisteredTypes, options);
            Assert.That(result.Success, Is.True, Diagnostics(result.Diagnostics));
            return result.Program;
        }

        private static GeneratedShardMetadataArtifact MaterializeShard()
            => GeneratedShardMetadataMaterializer.MaterializeArtifact(
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ShardId,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ShardVersion,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.CanonicalDescriptorJson,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.DescriptorHash,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ManifestRegistryJson,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.NodeRegistryHash);

        private sealed class Scenario : System.IDisposable
        {
            private GameObject _gameObject;
            private GeneratedBurstCatalogV2 _catalog;
            internal ProductionTreeHost Host { get; private set; }

            internal static Scenario Create()
            {
                var artifact = MaterializeShard();
                var compiled = CompileFixture(artifact);
                Assert.That(ExternalTreeWriteFixtureCatalog.TryCreateRuntimeCatalog(
                    Allocator.Persistent, out var catalog, out var catalogFailure), Is.True, catalogFailure.ToString());
                Assert.That(compiled.TryCreateRuntimeDefinitionV2(
                    catalog, artifact.RegisteredTypes, out var definition, out var definitionFailure),
                    Is.True, definitionFailure.ToString());

                var gameObject = new GameObject("AIBT.Tests.P7039.ExternalTreeWriteHost");
                var host = gameObject.AddComponent<ProductionTreeHost>();
                var traceCapacity = new NativeTraceChannelCapacityV1(
                    recordCapacity: 16, payloadCapacity: 0, maximumPayloadBytes: 0, emissionCapacity: 64);
                Assert.That(host.TryBootstrap(definition, catalog, traceCapacity, () => 123_456L, out var bootstrapFailure),
                    Is.True, bootstrapFailure.Code.ToString());

                return new Scenario { _gameObject = gameObject, _catalog = catalog, Host = host };
            }

            public void Dispose()
            {
                InvokeOnDestroy(_gameObject);
                UnityEngine.Object.DestroyImmediate(_gameObject);
                Assert.That(_catalog.TryDispose(out var failure), Is.True, failure.ToString());
            }
        }
    }
}
