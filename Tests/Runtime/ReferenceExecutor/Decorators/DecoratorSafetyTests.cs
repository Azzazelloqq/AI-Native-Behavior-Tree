using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class DecoratorSafetyTests
    {
        [TestCase("aibt.core.inverter", 1u, 1u, NodeMemoryLifetime.Activation)]
        [TestCase("aibt.core.repeater", 4u, 2u, NodeMemoryLifetime.Activation)]
        [TestCase("aibt.core.timeout", 8u, 8u, NodeMemoryLifetime.Instance)]
        [TestCase("aibt.core.cooldown", 8u, 8u, NodeMemoryLifetime.Activation)]
        public void InvalidDescriptorFaultsBeforeEnter(
            string typeId,
            uint size,
            uint alignment,
            NodeMemoryLifetime lifetime)
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Success);
            byte[] configuration;
            uint configurationAlignment;
            if (typeId == "aibt.core.repeater")
            {
                configuration = P113TestProgram.RepeaterConfiguration(1, false);
                configurationAlignment = 4;
            }
            else if (typeId == "aibt.core.timeout" || typeId == "aibt.core.cooldown")
            {
                configuration = P113TestProgram.TimedConfiguration(1, NodeStatus.Failure);
                configurationAlignment = 8;
            }
            else
            {
                configuration = null;
                configurationAlignment = 1;
            }
            var fixture = P113TestProgram.Create(P113TestProgram.Decorator(
                typeId,
                configuration,
                configurationAlignment,
                size,
                alignment,
                lifetime,
                P113TestProgram.Leaf("invalid-decorator-storage", child)));

            var result = fixture.Machine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(ReferenceExecutionDiagnosticCodes.InvalidNodeConfiguration));
            Assert.That(child.Calls, Is.Empty);
            Assert.That(fixture.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
        }

        [Test]
        public void RestartClearsCooldownInstanceMemory()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Success);
            var fixture = P113TestProgram.Create(P113TestProgram.Decorator(
                "aibt.core.cooldown",
                P113TestProgram.TimedConfiguration(10, NodeStatus.Failure),
                8, 8, 8, NodeMemoryLifetime.Instance,
                P113TestProgram.Leaf("cooldown-restart", child)));

            fixture.Machine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)), Is.Not.EqualTo(new byte[8]));

            fixture.Machine.Restart();

            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)), Is.EqualTo(new byte[8]));
        }

        [TestCase("aibt.core.repeater", 1u, 4u, 4u, NodeMemoryLifetime.Activation)]
        [TestCase("aibt.core.timeout", 4u, 8u, 8u, NodeMemoryLifetime.Activation)]
        [TestCase("aibt.core.cooldown", 4u, 8u, 8u, NodeMemoryLifetime.Instance)]
        public void WrongConfigurationAlignmentFaultsBeforeEnter(
            string typeId,
            uint configurationAlignment,
            uint memorySize,
            uint memoryAlignment,
            NodeMemoryLifetime lifetime)
        {
            var configuration = typeId == "aibt.core.repeater"
                ? P113TestProgram.RepeaterConfiguration(1, false)
                : P113TestProgram.TimedConfiguration(1, NodeStatus.Failure);
            var child = new ScriptedReferenceLeaf(NodeStatus.Success);
            var fixture = P113TestProgram.Create(P113TestProgram.Decorator(
                typeId,
                configuration,
                configurationAlignment,
                memorySize,
                memoryAlignment,
                lifetime,
                P113TestProgram.Leaf("decorator-wrong-config-alignment", child)));

            var result = fixture.Machine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(fixture.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
        }
    }
}
