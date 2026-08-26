using System;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;
using AuthoringNodeRegistry = AIBT.Authoring.NodeRegistry;

namespace AIBT.Tests.Editor.HotReload.Restart
{
    public sealed class HotReloadFullRestartTests
    {
        [Test]
        public void Restart_RejectsNullArguments()
        {
            var machine = CreateMachine(SingleLeafTree(ReferenceFixtureNodeManifests.SuccessTypeId));
            var program = Compile(SingleLeafTree(ReferenceFixtureNodeManifests.SuccessTypeId));

            Assert.That(() => Restart(null, program, 2), Throws.ArgumentNullException);
            Assert.That(() => Restart(machine, null, 2), Throws.ArgumentNullException);
        }

        [Test]
        public void Restart_AbortsAnActiveOldInstance_AndReturnsAFreshWorkingMachine()
        {
            var oldMachine = CreateMachine(SequenceOverLeafTree(ReferenceFixtureNodeManifests.RunningTypeId));
            var suspended = oldMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(suspended.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting),
                "precondition: the old instance must actually be active (still running) before restart");

            var newProgram = Compile(SequenceOverLeafTree(ReferenceFixtureNodeManifests.RunningTypeId));
            var freshMachine = Restart(oldMachine, newProgram, 2, out var report);

            Assert.That(report.OldInstanceWasAborted, Is.True);
            Assert.That(report.ActiveNodeCountBeforeRestart, Is.GreaterThan(0u));
            Assert.That(oldMachine.CaptureInspection().ActiveNodeCount, Is.EqualTo(0u),
                "the old instance must actually be torn down, not left dangling with active state");

            // The fresh machine starts clean and is independently usable.
            var envelope = freshMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(envelope.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
        }

        [Test]
        public void Restart_SkipsAbortForAnAlreadyIdleOldInstance()
        {
            var oldMachine = CreateMachine(SingleLeafTree(ReferenceFixtureNodeManifests.SuccessTypeId));
            var completed = oldMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(completed.RootResult, Is.EqualTo(NodeStatus.Success), "precondition: the old instance must already be terminal");

            var newProgram = Compile(SingleLeafTree(ReferenceFixtureNodeManifests.SuccessTypeId));
            Restart(oldMachine, newProgram, 2, out var report);

            Assert.That(report.OldInstanceWasAborted, Is.False);
        }

        [Test]
        public void Restart_FreshMachineIsBoundToTheNewProgramNotTheOld()
        {
            var oldMachine = CreateMachine(SingleLeafTree(ReferenceFixtureNodeManifests.SuccessTypeId));
            var newProgram = Compile(SingleLeafTree(ReferenceFixtureNodeManifests.FailureTypeId));

            var freshMachine = Restart(oldMachine, newProgram, 1, out _);

            var envelope = freshMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(envelope.RootResult, Is.EqualTo(NodeStatus.Failure));
        }

        [Test]
        public void Restart_RepeatedCyclesLeaveNoGrowingManagedState()
        {
            const int cycles = 50;
            var machine = CreateMachine(SequenceOverLeafTree(ReferenceFixtureNodeManifests.RunningTypeId));
            var program = Compile(SequenceOverLeafTree(ReferenceFixtureNodeManifests.RunningTypeId));

            for (var cycle = 1; cycle <= cycles; cycle++)
            {
                machine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
                machine = Restart(machine, program, 2, out var report);
                Assert.That(report.OldInstanceWasAborted, Is.True, "cycle " + cycle);
                Assert.That(machine.CaptureInspection().ActiveNodeCount, Is.EqualTo(0u), "cycle " + cycle + ": fresh instance must start with no active nodes");
            }
        }

        // --- helpers ---

        private static ReferenceExecutionMachine Restart(ReferenceExecutionMachine oldMachine, CompiledProgram newProgram, ulong abortUpdateId)
            => Restart(oldMachine, newProgram, abortUpdateId, out _);

        private static ReferenceExecutionMachine Restart(
            ReferenceExecutionMachine oldMachine, CompiledProgram newProgram, ulong abortUpdateId, out HotReloadFullRestartReport report)
        {
            return HotReloadFullRestart.Restart(
                oldMachine, newProgram, new ReferenceUpdateContext(abortUpdateId, new Revision(1), 0), new TreeInstanceId(1),
                ReferenceLeafRegistry.CreatePhase1Fixtures(), null,
                ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                RegisteredBlackboardRegistry.Empty,
                ReferenceObserverConditionRegistry.Empty,
                out report);
        }

        private static ReferenceExecutionMachine CreateMachine(TreeDocument document)
        {
            var program = Compile(document);
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
                "trees/hot-reload-restart-spec.aibt.json", ReferenceCompilationPolicy.Phase1,
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
                new TreeId("tree.restart-spec"), "Spec", leaf.Id, new[] { leaf },
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
                new TreeId("tree.restart-spec"), "Spec", root.Id, new[] { root, leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static NodeDocument Node(string id, string typeId) =>
            new NodeDocument(
                new NodeId(id), typeId, 1, Array.Empty<NodeId>(),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
    }
}
