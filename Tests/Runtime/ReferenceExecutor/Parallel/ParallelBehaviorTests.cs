using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class ParallelBehaviorTests
    {
        [Test]
        public void VisitsEveryNonTerminalBranchInSemanticOrderOncePerUpdate()
        {
            var first = new ScriptedReferenceLeaf(NodeStatus.Running, NodeStatus.Success);
            var terminal = new ScriptedReferenceLeaf(NodeStatus.Success);
            var third = new ScriptedReferenceLeaf(NodeStatus.Running, NodeStatus.Success);
            var fixture = Create(
                ReferenceParallelPolicy.RequireAllSuccess,
                P113TestProgram.Leaf("ordered-first", first),
                P113TestProgram.Leaf("ordered-terminal", terminal),
                P113TestProgram.Leaf("ordered-third", third));

            var update1 = fixture.Machine.Update(Update(1));
            var update2 = fixture.Machine.Update(Update(2));

            Assert.That(update1.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(update2.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(first.Calls.Count(item => item == "Tick"), Is.EqualTo(2));
            Assert.That(terminal.Calls.Count(item => item == "Tick"), Is.EqualTo(1));
            Assert.That(third.Calls.Count(item => item == "Tick"), Is.EqualTo(2));
            Assert.That(first.EnterGenerations, Is.EqualTo(new uint[] { 1 }));
            Assert.That(third.EnterGenerations, Is.EqualTo(new uint[] { 1 }));
            var firstUpdateTicks = fixture.Trace.Records
                .Where(item => item.UpdateId == 1 && item.Kind == ReferenceTraceEventKind.NodeTicked)
                .Select(item => item.NodeIndex.Value)
                .ToArray();
            Assert.That(firstUpdateTicks, Is.EqualTo(new uint[] { 1, 2, 3 }));
        }

        [TestCase(ReferenceParallelPolicy.RequireAnySuccess, NodeStatus.Success, NodeStatus.Running)]
        [TestCase(ReferenceParallelPolicy.RequireAllSuccess, NodeStatus.Failure, NodeStatus.Running)]
        public void TerminalPolicyStillVisitsFullPassThenAbortsRunningBranchesInReverseOrder(
            ReferenceParallelPolicy policy,
            NodeStatus firstResult,
            NodeStatus runningResult)
        {
            var terminal = new ScriptedReferenceLeaf(firstResult);
            var second = new ScriptedReferenceLeaf(runningResult);
            var third = new ScriptedReferenceLeaf(runningResult);
            var fixture = Create(
                policy,
                P113TestProgram.Leaf("terminal", terminal),
                P113TestProgram.Leaf("reverse-second", second),
                P113TestProgram.Leaf("reverse-third", third));

            var result = fixture.Machine.Update(Update(1));

            Assert.That(result.RootResult, Is.EqualTo(firstResult));
            Assert.That(second.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Abort:Explicit", "Exit:Aborted" }));
            Assert.That(third.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Abort:Explicit", "Exit:Aborted" }));
            var abortOrder = fixture.Trace.Records
                .Where(item => item.Kind == ReferenceTraceEventKind.NodeAbortStarted)
                .Select(item => item.NodeIndex.Value)
                .ToArray();
            Assert.That(abortOrder, Is.EqualTo(new uint[] { 3, 2 }));
            var parallelTick = fixture.Trace.Records.Single(item =>
                item.Kind == ReferenceTraceEventKind.NodeTicked && item.NodeIndex == new RuntimeNodeIndex(0));
            Assert.That(fixture.Trace.Records.IndexOf(parallelTick),
                Is.GreaterThan(fixture.Trace.Records.FindLastIndex(item => item.Kind == ReferenceTraceEventKind.NodeExited && item.ExitReason == NodeExitReason.Aborted)));
        }

        [TestCase(ReferenceParallelTieBreak.FailureFirst, NodeStatus.Failure)]
        [TestCase(ReferenceParallelTieBreak.SuccessFirst, NodeStatus.Success)]
        public void ThresholdTieIsResolvedAfterFullVisit(ReferenceParallelTieBreak tieBreak, NodeStatus expected)
        {
            var success = new ScriptedReferenceLeaf(NodeStatus.Success);
            var failure = new ScriptedReferenceLeaf(NodeStatus.Failure);
            var root = P113TestProgram.Parallel(
                P113TestProgram.ParallelConfiguration(ReferenceParallelPolicy.Threshold, 1, 1, tieBreak),
                P113TestProgram.Leaf("tie-success", success),
                P113TestProgram.Leaf("tie-failure", failure));
            var fixture = P113TestProgram.Create(root);

            var result = fixture.Machine.Update(Update(1));

            Assert.That(result.RootResult, Is.EqualTo(expected));
            Assert.That(success.Calls.Count(item => item == "Tick"), Is.EqualTo(1));
            Assert.That(failure.Calls.Count(item => item == "Tick"), Is.EqualTo(1));
        }

        [Test]
        public void ExternalAbortVisitsRunningBranchesReverseThenParallel()
        {
            var first = new ScriptedReferenceLeaf(NodeStatus.Running);
            var second = new ScriptedReferenceLeaf(NodeStatus.Running);
            var third = new ScriptedReferenceLeaf(NodeStatus.Running);
            var fixture = Create(
                ReferenceParallelPolicy.RequireAllSuccess,
                P113TestProgram.Leaf("abort-first", first),
                P113TestProgram.Leaf("abort-second", second),
                P113TestProgram.Leaf("abort-third", third));
            Assert.That(fixture.Machine.Update(Update(1)).Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));

            var result = fixture.Machine.Abort(Update(2), NodeAbortReason.TreeStopped, new RuntimeNodeIndex(0));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(result.RootResult, Is.Null);
            var abortOrder = fixture.Trace.Records
                .Where(item => item.UpdateId == 2 && item.Kind == ReferenceTraceEventKind.NodeAbortStarted)
                .Select(item => item.NodeIndex.Value)
                .ToArray();
            Assert.That(abortOrder, Is.EqualTo(new uint[] { 3, 2, 1, 0 }));
            Assert.That(fixture.Trace.Records
                .Where(item => item.UpdateId == 2 && item.Kind == ReferenceTraceEventKind.NodeAbortStarted)
                .All(item => item.AbortReason == NodeAbortReason.TreeStopped && item.SourceNodeIndex == new RuntimeNodeIndex(0)), Is.True);
        }

        [Test]
        public void NestedParallelAbortPreservesReverseSemanticOrderAtEveryLevel()
        {
            var innerFirst = new ScriptedReferenceLeaf(NodeStatus.Running);
            var innerSecond = new ScriptedReferenceLeaf(NodeStatus.Running);
            var outerSecond = new ScriptedReferenceLeaf(NodeStatus.Running);
            var inner = P113TestProgram.Parallel(
                P113TestProgram.ParallelConfiguration(ReferenceParallelPolicy.RequireAllSuccess),
                P113TestProgram.Leaf("nested-inner-first", innerFirst),
                P113TestProgram.Leaf("nested-inner-second", innerSecond));
            var fixture = Create(
                ReferenceParallelPolicy.RequireAllSuccess,
                inner,
                P113TestProgram.Leaf("nested-outer-second", outerSecond));
            Assert.That(fixture.Machine.Update(Update(1)).Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(fixture.Machine.CaptureInspection().ActiveNodeCount, Is.EqualTo(5));

            fixture.Machine.Abort(Update(2), NodeAbortReason.TreeStopped, new RuntimeNodeIndex(0));
            Assert.That(fixture.Machine.CaptureInspection().ActiveNodeCount, Is.Zero);

            var abortOrder = fixture.Trace.Records
                .Where(item => item.UpdateId == 2 && item.Kind == ReferenceTraceEventKind.NodeAbortStarted)
                .Select(item => item.NodeIndex.Value)
                .ToArray();
            Assert.That(abortOrder, Is.EqualTo(new uint[] { 4, 3, 2, 1, 0 }));
        }

        [Test]
        public void TimeoutAbortsEverySuspendedParallelBranchBeforeCompletingDecorator()
        {
            var first = new ScriptedReferenceLeaf(NodeStatus.Running);
            var second = new ScriptedReferenceLeaf(NodeStatus.Running);
            var parallel = P113TestProgram.Parallel(
                P113TestProgram.ParallelConfiguration(ReferenceParallelPolicy.RequireAllSuccess),
                P113TestProgram.Leaf("timed-parallel-first", first),
                P113TestProgram.Leaf("timed-parallel-second", second));
            var timeout = P113TestProgram.Decorator(
                "aibt.core.timeout",
                P113TestProgram.TimedConfiguration(10, NodeStatus.Failure),
                8, 8, 8, NodeMemoryLifetime.Activation,
                parallel);
            var fixture = P113TestProgram.Create(timeout);
            Assert.That(fixture.Machine.Update(new ReferenceUpdateContext(1, new Revision(1), 0)).Progress,
                Is.EqualTo(ReferenceExecutionProgress.Waiting));

            var result = fixture.Machine.Update(new ReferenceUpdateContext(2, new Revision(2), 10));

            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Failure));
            var aborts = fixture.Trace.Records
                .Where(item => item.UpdateId == 2 && item.Kind == ReferenceTraceEventKind.NodeAbortStarted)
                .ToArray();
            Assert.That(aborts.Select(item => item.NodeIndex.Value), Is.EqualTo(new uint[] { 3, 2, 1 }));
            Assert.That(aborts.All(item => item.AbortReason == NodeAbortReason.Timeout && item.SourceNodeIndex == new RuntimeNodeIndex(0)), Is.True);
        }

        private static P113Fixture Create(ReferenceParallelPolicy policy, params P113TestNode[] children)
            => P113TestProgram.Create(P113TestProgram.Parallel(P113TestProgram.ParallelConfiguration(policy), children));

        private static ReferenceUpdateContext Update(ulong id)
            => new ReferenceUpdateContext(id, new Revision(id), checked((long)id * 10));
    }
}
