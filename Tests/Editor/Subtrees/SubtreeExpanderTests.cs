using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AIBT.Authoring;
using NUnit.Framework;
using static AIBT.Tests.Editor.Subtrees.SubtreeTestKit;

namespace AIBT.Tests.Editor.Subtrees
{
    /// <summary>
    /// ADR-P11-001 §4.3-§4.6: authoring-time expansion of subtree references, its identity rule,
    /// equivalence with hand-copied trees and one negative test per expansion diagnostic
    /// (AIBT3020-AIBT3028).
    /// </summary>
    public sealed class SubtreeExpanderTests
    {
        private const string ParentSource = "trees/parent.aibt";
        private const string ChildSource = "trees/child.aibt";

        // --- identity rule -------------------------------------------------------------------

        [Test]
        public void IdentityRule_EveryExistingTreeIsReturnedAsTheSameObjectAndCompilesToTheSameHash()
        {
            var registries = new[]
            {
                Registry,
                NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry,
            };
            var checkedTrees = 0;
            var compiledTrees = 0;
            foreach (var path in ExistingTreeFiles())
            {
                var relative = Relative(path);
                var read = CanonicalTreeJson.Parse(File.ReadAllBytes(path), relative);
                if (!read.Success || read.Document.UsesSubtreeFeatures) continue;
                checkedTrees++;
                var document = read.Document;

                var expansion = SubtreeExpander.Expand(document, null, Registry);
                Assert.That(expansion.IsIdentity, Is.True, relative);
                Assert.That(expansion.Document, Is.SameAs(document), relative);
                Assert.That(expansion.Diagnostics, Is.Empty, relative);
                Assert.That(expansion.NodeSourceIds, Is.Empty, relative);

                var sourceId = "baseline/" + relative;
                foreach (var registry in registries)
                {
                    var before = ReferenceCompiler.Compile(document, registry, Options(sourceId));
                    var after = ReferenceCompiler.Compile(document, registry, Options(sourceId), new InMemorySubtreeResolver());
                    Assert.That(after.Success, Is.EqualTo(before.Success), relative);
                    if (!before.Success)
                    {
                        Assert.That(after.Diagnostics.Select(item => item.Code.Value + item.Location.JsonPointer),
                            Is.EqualTo(before.Diagnostics.Select(item => item.Code.Value + item.Location.JsonPointer)), relative);
                        continue;
                    }

                    compiledTrees++;
                    Assert.That(after.Program.Header.CompiledContentHash, Is.EqualTo(before.Program.Header.CompiledContentHash), relative);
                    Assert.That(after.Program.Header.CanonicalSemanticHash, Is.EqualTo(before.Program.Header.CanonicalSemanticHash), relative);
                }
            }

            Assert.That(checkedTrees, Is.GreaterThanOrEqualTo(40), "Every fixture and sample tree of the package is checked.");
            Assert.That(compiledTrees, Is.GreaterThan(0));
        }

        // --- the expansion itself ------------------------------------------------------------

        [Test]
        public void Expansion_RenamesInnerNodesPrivateKeysAndWatchedKeys()
        {
            var child = Tree("tests.p11.child", "root",
                NodeList(
                    Composite("root", ReactiveSelector, "check", "work"),
                    Leaf("check", ReadsFlagTypeId, "'displayName': 'Check Flag', 'observer': { 'mode': 'lower-priority', 'watchedKeys': ['count', 'flag', 'goal'] }"),
                    Leaf("work", Running)),
                Keys(BoolKey("flag"), IntKey("count", 3), BoolKey("goal")),
                Interface(1, "flag", "goal"));
            var parent = Parse(Tree("tests.p11.parent", "root",
                NodeList(Seq("root", "first", "use"), Leaf("first"), Ref("use", "tests.p11.child", 1, Keys(Bind("flag", "flag"), Bind("goal", "target")), "Use Child")),
                Keys(BoolKey("flag"), BoolKey("target"))));

            var expansion = ExpandOk(parent, Resolver(ChildSource, child));
            var document = expansion.Document;

            Assert.That(expansion.IsIdentity, Is.False);
            Assert.That(document.FormatVersion, Is.EqualTo(TreeDocument.LatestFormatVersion));
            Assert.That(document.Interface, Is.Null);
            Assert.That(document.TreeId, Is.EqualTo(parent.TreeId));
            Assert.That(document.Root, Is.EqualTo(parent.Root));
            Assert.That(NodeIds(document), Is.EqualTo(new[] { "first", "root", "use:check", "use:root", "use:work" }));
            Assert.That(KeyIds(document), Is.EqualTo(new[] { "flag", "target", "use:count" }));
            Assert.That(KeyById(document, "use:count").DefaultValue.TryGetRuntimeValue(out var count) && count.TryGetInt32(out var countValue) ? countValue : -1,
                Is.EqualTo(3), "A private key keeps its default.");

            Assert.That(NodeById(document, "root").Children.Select(id => id.Value), Is.EqualTo(new[] { "first", "use:root" }));
            Assert.That(NodeById(document, "use:root").Children.Select(id => id.Value), Is.EqualTo(new[] { "use:check", "use:work" }));
            Assert.That(NodeById(document, "use:check").Observer.WatchedKeys, Is.EqualTo(new[] { "use:count", "flag", "target" }));
            Assert.That(NodeById(document, "use:check").DisplayName, Is.EqualTo("Use Child / Check Flag"));
            Assert.That(NodeById(document, "use:root").DisplayName, Is.EqualTo("Use Child / root"));
            Assert.That(NodeById(document, "first").DisplayName, Is.Null, "The parent's own nodes are copied unchanged.");

            Assert.That(expansion.NodeSourceIds.Keys.Select(id => id.Value).OrderBy(id => id, StringComparer.Ordinal),
                Is.EqualTo(new[] { "use:check", "use:root", "use:work" }));
            Assert.That(expansion.NodeSourceIds.Values.Distinct(), Is.EqualTo(new[] { ChildSource }));
            Assert.That(SubtreeExpander.RequiresExpansion(document), Is.False);
        }

        [Test]
        public void ConstantBinding_BecomesAPrivateKeyWithTheConstantAsDefault()
        {
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf"), Keys(IntKey("speed", 1), BoolKey("armed")), Interface(1, "armed", "speed"));
            var parent = Parse(Tree("tests.p11.parent", "root",
                NodeList(Seq("root", "use"), Ref("use", "tests.p11.child", 1, Keys(Constant("speed", "7"), Constant("armed", "true"))))));

            var document = ExpandOk(parent, Resolver(ChildSource, child)).Document;

            var speed = KeyById(document, "use:speed");
            Assert.That(speed.Scope, Is.EqualTo(BlackboardScope.Tree));
            Assert.That(speed.DefaultValue.TryGetRuntimeValue(out var speedValue) && speedValue.TryGetInt32(out var speedInt) ? speedInt : -1, Is.EqualTo(7));
            var armed = KeyById(document, "use:armed");
            Assert.That(armed.DefaultValue.TryGetRuntimeValue(out var armedValue) && armedValue.TryGetBool(out var armedBool) && armedBool, Is.True);
        }

        [Test]
        public void NestedReferences_QualifyWithEveryPrefixAndKeepEachSource()
        {
            var leafTree = Tree("tests.p11.leaf", "act", Leaf("act"), Keys(IntKey("hits")));
            var middle = Tree("tests.p11.middle", "main", NodeList(Seq("main", "inner"), Ref("inner", "tests.p11.leaf")));
            var parent = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "outer"), Ref("outer", "tests.p11.middle", displayName: "Outer"))));

            var expansion = ExpandOk(parent, Resolver("trees/leaf.aibt", leafTree, "trees/middle.aibt", middle));
            var document = expansion.Document;

            Assert.That(NodeIds(document), Is.EqualTo(new[] { "outer:inner:act", "outer:main", "root" }));
            Assert.That(KeyIds(document), Is.EqualTo(new[] { "outer:inner:hits" }));
            Assert.That(NodeById(document, "outer:main").Children.Select(id => id.Value), Is.EqualTo(new[] { "outer:inner:act" }));
            Assert.That(NodeById(document, "outer:inner:act").DisplayName, Is.EqualTo("Outer / inner / act"));
            Assert.That(expansion.NodeSourceIds[new NodeId("outer:main")], Is.EqualTo("trees/middle.aibt"));
            Assert.That(expansion.NodeSourceIds[new NodeId("outer:inner:act")], Is.EqualTo("trees/leaf.aibt"));
        }

        [Test]
        public void SameSubtreeTwice_GetsSeparatePrivateKeysAndSharesBoundKeys()
        {
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf"), Keys(IntKey("count"), BoolKey("shared")), Interface(1, "shared"));
            var parent = Parse(Tree("tests.p11.parent", "root",
                NodeList(Seq("root", "a", "b"), Ref("a", "tests.p11.child", 1, Bind("shared", "alarm")), Ref("b", "tests.p11.child", 1, Bind("shared", "alarm"))),
                Keys(BoolKey("alarm"))));

            var document = ExpandOk(parent, Resolver(ChildSource, child)).Document;

            Assert.That(KeyIds(document), Is.EqualTo(new[] { "a:count", "alarm", "b:count" }));
            Assert.That(NodeIds(document), Is.EqualTo(new[] { "a:leaf", "b:leaf", "root" }));
        }

        [Test]
        public void HandCopyEquivalence_CompiledProgramIsIdenticalToTheHandWrittenTree()
        {
            var child = Tree("tests.p11.child", "main",
                NodeList(Seq("main", "check", "act"), Leaf("check", Failure, "'displayName': 'Check'"), Leaf("act", Running)),
                Keys(BoolKey("flag"), IntKey("ticks", 2)),
                Interface(1, "flag"));
            var parent = Parse(Tree("tests.p11.parent", "root",
                NodeList(Composite("root", ReactiveSelector, "use", "idle"), Leaf("idle"), Ref("use", "tests.p11.child", 1, Bind("flag", "flag"), "Guard")),
                Keys(BoolKey("flag"))));
            var handCopy = Parse(Tree("tests.p11.parent", "root",
                NodeList(
                    Composite("root", ReactiveSelector, "use:main", "idle"),
                    Leaf("idle"),
                    "'use:main': { 'type': '" + SubtreeTestKit.Sequence + "', 'typeVersion': 1, 'displayName': 'Guard / main', 'children': ['use:check', 'use:act'] }",
                    Leaf("use:check", Failure, "'displayName': 'Guard / Check'"),
                    Leaf("use:act", Running, "'displayName': 'Guard / act'")),
                Keys(BoolKey("flag"), IntKey("use:ticks", 2)),
                formatVersion: 2));

            // Same source ID for the subtree: the whole compiled program, debug map included, is identical.
            var expanded = CompileOk(parent, Resolver(ParentSource, child));
            var written = CompileOk(handCopy, null);
            Assert.That(expanded.Header.CanonicalSemanticHash, Is.EqualTo(written.Header.CanonicalSemanticHash));
            Assert.That(expanded.Header.CompiledContentHash, Is.EqualTo(written.Header.CompiledContentHash));

            // A separate subtree file only changes the debug-map source path of the inner nodes.
            var separate = CompileOk(parent, Resolver(ChildSource, child));
            Assert.That(separate.Header.CanonicalSemanticHash, Is.EqualTo(written.Header.CanonicalSemanticHash));
            Assert.That(separate.Header.CompiledContentHash, Is.Not.EqualTo(written.Header.CompiledContentHash));
            for (var index = 0; index < separate.DebugMap.Count; index++)
            {
                var entry = separate.DebugMap[index];
                var reference = written.DebugMap[index];
                Assert.That(entry.AuthoringNodeId, Is.EqualTo(reference.AuthoringNodeId));
                Assert.That(entry.DisplayName, Is.EqualTo(reference.DisplayName));
                Assert.That(entry.SourcePath, Is.EqualTo(entry.AuthoringNodeId.Value.StartsWith("use:", StringComparison.Ordinal) ? ChildSource : ParentSource));
            }

            Assert.That(separate.Nodes.Select(node => node.NodeTypeId), Is.EqualTo(written.Nodes.Select(node => node.NodeTypeId)));
            Assert.That(separate.BlackboardSlots.Count, Is.EqualTo(written.BlackboardSlots.Count));
        }

        [Test]
        public void ExpansionOverload_CompilesTheSameAsTheResolverOverload()
        {
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf"), Keys(IntKey("count")));
            var parent = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child"))));
            var resolver = Resolver(ChildSource, child);

            var viaResolver = ReferenceCompiler.Compile(parent, Registry, Options(), resolver);
            var viaExpansion = ReferenceCompiler.Compile(Expand(parent, resolver), Registry, Options());

            Assert.That(viaResolver.Success && viaExpansion.Success, Is.True, Describe(viaResolver.Diagnostics) + Describe(viaExpansion.Diagnostics));
            Assert.That(viaExpansion.Program.Header.CompiledContentHash, Is.EqualTo(viaResolver.Program.Header.CompiledContentHash));
        }

        // --- AIBT3020-AIBT3028 ---------------------------------------------------------------

        [Test]
        public void AIBT3020_SubtreeNotFound()
        {
            var parent = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.missing"))));

            var missing = SingleError(Expand(parent, new InMemorySubtreeResolver()), SubtreeExpansionDiagnosticCodes.SubtreeNotFound);
            Assert.That(missing.Location.DocumentId, Is.EqualTo(ParentSource));
            Assert.That(missing.Location.JsonPointer, Is.EqualTo("/nodes/use/subtree"));
            Assert.That(missing.Message, Does.Contain("tests.p11.missing"));

            var noResolver = SingleError(Expand(parent, null), SubtreeExpansionDiagnosticCodes.SubtreeNotFound);
            Assert.That(noResolver.Message, Does.Contain("ISubtreeResolver"));

            var oldOverload = ReferenceCompiler.Compile(parent, Registry, Options());
            Assert.That(oldOverload.Success, Is.False);
            Assert.That(oldOverload.Diagnostics.Select(item => item.Code), Does.Contain(SubtreeExpansionDiagnosticCodes.SubtreeNotFound));
        }

        [Test]
        public void AIBT3021_SubtreeAmbiguous()
        {
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf"));
            var parent = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child"))));

            var error = SingleError(Expand(parent, Resolver("trees/b.aibt", child, "trees/a.aibt", child)), SubtreeExpansionDiagnosticCodes.SubtreeAmbiguous);
            Assert.That(error.RelatedLocations.Select(item => item.DocumentId), Is.EqualTo(new[] { "trees/a.aibt", "trees/b.aibt" }));
        }

        [Test]
        public void AIBT3022_CycleOfOne()
        {
            var parentJson = Tree("tests.p11.a", "root", NodeList(Seq("root", "self"), Ref("self", "tests.p11.a")));
            var error = SingleError(Expand(Parse(parentJson), Resolver(ParentSource, parentJson)), SubtreeExpansionDiagnosticCodes.SubtreeCycle);
            Assert.That(error.Message, Does.Contain("tests.p11.a -> tests.p11.a"));
        }

        [Test]
        public void AIBT3022_CycleOfTwo()
        {
            var a = Tree("tests.p11.a", "root", NodeList(Seq("root", "to-b"), Ref("to-b", "tests.p11.b")));
            var b = Tree("tests.p11.b", "root", NodeList(Seq("root", "to-a"), Ref("to-a", "tests.p11.a")));

            var error = SingleError(Expand(Parse(a), Resolver(ParentSource, a, "trees/b.aibt", b)), SubtreeExpansionDiagnosticCodes.SubtreeCycle);
            Assert.That(error.Message, Does.Contain("tests.p11.a -> tests.p11.b -> tests.p11.a"));
            Assert.That(error.Location.DocumentId, Is.EqualTo("trees/b.aibt"), "Reported at the reference that closes the cycle.");
            Assert.That(error.RelatedLocations.Select(item => item.DocumentId), Does.Contain(ParentSource));
        }

        [Test]
        public void AIBT3022_CycleOfThree()
        {
            var a = Tree("tests.p11.a", "root", NodeList(Seq("root", "to-b"), Ref("to-b", "tests.p11.b")));
            var b = Tree("tests.p11.b", "root", NodeList(Seq("root", "to-c"), Ref("to-c", "tests.p11.c")));
            var c = Tree("tests.p11.c", "root", NodeList(Seq("root", "to-a"), Ref("to-a", "tests.p11.a")));

            var error = SingleError(Expand(Parse(a), Resolver(ParentSource, a, "trees/b.aibt", b, "trees/c.aibt", c)), SubtreeExpansionDiagnosticCodes.SubtreeCycle);
            Assert.That(error.Message, Does.Contain("tests.p11.a -> tests.p11.b -> tests.p11.c -> tests.p11.a"));
        }

        [Test]
        public void AIBT3023_DepthNineIsRejectedAndDepthEightIsAccepted()
        {
            Assert.That(SubtreeExpander.MaximumNestingDepth, Is.EqualTo(8));
            var eight = ExpandOk(Parse(ChainTree(0)), ChainResolver(8));
            Assert.That(NodeIds(eight.Document), Does.Contain("r:r:r:r:r:r:r:r:leaf"));

            var error = SingleError(Expand(Parse(ChainTree(0)), ChainResolver(9)), SubtreeExpansionDiagnosticCodes.SubtreeDepth);
            Assert.That(error.Message, Does.Contain("depth 9"));
        }

        [Test]
        public void AIBT3024_InterfaceVersionMismatch()
        {
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf"), Keys(BoolKey("a")), Interface(2, "a"));
            var parent = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child", 1, Bind("a", "goal"))), Keys(BoolKey("goal"))));

            var error = SingleError(Expand(parent, Resolver(ChildSource, child)), SubtreeExpansionDiagnosticCodes.SubtreeInterfaceVersion);
            Assert.That(error.Location.JsonPointer, Is.EqualTo("/nodes/use/subtree/interfaceVersion"));
            Assert.That(error.RelatedLocations.Select(item => item.DocumentId + item.JsonPointer), Does.Contain(ChildSource + "/interface/version"));
        }

        [Test]
        public void AIBT3024_BindingKeysToASubtreeWithoutAnInterface()
        {
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf"), Keys(BoolKey("a")));
            var parent = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child", 1, Bind("a", "goal"))), Keys(BoolKey("goal"))));

            SingleError(Expand(parent, Resolver(ChildSource, child)), SubtreeExpansionDiagnosticCodes.SubtreeInterfaceVersion);
        }

        [Test]
        public void AIBT3025_MissingAndUnknownKeys()
        {
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf"), Keys(BoolKey("a"), BoolKey("b")), Interface(1, "a", "b"));
            var missing = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child", 1, Bind("a", "goal"))), Keys(BoolKey("goal"))));
            var unknown = Parse(Tree("tests.p11.parent", "root",
                NodeList(Seq("root", "use"), Ref("use", "tests.p11.child", 1, Keys(Bind("a", "goal"), Bind("b", "goal"), Bind("c", "goal")))), Keys(BoolKey("goal"))));

            var missingError = SingleError(Expand(missing, Resolver(ChildSource, child)), SubtreeExpansionDiagnosticCodes.SubtreeKeySet);
            Assert.That(missingError.Message, Does.Contain("'b'"));
            var unknownError = SingleError(Expand(unknown, Resolver(ChildSource, child)), SubtreeExpansionDiagnosticCodes.SubtreeKeySet);
            Assert.That(unknownError.Location.JsonPointer, Is.EqualTo("/nodes/use/subtree/keys/c"));
        }

        [Test]
        public void AIBT3026_KeyTypeMismatch()
        {
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf"), Keys(BoolKey("a")), Interface(1, "a"));
            var parent = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child", 1, Bind("a", "count"))), Keys(IntKey("count"))));

            var error = SingleError(Expand(parent, Resolver(ChildSource, child)), SubtreeExpansionDiagnosticCodes.SubtreeKeyType);
            Assert.That(error.Location.JsonPointer, Is.EqualTo("/nodes/use/subtree/keys/a"));
        }

        [Test]
        public void AIBT3026_ConstantOfTheWrongType()
        {
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf"), Keys(BoolKey("a")), Interface(1, "a"));
            var parent = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child", 1, Constant("a", "3")))));

            var error = SingleError(Expand(parent, Resolver(ChildSource, child)), SubtreeExpansionDiagnosticCodes.SubtreeKeyType);
            Assert.That(error.Location.JsonPointer, Is.EqualTo("/nodes/use/subtree/keys/a/value"));
        }

        [Test]
        public void AIBT3026_FixedNameAccessThatWouldBeRenamed()
        {
            var privateFlag = Tree("tests.p11.child", "check", Leaf("check", ReadsFlagTypeId), Keys(BoolKey("flag")));
            var renamedFlag = Tree("tests.p11.child", "check", Leaf("check", ReadsFlagTypeId), Keys(BoolKey("flag")), Interface(1, "flag"));
            var asPrivate = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child"))));
            var toOther = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child", 1, Bind("flag", "other"))), Keys(BoolKey("other"))));
            var toSame = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child", 1, Bind("flag", "flag"))), Keys(BoolKey("flag"))));

            var privateError = SingleError(Expand(asPrivate, Resolver(ChildSource, privateFlag)), SubtreeExpansionDiagnosticCodes.SubtreeKeyType);
            Assert.That(privateError.Location.DocumentId, Is.EqualTo(ChildSource));
            Assert.That(privateError.Location.JsonPointer, Is.EqualTo("/nodes/check"));
            Assert.That(privateError.Message, Does.Contain("use:flag"));
            SingleError(Expand(toOther, Resolver(ChildSource, renamedFlag)), SubtreeExpansionDiagnosticCodes.SubtreeKeyType);
            ExpandOk(toSame, Resolver(ChildSource, renamedFlag));
        }

        [Test]
        public void AIBT3027_QualifiedIdTooLong()
        {
            var longInner = new string('n', 100);
            var child = Tree("tests.p11.child", longInner, Leaf(longInner));
            var reference = new string('r', 40);
            var parent = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", reference), Ref(reference, "tests.p11.child"))));

            var error = SingleError(Expand(parent, Resolver(ChildSource, child)), SubtreeExpansionDiagnosticCodes.SubtreeQualifiedIdentity);
            Assert.That(error.Message, Does.Contain("141 characters"));
        }

        [Test]
        public void AIBT3027_QualifiedIdCollidesWithAParentId()
        {
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf"), Keys(IntKey("count")));
            var nodeCollision = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use", "use:leaf"), Ref("use", "tests.p11.child"), Leaf("use:leaf"))));
            var keyCollision = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child")), Keys(IntKey("use:count"))));

            var node = SingleError(Expand(nodeCollision, Resolver(ChildSource, child)), SubtreeExpansionDiagnosticCodes.SubtreeQualifiedIdentity);
            Assert.That(node.Message, Does.Contain("use:leaf"));
            var key = SingleError(Expand(keyCollision, Resolver(ChildSource, child)), SubtreeExpansionDiagnosticCodes.SubtreeQualifiedIdentity);
            Assert.That(key.Message, Does.Contain("use:count"));
        }

        [Test]
        public void AIBT3028_ScopeContractMustBeIdentical()
        {
            const string vitals = "{ 'agent': { 'contractId': 'tests.p11.vitals', 'contractVersion': 1 } }";
            const string vitals2 = "{ 'agent': { 'contractId': 'tests.p11.vitals', 'contractVersion': 2 } }";
            const string hp = "'hp': { 'type': 'Int32', 'typeVersion': 1, 'scope': 'agent', 'default': 10 }";
            const string hpOther = "'hp': { 'type': 'Int32', 'typeVersion': 1, 'scope': 'agent', 'default': 20 }";
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf"), hp, contracts: vitals);
            var options = new ReferenceCompilationPolicy(supportsAgentScope: true).CreateValidationOptions(ParentSource);
            var resolver = Resolver(ChildSource, child);

            SubtreeExpansion Run(string parentKeys, string contracts)
                => SubtreeExpander.Expand(
                    Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child")), parentKeys, contracts: contracts)),
                    resolver,
                    Registry,
                    options);

            SingleError(Run(null, null), SubtreeExpansionDiagnosticCodes.SubtreeScopeContract);
            SingleError(Run(hp, vitals2), SubtreeExpansionDiagnosticCodes.SubtreeScopeContract);
            SingleError(Run(hpOther, vitals), SubtreeExpansionDiagnosticCodes.SubtreeScopeContract);
            var same = Run(hp, vitals);
            Assert.That(same.Success, Is.True, Describe(same.Diagnostics));
            Assert.That(KeyIds(same.Document), Is.EqualTo(new[] { "hp" }), "Agent keys are shared by identity, never renamed.");
        }

        [Test]
        public void SubtreeErrors_AreReportedInTheSubtreeWithTheReferenceAsRelatedLocation()
        {
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf", "tests.p11.unknown-type"));
            var parent = Parse(Tree("tests.p11.parent", "root", NodeList(Seq("root", "use"), Ref("use", "tests.p11.child"))));

            var expansion = Expand(parent, Resolver(ChildSource, child));
            Assert.That(expansion.Success, Is.False);
            var error = expansion.Diagnostics.First();
            Assert.That(error.Location.DocumentId, Is.EqualTo(ChildSource));
            Assert.That(error.RelatedLocations.Select(item => item.DocumentId + item.JsonPointer), Does.Contain(ParentSource + "/nodes/use/subtree"));
        }

        [Test]
        public void RootReference_IsRejectedBeforeExpansion()
        {
            var child = Tree("tests.p11.child", "leaf", Leaf("leaf"));
            var parent = Parse(Tree("tests.p11.parent", "use", Ref("use", "tests.p11.child")));

            var result = ReferenceCompiler.Compile(parent, Registry, Options(), Resolver(ChildSource, child));
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Select(item => item.Code), Does.Contain(TreeValidationDiagnosticCodes.SubtreeReferenceRoot));
        }

        [Test]
        public void ExpansionCodes_AreRegisteredCompilerErrors()
        {
            var codes = new[]
            {
                SubtreeExpansionDiagnosticCodes.SubtreeNotFound,
                SubtreeExpansionDiagnosticCodes.SubtreeAmbiguous,
                SubtreeExpansionDiagnosticCodes.SubtreeCycle,
                SubtreeExpansionDiagnosticCodes.SubtreeDepth,
                SubtreeExpansionDiagnosticCodes.SubtreeInterfaceVersion,
                SubtreeExpansionDiagnosticCodes.SubtreeKeySet,
                SubtreeExpansionDiagnosticCodes.SubtreeKeyType,
                SubtreeExpansionDiagnosticCodes.SubtreeQualifiedIdentity,
                SubtreeExpansionDiagnosticCodes.SubtreeScopeContract,
            };
            Assert.That(codes.Select(code => code.Value), Is.EqualTo(Enumerable.Range(3020, 9).Select(value => "AIBT" + value)));
            foreach (var code in codes)
            {
                Assert.That(ReferenceCompilerDiagnostics.Catalog.TryGet(code, out var descriptor), Is.True, code.Value);
                Assert.That(descriptor.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Error));
            }
        }

        // --- resolvers -----------------------------------------------------------------------

        [Test]
        public void DirectoryResolver_FindsTreesByIdAndReportsDuplicates()
        {
            var directory = Path.Combine(Path.GetTempPath(), "aibt-p11-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            try
            {
                File.WriteAllText(Path.Combine(directory, "one.aibt"), Tree("tests.p11.one", "leaf", Leaf("leaf")));
                File.WriteAllText(Path.Combine(directory, "nested", "two.aibt.json"), Tree("tests.p11.two", "leaf", Leaf("leaf")));
                File.WriteAllText(Path.Combine(directory, "nested", "two-again.aibt"), Tree("tests.p11.two", "leaf", Leaf("leaf")));
                File.WriteAllText(Path.Combine(directory, "broken.aibt"), "{");
                var resolver = new DirectorySubtreeResolver(directory, "lib");

                var one = resolver.Resolve(new TreeId("tests.p11.one"));
                Assert.That(one.Status, Is.EqualTo(SubtreeResolutionStatus.Found));
                Assert.That(one.SourceId, Is.EqualTo("lib/one.aibt"));
                var two = resolver.Resolve(new TreeId("tests.p11.two"));
                Assert.That(two.Status, Is.EqualTo(SubtreeResolutionStatus.Ambiguous));
                Assert.That(two.CandidateSourceIds, Is.EqualTo(new[] { "lib/nested/two-again.aibt", "lib/nested/two.aibt.json" }));
                var missing = resolver.Resolve(new TreeId("tests.p11.none"));
                Assert.That(missing.Status, Is.EqualTo(SubtreeResolutionStatus.NotFound));
                Assert.That(missing.Detail, Does.Contain("lib/broken.aibt"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        // --- helpers -------------------------------------------------------------------------

        private static string ChainTree(int index)
            => Tree("tests.p11.chain" + index, "root", NodeList(Seq("root", "r"), Ref("r", "tests.p11.chain" + (index + 1))));

        /// <summary>chain0 (the parent) -> chain1 -> ... -> chain<paramref name="depth"/>, which is a leaf tree.</summary>
        private static InMemorySubtreeResolver ChainResolver(int depth)
        {
            var resolver = new InMemorySubtreeResolver();
            for (var index = 1; index < depth; index++)
                resolver.Add(Parse(ChainTree(index), "trees/chain" + index + ".aibt"), "trees/chain" + index + ".aibt");
            resolver.Add(Parse(Tree("tests.p11.chain" + depth, "leaf", Leaf("leaf")), "trees/chain" + depth + ".aibt"), "trees/chain" + depth + ".aibt");
            return resolver;
        }

        private static IEnumerable<string> ExistingTreeFiles()
            => TreeSourcePaths.FindFiles(PackagePath())
                .Where(path =>
                {
                    var normalized = path.Replace('\\', '/');
                    return normalized.IndexOf("/Tools~/", StringComparison.Ordinal) < 0
                        && normalized.IndexOf("/Results/", StringComparison.Ordinal) < 0
                        && normalized.IndexOf("/.aibt/", StringComparison.Ordinal) < 0
                        && !normalized.EndsWith(".layout.json", StringComparison.Ordinal);
                });

        private static string Relative(string path)
        {
            var root = Path.GetFullPath(PackagePath()).TrimEnd('\\', '/');
            return Path.GetFullPath(path).Substring(root.Length + 1).Replace('\\', '/');
        }
    }
}
