using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class DecoratorBehaviorTests
    {
        [TestCase("aibt.core.inverter", NodeStatus.Success, NodeStatus.Failure)]
        [TestCase("aibt.core.inverter", NodeStatus.Failure, NodeStatus.Success)]
        [TestCase("aibt.core.succeeder", NodeStatus.Success, NodeStatus.Success)]
        [TestCase("aibt.core.succeeder", NodeStatus.Failure, NodeStatus.Success)]
        [TestCase("aibt.core.failer", NodeStatus.Success, NodeStatus.Failure)]
        [TestCase("aibt.core.failer", NodeStatus.Failure, NodeStatus.Failure)]
        public void SimpleDecoratorMapsTerminalChildResult(string typeId, NodeStatus childStatus, NodeStatus expected)
        {
            var child = new ScriptedReferenceLeaf(childStatus);
            var fixture = P113TestProgram.Create(P113TestProgram.Decorator(
                typeId, null, 1, 0, 1, NodeMemoryLifetime.Activation, P113TestProgram.Leaf("simple", child)));

            var result = fixture.Machine.Update(Update(1, 0));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(result.RootResult, Is.EqualTo(expected));
            Assert.That(child.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Exit:" + childStatus }));
        }

        [Test]
        public void RepeaterFullyExitsEachIterationBeforeTheNextEnter()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Success, NodeStatus.Failure, NodeStatus.Success);
            var fixture = P113TestProgram.Create(P113TestProgram.Decorator(
                "aibt.core.repeater",
                P113TestProgram.RepeaterConfiguration(3, false),
                4, 4, 4, NodeMemoryLifetime.Activation,
                P113TestProgram.Leaf("repeat", child)));

            var result = fixture.Machine.Update(Update(1, 0));

            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(child.Calls, Is.EqualTo(new[]
            {
                "Enter", "Tick", "Exit:Success",
                "Enter", "Tick", "Exit:Failure",
                "Enter", "Tick", "Exit:Success",
            }));
            Assert.That(child.EnterGenerations, Is.EqualTo(new uint[] { 1, 2, 3 }));
            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)), Is.EqualTo(new byte[4]));
        }

        [Test]
        public void RepeaterStopOnFailureStopsAfterFirstFailedIteration()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Failure, NodeStatus.Success);
            var fixture = P113TestProgram.Create(P113TestProgram.Decorator(
                "aibt.core.repeater",
                P113TestProgram.RepeaterConfiguration(3, true),
                4, 4, 4, NodeMemoryLifetime.Activation,
                P113TestProgram.Leaf("stop", child)));

            var result = fixture.Machine.Update(Update(1, 0));

            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Failure));
            Assert.That(child.EnterGenerations, Is.EqualTo(new uint[] { 1 }));
        }

        [TestCase(NodeStatus.Failure)]
        [TestCase(NodeStatus.Success)]
        public void TimeoutAbortsRunningChildAtDeadlineBeforeRetick(NodeStatus configuredResult)
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Running, NodeStatus.Success);
            var fixture = P113TestProgram.Create(P113TestProgram.Decorator(
                "aibt.core.timeout",
                P113TestProgram.TimedConfiguration(10, configuredResult),
                8, 8, 8, NodeMemoryLifetime.Activation,
                P113TestProgram.Leaf("timeout", child)));

            var first = fixture.Machine.Update(Update(1, -5));
            var second = fixture.Machine.Update(Update(2, 5));

            Assert.That(first.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(second.RootResult, Is.EqualTo(configuredResult));
            Assert.That(child.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Abort:Timeout", "Exit:Aborted" }));
            var abort = fixture.Trace.Records.Single(item => item.Kind == ReferenceTraceEventKind.NodeAbortStarted);
            Assert.That(abort.AbortReason, Is.EqualTo(NodeAbortReason.Timeout));
            Assert.That(abort.SourceNodeIndex, Is.EqualTo(new RuntimeNodeIndex(0)));
        }

        [TestCase(ReferenceCooldownStartPolicy.OnEnter)]
        [TestCase(ReferenceCooldownStartPolicy.OnSuccessfulExit)]
        public void CooldownBlocksASecondActivationWithoutReenteringChild(ReferenceCooldownStartPolicy policy)
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Success, NodeStatus.Success);
            var cooldown = P113TestProgram.Decorator(
                "aibt.core.cooldown",
                P113TestProgram.TimedConfiguration(10, NodeStatus.Failure, (byte)policy),
                8, 8, 8, NodeMemoryLifetime.Instance,
                P113TestProgram.Leaf("cooldown", child));
            var fixture = P113TestProgram.Create(P113TestProgram.Decorator(
                "aibt.core.repeater",
                P113TestProgram.RepeaterConfiguration(2, false),
                4, 4, 4, NodeMemoryLifetime.Activation,
                cooldown));

            var result = fixture.Machine.Update(Update(1, 0));

            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(child.EnterGenerations, Is.EqualTo(new uint[] { 1 }));
            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(1)), Is.Not.EqualTo(new byte[8]));
        }

        [Test]
        public void SuccessfulExitPolicyDoesNotStartCooldownAfterFailure()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Failure, NodeStatus.Success);
            var cooldown = P113TestProgram.Decorator(
                "aibt.core.cooldown",
                P113TestProgram.TimedConfiguration(10, NodeStatus.Failure, 1),
                8, 8, 8, NodeMemoryLifetime.Instance,
                P113TestProgram.Leaf("cooldown-failure", child));
            var fixture = P113TestProgram.Create(P113TestProgram.Decorator(
                "aibt.core.repeater",
                P113TestProgram.RepeaterConfiguration(2, false),
                4, 4, 4, NodeMemoryLifetime.Activation,
                cooldown));

            var result = fixture.Machine.Update(Update(1, 0));

            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(child.EnterGenerations, Is.EqualTo(new uint[] { 1, 2 }));
        }

        [Test]
        public void OnEnterCooldownDoesNotStartWhenAbortWinsBeforeChildEnter()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Running);
            var fixture = P113TestProgram.Create(P113TestProgram.Decorator(
                "aibt.core.cooldown",
                P113TestProgram.TimedConfiguration(10, NodeStatus.Failure, 0),
                8, 8, 8, NodeMemoryLifetime.Instance,
                P113TestProgram.Leaf("pre-enter-abort", child)));
            Assert.That(fixture.Machine.BeginUpdate(Update(1, 0)).Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(fixture.Machine.AdvanceOneStep().Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(fixture.Machine.AdvanceOneStep().Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));

            fixture.Machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            while (fixture.Machine.HasOpenUpdate) fixture.Machine.AdvanceOneStep();

            Assert.That(child.Calls, Is.Empty);
            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)), Is.EqualTo(new byte[8]));
        }

        private static ReferenceUpdateContext Update(ulong id, long time)
            => new ReferenceUpdateContext(id, new Revision(id), time);
    }
}
