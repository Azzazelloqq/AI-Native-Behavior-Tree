using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Collections;
using static AIBT.Tests.Editor.Subtrees.SubtreeTestKit;

namespace AIBT.Tests.Editor.Subtrees
{
    /// <summary>
    /// ADR-P11-001 stage 2: <see cref="SubtreeHotReloadTests"/> on the native lifecycle machine. An
    /// edited subtree reaches a running native instance as an ordinary edit of the expanded tree:
    /// a leaf-parameter edit inside the subtree migrates both instances and the running one resumes
    /// where it was; retargeting one reference restarts only that reference's branch.
    /// </summary>
    public sealed class SubtreeNativeHotReloadTests
    {
        private const string ChildSource = "trees/child.aibt";
        private static readonly IReadOnlyCollection<NodeId> NoExclusions = new List<NodeId>();

        [Test]
        public void EditingASubtreeParameter_MigratesBothInstances_AndTheRunningLeafResumes()
        {
            var oldProgram = CompileParent(Resolver(ChildSource, RepeaterChild(2, true)));
            var newProgram = CompileParent(Resolver(ChildSource, RepeaterChild(2, false)));
            var classification = HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram);
            Assert.That(classification.RequiresFullRestart, Is.False);
            Assert.That(classification.RestartSubtreeRootNodeIds, Is.Empty);
            foreach (var id in new[] { "root", "a:root", "a:leaf", "b:root", "b:leaf" })
                Assert.That(classification.NodeVerdicts[new NodeId(id)].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Migrate), id);

            Assert.That(NativeHotReloadInstance.TryBuild(oldProgram, Allocator.Persistent, out var old, out var failure), Is.True, failure.Code.ToString());
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Assert.That(old.Machine.TryBeginUpdate(1, 10, out failure), Is.True, failure.Code.ToString());
                Assert.That(Drive(ref old.Machine, oldProgram, NodeStatus.Running), Is.EqualTo(new[] { "a:leaf:Enter", "a:leaf:Tick" }));

                Assert.That(NativeHotReloadStateMigration.TryMigrate(old, oldProgram, newProgram, classification, NoExclusions,
                    Allocator.Persistent, out fresh, out var report, out failure), Is.True, failure.Code.ToString());
                Assert.That(report.MigratedNodeCount, Is.EqualTo(5u), "The parent's root and both instances of the subtree.");
                Assert.That(report.ResetNodeCount, Is.Zero);

                Assert.That(fresh.Machine.TryBeginUpdate(2, 20, out failure), Is.True, failure.Code.ToString());
                Assert.That(Drive(ref fresh.Machine, newProgram, NodeStatus.Success), Is.EqualTo(new[]
                {
                    "a:leaf:Tick", "a:leaf:Exit:Success", "a:leaf:Enter", "a:leaf:Tick", "a:leaf:Exit:Success",
                    "b:leaf:Enter", "b:leaf:Tick", "b:leaf:Exit:Success", "b:leaf:Enter", "b:leaf:Tick", "b:leaf:Exit:Success",
                }), "Instance a resumes its running leaf (Tick, no Enter) and finishes its two repeats; then instance b runs.");
                Assert.That(fresh.Control[0].HasRootStatus, Is.EqualTo(1));
                Assert.That(fresh.Control[0].RootStatus, Is.EqualTo(NodeStatus.Success));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [Test]
        public void RetargetingOneReference_RestartsOnlyThatBranch()
        {
            var oldProgram = CompileParent(Resolver(ChildSource, RepeaterChild(2, true)));
            var retargeted = Resolver(
                ChildSource, RepeaterChild(2, true),
                "trees/other.aibt", Tree("tests.p11.other", "root", NodeList(Seq("root", "leaf"), Leaf("leaf"))));
            var newProgram = CompileOk(Parse(ParentJson("tests.p11.other")), retargeted);
            var classification = HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram);
            Assert.That(classification.RequiresFullRestart, Is.False);
            Assert.That(classification.RestartSubtreeRootNodeIds.Select(id => id.Value), Is.EqualTo(new[] { "b:root" }));

            // The caller expands a restarted subtree root to its whole branch (NativeHotReloadStateMigration).
            var excluded = newProgram.DebugMap.Select(entry => entry.AuthoringNodeId).Where(id => id.Value.StartsWith("b:")).ToList();
            Assert.That(excluded.Select(id => id.Value).OrderBy(id => id), Is.EqualTo(new[] { "b:leaf", "b:root" }));

            Assert.That(NativeHotReloadInstance.TryBuild(oldProgram, Allocator.Persistent, out var old, out var failure), Is.True, failure.Code.ToString());
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Assert.That(old.Machine.TryBeginUpdate(1, 10, out failure), Is.True, failure.Code.ToString());
                Assert.That(Drive(ref old.Machine, oldProgram, NodeStatus.Running), Is.EqualTo(new[] { "a:leaf:Enter", "a:leaf:Tick" }));

                Assert.That(NativeHotReloadStateMigration.TryMigrate(old, oldProgram, newProgram, classification, excluded,
                    Allocator.Persistent, out fresh, out var report, out failure), Is.True, failure.Code.ToString());
                Assert.That(report.MigratedNodeCount, Is.EqualTo(3u), "root, a:root and a:leaf keep their state.");
                Assert.That(report.ResetNodeCount, Is.EqualTo(2u), "The retargeted branch starts fresh.");

                Assert.That(fresh.Machine.TryBeginUpdate(2, 20, out failure), Is.True, failure.Code.ToString());
                Assert.That(Drive(ref fresh.Machine, newProgram, NodeStatus.Success), Is.EqualTo(new[]
                {
                    "a:leaf:Tick", "a:leaf:Exit:Success", "a:leaf:Enter", "a:leaf:Tick", "a:leaf:Exit:Success",
                    "b:leaf:Enter", "b:leaf:Tick", "b:leaf:Exit:Success",
                }), "The untouched instance resumes; the retargeted one runs its new tree once.");
                Assert.That(fresh.Control[0].RootStatus, Is.EqualTo(NodeStatus.Success));
            }
            finally { fresh.Dispose(); old.Dispose(); }
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

        /// <summary>
        /// Advances to the next Waiting or Completed boundary; every leaf tick returns
        /// <paramref name="tick"/>. Returns the leaf callbacks as "node:Phase[:reason]".
        /// </summary>
        private static List<string> Drive(ref NativeLifecycleMachineV1 machine, CompiledProgram program, NodeStatus tick)
        {
            var calls = new List<string>();
            for (var guard = 0; guard < 256; guard++)
            {
                Assert.That(machine.TryAdvance(out var step, out var failure), Is.True, failure.Code.ToString());
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    var id = program.DebugMap[(int)step.NodeIndex].AuthoringNodeId.Value;
                    var suffix = step.Phase == BurstCallbackPhase.Abort ? ":" + step.AbortReason
                        : step.Phase == BurstCallbackPhase.Exit ? ":" + step.ExitReason : "";
                    calls.Add(id + ":" + step.Phase + suffix);
                    Assert.That(machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success,
                        step.Phase == BurstCallbackPhase.Tick ? tick : NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
                }
                else if (step.Kind == NativeLifecycleStepKindV1.Completed || step.Kind == NativeLifecycleStepKindV1.Waiting) return calls;
            }
            Assert.Fail("No execution boundary reached.");
            return calls;
        }
    }
}
