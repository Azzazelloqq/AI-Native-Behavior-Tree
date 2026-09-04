using System;
using System.Collections;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using AIBT.Editor.Layout;
using AIBT.Editor.Organization;
using NUnit.Framework;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

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

        [Test]
        public void TitlesUseReadableTypeSuffixAndPreserveExplicitDisplayName()
        {
            var json = File.ReadAllText(FixturePath()).Replace("\"type\": \"sample.graph-action\"",
                "\"displayName\": \"Wait by the gate\", \"type\": \"sample.graph-action\"");
            var document = CanonicalTreeJson.Parse(System.Text.Encoding.UTF8.GetBytes(json)).Document;
            var view = new BehaviorTreeGraphView();
            view.Populate(document, BuildRegistry());
            Assert.That(view.NodesById[new NodeId("root")].title, Is.EqualTo("Memory Sequence"));
            Assert.That(view.NodesById[new NodeId("condition")].title, Is.EqualTo("Graph Condition"));
            var action = view.NodesById[new NodeId("action")];
            Assert.That(action.title, Is.EqualTo("Wait by the gate"));
            Assert.That(action.tooltip, Is.EqualTo("sample.graph-action"));
        }

        [Test]
        public void NavigationDoesNotEnableSemanticEditing()
        {
            var view = new BehaviorTreeGraphView();
            view.Populate(ParseFixture(), BuildRegistry());
            foreach (var node in view.NodesById.Values)
            {
                Assert.That(node.capabilities.HasFlag(Capabilities.Selectable), Is.True);
                Assert.That(node.capabilities.HasFlag(Capabilities.Movable), Is.True);
                Assert.That(node.capabilities.HasFlag(Capabilities.Deletable), Is.False);
                Assert.That(node.capabilities.HasFlag(Capabilities.Copiable), Is.False);
                Assert.That(node.InputPort.enabledSelf, Is.False);
                if (node.OutputPort != null) Assert.That(node.OutputPort.enabledSelf, Is.False);
            }
            Assert.That(view.edges.ToList().All(edge => !edge.capabilities.HasFlag(Capabilities.Deletable)), Is.True);
        }

        [UnityTest]
        public IEnumerator ReopeningRestoresStoredPositionsWithoutWritingEitherDocument()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                var tree = ParseFixture();
                var placements = new System.Collections.Generic.Dictionary<NodeId, LayoutNodePlacement>
                {
                    [tree.Root] = new LayoutNodePlacement(new LayoutPoint(713, -129), true)
                };
                LayoutPersistenceController.Save(path, new LayoutDocument(tree.TreeId, LayoutDirection.TopToBottom, placements));
                var semanticBytes = File.ReadAllBytes(path);
                var layoutBytes = File.ReadAllBytes(LayoutPersistenceController.LayoutPathFor(path));
                window.OpenFromPath(path, BuildRegistry());
                yield return null;
                var view = window.rootVisualElement.Q<BehaviorTreeGraphView>();
                Assert.That(window.Diagnostics.Count, Is.Zero);
                Assert.That(view.NodesById.Count, Is.EqualTo(tree.Nodes.Count), "Partial stored layouts must include new nodes too.");
                AssertStoredPosition(view.NodesById[tree.Root], new Vector2(713, -129));
                view.NodesById[tree.Root].SetPosition(new Rect(10, 20, 0, 0));
                window.OpenFromPath(path, BuildRegistry());
                yield return null;
                AssertStoredPosition(view.NodesById[tree.Root], new Vector2(713, -129));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(semanticBytes));
                Assert.That(File.ReadAllBytes(LayoutPersistenceController.LayoutPathFor(path)), Is.EqualTo(layoutBytes));
            }
            return WithRenderedScratchWindow(Check);
        }

        [UnityTest]
        public IEnumerator MissingLayoutUsesDefaultWithoutCreatingFile()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                window.OpenFromPath(path, BuildRegistry());
                yield return null;
                var view = window.rootVisualElement.Q<BehaviorTreeGraphView>();
                Assert.That(window.Diagnostics.Count, Is.Zero);
                Assert.That(view.NodesById[new NodeId("root")].GetPosition().position, Is.EqualTo(Vector2.zero));
                Assert.That(view.NodesById[new NodeId("guard")].GetPosition().y, Is.GreaterThan(0));
                Assert.That(File.Exists(LayoutPersistenceController.LayoutPathFor(path)), Is.False);
            }
            return WithRenderedScratchWindow(Check);
        }

        private static void AssertStoredPosition(BehaviorTreeNode node, Vector2 expected)
        {
            Assert.That(node.style.left.value.value, Is.EqualTo(expected.x));
            Assert.That(node.style.top.value.value, Is.EqualTo(expected.y));
            // UI Toolkit rounds rendered coordinates to physical pixels on scaled displays.
            Assert.That(Vector2.Distance(node.GetPosition().position, expected),
                Is.LessThanOrEqualTo(1f / UnityEditor.EditorGUIUtility.pixelsPerPoint));
        }

        private static IEnumerator WithRenderedScratchWindow(Func<BehaviorTreeGraphWindow, string, IEnumerator> action)
        {
            var directory = Path.Combine(Path.GetTempPath(), "aibt-graph-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "tree.aibt.json");
            File.Copy(FixturePath(), path);
            var window = ScriptableObject.CreateInstance<BehaviorTreeGraphWindow>();
            try
            {
                window.Show();
                yield return action(window, path);
            }
            finally
            {
                window.Close();
                Directory.Delete(directory, true);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InvalidInputClearsPreviousGraphAndDisplaysDiagnostics(bool invalidLayout)
        {
            WithScratchWindow((window, path) =>
            {
                window.OpenFromPath(path, BuildRegistry());
                File.WriteAllText(invalidLayout ? LayoutPersistenceController.LayoutPathFor(path) : path, "{");
                window.OpenFromPath(path, BuildRegistry());
                Assert.That(window.rootVisualElement.Q<BehaviorTreeGraphView>().NodesById, Is.Empty);
                Assert.That(window.Diagnostics.Count, Is.GreaterThan(0));
                Assert.That(window.rootVisualElement.Q<Label>("aibt-graph-diagnostics").text, Is.Not.Empty);
            });
        }

        private static void WithScratchWindow(Action<BehaviorTreeGraphWindow, string> action)
        {
            var directory = Path.Combine(Path.GetTempPath(), "aibt-graph-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "tree.aibt.json");
            File.Copy(FixturePath(), path);
            var window = ScriptableObject.CreateInstance<BehaviorTreeGraphWindow>();
            try { action(window, path); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                Directory.Delete(directory, true);
            }
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
