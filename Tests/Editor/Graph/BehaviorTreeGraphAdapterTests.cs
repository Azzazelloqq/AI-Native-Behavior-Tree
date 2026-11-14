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
        public IEnumerator OpeningATreePreservesSelectionAcrossDeferredWindowInitialization()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                window.OpenFromPath(path, BuildRegistry());
                var view = window.rootVisualElement.Q<BehaviorTreeGraphView>();
                var selected = view.NodesById[new NodeId("root")];
                view.AddToSelection(selected);
                yield return null;
                yield return null;
                Assert.That(view.NodesById[new NodeId("root")], Is.SameAs(selected));
                Assert.That(view.selection, Does.Contain(selected));
            }
            return WithRenderedScratchWindow(Check);
        }

        [UnityTest]
        public IEnumerator EditingNodeParameterAndSavingCompletesADraft()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                var root = new NodeDocument(new NodeId("root"), "aibt.core.inverter", 1,
                    parameters: SemanticObject.Empty, tags: TagSet.Empty);
                var tree = new TreeDocument(TreeDocument.CurrentFormat, 1, new TreeId("tree.test.window"), "Window",
                    root.Id, new[] { root }, tags: TagSet.Empty, metadata: SemanticObject.Empty);
                File.WriteAllBytes(path, CanonicalTreeJson.Serialize(tree).Utf8);
                var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
                Assert.That(registry.TryGet("aibt.stdlib.wait", out var wait), Is.True);
                window.OpenFromPath(path, registry);
                yield return null;
                window.AddChildTo(root.Id, wait.Manifest);
                var view = window.rootVisualElement.Q<BehaviorTreeGraphView>();
                var child = window.Document.Nodes.Single(node => node.Id != root.Id);
                view.ClearSelection();
                view.AddToSelection(view.NodesById[child.Id]);
                using (var mouseUp = MouseUpEvent.GetPooled())
                {
                    mouseUp.target = view;
                    view.SendEvent(mouseUp);
                }
                yield return null;
                var field = window.rootVisualElement.Q<UnsignedIntegerField>("aibt-param-ticks");
                Assert.That(field, Is.Not.Null, "A UInt32 parameter is edited through a typed field.");
                Assert.That(field.label, Does.StartWith("ticks"));
                field.value = 3;
                yield return null;
                Assert.That(window.HasPendingChanges, Is.True);
                Assert.That(window.SaveAll(), Is.True);
                yield return null;
                Assert.That(window.HasPendingChanges, Is.False);
                var parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
                Assert.That(parsed.Success, Is.True);
                var options = new ReferenceCompilerOptions("trees/window.aibt.json", ReferenceCompilationPolicy.Phase1,
                    new CompiledCompilerVersion(1, 0, 0, 0));
                var compilation = ReferenceCompiler.Compile(parsed.Document, registry, options);
                Assert.That(compilation.Success, Is.True, Messages(compilation.Diagnostics));
            }
            return WithRenderedScratchWindow(Check);
        }

        [UnityTest]
        public IEnumerator NativeCheckCompilesCompleteV2TreeAndReportsIncompleteTree()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                var root = new NodeDocument(new NodeId("root"), "aibt.core.inverter", 1,
                    parameters: SemanticObject.Empty, tags: TagSet.Empty);
                var tree = TreeDocument.CreateVersion2(new TreeId("tree.test.native-check"), "Native check", root.Id,
                    new[] { root }, null, null, tags: TagSet.Empty, metadata: SemanticObject.Empty);
                File.WriteAllBytes(path, CanonicalTreeJson.Serialize(tree).Utf8);
                window.OpenFromPath(path, NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry);
                yield return null;
                window.CheckNative();
                var result = window.rootVisualElement.Q<Label>("aibt-native-validation");
                Assert.That(result.text, Does.StartWith("Native compilation:"));
                Assert.That(NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry.TryGet("aibt.stdlib.wait", out var wait), Is.True);
                window.AddChildTo(root.Id, wait.Manifest);
                var child = window.Document.Nodes.Single(node => node.Id != root.Id);
                var view = window.rootVisualElement.Q<BehaviorTreeGraphView>();
                view.AddToSelection(view.NodesById[child.Id]);
                using (var mouseUp = MouseUpEvent.GetPooled()) { mouseUp.target = view; view.SendEvent(mouseUp); }
                yield return null;
                window.rootVisualElement.Q<UnsignedIntegerField>("aibt-param-ticks").value = 3;
                yield return null;
                window.CheckNative();
                Assert.That(result.text, Does.StartWith("Native program compiled."), result.text);
                Assert.That(window.SaveAll(), Is.True);
                yield return null;
            }
            return WithRenderedScratchWindow(Check);
        }

        [UnityTest]
        public IEnumerator LayoutMovementCanBeUndoneAndRedone()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                window.OpenFromPath(path, BuildRegistry());
                yield return null;
                var view = window.rootVisualElement.Q<BehaviorTreeGraphView>();
                var root = view.NodesById[new NodeId("root")];
                var original = BehaviorTreeGraphView.NodePosition(root);
                Move(view, root, new Vector2(240, 120));
                yield return null;
                Assert.That(window.HasPendingChanges, Is.True, "Moving a node is a layout change that Save writes.");
                window.UndoEdit();
                for (var frame = 0; frame < 20 && Vector2.Distance(root.GetPosition().position, original) > 1; frame++)
                    yield return null;
                AssertStoredPosition(root, original);
                window.RedoEdit();
                for (var frame = 0; frame < 20 && Vector2.Distance(root.GetPosition().position, new Vector2(240, 120)) > 1; frame++)
                    yield return null;
                AssertStoredPosition(root, new Vector2(240, 120));
            }
            return WithRenderedScratchWindow(Check);
        }

        [UnityTest]
        public IEnumerator SavingLayoutPersistsPositionsWithoutChangingTree()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                var semanticBytes = File.ReadAllBytes(path);
                window.OpenFromPath(path, BuildRegistry());
                yield return null;
                var view = window.rootVisualElement.Q<BehaviorTreeGraphView>();
                Move(view, view.NodesById[new NodeId("root")], new Vector2(410, 220));
                yield return null;
                Assert.That(window.SaveAll(), Is.True);
                yield return null;
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(semanticBytes), "Saving a layout-only change must not rewrite the tree.");
                Assert.That(File.Exists(LayoutPersistenceController.LayoutPathFor(path)), Is.True);
                window.OpenFromPath(path, BuildRegistry());
                yield return null;
                AssertStoredPosition(view.NodesById[new NodeId("root")], new Vector2(410, 220));
            }
            return WithRenderedScratchWindow(Check);
        }

        [UnityTest]
        public IEnumerator SavingLayoutAfterRemovingBranchPreservesNotesAndSurvivingGroupMembers()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                var tree = ParseFixture();
                var root = tree.Root;
                var removed = new NodeId("guard");
                var placements = tree.Nodes.ToDictionary(node => node.Id,
                    node => new LayoutNodePlacement(new LayoutPoint(0, 0)));
                var groups = new System.Collections.Generic.Dictionary<string, LayoutGroup>
                {
                    ["group"] = new LayoutGroup("Group", new[] { root, removed })
                };
                var notes = new System.Collections.Generic.Dictionary<string, LayoutNote>
                {
                    ["note"] = new LayoutNote("Keep", new LayoutPoint(0, 0), new LayoutPoint(100, 50))
                };
                LayoutPersistenceController.Save(path, new LayoutDocument(tree.TreeId, LayoutDirection.TopToBottom, placements, groups, notes));
                var edited = AIBT.Editor.Editing.SemanticEditOperations.RemoveNode(tree, removed);
                File.WriteAllBytes(path, CanonicalTreeJson.Serialize(edited).Utf8);

                window.OpenFromPath(path, BuildRegistry());
                yield return null;
                var view = window.rootVisualElement.Q<BehaviorTreeGraphView>();
                Move(view, view.NodesById[root], new Vector2(32, 16));
                yield return null;
                Assert.That(window.SaveAll(), Is.True);
                yield return null;
                var layout = CanonicalLayoutJson.Parse(File.ReadAllBytes(LayoutPersistenceController.LayoutPathFor(path)), null, edited);
                Assert.That(layout.Success, Is.True, Messages(layout.Diagnostics));
                Assert.That(layout.Document.Groups["group"].MemberNodeIds, Is.EquivalentTo(new[] { root }));
                Assert.That(layout.Document.Notes["note"].Text, Is.EqualTo("Keep"));
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
                // The default is a top-down tree: the root above its children, children in semantic order.
                var root = BehaviorTreeGraphView.NodePosition(view.NodesById[new NodeId("root")]);
                var guard = BehaviorTreeGraphView.NodePosition(view.NodesById[new NodeId("guard")]);
                var action = BehaviorTreeGraphView.NodePosition(view.NodesById[new NodeId("action")]);
                var condition = BehaviorTreeGraphView.NodePosition(view.NodesById[new NodeId("condition")]);
                Assert.That(guard.y, Is.GreaterThan(root.y));
                Assert.That(action.y, Is.EqualTo(guard.y));
                Assert.That(condition.y, Is.GreaterThan(guard.y));
                Assert.That(guard.x, Is.LessThan(action.x));
                Assert.That(root.x, Is.GreaterThan(guard.x).And.LessThan(action.x), "The parent is centered over its children.");
                Assert.That(action.x - guard.x, Is.GreaterThanOrEqualTo(176f), "Sibling cards must not overlap.");
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
                if (window.hasUnsavedChanges) window.DiscardChanges();
                window.Close();
                Directory.Delete(directory, true);
            }
        }

        private static void Move(BehaviorTreeGraphView view, BehaviorTreeNode node, Vector2 position)
        {
            node.SetPosition(new Rect(position, Vector2.zero));
            view.graphViewChanged(new GraphViewChange { movedElements = new System.Collections.Generic.List<GraphElement> { node } });
        }

        [Test]
        public void InvalidTreeClearsPreviousGraphAndDisplaysDiagnostics()
        {
            WithScratchWindow((window, path) =>
            {
                window.OpenFromPath(path, BuildRegistry());
                File.WriteAllText(path, "{");
                window.OpenFromPath(path, BuildRegistry());
                Assert.That(window.rootVisualElement.Q<BehaviorTreeGraphView>().NodesById, Is.Empty);
                Assert.That(window.Diagnostics.Count, Is.GreaterThan(0));
                Assert.That(window.rootVisualElement.Q<Label>("aibt-graph-diagnostics").text, Is.Not.Empty);
            });
        }

        [Test]
        public void InvalidLayoutShowsDiagnosticButStillDisplaysTree()
        {
            WithScratchWindow((window, path) =>
            {
                var layoutPath = LayoutPersistenceController.LayoutPathFor(path);
                File.WriteAllText(layoutPath, "{ invalid layout }");
                var originalLayout = File.ReadAllBytes(layoutPath);

                window.OpenFromPath(path, BuildRegistry());

                Assert.That(window.Document, Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<BehaviorTreeGraphView>().NodesById.Count, Is.EqualTo(4));
                Assert.That(window.Diagnostics.Count, Is.GreaterThan(0));
                Assert.That(window.rootVisualElement.Q<Label>("aibt-graph-diagnostics").text, Is.Not.Empty);
                Assert.That(File.ReadAllBytes(layoutPath), Is.EqualTo(originalLayout));
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
