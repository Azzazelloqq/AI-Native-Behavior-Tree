using System;
using NUnit.Framework;

namespace AIBT.Tests.Editor.AuthoringModel.Tree
{
    public sealed class TreeDocumentRevisionTests
    {
        [Test]
        public void SuccessfulMutations_IncrementRevisionExactlyOnce()
        {
            var root = new Authoring.NodeDocument(new NodeId("Root"), "type", 1);
            var document = Create(root);

            Assert.That(document.Revision.Value, Is.EqualTo(1));
            Assert.That(document.SetName("Renamed"), Is.True);
            Assert.That(document.Revision.Value, Is.EqualTo(2));
            Assert.That(document.SetFormat("future", 4), Is.True);
            Assert.That(document.Revision.Value, Is.EqualTo(3), "changing two format fields is one mutation");
            document.AddNode(new Authoring.NodeDocument(new NodeId("Child"), "action", 1));
            Assert.That(document.Revision.Value, Is.EqualTo(4));
            Assert.That(document.ReplaceNodeAt(0, root.WithChildren(new[] { new NodeId("Child") })), Is.True);
            Assert.That(document.Revision.Value, Is.EqualTo(5));
            Assert.That(document.RemoveNodeAt(1), Is.True);
            Assert.That(document.Revision.Value, Is.EqualTo(6));
        }

        [Test]
        public void NoOps_DoNotIncrementRevision()
        {
            var root = new Authoring.NodeDocument(new NodeId("Root"), "type", 1);
            var document = Create(root);

            Assert.That(document.SetName(document.Name), Is.False);
            Assert.That(document.SetDescription(document.Description), Is.False);
            Assert.That(document.SetRoot(document.Root), Is.False);
            Assert.That(document.SetIdentity(document.TreeId), Is.False);
            Assert.That(document.SetFormat(document.Format, document.FormatVersion), Is.False);
            Assert.That(document.SetTags(null), Is.False);
            Assert.That(document.SetMetadata(null), Is.False);
            Assert.That(document.SetBlackboard(document.Blackboard), Is.False);
            Assert.That(document.ReplaceNodeAt(0, root), Is.False);
            Assert.That(document.RemoveNodeAt(7), Is.False);
            Assert.That(document.Revision.Value, Is.EqualTo(1));
        }

        [Test]
        public void ReplacingNullSemanticCollectionsWithEmptyValuesIsAMutation()
        {
            var root = new Authoring.NodeDocument(new NodeId("Root"), "type", 1);
            var document = Create(root);

            Assert.That(document.SetTags(Authoring.TagSet.Empty), Is.True);
            Assert.That(document.Revision.Value, Is.EqualTo(2));
            Assert.That(document.SetMetadata(Authoring.SemanticObject.Empty), Is.True);
            Assert.That(document.Revision.Value, Is.EqualTo(3));
        }

        [Test]
        public void StructurallyEqualBlackboardReplacementIsANoOp()
        {
            var root = new Authoring.NodeDocument(new NodeId("Root"), "type", 1);
            var original = new Authoring.BlackboardKeyDefinition(
                "speed",
                "Speed",
                Authoring.BlackboardTypeReference.BuiltIn(BlackboardValueType.Float32),
                BlackboardScope.Tree,
                Authoring.BlackboardDefaultValue.Float32(3.5f),
                "Movement speed");
            var document = new Authoring.TreeDocument(
                "aibt.tree",
                1,
                new TreeId("Tree"),
                "Tree",
                root.Id,
                new[] { root },
                new[] { original });
            var replacement = new Authoring.BlackboardKeyDefinition(
                "speed",
                "Speed",
                Authoring.BlackboardTypeReference.BuiltIn(BlackboardValueType.Float32),
                BlackboardScope.Tree,
                Authoring.BlackboardDefaultValue.Float32(3.5f),
                "Movement speed");

            Assert.That(document.SetBlackboard(new[] { replacement }), Is.False);
            Assert.That(document.Revision.Value, Is.EqualTo(1));
        }

        [Test]
        public void FailedOverflowMutation_LeavesDocumentUnchanged()
        {
            var root = new Authoring.NodeDocument(new NodeId("Root"), "type", 1);
            var document = new Authoring.TreeDocument(
                "aibt.tree",
                1,
                new TreeId("Tree"),
                "Before",
                root.Id,
                new[] { root },
                revision: new Revision(ulong.MaxValue));

            Assert.Throws<InvalidOperationException>(() => document.SetName("After"));
            Assert.That(document.Name, Is.EqualTo("Before"));
            Assert.That(document.Revision.Value, Is.EqualTo(ulong.MaxValue));
        }

        private static Authoring.TreeDocument Create(Authoring.NodeDocument root)
        {
            return new Authoring.TreeDocument(
                "aibt.tree",
                1,
                new TreeId("Tree"),
                "Tree",
                root.Id,
                new[] { root });
        }
    }
}
