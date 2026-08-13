using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class ParallelSafetyTests
    {
        [TestCase(4u, 4u, NodeMemoryLifetime.Activation)]
        [TestCase(8u, 8u, NodeMemoryLifetime.Activation)]
        [TestCase(8u, 4u, NodeMemoryLifetime.Instance)]
        [TestCase(0u, 1u, NodeMemoryLifetime.Activation)]
        public void InvalidDescriptorFaultsBeforeEnter(uint size, uint alignment, NodeMemoryLifetime lifetime)
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Success);
            var root = new P113TestNode(
                "aibt.core.parallel",
                P113TestProgram.ParallelConfiguration(ReferenceParallelPolicy.RequireAllSuccess),
                4,
                size,
                alignment,
                lifetime,
                null,
                P113TestProgram.Leaf("invalid-parallel-storage", child));
            var fixture = P113TestProgram.Create(root);

            var result = fixture.Machine.Update(Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(ReferenceExecutionDiagnosticCodes.InvalidNodeConfiguration));
            Assert.That(child.Calls, Is.Empty);
            Assert.That(fixture.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
        }

        [Test]
        public void InvalidConfigurationFaultsBeforeEnter()
        {
            var configuration = P113TestProgram.ParallelConfiguration(ReferenceParallelPolicy.RequireAllSuccess);
            configuration[0] = 3;
            var child = new ScriptedReferenceLeaf(NodeStatus.Success);
            var fixture = P113TestProgram.Create(P113TestProgram.Parallel(
                configuration,
                P113TestProgram.Leaf("invalid-parallel-config", child)));

            var result = fixture.Machine.Update(Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(ReferenceExecutionDiagnosticCodes.InvalidNodeConfiguration));
            Assert.That(fixture.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
        }

        [Test]
        public void AbortAtTerminalPendingChildExitsChildThenSuppressesParentAcceptance()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Success);
            var fixture = P113TestProgram.Create(P113TestProgram.Parallel(
                P113TestProgram.ParallelConfiguration(ReferenceParallelPolicy.RequireAllSuccess),
                P113TestProgram.Leaf("terminal-pending", child)));
            fixture.Machine.BeginUpdate(Update(1));
            for (var step = 0; step < 4; step++) fixture.Machine.AdvanceOneStep();

            fixture.Machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            ReferenceExecutionEnvelope result = default;
            while (fixture.Machine.HasOpenUpdate) result = fixture.Machine.AdvanceOneStep();

            Assert.That(result.RootResult, Is.Null);
            Assert.That(child.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Exit:Success" }));
            Assert.That(fixture.Trace.Records.Any(item =>
                item.Kind == ReferenceTraceEventKind.NodeTicked && item.NodeIndex == new RuntimeNodeIndex(0)), Is.False);
            Assert.That(fixture.Trace.Records.Any(item =>
                item.Kind == ReferenceTraceEventKind.NodeAbortStarted && item.NodeIndex == new RuntimeNodeIndex(0)), Is.True);
            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)), Is.EqualTo(new byte[8]));
        }

        [Test]
        public void PreEnterAbortHasNoLifecycleAndDoesNotIncrementGeneration()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Running);
            var fixture = P113TestProgram.Create(P113TestProgram.Parallel(
                P113TestProgram.ParallelConfiguration(ReferenceParallelPolicy.RequireAllSuccess),
                P113TestProgram.Leaf("parallel-pre-enter", child)));
            fixture.Machine.BeginUpdate(Update(1));

            fixture.Machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            var result = fixture.Machine.AdvanceOneStep();

            Assert.That(result.Steps, Is.Zero);
            Assert.That(child.Calls, Is.Empty);
            Assert.That(fixture.Machine.GetActivationGeneration(new RuntimeNodeIndex(0)), Is.Zero);
            Assert.That(fixture.Trace.Records.Any(item =>
                item.Kind == ReferenceTraceEventKind.NodeEntered
                || item.Kind == ReferenceTraceEventKind.NodeAbortStarted
                || item.Kind == ReferenceTraceEventKind.NodeExited), Is.False);
        }

        [Test]
        public void AbortImmediatelyAfterRestoringRunningBranchDoesNotFault()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Running, NodeStatus.Running);
            var fixture = P113TestProgram.Create(P113TestProgram.Parallel(
                P113TestProgram.ParallelConfiguration(ReferenceParallelPolicy.RequireAllSuccess),
                P113TestProgram.Leaf("resume-abort", child)));
            Assert.That(fixture.Machine.Update(Update(1)).Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(fixture.Machine.BeginUpdate(Update(2)).Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(fixture.Machine.AdvanceOneStep().Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));

            var requested = fixture.Machine.RequestAbort(NodeAbortReason.TreeStopped, new RuntimeNodeIndex(0));
            ReferenceExecutionEnvelope result = default;
            while (fixture.Machine.HasOpenUpdate) result = fixture.Machine.AdvanceOneStep();

            Assert.That(requested.Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(result.RootResult, Is.Null);
            Assert.That(fixture.Machine.IsFaulted, Is.False);
            Assert.That(child.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Abort:TreeStopped", "Exit:Aborted" }));
            var aborts = fixture.Trace.Records
                .Where(item => item.UpdateId == 2 && item.Kind == ReferenceTraceEventKind.NodeAbortStarted)
                .ToArray();
            Assert.That(aborts.Select(item => item.NodeIndex.Value), Is.EqualTo(new uint[] { 1, 0 }));
            Assert.That(aborts.All(item => item.AbortReason == NodeAbortReason.TreeStopped), Is.True);
        }

        [Test]
        public void WrongConfigurationAlignmentFaultsBeforeEnter()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Success);
            var root = new P113TestNode(
                "aibt.core.parallel",
                P113TestProgram.ParallelConfiguration(ReferenceParallelPolicy.RequireAllSuccess),
                1,
                8,
                4,
                NodeMemoryLifetime.Activation,
                null,
                P113TestProgram.Leaf("parallel-wrong-config-alignment", child));
            var fixture = P113TestProgram.Create(root);

            var result = fixture.Machine.Update(Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(fixture.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
        }

        private static ReferenceUpdateContext Update(ulong id)
            => new ReferenceUpdateContext(id, new Revision(id), checked((long)id * 10));
    }
}
