using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AIBT.Burst;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.CodeGen.Generation
{
    [AibtCatalogShard("aibt.tests.generation", 1u)]
    public partial struct GenerationShard { }

    public partial struct GenerationConfig
    {
        [AibtConfigField("target", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("agent-target", BurstBlackboardAccess.Read, BlackboardScope.Agent, "Int32", 1u)]
        public BlackboardReadHandle<int> Target;

        [AibtConfigField("enabled", "Bool", 1u)]
        public bool Enabled;
    }

    public partial struct GenerationMemory
    {
        [AibtMemoryField("count", "UInt32", 1u)]
        public uint Count;

        [AibtMemoryField("payload", "aibt.tests.registered-value", 1u)]
        public GenerationRegisteredValue Payload;
    }

    [AibtBurstValue("aibt.tests.registered-value", 1u, "aibt.tests.registered-value.schema")]
    public partial struct GenerationRegisteredValue
    {
        [AibtValueField("asset", "AssetId", 1u)] public AssetId Asset;
        [AibtValueField("count", "Int32", 1u)] public int Count;
    }

    [AibtNodeDocumentation("Generated fixture", "Tests", "Verify generated metadata", "Not production", "generation-success")]
    [AibtObserverCondition]
    [AibtBurstNode(
        "aibt.tests.generated-node", 1u, BurstNodeKind.Condition,
        typeof(GenerationConfig), typeof(GenerationMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct GenerationNode
    {
        public static void Enter(in GenerationConfig config, ref GenerationMemory memory, ref BurstEnterContext context) { }
        public static NodeStatus Tick(in GenerationConfig config, ref GenerationMemory memory, ref BurstTickContext context)
        {
            var result = GenerationShard.BurstAccess.TryRead(ref context, config.Target, out var value);
            if (result != BurstContextResult.Success)
            {
                return NodeStatus.Failure;
            }

            memory.Count = (uint)value + (config.Enabled ? 1u : 0u);
            return NodeStatus.Success;
        }
        public static void Abort(in GenerationConfig config, ref GenerationMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in GenerationConfig config, ref GenerationMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
        public static ConditionResult Evaluate(in GenerationConfig config, ref BurstObserverContext context) => ConditionResult.Success;
    }

    public sealed class GeneratedArtifactContractTests
    {
        [Test]
        public void ShardMetadata_ContainsCanonicalManifestRegistryAndTypedLayouts()
        {
            var metadata = GenerationShard.AibtGeneratedMetadata.CanonicalDescriptorJson;
            var registry = GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson;

            Assert.That(GenerationShard.IsUsable, Is.True);
            Assert.That(GenerationShard.AbiVersion, Is.EqualTo(2u));
            Assert.That(metadata, Does.StartWith("{\"abiVersion\":2,"));
            Assert.That(GenerationShard.AibtGeneratedMetadata.ShardId, Is.EqualTo("aibt.tests.generation"));
            Assert.That(GenerationShard.AibtGeneratedMetadata.NodeRegistryHash, Is.EqualTo(StableHash.Sha256Hex(registry)));
            var expectedRegistry = ExpectedRegistry();
            var writer = typeof(NodeRegistryBuilder).Assembly.GetType("AIBT.Authoring.NodeManifestCanonicalJson", true);
            var serialize = writer.GetMethod("SerializeRegistry", BindingFlags.Static | BindingFlags.NonPublic);
            var expectedJson = (string)serialize.Invoke(null, new object[] { expectedRegistry.Registry.ToArray() });
            Assert.That(registry, Is.EqualTo(expectedJson));
            Assert.That(GenerationShard.AibtGeneratedMetadata.NodeRegistryHash, Is.EqualTo(expectedRegistry.Registry.Hash));
            Assert.That(GenerationShard.AibtGeneratedMetadata.DescriptorHash, Is.EqualTo(StableHash.Sha256Hex(metadata)));
            Assert.That(registry, Does.StartWith("{\n  \"format\": \"aibt-node-registry\",\n  \"formatVersion\": 1,"));
            Assert.That(registry, Does.Contain("\"reads\": []"));
            Assert.That(registry, Does.Contain("\"writes\": []"));
            Assert.That(registry, Does.EndWith("\n"));
            Assert.That(metadata.IndexOf("\"id\":\"enabled\"", StringComparison.Ordinal),
                Is.LessThan(metadata.IndexOf("\"id\":\"target\"", StringComparison.Ordinal)));
            Assert.That(metadata, Does.Contain("\"id\":\"agent-target\""));
            Assert.That(metadata, Does.Contain("\"scope\":2"));
            Assert.That(metadata, Does.Contain("\"ordinal\":0"));
        }

        [Test]
        public void PublicModel_PreservesAllExactP1ConstructorsAndKeepsGeneratedHandleOutOfP1Projection()
        {
            var legacy = typeof(NodeConfigurationField).GetConstructor(new[]
            {
                typeof(string), typeof(uint), typeof(uint), typeof(byte),
            });
            var generated = typeof(NodeConfigurationField).GetConstructor(new[]
            {
                typeof(string), typeof(uint), typeof(uint), typeof(byte), typeof(bool),
            });

            Assert.That(legacy, Is.Not.Null, "The exact P1 four-argument constructor is a binary API contract.");
            Assert.That(generated, Is.Not.Null);
            Assert.That(((NodeConfigurationField)legacy.Invoke(new object[] { "enabled", 0u, 1u, (byte)1 })).IsGeneratedHandle, Is.False);

            var registry = ExpectedRegistry();
            var manifest = registry.Registry.Single().Manifest;
            Assert.That(manifest.Parameters.Select(parameter => parameter.Name), Is.EqualTo(new[] { "enabled" }));
            Assert.That(manifest.Configuration.Fields.Single(field => field.IsGeneratedHandle).ParameterName, Is.EqualTo("target"));
            Assert.That(GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson, Does.Not.Contain("agent-target"));
            Assert.That(GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson, Does.Not.Contain("isGeneratedHandle"));

            Assert.That(typeof(NodeDocument).GetConstructor(new[]
            {
                typeof(NodeId), typeof(string), typeof(int), typeof(IEnumerable<NodeId>), typeof(SemanticObject),
                typeof(NodeObserver), typeof(string), typeof(string), typeof(TagSet),
            }), Is.Not.Null);
            Assert.That(typeof(TreeDocument).GetConstructor(new[]
            {
                typeof(string), typeof(int), typeof(TreeId), typeof(string), typeof(NodeId), typeof(IEnumerable<NodeDocument>),
                typeof(IEnumerable<BlackboardKeyDefinition>), typeof(string), typeof(TagSet), typeof(SemanticObject), typeof(Revision),
            }), Is.Not.Null);
            Assert.That(typeof(BlackboardKeyDefinition).GetConstructor(new[]
            {
                typeof(string), typeof(string), typeof(BlackboardTypeReference), typeof(BlackboardScope),
                typeof(BlackboardDefaultValue), typeof(string),
            }), Is.Not.Null);
        }

        [Test]
        public void EmittedMetadata_MaterializesConsumerDescriptorAndCompilesCanonicalV2Artifact()
        {
            var descriptors = GeneratedShardMetadataMaterializer.Materialize(
                GenerationShard.AibtGeneratedMetadata.CanonicalDescriptorJson,
                GenerationShard.AibtGeneratedMetadata.DescriptorHash,
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash);
            var descriptor = descriptors.Single();
            Assert.That(descriptor.Bindings.Single().BindingId, Is.EqualTo("agent-target"));
            Assert.That(descriptor.Configuration.Single(field => field.BindingId != null).BindingId, Is.EqualTo("agent-target"));

            var path = FixturePath("generated-tree-v2.aibt.json");
            var json = File.ReadAllText(path);
            var parsed = CanonicalTreeJson.Parse(json, "Tests/Fixtures/P2/CodeGen/generated-tree-v2.aibt.json");
            Assert.That(parsed.Success, Is.True, Diagnostics(parsed.Diagnostics));
            var canonical = CanonicalTreeJson.Serialize(parsed.Document);
            Assert.That(canonical.Success, Is.True, Diagnostics(canonical.Diagnostics));
            var text = Encoding.UTF8.GetString(canonical.Utf8);
            Assert.That(text.IndexOf("\"blackboardContracts\"", StringComparison.Ordinal), Is.LessThan(text.IndexOf("\"blackboard\"", StringComparison.Ordinal)));
            Assert.That(text.IndexOf("\"parameters\"", StringComparison.Ordinal), Is.LessThan(text.IndexOf("\"bindings\"", StringComparison.Ordinal)));
            Assert.That(CanonicalTreeJson.Parse(canonical.Utf8).Document.FormatVersion, Is.EqualTo(2));

            var combined = GeneratedNodeRegistry.Build(descriptors, includeBuiltIns: true);
            Assert.That(combined.Success, Is.True, Diagnostics(combined.Diagnostics));
            var validation = TreeValidator.Validate(
                parsed.Document,
                combined.Registry,
                new ValidationOptions(
                    "Tests/Fixtures/P2/CodeGen/generated-tree-v2.aibt.json",
                    supportsAgentScope: true,
                    supportsSharedScope: true));
            Assert.That(validation, Is.Empty, Diagnostics(validation));

            var options = new ReferenceCompilerOptions(
                "Tests/Fixtures/P2/CodeGen/generated-tree-v2.aibt.json",
                ReferenceCompilationPolicy.Phase1,
                new CompiledCompilerVersion(1, 0, 0, 5));
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, descriptors, options);
            Assert.That(compiled.Success, Is.True, Diagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.SemanticProgram.Header.NodeRegistryHash.HexadecimalValue, Is.EqualTo(combined.Registry.Hash));
            Assert.That(compiled.Program.SemanticProgram.Header.CapabilityFlags & 0x7fu,
                Is.EqualTo((uint)combined.Registry.Capabilities), "P1 registry capability bits 0-6 must remain exact.");
            Assert.That(compiled.Program.SemanticProgram.Header.CapabilityFlags & GeneratedCompiledProgramV2.AgentScopeCapability, Is.Not.Zero);
            Assert.That(compiled.Program.SemanticProgram.Header.CapabilityFlags & GeneratedCompiledProgramV2.SharedScopeCapability, Is.Zero);
            Assert.That(compiled.Program.Accesses.Single().AccessOrdinal, Is.Zero);
            Assert.That(compiled.Program.SemanticProgram.Observers, Has.Count.EqualTo(1));
            Assert.That(compiled.Program.WatchedSlots, Has.Count.EqualTo(1));
            Assert.That(compiled.Program.SemanticProgram.DebugMap, Has.Count.EqualTo(2));
            Assert.That(compiled.Program.Scopes.Descriptors.Single().GetRawLayoutCopy(), Is.Not.Empty);
            Assert.That(compiled.Program.GetBytesCopy(), Is.EqualTo(IndependentV2Stream.Encode(compiled.Program)),
                "The production serializer must match the independent accepted P2-003 field stream byte-for-byte.");
            Assert.That(compiled.Program.SemanticProgram.Header.CompiledContentHash, Is.Not.EqualTo(compiled.Program.ContentHash),
                "The inner v1 semantic-program hash and outer exact v2 stream hash are separate boundaries.");
            var nativeHeader = compiled.Program.CreateNativeHeaderProjection();
            var nativeHash = new StringBuilder(64);
            for (var hashIndex = 0; hashIndex < 32; hashIndex++) nativeHash.Append(nativeHeader.CompiledContentHash.GetByte(hashIndex).ToString("x2"));
            Assert.That(nativeHash.ToString(), Is.EqualTo(compiled.Program.ContentHash.HexadecimalValue));
            var pinPath = FixturePath("generated-compiled-v2.json");
            var pin = File.ReadAllText(pinPath);
            Assert.That(compiled.Program.ContentHash.HexadecimalValue, Is.EqualTo(ExtractJsonString(pin, "contentHash")));
            Assert.That(compiled.Program.GetBytesCopy(), Is.EqualTo(ParseHex(ExtractJsonString(pin, "bytesHex"))));

            var shuffled = new TreeDocument(
                parsed.Document.Format,
                parsed.Document.FormatVersion,
                parsed.Document.TreeId,
                parsed.Document.Name,
                parsed.Document.Root,
                parsed.Document.Nodes.Reverse(),
                parsed.Document.Blackboard,
                parsed.Document.Description,
                parsed.Document.Tags,
                parsed.Document.Metadata,
                parsed.Document.Revision,
                parsed.Document.AgentContract,
                parsed.Document.SharedContract);
            var compiledShuffled = GeneratedCompiledProgramV2Compiler.Compile(shuffled, descriptors, options);
            Assert.That(compiledShuffled.Success, Is.True, Diagnostics(compiledShuffled.Diagnostics));
            Assert.That(compiledShuffled.Program.GetBytesCopy(), Is.EqualTo(compiled.Program.GetBytesCopy()));
            Assert.That(compiledShuffled.Program.ContentHash, Is.EqualTo(compiled.Program.ContentHash));

            var changedDefaultJson = ReplaceOnce(json, "\"default\": 0", "\"default\": 1");
            var changedDefault = CanonicalTreeJson.Parse(changedDefaultJson);
            var changedCompiled = GeneratedCompiledProgramV2Compiler.Compile(changedDefault.Document, descriptors, options);
            Assert.That(changedCompiled.Success, Is.True, Diagnostics(changedCompiled.Diagnostics));
            Assert.That(changedCompiled.Program.ContentHash, Is.Not.EqualTo(compiled.Program.ContentHash));

        }

        [Test]
        public void Materializer_RejectsInternallyInconsistentHashedShardMetadata()
        {
            var metadata = GenerationShard.AibtGeneratedMetadata.CanonicalDescriptorJson;
            var offsetTamper = ReplaceOnce(metadata, "\"offset\":4", "\"offset\":8");
            Assert.Throws<ArgumentException>(() => GeneratedShardMetadataMaterializer.Materialize(
                offsetTamper, StableHash.Sha256Hex(offsetTamper),
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash));

            var hashTamper = ReplaceOnce(metadata,
                ExtractHash(metadata, "configurationHash"),
                new string('0', 64));
            Assert.Throws<ArgumentException>(() => GeneratedShardMetadataMaterializer.Materialize(
                hashTamper, StableHash.Sha256Hex(hashTamper),
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash));

            var unknownEncoding = ReplaceOnce(metadata, "\"alignment\":1,\"encoding\":0", "\"alignment\":1,\"encoding\":99");
            Assert.Throws<ArgumentException>(() => GeneratedShardMetadataMaterializer.Materialize(
                unknownEncoding, StableHash.Sha256Hex(unknownEncoding),
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash));

            var nonCanonicalRegistry = GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson.Replace("\n", "\r\n");
            Assert.Throws<ArgumentException>(() => GeneratedShardMetadataMaterializer.Materialize(
                metadata, GenerationShard.AibtGeneratedMetadata.DescriptorHash,
                nonCanonicalRegistry, StableHash.Sha256Hex(nonCanonicalRegistry)));

            var nonCanonicalDescriptor = metadata.Replace("{\"abiVersion\":2", "{ \"abiVersion\":2");
            Assert.Throws<ArgumentException>(() => GeneratedShardMetadataMaterializer.Materialize(
                nonCanonicalDescriptor, StableHash.Sha256Hex(nonCanonicalDescriptor),
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash));

            var equalityTamper = ReplaceFirst(metadata, "69e3a80e385e338e", "0000000000000000");
            Assert.That(() => GeneratedShardMetadataMaterializer.Materialize(
                equalityTamper, StableHash.Sha256Hex(equalityTamper),
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash), Throws.InstanceOf<ArgumentException>());
            var scalarTamper = ReplaceFirst(metadata, "\"type\":\"Int32\",\"version\":1", "\"type\":\"UInt32\",\"version\":1");
            Assert.Throws<ArgumentException>(() => GeneratedShardMetadataMaterializer.Materialize(
                scalarTamper, StableHash.Sha256Hex(scalarTamper),
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash));

            var storageSchemaTamper = ReplaceOnce(metadata,
                "\"bindingId\":\"\",\"schemaId\":\"aibt.tests.registered-value.schema\"",
                "\"bindingId\":\"\",\"schemaId\":\"aibt.tests.forged-value.schema\"");
            Assert.Throws<ArgumentException>(() => GeneratedShardMetadataMaterializer.Materialize(
                storageSchemaTamper, StableHash.Sha256Hex(storageSchemaTamper),
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash));

            var artifact = GeneratedShardMetadataMaterializer.MaterializeArtifact(
                metadata, GenerationShard.AibtGeneratedMetadata.DescriptorHash,
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash);
            var entry = artifact.RegisteredTypes.Entries.Single();
            var registeredType = new GeneratedTypeRecord(GeneratedTypeRole.Value, entry.CanonicalTypeId,
                entry.Version, entry.SchemaHash, entry.Descriptor);
            var registeredBinding = new GeneratedBindingDescriptor("agent-target", GeneratedBindingKind.BlackboardRead,
                BlackboardScope.Agent, GeneratedPhaseCapability.None, new[] { registeredType });
            var original = artifact.Nodes.Single();
            var registeredDescriptor = new GeneratedNodeDescriptor(original.Manifest, original.Configuration,
                original.Memory, new[] { registeredBinding });
            var builtInTypeJson = "{\"role\":0,\"id\":\"Int32\",\"version\":1,\"size\":4,\"alignment\":4,\"encoding\":5,\"schemaId\":\"\",\"schemaHash\":\""
                + new string('0', 64) + "\",\"equalityContractId\":\"0000000000000000\"}";
            var registeredTypeJson = "{\"role\":0,\"id\":\"" + entry.CanonicalTypeId + "\",\"version\":" + entry.Version
                + ",\"size\":" + entry.Descriptor.Size + ",\"alignment\":" + entry.Descriptor.Alignment
                + ",\"encoding\":13,\"schemaId\":\"" + entry.CanonicalSchemaId + "\",\"schemaHash\":\"" + entry.SchemaHash
                + "\",\"equalityContractId\":\"69e3a80e385e338e\"}";
            var registeredBindingMetadata = ReplaceOnce(metadata, builtInTypeJson, registeredTypeJson);
            registeredBindingMetadata = ReplaceOnce(registeredBindingMetadata,
                "\"accessHash\":\"" + original.AccessLayoutHash.HexadecimalValue + "\"",
                "\"accessHash\":\"" + registeredDescriptor.AccessLayoutHash.HexadecimalValue + "\"");
            Assert.That(GeneratedShardMetadataMaterializer.MaterializeArtifact(
                registeredBindingMetadata, StableHash.Sha256Hex(registeredBindingMetadata),
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash).Nodes, Has.Count.EqualTo(1));
            var forgedBindingTypeJson = registeredTypeJson.Replace(entry.CanonicalSchemaId, "aibt.tests.forged-value.schema");
            var bindingSchemaTamper = ReplaceOnce(registeredBindingMetadata, registeredTypeJson, forgedBindingTypeJson);
            Assert.Throws<ArgumentException>(() => GeneratedShardMetadataMaterializer.Materialize(
                bindingSchemaTamper, StableHash.Sha256Hex(bindingSchemaTamper),
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash));
        }

        [Test]
        public void RegisteredCatalog_DrivesJsonValidationAndCompiledDefaultCodec()
        {
            var artifact = GeneratedShardMetadataMaterializer.MaterializeArtifact(
                GenerationShard.AibtGeneratedMetadata.CanonicalDescriptorJson,
                GenerationShard.AibtGeneratedMetadata.DescriptorHash,
                GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson,
                GenerationShard.AibtGeneratedMetadata.NodeRegistryHash);
            var source = File.ReadAllText(FixturePath("generated-tree-v2.aibt.json"));
            var registeredEntry = "\"payload\":{\"type\":\"aibt.tests.registered-value\",\"typeVersion\":1,\"scope\":\"agent\",\"default\":{\"asset\":{\"guid\":\"00000000000000000000000000000001\"},\"count\":7}},\"score\":";
            var registeredJson = ReplaceOnce(source, "\"score\":", registeredEntry);
            var legacy = CanonicalTreeJson.Parse(registeredJson);
            Assert.That(legacy.Success, Is.False, "The legacy overload must remain fail-closed for registered values.");
            var parsed = CanonicalTreeJson.Parse(registeredJson, artifact.RegisteredTypes, "registered-v2.aibt.json");
            Assert.That(parsed.Success, Is.True, Diagnostics(parsed.Diagnostics));
            var canonical = CanonicalTreeJson.Serialize(parsed.Document, artifact.RegisteredTypes);
            Assert.That(canonical.Success, Is.True, Diagnostics(canonical.Diagnostics));
            var registry = GeneratedNodeRegistry.Build(artifact.Nodes, includeBuiltIns: true);
            var options = new ReferenceCompilerOptions("registered-v2.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 5));
            var validation = TreeValidator.Validate(parsed.Document, registry.Registry,
                new ValidationOptions("registered-v2.aibt.json", supportsAgentScope: true, supportsSharedScope: true), artifact.RegisteredTypes);
            Assert.That(validation, Is.Empty, Diagnostics(validation));
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, artifact.Nodes, artifact.RegisteredTypes, options);
            Assert.That(compiled.Success, Is.True, Diagnostics(compiled.Diagnostics));
            var payload = compiled.Program.Slots.Single(value => value.Slot.Key.Id == "payload").Slot.DefaultBytes;
            Assert.That(payload, Has.Length.EqualTo(40));
            Assert.That(payload[0], Is.EqualTo(0));
            Assert.That(payload[8], Is.EqualTo(1));
            Assert.That(BitConverter.ToInt32(payload, 32), Is.EqualTo(7));

            const string assetWithoutLocal = "\"asset\":{\"guid\":\"00000000000000000000000000000001\"}";
            var withLocal = registeredJson.Replace(assetWithoutLocal,
                "\"asset\":{\"guid\":\"00000000000000000000000000000001\",\"localFileId\":-7}");
            var parsedWithLocal = CanonicalTreeJson.Parse(withLocal, artifact.RegisteredTypes);
            Assert.That(parsedWithLocal.Success, Is.True, Diagnostics(parsedWithLocal.Diagnostics));
            var compiledWithLocal = GeneratedCompiledProgramV2Compiler.Compile(
                parsedWithLocal.Document, artifact.Nodes, artifact.RegisteredTypes, options);
            Assert.That(compiledWithLocal.Success, Is.True, Diagnostics(compiledWithLocal.Diagnostics));
            var localPayload = compiledWithLocal.Program.Slots.Single(value => value.Slot.Key.Id == "payload").Slot.DefaultBytes;
            Assert.That(BitConverter.ToInt64(localPayload, 16), Is.EqualTo(-7));
            Assert.That(localPayload[24], Is.EqualTo(1));

            var extra = registeredJson.Replace("\"count\":7", "\"count\":7,\"extra\":0");
            Assert.That(CanonicalTreeJson.Parse(extra, artifact.RegisteredTypes).Success, Is.False);
            var invalidAsset = registeredJson.Replace("00000000000000000000000000000001", "invalid-guid");
            Assert.That(CanonicalTreeJson.Parse(invalidAsset, artifact.RegisteredTypes).Success, Is.False);
            Assert.That(CanonicalTreeJson.Parse(registeredJson.Replace(assetWithoutLocal,
                "\"asset\":{\"guid\":\"00000000000000000000000000000001\",\"unknown\":0}"), artifact.RegisteredTypes).Success, Is.False);
            Assert.That(CanonicalTreeJson.Parse(registeredJson.Replace(assetWithoutLocal,
                "\"asset\":{\"localFileId\":1}"), artifact.RegisteredTypes).Success, Is.False);
            Assert.That(CanonicalTreeJson.Parse(registeredJson.Replace(assetWithoutLocal,
                "\"asset\":{\"localFileId\":1,\"guid\":\"00000000000000000000000000000001\"}"), artifact.RegisteredTypes).Success, Is.False);
            Assert.That(CanonicalTreeJson.Parse(registeredJson.Replace(assetWithoutLocal,
                "\"asset\":{\"guid\":\"00000000000000000000000000000001\",\"guid\":\"00000000000000000000000000000001\"}"), artifact.RegisteredTypes).Success, Is.False);

            var registeredKey = parsed.Document.Blackboard.Single(value => value.Id == "payload");
            var forgedKeys = parsed.Document.Blackboard.Select(value => value.Id == "payload"
                ? new BlackboardKeyDefinition(value.Id, value.Name, value.Type, value.Scope,
                    BlackboardDefaultValue.RegisteredSource(value.Type.CanonicalTypeId, value.Type.RegisteredDescriptor.Version,
                        "{\"asset\":{\"guid\":\"00000000000000000000000000000001\"},\"count\":7}"), value.Description, value.Reduction)
                : value).ToArray();
            var forged = CopyTree(parsed.Document, parsed.Document.Nodes, forgedKeys);
            Assert.That(CanonicalTreeJson.Serialize(forged, artifact.RegisteredTypes).Success, Is.False);
            Assert.That(TreeValidator.Validate(forged, registry.Registry,
                new ValidationOptions("registered-v2.aibt.json", supportsAgentScope: true, supportsSharedScope: true), artifact.RegisteredTypes), Is.Not.Empty);
        }

        [Test]
        public void GeneratedTopology_UsesOriginalCombinedRegistryPoliciesWithoutTestFixtures()
        {
            var descriptors = new[]
            {
                TopologyDescriptor("aibt.tests.topology.composite", NodeBehaviorKind.Composite, 1, null),
                TopologyDescriptor("aibt.tests.topology.decorator", NodeBehaviorKind.Decorator, 1, 1),
                TopologyDescriptor("aibt.tests.topology.condition", NodeBehaviorKind.Condition, 0, 0),
                TopologyDescriptor("aibt.tests.topology.action", NodeBehaviorKind.Action, 0, 0),
            };
            var nodes = new[]
            {
                TopologyNode("action", "aibt.tests.topology.action"),
                TopologyNode("condition", "aibt.tests.topology.condition"),
                TopologyNode("decorator", "aibt.tests.topology.decorator", "condition"),
                TopologyNode("root", "aibt.tests.topology.composite", "decorator", "action"),
            };
            var tree = new TreeDocument(TreeDocument.CurrentFormat, TreeDocument.LatestFormatVersion,
                new TreeId("tests.generated.topology"), "Generated topology", new NodeId("root"), nodes,
                Array.Empty<BlackboardKeyDefinition>(), null, TagSet.Empty, SemanticObject.Empty, new Revision(1), null, null);
            var options = new ReferenceCompilerOptions("generated-topology.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 5));
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(tree, descriptors, options);
            Assert.That(compiled.Success, Is.True, Diagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.SemanticProgram.DebugMap.Select(value => value.AuthoringNodeId.Value),
                Is.EqualTo(new[] { "root", "decorator", "condition", "action" }));
            Assert.That(compiled.Program.SemanticProgram.Nodes.Select(value => value.Children.Count), Is.EqualTo(new uint[] { 2, 1, 0, 0 }));
            Assert.That(compiled.Program.SemanticProgram.Nodes.Select(value => value.NodeTypeId),
                Is.EqualTo(new[] { "aibt.tests.topology.composite", "aibt.tests.topology.decorator", "aibt.tests.topology.condition", "aibt.tests.topology.action" }.Select(StableHash.Fnv1A64)));

            var invalid = CopyTree(tree, new[]
            {
                TopologyNode("condition", "aibt.tests.topology.condition"),
                TopologyNode("decorator", "aibt.tests.topology.decorator"),
                TopologyNode("root", "aibt.tests.topology.composite", "decorator", "condition"),
            }, tree.Blackboard);
            Assert.That(GeneratedCompiledProgramV2Compiler.Compile(invalid, descriptors, options).Success, Is.False,
                "Original generated decorator child policy must reject before the structural projection callback.");
            Assert.That(GeneratedCompiledProgramV2Compiler.Compile(invalid, descriptors, null).Success, Is.False,
                "Nullable options cannot bypass original combined-registry validation authority.");
        }

        private static NodeRegistryBuildResult ExpectedRegistry()
        {
            var manifest = new NodeManifest(
                "aibt.tests.generated-node", 1, "Generated fixture", "Tests", NodeBehaviorKind.Condition,
                "Verify generated metadata", "Not production", NodeExecutionDomain.Burst, true,
                new[] { new NodeParameterContract("enabled", NodeParameterType.Boolean, true) },
                new NodeChildPolicy(0, 0, true), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Failure },
                new NodeMemoryDescriptor(48, 8, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(8, 4, new[]
                {
                    new NodeConfigurationField("enabled", 0, 1, 1),
                    new NodeConfigurationField("target", 4, 4, 4, isGeneratedHandle: true),
                }),
                NodeCancellationMode.NotApplicable, NodeCostHint.Trivial,
                new[] { new NodeManifestExample("generation-success", "{\"enabled\":false}", "Generated fixture") });
            return new NodeRegistryBuilder().AddUserExtension(manifest).Build();
        }

        private static GeneratedNodeDescriptor TopologyDescriptor(
            string typeId,
            NodeBehaviorKind kind,
            uint minimumChildren,
            uint? maximumChildren)
        {
            var manifest = new NodeManifest(
                typeId, 1, typeId, "Tests", kind, "Topology canary", "Not production",
                NodeExecutionDomain.Burst, true, Array.Empty<NodeParameterContract>(),
                new NodeChildPolicy(minimumChildren, maximumChildren, true),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Failure },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                NodeCancellationMode.NotApplicable, NodeCostHint.Trivial,
                new[] { new NodeManifestExample("topology", "{}", "Topology canary") });
            return new GeneratedNodeDescriptor(manifest, Array.Empty<GeneratedStorageField>(),
                Array.Empty<GeneratedStorageField>(), Array.Empty<GeneratedBindingDescriptor>());
        }

        private static NodeDocument TopologyNode(string id, string typeId, params string[] children)
            => new NodeDocument(new NodeId(id), typeId, 1, children.Select(value => new NodeId(value)), SemanticObject.Empty,
                null, null, null, TagSet.Empty);

        private static TreeDocument CopyTree(
            TreeDocument source,
            IEnumerable<NodeDocument> nodes,
            IEnumerable<BlackboardKeyDefinition> blackboard)
            => new TreeDocument(source.Format, source.FormatVersion, source.TreeId, source.Name, source.Root,
                nodes, blackboard, source.Description, source.Tags, source.Metadata, source.Revision,
                source.AgentContract, source.SharedContract);

        private static string Diagnostics(DiagnosticCollection diagnostics)
            => string.Join(" | ", diagnostics.Select(value => value.Code.Value + ":" + value.Message));

        private static string FixturePath(string fileName)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(GeneratedArtifactContractTests).Assembly);
            Assert.That(package, Is.Not.Null, "The CodeGen test assembly must belong to the AIBT package.");
            return Path.Combine(package.resolvedPath, "Tests", "Fixtures", "P2", "CodeGen", fileName);
        }

        private static string ReplaceOnce(string value, string oldValue, string newValue)
        {
            var index = value.IndexOf(oldValue, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Assert.That(value.IndexOf(oldValue, index + oldValue.Length, StringComparison.Ordinal), Is.LessThan(0));
            return value.Substring(0, index) + newValue + value.Substring(index + oldValue.Length);
        }

        private static string ReplaceFirst(string value, string oldValue, string newValue)
        {
            var index = value.IndexOf(oldValue, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            return value.Substring(0, index) + newValue + value.Substring(index + oldValue.Length);
        }

        private static string ExtractHash(string json, string property)
        {
            var prefix = "\"" + property + "\":\"";
            var start = json.IndexOf(prefix, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            return json.Substring(start + prefix.Length, 64);
        }

        private static string ExtractJsonString(string json, string property)
        {
            var prefix = "\"" + property + "\": \"";
            var start = json.IndexOf(prefix, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            start += prefix.Length;
            var end = json.IndexOf('"', start);
            Assert.That(end, Is.GreaterThan(start));
            return json.Substring(start, end - start);
        }

        private static byte[] ParseHex(string value)
        {
            var result = new byte[value.Length / 2];
            for (var index = 0; index < result.Length; index++)
                result[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
            return result;
        }
    }

    internal static class IndependentV2Stream
    {
        internal static byte[] Encode(GeneratedCompiledProgramV2 value)
        {
            var output = new IndependentByteWriter();
            var program = value.SemanticProgram;
            var header = program.Header;
            output.U32(header.Magic);
            output.U32(2);
            output.U32(header.ExecutionSemanticsVersion);
            output.U16(header.CompilerVersion.Major);
            output.U16(header.CompilerVersion.Minor);
            output.U16(header.CompilerVersion.Patch);
            output.U32(header.CompilerVersion.BuildRevision);
            output.Hash(header.CanonicalSemanticHash.HexadecimalValue);
            output.Hash(header.NodeRegistryHash.HexadecimalValue);
            output.Hash(header.CanonicalPolicyHash.HexadecimalValue);
            output.U32(header.PolicyFormatVersion);
            output.U32(header.RootNodeIndex);
            output.U32((uint)program.Nodes.Count);
            output.U32((uint)program.ChildIndices.Count);
            output.U32((uint)value.Slots.Count);
            output.U32((uint)program.DebugMap.Count);
            var config = value.GetConfigBlobCopy();
            var defaults = value.GetDefaultValueBlobCopy();
            output.U32((uint)config.Length);
            output.U32(header.InstanceNodeMemorySize);
            output.U32(header.RequiredMaximumAlignment);
            var capabilities = header.CapabilityFlags;
            foreach (var descriptor in value.Scopes.Descriptors)
                capabilities |= descriptor.Scope == BlackboardScope.Agent
                    ? GeneratedCompiledProgramV2.AgentScopeCapability
                    : GeneratedCompiledProgramV2.SharedScopeCapability;
            output.U32(capabilities);
            output.U8(header.DeterministicModeCompatible ? (byte)1 : (byte)0);

            output.U32((uint)value.Scopes.Descriptors.Count);
            foreach (var descriptor in value.Scopes.Descriptors)
            {
                output.U8(Scope(descriptor.Scope));
                output.Text(descriptor.Contract.ContractId);
                output.U64(StableHash.Fnv1A64(descriptor.Contract.ContractId));
                output.U32(descriptor.Contract.ContractVersion);
                output.Hash(descriptor.SchemaHash.HexadecimalValue);
                output.Hash(descriptor.LayoutHash.HexadecimalValue);
                output.U32(descriptor.FirstSlot);
                output.U32(descriptor.SlotCount);
            }

            for (var nodeIndex = 0; nodeIndex < program.Nodes.Count; nodeIndex++)
            {
                var node = program.Nodes[nodeIndex];
                output.U64(node.NodeTypeId); output.U32(node.NodeTypeVersion);
                output.U32(node.ConfigOffset); output.U32(node.ConfigSize); output.U32(node.ConfigAlignment);
                output.U32(node.InstanceMemoryOffset); output.U32(node.InstanceMemorySize); output.U32(node.InstanceMemoryAlignment);
                output.U8((byte)node.MemoryLifetime);
                output.U32(node.Children.Offset); output.U32(node.Children.Count);
                output.U32((uint)node.Flags); output.U32(node.DebugIdentityIndex);
                var firstRead = 0u; var readCount = 0u; var firstWrite = 0u; var writeCount = 0u;
                var hasRead = false; var hasWrite = false;
                for (var accessIndex = 0; accessIndex < value.Accesses.Count; accessIndex++)
                {
                    var access = value.Accesses[accessIndex];
                    if (access.NodeIndex != nodeIndex) continue;
                    if (access.Mode != GeneratedAccessModeV2.Write)
                    { if (!hasRead) { firstRead = (uint)accessIndex; hasRead = true; } readCount++; }
                    if (access.Mode != GeneratedAccessModeV2.Read)
                    { if (!hasWrite) { firstWrite = (uint)accessIndex; hasWrite = true; } writeCount++; }
                }
                output.U32(firstRead); output.U32(readCount); output.U32(firstWrite); output.U32(writeCount);
            }
            foreach (var child in program.ChildIndices) output.U32(child);

            output.U32((uint)value.Accesses.Count);
            foreach (var access in value.Accesses)
            {
                output.U32(access.NodeIndex); output.U32(access.AccessOrdinal); output.U8(Scope(access.Scope));
                output.U32(access.SlotIndex); output.U8((byte)access.Mode); output.U8((byte)access.Reduction);
            }
            output.U32((uint)value.Slots.Count);
            foreach (var record in value.Slots)
            {
                var slot = record.Slot;
                var layout = slot.Key.Type.RuntimeDescriptor;
                output.Text(slot.Key.Id); output.U64(StableHash.Fnv1A64(slot.Key.Id));
                output.U64(layout.TypeId); output.U32(layout.Version); output.U64(slot.Key.Type.EnumContractId);
                output.U8(Scope(slot.Key.Scope)); output.U32(slot.SlotIndex); output.U32(slot.Offset);
                output.U32((uint)layout.Size); output.U32((uint)layout.Alignment);
                output.U32(record.DefaultOffset); output.U32((uint)(slot.DefaultBytes?.Length ?? 0));
                output.U8((byte)record.AccessFlags); output.U32(uint.MaxValue); output.U32(0);
            }
            output.U32((uint)program.Observers.Count);
            foreach (var observer in program.Observers)
            {
                output.U32(observer.ObserverNodeIndex); output.U32(observer.OwningReactiveCompositeIndex);
                output.U8((byte)observer.Mode); output.U32(observer.WatchedSlots.Offset); output.U32(observer.WatchedSlots.Count);
            }
            output.U32((uint)value.WatchedSlots.Count);
            foreach (var watched in value.WatchedSlots) { output.U8(Scope(watched.Scope)); output.U32(watched.SlotIndex); }
            output.Bytes(config); output.Bytes(defaults);
            output.U32((uint)value.Scopes.Descriptors.Count);
            foreach (var descriptor in value.Scopes.Descriptors) output.Bytes(descriptor.GetRawLayoutCopy());
            output.U32((uint)program.DebugMap.Count);
            foreach (var debug in program.DebugMap)
            {
                output.U32(debug.RuntimeNodeIndex); output.Text(debug.AuthoringNodeId.Value);
                output.Text(debug.SourcePath); output.Text(debug.DisplayName ?? string.Empty);
            }
            return output.ToArray();
        }

        private static byte Scope(BlackboardScope value)
            => value == BlackboardScope.Tree ? (byte)0 : value == BlackboardScope.Agent ? (byte)1 : (byte)2;
    }

    internal sealed class IndependentByteWriter
    {
        private readonly MemoryStream _stream = new MemoryStream();
        internal void U8(byte value) => _stream.WriteByte(value);
        internal void U16(ushort value) { U8((byte)value); U8((byte)(value >> 8)); }
        internal void U32(uint value) { U8((byte)value); U8((byte)(value >> 8)); U8((byte)(value >> 16)); U8((byte)(value >> 24)); }
        internal void U64(ulong value) { U32((uint)value); U32((uint)(value >> 32)); }
        internal void Text(string value) => Bytes(Encoding.UTF8.GetBytes(value));
        internal void Bytes(byte[] value) { U32((uint)value.Length); _stream.Write(value, 0, value.Length); }
        internal void Hash(string value)
        {
            for (var index = 0; index < value.Length; index += 2)
                U8(Convert.ToByte(value.Substring(index, 2), 16));
        }
        internal byte[] ToArray() => _stream.ToArray();
    }

}
