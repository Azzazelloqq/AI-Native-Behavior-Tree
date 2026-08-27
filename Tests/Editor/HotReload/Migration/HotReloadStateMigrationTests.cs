using System;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;
using AuthoringNodeRegistry = AIBT.Authoring.NodeRegistry;

namespace AIBT.Tests.Editor.HotReload.Migration
{
    public sealed class HotReloadStateMigrationTests
    {
        [Test]
        public void Migrate_RejectsNullArguments()
        {
            var program = Compile(RepeaterTree(3));
            var machine = CreateMachine(program);
            var classification = HotReloadCompatibilityClassifier.Classify(program, program);

            Assert.That(() => Migrate(null, program, program, classification), Throws.ArgumentNullException);
            Assert.That(() => Migrate(machine, null, program, classification), Throws.ArgumentNullException);
            Assert.That(() => Migrate(machine, program, null, classification), Throws.ArgumentNullException);
            Assert.That(() => Migrate(machine, program, program, null), Throws.ArgumentNullException);
        }

        [Test]
        public void Migrate_FallsBackToFullRestart_WhenOldInstanceIsActive()
        {
            var oldProgram = Compile(SequenceOverLeafTree(ReferenceFixtureNodeManifests.RunningTypeId));
            var oldMachine = CreateMachine(oldProgram);
            var active = oldMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(active.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting),
                "precondition: the old instance must actually be active before migration is attempted");

            var newProgram = Compile(SequenceOverLeafTree(ReferenceFixtureNodeManifests.RunningTypeId));
            var classification = HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram);

            var result = Migrate(oldMachine, oldProgram, newProgram, classification, out var report);

            Assert.That(report.FellBackToFullRestart, Is.True);
            Assert.That(report.FullRestartReport.Value.OldInstanceWasAborted, Is.True);
            var envelope = result.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(envelope.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
        }

        [Test]
        public void Migrate_PreservesPerNodeInstanceStateAcrossAParameterEdit()
        {
            var oldProgram = Compile(RepeaterTree(5));
            var oldMachine = CreateMachine(oldProgram);
            var completed = oldMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(completed.RootResult, Is.EqualTo(NodeStatus.Success),
                "precondition: a Repeater over an always-Success leaf completes within one tick");
            Assert.That(oldMachine.CaptureInspection().ActiveNodeCount, Is.EqualTo(0u),
                "precondition: the old instance must be idle before migration is attempted");

            var oldRootState = oldMachine.CaptureNodeState(new RuntimeNodeIndex(0));
            Assert.That(oldRootState.ActivationGeneration, Is.GreaterThan(0u),
                "precondition: the repeater must have actually activated at least once, or this test proves nothing");

            // Same count -- a genuine parameter edit that changes an unrelated field, per
            // testing.md's category, still classifies Migrate for the repeater itself.
            var newProgram = Compile(RepeaterTree(5, stopOnFailure: false));
            var classification = HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram);
            Assert.That(classification.NodeVerdicts[new NodeId("root")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Migrate));

            var freshMachine = Migrate(oldMachine, oldProgram, newProgram, classification, out var report);

            Assert.That(report.FellBackToFullRestart, Is.False);
            Assert.That(report.MigratedNodeCount, Is.EqualTo(2u), "root (repeater) and leaf");

            // Proves the mechanism actually copied bytes, not merely that both instances happen
            // to agree by coincidence: the fresh instance's root state matches the exact
            // activation generation and memory bytes the OLD instance had recorded, read back
            // through the same CaptureNodeState this migration used internally to capture it.
            var freshRootState = freshMachine.CaptureNodeState(new RuntimeNodeIndex(0));
            Assert.That(freshRootState.ActivationGeneration, Is.EqualTo(oldRootState.ActivationGeneration));
            Assert.That(freshRootState.Memory, Is.EqualTo(oldRootState.Memory));
        }

        [Test]
        public void Migrate_DoesNotMigrateStateForIncompatibleTypeChange()
        {
            var oldProgram = Compile(SingleLeafTree(ReferenceFixtureNodeManifests.SuccessTypeId));
            var oldMachine = CreateMachine(oldProgram);
            oldMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));

            var newProgram = Compile(SingleLeafTree(ReferenceFixtureNodeManifests.FailureTypeId));
            var classification = HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram);
            Assert.That(classification.NodeVerdicts[new NodeId("leaf")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.IncompatibleRestart));

            var freshMachine = Migrate(oldMachine, oldProgram, newProgram, classification, out var report);

            Assert.That(report.MigratedNodeCount, Is.EqualTo(0u));
            Assert.That(report.ResetNodeCount, Is.EqualTo(1u));
            var envelope = freshMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(envelope.RootResult, Is.EqualTo(NodeStatus.Failure));
        }

        // --- helpers ---

        private static ReferenceExecutionMachine Migrate(
            ReferenceExecutionMachine oldMachine, CompiledProgram oldProgram, CompiledProgram newProgram,
            HotReloadClassificationResult classification)
            => Migrate(oldMachine, oldProgram, newProgram, classification, out _);

        private static ReferenceExecutionMachine Migrate(
            ReferenceExecutionMachine oldMachine, CompiledProgram oldProgram, CompiledProgram newProgram,
            HotReloadClassificationResult classification, out HotReloadMigrationReport report)
        {
            return HotReloadStateMigration.Migrate(
                oldMachine, oldProgram, newProgram, classification, new ReferenceUpdateContext(2, new Revision(1), 0),
                new TreeInstanceId(1), ReferenceLeafRegistry.CreatePhase1Fixtures(), null,
                ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                RegisteredBlackboardRegistry.Empty,
                ReferenceObserverConditionRegistry.Empty,
                out report);
        }

        private static ReferenceExecutionMachine CreateMachine(CompiledProgram program)
        {
            return new ReferenceExecutionMachine(
                program, new TreeInstanceId(1),
                ReferenceLeafRegistry.CreatePhase1Fixtures(), null,
                ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                RegisteredBlackboardRegistry.Empty,
                ReferenceObserverConditionRegistry.Empty);
        }

        private static CompiledProgram Compile(TreeDocument document)
        {
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().AddTestFixtures().Build().Registry;
            var options = new ReferenceCompilerOptions(
                "trees/hot-reload-migration-spec.aibt.json", ReferenceCompilationPolicy.Phase1,
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
                new TreeId("tree.migration-spec"), "Spec", leaf.Id, new[] { leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument SequenceOverLeafTree(string leafTypeId)
        {
            var leaf = Node("leaf", leafTypeId);
            var root = new NodeDocument(
                new NodeId("root"), BuiltInNodeManifests.MemorySequenceTypeId, 1, new[] { new NodeId("leaf") },
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.migration-spec"), "Spec", root.Id, new[] { root, leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument RepeaterTree(uint count, bool stopOnFailure = true)
        {
            var parameters = new SemanticObject(new[]
            {
                new SemanticProperty("stopOnFailure", SemanticValue.FromBoolean(stopOnFailure)),
                new SemanticProperty("count", SemanticValue.FromUInt64(count)),
            });
            var root = new NodeDocument(
                new NodeId("root"), BuiltInNodeManifests.RepeaterTypeId, 1,
                new[] { new NodeId("leaf") }, parameters, tags: TagSet.Empty);
            var leaf = Node("leaf", ReferenceFixtureNodeManifests.SuccessTypeId);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.migration-spec"), "Spec", root.Id, new[] { root, leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static NodeDocument Node(string id, string typeId) =>
            new NodeDocument(
                new NodeId(id), typeId, 1, Array.Empty<NodeId>(),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
    }
}
