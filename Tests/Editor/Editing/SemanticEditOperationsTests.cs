using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Editing
{
    public sealed class SemanticEditOperationsTests
    {
        [Test]
        public void AddNodeAppendsToParentChildrenAndLeavesOriginalUnchanged()
        {
            var original = MinimalRootOnlyTree();

            var leaf = NewNode("leaf", "aibt.core.test-leaf");
            var edited = SemanticEditOperations.AddNode(original, leaf, new NodeId("root"));

            Assert.That(original.Nodes.Count, Is.EqualTo(1), "The original document must not be mutated.");
            Assert.That(edited.Nodes.Count, Is.EqualTo(2));
            var root = edited.Nodes.Single(n => n.Id == new NodeId("root"));
            Assert.That(root.Children, Is.EqualTo(new[] { new NodeId("leaf") }));
        }

        [Test]
        public void RemoveNodeDropsSubtreeAndCleansUpParentReference()
        {
            var document = ThreeLevelTree();

            var edited = SemanticEditOperations.RemoveNode(document, new NodeId("guard"));

            Assert.That(edited.Nodes.Any(n => n.Id == new NodeId("guard")), Is.False);
            Assert.That(edited.Nodes.Any(n => n.Id == new NodeId("leaf")), Is.False, "Removing a node removes its whole subtree.");
            var root = edited.Nodes.Single(n => n.Id == new NodeId("root"));
            Assert.That(root.Children, Is.Empty);
        }

        [Test]
        public void ConnectAndDisconnectRoundTrip()
        {
            var document = MinimalRootOnlyTree();
            var leaf = NewNode("leaf", "aibt.core.test-leaf");
            document = SemanticEditOperations.AddNode(document, leaf, new NodeId("root"));
            document = SemanticEditOperations.Disconnect(document, new NodeId("root"), new NodeId("leaf"));

            var root = document.Nodes.Single(n => n.Id == new NodeId("root"));
            Assert.That(root.Children, Is.Empty);
            Assert.That(document.Nodes.Any(n => n.Id == new NodeId("leaf")), Is.True, "Disconnect detaches, it does not delete.");

            var reconnected = SemanticEditOperations.Connect(document, new NodeId("root"), new NodeId("leaf"));
            Assert.That(reconnected.Nodes.Single(n => n.Id == new NodeId("root")).Children, Is.EqualTo(new[] { new NodeId("leaf") }));
        }

        [Test]
        public void SetParameterReplacesExistingValueAndAddsNewOne()
        {
            var document = MinimalRootOnlyTree();
            var leaf = NewNode("leaf", "aibt.core.test-leaf", parameters: OneBooleanParameter(true));
            document = SemanticEditOperations.AddNode(document, leaf, new NodeId("root"));

            var updated = SemanticEditOperations.SetParameter(document, new NodeId("leaf"), "enabled", SemanticValue.FromBoolean(false));

            var updatedLeaf = updated.Nodes.Single(n => n.Id == new NodeId("leaf"));
            Assert.That(updatedLeaf.Parameters.TryGetValue("enabled", out var value), Is.True);
            Assert.That(value.TryGetBoolean(out var flag), Is.True);
            Assert.That(flag, Is.False);

            var withNewParameter = SemanticEditOperations.SetParameter(updated, new NodeId("leaf"), "extra", SemanticValue.FromInt64(7));
            var finalLeaf = withNewParameter.Nodes.Single(n => n.Id == new NodeId("leaf"));
            Assert.That(finalLeaf.Parameters.Properties.Count, Is.EqualTo(2));
        }

        private static TreeDocument MinimalRootOnlyTree()
        {
            var root = NewNode("root", "aibt.core.memory-sequence");
            return NewTree(new[] { root });
        }

        internal static TreeDocument ThreeLevelTree()
        {
            var root = NewNode("root", "aibt.core.memory-sequence", children: new[] { new NodeId("guard") });
            var guard = NewNode("guard", "aibt.core.inverter", children: new[] { new NodeId("leaf") });
            var leaf = NewNode("leaf", "aibt.core.test-leaf", parameters: OneBooleanParameter(true));
            return NewTree(new[] { root, guard, leaf });
        }

        /// <summary>
        /// AIBT.Authoring.CanonicalTreeJson's ValidateRepresentable (used by both Serialize and,
        /// transitively, ReferenceCompiler.Compile) requires every node's Parameters and the
        /// document's Tags to be non-null -- ordinary NodeDocument/TreeDocument construction
        /// leaves them null when omitted, so every fixture here goes through these helpers
        /// instead of the raw constructors directly.
        /// </summary>
        internal static NodeDocument NewNode(string id, string typeId, IEnumerable<NodeId> children = null, SemanticObject parameters = null)
        {
            return new NodeDocument(new NodeId(id), typeId, 1, children, parameters ?? SemanticObject.Empty, null, null, null, TagSet.Empty);
        }

        internal static SemanticObject OneBooleanParameter(bool value)
        {
            return new SemanticObject(new[] { new SemanticProperty("enabled", SemanticValue.FromBoolean(value)) });
        }

        internal static TreeDocument NewTree(IEnumerable<NodeDocument> nodes)
        {
            return new TreeDocument(
                "aibt.tree", 1, new TreeId("tree.test.editing"), "Editing Test", new NodeId("root"), nodes,
                blackboard: null, description: null, tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }
    }
}
