using System;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Graph
{
    public sealed class BehaviorTreeGraphAdapterTests
    {
        [Test]
        public void EveryNodeKindRendersWithCorrectConnections()
        {
            var document = ParseFixture();
            var registry = BuildRegistry();

            var view = new BehaviorTreeGraphView();
            view.Populate(document, registry);

            Assert.That(view.NodesById.Count, Is.EqualTo(4));

            var root = view.NodesById[new NodeId("root")];
            var guard = view.NodesById[new NodeId("guard")];
            var condition = view.NodesById[new NodeId("condition")];
            var action = view.NodesById[new NodeId("action")];

            Assert.That(root.Manifest?.Kind, Is.EqualTo(NodeBehaviorKind.Composite));
            Assert.That(guard.Manifest?.Kind, Is.EqualTo(NodeBehaviorKind.Decorator));
            Assert.That(condition.Manifest?.Kind, Is.EqualTo(NodeBehaviorKind.Condition));
            Assert.That(action.Manifest?.Kind, Is.EqualTo(NodeBehaviorKind.Action));

            Assert.That(root.OutputPort, Is.Not.Null, "A composite must expose an output port.");
            Assert.That(guard.OutputPort, Is.Not.Null, "A decorator must expose an output port.");
            Assert.That(condition.OutputPort, Is.Null, "A condition (leaf) must not expose an output port.");
            Assert.That(action.OutputPort, Is.Null, "An action (leaf) must not expose an output port.");

            Assert.That(root.OutputPort.connections.Count(), Is.EqualTo(2), "root connects to guard and action.");
            Assert.That(guard.OutputPort.connections.Count(), Is.EqualTo(1), "guard connects to condition.");
            Assert.That(guard.InputPort.connections.Count(), Is.EqualTo(1));
            Assert.That(condition.InputPort.connections.Count(), Is.EqualTo(1));
            Assert.That(action.InputPort.connections.Count(), Is.EqualTo(1));

            var guardEdge = guard.OutputPort.connections.Single();
            Assert.That(guardEdge.input, Is.EqualTo(condition.InputPort));

            var rootChildInputs = root.OutputPort.connections.Select(edge => edge.input).ToArray();
            Assert.That(rootChildInputs, Does.Contain(guard.InputPort));
            Assert.That(rootChildInputs, Does.Contain(action.InputPort));
        }

        [Test]
        public void OpeningADocumentNeverMutatesItOnDiskOrInMemory()
        {
            var path = FixturePath();
            var before = File.ReadAllBytes(path);

            var parseResult = CanonicalTreeJson.Parse(before, path);
            Assert.That(parseResult.Success, Is.True, Messages(parseResult.Diagnostics));
            var revisionBefore = parseResult.Document.Revision;

            var registry = BuildRegistry();
            var view = new BehaviorTreeGraphView();
            view.Populate(parseResult.Document, registry);

            var after = File.ReadAllBytes(path);
            Assert.That(after, Is.EqualTo(before), "Opening a document for rendering must never touch the file on disk.");
            Assert.That(parseResult.Document.Revision, Is.EqualTo(revisionBefore), "Rendering must never mutate the in-memory document (Revision is bumped only by Mutate()).");
        }

        [Test]
        public void UnresolvedNodeTypesRenderWithoutThrowing()
        {
            var document = ParseFixture();

            var view = new BehaviorTreeGraphView();

            Assert.DoesNotThrow(() => view.Populate(document, registry: null));
            Assert.That(view.NodesById.Count, Is.EqualTo(4));
            Assert.That(view.NodesById[new NodeId("condition")].Manifest, Is.Null);
        }

        private static TreeDocument ParseFixture()
        {
            var path = FixturePath();
            var result = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
            Assert.That(result.Success, Is.True, Messages(result.Diagnostics));
            return result.Document;
        }

        private static string FixturePath()
        {
            return EditorTestPackagePaths.Resolve("Tests", "Editor", "Graph", "Fixtures", "four-kinds.aibt.json");
        }

        // Fully qualified: this file's namespace (AIBT.Tests.Editor.Graph) is nested under
        // AIBT.Tests.Editor, which also contains the sibling namespace AIBT.Tests.Editor.NodeRegistry
        // (see Tests/Editor/NodeRegistry/) -- an unqualified "NodeRegistry" resolves to that
        // namespace instead of AIBT.Authoring.NodeRegistry (CS0118).
        private static AIBT.Authoring.NodeRegistry BuildRegistry()
        {
            var leaf = new NodeChildPolicy(0, 0, true);

            var conditionManifest = new NodeManifest(
                "sample.graph-condition",
                1,
                "A test condition.",
                "Test",
                NodeBehaviorKind.Condition,
                "Use in graph adapter tests.",
                "Do not use in production.",
                NodeExecutionDomain.Burst,
                true,
                Array.Empty<NodeParameterContract>(),
                leaf,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Failure },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                NodeCancellationMode.NotApplicable,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Success", "{}", "Returns success when the condition holds.") });

            var actionManifest = new NodeManifest(
                "sample.graph-action",
                1,
                "A test action.",
                "Test",
                NodeBehaviorKind.Action,
                "Use in graph adapter tests.",
                "Do not use in production.",
                NodeExecutionDomain.Burst,
                true,
                Array.Empty<NodeParameterContract>(),
                leaf,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Running, NodeStatus.Failure },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                NodeCancellationMode.AbortOnly,
                NodeCostHint.Low,
                new[] { new NodeManifestExample("Success", "{}", "Completes the action.") });

            var buildResult = NodeRegistryBuilder.CreateWithBuiltIns()
                .AddUserExtension(conditionManifest)
                .AddUserExtension(actionManifest)
                .Build();
            Assert.That(buildResult.Success, Is.True, Messages(buildResult.Diagnostics));
            return buildResult.Registry;
        }

        private static string Messages(DiagnosticCollection diagnostics)
        {
            return diagnostics == null ? string.Empty : string.Join("; ", diagnostics.Select(d => d.Code.Value + ": " + d.Message));
        }
    }
}
