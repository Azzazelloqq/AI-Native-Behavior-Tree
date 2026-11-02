using System;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Editor.Graph;
using AIBT.Editor.Layout;
using AIBT.Editor.Organization;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Graph
{
    public sealed class GraphEditSessionTests
    {
        [Test]
        public void IncompleteTreeCanBeSavedAndReopenedButCannotCompileUntilCompleted()
        {
            var directory = Path.Combine(Path.GetTempPath(), "aibt-draft-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "tree.aibt.json");
                var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
                var session = new GraphEditSession(CreateTree(), registry, Options(), null);
                Assert.That(session.CanCompile, Is.False);
                Assert.That(session.Diagnostics.Count, Is.GreaterThan(0));
                session.Save(path);
                var bytes = File.ReadAllBytes(path);
                var parsed = CanonicalTreeJson.Parse(bytes, path);
                Assert.That(parsed.Success, Is.True);
                var reopened = new GraphEditSession(parsed.Document, registry, Options(), bytes);
                Assert.That(reopened.CanCompile, Is.False);
                Assert.That(reopened.Diagnostics.Count, Is.GreaterThan(0));
                var parameters = new SemanticObject(new[] { new SemanticProperty("ticks", SemanticValue.FromUInt64(1)) });
                var child = new NodeDocument(new NodeId("wait"), "aibt.stdlib.wait", 1,
                    parameters: parameters, tags: TagSet.Empty);
                reopened.Apply(tree => SemanticEditOperations.AddNode(tree, child, new NodeId("root")));
                Assert.That(reopened.CanCompile, Is.True, string.Join("; ", reopened.Diagnostics));
                reopened.Undo();
                Assert.That(reopened.CanCompile, Is.False);
                reopened.Redo();
                Assert.That(reopened.CanCompile, Is.True);
                reopened.Apply(tree => SemanticEditOperations.RemoveParameter(tree, new NodeId("wait"), "ticks"));
                Assert.That(reopened.CanCompile, Is.False);
                reopened.Undo();
                Assert.That(reopened.CanCompile, Is.True);
                reopened.Apply(tree => SemanticEditOperations.SetRoot(tree, new NodeId("wait")));
                Assert.That(reopened.Document.Root, Is.EqualTo(new NodeId("wait")));
                Assert.That(reopened.Document.Nodes.Single(node => node.Id == new NodeId("root")).Children, Is.Empty);
                reopened.Undo();
                Assert.That(reopened.Document.Root, Is.EqualTo(new NodeId("root")));
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void BlackboardAndBindingsSurviveSaveUndoRedoAndDanglingKeyRemoval()
        {
            var path = Path.Combine(Path.GetTempPath(), "aibt-blackboard-" + Guid.NewGuid().ToString("N") + ".aibt.json");
            try
            {
                var root = new NodeDocument(new NodeId("root"), "aibt.core.inverter", 1,
                    parameters: SemanticObject.Empty, tags: TagSet.Empty);
                var tree = TreeDocument.CreateVersion2(new TreeId("tree.test.blackboard"), "Blackboard", root.Id,
                    new[] { root }, null, null, tags: TagSet.Empty, metadata: SemanticObject.Empty);
                var session = new GraphEditSession(tree, NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry, Options(), null);
                var key = new BlackboardKeyDefinition("target", "target", BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32),
                    defaultValue: BlackboardDefaultValue.Int32(42));
                session.Apply(document => SemanticEditOperations.SetBlackboardKey(document, key));
                session.Apply(document => SemanticEditOperations.SetBinding(document, root.Id, "input", key.Id));
                session.Undo();
                Assert.That(session.Document.Nodes.Single().Bindings, Is.Null);
                session.Redo();
                session.Save(path);
                var reopened = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
                Assert.That(reopened.Success, Is.True, string.Join("; ", reopened.Diagnostics));
                Assert.That(reopened.Document.Blackboard.Single().Name, Is.EqualTo("target"));
                Assert.That(reopened.Document.Blackboard.Single().DefaultValue.TryGetRuntimeValue(out var value), Is.True);
                Assert.That(value.TryGetInt32(out var number) && number == 42, Is.True);
                Assert.That(reopened.Document.Nodes.Single().Bindings.Values["input"], Is.EqualTo("target"));
                session.Apply(document => SemanticEditOperations.RemoveBlackboardKey(document, "target"));
                Assert.That(session.Document.Blackboard, Is.Empty);
                Assert.That(session.Document.Nodes.Single().Bindings.Values["input"], Is.EqualTo("target"));
                session.Undo();
                Assert.That(session.Document.Blackboard.Single().Id, Is.EqualTo("target"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void LayoutUndoPreservesHistoryWhenNodesAreAddedAndRestored()
        {
            var root = new NodeId("root");
            var child = new NodeId("child");
            var initial = new System.Collections.Generic.Dictionary<NodeId, LayoutNodePlacement>
            {
                [root] = new LayoutNodePlacement(new LayoutPoint(0, 0))
            };
            var moved = new System.Collections.Generic.Dictionary<NodeId, LayoutNodePlacement>
            {
                [root] = new LayoutNodePlacement(new LayoutPoint(100, 50))
            };
            var history = new LayoutHistory(new LayoutDocument(new TreeId("tree.test.layout"), LayoutDirection.TopToBottom, initial));
            history.Do(new LayoutDocument(new TreeId("tree.test.layout"), LayoutDirection.TopToBottom, moved));
            var withChild = new System.Collections.Generic.Dictionary<NodeId, LayoutNodePlacement>(moved)
            {
                [child] = new LayoutNodePlacement(new LayoutPoint(20, 30))
            };
            history.IncludeNodes(withChild);
            var undo = history.Undo();
            Assert.That(undo.Nodes[root].Position, Is.EqualTo(new LayoutPoint(0, 0)));
            Assert.That(undo.Nodes[child].Position, Is.EqualTo(new LayoutPoint(20, 30)));
            var redo = history.Redo();
            Assert.That(redo.Nodes[root].Position, Is.EqualTo(new LayoutPoint(100, 50)));
            Assert.That(redo.Nodes[child].Position, Is.EqualTo(new LayoutPoint(20, 30)));
        }

        [Test]
        public void ReorderingAndReattachingBranchesPersistsSemanticOrder()
        {
            var path = Path.Combine(Path.GetTempPath(), "aibt-draft-" + Guid.NewGuid().ToString("N") + ".aibt.json");
            try
            {
                var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
                var rootId = new NodeId("root");
                var firstId = new NodeId("first");
                var secondId = new NodeId("second");
                var root = new NodeDocument(rootId, "aibt.core.memory-sequence", 1,
                    new[] { firstId, secondId }, SemanticObject.Empty, tags: TagSet.Empty);
                var parameters = new SemanticObject(new[] { new SemanticProperty("ticks", SemanticValue.FromUInt64(1)) });
                var first = new NodeDocument(firstId, "aibt.stdlib.wait", 1, parameters: parameters, tags: TagSet.Empty);
                var second = new NodeDocument(secondId, "aibt.stdlib.wait", 1, parameters: parameters, tags: TagSet.Empty);
                var tree = new TreeDocument(TreeDocument.CurrentFormat, 1, new TreeId("tree.test.order"), "Order",
                    rootId, new[] { root, first, second }, tags: TagSet.Empty, metadata: SemanticObject.Empty);
                var session = new GraphEditSession(tree, registry, Options(), null);
                session.Apply(document => SemanticEditOperations.Disconnect(document, rootId, firstId));
                session.Apply(document => SemanticEditOperations.Connect(document, rootId, firstId));
                Assert.That(session.Document.Nodes.Single(node => node.Id == rootId).Children,
                    Is.EqualTo(new[] { secondId, firstId }));
                session.Save(path);
                var reopened = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
                Assert.That(reopened.Success, Is.True);
                Assert.That(reopened.Document.Nodes.Single(node => node.Id == rootId).Children,
                    Is.EqualTo(new[] { secondId, firstId }));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void RestoredDraftRemainsDirtyAndRetainsOriginalFileBaseline()
        {
            var path = Path.Combine(Path.GetTempPath(), "aibt-draft-" + Guid.NewGuid().ToString("N") + ".aibt.json");
            try
            {
                var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
                var session = new GraphEditSession(CreateTree(), registry, Options(), null);
                session.Save(path);
                var baseline = File.ReadAllBytes(path);
                var child = new NodeDocument(new NodeId("wait"), "aibt.stdlib.wait", 1,
                    parameters: SemanticObject.Empty, tags: TagSet.Empty);
                session.Apply(tree => SemanticEditOperations.AddNode(tree, child, new NodeId("root")));
                var draft = CanonicalTreeJson.Parse(CanonicalTreeJson.Serialize(session.Document).Utf8).Document;
                var restored = new GraphEditSession(draft, registry, Options(), baseline);
                Assert.That(restored.IsDirty, Is.True);
                File.WriteAllText(path, "external edit");
                Assert.Throws<IOException>(() => restored.Save(path));
                Assert.That(File.ReadAllText(path), Is.EqualTo("external edit"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void SaveDoesNotOverwriteExternalChanges()
        {
            var path = Path.Combine(Path.GetTempPath(), "aibt-draft-" + Guid.NewGuid().ToString("N") + ".aibt.json");
            try
            {
                var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
                var session = new GraphEditSession(CreateTree(), registry, Options(), null);
                session.Save(path);
                File.WriteAllText(path, "external edit");
                Assert.Throws<IOException>(() => session.Save(path));
                Assert.That(File.ReadAllText(path), Is.EqualTo("external edit"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static TreeDocument CreateTree()
        {
            var root = new NodeDocument(new NodeId("root"), "aibt.core.inverter", 1,
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            return new TreeDocument(TreeDocument.CurrentFormat, 1, new TreeId("tree.test.draft"), "Draft",
                root.Id, new[] { root }, tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static ReferenceCompilerOptions Options() => new ReferenceCompilerOptions(
            "trees/draft.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0));
    }
}
