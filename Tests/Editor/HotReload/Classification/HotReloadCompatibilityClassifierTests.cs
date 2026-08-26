using System;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;
using AuthoringNodeRegistry = AIBT.Authoring.NodeRegistry;

namespace AIBT.Tests.Editor.HotReload.Classification
{
    public sealed class HotReloadCompatibilityClassifierTests
    {
        [Test]
        public void Classify_RejectsNullPrograms()
        {
            var program = Compile(SingleLeafTree(ReferenceFixtureNodeManifests.SuccessTypeId));
            Assert.That(() => HotReloadCompatibilityClassifier.Classify(null, program), Throws.ArgumentNullException);
            Assert.That(() => HotReloadCompatibilityClassifier.Classify(program, null), Throws.ArgumentNullException);
        }

        [Test]
        public void ParameterEdit_EveryNodeMigrates_NoStructuralChange_NoRestart()
        {
            var before = Compile(RepeaterTree(3));
            var after = Compile(RepeaterTree(5));

            var result = HotReloadCompatibilityClassifier.Classify(before, after);

            Assert.That(result.NodeVerdicts[new NodeId("root")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Migrate));
            Assert.That(result.NodeVerdicts[new NodeId("leaf")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Migrate));
            Assert.That(result.StructuralChildChangeNodeIds, Is.Empty);
            Assert.That(result.RestartSubtreeRootNodeIds, Is.Empty);
            Assert.That(result.RequiresFullRestart, Is.False);
        }

        [Test]
        public void Insertion_NewNodeIsNew_ParentFlaggedStructuralChange_NoRestart()
        {
            var before = Compile(SequenceTree("root", "first"));
            var after = Compile(SequenceTree("root", "first", "second"));

            var result = HotReloadCompatibilityClassifier.Classify(before, after);

            Assert.That(result.NodeVerdicts[new NodeId("first")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Migrate));
            Assert.That(result.NodeVerdicts[new NodeId("second")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.New));
            Assert.That(result.StructuralChildChangeNodeIds, Is.EquivalentTo(new[] { new NodeId("root") }));
            Assert.That(result.RestartSubtreeRootNodeIds, Is.Empty);
            Assert.That(result.RequiresFullRestart, Is.False);
        }

        [Test]
        public void Removal_DroppedNodeReported_ParentFlaggedStructuralChange_NoRestart()
        {
            var before = Compile(SequenceTree("root", "first", "second"));
            var after = Compile(SequenceTree("root", "first"));

            var result = HotReloadCompatibilityClassifier.Classify(before, after);

            Assert.That(result.NodeVerdicts[new NodeId("second")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Dropped));
            Assert.That(result.NodeVerdicts[new NodeId("first")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Migrate));
            Assert.That(result.StructuralChildChangeNodeIds, Is.EquivalentTo(new[] { new NodeId("root") }));
            Assert.That(result.RequiresFullRestart, Is.False);
        }

        [Test]
        public void Reordering_ChildrenStillMigrate_OnlyParentFlaggedStructuralChange()
        {
            var before = Compile(SequenceTree("root", "first", "second"));
            var after = Compile(SequenceTree("root", "second", "first"));

            var result = HotReloadCompatibilityClassifier.Classify(before, after);

            Assert.That(result.NodeVerdicts[new NodeId("first")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Migrate));
            Assert.That(result.NodeVerdicts[new NodeId("second")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Migrate));
            Assert.That(result.StructuralChildChangeNodeIds, Is.EquivalentTo(new[] { new NodeId("root") }));
            Assert.That(result.StructuralChildChangeNodeIds, Has.No.Member(new NodeId("first")));
            Assert.That(result.RequiresFullRestart, Is.False);
        }

        [Test]
        public void TypeChange_NodeIsIncompatible_DescendantSweptIntoRestartRegion_RootIsJustTheChangedNode()
        {
            // decorator: Inverter -> Succeeder (same NodeId "decorator"), wrapping an unchanged leaf.
            var before = DecoratorOverLeafTree(BuiltInNodeManifests.InverterTypeId);
            var after = DecoratorOverLeafTree(BuiltInNodeManifests.SucceederTypeId);

            var result = HotReloadCompatibilityClassifier.Classify(Compile(before), Compile(after));

            Assert.That(result.NodeVerdicts[new NodeId("decorator")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.IncompatibleRestart));
            // The leaf's OWN per-node comparison still says Migrate (it did not itself change) --
            // RestartSubtreeRootNodeIds is what tells a consumer it must restart anyway, because
            // it is nested under an incompatible ancestor whose own state machine changed.
            Assert.That(result.NodeVerdicts[new NodeId("leaf")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Migrate));
            Assert.That(result.RestartSubtreeRootNodeIds, Is.EquivalentTo(new[] { new NodeId("decorator") }));
            Assert.That(result.RequiresFullRestart, Is.False, "root itself is unaffected and should still migrate");
        }

        [Test]
        public void TypeChangeAtRoot_RequiresFullRestart()
        {
            var before = SingleLeafTree(ReferenceFixtureNodeManifests.SuccessTypeId, id: "root");
            var after = SingleLeafTree(ReferenceFixtureNodeManifests.FailureTypeId, id: "root");

            var result = HotReloadCompatibilityClassifier.Classify(Compile(before), Compile(after));

            Assert.That(result.RestartSubtreeRootNodeIds, Is.EquivalentTo(new[] { new NodeId("root") }));
            Assert.That(result.RequiresFullRestart, Is.True);
        }

        [Test]
        public void SharedBlackboardWriteInsideCandidateRegion_EscalatesToFullTreeRestart()
        {
            // Phase 1's ReferenceCompiler rejects Shared-scope blackboard writes outright
            // (AIBT2030/AIBT2032 -- "requires a deterministic reduction policy not available in
            // Phase 1"), so this compatible-data category cannot be produced through the real
            // compiler today. The classifier's own escalation rule is format-level, not
            // compiler-policy-level, so it is proven here against a hand-constructed
            // CompiledProgram pair instead -- still real CompiledProgram data, just built directly
            // rather than through ReferenceCompiler. See this card's evidence for the disclosed
            // compiler-policy limitation this works around.
            var before = BuildProgramWithSharedWrite(BuiltInNodeManifests.InverterTypeId);
            var after = BuildProgramWithSharedWrite(BuiltInNodeManifests.SucceederTypeId);

            var result = HotReloadCompatibilityClassifier.Classify(before, after);

            Assert.That(result.NodeVerdicts[new NodeId("decorator")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.IncompatibleRestart));
            Assert.That(result.RestartSubtreeRootNodeIds, Is.EquivalentTo(new[] { new NodeId("root") }),
                "a shared-scope write inside the candidate restart region must escalate to the whole tree, not stay localized");
            Assert.That(result.RequiresFullRestart, Is.True);
        }

        // Hand-constructs a real, fully-validated CompiledProgram (root:MemorySequence ->
        // decorator -> writer), bypassing ReferenceCompiler, so a Shared-scope write can be
        // exercised despite Phase 1's compiler rejecting it as an authoring input.
        private static CompiledProgram BuildProgramWithSharedWrite(string decoratorTypeId)
        {
            var hash = new CompiledHash(new string('a', 64));
            var int32 = BuiltInBlackboardTypes.Int32;

            var nodes = new[]
            {
                new CompiledNodeRecord( // 0: root (MemorySequence)
                    StableHash.Fnv1A64(BuiltInNodeManifests.MemorySequenceTypeId), 1,
                    0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                    new CompiledRange(0, 1), CompiledNodeFlags.BurstDomain, 0,
                    new CompiledRange(0, 0), new CompiledRange(0, 0)),
                new CompiledNodeRecord( // 1: decorator
                    StableHash.Fnv1A64(decoratorTypeId), 1,
                    0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                    new CompiledRange(1, 1), CompiledNodeFlags.BurstDomain, 1,
                    new CompiledRange(0, 0), new CompiledRange(0, 0)),
                new CompiledNodeRecord( // 2: writer (writes the shared slot)
                    StableHash.Fnv1A64("aibt.core.hot-reload-classifier-spec-writer"), 1,
                    0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                    new CompiledRange(2, 0), CompiledNodeFlags.BurstDomain, 2,
                    new CompiledRange(0, 0), new CompiledRange(0, 1)),
            };
            var childIndices = new uint[] { 1, 2 };
            var writeSlotIndices = new uint[] { 0 };
            var blackboardSlots = new[]
            {
                new CompiledBlackboardSlotRecord(
                    StableHash.Fnv1A64("shared.key"), int32.TypeId, int32.Version, 0,
                    BlackboardScope.Shared, 0, (uint)int32.Size, (uint)int32.Alignment, 0,
                    CompiledBlackboardAccessFlags.Write),
            };
            var debugMap = new[]
            {
                new CompiledDebugMapEntry(0, new NodeId("root"), "trees/hot-reload-classifier-spec.aibt.json"),
                new CompiledDebugMapEntry(1, new NodeId("decorator"), "trees/hot-reload-classifier-spec.aibt.json"),
                new CompiledDebugMapEntry(2, new NodeId("writer"), "trees/hot-reload-classifier-spec.aibt.json"),
            };
            var header = new CompiledProgramHeader(
                1, 1, new CompiledCompilerVersion(1, 0, 0, 0), hash, hash, hash, 1, hash,
                rootNodeIndex: 0, nodeCount: 3, childIndexCount: 2, blackboardSlotCount: 1, debugMapCount: 3,
                configBlobSize: 0, instanceNodeMemorySize: 0, requiredMaximumAlignment: 1,
                capabilityFlags: 0, deterministicModeCompatible: true);

            return new CompiledProgram(
                header, nodes, childIndices,
                Array.Empty<uint>(), writeSlotIndices,
                blackboardSlots, Array.Empty<CompiledObserverRecord>(), Array.Empty<uint>(),
                Array.Empty<byte>(), new byte[int32.Size],
                debugMap);
        }

        // --- helpers ---

        private static CompiledProgram Compile(TreeDocument document, AuthoringNodeRegistry registry = null)
        {
            registry ??= NodeRegistryBuilder.CreateWithBuiltIns().AddTestFixtures().Build().Registry;
            var options = new ReferenceCompilerOptions(
                "trees/hot-reload-classifier-spec.aibt.json", ReferenceCompilationPolicy.Phase1,
                new CompiledCompilerVersion(1, 0, 0, 0));
            var result = ReferenceCompiler.Compile(document, registry, options);
            Assert.That(result.Success, Is.True,
                string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            return result.Program;
        }

        private static TreeDocument SingleLeafTree(string leafTypeId, string id = "leaf")
        {
            var leaf = Node(id, leafTypeId);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.classifier-spec"), "Spec", leaf.Id, new[] { leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument DecoratorOverLeafTree(string decoratorTypeId)
        {
            var leaf = Node("leaf", ReferenceFixtureNodeManifests.SuccessTypeId);
            var decorator = new NodeDocument(
                new NodeId("decorator"), decoratorTypeId, 1, new[] { new NodeId("leaf") },
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var root = Node("root", BuiltInNodeManifests.MemorySequenceTypeId, "decorator");
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.classifier-spec"), "Spec", root.Id, new[] { root, decorator, leaf },
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
                new TreeId("tree.classifier-spec"), "Spec", root.Id, new[] { root, leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument SequenceTree(string rootId, params string[] childIds)
        {
            var nodes = new System.Collections.Generic.List<NodeDocument> { Node(rootId, BuiltInNodeManifests.MemorySequenceTypeId, childIds) };
            nodes.AddRange(childIds.Select(id => Node(id, ReferenceFixtureNodeManifests.SuccessTypeId)));
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.classifier-spec"), "Spec", new NodeId(rootId), nodes,
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static NodeDocument Node(string id, string typeId, params string[] children) =>
            new NodeDocument(
                new NodeId(id), typeId, 1, children.Select(child => new NodeId(child)),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
    }
}
