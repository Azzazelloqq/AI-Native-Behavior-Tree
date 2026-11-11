using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using AIBT.Editor.Preview;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AIBT.Tests.Consumer
{
    public sealed class ProductionConsumerWorkflowTests
    {
        private static readonly ReferenceCompilerOptions Options = new ReferenceCompilerOptions(
            "trees/consumer.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6));

        internal static GeneratedShardMetadataArtifact Artifact() => GeneratedShardMetadataMaterializer.MaterializeArtifact(
            ConsumerShard.AibtGeneratedMetadata.ShardId, ConsumerShard.AibtGeneratedMetadata.ShardVersion,
            ConsumerShard.AibtGeneratedMetadata.CanonicalDescriptorJson, ConsumerShard.AibtGeneratedMetadata.DescriptorHash,
            ConsumerShard.AibtGeneratedMetadata.ManifestRegistryJson, ConsumerShard.AibtGeneratedMetadata.NodeRegistryHash);

        internal static TreeDocument Tree(SemanticValue radius)
        {
            var node = new NodeDocument(new NodeId("root"), "aibt.tests.consumer.radius", 1,
                children: null, observer: null, displayName: null, description: null,
                parameters: new SemanticObject(new[] { new SemanticProperty("radius", radius) }),
                tags: TagSet.Empty, bindings: new NodeBindingMap(new[] { new System.Collections.Generic.KeyValuePair<string, string>("distance", "distance") }));
            var key = new BlackboardKeyDefinition("distance", "distance", BlackboardTypeReference.BuiltIn(BlackboardValueType.Float32),
                defaultValue: BlackboardDefaultValue.Float32(0f));
            return TreeDocument.CreateVersion2(new TreeId("tree.test.consumer"), "Consumer", node.Id, new[] { node }, null, null,
                blackboard: new[] { key }, tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        [TestCase(4.5)]
        [TestCase(-2.25)]
        [TestCase(0.0)]
        [TestCase(0.1)]
        public void Float32RoundTripsAndPacksTheDeclaredValue(double value)
        {
            var artifact = Artifact();
            var encoded = CanonicalTreeJson.Serialize(Tree(SemanticValue.FromNumber(value)));
            Assert.That(encoded.Success, Is.True);
            var parsed = CanonicalTreeJson.Parse(encoded.Utf8);
            Assert.That(parsed.Success, Is.True);
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, artifact.Nodes, artifact.RegisteredTypes, Options);
            Assert.That(compiled.Success, Is.True, string.Join("; ", compiled.Diagnostics));
            var field = artifact.Nodes.Single().Configuration.Single(item => item.FieldId == "radius");
            Assert.That(BitConverter.ToSingle(compiled.Program.GetConfigBlobCopy(), (int)field.Offset), Is.EqualTo((float)value));
            Assert.That(artifact.Nodes.Single().Manifest.Parameters.Single().Type, Is.EqualTo(NodeParameterType.Float32));
        }

        [Test]
        public void InvalidFloatConfigurationIsRejectedBeforeExecution()
        {
            var artifact = Artifact();
            foreach (var value in new[] { SemanticValue.FromNumber(double.NaN), SemanticValue.FromNumber(double.PositiveInfinity),
                         SemanticValue.FromNumber(double.MaxValue), SemanticValue.FromString("4.5"), SemanticValue.FromBoolean(true) })
            {
                var result = GeneratedCompiledProgramV2Compiler.Compile(Tree(value), artifact.Nodes, artifact.RegisteredTypes, Options);
                Assert.That(result.Success, Is.False, value.Kind.ToString());
            }
            var integral = GeneratedCompiledProgramV2Compiler.Compile(Tree(SemanticValue.FromInt64(4)), artifact.Nodes, artifact.RegisteredTypes, Options);
            Assert.That(integral.Success, Is.True, string.Join("; ", integral.Diagnostics));
        }

        [Test]
        public void ReferenceCompilerUsesTheSameFiniteFloat32Encoding()
        {
            var manifest = new NodeManifest("aibt.tests.reference-float", 1, "Reference float", "Tests",
                NodeBehaviorKind.Condition, "Test reference packing.", "Not gameplay.", NodeExecutionDomain.Burst, true,
                new[] { new NodeParameterContract("radius", NodeParameterType.Float32, true) }, new NodeChildPolicy(0, 0, true),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), new[] { NodeStatus.Success, NodeStatus.Failure },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(4, 4, new[] { new NodeConfigurationField("radius", 0, 4, 4) }),
                NodeCancellationMode.NotApplicable, NodeCostHint.Trivial, new[] { new NodeManifestExample("float", "{\"radius\":-1.25}", "Success") });
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().AddProjectExtension(manifest, new ScalarBehavior()).Build().Registry;
            var node = new NodeDocument(new NodeId("root"), manifest.TypeId, 1,
                parameters: new SemanticObject(new[] { new SemanticProperty("radius", SemanticValue.FromNumber(-1.25)) }), tags: TagSet.Empty);
            var tree = TreeDocument.CreateVersion2(new TreeId("tree.test.reference-float"), "Float", node.Id, new[] { node }, null, null,
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
            var compiled = ReferenceCompiler.Compile(tree, registry, Options);
            Assert.That(compiled.Success, Is.True, string.Join("; ", compiled.Diagnostics));
            Assert.That(compiled.Program.ConfigBlob, Is.EqualTo(new byte[] { 0, 0, 160, 191 }));
        }

        private sealed class ScalarBehavior : IReferenceLeafBehavior
        {
            public void Enter(ref ReferenceLeafContext context) { }
            public NodeStatus Tick(ref ReferenceLeafContext context) => NodeStatus.Success;
            public void Abort(ref ReferenceLeafContext context, NodeAbortReason reason) { }
            public void Exit(ref ReferenceLeafContext context, NodeExitReason reason) { }
        }

        [Test]
        public void PreparedAssetSurvivesReloadAndRunsSharedHostsWithInputsAndEffects()
        {
            var artifact = Artifact();
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(Tree(SemanticValue.FromNumber(4.5)), artifact.Nodes, artifact.RegisteredTypes, Options);
            Assert.That(compiled.Success, Is.True, string.Join("; ", compiled.Diagnostics));
            var asset = ProductionTreeAssetBaker.Create(Tree(SemanticValue.FromNumber(4.5)), Options.SourceId, typeof(ConsumerCatalog), out var preparationDiagnostics);
            Assert.That(asset, Is.Not.Null, string.Join("; ", preparationDiagnostics));
            const string directory = "Assets/AibtConsumerWorkflowTest";
            AssetDatabase.CreateFolder("Assets", "AibtConsumerWorkflowTest");
            var first = new GameObject("consumer first");
            var second = new GameObject("consumer second");
            ProductionTreeProgram program = null;
            ProductionTreeHost host = null;
            ProductionTreeHost other = null;
            try
            {
                AssetDatabase.CreateAsset(asset, directory + "/tree.asset");
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(directory + "/tree.asset", ImportAssetOptions.ForceUpdate);
                var reloaded = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(directory + "/tree.asset");
                Assert.That(reloaded.TryCreateProgram(ConsumerCatalog.TryCreateRuntimeCatalog, out program, out var failure), Is.True, failure.ToString());
                host = first.AddComponent<ProductionTreeHost>();
                other = second.AddComponent<ProductionTreeHost>();
                Assert.That(program.TryBootstrap(host, out _, effectCapacity: 4), Is.True);
                Assert.That(program.TryBootstrap(other, out _, effectCapacity: 4), Is.True);
                Assert.That(program.TryDispose(out _), Is.False, "A live host must retain its catalog.");
                var type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
                Assert.That(host.TryResolveExternalTreeWrite<float>("distance", type, out var handle, out _), Is.True);
                Assert.That(host.TryWriteExternalTreeValue(handle, 10f, out _, out _), Is.True);
                InvokeUpdate(host);
                Assert.That(host.LastRootResult, Is.Null);
                var receiver = new Receiver();
                Assert.That(host.TryDrainEffects(receiver, out _), Is.True);
                Assert.That(receiver.Value, Is.EqualTo(4.5f));
                Assert.That(host.TryWriteExternalTreeValue(handle, 2f, out _, out _), Is.True);
                InvokeUpdate(host);
                InvokeUpdate(other);
                Assert.That(host.LastRootResult, Is.EqualTo(NodeStatus.Success));
                Assert.That(other.LastRootResult, Is.EqualTo(NodeStatus.Success), "Instance inputs must remain isolated.");
            }
            finally
            {
                host?.Stop();
                other?.Stop();
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
                program?.Dispose();
                AssetDatabase.DeleteAsset(directory);
            }
        }

        [Test]
        public void CorruptPreparedPayloadDoesNotCreateACatalog()
        {
            var artifact = Artifact();
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(Tree(SemanticValue.FromNumber(4.5)), artifact.Nodes, artifact.RegisteredTypes, Options);
            var asset = ProductionTreeAsset.Create(compiled.Program.CreateNativeBlackboardBindingV2(artifact.RegisteredTypes), ConsumerCatalog.Fingerprint);
            try
            {
                var field = typeof(ProductionTreeAsset).GetField("_payload", BindingFlags.Instance | BindingFlags.NonPublic);
                var bytes = (byte[])field.GetValue(asset);
                bytes[bytes.Length - 1] ^= 1;
                Assert.That(asset.TryCreateProgram((Unity.Collections.Allocator allocator, out GeneratedBurstCatalogV2 catalog, out BurstContextResult failure) =>
                { catalog = null; failure = default; Assert.Fail("Invalid data must be rejected before creating native owners."); return false; }, out _, out _), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        private sealed class Receiver : IProductionTreeEffectReceiver
        {
            internal float Value;
            public void Receive(in ProductionTreeEffect effect) => effect.TryGetFloat32(out Value);
        }

        [TestCase("version")]
        [TestCase("truncated")]
        [TestCase("count")]
        public void InvalidArchiveIsRejectedEvenWithAValidEnvelopeChecksum(string damage)
        {
            var artifact = Artifact();
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(Tree(SemanticValue.FromNumber(4.5)), artifact.Nodes, artifact.RegisteredTypes, Options);
            var asset = ProductionTreeAsset.Create(compiled.Program.CreateNativeBlackboardBindingV2(artifact.RegisteredTypes), ConsumerCatalog.Fingerprint);
            try
            {
                var payloadField = typeof(ProductionTreeAsset).GetField("_payload", BindingFlags.Instance | BindingFlags.NonPublic);
                var bytes = (byte[])payloadField.GetValue(asset);
                if (damage == "version") bytes[4] = 2;
                else if (damage == "truncated") Array.Resize(ref bytes, bytes.Length - 1);
                else for (var index = 8; index < 12; index++) bytes[index] = 255;
                payloadField.SetValue(asset, bytes);
                typeof(ProductionTreeAsset).GetField("_payloadHash", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(asset, StableHash.Sha256Hex(bytes));
                Assert.That(asset.TryCreateProgram((Unity.Collections.Allocator allocator, out GeneratedBurstCatalogV2 catalog, out BurstContextResult failure) =>
                { catalog = null; failure = default; Assert.Fail("Malformed archives must be rejected before native allocation."); return false; }, out _, out _), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void AChangedCatalogIsRejectedAndItsNativeStorageIsReleased()
        {
            var artifact = Artifact();
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(Tree(SemanticValue.FromNumber(4.5)), artifact.Nodes, artifact.RegisteredTypes, Options);
            var asset = ProductionTreeAsset.Create(compiled.Program.CreateNativeBlackboardBindingV2(artifact.RegisteredTypes), new BurstCatalogFingerprint(default));
            GeneratedBurstCatalogV2 acquired = null;
            try
            {
                bool Factory(Unity.Collections.Allocator allocator, out GeneratedBurstCatalogV2 catalog, out BurstContextResult failure)
                {
                    var success = ConsumerCatalog.TryCreateRuntimeCatalog(allocator, out catalog, out failure);
                    acquired = catalog;
                    return success;
                }
                Assert.That(asset.TryCreateProgram(Factory, out var program, out var failure), Is.False);
                Assert.That(failure, Is.EqualTo(BurstContextResult.TypeMismatch));
                Assert.That(program, Is.Null);
                Assert.That(acquired.IsCreated, Is.False);
            }
            finally { acquired?.Dispose(); UnityEngine.Object.DestroyImmediate(asset); }
        }

        private static void InvokeUpdate(ProductionTreeHost host)
            => typeof(ProductionTreeHost).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
    }
}
