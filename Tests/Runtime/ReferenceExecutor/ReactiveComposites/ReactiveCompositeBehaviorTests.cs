using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class ReactiveCompositeBehaviorTests
    {
        [Test]
        public void ReactiveSequenceTableCoversFailureSuccessChainsAndEmpty()
        {
            Assert.That(ReactiveCompositeTestProgram.Sequence().Machine
                .Update(ReferenceExecutionTestProgram.Update(1)).RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(ReactiveCompositeTestProgram.Sequence(new ScriptedReferenceLeaf(NodeStatus.Failure)).Machine
                .Update(ReferenceExecutionTestProgram.Update(1)).RootResult, Is.EqualTo(NodeStatus.Failure));
            Assert.That(ReactiveCompositeTestProgram.Sequence(
                    new ScriptedReferenceLeaf(NodeStatus.Success),
                    new ScriptedReferenceLeaf(NodeStatus.Failure)).Machine
                .Update(ReferenceExecutionTestProgram.Update(1)).RootResult, Is.EqualTo(NodeStatus.Failure));
            Assert.That(ReactiveCompositeTestProgram.Sequence(
                    new ScriptedReferenceLeaf(NodeStatus.Success),
                    new ScriptedReferenceLeaf(NodeStatus.Success)).Machine
                .Update(ReferenceExecutionTestProgram.Update(1)).RootResult, Is.EqualTo(NodeStatus.Success));
        }

        [Test]
        public void ReactiveSelectorTableMirrorsSequenceRules()
        {
            Assert.That(ReactiveCompositeTestProgram.Selector().Machine
                .Update(ReferenceExecutionTestProgram.Update(1)).RootResult, Is.EqualTo(NodeStatus.Failure));
            Assert.That(ReactiveCompositeTestProgram.Selector(new ScriptedReferenceLeaf(NodeStatus.Success)).Machine
                .Update(ReferenceExecutionTestProgram.Update(1)).RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(ReactiveCompositeTestProgram.Selector(
                    new ScriptedReferenceLeaf(NodeStatus.Failure),
                    new ScriptedReferenceLeaf(NodeStatus.Success)).Machine
                .Update(ReferenceExecutionTestProgram.Update(1)).RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(ReactiveCompositeTestProgram.Selector(
                    new ScriptedReferenceLeaf(NodeStatus.Failure),
                    new ScriptedReferenceLeaf(NodeStatus.Failure)).Machine
                .Update(ReferenceExecutionTestProgram.Update(1)).RootResult, Is.EqualTo(NodeStatus.Failure));
        }

        [Test]
        public void ReactiveSequenceAbortsOldRunningBranchBeforeReevaluatingFromZero()
        {
            var guard = new ScriptedReferenceLeaf(NodeStatus.Success, NodeStatus.Success);
            var action = new ScriptedReferenceLeaf(NodeStatus.Running, NodeStatus.Running);
            var fixture = ReactiveCompositeTestProgram.Sequence(guard, action);
            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            var second = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(2));

            Assert.That(second.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(guard.Calls, Is.EqualTo(new[]
            {
                "Enter", "Tick", "Exit:Success", "Enter", "Tick", "Exit:Success",
            }));
            Assert.That(action.Calls, Is.EqualTo(new[]
            {
                "Enter", "Tick", "Abort:Explicit", "Exit:Aborted", "Enter", "Tick",
            }));
            var update2 = fixture.Trace.Records.Where(item => item.UpdateId == 2).ToArray();
            var oldExit = System.Array.FindIndex(update2, item =>
                item.Kind == ReferenceTraceEventKind.NodeExited && item.ExitReason == NodeExitReason.Aborted);
            var guardEnter = System.Array.FindIndex(update2, item =>
                item.Kind == ReferenceTraceEventKind.NodeEntered && item.NodeIndex == new RuntimeNodeIndex(1));
            Assert.That(oldExit, Is.LessThan(guardEnter));
            Assert.That(fixture.Machine.GetActivationGeneration(new RuntimeNodeIndex(0)), Is.EqualTo(1));
            Assert.That(fixture.Machine.GetActivationGeneration(new RuntimeNodeIndex(1)), Is.EqualTo(2));
            Assert.That(fixture.Machine.GetActivationGeneration(new RuntimeNodeIndex(2)), Is.EqualTo(2));
        }

        [Test]
        public void ReactiveSelectorRechecksHigherPriorityBranchEveryUpdate()
        {
            var high = new ScriptedReferenceLeaf(NodeStatus.Failure, NodeStatus.Success);
            var low = new ScriptedReferenceLeaf(NodeStatus.Running);
            var fixture = ReactiveCompositeTestProgram.Selector(high, low);
            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            var second = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(2));

            Assert.That(second.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(low.Calls, Is.EqualTo(new[]
            {
                "Enter", "Tick", "Abort:Explicit", "Exit:Aborted",
            }));
            Assert.That(high.Calls.Count(call => call == "Tick"), Is.EqualTo(2));
        }

        [Test]
        public void StepwiseResumeOfSameUpdateDoesNotRepeatReplacement()
        {
            var running = new ScriptedReferenceLeaf(NodeStatus.Running, NodeStatus.Running);
            var fixture = ReactiveCompositeTestProgram.Sequence(running);
            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            fixture.Machine.BeginUpdate(ReferenceExecutionTestProgram.Update(2));

            ReferenceExecutionEnvelope step;
            do
            {
                step = fixture.Machine.AdvanceOneStep();
            }
            while (step.Progress == ReferenceExecutionProgress.Suspended);

            Assert.That(step.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(running.Calls.Count(call => call.StartsWith("Abort")), Is.EqualTo(1));
            Assert.That(running.Calls.Count(call => call == "Enter"), Is.EqualTo(2));
        }

        [Test]
        public void ShallowestReactiveOwnerControlsNestedReplacement()
        {
            var running = new ScriptedReferenceLeaf(NodeStatus.Running, NodeStatus.Running);
            var fixture = ReactiveCompositeTestProgram.NestedSequences(running);
            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(2));

            var update2Aborts = fixture.Trace.Records.Where(item =>
                item.UpdateId == 2 && item.Kind == ReferenceTraceEventKind.NodeAbortStarted).ToArray();
            Assert.That(update2Aborts.Select(item => item.NodeIndex.Value), Is.EqualTo(new uint[] { 2, 1 }));
            Assert.That(update2Aborts.Any(item => item.NodeIndex.Value == 0), Is.False);
            Assert.That(running.Calls.Count(call => call == "Enter"), Is.EqualTo(2));
            Assert.That(update2Aborts.Count(item => item.NodeIndex.Value == 1), Is.EqualTo(1));
            Assert.That(fixture.Machine.GetActivationGeneration(new RuntimeNodeIndex(0)), Is.EqualTo(1));
            Assert.That(fixture.Machine.GetActivationGeneration(new RuntimeNodeIndex(1)), Is.EqualTo(2));
            Assert.That(fixture.Machine.GetActivationGeneration(new RuntimeNodeIndex(2)), Is.EqualTo(2));
        }

        [Test]
        public void ReactiveResetClearsActivationCursorBeforeReevaluation()
        {
            var guard = new ScriptedReferenceLeaf(NodeStatus.Success, NodeStatus.Success);
            var action = new ScriptedReferenceLeaf(NodeStatus.Running, NodeStatus.Running);
            var fixture = ReactiveCompositeTestProgram.Sequence(guard, action);
            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)),
                Is.EqualTo(new byte[] { 1, 0, 0, 0 }));
            fixture.Machine.BeginUpdate(ReferenceExecutionTestProgram.Update(2));
            fixture.Machine.AdvanceOneStep();
            fixture.Machine.AdvanceOneStep();
            fixture.Machine.AdvanceOneStep(); // reactive reset transition

            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)),
                Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
        }
    }
}
