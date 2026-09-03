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
