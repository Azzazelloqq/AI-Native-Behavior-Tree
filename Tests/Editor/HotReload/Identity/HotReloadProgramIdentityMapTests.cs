using System;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;
using AuthoringNodeRegistry = AIBT.Authoring.NodeRegistry;

namespace AIBT.Tests.Editor.HotReload.Identity
{
    public sealed class HotReloadProgramIdentityMapTests
    {
        [Test]
        public void Build_RejectsNullProgram()
        {
            Assert.That(() => HotReloadProgramIdentityMap.Build(null), Throws.ArgumentNullException);
        }

        [Test]
        public void Build_MapsEveryDebugMapEntryToItsSignatureAndRuntimeIndex()
        {
            var program = Compile(SequenceTree("root", "first", "second"));

            var map = HotReloadProgramIdentityMap.Build(program);

            Assert.That(map.NodeIds, Is.EquivalentTo(new[] { new NodeId("root"), new NodeId("first"), new NodeId("second") }));

            Assert.That(map.TryGetRuntimeIndex(new NodeId("root"), out var rootIndex), Is.True);
            Assert.That(rootIndex, Is.EqualTo(0u));

            Assert.That(map.TryGetSignature(new NodeId("first"), out var firstSignature), Is.True);
            Assert.That(firstSignature.TypeId, Is.EqualTo(StableHash.Fnv1A64(ReferenceFixtureNodeManifests.SuccessTypeId)));
            Assert.That(firstSignature.TypeVersion, Is.EqualTo(1u));
        }

        [Test]
        public void TryGetSignatureAndRuntimeIndex_ReturnFalseForUnknownNodeId()
        {
            var program = Compile(SequenceTree("root", "first"));
            var map = HotReloadProgramIdentityMap.Build(program);

            Assert.That(map.TryGetSignature(new NodeId("never-existed"), out _), Is.False);
            Assert.That(map.TryGetRuntimeIndex(new NodeId("never-existed"), out _), Is.False);
        }

        [Test]
        public void HasSameTypeAndVersion_TrueOnlyWhenBothMatch()
        {
            var successProgram = Compile(SingleLeafTree(ReferenceFixtureNodeManifests.SuccessTypeId));
            var failureProgram = Compile(SingleLeafTree(ReferenceFixtureNodeManifests.FailureTypeId));

            var successMap = HotReloadProgramIdentityMap.Build(successProgram);
            var failureMap = HotReloadProgramIdentityMap.Build(failureProgram);
            successMap.TryGetSignature(new NodeId("leaf"), out var successSignature);
            failureMap.TryGetSignature(new NodeId("leaf"), out var failureSignature);

            Assert.That(successSignature.HasSameTypeAndVersion(successSignature), Is.True);
            Assert.That(successSignature.HasSameTypeAndVersion(failureSignature), Is.False);
        }

        [Test]
        public void HasCompatibleLayout_TrueWhenInstanceMemoryShapeMatches()
        {
            // Repeater(count=3) vs Repeater(count=5): config bytes differ, layout does not --
            // exactly the parameter-edit category ADR-P5-001 classifies CompatibleMigrate.
            var before = Compile(RepeaterTree(3));
            var after = Compile(RepeaterTree(5));
            var beforeMap = HotReloadProgramIdentityMap.Build(before);
            var afterMap = HotReloadProgramIdentityMap.Build(after);
            beforeMap.TryGetSignature(new NodeId("root"), out var beforeSignature);
            afterMap.TryGetSignature(new NodeId("root"), out var afterSignature);

            Assert.That(beforeSignature.HasSameTypeAndVersion(afterSignature), Is.True);
            Assert.That(beforeSignature.HasCompatibleLayout(afterSignature), Is.True);
            Assert.That(before.ConfigBlob, Is.Not.EqualTo(after.ConfigBlob));
        }

        [Test]
        public void RuntimeIndex_ShiftsAcrossRecompileEvenWhenTheNodeItselfIsUnchanged()
        {
            // The load-bearing fact ADR-P5-001 is built on: compiled index is a pure
            // pre-order-DFS artifact, not a stable identity.
            var before = Compile(SequenceTree("root", "first", "second"));
            var after = Compile(SequenceTree("root", "second", "first"));
            var beforeMap = HotReloadProgramIdentityMap.Build(before);
            var afterMap = HotReloadProgramIdentityMap.Build(after);

            beforeMap.TryGetRuntimeIndex(new NodeId("first"), out var firstIndexBefore);
            afterMap.TryGetRuntimeIndex(new NodeId("first"), out var firstIndexAfter);

            Assert.That(firstIndexAfter, Is.Not.EqualTo(firstIndexBefore));
        }

        // --- helpers (mirrors Tests/Editor/Compilation/ReferenceCompilerTests.cs's own pattern) ---

        private static CompiledProgram Compile(TreeDocument document)
        {
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().AddTestFixtures().Build().Registry;
            var options = new ReferenceCompilerOptions(
                "trees/hot-reload-identity-spec.aibt.json", ReferenceCompilationPolicy.Phase1,
                new CompiledCompilerVersion(1, 0, 0, 0));
            var result = ReferenceCompiler.Compile(document, registry, options);
            Assert.That(result.Success, Is.True,
                string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            return result.Program;
        }

        private static TreeDocument SingleLeafTree(string leafTypeId)
        {
            var leaf = Node("leaf", leafTypeId);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.identity-spec"), "Spec", leaf.Id, new[] { leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument RepeaterTree(uint count)
        {
            var parameters = new SemanticObject(new[]
            {
                new SemanticProperty("stopOnFailure", SemanticValue.FromBoolean(true)),
                new SemanticProperty("count", SemanticValue.FromUInt64(count)),
            });
            var root = new NodeDocument(
                new NodeId("root"), BuiltInNodeManifests.RepeaterTypeId, 1,
                new[] { new NodeId("leaf") }, parameters, tags: TagSet.Empty);
            var leaf = Node("leaf", ReferenceFixtureNodeManifests.SuccessTypeId);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.identity-spec"), "Spec", root.Id, new[] { root, leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument SequenceTree(string rootId, params string[] childIds)
        {
            var nodes = new System.Collections.Generic.List<NodeDocument> { Node(rootId, BuiltInNodeManifests.MemorySequenceTypeId, childIds) };
            nodes.AddRange(childIds.Select(id => Node(id, ReferenceFixtureNodeManifests.SuccessTypeId)));
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.identity-spec"), "Spec", new NodeId(rootId), nodes,
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static NodeDocument Node(string id, string typeId, params string[] children) =>
            new NodeDocument(
                new NodeId(id), typeId, 1, children.Select(child => new NodeId(child)),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
    }
}
