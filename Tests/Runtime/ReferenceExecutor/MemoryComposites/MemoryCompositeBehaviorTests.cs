using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class MemoryCompositeBehaviorTests
    {
        [Test]
        public void EmptySequenceSucceedsAndEmptySelectorFails()
        {
            var sequence = MemoryCompositeTestProgram.Sequence();
            var selector = MemoryCompositeTestProgram.Selector();

            var sequenceResult = sequence.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            var selectorResult = selector.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(sequenceResult.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(selectorResult.RootResult, Is.EqualTo(NodeStatus.Failure));
            Assert.That(sequenceResult.Steps, Is.EqualTo(3));
            Assert.That(selectorResult.Steps, Is.EqualTo(3));
        }

        [Test]
        public void SequenceAdvancesAcrossImmediateSuccessesInOneUpdate()
        {
            var first = new ScriptedReferenceLeaf(NodeStatus.Success);
            var second = new ScriptedReferenceLeaf(NodeStatus.Success);
            var fixture = MemoryCompositeTestProgram.Sequence(first, second);

            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(result.Steps, Is.EqualTo(12));
            Assert.That(first.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Exit:Success" }));
            Assert.That(second.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Exit:Success" }));
            Assert.That(fixture.Trace.Records.Select(item => item.Kind), Is.EqualTo(new[]
            {
                ReferenceTraceEventKind.UpdateStarted,
                ReferenceTraceEventKind.NodeEntered,
                ReferenceTraceEventKind.NodeEntered,
                ReferenceTraceEventKind.NodeTicked,
                ReferenceTraceEventKind.NodeExited,
                ReferenceTraceEventKind.NodeEntered,
                ReferenceTraceEventKind.NodeTicked,
                ReferenceTraceEventKind.NodeExited,
                ReferenceTraceEventKind.NodeTicked,
                ReferenceTraceEventKind.NodeExited,
                ReferenceTraceEventKind.UpdateCompleted,
            }));
        }

        [Test]
        public void SequenceStopsAtFirstFailureWithoutEnteringLaterChild()
        {
            var first = new ScriptedReferenceLeaf(NodeStatus.Success);
            var failing = new ScriptedReferenceLeaf(NodeStatus.Failure);
            var unreached = new ScriptedReferenceLeaf(NodeStatus.Success);
            var fixture = MemoryCompositeTestProgram.Sequence(first, failing, unreached);

            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Failure));
            Assert.That(unreached.Calls, Is.Empty);
        }

        [Test]
        public void SelectorAdvancesAcrossFailuresAndStopsAtFirstSuccess()
        {
            var first = new ScriptedReferenceLeaf(NodeStatus.Failure);
            var second = new ScriptedReferenceLeaf(NodeStatus.Failure);
            var successful = new ScriptedReferenceLeaf(NodeStatus.Success);
            var unreached = new ScriptedReferenceLeaf(NodeStatus.Failure);
            var fixture = MemoryCompositeTestProgram.Selector(first, second, successful, unreached);

            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(unreached.Calls, Is.Empty);
        }

        [Test]
        public void RunningChildRetainsCursorAndEarlierChildIsNotReticked()
        {
            var completed = new ScriptedReferenceLeaf(NodeStatus.Success);
            var running = new ScriptedReferenceLeaf(NodeStatus.Running, NodeStatus.Success);
            var final = new ScriptedReferenceLeaf(NodeStatus.Success);
            var fixture = MemoryCompositeTestProgram.Sequence(completed, running, final);

            var first = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)),
                Is.EqualTo(new byte[] { 1, 0, 0, 0 }));
            var second = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(2));

            Assert.That(first.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(second.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(completed.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Exit:Success" }));
            Assert.That(running.Calls.Count(call => call == "Tick"), Is.EqualTo(2));
            Assert.That(final.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Exit:Success" }));
            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)),
                Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
        }

        [Test]
        public void InvalidMemoryCompositeStorageFaultsBeforeLifecycleTrace()
        {
            var fixture = MemoryCompositeTestProgram.InvalidSequenceStorage(8, 4);

            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.Diagnostics.Single().Code,
                Is.EqualTo(ReferenceExecutionDiagnosticCodes.InvalidCompositeState));
            Assert.That(fixture.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
        }
    }
}
