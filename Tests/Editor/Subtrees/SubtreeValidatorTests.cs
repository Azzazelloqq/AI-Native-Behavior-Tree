using System;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;
using static AIBT.Tests.Editor.Subtrees.SubtreeTestKit;

namespace AIBT.Tests.Editor.Subtrees
{
    /// <summary>ADR-P11-001 §4.6, validator range: AIBT2047-AIBT2050 (rules checkable on one document).</summary>
    public sealed class SubtreeValidatorTests
    {
        private const string AgentContracts = "{ 'agent': { 'contractId': 'tests.p11.vitals', 'contractVersion': 1 } }";
        private const string AgentKey = "'hp': { 'type': 'Int32', 'typeVersion': 1, 'scope': 'agent', 'default': 0 }";

        [Test]
        public void ValidVersion3Documents_HaveNoSubtreeDiagnostics()
        {
            var parent = Parse(Tree("tests.p11.parent", "root",
                NodeList(Seq("root", "use"), Ref("use", "tests.p11.child", 1, Keys(Bind("a", "goal"), Constant("b", "true")))),
                Keys(BoolKey("goal"))));
            var child = Parse(Tree("tests.p11.child", "leaf", Leaf("leaf"), Keys(BoolKey("a"), BoolKey("b")), Interface(1, "a", "b")));

            Assert.That(SubtreeCodes(Validate(parent)), Is.Empty, Describe(Validate(parent)));
            Assert.That(SubtreeCodes(Validate(child)), Is.Empty, Describe(Validate(child)));
        }

        [Test]
        public void AIBT2047_ReferenceOutsideVersion3()
        {
            var reference = NodeDocument.CreateSubtreeReference(new NodeId("use"), new SubtreeReference(new TreeId("tests.p11.child"), 1));
            var root = new NodeDocument(new NodeId("root"), SubtreeTestKit.Sequence, 1, new[] { reference.Id }, SemanticObject.Empty, tags: TagSet.Empty);
            var document = new TreeDocument(TreeDocument.CurrentFormat, TreeDocument.LatestFormatVersion, new TreeId("tests.p11.v2"), "V2",
                root.Id, new[] { root, reference }, tags: TagSet.Empty, metadata: SemanticObject.Empty);

            AssertHas(Validate(document), TreeValidationDiagnosticCodes.SubtreeReferenceShape, "/nodes/use/subtree");
        }

        [Test]
        public void AIBT2047_ReferenceWithMembersOfAnOrdinaryNode()
        {
            var reference = NodeDocument.CreateSubtreeReference(new NodeId("use"), new SubtreeReference(new TreeId("tests.p11.child"), 1))
                .WithChildren(new[] { new NodeId("leaf") });
            var leaf = new NodeDocument(new NodeId("leaf"), SubtreeTestKit.Success, 1, Array.Empty<NodeId>(), SemanticObject.Empty, tags: TagSet.Empty);
            var root = new NodeDocument(new NodeId("root"), SubtreeTestKit.Sequence, 1, new[] { reference.Id }, SemanticObject.Empty, tags: TagSet.Empty);
            var document = Version3(root, reference, leaf);

            AssertHas(Validate(document), TreeValidationDiagnosticCodes.SubtreeReferenceShape, "/nodes/use");
        }

        [Test]
        public void AIBT2047_ReferenceIdWithAColon()
        {
            var document = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "a:b"), Ref("a:b", "tests.p11.child"))));
            AssertHas(Validate(document), TreeValidationDiagnosticCodes.SubtreeReferenceShape, "/nodes/a:b");
        }

        [Test]
        public void AIBT2048_BindingToAnUndeclaredKey()
        {
            var document = Parse(Tree("tests.p11.parent", "root",
                NodeList(Seq("root", "use"), Ref("use", "tests.p11.child", 1, Bind("a", "missing"))), Keys(BoolKey("goal"))));
            AssertHas(Validate(document), TreeValidationDiagnosticCodes.SubtreeKeyTarget, "/nodes/use/subtree/keys/a");
        }

        [Test]
        public void AIBT2048_BindingToANonTreeKey()
        {
            var document = Parse(Tree("tests.p11.parent", "root",
                NodeList(Seq("root", "use"), Ref("use", "tests.p11.child", 1, Bind("a", "hp"))), AgentKey, contracts: AgentContracts));
            AssertHas(Validate(document), TreeValidationDiagnosticCodes.SubtreeKeyTarget, "/nodes/use/subtree/keys/a");
        }

        [Test]
        public void AIBT2049_RootIsAReference()
        {
            var document = Parse(Tree("tests.p11.parent", "use", Ref("use", "tests.p11.child")));
            AssertHas(Validate(document), TreeValidationDiagnosticCodes.SubtreeReferenceRoot, "/root");
        }

        [Test]
        public void AIBT2050_InterfaceOutsideVersion3()
        {
            var leaf = new NodeDocument(new NodeId("leaf"), SubtreeTestKit.Success, 1, Array.Empty<NodeId>(), SemanticObject.Empty, tags: TagSet.Empty);
            var key = new BlackboardKeyDefinition("a", "a", BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool), BlackboardScope.Tree, BlackboardDefaultValue.Bool(false));
            var document = new TreeDocument(TreeDocument.CurrentFormat, TreeDocument.LatestFormatVersion, new TreeId("tests.p11.v2"), "V2",
                leaf.Id, new[] { leaf }, new[] { key }, null, TagSet.Empty, SemanticObject.Empty, default, null, null, new SubtreeInterface(1, new[] { "a" }));

            AssertHas(Validate(document), TreeValidationDiagnosticCodes.SubtreeInterface, "/interface");
        }

        [Test]
        public void AIBT2050_InterfaceVersionZeroOrDuplicateKey()
        {
            var leaf = new NodeDocument(new NodeId("leaf"), SubtreeTestKit.Success, 1, Array.Empty<NodeId>(), SemanticObject.Empty, tags: TagSet.Empty);
            var key = new BlackboardKeyDefinition("a", "a", BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool), BlackboardScope.Tree, BlackboardDefaultValue.Bool(false));
            var zero = Version3Interface(leaf, key, new SubtreeInterface(0, new[] { "a" }));
            var duplicate = Version3Interface(leaf, key, new SubtreeInterface(1, new[] { "a", "a" }));

            AssertHas(Validate(zero), TreeValidationDiagnosticCodes.SubtreeInterface, "/interface/version");
            AssertHas(Validate(duplicate), TreeValidationDiagnosticCodes.SubtreeInterface, "/interface/keys/1");
        }

        [Test]
        public void AIBT2050_InterfaceKeyNotADeclaredTreeKey()
        {
            var undeclared = Parse(Tree("tests.p11.child", "leaf", Leaf("leaf"), Keys(BoolKey("a")), Interface(1, "a", "missing")));
            var agent = Parse(Tree("tests.p11.child", "leaf", Leaf("leaf"), AgentKey, Interface(1, "hp"), contracts: AgentContracts));

            AssertHas(Validate(undeclared), TreeValidationDiagnosticCodes.SubtreeInterface, "/interface/keys/1");
            AssertHas(Validate(agent), TreeValidationDiagnosticCodes.SubtreeInterface, "/interface/keys/0");
        }

        [Test]
        public void SubtreeCodes_AreRegisteredValidatorErrors()
        {
            foreach (var code in new[]
            {
                TreeValidationDiagnosticCodes.SubtreeReferenceShape,
                TreeValidationDiagnosticCodes.SubtreeKeyTarget,
                TreeValidationDiagnosticCodes.SubtreeReferenceRoot,
                TreeValidationDiagnosticCodes.SubtreeInterface,
            })
            {
                Assert.That(TreeValidationDiagnosticCatalog.Catalog.TryGet(code, out var descriptor), Is.True, code.Value);
                Assert.That(descriptor.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Error), code.Value);
            }

            Assert.That(new[] { "AIBT2047", "AIBT2048", "AIBT2049", "AIBT2050" }, Is.EqualTo(new[]
            {
                TreeValidationDiagnosticCodes.SubtreeReferenceShape.Value,
                TreeValidationDiagnosticCodes.SubtreeKeyTarget.Value,
                TreeValidationDiagnosticCodes.SubtreeReferenceRoot.Value,
                TreeValidationDiagnosticCodes.SubtreeInterface.Value,
            }));
        }

        private static DiagnosticCollection Validate(TreeDocument document)
            => TreeValidator.Validate(document, Registry, ReferenceCompilationPolicy.Phase1.CreateValidationOptions("trees/test.aibt"));

        private static string[] SubtreeCodes(DiagnosticCollection diagnostics)
            => diagnostics.Select(item => item.Code.Value)
                .Where(code => string.CompareOrdinal(code, "AIBT2047") >= 0 && string.CompareOrdinal(code, "AIBT2050") <= 0)
                .ToArray();

        private static void AssertHas(DiagnosticCollection diagnostics, DiagnosticCode code, string pointer)
        {
            var match = diagnostics.FirstOrDefault(item => item.Code == code && item.Location.JsonPointer == pointer);
            Assert.That(match, Is.Not.Null, "Expected " + code.Value + " at " + pointer + "; got " + Describe(diagnostics));
            Assert.That(match.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        }

        private static TreeDocument Version3(params NodeDocument[] nodes)
            => new TreeDocument(TreeDocument.CurrentFormat, TreeDocument.SubtreeFormatVersion, new TreeId("tests.p11.v3"), "V3",
                nodes[0].Id, nodes, tags: TagSet.Empty, metadata: SemanticObject.Empty);

        private static TreeDocument Version3Interface(NodeDocument leaf, BlackboardKeyDefinition key, SubtreeInterface subtreeInterface)
            => new TreeDocument(TreeDocument.CurrentFormat, TreeDocument.SubtreeFormatVersion, new TreeId("tests.p11.v3"), "V3",
                leaf.Id, new[] { leaf }, new[] { key }, null, TagSet.Empty, SemanticObject.Empty, default, null, null, subtreeInterface);
    }
}
