using System.Linq;
using AIBT.Authoring;
using AIBT.Authoring.Migration;
using NUnit.Framework;
using AuthoringNodeRegistry = AIBT.Authoring.NodeRegistry;

namespace AIBT.Tests.Editor.Migration
{
    /// <summary>
    /// P7-006 (ADR-P7-005): proves <see cref="DocumentMigrator"/> against real
    /// <see cref="TreeDocument"/>/<see cref="NodeDocument"/> objects and the real, accepted
    /// <see cref="ReferenceCompiler"/> -- never a synthetic in-memory-only object, per this card's
    /// own acceptance criterion. Fixture construction mirrors the already-archived
    /// <c>Spikes~/MigrationToolingDecision/SpikeMigrationTooling.cs</c> exactly.
    /// </summary>
    public sealed class DocumentMigratorTests
    {
        private const string TypeId = "aibt.core.migration-test-node";

        [TestCase(1)]
        [TestCase(2)]
        public void Migration_PreservesDocumentAndNodeFields_WithoutChangingSource(int formatVersion)
        {
            var original = AuthoredDocument(1, ("moveSpeed", SemanticValue.FromUInt64(10)));
            var bindings = new NodeBindingMap(new[] { new System.Collections.Generic.KeyValuePair<string, string>("target", "value") });
            var leaf = new NodeDocument(original.Root, TypeId, 1, null, original.Nodes[0].Parameters,
                null, "Display", "Node description", TagSet.Empty, formatVersion == 2 ? bindings : null);
            var keys = new System.Collections.Generic.List<BlackboardKeyDefinition> { new BlackboardKeyDefinition("value", "value",
                BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool), defaultValue: BlackboardDefaultValue.Bool(true)) };
            if (formatVersion == 2)
            {
                keys.Add(new BlackboardKeyDefinition("agent.value", "agent.value", BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool),
                    BlackboardScope.Agent, BlackboardDefaultValue.Bool(false)));
                keys.Add(new BlackboardKeyDefinition("shared.value", "shared.value", BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool),
                    BlackboardScope.Shared, BlackboardDefaultValue.Bool(true)));
            }
            var source = new TreeDocument(original.Format, formatVersion, original.TreeId, "Rich document", original.Root,
                new[] { leaf }, keys, "Tree description", TagSet.Empty, SemanticObject.Empty, new Revision(17),
                formatVersion == 2 ? new BlackboardScopeContract("agent.contract", 2) : null,
                formatVersion == 2 ? new BlackboardScopeContract("shared.contract", 3) : null);
            var serialized = CanonicalTreeJson.Serialize(source);
            Assert.That(serialized.Success, Is.True, string.Join(" | ", serialized.Diagnostics.Select(d => d.Message)));
            var before = serialized.Utf8;
            var rules = NodeMigrationRegistry.Empty.WithRule(new NodeMigrationRule(TypeId, 1,
                renames: new[] { new NodeFieldRename("moveSpeed", "speed") },
                additions: new[] { new NodeFieldAddition("acceleration", SemanticValue.FromUInt64(5)) }));
            var migrated = DocumentMigrator.TryMigrate(source, RegistryWithManifest(ManifestVersion(2)), rules, out _);
            Assert.That(migrated.Blackboard, Is.EqualTo(source.Blackboard));
            Assert.That(migrated.Description, Is.EqualTo(source.Description));
            Assert.That(migrated.Revision, Is.EqualTo(source.Revision));
            Assert.That(migrated.AgentContract, Is.SameAs(source.AgentContract));
            Assert.That(migrated.SharedContract, Is.SameAs(source.SharedContract));
            Assert.That(migrated.Nodes[0].Bindings, Is.SameAs(leaf.Bindings));
            Assert.That(migrated.Nodes[0].DisplayName, Is.EqualTo(leaf.DisplayName));
            Assert.That(migrated.Nodes[0].Description, Is.EqualTo(leaf.Description));
            Assert.That(migrated.Nodes[0].TypeVersion, Is.EqualTo(2));
            var expectedNode = new NodeDocument(leaf.Id, leaf.TypeId, 2, leaf.Children, migrated.Nodes[0].Parameters,
                leaf.Observer, leaf.DisplayName, leaf.Description, leaf.Tags, leaf.Bindings);
            var expected = new TreeDocument(source.Format, source.FormatVersion, source.TreeId, source.Name, source.Root,
                new[] { expectedNode }, source.Blackboard, source.Description, source.Tags, source.Metadata, source.Revision,
                source.AgentContract, source.SharedContract);
            Assert.That(CanonicalTreeJson.Serialize(migrated).Utf8, Is.EqualTo(CanonicalTreeJson.Serialize(expected).Utf8));
            Assert.That(CanonicalTreeJson.Serialize(source).Utf8, Is.EqualTo(before));
            Assert.That(CanonicalTreeJson.Parse(CanonicalTreeJson.Serialize(migrated).Utf8).Success, Is.True);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void NoApplicableRule_ReturnsTheOriginalDocument(int version)
        {
            var source = AuthoredDocument(version, ("speed", SemanticValue.FromUInt64(10)));
            var migrated = DocumentMigrator.TryMigrate(source, RegistryWithManifest(ManifestVersion(2)),
                NodeMigrationRegistry.Empty, out var outcomes);
            Assert.That(migrated, Is.SameAs(source));
            Assert.That(outcomes, Is.Empty);
        }

        [Test]
        public void SingleHopMigration_RenamesAndAddsField_AgainstRealDocumentAndCompiler()
        {
            var registry = RegistryWithManifest(ManifestVersion(2));
            var rules = NodeMigrationRegistry.Empty.WithRule(new NodeMigrationRule(
                TypeId, sourceVersion: 1,
                renames: new[] { new NodeFieldRename("moveSpeed", "speed") },
                additions: new[] { new NodeFieldAddition("acceleration", SemanticValue.FromUInt64(5)) }));
            var document = AuthoredDocument(version: 1, ("moveSpeed", SemanticValue.FromUInt64(10)));

            var migrated = DocumentMigrator.TryMigrate(document, registry, rules, out var outcomes);

            Assert.That(outcomes, Has.Count.EqualTo(1));
            var outcome = outcomes[0];
            Assert.That(outcome.FromVersion, Is.EqualTo(1u));
            Assert.That(outcome.ToVersion, Is.EqualTo(2u));
            Assert.That(outcome.Changes.Select(c => c.Description), Is.EquivalentTo(new[]
            {
                "field 'moveSpeed' renamed to 'speed'",
                "field 'acceleration' added, default 5",
            }));

            var node = migrated.Nodes.Single(n => n.TypeId == TypeId);
            Assert.That(node.TypeVersion, Is.EqualTo(2));
            Assert.That(node.Parameters.TryGetValue("speed", out _), Is.True);
            Assert.That(node.Parameters.TryGetValue("moveSpeed", out _), Is.False);
            Assert.That(node.Parameters.TryGetValue("acceleration", out _), Is.True);

            var options = new ReferenceCompilerOptions("tests/migration.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 1));
            var result = ReferenceCompiler.Compile(migrated, registry, options);
            Assert.That(result.Success, Is.True,
                string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        }

        [Test]
        public void ChainedTwoHopMigration_AppliesBothRulesInOrder_NeverSkipsAhead()
        {
            var registry = RegistryWithManifest(ManifestVersion(3));
            var rules = NodeMigrationRegistry.Empty
                .WithRule(new NodeMigrationRule(TypeId, sourceVersion: 1,
                    renames: new[] { new NodeFieldRename("moveSpeed", "speed") }))
                .WithRule(new NodeMigrationRule(TypeId, sourceVersion: 2,
                    additions: new[] { new NodeFieldAddition("acceleration", SemanticValue.FromUInt64(5)) }));
            var document = AuthoredDocument(version: 1, ("moveSpeed", SemanticValue.FromUInt64(10)));

            var migrated = DocumentMigrator.TryMigrate(document, registry, rules, out var outcomes);

            Assert.That(outcomes, Has.Count.EqualTo(1));
            var outcome = outcomes[0];
            Assert.That(outcome.FromVersion, Is.EqualTo(1u));
            Assert.That(outcome.ToVersion, Is.EqualTo(3u));
            Assert.That(outcome.Changes.Select(c => c.Description), Is.EqualTo(new[]
            {
                "field 'moveSpeed' renamed to 'speed'",
                "field 'acceleration' added, default 5",
            }), "Hops must apply in order: rename at v1->v2, then addition at v2->v3.");

            var node = migrated.Nodes.Single(n => n.TypeId == TypeId);
            Assert.That(node.TypeVersion, Is.EqualTo(3));
        }

        [Test]
        public void UnregisteredGap_LeavesNodeUntouched_ExistingValidatorStillHardFails()
        {
            // v3 removes "moveSpeed"/"speed" entirely relative to v1 -- no rule registered for the
            // v1->v2 hop at all. The node must be left completely untouched, never partially
            // migrated, and TreeValidator's own existing UnsupportedNodeVersion check must still
            // fire exactly as it does today.
            var registry = RegistryWithManifest(ManifestVersionNoParameters(3));
            var document = AuthoredDocument(version: 1, ("moveSpeed", SemanticValue.FromUInt64(10)));

            var migrated = DocumentMigrator.TryMigrate(document, registry, NodeMigrationRegistry.Empty, out var outcomes);

            Assert.That(outcomes, Is.Empty);
            var node = migrated.Nodes.Single(n => n.TypeId == TypeId);
            Assert.That(node.TypeVersion, Is.EqualTo(1), "An unmigratable node must be left at its original version.");
            Assert.That(node.Parameters.TryGetValue("moveSpeed", out _), Is.True);

            var options = new ValidationOptions("tests/migration-negative.aibt.json");
            var diagnostics = TreeValidator.Validate(migrated, registry, options);
            Assert.That(diagnostics.Select(d => d.Code.Value),
                Has.Some.EqualTo(TreeValidationDiagnosticCodes.UnsupportedNodeVersion.Value));
        }

        private static TreeDocument AuthoredDocument(int version, params (string Name, SemanticValue Value)[] parameters)
        {
            var root = new NodeDocument(
                new NodeId("root"), TypeId, version, children: null,
                parameters: new SemanticObject(parameters.Select(p => new SemanticProperty(p.Name, p.Value))),
                tags: TagSet.Empty);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.migration-test"), "Migration test", root.Id, new[] { root },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static AuthoringNodeRegistry RegistryWithManifest(NodeManifest manifest)
        {
            var builder = NodeRegistryBuilder.CreateWithBuiltIns();
            builder.AddBuiltInForTest(manifest, new NodeHandlerBindingContract(
                "aibt.reference.core.migration-test-node", manifest.Version, manifest.ExecutionDomain));
            var result = builder.Build();
            Assert.That(result.Success, Is.True, string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            return result.Registry;
        }

        private static NodeManifest ManifestVersion(uint version)
        {
            var parameters = new[]
            {
                new NodeParameterContract("speed", NodeParameterType.UInt32, true),
                new NodeParameterContract("acceleration", NodeParameterType.UInt32, true),
            };
            var configuration = new NodeConfigurationDescriptor(8, 4, new[]
            {
                new NodeConfigurationField("speed", 0, 4, 4),
                new NodeConfigurationField("acceleration", 4, 4, 4),
            });
            return BuildManifest(version, parameters, configuration);
        }

        private static NodeManifest ManifestVersionNoParameters(uint version)
        {
            return BuildManifest(version, System.Array.Empty<NodeParameterContract>(),
                new NodeConfigurationDescriptor(1, 1, System.Array.Empty<NodeConfigurationField>()));
        }

        private static NodeManifest BuildManifest(uint version, NodeParameterContract[] parameters, NodeConfigurationDescriptor configuration)
        {
            var childPolicy = new NodeChildPolicy(0, 0, true);
            return new NodeManifest(
                TypeId,
                version,
                "P7-006 test fixture node.",
                "Test",
                NodeBehaviorKind.Action,
                "Used only by DocumentMigratorTests.",
                "Do not use in production.",
                NodeExecutionDomain.Burst,
                true,
                parameters,
                childPolicy,
                reads: new string[0],
                writes: new string[0],
                sideEffects: new string[0],
                new[] { NodeStatus.Success },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                configuration,
                NodeCancellationMode.AbortOnly,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Success", "{}", "Returns success.") });
        }
    }
}
