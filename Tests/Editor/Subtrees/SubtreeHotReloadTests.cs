using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;
using static AIBT.Tests.Editor.Subtrees.SubtreeTestKit;

namespace AIBT.Tests.Editor.Subtrees
{
    /// <summary>
    /// ADR-P11-001 §4.7 with the reference executor's hot reload: subtree edits reach the runtime
    /// as ordinary edits of the expanded tree. Editing a leaf parameter inside a subtree migrates
    /// both of its instances; retargeting one reference restarts only that reference's branch.
    /// </summary>
    public sealed class SubtreeHotReloadTests
    {
        private const string ChildSource = "trees/child.aibt";

        [Test]
        public void EditingASubtreeLeafParameter_MigratesBothInstances()
        {
            var oldProgram = CompileParent(Resolver(ChildSource, RepeaterChild(5, true)));
            var oldMachine = CreateMachine(oldProgram);
            var completed = oldMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(completed.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(oldMachine.CaptureInspection().ActiveNodeCount, Is.EqualTo(0u));
            var oldA = oldMachine.CaptureNodeState(IndexOf(oldProgram, "a:root"));
            var oldB = oldMachine.CaptureNodeState(IndexOf(oldProgram, "b:root"));
            Assert.That(oldA.ActivationGeneration, Is.GreaterThan(0u));
            Assert.That(oldB.ActivationGeneration, Is.GreaterThan(0u));

            var newProgram = CompileParent(Resolver(ChildSource, RepeaterChild(5, false)));
            var classification = HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram);
            Assert.That(classification.RequiresFullRestart, Is.False);
            Assert.That(classification.RestartSubtreeRootNodeIds, Is.Empty);
            foreach (var id in new[] { "root", "a:root", "a:leaf", "b:root", "b:leaf" })
                Assert.That(classification.NodeVerdicts[new NodeId(id)].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Migrate), id);

            var fresh = Migrate(oldMachine, oldProgram, newProgram, classification, out var report);

            Assert.That(report.FellBackToFullRestart, Is.False);
            Assert.That(report.MigratedNodeCount, Is.EqualTo(5u), "The parent's root and both instances of the subtree.");
            Assert.That(fresh.CaptureNodeState(IndexOf(newProgram, "a:root")).Memory, Is.EqualTo(oldA.Memory));
            Assert.That(fresh.CaptureNodeState(IndexOf(newProgram, "a:root")).ActivationGeneration, Is.EqualTo(oldA.ActivationGeneration));
            Assert.That(fresh.CaptureNodeState(IndexOf(newProgram, "b:root")).Memory, Is.EqualTo(oldB.Memory));
            Assert.That(fresh.CaptureNodeState(IndexOf(newProgram, "b:root")).ActivationGeneration, Is.EqualTo(oldB.ActivationGeneration));
        }

        [Test]
        public void RetargetingOneReference_RestartsOnlyThatBranch()
        {
            var original = Resolver(ChildSource, RepeaterChild(5, true));
            var oldProgram = CompileParent(original);
            var oldMachine = CreateMachine(oldProgram);
            Assert.That(oldMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0)).RootResult, Is.EqualTo(NodeStatus.Success));
            var oldA = oldMachine.CaptureNodeState(IndexOf(oldProgram, "a:root"));

            // Reference 'b' now names another subtree whose root has another node type.
            var retargeted = Resolver(
                ChildSource, RepeaterChild(5, true),
                "trees/other.aibt", Tree("tests.p11.other", "root", NodeList(Seq("root", "leaf"), Leaf("leaf"))));
            var newProgram = CompileOk(Parse(ParentJson("tests.p11.other")), retargeted);
            var classification = HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram);

            Assert.That(classification.RequiresFullRestart, Is.False);
            Assert.That(classification.RestartSubtreeRootNodeIds.Select(id => id.Value), Is.EqualTo(new[] { "b:root" }));
            Assert.That(classification.NodeVerdicts[new NodeId("b:root")].Category, Is.EqualTo(HotReloadNodeVerdictCategory.IncompatibleRestart));
            foreach (var id in new[] { "root", "a:root", "a:leaf" })
                Assert.That(classification.NodeVerdicts[new NodeId(id)].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Migrate), id);

            var fresh = Migrate(oldMachine, oldProgram, newProgram, classification, out var report);

            Assert.That(report.FellBackToFullRestart, Is.False);
            Assert.That(fresh.CaptureNodeState(IndexOf(newProgram, "a:root")).ActivationGeneration, Is.EqualTo(oldA.ActivationGeneration),
                "The untouched instance keeps its state.");
            Assert.That(fresh.CaptureNodeState(IndexOf(newProgram, "b:root")).ActivationGeneration, Is.EqualTo(0u),
                "The retargeted branch starts fresh.");
            Assert.That(fresh.Update(new ReferenceUpdateContext(3, new Revision(1), 0)).RootResult, Is.EqualTo(NodeStatus.Success));
        }

        private static string RepeaterChild(uint count, bool stopOnFailure)
            => Tree("tests.p11.child", "root",
                NodeList(
                    "'root': { 'type': '" + Repeater + "', 'typeVersion': 1, 'parameters': { 'count': " + count + ", 'stopOnFailure': "
                    + (stopOnFailure ? "true" : "false") + " }, 'children': ['leaf'] }",
                    Leaf("leaf")));

        private static string ParentJson(string secondTreeId)
            => Tree("tests.p11.parent", "root",
                NodeList(Seq("root", "a", "b"), Ref("a", "tests.p11.child"), Ref("b", secondTreeId)));

        private static CompiledProgram CompileParent(ISubtreeResolver resolver)
            => CompileOk(Parse(ParentJson("tests.p11.child")), resolver);

        private static RuntimeNodeIndex IndexOf(CompiledProgram program, string id)
        {
            var entry = program.DebugMap.Single(item => item.AuthoringNodeId.Value == id);
            return new RuntimeNodeIndex(entry.RuntimeNodeIndex);
        }

        private static ReferenceExecutionMachine CreateMachine(CompiledProgram program)
            => new ReferenceExecutionMachine(
                program, new TreeInstanceId(1),
                ReferenceLeafRegistry.CreatePhase1Fixtures(), null,
                ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                RegisteredBlackboardRegistry.Empty,
                ReferenceObserverConditionRegistry.Empty);

        private static ReferenceExecutionMachine Migrate(
            ReferenceExecutionMachine oldMachine, CompiledProgram oldProgram, CompiledProgram newProgram,
            HotReloadClassificationResult classification, out HotReloadMigrationReport report)
            => HotReloadStateMigration.Migrate(
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
}
