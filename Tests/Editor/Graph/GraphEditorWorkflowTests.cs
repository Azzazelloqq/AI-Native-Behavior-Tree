using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using NUnit.Framework;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIBT.Tests.Editor.Graph
{
    /// <summary>
    /// P9-003: graph editing without the old per-action toolbar — create, connect, reorder,
    /// duplicate, delete, one undo history and one Save — checked through the window's model.
    /// </summary>
    public sealed class GraphEditorWorkflowTests
    {
        private static AIBT.Authoring.NodeRegistry Registry() => NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;

        private static NodeManifest Manifest(string typeId)
        {
            Assert.That(Registry().TryGet(typeId, out var entry), Is.True, typeId);
            return entry.Manifest;
        }

        private static NodeDocument Wait(string id, ulong ticks)
            => new NodeDocument(new NodeId(id), "aibt.stdlib.wait", 1, parameters:
                new SemanticObject(new[] { new SemanticProperty("ticks", SemanticValue.FromUInt64(ticks)) }), tags: TagSet.Empty);

        /// <summary>root (memory sequence) → a, b, c (waits).</summary>
        private static TreeDocument Sequence()
        {
            var root = new NodeDocument(new NodeId("root"), "aibt.core.memory-sequence", 1,
                children: new[] { new NodeId("a"), new NodeId("b"), new NodeId("c") }, parameters: SemanticObject.Empty, tags: TagSet.Empty);
            return new TreeDocument(TreeDocument.CurrentFormat, 1, new TreeId("tree.test.workflow"), "Workflow", root.Id,
                new[] { root, Wait("a", 1), Wait("b", 2), Wait("c", 3) }, tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static IEnumerable<string> ChildIds(TreeDocument document, string parent)
            => GraphDocumentEdits.Find(document, new NodeId(parent)).Children.Select(id => id.Value);

        // ---------------------------------------------------------------- pure helpers

        [Test]
        public void ParameterSummaryListsContractParametersInOrder()
        {
            var tree = Sequence();
            var summary = NodeParameterSummary.Describe(GraphDocumentEdits.Find(tree, new NodeId("b")), Manifest("aibt.stdlib.wait"));
            Assert.That(summary, Is.EqualTo("ticks: 2"));
            Assert.That(NodeParameterSummary.Describe(GraphDocumentEdits.Find(tree, new NodeId("root")), Manifest("aibt.core.memory-sequence")),
                Is.Empty);
        }

        [Test]
        public void MovingAChildChangesOnlySemanticOrder()
        {
            var tree = Sequence();
            Assert.That(GraphDocumentEdits.TryMoveChild(tree, new NodeId("c"), -1, out var moved), Is.True);
            Assert.That(ChildIds(moved, "root"), Is.EqualTo(new[] { "a", "c", "b" }));
            Assert.That(GraphDocumentEdits.TryMoveChild(tree, new NodeId("a"), -1, out _), Is.False, "The first child cannot move earlier.");
            Assert.That(GraphDocumentEdits.TryMoveChild(tree, new NodeId("root"), 1, out _), Is.False, "The root has no siblings.");
        }

        [Test]
        public void DuplicatingASubtreeInsertsTheCopyAfterTheOriginal()
        {
            var tree = Sequence();
            var copy = GraphDocumentEdits.DuplicateSubtree(tree, new NodeId("a"), out var copyId);
            Assert.That(ChildIds(copy, "root"), Is.EqualTo(new[] { "a", copyId.Value, "b", "c" }));
            Assert.That(GraphDocumentEdits.Find(copy, copyId).Parameters, Is.EqualTo(GraphDocumentEdits.Find(tree, new NodeId("a")).Parameters));
            Assert.That(copy.Nodes.Count, Is.EqualTo(tree.Nodes.Count + 1));
        }

        [Test]
        public void DetachingKeepsTheNodeAsAnOrphan()
        {
            Assert.That(GraphDocumentEdits.TryDetach(Sequence(), new NodeId("b"), out var detached), Is.True);
            Assert.That(ChildIds(detached, "root"), Is.EqualTo(new[] { "a", "c" }));
            Assert.That(GraphDocumentEdits.Find(detached, new NodeId("b")), Is.Not.Null);
            Assert.That(GraphDocumentEdits.ParentOf(detached, new NodeId("b")), Is.Null);
        }

        [Test]
        public void EditHistoryUndoesInOrderAndClearsRedoOnNewEdit()
        {
            var history = new GraphEditHistory();
            history.Record(GraphEditKind.Semantic);
            history.Record(GraphEditKind.Layout);
            Assert.That(history.Undo(), Is.EqualTo(GraphEditKind.Layout));
            Assert.That(history.Undo(), Is.EqualTo(GraphEditKind.Semantic));
            Assert.That(history.CanUndo, Is.False);
            Assert.That(history.Redo(), Is.EqualTo(GraphEditKind.Semantic));
            history.Record(GraphEditKind.Layout);
            Assert.That(history.CanRedo, Is.False);
            Assert.That(history.Position, Is.EqualTo(2));
        }

        [Test]
        public void NodeSearchGroupsNodesByKind()
        {
            var tree = NodeSearchProvider.BuildTree(Registry(), "Create Node");
            Assert.That(tree[0].level, Is.EqualTo(0));
            var groups = tree.OfType<SearchTreeGroupEntry>().Where(entry => entry.level == 1).Select(entry => entry.content.text).ToArray();
            Assert.That(groups, Is.EqualTo(new[] { "Composites (flow)", "Decorators", "Conditions", "Actions" }));
            var items = tree.Where(entry => !(entry is SearchTreeGroupEntry)).ToArray();
            Assert.That(items.All(entry => entry.level == 2), Is.True);
            Assert.That(items.Select(entry => ((NodeManifest)entry.userData).TypeId), Does.Contain("aibt.stdlib.wait"));
            Assert.That(items.Select(entry => ((NodeManifest)entry.userData).TypeId), Does.Contain("aibt.core.memory-sequence"));
        }

        [Test]
        public void LiveAnimationCurvesStayInRange()
        {
            Assert.That(GraphLiveDebugger.FlashStrength(0d), Is.EqualTo(1f));
            Assert.That(GraphLiveDebugger.FlashStrength(GraphLiveDebugger.FlashSeconds / 2d), Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(GraphLiveDebugger.FlashStrength(GraphLiveDebugger.FlashSeconds), Is.Zero);
            for (var t = 0d; t < 2d; t += 0.05d)
                Assert.That(GraphLiveDebugger.Pulse(t), Is.InRange(0.1f, 1f));
            Assert.That(GraphLiveDebugger.Matches(null, Sequence()), Is.False);
        }

        [Test]
        public void ManagedSampleTreesAreListedAsValid()
        {
            var catalog = global::AIBT.Editor.Registry.ProjectNodeCatalog.Discover();
            foreach (var name in new[] { "guard.aibt", "wander.aibt" })
            {
                var path = Directory.GetFiles(Application.dataPath, name, SearchOption.AllDirectories)
                    .FirstOrDefault(file => file.Replace('\\', '/').Contains("/Simple Agent/Trees/"));
                if (path == null) Assert.Ignore("The Simple Agent sample is not imported in this project.");
                var entry = TreeCatalogEntry.Read(path, catalog.Registry);
                Assert.That(entry.Status, Is.EqualTo("Valid"), name + ": " + entry.Details);
            }
        }

        // ---------------------------------------------------------------- view

        [Test]
        public void IncrementalSyncKeepsUnchangedCardsAndUpdatesOrderBadges()
        {
            var view = new BehaviorTreeGraphView();
            var tree = Sequence();
            view.Populate(tree, Registry());
            var a = view.NodesById[new NodeId("a")];
            var c = view.NodesById[new NodeId("c")];
            Assert.That(a.OrderText, Is.EqualTo("1"));
            Assert.That(c.OrderText, Is.EqualTo("3"));
            Assert.That(view.NodesById[new NodeId("root")].OrderText, Is.Null, "The root has no order badge.");
            Assert.That(a.Summary, Is.EqualTo("ticks: 1"));

            Assert.That(GraphDocumentEdits.TryMoveChild(tree, new NodeId("c"), -2, out var moved), Is.True);
            view.Sync(moved, Registry(), _ => Vector2.zero);
            Assert.That(view.NodesById[new NodeId("a")], Is.SameAs(a), "Sync must not recreate unchanged cards.");
            Assert.That(view.NodesById[new NodeId("c")].OrderText, Is.EqualTo("1"));
            Assert.That(a.OrderText, Is.EqualTo("2"));
            Assert.That(view.edges.ToList().Count, Is.EqualTo(3));

            var key = (new NodeId("root"), new NodeId("a"));
            view.SetActiveEdges(new[] { key });
            Assert.That(view.EdgesByKey[key].ClassListContains("aibt-edge--active"), Is.True, "Live path edges are styled as active.");
            view.ClearExecutionHighlight();
            Assert.That(view.EdgesByKey[key].ClassListContains("aibt-edge--active"), Is.False);
        }

        // ---------------------------------------------------------------- window

        [UnityTest]
        public IEnumerator CreateConnectReorderDeleteAndUndoThroughTheWindow()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                window.OpenFromPath(path, Registry());
                yield return null;
                var view = window.View;
                Assert.That(view.Editable, Is.True);

                var created = window.CreateNode(Manifest("aibt.stdlib.wait"), new NodeId("root"));
                Assert.That(created.HasValue, Is.True);
                Assert.That(ChildIds(window.Document, "root").Last(), Is.EqualTo(created.Value.Value));
                Assert.That(view.NodesById.ContainsKey(created.Value), Is.True);

                Assert.That(window.MoveChild(created.Value, -3), Is.True);
                Assert.That(ChildIds(window.Document, "root").First(), Is.EqualTo(created.Value.Value));

                Assert.That(window.DetachNode(new NodeId("b")), Is.True);
                Assert.That(ChildIds(window.Document, "root"), Does.Not.Contain("b"));
                Assert.That(window.ConnectNodes(new NodeId("root"), new NodeId("b")), Is.True);
                Assert.That(ChildIds(window.Document, "root").Last(), Is.EqualTo("b"));

                var copies = window.DuplicateNodes(new[] { new NodeId("a") });
                Assert.That(copies.Count, Is.EqualTo(1));
                Assert.That(window.DeleteNodes(new[] { new NodeId("c") }), Is.True);
                Assert.That(GraphDocumentEdits.Find(window.Document, new NodeId("c")), Is.Null);
                Assert.That(view.NodesById.ContainsKey(new NodeId("c")), Is.False);

                // One undo history walks every edit back to the opened tree.
                for (var step = 0; step < 6; step++) window.UndoEdit();
                yield return null;
                Assert.That(ChildIds(window.Document, "root"), Is.EqualTo(new[] { "a", "b", "c" }));
                Assert.That(window.Document.Nodes.Count, Is.EqualTo(4));
                Assert.That(view.NodesById.Count, Is.EqualTo(4));
                window.RedoEdit();
                Assert.That(ChildIds(window.Document, "root").Last(), Is.EqualTo(created.Value.Value));
            }
            return WithWindow(Sequence(), Check);
        }

        [UnityTest]
        public IEnumerator UnityUndoStepsTheGraphHistory()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                window.OpenFromPath(path, Registry());
                yield return null;
                Assert.That(window.MoveChild(new NodeId("c"), -1), Is.True);
                UnityEditor.Undo.PerformUndo();
                yield return null;
                Assert.That(ChildIds(window.Document, "root"), Is.EqualTo(new[] { "a", "b", "c" }));
                UnityEditor.Undo.PerformRedo();
                yield return null;
                Assert.That(ChildIds(window.Document, "root"), Is.EqualTo(new[] { "a", "c", "b" }));
            }
            return WithWindow(Sequence(), Check);
        }

        [UnityTest]
        public IEnumerator OneSaveWritesTreeAndLayout()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                window.OpenFromPath(path, Registry());
                yield return null;
                var layoutPath = global::AIBT.Editor.Organization.LayoutPersistenceController.LayoutPathFor(path);
                Assert.That(window.MoveChild(new NodeId("c"), -1), Is.True);
                var node = window.View.NodesById[new NodeId("a")];
                node.SetPosition(new Rect(500, 40, 0, 0));
                window.View.graphViewChanged(new GraphViewChange { movedElements = new List<GraphElement> { node } });
                Assert.That(window.HasPendingChanges, Is.True);
                Assert.That(window.SaveAll(), Is.True);
                Assert.That(window.HasPendingChanges, Is.False);
                var saved = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
                Assert.That(saved.Success, Is.True);
                Assert.That(ChildIds(saved.Document, "root"), Is.EqualTo(new[] { "a", "c", "b" }));
                Assert.That(File.Exists(layoutPath), Is.True);
            }
            return WithWindow(Sequence(), Check);
        }

        [UnityTest]
        public IEnumerator ProblemsAreBadgedOnTheirNode()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                window.OpenFromPath(path, Registry());
                yield return null;
                var item = window.Problems.FirstOrDefault(problem => problem.Node == new NodeId("a"));
                Assert.That(item, Is.Not.Null, "A wait without its required ticks reports a problem on its node: "
                    + string.Join("; ", window.Problems.Select(problem => problem.Text)));
                Assert.That(window.View.NodesById[new NodeId("a")].ProblemText, Is.Not.Null.And.Not.Empty);
            }
            var root = new NodeDocument(new NodeId("root"), "aibt.core.memory-sequence", 1, children: new[] { new NodeId("a") },
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var missingTicks = new NodeDocument(new NodeId("a"), "aibt.stdlib.wait", 1, parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var empty = new TreeDocument(TreeDocument.CurrentFormat, 1, new TreeId("tree.test.invalid"), "Invalid", root.Id,
                new[] { root, missingTicks }, tags: TagSet.Empty, metadata: SemanticObject.Empty);
            return WithWindow(empty, Check);
        }

        [UnityTest]
        public IEnumerator OpeningAnotherTreeWhileDirtyNeverDiscardsOrBlocks()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, string path)
            {
                window.OpenFromPath(path, Registry());
                yield return null;
                Assert.That(window.MoveChild(new NodeId("c"), -1), Is.True);
                var other = Path.Combine(Path.GetDirectoryName(path), "other.aibt.json");
                File.Copy(path, other);
                window.OpenFromPath(other, Registry());
                yield return null;
                Assert.That(window.SourcePath, Is.EqualTo(path), "A dirty tree stays open until the user decides.");
                Assert.That(window.HasPendingChanges, Is.True);
                Assert.That(window.BannerText, Does.Contain("unsaved").IgnoreCase);
            }
            return WithWindow(Sequence(), Check);
        }

        private static IEnumerator WithWindow(TreeDocument tree, Func<BehaviorTreeGraphWindow, string, IEnumerator> action)
        {
            var directory = Path.Combine(Path.GetTempPath(), "aibt-graph-workflow-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "tree.aibt.json");
            File.WriteAllBytes(path, CanonicalTreeJson.Serialize(tree).Utf8);
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
    }
}
