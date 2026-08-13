using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class ReferenceLeafLifecycleTests
    {
        [TestCase(NodeStatus.Success, NodeExitReason.Success)]
        [TestCase(NodeStatus.Failure, NodeExitReason.Failure)]
        public void TerminalLeaf_UsesExactLifecycleAndExposesResultAfterExit(
            NodeStatus status,
            NodeExitReason exitReason)
        {
            var trace = new RecordingReferenceTraceSink();
            var handler = new ScriptedReferenceLeaf(status);
            var machine = ReferenceExecutionTestProgram.Create(handler, trace);

            var result = machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(result.RootResult, Is.EqualTo(status));
            Assert.That(result.Steps, Is.EqualTo(3));
            Assert.That(handler.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Exit:" + exitReason }));
            Assert.That(trace.Records.Select(item => item.Kind), Is.EqualTo(new[]
            {
                ReferenceTraceEventKind.UpdateStarted,
                ReferenceTraceEventKind.NodeEntered,
                ReferenceTraceEventKind.NodeTicked,
                ReferenceTraceEventKind.NodeExited,
                ReferenceTraceEventKind.UpdateCompleted,
            }));
            Assert.That(trace.Records.Select(item => item.Sequence), Is.EqualTo(new ulong[] { 1, 2, 3, 4, 5 }));
            Assert.That(trace.Records.All(item => item.TraceFormatVersion == ReferenceTraceRecord.FormatVersion), Is.True);
            Assert.That(trace.Records.All(item => item.UpdateId == 1), Is.True);
            Assert.That(trace.Records.All(item => item.SnapshotRevision == new Revision(101)), Is.True);
            Assert.That(trace.Records.All(item => item.TreeInstanceId == new TreeInstanceId(41)), Is.True);
            Assert.That(trace.Records.All(item => item.TreeSemanticHash.IsValid), Is.True);
            Assert.That(trace.Records[2].Status, Is.EqualTo(status));
            Assert.That(trace.Records[3].ExitReason, Is.EqualTo(exitReason));
        }

        [Test]
        public void RunningLeaf_TicksAtMostOncePerEligibleUpdate()
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Running, NodeStatus.Success);
            var machine = ReferenceExecutionTestProgram.Create(handler);

            var first = machine.Update(ReferenceExecutionTestProgram.Update(1));
            var duplicate = machine.Update(ReferenceExecutionTestProgram.Update(1));
            var second = machine.Update(ReferenceExecutionTestProgram.Update(2));

            Assert.That(first.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(first.Steps, Is.EqualTo(2));
            Assert.That(duplicate.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(second.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(second.Steps, Is.EqualTo(2));
            Assert.That(handler.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Tick", "Exit:Success" }));
        }

        [Test]
        public void UpdateContext_PreservesNegativeSignedTimePoint()
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success);
            var machine = ReferenceExecutionTestProgram.Create(handler);

            machine.Update(new ReferenceUpdateContext(1, new Revision(101), -25));

            Assert.That(handler.TimesAtTick, Is.EqualTo(new long[] { -25 }));
        }

        [Test]
        public void ContextualAbort_CallsAbortThenExitAborted()
        {
            var trace = new RecordingReferenceTraceSink();
            var handler = new ScriptedReferenceLeaf(NodeStatus.Running);
            var machine = ReferenceExecutionTestProgram.Create(handler, trace);
            machine.Update(ReferenceExecutionTestProgram.Update(1));

            var result = machine.Abort(
                ReferenceExecutionTestProgram.Update(2),
                NodeAbortReason.TreeStopped,
                new RuntimeNodeIndex(0));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(result.RootResult, Is.Null);
            Assert.That(result.Steps, Is.EqualTo(2));
            Assert.That(handler.Calls, Is.EqualTo(new[]
            {
                "Enter", "Tick", "Abort:TreeStopped", "Exit:Aborted",
            }));
            Assert.That(trace.Records[5].Kind, Is.EqualTo(ReferenceTraceEventKind.NodeAbortStarted));
            Assert.That(trace.Records[5].AbortReason, Is.EqualTo(NodeAbortReason.TreeStopped));
            Assert.That(trace.Records[5].SourceNodeIndex, Is.EqualTo(new RuntimeNodeIndex(0)));
            Assert.That(trace.Records[6].Kind, Is.EqualTo(ReferenceTraceEventKind.NodeExited));
            Assert.That(trace.Records[6].ExitReason, Is.EqualTo(NodeExitReason.Aborted));
            var abortPass = trace.Records.Skip(4).ToArray();
            Assert.That(abortPass.Select(item => item.Kind), Is.EqualTo(new[]
            {
                ReferenceTraceEventKind.UpdateStarted,
                ReferenceTraceEventKind.NodeAbortStarted,
                ReferenceTraceEventKind.NodeExited,
                ReferenceTraceEventKind.UpdateCompleted,
            }));
            Assert.That(abortPass.All(item => item.UpdateId == 2), Is.True);
            Assert.That(abortPass.All(item => item.SnapshotRevision == new Revision(102)), Is.True);
            Assert.That(abortPass.All(item => item.TreeInstanceId == new TreeInstanceId(41)), Is.True);
            Assert.That(abortPass.All(item => item.TreeSemanticHash.IsValid), Is.True);
        }

        [Test]
        public void AbortRequestedAfterEnter_SkipsTickAndRemainsAtomic()
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success);
            var machine = ReferenceExecutionTestProgram.Create(handler);

            Assert.That(machine.BeginUpdate(ReferenceExecutionTestProgram.Update(1)).Progress,
                Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(machine.AdvanceOneStep().Steps, Is.EqualTo(1));
            Assert.That(machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0)).Steps, Is.Zero);
            Assert.That(machine.AdvanceOneStep().Steps, Is.EqualTo(1));
            var exit = machine.AdvanceOneStep();

            Assert.That(exit.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(handler.Calls, Is.EqualTo(new[] { "Enter", "Abort:Explicit", "Exit:Aborted" }));
        }

        [Test]
        public void AbortRequestedBeforeEnter_DiscardsInactiveFrameWithoutCallbacks()
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success);
            var machine = ReferenceExecutionTestProgram.Create(handler);
            machine.BeginUpdate(ReferenceExecutionTestProgram.Update(1));

            var request = machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            var completed = machine.AdvanceOneStep();

            Assert.That(request.Steps, Is.Zero);
            Assert.That(completed.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(completed.Steps, Is.Zero);
            Assert.That(handler.Calls, Is.Empty);
        }

        [Test]
        public void TerminalPendingExit_CannotBeReplacedByAbort()
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success);
            var machine = ReferenceExecutionTestProgram.Create(handler);
            machine.BeginUpdate(ReferenceExecutionTestProgram.Update(1));
            machine.AdvanceOneStep();
            machine.AdvanceOneStep();

            machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            var result = machine.AdvanceOneStep();

            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(handler.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Exit:Success" }));
        }

        [Test]
        public void TerminalTree_DoesNotImplicitlyRestart()
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success);
            var machine = ReferenceExecutionTestProgram.Create(handler);
            machine.Update(ReferenceExecutionTestProgram.Update(1));

            var result = machine.Update(ReferenceExecutionTestProgram.Update(2));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(result.Steps, Is.Zero);
            Assert.That(handler.Calls, Has.Count.EqualTo(3));
        }

        [Test]
        public void Restart_RequiresInactiveTreeAndPreservesPerNodeGeneration()
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success, NodeStatus.Success);
            var machine = ReferenceExecutionTestProgram.Create(handler);
            machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(machine.Restart().Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            machine.Update(ReferenceExecutionTestProgram.Update(2));

            Assert.That(handler.EnterGenerations, Is.EqualTo(new uint[] { 1, 2 }));
        }

        [Test]
        public void RestartWhileRunning_IsRejected()
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Running);
            var machine = ReferenceExecutionTestProgram.Create(handler);
            machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(machine.Restart().Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
        }

        [Test]
        public void RestartWhileUpdateIsSuspendedBeforeEnter_IsRejected()
        {
            var machine = ReferenceExecutionTestProgram.Create(new ScriptedReferenceLeaf(NodeStatus.Success));
            machine.BeginUpdate(ReferenceExecutionTestProgram.Update(1));

            Assert.That(machine.Restart().Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(machine.HasOpenUpdate, Is.True);
        }

        [Test]
        public void OpenUpdate_RejectsNewUpdateEntryAndPreservesFrozenContextUntilCompletion()
        {
            var trace = new RecordingReferenceTraceSink();
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success);
            var machine = ReferenceExecutionTestProgram.Create(handler, trace);
            machine.BeginUpdate(ReferenceExecutionTestProgram.Update(1));

            var updateReject = machine.Update(ReferenceExecutionTestProgram.Update(2));
            var beginReject = machine.BeginUpdate(ReferenceExecutionTestProgram.Update(3));

            Assert.That(updateReject.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(beginReject.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(machine.HasOpenUpdate, Is.True);
            ReferenceExecutionEnvelope step;
            do
            {
                step = machine.AdvanceOneStep();
            }
            while (step.Progress == ReferenceExecutionProgress.Suspended);

            Assert.That(step.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(step.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(machine.HasOpenUpdate, Is.False);
            var semanticRecords = trace.Records.Where(item =>
                item.Kind != ReferenceTraceEventKind.DiagnosticRaised).ToArray();
            Assert.That(semanticRecords.All(item => item.UpdateId == 1), Is.True);
            Assert.That(semanticRecords.All(item => item.SnapshotRevision == new Revision(101)), Is.True);
            Assert.That(trace.Records.Count(item => item.Kind == ReferenceTraceEventKind.DiagnosticRaised), Is.EqualTo(2));
        }

        [TestCase(NodeMemoryLifetime.Activation, 0)]
        [TestCase(NodeMemoryLifetime.Instance, 77)]
        public void MemoryLifetime_IsAppliedImmediatelyAfterTerminalExit(
            NodeMemoryLifetime lifetime,
            byte expectedAfterExit)
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success)
            {
                TickMemoryValue = 31,
                ExitMemoryValue = 77,
            };
            var machine = ReferenceExecutionTestProgram.Create(handler, memoryLifetime: lifetime);

            machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(machine.CopyNodeMemory(new RuntimeNodeIndex(0)), Is.EqualTo(new[] { expectedAfterExit }));
            machine.Restart();
            Assert.That(machine.CopyNodeMemory(new RuntimeNodeIndex(0)), Is.EqualTo(new byte[] { 0 }));
        }

        [TestCase(NodeMemoryLifetime.Activation, 0)]
        [TestCase(NodeMemoryLifetime.Instance, 77)]
        public void MemoryLifetime_IsAppliedImmediatelyAfterAbortedExit(
            NodeMemoryLifetime lifetime,
            byte expectedAfterExit)
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Running)
            {
                TickMemoryValue = 31,
                ExitMemoryValue = 77,
            };
            var machine = ReferenceExecutionTestProgram.Create(handler, memoryLifetime: lifetime);
            machine.Update(ReferenceExecutionTestProgram.Update(1));

            machine.Abort(ReferenceExecutionTestProgram.Update(2), NodeAbortReason.Explicit, new RuntimeNodeIndex(0));

            Assert.That(machine.CopyNodeMemory(new RuntimeNodeIndex(0)), Is.EqualTo(new[] { expectedAfterExit }));
            machine.Restart();
            Assert.That(machine.CopyNodeMemory(new RuntimeNodeIndex(0)), Is.EqualTo(new byte[] { 0 }));
        }
    }
}
