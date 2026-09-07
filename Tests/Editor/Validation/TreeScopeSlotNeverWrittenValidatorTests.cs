using System;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Validation
{
    /// <summary>
    /// P7-039 (ADR-P7-039): <c>AIBT2043 TreeScopeSlotNeverWritten</c> -- an Info-severity heads-up
    /// for a declared Tree-scope key no node in the tree ever writes, eligible for a native
    /// <c>ProductionTreeHost</c> external write. Never blocks compilation, and never fires for
    /// Agent/Shared-scope keys.
    /// </summary>
    public sealed class TreeScopeSlotNeverWrittenValidatorTests
    {
        private AIBT.Authoring.NodeRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            var result = NodeRegistryBuilder.CreateWithBuiltIns().Build();
            Assert.That(result.Success, Is.True);
            _registry = result.Registry;
        }

        [Test]
        public void TreeScopeKeyWithNoNodeWriter_EmitsExactlyOneInfoDiagnostic_AndStillValidates()
        {
            var manifest = Manifest("game.reader", reads: new[] { "never-written" }, writes: Array.Empty<string>());
            var registry = new NodeRegistryBuilder().AddUserExtension(manifest).Build().Registry;
            var document = Document(
                new[] { Node("root", manifest.TypeId) },
                blackboard: new[] { Key("never-written", "never-written", BlackboardScope.Tree) });

            var diagnostics = TreeValidator.Validate(document, registry);

            Assert.That(diagnostics.Count(item => item.Code == TreeValidationDiagnosticCodes.TreeScopeSlotNeverWritten), Is.EqualTo(1));
            var diagnostic = diagnostics.Single(item => item.Code == TreeValidationDiagnosticCodes.TreeScopeSlotNeverWritten);
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Info));
            Assert.That(diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error), Is.False,
                "An Info heads-up must never block an otherwise-valid document.");
        }

        [Test]
        public void TreeScopeKeyANodeWrites_NeverEmitsTheDiagnostic()
        {
            var manifest = Manifest("game.writer", reads: Array.Empty<string>(), writes: new[] { "owned" });
            var registry = new NodeRegistryBuilder().AddUserExtension(manifest).Build().Registry;
            var document = Document(
                new[] { Node("root", manifest.TypeId) },
                blackboard: new[] { Key("owned", "owned", BlackboardScope.Tree) });

            var diagnostics = TreeValidator.Validate(document, registry);

            Assert.That(diagnostics.Any(item => item.Code == TreeValidationDiagnosticCodes.TreeScopeSlotNeverWritten), Is.False);
        }

        [Test]
        public void AgentAndSharedScopeKeys_AreNeverConsideredByThisDiagnostic_EvenWhenNeverWritten()
        {
            var manifest = Manifest("game.reader-agent-shared",
                reads: new[] { "agent-key", "shared-key" }, writes: Array.Empty<string>());
            var registry = new NodeRegistryBuilder().AddUserExtension(manifest).Build().Registry;
            var document = Document(
                new[] { Node("root", manifest.TypeId) },
                blackboard: new[]
                {
                    Key("agent-key", "agent-key", BlackboardScope.Agent),
                    Key("shared-key", "shared-key", BlackboardScope.Shared),
                },
                agentContract: new BlackboardScopeContract("tests.agent", 1),
                sharedContract: new BlackboardScopeContract("tests.shared", 1));

            var diagnostics = TreeValidator.Validate(document, registry);

            Assert.That(diagnostics.Any(item => item.Code == TreeValidationDiagnosticCodes.TreeScopeSlotNeverWritten), Is.False,
                "Only Tree-scope keys are in scope for this diagnostic.");
        }

        private static NodeManifest Manifest(
            string typeId,
            string[] reads,
            string[] writes)
        {
            return new NodeManifest(
                typeId,
                1,
                "Test node.",
                "Test",
                NodeBehaviorKind.Action,
                "Use in validation tests.",
                "Do not use in production.",
                NodeExecutionDomain.Burst,
                true,
                Array.Empty<NodeParameterContract>(),
                new NodeChildPolicy(0, 0, true),
                reads,
                writes,
                Array.Empty<string>(),
                new[] { NodeStatus.Success },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                NodeCancellationMode.AbortOnly,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Success", "{}", "Succeeds.") });
        }

        private static TreeDocument Document(
            System.Collections.Generic.IEnumerable<NodeDocument> nodes,
            System.Collections.Generic.IEnumerable<BlackboardKeyDefinition> blackboard,
            BlackboardScopeContract agentContract = null,
            BlackboardScopeContract sharedContract = null)
        {
            if (agentContract != null || sharedContract != null)
            {
                return TreeDocument.CreateVersion2(
                    new TreeId("tree"), "Tree", Id("root"), nodes.ToArray(),
                    agentContract, sharedContract, blackboard);
            }
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree"), "Tree", Id("root"), nodes.ToArray(), blackboard);
        }

        private static NodeDocument Node(string id, string type)
            => new NodeDocument(Id(id), type, 1, null, null);

        private static BlackboardKeyDefinition Key(string id, string name, BlackboardScope scope)
            => new BlackboardKeyDefinition(
                id, name, BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool), scope,
                BlackboardDefaultValue.Bool(false));

        private static NodeId Id(string value) => new NodeId(value);
    }
}
