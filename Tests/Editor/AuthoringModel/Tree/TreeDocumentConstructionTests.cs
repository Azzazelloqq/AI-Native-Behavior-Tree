using System;
using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.AuthoringModel.Tree
{
    public sealed class TreeDocumentConstructionTests
    {
        [Test]
        public void Construction_PreservesRelevantInvalidIntermediateState()
        {
            var invalidNode = new Authoring.NodeDocument(
                default,
                string.Empty,
                0,
                new[] { default(NodeId), default(NodeId) },
                new Authoring.SemanticObject(new[]
                {
                    new Authoring.SemanticProperty("duration", Authoring.SemanticValue.FromNumber(double.NaN)),
                }),
                new Authoring.NodeObserver("unknown", Array.Empty<string>()));

            var document = new Authoring.TreeDocument(
                null,
                -1,
                default,
                null,
                default,
                new[] { invalidNode, invalidNode });

            Assert.That(document.Format, Is.Null);
            Assert.That(document.FormatVersion, Is.EqualTo(-1));
            Assert.That(document.TreeId.IsValid, Is.False);
            Assert.That(document.Root.IsValid, Is.False);
            Assert.That(document.Name, Is.Null);
            Assert.That(document.Nodes, Has.Count.EqualTo(2), "duplicate identities must reach validation unchanged");
            Assert.That(document.Nodes[0].TypeId, Is.Empty);
            Assert.That(document.Nodes[0].TypeVersion, Is.Zero);
            Assert.That(document.Nodes[0].Children, Has.Count.EqualTo(2));
            Assert.That(document.Nodes[0].Observer.Mode, Is.EqualTo("unknown"));
            Assert.That(document.Nodes[0].Observer.WatchedKeys, Is.Empty);
            Assert.That(document.Nodes[0].Tags, Is.Null);
            Assert.That(document.Tags, Is.Null);
            Assert.That(document.Metadata, Is.Null);

            Assert.That(document.Nodes[0].Parameters.TryGetValue("duration", out var value), Is.True);
            Assert.That(value.TryGetNumber(out var number), Is.True);
            Assert.That(double.IsNaN(number), Is.True);
        }

        [Test]
        public void Construction_PreservesNullSemanticCollectionsForDiagnostics()
        {
            var node = new Authoring.NodeDocument(
                new NodeId("Root"),
                "example.action",
                1,
                parameters: null,
                tags: null);
            var document = new Authoring.TreeDocument(
                "aibt.tree",
                1,
                new TreeId("Tree"),
                "Tree",
                node.Id,
                new[] { node },
                tags: null,
                metadata: null);

            Assert.That(node.Parameters, Is.Null);
            Assert.That(node.Tags, Is.Null);
            Assert.That(document.Tags, Is.Null);
            Assert.That(document.Metadata, Is.Null);
        }

        [Test]
        public void Construction_RepresentsValidV1FieldsWithoutJsonOrUnityTypes()
        {
            var blackboard = new Authoring.BlackboardKeyDefinition(
                "target",
                "Target",
                Authoring.BlackboardTypeReference.BuiltIn(BlackboardValueType.EntityId),
                BlackboardScope.Tree,
                description: "Current target");
            var metadata = new Authoring.SemanticObject(new[]
            {
                new Authoring.SemanticProperty("owner", Authoring.SemanticValue.FromString("team-a")),
            });
            var root = new Authoring.NodeDocument(
                new NodeId("Root"),
                "aibt.core.memory-sequence",
                1,
                new[] { new NodeId("Child") },
                tags: new Authoring.TagSet(new[] { "combat" }));
            var child = new Authoring.NodeDocument(new NodeId("Child"), "example.action", 3);

            var document = new Authoring.TreeDocument(
                Authoring.TreeDocument.CurrentFormat,
                Authoring.TreeDocument.CurrentFormatVersion,
                new TreeId("Tree.Main"),
                "Main",
                root.Id,
                new[] { root, child },
                new[] { blackboard },
                "Description",
                new Authoring.TagSet(new[] { "example" }),
                metadata);

            Assert.That(document.Blackboard.Single(), Is.SameAs(blackboard));
            Assert.That(document.Nodes.Select(node => node.Id), Is.EqualTo(new[] { root.Id, child.Id }));
            Assert.That(document.Nodes[0].Children.Single(), Is.EqualTo(child.Id));
            Assert.That(document.Tags.Values, Is.EqualTo(new[] { "example" }));
            Assert.That(document.Metadata, Is.SameAs(metadata));
            Assert.That(document.Revision.IsValid, Is.True);
        }

        [Test]
        public void Collections_AreDefensiveCopiesAndChildrenKeepSemanticOrder()
        {
            var children = new[] { new NodeId("Zulu"), new NodeId("alpha"), new NodeId("Beta") };
            var nodes = new[] { new Authoring.NodeDocument(new NodeId("Root"), "type", 1, children) };
            var document = new Authoring.TreeDocument("aibt.tree", 1, new TreeId("Tree"), "Tree", new NodeId("Root"), nodes);

            children[0] = new NodeId("Changed");
            nodes[0] = new Authoring.NodeDocument(new NodeId("Changed"), "type", 1);

            Assert.That(document.Nodes[0].Id, Is.EqualTo(new NodeId("Root")));
            Assert.That(document.Nodes[0].Children, Is.EqualTo(new[]
            {
                new NodeId("Zulu"),
                new NodeId("alpha"),
                new NodeId("Beta"),
            }));
        }

        [Test]
        public void Model_PublicSurfaceHasNoPresentationOrRuntimeArtifacts()
        {
            var forbiddenFragments = new[]
            {
                "Position", "Coordinate", "Canvas", "Color", "Selection", "RuntimeIndex", "Cache", "UnityEngine",
            };
            var modelTypes = new[]
            {
                typeof(Authoring.TreeDocument),
                typeof(Authoring.NodeDocument),
                typeof(Authoring.NodeObserver),
                typeof(Authoring.SemanticValue),
                typeof(Authoring.SemanticObject),
                typeof(Authoring.TagSet),
            };

            foreach (var type in modelTypes)
            {
                foreach (var property in type.GetProperties())
                {
                    Assert.That(
                        forbiddenFragments.Any(fragment => property.Name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0
                            || property.PropertyType.FullName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0),
                        Is.False,
                        $"{type.Name}.{property.Name} leaks non-semantic state");
                }
            }
        }
    }
}
