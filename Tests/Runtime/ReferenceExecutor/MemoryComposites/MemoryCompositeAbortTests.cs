using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class MemoryCompositeAbortTests
    {
        [Test]
        public void ExternalAbortTraversesRunningChildThenParentExactlyOnce()
        {
            var running = new ScriptedReferenceLeaf(NodeStatus.Running);
            var fixture = MemoryCompositeTestProgram.Sequence(running);
            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            var result = fixture.Machine.Abort(
                ReferenceExecutionTestProgram.Update(2),
                NodeAbortReason.TreeStopped,
                new RuntimeNodeIndex(0));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(result.Steps, Is.EqualTo(4));
            Assert.That(running.Calls, Is.EqualTo(new[]
            {
                "Enter", "Tick", "Abort:TreeStopped", "Exit:Aborted",
            }));
            var abortRecords = fixture.Trace.Records.Where(item => item.UpdateId == 2).ToArray();
            Assert.That(abortRecords.Select(item => item.Kind), Is.EqualTo(new[]
            {
                ReferenceTraceEventKind.UpdateStarted,
                ReferenceTraceEventKind.NodeAbortStarted,
                ReferenceTraceEventKind.NodeExited,
                ReferenceTraceEventKind.NodeAbortStarted,
                ReferenceTraceEventKind.NodeExited,
                ReferenceTraceEventKind.UpdateCompleted,
            }));
            Assert.That(abortRecords.Where(item => item.Kind == ReferenceTraceEventKind.NodeAbortStarted)
                .All(item => item.AbortReason == NodeAbortReason.TreeStopped
                    && item.SourceNodeIndex == new RuntimeNodeIndex(0)), Is.True);
        }

        [Test]
        public void AbortAfterTerminalChildTickCompletesChildExitButSuppressesSiblingSelection()
        {
            var terminal = new ScriptedReferenceLeaf(NodeStatus.Success);
            var unreached = new ScriptedReferenceLeaf(NodeStatus.Success);
            var fixture = MemoryCompositeTestProgram.Sequence(terminal, unreached);
            fixture.Machine.BeginUpdate(ReferenceExecutionTestProgram.Update(1));
            fixture.Machine.AdvanceOneStep(); // parent Enter
            fixture.Machine.AdvanceOneStep(); // select child
            fixture.Machine.AdvanceOneStep(); // child Enter
            fixture.Machine.AdvanceOneStep(); // child terminal Tick

            fixture.Machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            var traceStart = fixture.Trace.Records.Count;
            ulong remainingSteps = 0;
            ReferenceExecutionEnvelope step;
            do
            {
                step = fixture.Machine.AdvanceOneStep();
                remainingSteps += step.Steps;
            }
            while (step.Progress == ReferenceExecutionProgress.Suspended);

            Assert.That(step.RootResult, Is.Null);
            Assert.That(terminal.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Exit:Success" }));
            Assert.That(unreached.Calls, Is.Empty);
            Assert.That(fixture.Trace.Records.Count(item => item.Kind == ReferenceTraceEventKind.NodeAbortStarted),
                Is.EqualTo(1));
            Assert.That(remainingSteps, Is.EqualTo(3));
            Assert.That(fixture.Trace.Records.Skip(traceStart).Select(item => item.Kind), Is.EqualTo(new[]
            {
                ReferenceTraceEventKind.NodeExited,
                ReferenceTraceEventKind.NodeAbortStarted,
                ReferenceTraceEventKind.NodeExited,
                ReferenceTraceEventKind.UpdateCompleted,
            }));
            Assert.That(fixture.Trace.Records.Skip(traceStart)
                .Any(item => item.Kind == ReferenceTraceEventKind.NodeTicked && item.NodeIndex == new RuntimeNodeIndex(0)), Is.False);
        }

        [Test]
        public void AbortBeforeSelectionAbortsOnlyEnteredParent()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Success);
            var fixture = MemoryCompositeTestProgram.Sequence(child);
            fixture.Machine.BeginUpdate(ReferenceExecutionTestProgram.Update(1));
            fixture.Machine.AdvanceOneStep();

            fixture.Machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            fixture.Machine.AdvanceOneStep();
            var exit = fixture.Machine.AdvanceOneStep();

            Assert.That(exit.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(child.Calls, Is.Empty);
        }

        [Test]
        public void AbortAfterSelectionDiscardsInactiveChildThenAbortsParent()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Success);
            var fixture = MemoryCompositeTestProgram.Sequence(child);
            fixture.Machine.BeginUpdate(ReferenceExecutionTestProgram.Update(1));
            fixture.Machine.AdvanceOneStep();
            fixture.Machine.AdvanceOneStep();

            fixture.Machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            fixture.Machine.AdvanceOneStep();
            var exit = fixture.Machine.AdvanceOneStep();

            Assert.That(exit.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(child.Calls, Is.Empty);
        }

        [Test]
        public void CompositeAbortClearsCursorAndRestartReentersWithNextGeneration()
        {
            var first = new ScriptedReferenceLeaf(NodeStatus.Success);
            var running = new ScriptedReferenceLeaf(NodeStatus.Running);
            var fixture = MemoryCompositeTestProgram.Sequence(first, running);
            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)),
                Is.EqualTo(new byte[] { 1, 0, 0, 0 }));

            fixture.Machine.Abort(
                ReferenceExecutionTestProgram.Update(2),
                NodeAbortReason.Explicit,
                new RuntimeNodeIndex(0));

            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)),
                Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
            Assert.That(fixture.Machine.GetActivationGeneration(new RuntimeNodeIndex(0)), Is.EqualTo(1));
            fixture.Machine.Restart();
            fixture.Machine.BeginUpdate(ReferenceExecutionTestProgram.Update(3));
            fixture.Machine.AdvanceOneStep();
            Assert.That(fixture.Machine.GetActivationGeneration(new RuntimeNodeIndex(0)), Is.EqualTo(2));
        }

        [Test]
        public void PreEnterCompositeAbortHasZeroCallbacksLifecycleTraceAndSteps()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Success);
            var fixture = MemoryCompositeTestProgram.Sequence(child);
            fixture.Machine.BeginUpdate(ReferenceExecutionTestProgram.Update(1));

            var request = fixture.Machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            var completed = fixture.Machine.AdvanceOneStep();

            Assert.That(request.Steps, Is.Zero);
            Assert.That(completed.Steps, Is.Zero);
            Assert.That(completed.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(child.Calls, Is.Empty);
            Assert.That(fixture.Trace.Records.Select(item => item.Kind), Is.EqualTo(new[]
            {
                ReferenceTraceEventKind.UpdateStarted,
                ReferenceTraceEventKind.UpdateCompleted,
            }));
        }
    }
}
