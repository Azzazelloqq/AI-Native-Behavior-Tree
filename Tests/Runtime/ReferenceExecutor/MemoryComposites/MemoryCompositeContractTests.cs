using System;
using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class MemoryCompositeContractTests
    {
        [TestCase(8u, 4u, NodeMemoryLifetime.Activation)]
        [TestCase(4u, 2u, NodeMemoryLifetime.Activation)]
        [TestCase(4u, 4u, NodeMemoryLifetime.Instance)]
        [TestCase(0u, 1u, NodeMemoryLifetime.Activation)]
        public void InvalidDescriptorMatrixFaultsBeforeLifecycle(
            uint size,
            uint alignment,
            NodeMemoryLifetime lifetime)
        {
            var fixture = MemoryCompositeTestProgram.InvalidSequenceStorage(size, alignment, lifetime);

            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.Diagnostics.Single().Code,
                Is.EqualTo(ReferenceExecutionDiagnosticCodes.InvalidCompositeState));
            Assert.That(fixture.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
        }

        [Test]
        public void RegistryRejectsDuplicateTypeAndVersion()
        {
            var binding = new ReferenceMemoryCompositeBinding(
                17,
                1,
                new ReferenceMemorySequenceHandler());

            Assert.Throws<ArgumentException>(() => new ReferenceMemoryCompositeRegistry(new[]
            {
                binding,
                binding,
            }));
        }

        [Test]
        public void MissingAndWrongVersionBindingsFaultBeforeLifecycle()
        {
            var missing = MemoryCompositeTestProgram.WithCompositeRegistry(ReferenceMemoryCompositeRegistry.Empty);
            var wrongVersion = MemoryCompositeTestProgram.WithCompositeRegistry(
                ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                compositeVersion: 2);

            var missingResult = missing.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            var wrongResult = wrongVersion.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(missingResult.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(wrongResult.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(missing.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
            Assert.That(wrongVersion.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
        }

        [Test]
        public void LeafBindingCannotBypassChildPolicy()
        {
            var root = new ScriptedReferenceLeaf(NodeStatus.Success);
            var child = new ScriptedReferenceLeaf(NodeStatus.Success);
            var fixture = MemoryCompositeTestProgram.LeafBindingWithChild(root, child);

            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(root.Calls, Is.Empty);
            Assert.That(child.Calls, Is.Empty);
        }

        [Test]
        public void InvalidHandlerDecisionFaultsBeforeCursorOrFrameMutation()
        {
            var child = new ScriptedReferenceLeaf(NodeStatus.Success);
            var registry = new ReferenceMemoryCompositeRegistry(new[]
            {
                new ReferenceMemoryCompositeBinding(
                    StableHash.Fnv1A64("aibt.core.memory-sequence"),
                    1,
                    new InvalidDecisionHandler()),
            });
            var fixture = MemoryCompositeTestProgram.WithCompositeRegistry(registry, child);

            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.Diagnostics.Single().Code,
                Is.EqualTo(ReferenceExecutionDiagnosticCodes.InvalidCompositeState));
            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)),
                Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
        }

        [Test]
        public void InvalidEmptyResultFaultsWithoutTickExitOrRootResult()
        {
            var registry = new ReferenceMemoryCompositeRegistry(new[]
            {
                new ReferenceMemoryCompositeBinding(
                    StableHash.Fnv1A64("aibt.core.memory-sequence"),
                    1,
                    new InvalidEmptyHandler()),
            });
            var fixture = MemoryCompositeTestProgram.WithCompositeRegistry(registry);

            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.RootResult, Is.Null);
            Assert.That(result.Diagnostics.Single().Code,
                Is.EqualTo(ReferenceExecutionDiagnosticCodes.InvalidCompositeState));
            Assert.That(fixture.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeTicked), Is.False);
            Assert.That(fixture.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeExited), Is.False);
            Assert.That(fixture.Machine.CopyNodeMemory(new RuntimeNodeIndex(0)),
                Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
        }

        private sealed class InvalidDecisionHandler : IReferenceMemoryCompositeHandler
        {
            public NodeStatus EmptyResult => NodeStatus.Success;

            public ReferenceCompositeDecision Accept(NodeStatus childResult, uint childCursor, uint childCount)
                => ReferenceCompositeDecision.Terminal(NodeStatus.Running, childCursor + 1);
        }

        private sealed class InvalidEmptyHandler : IReferenceMemoryCompositeHandler
        {
            public NodeStatus EmptyResult => NodeStatus.Running;

            public ReferenceCompositeDecision Accept(NodeStatus childResult, uint childCursor, uint childCount)
                => ReferenceCompositeDecision.Continue(childCursor + 1);
        }
    }
}
