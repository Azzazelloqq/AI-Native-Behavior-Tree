using System.Reflection;
using System.Text.RegularExpressions;
using AIBT.Authoring;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-004 (ADR-P11-004): reading a Tree-scope value of a running native host through the public
    /// <see cref="ProductionTreeHost"/> read pair and the internal type-erased editor read. The tree
    /// reuses the P7-039 fixture nodes: "live-target" (Float32, read by a node, written only from
    /// outside), "owned-target" (Int32, written by a node), and two keys no node touches:
    /// "spot" (Float3) and "flag" (Bool). An Agent-scope key cannot be part of the fixture: the
    /// generated v2 compiler refuses one today (AIBT3017 "Tree-scope blackboard memory ranges
    /// overlap"), so the BlackboardUnsupportedScope branch is not reachable through a generated host.
    /// </summary>
    public sealed class ExternalTreeBlackboardReadTests
    {
        internal const string TreeJson = @"{
  ""format"": ""aibt.tree"",
  ""formatVersion"": 2,
  ""treeId"": ""tests.p11004.external-tree-read"",
  ""name"": ""P11-004 External Tree Read Fixture"",
  ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""owned-target"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": -3 },
    ""spot"": { ""type"": ""Float3"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": { ""x"": 1.0, ""y"": 2.0, ""z"": 3.0 } },
    ""flag"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": true }
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

        private static readonly NativeBlackboardTypeIdV2 Float32Type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
        private static readonly NativeBlackboardTypeIdV2 Int32Type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32);
        private static readonly NativeBlackboardTypeIdV2 Float3Type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float3);
        private static readonly NativeBlackboardTypeIdV2 BoolType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Bool);

        [Test]
        public void BeforeTheFirstUpdate_EveryKeyReadsItsCompiledDefault()
        {
            using (var scenario = Scenario.Create())
            {
                var host = scenario.Host;
                Assert.That(host.CanReadTreeValues, Is.True);
                Assert.That(Read<int>(host, "owned-target", Int32Type), Is.EqualTo(-3));
                Assert.That(Read<float>(host, "live-target", Float32Type), Is.EqualTo(0f));
                Assert.That(Read<bool>(host, "flag", BoolType), Is.True);
                var spot = Read<Float3Value>(host, "spot", Float3Type);
                Assert.That(new[] { spot.X, spot.Y, spot.Z }, Is.EqualTo(new[] { 1f, 2f, 3f }));
            }
        }

        [Test]
        public void NodeWrittenAndExternallyWrittenKeys_ReadTheirCurrentValues_AlsoAfterTheRootFinished()
        {
            using (var scenario = Scenario.Create())
            {
                var host = scenario.Host;
                Assert.That(host.TryResolveExternalTreeWrite<float>("live-target", Float32Type, out var write, out var writeFailure),
                    Is.True, writeFailure.Code.ToString());
                Assert.That(host.TryWriteExternalTreeValue(write, 2.5f, out _, out writeFailure), Is.True, writeFailure.Code.ToString());
                Assert.That(Read<float>(host, "live-target", Float32Type), Is.EqualTo(2.5f), "An external write is visible at once.");

                InvokeUpdate(host);

                Assert.That(host.LastRootResult, Is.EqualTo(NodeStatus.Success));
                Assert.That(host.CanReadTreeValues, Is.True, "A finished host stays readable until Stop.");
                Assert.That(host.CanAcceptExternalTreeWrites, Is.False, "...while writes are closed.");
                Assert.That(Read<int>(host, "owned-target", Int32Type), Is.EqualTo(7), "The writer node's value.");
                Assert.That(Read<float>(host, "live-target", Float32Type), Is.EqualTo(2.5f));
            }
        }

        [Test]
        public void ResolveRefusals_UseTheExistingCodes()
        {
            using (var scenario = Scenario.Create())
            {
                var host = scenario.Host;
                AssertResolveFails<float>(host, "no-such-key", Float32Type, NativeRuntimeDiagnosticCodeV1.BlackboardUndeclaredAccess);
                AssertResolveFails<int>(host, "live-target", Int32Type, NativeRuntimeDiagnosticCodeV1.BlackboardTypeMismatch);
                AssertResolveFails<long>(host, "owned-target", Int32Type, NativeRuntimeDiagnosticCodeV1.BlackboardTypeMismatch);
                Assert.Throws<System.ArgumentException>(() => host.TryResolveExternalTreeRead<int>(string.Empty, Int32Type, out _, out _));
            }
        }

        /// <summary>
        /// P11-008 (review F8): <typeparamref name="T"/> must be the managed type of
        /// <c>expectedType</c>, not just a type of the same size and alignment. Each case names the
        /// slot's own type correctly, so only the type argument is wrong.
        /// </summary>
        [Test]
        public void ResolveRefuses_ATypeArgumentThatIsNotTheManagedTypeOfExpectedType()
        {
            using (var scenario = Scenario.Create())
            {
                var host = scenario.Host;
                AssertResolveFails<int>(host, "live-target", Float32Type, NativeRuntimeDiagnosticCodeV1.BlackboardTypeMismatch);
                AssertResolveFails<uint>(host, "owned-target", Int32Type, NativeRuntimeDiagnosticCodeV1.BlackboardTypeMismatch);
                AssertResolveFails<float>(host, "owned-target", Int32Type, NativeRuntimeDiagnosticCodeV1.BlackboardTypeMismatch);
                AssertResolveFails<byte>(host, "flag", BoolType, NativeRuntimeDiagnosticCodeV1.BlackboardTypeMismatch);
                AssertResolveFails<ThreeFloats>(host, "spot", Float3Type, NativeRuntimeDiagnosticCodeV1.BlackboardTypeMismatch);

                Assert.That(Read<float>(host, "live-target", Float32Type), Is.EqualTo(0f), "The matching type still resolves.");
                Assert.That(Read<Float3Value>(host, "spot", Float3Type).Z, Is.EqualTo(3f));
            }
        }

        private struct ThreeFloats
        {
            public float A;
            public float B;
            public float C;
        }

        [Test]
        public void ResolveIsLegalWhileExecuting_ButTheReadIsRefusedAndTouchesNothing()
        {
            using (var scenario = Scenario.Create())
            {
                var host = scenario.Host;
                SetDriving(host, true);
                try
                {
                    Assert.That(host.CanReadTreeValues, Is.False);
                    Assert.That(host.TryResolveExternalTreeRead<int>("owned-target", Int32Type, out var handle, out var resolveFailure),
                        Is.True, resolveFailure.Code.ToString());
                    var value = 99;
                    Assert.That(host.TryReadExternalTreeValue(handle, out value, out var failure), Is.False);
                    Assert.That(value, Is.EqualTo(0));
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                    Assert.That(host.TryReadTreeValue("owned-target", out _, out failure), Is.False);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                }
                finally
                {
                    SetDriving(host, false);
                }
                Assert.That(Read<int>(host, "owned-target", Int32Type), Is.EqualTo(-3));
            }
        }

        [Test]
        public void HandleOfAnotherHost_AndTheDefaultHandle_AreRefused()
        {
            using (var a = Scenario.Create())
            using (var b = Scenario.Create())
            {
                Assert.That(a.Host.TryResolveExternalTreeRead<int>("owned-target", Int32Type, out var fromA, out _), Is.True);
                Assert.That(b.Host.TryReadExternalTreeValue(fromA, out _, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                Assert.That(a.Host.TryReadExternalTreeValue(default(ProductionTreeHost.ExternalTreeReadHandle<int>), out _, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
            }
        }

        [Test]
        public void AfterStop_ReadsAreRefused()
        {
            using (var scenario = Scenario.Create())
            {
                var host = scenario.Host;
                Assert.That(host.TryResolveExternalTreeRead<int>("owned-target", Int32Type, out var handle, out _), Is.True);
                host.Stop();
                Assert.That(host.CanReadTreeValues, Is.False);
                Assert.That(host.TryReadExternalTreeValue(handle, out _, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                Assert.That(host.TryResolveExternalTreeRead<int>("owned-target", Int32Type, out _, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
            }
        }

        [Test]
        public void AfterAFault_TheStorageStaysReadable()
        {
            using (var scenario = Scenario.Create())
            {
                var host = scenario.Host;
                Assert.That(host.TryResolveExternalTreeRead<int>("owned-target", Int32Type, out var handle, out _), Is.True);
                LogAssert.Expect(LogType.Error, new Regex("AIBT ProductionTreeHost: NativeCapacityPlanInvalid"));
                host.FailExternalScheduling(new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
                Assert.That(host.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
                Assert.That(host.CanReadTreeValues, Is.True);
                Assert.That(host.TryReadExternalTreeValue(handle, out var value, out var failure), Is.True, failure.Code.ToString());
                Assert.That(value, Is.EqualTo(-3));
            }
        }

        [Test]
        public void DelegateBasedHost_IsRefusedStructurally()
        {
            var gameObject = new GameObject("AIBT.Tests.P11004.DelegateHost");
            try
            {
                var host = gameObject.AddComponent<ProductionTreeHost>();
                Assert.That(host.TryBootstrap(
                    MinimalDelegateProgram(), _ => NodeStatus.Success,
                    new NativeTraceChannelCapacityV1(recordCapacity: 8, payloadCapacity: 0, maximumPayloadBytes: 0, emissionCapacity: 32),
                    out var bootstrapFailure), Is.True, bootstrapFailure.Code.ToString());
                Assert.That(host.CanReadTreeValues, Is.False);
                AssertResolveFails<float>(host, "anything", Float32Type, NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid);
                Assert.That(host.TryReadTreeValue("anything", out _, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
            }
            finally
            {
                InvokeOnDestroy(gameObject);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void HandleRead_RepeatedAfterWarmup_AllocatesNoManagedMemory()
        {
            using (var scenario = Scenario.Create())
            {
                var host = scenario.Host;
                Assert.That(host.TryResolveExternalTreeRead<Float3Value>("spot", Float3Type, out var handle, out var resolveFailure),
                    Is.True, resolveFailure.Code.ToString());
                Assert.That(host.TryReadExternalTreeValue(handle, out _, out _), Is.True);
                var sum = 0f;
                Assert.That(() =>
                {
                    for (var index = 0; index < 1000; index++)
                    {
                        host.TryReadExternalTreeValue(handle, out var spot, out _);
                        sum += spot.Z;
                    }
                },
                GcAllocIs.Not.AllocatingGCMemory(), "A warmed read handle must not allocate managed memory.");
                Assert.That(sum, Is.EqualTo(3000f));
            }
        }

        [Test]
        public void TypeErasedRead_DecodesBuiltInValues_AndReportsFailures()
        {
            using (var scenario = Scenario.Create())
            {
                var host = scenario.Host;
                Assert.That(host.TryReadTreeValue("owned-target", out var value, out var failure), Is.True, failure.Code.ToString());
                Assert.That(value.TryGetInt32(out var number) && number == -3, Is.True);
                Assert.That(host.TryReadTreeValue("flag", out value, out failure), Is.True, failure.Code.ToString());
                Assert.That(value.TryGetBool(out var flag) && flag, Is.True);
                Assert.That(host.TryReadTreeValue("live-target", out value, out failure), Is.True, failure.Code.ToString());
                Assert.That(value.TryGetFloat32(out var target) && target == 0f, Is.True);
                Assert.That(host.TryReadTreeValue("spot", out value, out failure), Is.True, failure.Code.ToString());
                Assert.That(value.TryGetFloat3(out var spot) && spot.Y == 2f, Is.True);

                Assert.That(host.TryReadTreeValue("no-such-key", out _, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardUndeclaredAccess));
            }
        }

        // ------------------------------------------------------------------ helpers

        private static string Describe(DiagnosticCollection diagnostics)
            => string.Join("\n", System.Linq.Enumerable.Select(diagnostics, value => value.Code + ": " + value.Message));

        private static T Read<T>(ProductionTreeHost host, string key, NativeBlackboardTypeIdV2 type) where T : unmanaged
        {
            Assert.That(host.TryResolveExternalTreeRead<T>(key, type, out var handle, out var failure), Is.True, key + ": " + failure.Code);
            Assert.That(host.TryReadExternalTreeValue(handle, out var value, out failure), Is.True, key + ": " + failure.Code);
            return value;
        }

        private static void AssertResolveFails<T>(
            ProductionTreeHost host, string key, NativeBlackboardTypeIdV2 type, NativeRuntimeDiagnosticCodeV1 expected) where T : unmanaged
        {
            Assert.That(host.TryResolveExternalTreeRead<T>(key, type, out var handle, out var failure), Is.False, key);
            Assert.That(failure.Code, Is.EqualTo(expected), key);
            Assert.That(host.TryReadExternalTreeValue(handle, out _, out _), Is.False, key + ": a failed resolve yields no usable handle");
        }

        private static CompiledProgram MinimalDelegateProgram()
        {
            const string sourceId = "Tests/P11004/minimal-delegate.aibt.json";
            const string json = @"{
  ""format"": ""aibt.tree"",
  ""formatVersion"": 1,
  ""treeId"": ""tests.p11004.minimal-delegate"",
  ""name"": ""P11-004 Minimal Delegate Fixture"",
  ""root"": ""root"",
  ""nodes"": { ""root"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 1 } } }
}";
            var parsed = CanonicalTreeJson.Parse(json, sourceId);
            Assert.That(parsed.Success, Is.True, Describe(parsed.Diagnostics));
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var result = ReferenceCompiler.Compile(parsed.Document, registry,
                new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 5)));
            Assert.That(result.Success, Is.True, Describe(result.Diagnostics));
            return result.Program;
        }

        private static void InvokeUpdate(ProductionTreeHost host)
            => typeof(ProductionTreeHost).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(host, null);

        private static void InvokeOnDestroy(GameObject gameObject)
        {
            var host = gameObject.GetComponent<ProductionTreeHost>();
            if (host == null) return;
            typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(host, null);
        }

        private static void SetDriving(ProductionTreeHost host, bool value)
            => typeof(ProductionTreeHost).GetField("_driving", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(host, value);

        internal static GeneratedShardMetadataArtifact MaterializeShard()
            => GeneratedShardMetadataMaterializer.MaterializeArtifact(
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ShardId,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ShardVersion,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.CanonicalDescriptorJson,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.DescriptorHash,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ManifestRegistryJson,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.NodeRegistryHash);

        internal static GeneratedCompiledProgramV2 CompileFixture(GeneratedShardMetadataArtifact artifact)
        {
            const string sourceId = "Tests/P11004/external-tree-read.aibt.json";
            var parsed = CanonicalTreeJson.Parse(TreeJson, sourceId);
            Assert.That(parsed.Success, Is.True, Describe(parsed.Diagnostics));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, artifact.Nodes, artifact.RegisteredTypes,
                new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 5)));
            Assert.That(result.Success, Is.True, Describe(result.Diagnostics));
            return result.Program;
        }

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
                var gameObject = new GameObject("AIBT.Tests.P11004.ExternalTreeReadHost");
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
                Object.DestroyImmediate(_gameObject);
                Assert.That(_catalog.TryDispose(out var failure), Is.True, failure.ToString());
            }
        }
    }
}
