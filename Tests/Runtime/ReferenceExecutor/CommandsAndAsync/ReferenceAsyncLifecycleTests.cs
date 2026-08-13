using System;
using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    public sealed class ReferenceAsyncLifecycleTests
    {
        [Test]
        public void Start_EmitsOnceAndReticksRemainRunningWithoutDuplicateCommand()
        {
            var trace = new RecordingReferenceTraceSink();
            var machine = AsyncProgram.Create(trace);

            var first = machine.Update(AsyncProgram.Update(1));
            var firstInspection = machine.CaptureInspection();
            var second = machine.Update(AsyncProgram.Update(2));

            Assert.That(first.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(first.Commands.Records.Count, Is.EqualTo(1));
            Assert.That(first.Commands.Records[0].Phase, Is.EqualTo(CommandPhase.Execute));
            Assert.That(first.Commands.Records[0].OperationId.ActivationGeneration, Is.EqualTo(1));
            Assert.That(firstInspection.ActiveNodeCount, Is.EqualTo(1));
            Assert.That(firstInspection.ActiveOperationCount, Is.EqualTo(1));
            Assert.That(second.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(second.Commands.Records, Is.Empty);
            Assert.That(trace.Records.Count(item => item.Kind == ReferenceTraceEventKind.CommandEmitted), Is.EqualTo(1));
        }

        [TestCase(CompletionOutcome.Succeeded, NodeStatus.Success)]
        [TestCase(CompletionOutcome.Failed, NodeStatus.Failure)]
        [TestCase(CompletionOutcome.Cancelled, NodeStatus.Failure)]
        public void MatchingCompletion_IsConsumedOnceAndMapsOutcome(
            CompletionOutcome outcome,
            NodeStatus expected)
        {
            var trace = new RecordingReferenceTraceSink();
            var machine = AsyncProgram.Create(trace);
            var start = machine.Update(AsyncProgram.Update(1));
            var operation = start.Commands.Records.Single().OperationId;

            var result = machine.Update(AsyncProgram.Update(
                2,
                Batch(Record(operation, outcome, 1, 1))));

            Assert.That(result.RootResult, Is.EqualTo(expected));
            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(trace.Records.Count(item => item.Kind == ReferenceTraceEventKind.CompletionConsumed), Is.EqualTo(1));
            Assert.That(result.Commands.Records, Is.Empty);
            Assert.That(machine.CaptureInspection().ActiveNodeCount, Is.Zero);
            Assert.That(machine.CaptureInspection().ActiveOperationCount, Is.Zero);
        }

        [Test]
        public void Abort_EmitsOneCancelAndLateCompletionCannotReactivateOldWork()
        {
            var machine = AsyncProgram.Create();
            var start = machine.Update(AsyncProgram.Update(1));
            var operation = start.Commands.Records.Single().OperationId;

            var abort = machine.Abort(
                AsyncProgram.Update(2),
                NodeAbortReason.Explicit,
                new RuntimeNodeIndex(0));
            Assert.That(abort.Commands.Records.Count, Is.EqualTo(1));
            Assert.That(abort.Commands.Records[0].Phase, Is.EqualTo(CommandPhase.Cancel));
            Assert.That(abort.Commands.Records[0].OperationId, Is.EqualTo(operation));

            Assert.That(machine.Restart().Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            var restarted = machine.Update(AsyncProgram.Update(
                3,
                Batch(Record(operation, CompletionOutcome.Succeeded, 1, 1))));
            Assert.That(restarted.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(restarted.RootResult, Is.Null);
            Assert.That(restarted.Commands.Records.Single().OperationId, Is.Not.EqualTo(operation));
            Assert.That(restarted.Diagnostics.Any(item => item.Code == CommandAsyncDiagnosticCodes.CancelledOperation), Is.True);
            Assert.That(restarted.Diagnostics.Single(item => item.Code == CommandAsyncDiagnosticCodes.CancelledOperation).Severity,
                Is.EqualTo(DiagnosticSeverity.Info));
        }

        [Test]
        public void Restart_PreservesOperationAndCommandSequences()
        {
            var machine = AsyncProgram.Create();
            var first = machine.Update(AsyncProgram.Update(1));
            var firstOperation = first.Commands.Records.Single().OperationId;
            machine.Abort(AsyncProgram.Update(2), NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            machine.Restart();

            var second = machine.Update(AsyncProgram.Update(3));
            var secondOperation = second.Commands.Records.Single().OperationId;

            Assert.That(secondOperation.Sequence, Is.GreaterThan(firstOperation.Sequence));
            Assert.That(second.Commands.Records.Single().Sequence, Is.EqualTo(3));
        }

        [Test]
        public void NormalizeDiagnostics_HaveContractSeveritiesAndDiscardTrace()
        {
            var trace = new RecordingReferenceTraceSink();
            var machine = AsyncProgram.Create(trace);
            var operation = machine.Update(AsyncProgram.Update(1)).Commands.Records.Single().OperationId;
            var duplicate = Batch(
                Record(operation, CompletionOutcome.Succeeded, 3, 4),
                Record(operation, CompletionOutcome.Failed, 3, 4));

            var result = machine.Update(AsyncProgram.Update(2, duplicate));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(CommandAsyncDiagnosticCodes.DuplicateCompletionOrderingKey));
            Assert.That(result.Diagnostics.Single().Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(trace.Records.Count(item => item.Kind == ReferenceTraceEventKind.CompletionDiscarded), Is.EqualTo(1));

            var unknown = new OperationId(new TreeInstanceId(41), new RuntimeNodeIndex(0), 1, 999);
            var warning = machine.Update(AsyncProgram.Update(
                3,
                Batch(Record(unknown, CompletionOutcome.Succeeded, 4, 1))));
            Assert.That(warning.Diagnostics.Single().Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        }

        [TestCase(0u, 1u, NodeMemoryLifetime.Activation)]
        [TestCase(8u, 8u, NodeMemoryLifetime.Activation)]
        [TestCase(17u, 1u, NodeMemoryLifetime.Activation)]
        [TestCase(16u, 4u, NodeMemoryLifetime.Activation)]
        [TestCase(16u, 8u, NodeMemoryLifetime.Instance)]
        public void InvalidAsyncStorage_FaultsBeforeNodeEntered(
            uint size,
            uint alignment,
            NodeMemoryLifetime lifetime)
        {
            var trace = new RecordingReferenceTraceSink();
            var machine = AsyncProgram.Create(trace, size, alignment, lifetime);

            var result = machine.Update(AsyncProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
            Assert.That(result.Commands.Records, Is.Empty);
        }

        [Test]
        public void FaultAfterPublishedStart_EmitsCompensatingCancelAndTombstonesOperation()
        {
            var handler = new StartThenThrowLeaf();
            var machine = AsyncProgram.CreateWithHandler(handler);
            var start = machine.Update(AsyncProgram.Update(1));
            var operation = start.Commands.Records.Single().OperationId;

            var fault = machine.Update(AsyncProgram.Update(2));

            Assert.That(fault.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(fault.Commands.Records.Count, Is.EqualTo(1));
            Assert.That(fault.Commands.Records[0].Phase, Is.EqualTo(CommandPhase.Cancel));
            Assert.That(fault.Commands.Records[0].OperationId, Is.EqualTo(operation));
            CollectionAssert.AreEqual(new byte[] { 9, 8 }, fault.Commands.GetPayload(fault.Commands.Records[0]).ToArray());
            machine.Restart();
            var late = machine.Update(AsyncProgram.Update(
                3,
                Batch(Record(operation, CompletionOutcome.Succeeded, 1, 1))));
            Assert.That(late.Diagnostics.Any(item => item.Code == CommandAsyncDiagnosticCodes.CancelledOperation), Is.True);
        }

        [Test]
        public void ReentrantReject_DoesNotDrainOuterStartCommand()
        {
            var handler = new StartThenReenterLeaf();
            var machine = AsyncProgram.CreateWithHandler(handler);
            handler.Machine = machine;

            var outer = machine.Update(AsyncProgram.Update(1));

            Assert.That(handler.Inner.HasValue, Is.True);
            Assert.That(handler.Inner.Value.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(handler.Inner.Value.Commands.Records, Is.Empty);
            Assert.That(outer.Commands.Records.Count, Is.EqualTo(1));
            Assert.That(outer.Commands.Records[0].Phase, Is.EqualTo(CommandPhase.Execute));
        }

        [Test]
        public void StepwiseDelivery_ReturnsStartExactlyOnTickStep()
        {
            var machine = AsyncProgram.Create();

            Assert.That(machine.BeginUpdate(AsyncProgram.Update(1)).Commands.Records, Is.Empty);
            Assert.That(machine.AdvanceOneStep().Commands.Records, Is.Empty);
            var tick = machine.AdvanceOneStep();

            Assert.That(tick.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(tick.Commands.Records.Count, Is.EqualTo(1));
            Assert.That(machine.AdvanceOneStep().Commands.Records, Is.Empty);
        }

        [Test]
        public void AbortWithMatchingCompletion_CancelsAndDiscardsWithoutConsumption()
        {
            var trace = new RecordingReferenceTraceSink();
            var machine = AsyncProgram.Create(trace);
            var operation = machine.Update(AsyncProgram.Update(1)).Commands.Records.Single().OperationId;

            var abort = machine.Abort(
                AsyncProgram.Update(2, Batch(Record(operation, CompletionOutcome.Succeeded, 1, 1))),
                NodeAbortReason.Explicit,
                new RuntimeNodeIndex(0));

            Assert.That(abort.Commands.Records.Single().Phase, Is.EqualTo(CommandPhase.Cancel));
            Assert.That(abort.Diagnostics.Any(item => item.Code == CommandAsyncDiagnosticCodes.CancelledOperation), Is.True);
            Assert.That(trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.CompletionConsumed), Is.False);
            Assert.That(trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.CompletionDiscarded), Is.True);
        }

        [Test]
        public void TerminalPendingAfterConsumedCompletion_IgnoresAbortAndEmitsNoCancel()
        {
            var machine = AsyncProgram.Create();
            var operation = machine.Update(AsyncProgram.Update(1)).Commands.Records.Single().OperationId;
            machine.BeginUpdate(AsyncProgram.Update(2, Batch(Record(operation, CompletionOutcome.Succeeded, 1, 1))));
            var tick = machine.AdvanceOneStep();
            Assert.That(tick.Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));

            var request = machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            var exit = machine.AdvanceOneStep();

            Assert.That(request.Commands.Records, Is.Empty);
            Assert.That(exit.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(exit.Commands.Records, Is.Empty);
        }

        [Test]
        public void Machine_SourceHighWaterAllowsGapsAndRejectsBackwardsInput()
        {
            var machine = AsyncProgram.Create();
            var operation = machine.Update(AsyncProgram.Update(1)).Commands.Records.Single().OperationId;
            var unknown = new OperationId(new TreeInstanceId(41), new RuntimeNodeIndex(0), 1, 999);
            var gap = machine.Update(AsyncProgram.Update(2, Batch(Record(unknown, CompletionOutcome.Succeeded, 7, 100))));
            var backwards = machine.Update(AsyncProgram.Update(3, Batch(Record(operation, CompletionOutcome.Succeeded, 7, 99))));

            Assert.That(gap.Diagnostics.Single().Code, Is.EqualTo(CommandAsyncDiagnosticCodes.UnknownOperation));
            Assert.That(backwards.Diagnostics.Single().Code, Is.EqualTo(CommandAsyncDiagnosticCodes.NonIncreasingSourceSequence));
            Assert.That(backwards.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
        }

        [Test]
        public void Machine_DiscardsActiveOperationFromPriorActivationAsStaleGeneration()
        {
            var handler = new StartThenTerminalLeaf();
            var machine = AsyncProgram.CreateWithHandler(handler);
            var first = machine.Update(AsyncProgram.Update(1));
            var oldOperation = first.Commands.Records.Single().OperationId;
            machine.Restart();
            machine.Update(AsyncProgram.Update(2));

            var stale = machine.Update(AsyncProgram.Update(
                3,
                Batch(Record(oldOperation, CompletionOutcome.Succeeded, 9, 1))));

            Assert.That(stale.Diagnostics.Any(item => item.Code == CommandAsyncDiagnosticCodes.StaleOperationGeneration), Is.True);
            Assert.That(stale.Diagnostics.Single(item => item.Code == CommandAsyncDiagnosticCodes.StaleOperationGeneration).Severity,
                Is.EqualTo(DiagnosticSeverity.Info));
        }

        [Test]
        public void AsyncMemory_UsesExactLittleEndianSequenceAndLeavesPaddingZero()
        {
            var memory = new byte[16];
            var services = new PatternOperationServices(0x0807060504030201UL);
            var context = new ReferenceNodeContext(
                Array.Empty<byte>(), 0, 0,
                memory, 0, memory.Length,
                AsyncProgram.Update(1),
                new TreeInstanceId(41),
                new RuntimeNodeIndex(0),
                1,
                services);
            var handler = new ReferenceAsyncActionHandler(AsyncProgram.Contract);

            Assert.That(handler.Tick(ref context), Is.EqualTo(NodeStatus.Running));

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, memory.Skip(8).ToArray());
            CollectionAssert.AreEqual(new byte[7], memory.Skip(1).Take(7).ToArray());
        }

        private static CompletionRecord Record(
            OperationId operationId,
            CompletionOutcome outcome,
            ulong sourceId,
            ulong sourceSequence)
        {
            return new CompletionRecord(
                operationId,
                outcome,
                default,
                0,
                0,
                sourceId,
                sourceSequence,
                new Revision(50));
        }

        private static CompletionBatch Batch(params CompletionRecord[] records)
            => new CompletionBatch(records, Array.Empty<byte>());

        private static class AsyncProgram
        {
            private static readonly CompiledHash Hash = new CompiledHash(new string('b', CompiledHash.HexLength));

            internal static ReferenceAsyncCommandContract Contract { get; } = new ReferenceAsyncCommandContract(
                new CommandType(StableHash.Fnv1A64("aibt.test.command.async-start"), 1),
                new CommandType(StableHash.Fnv1A64("aibt.test.command.async-cancel"), 1));

            internal static ReferenceExecutionMachine Create(
                RecordingReferenceTraceSink trace = null,
                uint memorySize = 16,
                uint memoryAlignment = 8,
                NodeMemoryLifetime memoryLifetime = NodeMemoryLifetime.Activation)
            {
                var program = CreateProgram(memorySize, memoryAlignment, memoryLifetime);
                return new ReferenceExecutionMachine(
                    program,
                    new TreeInstanceId(41),
                    ReferenceLeafRegistry.CreatePhase1Fixtures(),
                    trace);
            }

            internal static ReferenceExecutionMachine CreateWithHandler(IReferenceLeafHandler handler)
            {
                return new ReferenceExecutionMachine(
                    CreateProgram(16, 8, NodeMemoryLifetime.Activation),
                    new TreeInstanceId(41),
                    new ReferenceLeafRegistry(new[]
                    {
                        new ReferenceLeafBinding(StableHash.Fnv1A64(ReferenceAsyncActionHandler.TypeId), 1, handler),
                    }));
            }

            private static CompiledProgram CreateProgram(
                uint memorySize,
                uint memoryAlignment,
                NodeMemoryLifetime memoryLifetime)
            {
                var node = new CompiledNodeRecord(
                    StableHash.Fnv1A64(ReferenceAsyncActionHandler.TypeId),
                    1,
                    0,
                    2,
                    1,
                    0,
                    memorySize,
                    memoryAlignment,
                    memoryLifetime,
                    new CompiledRange(0, 0),
                    CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                    CompiledIndex.Invalid,
                    new CompiledRange(0, 0),
                    new CompiledRange(0, 0));
                var header = new CompiledProgramHeader(
                    1, 1, new CompiledCompilerVersion(1, 0, 0, 0),
                    Hash, Hash, Hash, 1, Hash,
                    0, 1, 0, 0, 0, 2, memorySize, memoryAlignment, 0, true);
                return new CompiledProgram(
                    header,
                    new[] { node },
                    Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<uint>(),
                    Array.Empty<CompiledBlackboardSlotRecord>(),
                    Array.Empty<CompiledObserverRecord>(), Array.Empty<uint>(),
                    new byte[] { 7, 8 }, Array.Empty<byte>(), Array.Empty<CompiledDebugMapEntry>());
            }

            internal static ReferenceUpdateContext Update(ulong id, CompletionBatch completions = null)
                => new ReferenceUpdateContext(id, new Revision(id + 100), checked((long)id * 10), completions);
        }

        private sealed class StartThenThrowLeaf : IReferenceLeafHandler
        {
            private int _ticks;
            private static readonly ReferenceAsyncCommandContract FaultContract = new ReferenceAsyncCommandContract(
                new CommandType(StableHash.Fnv1A64("aibt.test.command.fault-start"), 1),
                new CommandType(StableHash.Fnv1A64("aibt.test.command.fault-cancel"), 1),
                new byte[] { 9, 8 });
            public void Enter(ref ReferenceNodeContext context) { }
            public NodeStatus Tick(ref ReferenceNodeContext context)
            {
                if (_ticks++ == 0)
                {
                    context.TryStartOperation(FaultContract, ReadOnlySpan<byte>.Empty, out _);
                    return NodeStatus.Running;
                }

                throw new InvalidOperationException("fixture fault");
            }
            public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason) { }
            public void Exit(ref ReferenceNodeContext context, NodeExitReason reason) { }
        }

        private sealed class StartThenTerminalLeaf : IReferenceLeafHandler
        {
            private int _activation;
            public void Enter(ref ReferenceNodeContext context) => _activation++;
            public NodeStatus Tick(ref ReferenceNodeContext context)
            {
                if (_activation == 1)
                {
                    context.TryStartOperation(AsyncProgram.Contract, ReadOnlySpan<byte>.Empty, out _);
                    return NodeStatus.Success;
                }

                return NodeStatus.Running;
            }
            public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason) { }
            public void Exit(ref ReferenceNodeContext context, NodeExitReason reason) { }
        }

        private sealed class StartThenReenterLeaf : IReferenceLeafHandler
        {
            internal ReferenceExecutionMachine Machine { get; set; }
            internal ReferenceExecutionEnvelope? Inner { get; private set; }
            public void Enter(ref ReferenceNodeContext context) { }
            public NodeStatus Tick(ref ReferenceNodeContext context)
            {
                context.TryStartOperation(AsyncProgram.Contract, ReadOnlySpan<byte>.Empty, out _);
                Inner = Machine.Update(AsyncProgram.Update(999));
                return NodeStatus.Running;
            }
            public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason) { }
            public void Exit(ref ReferenceNodeContext context, NodeExitReason reason) { }
        }

        private sealed class PatternOperationServices : IReferenceNodeServices
        {
            private readonly ulong _sequence;
            internal PatternOperationServices(ulong sequence) => _sequence = sequence;
            public bool TryStart(RuntimeNodeIndex nodeIndex, uint activationGeneration, ReferenceAsyncCommandContract contract, ReadOnlySpan<byte> payload, out OperationId operationId)
            {
                operationId = new OperationId(new TreeInstanceId(41), nodeIndex, activationGeneration, _sequence);
                return true;
            }
            public bool TryConsume(RuntimeNodeIndex nodeIndex, uint activationGeneration, OperationId operationId, ReferenceCompletionExpectation expectation, out ReferenceCompletionView completion)
            {
                completion = default;
                return false;
            }
            public bool TryCancel(RuntimeNodeIndex nodeIndex, uint activationGeneration, OperationId operationId, ReferenceAsyncCommandContract contract, ReadOnlySpan<byte> payload) => true;
        }
    }
}
