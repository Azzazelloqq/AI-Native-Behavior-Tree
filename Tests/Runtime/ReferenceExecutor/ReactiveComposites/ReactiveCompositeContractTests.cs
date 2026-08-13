using System;
using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class ReactiveCompositeContractTests
    {
        [Test]
        public void RegistryRejectsDuplicateAndDoesNotLeakIntoMemoryBuiltIns()
        {
            var binding = new ReferenceReactiveCompositeBinding(
                17,
                1,
                new ReferenceReactiveSequenceHandler(),
                ReferenceReactiveCompositeKind.Sequence);
            Assert.Throws<ArgumentException>(() => new ReferenceReactiveCompositeRegistry(new[]
            {
                binding,
                binding,
            }));
            Assert.That(ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns().TryGet(
                StableHash.Fnv1A64("aibt.core.reactive-sequence"),
                1,
                out _), Is.False);
        }

        [Test]
        public void UnboundAndWrongVersionReactiveNodesFaultBeforeLifecycle()
        {
            var unbound = ReactiveCompositeTestProgram.SequenceWithRegistry(ReferenceReactiveCompositeRegistry.Empty);
            var wrong = ReactiveCompositeTestProgram.SequenceWithRegistry(
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                version: 2);

            var first = unbound.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            var second = wrong.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(first.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(second.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(unbound.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
            Assert.That(wrong.Trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
        }

        [TestCase(8u, 4u, NodeMemoryLifetime.Activation)]
        [TestCase(4u, 2u, NodeMemoryLifetime.Activation)]
        [TestCase(4u, 4u, NodeMemoryLifetime.Instance)]
        [TestCase(0u, 1u, NodeMemoryLifetime.Activation)]
        public void InvalidDescriptorMatrixFaultsBeforeLifecycle(
            uint size,
            uint alignment,
            NodeMemoryLifetime lifetime)
        {
            var fixture = ReactiveCompositeTestProgram.InvalidSequenceStorage(size, alignment, lifetime);

            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.Diagnostics.Single().Code,
                Is.EqualTo(ReferenceExecutionDiagnosticCodes.InvalidCompositeState));
        }

        [Test]
        public void CompiledRecordRejectsMisalignedReactiveMemoryEnvelope()
        {
            Assert.Throws<ArgumentException>(() => new CompiledNodeRecord(
                StableHash.Fnv1A64("aibt.core.reactive-sequence"),
                1, 0, 0, 1,
                2, 4, 4, NodeMemoryLifetime.Activation,
                new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain,
                CompiledIndex.Invalid, new CompiledRange(0, 0), new CompiledRange(0, 0)));
        }

        [Test]
        public void InvalidEmptyAndAcceptDecisionsFaultWithoutTerminalExposure()
        {
            var emptyRegistry = Registry(new InvalidReactiveHandler(NodeStatus.Running, invalidAccept: false));
            var acceptRegistry = Registry(new InvalidReactiveHandler(NodeStatus.Success, invalidAccept: true));
            var empty = ReactiveCompositeTestProgram.SequenceWithRegistry(emptyRegistry);
            var accept = ReactiveCompositeTestProgram.SequenceWithRegistry(
                acceptRegistry,
                1,
                new ScriptedReferenceLeaf(NodeStatus.Success));

            var emptyResult = empty.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            var acceptResult = accept.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(emptyResult.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(acceptResult.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(emptyResult.RootResult, Is.Null);
            Assert.That(acceptResult.RootResult, Is.Null);
        }

        private static ReferenceReactiveCompositeRegistry Registry(IReferenceReactiveCompositeHandler handler)
        {
            return new ReferenceReactiveCompositeRegistry(new[]
            {
                new ReferenceReactiveCompositeBinding(
                    StableHash.Fnv1A64("aibt.core.reactive-sequence"), 1, handler,
                    ReferenceReactiveCompositeKind.Sequence),
            });
        }

        private sealed class InvalidReactiveHandler : IReferenceReactiveCompositeHandler
        {
            private readonly bool _invalidAccept;
            internal InvalidReactiveHandler(NodeStatus emptyResult, bool invalidAccept)
            {
                EmptyResult = emptyResult;
                _invalidAccept = invalidAccept;
            }

            public NodeStatus EmptyResult { get; }

            public ReferenceCompositeDecision Accept(NodeStatus childResult, uint childCursor, uint childCount)
                => _invalidAccept
                    ? ReferenceCompositeDecision.Terminal(NodeStatus.Running, childCursor + 1)
                    : ReferenceCompositeDecision.Continue(childCursor + 1);
        }
    }
}
