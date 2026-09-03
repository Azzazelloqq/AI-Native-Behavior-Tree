using System.Linq;
using AIBT.Authoring;
using AIBT.Authoring.Migration;
using AIBT.Mcp.Verification;
using NUnit.Framework;
using AuthoringNodeRegistry = AIBT.Authoring.NodeRegistry;

namespace AIBT.Tests.Editor.Migration
{
    /// <summary>
    /// P7-006 (ADR-P7-005): proves <see cref="McpVerificationToolDispatcher.ApplyMigrations"/> --
    /// the exact hook <c>Validate</c>/<c>Compile</c> call on every request -- not just the
    /// standalone <see cref="DocumentMigrator"/> engine.
    /// </summary>
    public sealed class McpMigrationHookTests
    {
        private const string TypeId = "aibt.core.migration-hook-test-node";

        [Test]
        public void ApplyMigrations_WithAPopulatedRegistry_ProducesAnAibt2042InfoDiagnostic()
        {
            var registry = RegistryWithManifest();
            var rules = NodeMigrationRegistry.Empty.WithRule(new NodeMigrationRule(
                TypeId, sourceVersion: 1,
                additions: new[] { new NodeFieldAddition("acceleration", SemanticValue.FromUInt64(5)) }));
            var document = AuthoredDocument();

            var migrated = McpVerificationToolDispatcher.ApplyMigrations(document, registry, out var diagnostics, rules);

            Assert.That(diagnostics, Has.Count.EqualTo(1));
            var diagnostic = diagnostics[0];
            Assert.That(diagnostic.Code.Value, Is.EqualTo(TreeValidationDiagnosticCodes.MigrationApplied.Value));
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Info), "A migration-applied diagnostic must never be Error -- the document already compiles.");
            Assert.That(diagnostic.Message, Does.Contain("acceleration"));

            var node = migrated.Nodes.Single(n => n.TypeId == TypeId);
            Assert.That(node.TypeVersion, Is.EqualTo(2));
        }

        [Test]
        public void ApplyMigrations_WithTheDefaultEmptyRegistry_IsANoOpAndProducesNoDiagnostics()
        {
            var registry = RegistryWithManifest();
            var document = AuthoredDocument();

            var migrated = McpVerificationToolDispatcher.ApplyMigrations(document, registry, out var diagnostics);

            Assert.That(diagnostics, Is.Empty, "No real production migration rule exists yet -- the hook must be a real no-op today.");
            Assert.That(ReferenceEquals(migrated, document), Is.True, "An unchanged document must be returned as-is, not a needless copy.");
        }

        private static TreeDocument AuthoredDocument()
        {
            var root = new NodeDocument(
                new NodeId("root"), TypeId, 1, children: null,
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.migration-hook-test"), "Migration hook test", root.Id, new[] { root },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static AuthoringNodeRegistry RegistryWithManifest()
        {
            var parameters = new[] { new NodeParameterContract("acceleration", NodeParameterType.UInt32, true) };
            var configuration = new NodeConfigurationDescriptor(4, 4, new[] { new NodeConfigurationField("acceleration", 0, 4, 4) });
            var childPolicy = new NodeChildPolicy(0, 0, true);
            var manifest = new NodeManifest(
                TypeId, 2, "P7-006 hook test fixture node.", "Test", NodeBehaviorKind.Action,
                "Used only by McpMigrationHookTests.", "Do not use in production.",
                NodeExecutionDomain.Burst, true, parameters, childPolicy,
                reads: new string[0], writes: new string[0], sideEffects: new string[0],
                new[] { NodeStatus.Success }, new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                configuration, NodeCancellationMode.AbortOnly, NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Success", "{}", "Returns success.") });

            var builder = NodeRegistryBuilder.CreateWithBuiltIns();
            builder.AddBuiltInForTest(manifest, new NodeHandlerBindingContract(
                "aibt.reference.core.migration-hook-test-node", manifest.Version, manifest.ExecutionDomain));
            var result = builder.Build();
            Assert.That(result.Success, Is.True, string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            return result.Registry;
        }
    }
}
