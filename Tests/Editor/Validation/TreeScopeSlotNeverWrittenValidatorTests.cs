using System;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Validation
{
    /// <summary>
    /// P7-039 (ADR-P7-039): <c>AIBT2053 TreeScopeSlotNeverWritten</c> -- an Info-severity heads-up
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
        public void TreeScopeKeyWithNoNodeWriter_IsDescribedAsAnInputGameCodeSets()
        {
            // P9-006: keys gameplay code writes (agent.Blackboard.Set / NativeTreeAgent.SetFact) are
            // the documented path, so the note names that path instead of reading as a defect.
            var manifest = Manifest("game.reader", reads: new[] { "target-visible" }, writes: Array.Empty<string>());
            var registry = new NodeRegistryBuilder().AddUserExtension(manifest).Build().Registry;
            var document = Document(
                new[] { Node("root", manifest.TypeId) },
                blackboard: new[] { Key("target-visible", "target-visible", BlackboardScope.Tree) });

            var diagnostic = TreeValidator.Validate(document, registry)
                .Single(item => item.Code == TreeValidationDiagnosticCodes.TreeScopeSlotNeverWritten);

            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Info));
            Assert.That(diagnostic.Message, Does.StartWith("Input key 'target-visible':"),
                "The key is named, so several input keys stay distinct notes in the graph's Problems list.");
            Assert.That(diagnostic.Message, Does.Contain("Blackboard.Set"));
            Assert.That(diagnostic.Message, Does.Contain("SetFact"));
            Assert.That(diagnostic.Message, Does.Not.Contain("declares Write access"));
        }

        [Test]
        public void KeysAManagedNodeBindsThroughABlackboardKeyParameter_AreNotReportedAsInputs()
        {
            // P9-006: blackboard-key parameters (managed nodes, ADR-P9-001) do not say whether the node
            // reads or writes the key, so a bound key may be node-written and is not reported. A key no
            // node binds at all still is.
            const string json = @"{ ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tree.managed-inputs"", ""name"": ""Inputs"", ""root"": ""set"",
  ""blackboard"": {
    ""flag"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false },
    ""unused"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false } },
  ""nodes"": { ""set"": { ""type"": ""aibt.unity.set-bool"", ""typeVersion"": 1, ""parameters"": { ""key"": ""flag"", ""value"": true } } } }";
            var read = CanonicalTreeJson.Parse(json, documentId: "trees/inputs.aibt");
            Assert.That(read.Document, Is.Not.Null);
            var options = AIBT.Agents.ManagedTreeProgram.CreateOptions("trees/inputs.aibt");
            var diagnostics = TreeValidator.Validate(read.Document, AIBT.Tests.Editor.Agents.StandardNodeKit.Nodes.Registry,
                options.Policy.CreateValidationOptions(options.SourceId));

            Assert.That(diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error), Is.Empty,
                string.Join("\n", diagnostics.Select(item => item.Code.Value + ": " + item.Message)));
            var inputs = diagnostics.Where(item => item.Code == TreeValidationDiagnosticCodes.TreeScopeSlotNeverWritten).ToArray();
            Assert.That(inputs.Length, Is.EqualTo(1));
            Assert.That(inputs[0].Message, Does.StartWith("Input key 'unused':"));
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
