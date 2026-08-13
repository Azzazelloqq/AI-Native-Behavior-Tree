using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class ReactiveCompositeTraceTests
    {
        [Test]
        public void ReplacementTraceIsDeepestFirstAndCompletesOldExitBeforeCandidateEnter()
        {
            var guard = new ScriptedReferenceLeaf(NodeStatus.Success, NodeStatus.Success);
            var action = new ScriptedReferenceLeaf(NodeStatus.Running, NodeStatus.Running);
            var fixture = ReactiveCompositeTestProgram.Sequence(guard, action);
            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(2));

            var records = fixture.Trace.Records.Where(item => item.UpdateId == 2).ToArray();
            var abort = records.Single(item => item.Kind == ReferenceTraceEventKind.NodeAbortStarted);
            Assert.That(abort.NodeIndex, Is.EqualTo(new RuntimeNodeIndex(2)));
            Assert.That(abort.AbortReason, Is.EqualTo(NodeAbortReason.Explicit));
            Assert.That(abort.SourceNodeIndex, Is.EqualTo(new RuntimeNodeIndex(0)));
            var oldExit = System.Array.FindIndex(records, item =>
                item.Kind == ReferenceTraceEventKind.NodeExited && item.ExitReason == NodeExitReason.Aborted);
            var candidateEnter = System.Array.FindIndex(records, item =>
                item.Kind == ReferenceTraceEventKind.NodeEntered && item.NodeIndex == new RuntimeNodeIndex(1));
            Assert.That(oldExit, Is.LessThan(candidateEnter));
        }
    }
}
