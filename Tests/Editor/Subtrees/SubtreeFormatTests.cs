using System;
using System.IO;
using System.Linq;
using System.Text;
using AIBT.Authoring;
using NUnit.Framework;
using static AIBT.Tests.Editor.Subtrees.SubtreeTestKit;

namespace AIBT.Tests.Editor.Subtrees
{
    /// <summary>ADR-P11-001 §4.1/§4.2: tree format version 3 (interface and reference nodes) and its canonical form.</summary>
    public sealed class SubtreeFormatTests
    {
        private static readonly string[] CanonicalV3Fixtures =
        {
            "Subtrees/alert-raise.aibt.json",
            "Subtrees/reactive-parent.aibt.json",
            "Subtrees/inverted-check.aibt.json",
            "Subtrees/twice-parent.aibt.json",
        };

        private static readonly string[] CanonicalV1V2Fixtures =
        {
            "patrol-react.aibt.json",
            "SubtreeHandCopies/reactive-parent.expanded.aibt.json",
            "SubtreeHandCopies/twice-parent.expanded.aibt.json",
        };

        [TestCaseSource(nameof(CanonicalV3Fixtures))]
        public void CanonicalVersion3Fixture_RoundTripsByteForByte(string relative)
        {
            var bytes = File.ReadAllBytes(GoldenTree(relative));
            var read = CanonicalTreeJson.Parse(bytes, relative);
            Assert.That(read.Success, Is.True, Describe(read.Diagnostics));
            Assert.That(read.Document.FormatVersion, Is.EqualTo(TreeDocument.SubtreeFormatVersion));

            var written = CanonicalTreeJson.Serialize(read.Document);
            Assert.That(written.Success, Is.True, Describe(written.Diagnostics));
            Assert.That(Encoding.UTF8.GetString(written.Utf8), Is.EqualTo(Encoding.UTF8.GetString(bytes)));
        }

        [TestCaseSource(nameof(CanonicalV1V2Fixtures))]
        public void CanonicalVersion1And2Fixtures_StillRoundTripByteForByte(string relative)
        {
            var bytes = File.ReadAllBytes(GoldenTree(relative));
            var read = CanonicalTreeJson.Parse(bytes, relative);
            Assert.That(read.Success, Is.True, Describe(read.Diagnostics));
            Assert.That(read.Document.FormatVersion, Is.LessThanOrEqualTo(TreeDocument.LatestFormatVersion));
            Assert.That(read.Document.Interface, Is.Null);
            Assert.That(read.Document.Nodes.Any(node => node.IsSubtreeReference), Is.False);

            Assert.That(Encoding.UTF8.GetString(CanonicalTreeJson.Serialize(read.Document).Utf8), Is.EqualTo(Encoding.UTF8.GetString(bytes)));
        }

        [Test]
        public void Version3_ReadsInterfaceAndReferenceNodes()
        {
            var document = Parse(Tree(
                "tests.p11.parent",
                "root",
                NodeList(
                    Seq("root", "use"),
                    Ref("use", "tests.p11.child", 4, Keys(Bind("target", "goal"), Constant("speed", "2.5"), Constant("armed", "true")), "Use Child")),
                Keys(BoolKey("goal")),
                Interface(7, "zeta", "alpha")));

            Assert.That(document.FormatVersion, Is.EqualTo(3));
            Assert.That(document.Interface.Version, Is.EqualTo(7u));
            Assert.That(document.Interface.Keys, Is.EqualTo(new[] { "alpha", "zeta" }), "Interface keys are kept sorted.");

            var use = NodeById(document, "use");
            Assert.That(use.IsSubtreeReference, Is.True);
            Assert.That(use.TypeId, Is.Null);
            Assert.That(use.DisplayName, Is.EqualTo("Use Child"));
            Assert.That(use.Subtree.TreeId, Is.EqualTo(new TreeId("tests.p11.child")));
            Assert.That(use.Subtree.InterfaceVersion, Is.EqualTo(4u));
            Assert.That(use.Subtree.Keys.Select(item => item.InterfaceKey), Is.EqualTo(new[] { "armed", "speed", "target" }));
            Assert.That(use.Subtree.TryGetBinding("target", out var target), Is.True);
            Assert.That(target.IsConstant, Is.False);
            Assert.That(target.ParentKey, Is.EqualTo("goal"));
            Assert.That(use.Subtree.TryGetBinding("speed", out var speed), Is.True);
            Assert.That(speed.IsConstant, Is.True);
            Assert.That(speed.Constant.TryGetNumber(out var value), Is.True);
            Assert.That(value, Is.EqualTo(2.5d));
            Assert.That(NodeById(document, "root").IsSubtreeReference, Is.False);
            Assert.That(document.UsesSubtreeFeatures, Is.True);
        }

        [Test]
        public void Version3_WritesCanonicalMemberOrder()
        {
            var document = Parse(Tree(
                "tests.p11.parent",
                "root",
                NodeList(
                    Seq("root", "use"),
                    "'use': { 'subtree': { 'keys': { 'b': { 'value': 1 }, 'a': 'goal' }, 'interfaceVersion': 2, 'treeId': 'tests.p11.child' }, 'description': 'd', 'displayName': 'n', 'tags': ['y', 'x'] }"),
                Keys(BoolKey("goal")),
                "{ 'keys': ['k2', 'k1'], 'version': 3 }"));

            var text = Encoding.UTF8.GetString(CanonicalTreeJson.Serialize(document).Utf8);
            var expected = Q(string.Join("\n", new[]
            {
                "{",
                "  'format': 'aibt.tree',",
                "  'formatVersion': 3,",
                "  'treeId': 'tests.p11.parent',",
                "  'name': 'tests.p11.parent',",
                "  'root': 'root',",
                "  'interface': {",
                "    'version': 3,",
                "    'keys': [",
                "      'k1',",
                "      'k2'",
                "    ]",
                "  },",
                "  'blackboard': {",
                "    'goal': {",
                "      'type': 'Bool',",
                "      'default': false",
                "    }",
                "  },",
                "  'nodes': {",
                "    'root': {",
                "      'type': 'aibt.core.memory-sequence',",
                "      'typeVersion': 1,",
                "      'children': [",
                "        'use'",
                "      ]",
                "    },",
                "    'use': {",
                "      'displayName': 'n',",
                "      'description': 'd',",
                "      'subtree': {",
                "        'treeId': 'tests.p11.child',",
                "        'interfaceVersion': 2,",
                "        'keys': {",
                "          'a': 'goal',",
                "          'b': {",
                "            'value': 1",
                "          }",
                "        }",
                "      },",
                "      'tags': [",
                "        'x',",
                "        'y'",
                "      ]",
                "    }",
                "  }",
                "}",
                string.Empty,
            }));
            Assert.That(text, Is.EqualTo(expected));
            Assert.That(Encoding.UTF8.GetString(CanonicalTreeJson.Serialize(CanonicalTreeJson.Parse(text, "again.aibt").Document).Utf8), Is.EqualTo(text));
        }

        [Test]
        public void InterfaceIsRejectedBeforeVersion3()
        {
            foreach (var version in new[] { 1, 2 })
            {
                var json = Tree("tests.p11.v", "leaf", Leaf("leaf"), Keys(BoolKey("goal")), Interface(1, "goal"), version);
                var read = CanonicalTreeJson.Parse(json, "v.aibt");
                Assert.That(read.Success, Is.False, "formatVersion " + version);
                Assert.That(read.Diagnostics.Single().Code, Is.EqualTo(TreeJsonDiagnosticCodes.SchemaViolation));
                Assert.That(read.Diagnostics.Single().Location.JsonPointer, Is.EqualTo("/interface"));
            }
        }

        [Test]
        public void ReferenceNodeIsRejectedBeforeVersion3()
        {
            foreach (var version in new[] { 1, 2 })
            {
                var json = Tree("tests.p11.v", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child")), formatVersion: version);
                var read = CanonicalTreeJson.Parse(json, "v.aibt");
                Assert.That(read.Success, Is.False, "formatVersion " + version);
                Assert.That(read.Diagnostics.Single().Code, Is.EqualTo(TreeJsonDiagnosticCodes.SchemaViolation));
            }
        }

        [TestCase("'use': { 'type': 'aibt.test.success', 'typeVersion': 1, 'subtree': { 'treeId': 'c', 'interfaceVersion': 1, 'keys': {} } }")]
        [TestCase("'use': { 'subtree': { 'treeId': 'c', 'interfaceVersion': 1, 'keys': {} }, 'children': [] }")]
        [TestCase("'use': { 'subtree': { 'treeId': 'c', 'interfaceVersion': 1, 'keys': {} }, 'parameters': {} }")]
        [TestCase("'use': { 'subtree': { 'treeId': 'c', 'interfaceVersion': 0, 'keys': {} } }")]
        [TestCase("'use': { 'subtree': { 'treeId': 'c', 'interfaceVersion': 1 } }")]
        [TestCase("'use': { 'subtree': { 'treeId': 'c', 'interfaceVersion': 1, 'keys': {}, 'extra': 1 } }")]
        [TestCase("'use': { 'subtree': { 'treeId': 'c', 'interfaceVersion': 1, 'keys': { 'a': 3 } } }")]
        [TestCase("'use': { 'subtree': { 'treeId': 'c', 'interfaceVersion': 1, 'keys': { 'a': { 'value': 1, 'more': 2 } } } }")]
        public void MalformedReferenceNode_IsASchemaViolation(string node)
        {
            var read = CanonicalTreeJson.Parse(Tree("tests.p11.v", "root", NodeList(Seq("root", "use"), Q(node))), "v.aibt");
            Assert.That(read.Success, Is.False);
            Assert.That(read.Diagnostics.Single().Code, Is.EqualTo(TreeJsonDiagnosticCodes.SchemaViolation));
        }

        [TestCase("{ 'version': 0, 'keys': [] }")]
        [TestCase("{ 'version': 1 }")]
        [TestCase("{ 'version': 1, 'keys': ['a', 'a'] }")]
        [TestCase("{ 'version': 1, 'keys': [], 'extra': true }")]
        public void MalformedInterface_IsASchemaViolation(string subtreeInterface)
        {
            var read = CanonicalTreeJson.Parse(Tree("tests.p11.v", "leaf", Leaf("leaf"), null, Q(subtreeInterface)), "v.aibt");
            Assert.That(read.Success, Is.False);
            Assert.That(read.Diagnostics.Single().Code, Is.EqualTo(TreeJsonDiagnosticCodes.SchemaViolation));
        }

        [Test]
        public void ModelTypes_AreValueEqualAndPreserveTheReference()
        {
            var reference = new SubtreeReference(
                new TreeId("tests.p11.child"),
                2,
                new[] { SubtreeKeyBinding.ToConstant("b", SemanticValue.FromBoolean(true)), SubtreeKeyBinding.ToParentKey("a", "goal") });
            Assert.That(reference.Keys.Select(item => item.InterfaceKey), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(reference, Is.EqualTo(new SubtreeReference(
                new TreeId("tests.p11.child"),
                2,
                new[] { SubtreeKeyBinding.ToParentKey("a", "goal"), SubtreeKeyBinding.ToConstant("b", SemanticValue.FromBoolean(true)) })));
            Assert.That(() => new SubtreeReference(
                    new TreeId("tests.p11.child"),
                    1,
                    new[] { SubtreeKeyBinding.ToParentKey("a", "x"), SubtreeKeyBinding.ToParentKey("a", "y") }),
                Throws.ArgumentException);

            var node = NodeDocument.CreateSubtreeReference(new NodeId("use"), reference, "Use");
            Assert.That(node.IsSubtreeReference, Is.True);
            Assert.That(node.WithDetails("Other", "d", TagSet.Empty).Subtree, Is.EqualTo(reference));
            Assert.That(node, Is.Not.EqualTo(NodeDocument.CreateSubtreeReference(new NodeId("use"),
                new SubtreeReference(new TreeId("tests.p11.other"), 2, reference.Keys), "Use")));

            var subtreeInterface = new SubtreeInterface(3, new[] { "b", "a" });
            Assert.That(subtreeInterface.Keys, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(subtreeInterface, Is.EqualTo(new SubtreeInterface(3, new[] { "a", "b" })));
            Assert.That(subtreeInterface, Is.Not.EqualTo(new SubtreeInterface(4, new[] { "a", "b" })));
        }

        [Test]
        public void ADocumentWithoutSubtreeFeatures_DoesNotUseThem()
        {
            var document = Parse(Tree("tests.p11.plain", "leaf", Leaf("leaf"), formatVersion: 2));
            Assert.That(document.UsesSubtreeFeatures, Is.False);
            Assert.That(SubtreeExpander.RequiresExpansion(document), Is.False);
            var version3 = Parse(Tree("tests.p11.plain", "leaf", Leaf("leaf")));
            Assert.That(SubtreeExpander.RequiresExpansion(version3), Is.False, "Format 3 alone is not a reason to expand.");
        }

        private static string GoldenTree(string relative)
            => PackagePath(new[] { "Tests", "Fixtures", "Golden", "Trees" }.Concat(relative.Split('/')).ToArray());
    }
}
