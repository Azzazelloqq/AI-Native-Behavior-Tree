using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AIBT.Authoring;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using AuthoringNodeRegistry = AIBT.Authoring.NodeRegistry;

namespace AIBT.Tests.Editor.Compilation
{
    public sealed class ReferenceCompilerTests
    {
        [Test]
        public void Compile_MinimalTree_ProducesExclusiveSuccessfulResult()
        {
            var result = Compile(MinimalTree(), RegistryWithFixtures(), Options());

            Assert.That(result.Success, Is.True, DiagnosticsText(result));
            Assert.That(result.Program, Is.Not.Null);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Program.Header.CanonicalSemanticHash.IsValid, Is.True);
            Assert.That(result.Program.Header.CompiledContentHash.IsValid, Is.True);
            Assert.That(result.Program.DebugMap[0].SourcePath, Is.EqualTo("trees/minimal.aibt.json"));

            var goldenPath = EditorTestPackagePaths.Resolve(
                "Tests", "Fixtures", "Trees", "Compilation", "minimal-compiled-v1.golden.json");
            var golden = JObject.Parse(File.ReadAllText(goldenPath));
            Assert.That(result.Program.Header.CompiledContentHash.HexadecimalValue,
                Is.EqualTo((string)golden["compiledContentHash"]));
            CollectionAssert.AreEqual(
                golden["nodeOrder"].Values<string>(),
                result.Program.DebugMap.Select(item => item.AuthoringNodeId.Value));
            Assert.That(Hex(result.Program.ConfigBlob), Is.EqualTo((string)golden["configHex"]));
            Assert.That(Hex(result.Program.DefaultValueBlob), Is.EqualTo((string)golden["defaultValueHex"]));
        }

        [Test]
        public void Compile_PermutedDocumentStorage_UsesPreorderAndProducesIdenticalProgram()
        {
            var root = Node("root", BuiltInNodeManifests.MemorySequenceTypeId, "second", "first");
            var first = Node("first", ReferenceFixtureNodeManifests.FailureTypeId);
            var second = Node("second", ReferenceFixtureNodeManifests.SuccessTypeId);
            var left = Tree(root, root, first, second);
            var right = Tree(root, second, root, first);
            var registry = RegistryWithFixtures();

            var leftResult = Compile(left, registry, Options());
            var rightResult = Compile(right, registry, Options());

            Assert.That(leftResult.Success, Is.True);
            Assert.That(rightResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "root", "second", "first" },
                leftResult.Program.DebugMap.Select(item => item.AuthoringNodeId.Value));
            CollectionAssert.AreEqual(new uint[] { 1, 2 }, leftResult.Program.ChildIndices);
            Assert.That(rightResult.Program.Header.CompiledContentHash,
                Is.EqualTo(leftResult.Program.Header.CompiledContentHash));
            CollectionAssert.AreEqual(leftResult.Program.ConfigBlob, rightResult.Program.ConfigBlob);
        }

        [Test]
        public void Compile_Repeater_PacksConfigurationAndMemoryLayoutDeterministically()
        {
            var parameters = new SemanticObject(new[]
            {
                new SemanticProperty("stopOnFailure", SemanticValue.FromBoolean(true)),
                new SemanticProperty("count", SemanticValue.FromUInt64(3)),
            });
            var root = new NodeDocument(
                new NodeId("root"),
                BuiltInNodeManifests.RepeaterTypeId,
                1,
                new[] { new NodeId("leaf") },
                parameters,
                tags: TagSet.Empty);
            var tree = Tree(root, root, Node("leaf", ReferenceFixtureNodeManifests.SuccessTypeId));

            var result = Compile(tree, RegistryWithFixtures(), Options());

            Assert.That(result.Success, Is.True, DiagnosticsText(result));
            Assert.That(result.Program.ConfigBlob.Count, Is.EqualTo(8));
            CollectionAssert.AreEqual(new byte[] { 3, 0, 0, 0, 1, 0, 0, 0 }, result.Program.ConfigBlob);
            Assert.That(result.Program.Nodes[0].ConfigOffset, Is.Zero);
            Assert.That(result.Program.Nodes[0].ConfigSize, Is.EqualTo(8));
            Assert.That(result.Program.Nodes[0].ConfigAlignment, Is.EqualTo(4));
            Assert.That(result.Program.Nodes[0].InstanceMemoryOffset, Is.Zero);
            Assert.That(result.Program.Nodes[0].InstanceMemorySize, Is.EqualTo(4));
            Assert.That(result.Program.Nodes[0].InstanceMemoryAlignment, Is.EqualTo(4));
            Assert.That(result.Program.Header.InstanceNodeMemorySize, Is.EqualTo(4));
        }

        [Test]
        public void Compile_BlackboardAndObservers_EmitStableSlotAndWatchTables()
        {
            var keys = new[]
            {
                new BlackboardKeyDefinition(
                    "key.b",
                    "key.b",
                    BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool),
                    defaultValue: BlackboardDefaultValue.Bool(false)),
                new BlackboardKeyDefinition(
                    "key.a",
                    "key.a",
                    BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool),
                    defaultValue: BlackboardDefaultValue.Bool(true)),
            };
            var root = Node("root", BuiltInNodeManifests.ReactiveSelectorTypeId, "condition", "running");
            var condition = new NodeDocument(
                new NodeId("condition"),
                "aibt.core.condition-fixture",
                1,
                parameters: SemanticObject.Empty,
                observer: new NodeObserver("both", new[] { "key.b", "key.a" }),
                tags: TagSet.Empty);
            var running = Node("running", ReferenceFixtureNodeManifests.RunningTypeId);
            var tree = new TreeDocument(
                TreeDocument.CurrentFormat,
                TreeDocument.CurrentFormatVersion,
                new TreeId("tree.fixture"),
                "Fixture",
                root.Id,
                new[] { running, condition, root },
                keys,
                tags: TagSet.Empty,
                metadata: SemanticObject.Empty);

            var registry = RegistryWithFixtures(new[]
            {
                Manifest("aibt.core.condition-fixture", kind: NodeBehaviorKind.Condition),
            });
            var result = Compile(tree, registry, Options());

            Assert.That(result.Success, Is.True, DiagnosticsText(result));
            Assert.That(result.Program.BlackboardSlots, Has.Count.EqualTo(2));
            Assert.That(result.Program.BlackboardSlots[0].StableKeyId, Is.EqualTo(StableHash.Fnv1A64("key.a")));
            Assert.That(result.Program.BlackboardSlots[1].StableKeyId, Is.EqualTo(StableHash.Fnv1A64("key.b")));
            Assert.That(result.Program.BlackboardSlots.All(slot =>
                (slot.AccessFlags & CompiledBlackboardAccessFlags.Observed) != 0), Is.True);
            Assert.That(result.Program.DefaultValueBlob[0], Is.EqualTo(1));
            Assert.That(result.Program.DefaultValueBlob[1], Is.EqualTo(0));
            Assert.That(result.Program.Observers, Has.Count.EqualTo(1));
            Assert.That(result.Program.Observers[0].ObserverNodeIndex, Is.EqualTo(1));
            Assert.That(result.Program.Observers[0].OwningReactiveCompositeIndex, Is.EqualTo(0));

            var expectedWatchOrder = new[] { "key.a", "key.b" }
                .OrderBy(StableHash.Fnv1A64)
                .Select(id => id == "key.a" ? 0u : 1u)
                .ToArray();
            CollectionAssert.AreEqual(expectedWatchOrder, result.Program.WatchedSlotIndices);
        }

        [Test]
        public void Compile_ManifestAccesses_EmitReadWriteRangesAndFlags()
        {
            var manifest = Manifest(
                "aibt.core.access-fixture",
                reads: new[] { "read.key" },
                writes: new[] { "write.key" });
            var registry = Registry(manifest, NodeManifestSource.BuiltIn, true);
            var root = Node("root", manifest.TypeId);
            var tree = new TreeDocument(
                TreeDocument.CurrentFormat,
                TreeDocument.CurrentFormatVersion,
                new TreeId("tree.fixture"),
                "Fixture",
                root.Id,
                new[] { root },
                new[]
                {
                    new BlackboardKeyDefinition("write.key", "write.key", BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32)),
                    new BlackboardKeyDefinition("read.key", "read.key", BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32)),
                },
                tags: TagSet.Empty,
                metadata: SemanticObject.Empty);

            var result = Compile(tree, registry, Options());

            Assert.That(result.Success, Is.True, DiagnosticsText(result));
            CollectionAssert.AreEqual(new uint[] { 0 }, result.Program.ReadSlotIndices);
            CollectionAssert.AreEqual(new uint[] { 1 }, result.Program.WriteSlotIndices);
            Assert.That(result.Program.Nodes[0].ReadSlots, Is.EqualTo(new CompiledRange(0, 1)));
            Assert.That(result.Program.Nodes[0].WriteSlots, Is.EqualTo(new CompiledRange(0, 1)));
            Assert.That(result.Program.BlackboardSlots[0].AccessFlags, Is.EqualTo(CompiledBlackboardAccessFlags.Read));
            Assert.That(result.Program.BlackboardSlots[1].AccessFlags, Is.EqualTo(CompiledBlackboardAccessFlags.Write));
        }

        [Test]
        public void Compile_AgentAndSharedScopeBlackboard_RequiresPolicyOptIn()
        {
            var keys = new[]
            {
                new BlackboardKeyDefinition(
                    "agent.key",
                    "agent.key",
                    BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool),
                    BlackboardScope.Agent,
                    defaultValue: BlackboardDefaultValue.Bool(false)),
                new BlackboardKeyDefinition(
                    "shared.key",
                    "shared.key",
                    BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32),
                    BlackboardScope.Shared,
                    defaultValue: BlackboardDefaultValue.Int32(0)),
            };
            var root = Node("root", ReferenceFixtureNodeManifests.SuccessTypeId);
            var tree = TreeDocument.CreateVersion2(
                new TreeId("tree.fixture"),
                "Fixture",
                root.Id,
                new[] { root },
                agentContract: new BlackboardScopeContract("contract.agent", 1),
                sharedContract: new BlackboardScopeContract("contract.shared", 1),
                blackboard: keys,
                tags: TagSet.Empty,
                metadata: SemanticObject.Empty);
            var registry = RegistryWithFixtures();

            // TreeValidator.Validate runs first inside ReferenceCompiler.Compile (using the same
            // policy-derived ValidationOptions) and already rejects Agent/Shared scope via its own,
            // pre-existing TreeValidationDiagnosticCodes.UnsupportedBlackboardScope (AIBT2030) --
            // this card's own BuildBlackboardSlots fix (ReferenceCompilerDiagnosticCodes
            // .UnsupportedCapability, AIBT3012) is reached only once validation already passes with
            // a real opt-in policy, proven below.
            var rejected = Compile(tree, registry, Options());
            Assert.That(rejected.Success, Is.False, DiagnosticsText(rejected));
            Assert.That(rejected.Diagnostics.Any(item => item.Code == TreeValidationDiagnosticCodes.UnsupportedBlackboardScope), Is.True, DiagnosticsText(rejected));

            var accepted = Compile(tree, registry, Options(policy: new ReferenceCompilationPolicy(supportsAgentScope: true, supportsSharedScope: true)));
            Assert.That(accepted.Success, Is.True, DiagnosticsText(accepted));
            Assert.That(accepted.Program.BlackboardSlots, Has.Count.EqualTo(2));
            Assert.That(accepted.Program.BlackboardSlots.Select(slot => slot.Scope),
                Is.EquivalentTo(new[] { BlackboardScope.Agent, BlackboardScope.Shared }));

            // ReferenceCompilationPolicy.Phase1's own shared default is untouched by this feature --
            // every other Phase1-using call site keeps rejecting Agent/Shared exactly as before.
            var phase1 = Compile(tree, registry, Options(policy: ReferenceCompilationPolicy.Phase1));
            Assert.That(phase1.Success, Is.False);
            Assert.That(ReferenceCompilationPolicy.Phase1.SupportsAgentScope, Is.False);
            Assert.That(ReferenceCompilationPolicy.Phase1.SupportsSharedScope, Is.False);
        }

        [Test]
        public void Compile_AgentScopeAllowedButSharedRejected_OnlyAgentSlotCompiles()
        {
            var keys = new[]
            {
                new BlackboardKeyDefinition(
                    "agent.only",
                    "agent.only",
                    BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool),
                    BlackboardScope.Agent,
                    defaultValue: BlackboardDefaultValue.Bool(false)),
            };
            var root = Node("root", ReferenceFixtureNodeManifests.SuccessTypeId);
            var tree = TreeDocument.CreateVersion2(
                new TreeId("tree.fixture"),
                "Fixture",
                root.Id,
                new[] { root },
                agentContract: new BlackboardScopeContract("contract.agent", 1),
                sharedContract: null,
                blackboard: keys,
                tags: TagSet.Empty,
                metadata: SemanticObject.Empty);
            var registry = RegistryWithFixtures();

            var result = Compile(tree, registry, Options(policy: new ReferenceCompilationPolicy(supportsAgentScope: true, supportsSharedScope: false)));

            Assert.That(result.Success, Is.True, DiagnosticsText(result));
            Assert.That(result.Program.BlackboardSlots, Has.Count.EqualTo(1));
            Assert.That(result.Program.BlackboardSlots[0].Scope, Is.EqualTo(BlackboardScope.Agent));
        }

        [Test]
        public void Compile_InvalidSourceOrMissingTypedPolicy_ReturnsDiagnosticsWithoutProgram()
        {
            var registry = RegistryWithFixtures();
            var invalidSource = Compile(MinimalTree(), registry, Options(sourceId: "C:\\machine\\tree.json"));
            var invalidPolicy = Compile(
                MinimalTree(),
                registry,
                new ReferenceCompilerOptions(
                    "trees/minimal.aibt.json",
                    null,
                    CompilerVersion));

            Assert.That(invalidSource.Success, Is.False);
            Assert.That(invalidSource.Program, Is.Null);
            Assert.That(invalidSource.Diagnostics.Any(item => item.Code == ReferenceCompilerDiagnosticCodes.InvalidOptions), Is.True);
            Assert.That(invalidPolicy.Success, Is.False);
            Assert.That(invalidPolicy.Program, Is.Null);
            Assert.That(invalidPolicy.Diagnostics.Any(item => item.Code == ReferenceCompilerDiagnosticCodes.InvalidOptions), Is.True);
        }

        [Test]
        public void PolicyCodec_RejectsNonCanonicalOrMismatchedBytes()
        {
            var policy = ReferenceCompilationPolicy.Phase1;
            var canonical = policy.ToCanonicalUtf8();
            var nonCanonical = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonical).Replace("  ", "    "));
            var different = new ReferenceCompilationPolicy(allowSideEffects: false);

            Assert.That(ReferenceCompilationPolicyCodec.IsExactCanonicalEncoding(policy, canonical), Is.True);
            Assert.That(ReferenceCompilationPolicyCodec.IsExactCanonicalEncoding(policy, nonCanonical), Is.False);
            Assert.That(ReferenceCompilationPolicyCodec.IsExactCanonicalEncoding(different, canonical), Is.False);
            Assert.That(policy.ComputeHash(), Is.EqualTo(new CompiledHash(StableHash.Sha256Hex(canonical))));
        }

        [Test]
        public void PolicyCodec_FullPolicy_MatchesExactGoldenBytes()
        {
            var policy = new ReferenceCompilationPolicy(
                maxTreeDepth: 64,
                maxNodesPerTree: 4096,
                allowManagedNodes: true,
                allowMainThreadNodes: true,
                requireTreeDescription: true,
                requireNodeDescriptions: true,
                blackboardNaming: BlackboardNamingPolicy.SnakeCase,
                forbiddenNodeTypes: new[] { "project.ai.forbidden" },
                requireDeterministicNodes: false,
                allowSideEffects: false,
                unreachableNodes: UnreachableNodePolicy.Warning,
                supportsAgentScope: true,
                supportsSharedScope: true,
                warningsAsErrors: new[] { new DiagnosticCode("AIBT2020") },
                maxEstimatedCost: 42.5,
                forbidUnboundedRepeaters: true,
                requireEventDrivenServices: true);
            var expected = "{\n"
                + "  \"format\": \"aibt.policy\",\n"
                + "  \"formatVersion\": 1,\n"
                + "  \"maxTreeDepth\": 64,\n"
                + "  \"maxNodesPerTree\": 4096,\n"
                + "  \"allowManagedNodes\": true,\n"
                + "  \"allowMainThreadNodes\": true,\n"
                + "  \"requireTreeDescription\": true,\n"
                + "  \"requireNodeDescriptions\": true,\n"
                + "  \"blackboardNaming\": \"snake_case\",\n"
                + "  \"requireDeterministicNodes\": false,\n"
                + "  \"allowSideEffects\": false,\n"
                + "  \"unreachableNodes\": \"warning\",\n"
                + "  \"supportsAgentScope\": true,\n"
                + "  \"supportsSharedScope\": true,\n"
                + "  \"forbiddenNodeTypes\": [\"project.ai.forbidden\"],\n"
                + "  \"warningsAsErrors\": [\"AIBT2020\"],\n"
                + "  \"performance\": {\n"
                + "    \"maxEstimatedCost\": 42.5,\n"
                + "    \"forbidUnboundedRepeaters\": true,\n"
                + "    \"requireEventDrivenServices\": true\n"
                + "  }\n"
                + "}\n";

            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(expected), policy.ToCanonicalUtf8());
        }

        [Test]
        public void Compile_PolicyAndCompilerVersionsAffectContentHash()
        {
            var registry = RegistryWithFixtures();
            var baseline = Compile(MinimalTree(), registry, Options());
            var policyChanged = Compile(
                MinimalTree(),
                registry,
                Options(policy: new ReferenceCompilationPolicy(allowSideEffects: false)));
            var compilerChanged = Compile(
                MinimalTree(),
                registry,
                Options(compilerVersion: new CompiledCompilerVersion(1, 0, 1, 0)));

            Assert.That(policyChanged.Success, Is.True);
            Assert.That(compilerChanged.Success, Is.True);
            Assert.That(policyChanged.Program.Header.CompiledContentHash,
                Is.Not.EqualTo(baseline.Program.Header.CompiledContentHash));
            Assert.That(compilerChanged.Program.Header.CompiledContentHash,
                Is.Not.EqualTo(baseline.Program.Header.CompiledContentHash));
        }

        [Test]
        public void Compile_NodeTypeVersionAffectsSemanticRegistryAndContentHashes()
        {
            var v1 = Manifest("aibt.core.versioned-fixture", version: 1);
            var v2 = Manifest("aibt.core.versioned-fixture", version: 2);
            var first = Compile(MinimalTree(v1.TypeId, 1), Registry(v1, NodeManifestSource.BuiltIn, true), Options());
            var second = Compile(MinimalTree(v2.TypeId, 2), Registry(v2, NodeManifestSource.BuiltIn, true), Options());

            Assert.That(first.Success, Is.True, DiagnosticsText(first));
            Assert.That(second.Success, Is.True, DiagnosticsText(second));
            Assert.That(second.Program.Header.CanonicalSemanticHash, Is.Not.EqualTo(first.Program.Header.CanonicalSemanticHash));
            Assert.That(second.Program.Header.NodeRegistryHash, Is.Not.EqualTo(first.Program.Header.NodeRegistryHash));
            Assert.That(second.Program.Header.CompiledContentHash, Is.Not.EqualTo(first.Program.Header.CompiledContentHash));
        }

        [Test]
        public void Compile_ConfigAlignmentAndMemoryLifetimeAffectRecordsAndContentHash()
        {
            var config1 = Manifest("aibt.core.layout-fixture", configurationSize: 4, configurationAlignment: 1);
            var config4 = Manifest("aibt.core.layout-fixture", configurationSize: 4, configurationAlignment: 4);
            var first = Compile(MinimalTree(config1.TypeId), Registry(config1, NodeManifestSource.BuiltIn, true), Options());
            var second = Compile(MinimalTree(config4.TypeId), Registry(config4, NodeManifestSource.BuiltIn, true), Options());
            Assert.That(first.Program.Nodes[0].ConfigAlignment, Is.EqualTo(1));
            Assert.That(second.Program.Nodes[0].ConfigAlignment, Is.EqualTo(4));
            Assert.That(second.Program.Header.CompiledContentHash, Is.Not.EqualTo(first.Program.Header.CompiledContentHash));

            var activation = Manifest("aibt.core.memory-fixture", memorySize: 4, memoryLifetime: NodeMemoryLifetime.Activation);
            var instance = Manifest("aibt.core.memory-fixture", memorySize: 4, memoryLifetime: NodeMemoryLifetime.Instance);
            var activationResult = Compile(MinimalTree(activation.TypeId), Registry(activation, NodeManifestSource.BuiltIn, true), Options());
            var instanceResult = Compile(MinimalTree(instance.TypeId), Registry(instance, NodeManifestSource.BuiltIn, true), Options());
            Assert.That(activationResult.Program.Nodes[0].MemoryLifetime, Is.EqualTo(NodeMemoryLifetime.Activation));
            Assert.That(instanceResult.Program.Nodes[0].MemoryLifetime, Is.EqualTo(NodeMemoryLifetime.Instance));
            Assert.That(instanceResult.Program.Header.CompiledContentHash,
                Is.Not.EqualTo(activationResult.Program.Header.CompiledContentHash));
        }

        [Test]
        public void Compile_EnumContractIsCompiledIntoSlotDefaultAndHashes()
        {
            var root = Node("root", ReferenceFixtureNodeManifests.SuccessTypeId);
            TreeDocument EnumTree(string contract, bool includeDefault = true)
            {
                return new TreeDocument(
                    TreeDocument.CurrentFormat,
                    TreeDocument.CurrentFormatVersion,
                    new TreeId("tree.enum"),
                    "Enum",
                    root.Id,
                    new[] { root },
                    new[]
                    {
                        new BlackboardKeyDefinition(
                            "state",
                            "state",
                            BlackboardTypeReference.Enum32(contract),
                            defaultValue: includeDefault ? BlackboardDefaultValue.Enum32(contract, 3) : null),
                    },
                    tags: TagSet.Empty,
                    metadata: SemanticObject.Empty);
            }

            var first = Compile(EnumTree("game.state"), RegistryWithFixtures(), Options());
            var second = Compile(EnumTree("game.other-state"), RegistryWithFixtures(), Options());
            var implicitDefault = Compile(EnumTree("game.state", includeDefault: false), RegistryWithFixtures(), Options());
            var expectedId = StableHash.Fnv1A64("game.state");

            Assert.That(first.Success, Is.True, DiagnosticsText(first));
            Assert.That(second.Success, Is.True, DiagnosticsText(second));
            Assert.That(implicitDefault.Success, Is.True, DiagnosticsText(implicitDefault));
            Assert.That(first.Program.BlackboardSlots.Single().EnumContractId, Is.EqualTo(expectedId));
            Assert.That(ReadUInt64(first.Program.DefaultValueBlob, 0), Is.EqualTo(expectedId));
            Assert.That(ReadUInt64(implicitDefault.Program.DefaultValueBlob, 0), Is.EqualTo(expectedId));
            Assert.That(second.Program.Header.CanonicalSemanticHash,
                Is.Not.EqualTo(first.Program.Header.CanonicalSemanticHash));
            Assert.That(second.Program.Header.CompiledContentHash,
                Is.Not.EqualTo(first.Program.Header.CompiledContentHash));
        }

        [Test]
        public void Compile_InjectedBlackboardHashCollision_FailsDeterministically()
        {
            var root = Node("root", ReferenceFixtureNodeManifests.SuccessTypeId);
            var tree = new TreeDocument(
                TreeDocument.CurrentFormat,
                TreeDocument.CurrentFormatVersion,
                new TreeId("tree.fixture"),
                "Fixture",
                root.Id,
                new[] { root },
                new[]
                {
                    new BlackboardKeyDefinition("key.a", "key.a", BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool)),
                    new BlackboardKeyDefinition("key.b", "key.b", BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool)),
                },
                tags: TagSet.Empty,
                metadata: SemanticObject.Empty);
            var options = new ReferenceCompilerOptions(
                "trees/minimal.aibt.json",
                ReferenceCompilationPolicy.Phase1,
                CompilerVersion,
                false,
                _ => 42,
                null,
                uint.MaxValue);

            var first = Compile(tree, RegistryWithFixtures(), options);
            var second = Compile(tree, RegistryWithFixtures(), options);
            Assert.That(first.Success, Is.False);
            Assert.That(first.Diagnostics[0].Code, Is.EqualTo(ReferenceCompilerDiagnosticCodes.StableIdentityCollision));
            Assert.That(second.Diagnostics[0], Is.EqualTo(first.Diagnostics[0]));
        }

        [Test]
        public void Compile_MemoryBlackboardAndTableOverflow_FailBeforeLargeAllocation()
        {
            var memory = Manifest("aibt.core.memory-overflow", memorySize: uint.MaxValue);
            var memoryResult = Compile(MinimalTree(memory.TypeId), Registry(memory, NodeManifestSource.BuiltIn, true), Options());
            Assert.That(memoryResult.Diagnostics.Any(item => item.Code == ReferenceCompilerDiagnosticCodes.LayoutOverflow),
                Is.True, DiagnosticsText(memoryResult));

            var root = Node("root", ReferenceFixtureNodeManifests.SuccessTypeId);
            var blackboardTree = new TreeDocument(
                TreeDocument.CurrentFormat, 1, new TreeId("tree.fixture"), "Fixture", root.Id, new[] { root },
                new[]
                {
                    new BlackboardKeyDefinition("key.a", "key.a", BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool)),
                    new BlackboardKeyDefinition("key.b", "key.b", BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool)),
                }, tags: TagSet.Empty, metadata: SemanticObject.Empty);
            var blackboardOverflow = new ReferenceCompilerOptions(
                "trees/minimal.aibt.json", ReferenceCompilationPolicy.Phase1, CompilerVersion,
                false, StableHash.Fnv1A64,
                descriptor => new BlackboardTypeDescriptor(
                    descriptor.ValueType, descriptor.TypeId, descriptor.Version, int.MaxValue, 1),
                uint.MaxValue);
            var blackboardResult = Compile(blackboardTree, RegistryWithFixtures(), blackboardOverflow);
            Assert.That(blackboardResult.Diagnostics.Any(item => item.Code == ReferenceCompilerDiagnosticCodes.LayoutOverflow),
                Is.True, DiagnosticsText(blackboardResult));

            var composite = Node("root", BuiltInNodeManifests.MemorySequenceTypeId, "a", "b");
            var tableTree = Tree(composite, composite,
                Node("a", ReferenceFixtureNodeManifests.SuccessTypeId),
                Node("b", ReferenceFixtureNodeManifests.SuccessTypeId));
            var limited = new ReferenceCompilerOptions(
                "trees/minimal.aibt.json", ReferenceCompilationPolicy.Phase1, CompilerVersion,
                false, StableHash.Fnv1A64, null, 2);
            var tableResult = Compile(tableTree, RegistryWithFixtures(), limited);
            Assert.That(tableResult.Diagnostics.Any(item => item.Code == ReferenceCompilerDiagnosticCodes.LayoutOverflow),
                Is.True, DiagnosticsText(tableResult));
        }

        [Test]
        public void Compile_OversizedConfiguration_FailsBeforeBlobAllocation()
        {
            var manifest = Manifest("aibt.core.oversized-fixture", configurationSize: uint.MaxValue);
            var result = Compile(MinimalTree(manifest.TypeId), Registry(manifest, NodeManifestSource.BuiltIn, true), Options());

            Assert.That(result.Success, Is.False);
            Assert.That(result.Program, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == ReferenceCompilerDiagnosticCodes.LayoutOverflow), Is.True);
        }

        [Test]
        public void Compile_UnboundExtension_FailsAsUnsupportedCapability()
        {
            var manifest = Manifest("project.ai.unbound");
            var result = Compile(MinimalTree(manifest.TypeId), Registry(manifest, NodeManifestSource.UserExtension, false), Options());

            Assert.That(result.Success, Is.False);
            Assert.That(result.Program, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == ReferenceCompilerDiagnosticCodes.UnsupportedCapability), Is.True);
        }

        private static readonly CompiledCompilerVersion CompilerVersion = new CompiledCompilerVersion(1, 0, 0, 0);

        private static ReferenceCompilationResult Compile(
            TreeDocument document,
            AuthoringNodeRegistry registry,
            ReferenceCompilerOptions options)
            => ReferenceCompiler.Compile(document, registry, options);

        private static string DiagnosticsText(ReferenceCompilationResult result)
            => string.Join(" | ", result.Diagnostics.Select(item => item.Code + ": " + item.Message));

        private static string Hex(IEnumerable<byte> bytes)
        {
            var builder = new StringBuilder();
            foreach (var value in bytes)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }

        private static ulong ReadUInt64(IReadOnlyList<byte> bytes, int offset)
        {
            ulong value = 0;
            for (var index = 0; index < 8; index++) value |= (ulong)bytes[offset + index] << (index * 8);
            return value;
        }

        private static ReferenceCompilerOptions Options(
            string sourceId = "trees/minimal.aibt.json",
            ReferenceCompilationPolicy policy = null,
            CompiledCompilerVersion? compilerVersion = null)
        {
            return new ReferenceCompilerOptions(
                sourceId,
                policy ?? ReferenceCompilationPolicy.Phase1,
                compilerVersion ?? CompilerVersion);
        }

        private static TreeDocument MinimalTree(string typeId = null, int version = 1)
        {
            var root = Node("root", typeId ?? ReferenceFixtureNodeManifests.SuccessTypeId, version);
            return Tree(root, root);
        }

        private static TreeDocument Tree(NodeDocument root, params NodeDocument[] nodes)
        {
            return new TreeDocument(
                TreeDocument.CurrentFormat,
                TreeDocument.CurrentFormatVersion,
                new TreeId("tree.fixture"),
                "Fixture",
                root.Id,
                nodes,
                tags: TagSet.Empty,
                metadata: SemanticObject.Empty);
        }

        private static NodeDocument Node(string id, string typeId, params string[] children)
            => Node(id, typeId, 1, children);

        private static NodeDocument Node(string id, string typeId, int version, params string[] children)
        {
            return new NodeDocument(
                new NodeId(id),
                typeId,
                version,
                children.Select(child => new NodeId(child)),
                parameters: SemanticObject.Empty,
                tags: TagSet.Empty);
        }

        private static AuthoringNodeRegistry RegistryWithFixtures(IEnumerable<NodeManifest> additional = null)
        {
            var builder = NodeRegistryBuilder.CreateWithBuiltIns().AddTestFixtures();
            foreach (var manifest in additional ?? Array.Empty<NodeManifest>())
            {
                builder.AddBuiltInForTest(
                    manifest,
                    new NodeHandlerBindingContract("aibt.reference.test-handler", manifest.Version, manifest.ExecutionDomain));
            }
            var result = builder.Build();
            Assert.That(result.Success, Is.True, RegistryDiagnosticsText(result));
            return result.Registry;
        }

        private static AuthoringNodeRegistry Registry(NodeManifest manifest, NodeManifestSource source, bool bind)
        {
            NodeRegistryBuilder builder;
            if (source == NodeManifestSource.UserExtension)
            {
                builder = new NodeRegistryBuilder().AddUserExtension(manifest);
            }
            else
            {
                builder = new NodeRegistryBuilder().AddBuiltInForTest(
                    manifest,
                    bind ? new NodeHandlerBindingContract("aibt.reference.test-handler", manifest.Version, manifest.ExecutionDomain) : null);
            }

            var result = builder.Build();
            Assert.That(result.Success, Is.True, RegistryDiagnosticsText(result));
            return result.Registry;
        }

        private static string RegistryDiagnosticsText(NodeRegistryBuildResult result)
            => string.Join(" | ", result.Diagnostics.Select(item => item.Code + ": " + item.Message));

        private static NodeManifest Manifest(
            string typeId,
            IEnumerable<string> reads = null,
            IEnumerable<string> writes = null,
            uint configurationSize = 0,
            NodeBehaviorKind kind = NodeBehaviorKind.Action,
            uint version = 1,
            byte configurationAlignment = 1,
            uint memorySize = 0,
            NodeMemoryLifetime memoryLifetime = NodeMemoryLifetime.Activation)
        {
            return new NodeManifest(
                typeId,
                version,
                "Compiler fixture.",
                "Test fixture",
                kind,
                "Use in compiler tests.",
                "Do not use in production.",
                NodeExecutionDomain.Burst,
                true,
                Array.Empty<NodeParameterContract>(),
                new NodeChildPolicy(0, 0, true),
                reads ?? Array.Empty<string>(),
                writes ?? Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { NodeStatus.Success },
                new NodeMemoryDescriptor(memorySize, 1, memoryLifetime),
                new NodeConfigurationDescriptor(configurationSize, configurationAlignment, Array.Empty<NodeConfigurationField>()),
                NodeCancellationMode.AbortOnly,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Success", "{}", "Returns success.") });
        }
    }
}
