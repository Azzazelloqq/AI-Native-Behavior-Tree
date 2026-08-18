using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.BlackboardScopes
{
    public sealed class GeneratedScopeCompilerTests
    {
        [Test]
        public void LayoutAndPacking_AreCanonicalAndZeroPaddingIndependentOfDeclarationOrder()
        {
            var first = Descriptor(new[]
            {
                Field("value", "UInt64", 8, 8, GeneratedFieldEncoding.UInt64LE),
                Field("enabled", "Bool", 1, 1, GeneratedFieldEncoding.Bool8),
                Handle("target", "agent-target"),
            }, Binding("agent-target", GeneratedBindingKind.BlackboardRead, BlackboardScope.Agent, "Int32"));
            var second = Descriptor(new[]
            {
                Handle("target", "agent-target"),
                Field("enabled", "Bool", 1, 1, GeneratedFieldEncoding.Bool8),
                Field("value", "UInt64", 8, 8, GeneratedFieldEncoding.UInt64LE),
            }, Binding("agent-target", GeneratedBindingKind.BlackboardRead, BlackboardScope.Agent, "Int32"));

            Assert.That(second.ConfigurationLayoutHash, Is.EqualTo(first.ConfigurationLayoutHash));
            Assert.That(second.AccessLayoutHash, Is.EqualTo(first.AccessLayoutHash));
            Assert.That(first.Configuration.Select(field => field.FieldId), Is.EqualTo(new[] { "enabled", "target", "value" }));
            Assert.That(first.Configuration.Select(field => field.Offset), Is.EqualTo(new uint[] { 0, 4, 8 }));

            var packed = GeneratedConfigurationPacker.Pack(
                first,
                new SemanticObject(new[]
                {
                    new SemanticProperty("value", SemanticValue.FromUInt64(0x0102030405060708UL)),
                    new SemanticProperty("enabled", SemanticValue.FromBoolean(true)),
                }),
                new Dictionary<string, uint> { ["agent-target"] = 7 });

            var fixturePath = EditorTestPackagePaths.Resolve("Tests", "Fixtures", "P2", "CodeGen", "generated-layout-v1.json");
            var fixture = JObject.Parse(File.ReadAllText(fixturePath));
            var expected = Hex((string)fixture["configuration"]["packedHex"]);
            Assert.That(first.Manifest.Configuration.Size, Is.EqualTo((uint)fixture["configuration"]["size"]));
            Assert.That(first.Manifest.Configuration.Alignment, Is.EqualTo((byte)fixture["configuration"]["alignment"]));
            Assert.That(packed.Success, Is.True, Diagnostics(packed.Diagnostics));
            Assert.That(packed.Bytes, Is.EqualTo(expected));
        }

        [Test]
        public void Compile_BindsExactAgentKeyAndRejectsMissingWrongOrUnknownMappings()
        {
            var descriptor = Descriptor(
                new[] { Handle("target", "agent-target") },
                Binding("agent-target", GeneratedBindingKind.BlackboardRead, BlackboardScope.Agent, "Int32"));
            var valid = Tree(
                BlackboardScope.Agent,
                BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32),
                new NodeBindingMap(new[] { Pair("agent-target", "score") }));

            var result = GeneratedScopeCompiler.Compile(valid, new[] { descriptor }, "fixtures/generated.aibt.json");

            Assert.That(result.Success, Is.True, Diagnostics(result.Diagnostics));
            Assert.That(result.Descriptors.Single().Scope, Is.EqualTo(BlackboardScope.Agent));
            Assert.That(result.Accesses.Single().AccessOrdinal, Is.Zero);
            Assert.That(result.Accesses.Single().ScopeSlot, Is.Zero);
            Assert.That(result.Configurations.Single().Value, Is.EqualTo(new byte[] { 0, 0, 0, 0 }));

            AssertCode(GeneratedScopeCompiler.Compile(
                Tree(BlackboardScope.Agent, BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32), null),
                new[] { descriptor }), ReferenceCompilerDiagnosticCodes.InvalidGeneratedBinding);
            AssertCode(GeneratedScopeCompiler.Compile(
                Tree(BlackboardScope.Tree, BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32), new NodeBindingMap(new[] { Pair("agent-target", "score") })),
                new[] { descriptor }), ReferenceCompilerDiagnosticCodes.InvalidGeneratedBinding);
            AssertCode(GeneratedScopeCompiler.Compile(
                Tree(BlackboardScope.Agent, BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool), new NodeBindingMap(new[] { Pair("agent-target", "score") })),
                new[] { descriptor }), ReferenceCompilerDiagnosticCodes.InvalidGeneratedBinding);
            AssertCode(GeneratedScopeCompiler.Compile(
                Tree(BlackboardScope.Agent, BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32), new NodeBindingMap(new[] { Pair("other", "score") })),
                new[] { descriptor }), ReferenceCompilerDiagnosticCodes.InvalidGeneratedBinding);
        }

        [Test]
        public void CompiledV2_PreservesLocalOrdinalsForNonBlackboardHandles()
        {
            var descriptor = Descriptor(
                new[]
                {
                    Handle("a-first", "first-snapshot"),
                    Handle("b-second", "second-snapshot"),
                },
                Binding("first-snapshot", GeneratedBindingKind.SnapshotRead,
                    (BlackboardScope)byte.MaxValue, "Int32"),
                Binding("second-snapshot", GeneratedBindingKind.SnapshotRead,
                    (BlackboardScope)byte.MaxValue, "Int32"));
            var root = NodeDocument.CreateVersion2(
                new NodeId("root"), "example.generated-node", 1, null,
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var document = TreeDocument.CreateVersion2(
                new TreeId("tree"), "Tree", root.Id, new[] { root },
                null, null, Array.Empty<BlackboardKeyDefinition>(),
                tags: TagSet.Empty, metadata: SemanticObject.Empty);

            var result = GeneratedCompiledProgramV2Compiler.Compile(
                document,
                new[] { descriptor },
                new ReferenceCompilerOptions(
                    "nonblackboard-handles.aibt.json",
                    ReferenceCompilationPolicy.Phase1,
                    new CompiledCompilerVersion(1, 0, 0, 5)));

            Assert.That(result.Success, Is.True, Diagnostics(result.Diagnostics));
            Assert.That(result.Program.GetConfigBlobCopy(), Is.EqualTo(new byte[]
            {
                0, 0, 0, 0,
                1, 0, 0, 0,
            }));
            Assert.That(result.Program.Accesses, Is.Empty,
                "Non-blackboard binding ordinals must not fabricate blackboard access records.");
        }

        [Test]
        public void ScopeDescriptor_FirstSlotIndexesTheCombinedTreeAgentSharedTable()
        {
            var descriptor = Descriptor(Array.Empty<GeneratedStorageField>());
            var root = new NodeDocument(new NodeId("root"), "example.generated-node", 1);
            var document = TreeDocument.CreateVersion2(
                new TreeId("tree"), "Tree", root.Id, new[] { root },
                new BlackboardScopeContract("example.agent", 1),
                new BlackboardScopeContract("example.shared", 1),
                new[]
                {
                    new BlackboardKeyDefinition("tree-key", "Tree", BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32), BlackboardScope.Tree, BlackboardDefaultValue.Int32(0)),
                    new BlackboardKeyDefinition("agent-key", "Agent", BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32), BlackboardScope.Agent, BlackboardDefaultValue.Int32(0)),
                    new BlackboardKeyDefinition("shared-key", "Shared", BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32), BlackboardScope.Shared, BlackboardDefaultValue.Int32(0), null, BlackboardReductionKind.First),
                });

            var result = GeneratedScopeCompiler.Compile(document, new[] { descriptor });

            Assert.That(result.Success, Is.True, Diagnostics(result.Diagnostics));
            Assert.That(result.Descriptors.Select(value => value.FirstSlot), Is.EqualTo(new uint[] { 1, 2 }));
        }

        [Test]
        public void ScopeHashes_AreStableAcrossCultureAndBlackboardOrderAndCoverReduction()
        {
            var descriptor = Descriptor(Array.Empty<GeneratedStorageField>());
            var keys = new[]
            {
                Key("zeta", BlackboardDefaultValue.Int64(3), BlackboardReductionKind.Sum),
                Key("alpha", BlackboardDefaultValue.Int64(-4), BlackboardReductionKind.Min),
            };
            var first = ScopeTree(keys);
            var second = ScopeTree(keys.Reverse());
            var original = CultureInfo.CurrentCulture;
            GeneratedScopeCompilationResult left;
            GeneratedScopeCompilationResult right;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                left = GeneratedScopeCompiler.Compile(first, new[] { descriptor });
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                right = GeneratedScopeCompiler.Compile(second, new[] { descriptor });
            }
            finally { CultureInfo.CurrentCulture = original; }

            Assert.That(left.Success, Is.True, Diagnostics(left.Diagnostics));
            Assert.That(right.Success, Is.True, Diagnostics(right.Diagnostics));
            Assert.That(right.Descriptors.Single().SchemaHash, Is.EqualTo(left.Descriptors.Single().SchemaHash));
            Assert.That(right.Descriptors.Single().LayoutHash, Is.EqualTo(left.Descriptors.Single().LayoutHash));

            var changed = ScopeTree(new[]
            {
                Key("alpha", BlackboardDefaultValue.Int64(-4), BlackboardReductionKind.Max),
                Key("zeta", BlackboardDefaultValue.Int64(3), BlackboardReductionKind.Sum),
            });
            var changedResult = GeneratedScopeCompiler.Compile(changed, new[] { descriptor });
            Assert.That(changedResult.Descriptors.Single().SchemaHash, Is.Not.EqualTo(left.Descriptors.Single().SchemaHash));
            Assert.That(changedResult.Descriptors.Single().LayoutHash, Is.Not.EqualTo(left.Descriptors.Single().LayoutHash));
        }

        [Test]
        public void SharedGeneratedWritesAndUnacceptedRegisteredEqualityAreRejectedBeforeCompilation()
        {
            Assert.Throws<ArgumentException>(() => Binding(
                "shared-write", GeneratedBindingKind.BlackboardWrite, BlackboardScope.Shared, "Int32"));

            var typeId = "example.value";
            var wrongEquality = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64(typeId), 1, 4, 4, 1, StableHash.Fnv1A64("example.schema"));
            Assert.Throws<ArgumentException>(() => new GeneratedTypeRecord(
                GeneratedTypeRole.Value, typeId, 1,
                "1111111111111111111111111111111111111111111111111111111111111111",
                wrongEquality));

            var accepted = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("Invalid_Type"), 1, 4, 4,
                0x69e3a80e385e338eUL, StableHash.Fnv1A64("example.schema"));
            Assert.Throws<ArgumentException>(() => new GeneratedTypeRecord(
                GeneratedTypeRole.Value, "Invalid_Type", 1,
                "1111111111111111111111111111111111111111111111111111111111111111", accepted));
            Assert.Throws<ArgumentException>(() => new GeneratedStorageField(
                "registered", "Invalid_Type", 1, 4, 4, GeneratedFieldEncoding.Registered,
                "1111111111111111111111111111111111111111111111111111111111111111",
                registeredDescriptor: accepted));
        }

        [Test]
        public void GeneratedRegistry_UsesUnmodifiedP1ManifestProjectionAndHash()
        {
            var descriptor = Descriptor(Array.Empty<GeneratedStorageField>());
            var generated = GeneratedNodeRegistry.Build(new[] { descriptor }, includeBuiltIns: false);
            var baseline = new NodeRegistryBuilder().AddUserExtension(descriptor.Manifest).Build();

            Assert.That(generated.Success, Is.True, Diagnostics(generated.Diagnostics));
            Assert.That(generated.Registry.Hash, Is.EqualTo(baseline.Registry.Hash));
            Assert.That(generated.Registry.Single().Manifest.Reads, Is.Empty);
            Assert.That(generated.Registry.Single().Manifest.Writes, Is.Empty);
        }

        [Test]
        public void GeneratedLayoutsAndRegistry_RejectForgedLayoutDuplicateFieldsAndDuplicateTypeVersion()
        {
            var descriptor = Descriptor(Array.Empty<GeneratedStorageField>());
            Assert.Throws<ArgumentException>(() => new GeneratedStorageField(
                "forged", "UInt64", 1, 4, 4, GeneratedFieldEncoding.UInt32LE));
            Assert.Throws<ArgumentException>(() => new GeneratedStorageField(
                "unregistered", "example.value", 1, 4, 4, GeneratedFieldEncoding.Registered,
                "1111111111111111111111111111111111111111111111111111111111111111"));

            Assert.Throws<ArgumentException>(() => new GeneratedNodeDescriptor(
                descriptor.Manifest,
                Array.Empty<GeneratedStorageField>(),
                new[]
                {
                    Field("duplicate", "UInt32", 4, 4, GeneratedFieldEncoding.UInt32LE),
                    Field("duplicate", "UInt32", 4, 4, GeneratedFieldEncoding.UInt32LE),
                },
                Array.Empty<GeneratedBindingDescriptor>()));

            var duplicate = GeneratedNodeRegistry.Build(new[] { descriptor, descriptor }, includeBuiltIns: false);
            Assert.That(duplicate.Success, Is.False);
            Assert.That(duplicate.Diagnostics.Select(value => value.Code.Value), Does.Contain("AIBT3001"));
        }

        [Test]
        public void ScopeCompiler_EmitsExactContractAndReducerDiagnostics()
        {
            var descriptor = Descriptor(Array.Empty<GeneratedStorageField>());
            var node = new NodeDocument(new NodeId("root"), "example.generated-node", 1);
            var mismatched = TreeDocument.CreateVersion2(
                new TreeId("tree"), "Tree", node.Id, new[] { node },
                new BlackboardScopeContract("example.same", 1),
                new BlackboardScopeContract("example.same", 1),
                new[]
                {
                    new BlackboardKeyDefinition("agent", "agent", BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32), BlackboardScope.Agent, BlackboardDefaultValue.Int32(0)),
                    new BlackboardKeyDefinition("shared", "shared", BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool), BlackboardScope.Shared, BlackboardDefaultValue.Bool(false), null, BlackboardReductionKind.Any),
                });
            AssertCode(GeneratedScopeCompiler.Compile(mismatched, new[] { descriptor }), ReferenceCompilerDiagnosticCodes.ScopeContractMismatch);

            var custom = TreeDocument.CreateVersion2(
                new TreeId("tree"), "Tree", node.Id, new[] { node }, null,
                new BlackboardScopeContract("example.shared", 1),
                new[] { new BlackboardKeyDefinition("shared", "shared", BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32), BlackboardScope.Shared, BlackboardDefaultValue.Int32(0), null, (BlackboardReductionKind)255) });
            AssertCode(GeneratedScopeCompiler.Compile(custom, new[] { descriptor }), ReferenceCompilerDiagnosticCodes.UnsupportedReduction);

            var invalid = TreeDocument.CreateVersion2(
                new TreeId("tree"), "Tree", node.Id, new[] { node }, null,
                new BlackboardScopeContract("example.shared", 1),
                new[] { new BlackboardKeyDefinition("shared", "shared", BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool), BlackboardScope.Shared, BlackboardDefaultValue.Bool(false), null, BlackboardReductionKind.Min) });
            AssertCode(GeneratedScopeCompiler.Compile(invalid, new[] { descriptor }), ReferenceCompilerDiagnosticCodes.InvalidReduction);
        }

        [Test]
        public void CompileSet_RejectsCrossTreeContractSchemaMismatchWithDeterministicRelatedLocation()
        {
            var descriptor = Descriptor(Array.Empty<GeneratedStorageField>());
            var first = ScopeTree("tree-a", "example.shared-set", new[]
            {
                Key("score", BlackboardDefaultValue.Int64(1), BlackboardReductionKind.Sum),
            });
            var second = ScopeTree("tree-z", "example.shared-set", new[]
            {
                Key("score", BlackboardDefaultValue.Int64(2), BlackboardReductionKind.Sum),
            });

            var result = GeneratedScopeCompiler.CompileSet(new[]
            {
                new GeneratedScopeCompilationInput(second, new[] { descriptor }, "z.aibt.json"),
                new GeneratedScopeCompilationInput(first, new[] { descriptor }, "a.aibt.json"),
            });

            var diagnostic = result.Diagnostics.Single(value => value.Code == ReferenceCompilerDiagnosticCodes.ScopeContractMismatch);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Results, Has.Count.EqualTo(2));
            Assert.That(diagnostic.Location.DocumentId, Is.EqualTo("z.aibt.json"));
            Assert.That(diagnostic.Location.JsonPointer, Is.EqualTo("/blackboardContracts/shared"));
            Assert.That(diagnostic.RelatedLocations, Has.Count.EqualTo(1));
            Assert.That(diagnostic.RelatedLocations[0].DocumentId, Is.EqualTo("a.aibt.json"));
            Assert.That(diagnostic.RelatedLocations[0].JsonPointer, Is.EqualTo("/blackboardContracts/shared"));
        }

        [Test]
        public void CompiledV2_InnerSemanticProgramRetainsExactV1ContentHash()
        {
            var descriptor = Descriptor(Array.Empty<GeneratedStorageField>());
            var node = NodeDocument.CreateVersion2(new NodeId("root"), "example.generated-node", 1, null,
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var document = TreeDocument.CreateVersion2(
                new TreeId("tree"), "Tree", node.Id, new[] { node },
                new BlackboardScopeContract("example.agent", 1), null,
                new[]
                {
                    new BlackboardKeyDefinition("score", "score", BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32),
                        BlackboardScope.Agent, BlackboardDefaultValue.Int32(0)),
                }, tags: TagSet.Empty, metadata: SemanticObject.Empty);
            var result = GeneratedCompiledProgramV2Compiler.Compile(
                document,
                new[] { descriptor },
                new ReferenceCompilerOptions("inner-hash.aibt.json", ReferenceCompilationPolicy.Phase1,
                    new CompiledCompilerVersion(1, 0, 0, 5)));

            Assert.That(result.Success, Is.True, Diagnostics(result.Diagnostics));
            Assert.That(result.Program.SemanticProgram.Header.CompiledContentHash,
                Is.EqualTo(CompiledProgramContentHashV1.Compute(result.Program.SemanticProgram)));
            Assert.That(result.Program.SemanticProgram.Header.CompiledContentHash,
                Is.Not.EqualTo(result.Program.ContentHash));
        }

        private static GeneratedNodeDescriptor Descriptor(IEnumerable<GeneratedStorageField> fields, params GeneratedBindingDescriptor[] bindings)
        {
            var ordered = fields.OrderBy(field => field.FieldId, StringComparer.Ordinal).ToArray();
            uint cursor = 0;
            byte alignment = 1;
            var parameters = new List<NodeParameterContract>();
            var packing = new List<NodeConfigurationField>();
            foreach (var field in ordered)
            {
                cursor = Align(cursor, field.Alignment);
                packing.Add(new NodeConfigurationField(
                    field.FieldId, cursor, field.Size, field.Alignment,
                    field.Encoding == GeneratedFieldEncoding.GeneratedHandle));
                if (field.Encoding != GeneratedFieldEncoding.GeneratedHandle)
                    parameters.Add(new NodeParameterContract(
                        field.FieldId,
                        field.Encoding == GeneratedFieldEncoding.Bool8 ? NodeParameterType.Boolean
                            : field.Encoding == GeneratedFieldEncoding.UInt32LE ? NodeParameterType.UInt32 : NodeParameterType.UInt64,
                        true));
                cursor += field.Size;
                if (field.Alignment > alignment) alignment = field.Alignment;
            }
            cursor = Align(cursor, alignment);
            var manifest = new NodeManifest(
                "example.generated-node", 1, "Generated node.", "Tests", NodeBehaviorKind.Action,
                "Use in tests.", "Do not ship.", NodeExecutionDomain.Burst, true,
                parameters, new NodeChildPolicy(0, 0, true), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                new[] { NodeStatus.Success }, new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(cursor, alignment, packing), NodeCancellationMode.AbortOnly,
                NodeCostHint.Trivial, new[] { new NodeManifestExample("success", "{}", "Succeeds.") });
            return new GeneratedNodeDescriptor(manifest, ordered, Array.Empty<GeneratedStorageField>(), bindings);
        }

        private static GeneratedStorageField Field(string id, string type, uint size, byte alignment, GeneratedFieldEncoding encoding)
            => new GeneratedStorageField(id, type, 1, size, alignment, encoding);
        private static GeneratedStorageField Handle(string id, string binding)
            => new GeneratedStorageField(id, "GeneratedHandle", 1, 4, 4, GeneratedFieldEncoding.GeneratedHandle, bindingId: binding);
        private static GeneratedBindingDescriptor Binding(string id, GeneratedBindingKind kind, BlackboardScope scope, string type)
            => new GeneratedBindingDescriptor(id, kind, scope, GeneratedPhaseCapability.None,
                new[] { new GeneratedTypeRecord(GeneratedTypeRole.Value, type, 1) });
        private static KeyValuePair<string, string> Pair(string binding, string key) => new KeyValuePair<string, string>(binding, key);

        private static TreeDocument Tree(BlackboardScope scope, BlackboardTypeReference type, NodeBindingMap bindings)
        {
            var key = new BlackboardKeyDefinition("score", "Score", type, scope,
                BlackboardDefaultValue.Int32(0));
            var node = NodeDocument.CreateVersion2(new NodeId("root"), "example.generated-node", 1, bindings);
            return TreeDocument.CreateVersion2(
                new TreeId("tree"), "Tree", node.Id, new[] { node },
                scope == BlackboardScope.Agent ? new BlackboardScopeContract("example.agent", 1) : null,
                scope == BlackboardScope.Shared ? new BlackboardScopeContract("example.shared", 1) : null,
                new[] { key });
        }

        private static TreeDocument ScopeTree(IEnumerable<BlackboardKeyDefinition> keys)
            => ScopeTree("tree", "example.shared", keys);

        private static TreeDocument ScopeTree(string treeId, string contractId, IEnumerable<BlackboardKeyDefinition> keys)
        {
            var node = new NodeDocument(new NodeId("root"), "example.generated-node", 1);
            return TreeDocument.CreateVersion2(
                new TreeId(treeId), "Tree", node.Id, new[] { node }, null,
                new BlackboardScopeContract(contractId, 3), keys);
        }

        private static BlackboardKeyDefinition Key(string id, BlackboardDefaultValue value, BlackboardReductionKind reduction)
            => new BlackboardKeyDefinition(id, id, BlackboardTypeReference.BuiltIn(BlackboardValueType.Int64), BlackboardScope.Shared, value, null, reduction);
        private static uint Align(uint value, byte alignment) => (value + (uint)alignment - 1) & ~((uint)alignment - 1);
        private static byte[] Hex(string value)
        {
            var bytes = new byte[value.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
                bytes[index] = byte.Parse(value.Substring(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }
        private static void AssertCode(GeneratedScopeCompilationResult result, DiagnosticCode code)
            => Assert.That(result.Diagnostics.Any(value => value.Code == code), Is.True, Diagnostics(result.Diagnostics));
        private static string Diagnostics(DiagnosticCollection values) => string.Join("\n", values.Select(value => value.Code + ": " + value.Message));
    }
}
