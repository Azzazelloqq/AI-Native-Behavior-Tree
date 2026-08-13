using System;
using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class ReferenceObserverMachineTests
    {
        [Test]
        public void ChangedWrite_IsVisibleImmediately_ButObserverRunsAfterTickAndBeforeUpdateCompletes()
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success);

            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(fixture.Writer.EvaluatorCallsDuringTick, Is.Zero);
            Assert.That(fixture.Evaluator.Calls, Is.EqualTo(1));
            Assert.That(fixture.Writer.ReadBack, Is.EqualTo(BlackboardValue.FromBool(true)));
            var kinds = fixture.Trace.Records.Select(record => record.Kind).ToArray();
            Assert.That(Array.IndexOf(kinds, ReferenceTraceEventKind.BlackboardChanged), Is.LessThan(Array.IndexOf(kinds, ReferenceTraceEventKind.ObserverEvaluated)));
            Assert.That(Array.IndexOf(kinds, ReferenceTraceEventKind.ObserverQueued), Is.LessThan(Array.IndexOf(kinds, ReferenceTraceEventKind.ObserverEvaluated)));
            Assert.That(Array.IndexOf(kinds, ReferenceTraceEventKind.ObserverEvaluated), Is.LessThan(Array.LastIndexOf(kinds, ReferenceTraceEventKind.UpdateCompleted)));
            var changed = fixture.Trace.Records.Single(record => record.Kind == ReferenceTraceEventKind.BlackboardChanged);
            Assert.That(changed.StableBlackboardKeyId, Is.EqualTo(101));
            Assert.That(changed.OldBlackboardVersion, Is.Zero);
            Assert.That(changed.NewBlackboardVersion, Is.EqualTo(1));
        }

        [Test]
        public void EqualWrite_DoesNotQueueOrEvaluateObserver()
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Failure, writeValue: false);

            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(fixture.Evaluator.Calls, Is.Zero);
            Assert.That(fixture.Trace.Records.Any(record => record.Kind == ReferenceTraceEventKind.BlackboardChanged), Is.False);
            Assert.That(fixture.Trace.Records.Any(record => record.Kind == ReferenceTraceEventKind.ObserverQueued), Is.False);
        }

        [Test]
        public void NormalConditionResultSeedsObserver_AndLowerPriorityTransitionAbortsActiveSibling()
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success);

            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(fixture.Writer.AbortReason, Is.EqualTo(NodeAbortReason.ObserverLowerPriority));
            var abort = fixture.Trace.Records.Single(record => record.Kind == ReferenceTraceEventKind.NodeAbortStarted);
            Assert.That(abort.NodeIndex, Is.EqualTo(new RuntimeNodeIndex(2)));
            Assert.That(abort.AbortReason, Is.EqualTo(NodeAbortReason.ObserverLowerPriority));
            Assert.That(abort.SourceNodeIndex, Is.EqualTo(new RuntimeNodeIndex(1)));
        }

        [TestCase(CompiledObserverMode.Self, NodeAbortReason.ObserverSelf, true)]
        [TestCase(CompiledObserverMode.Both, NodeAbortReason.ObserverLowerPriority, false)]
        public void ObserverModes_ApplyExactScopedAbort(
            CompiledObserverMode mode,
            NodeAbortReason expectedReason,
            bool sequenceOwner)
        {
            var fixture = ObserverMachineFixture.Create(
                sequenceOwner ? NodeStatus.Success : NodeStatus.Failure,
                sequenceOwner ? NodeStatus.Failure : NodeStatus.Success,
                mode: mode,
                sequenceOwner: sequenceOwner);

            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(fixture.Writer.AbortReason, Is.EqualTo(expectedReason));
        }

        [Test]
        public void NonMatchingTransition_DoesNotAbort()
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success, mode: CompiledObserverMode.Self);
            Assert.That(fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1)).Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(fixture.Writer.AbortReason, Is.Null);
            var kinds = fixture.Trace.Records.Select(record => record.Kind).ToArray();
            Assert.That(Array.IndexOf(kinds, ReferenceTraceEventKind.ObserverEvaluated), Is.LessThan(Array.LastIndexOf(kinds, ReferenceTraceEventKind.UpdateCompleted)));
        }

        [Test]
        public void MultipleWritesInOneTick_DeduplicateObserverQueue()
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success, doubleWrite: true);
            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            Assert.That(fixture.Trace.Records.Count(record => record.Kind == ReferenceTraceEventKind.BlackboardChanged), Is.EqualTo(2));
            Assert.That(fixture.Trace.Records.Count(record => record.Kind == ReferenceTraceEventKind.ObserverQueued), Is.EqualTo(1));
            Assert.That(fixture.Evaluator.Calls, Is.EqualTo(1));
        }

        [Test]
        public void MultipleObserversQueueAndEvaluateOnceInRuntimeNodeOrder()
        {
            var fixture = ObserverMachineFixture.CreateMultipleObservers();
            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            var queued = fixture.Trace.Records
                .Where(record => record.Kind == ReferenceTraceEventKind.ObserverQueued)
                .Select(record => record.NodeIndex.Value)
                .ToArray();
            var evaluated = fixture.Trace.Records
                .Where(record => record.Kind == ReferenceTraceEventKind.ObserverEvaluated)
                .Select(record => record.NodeIndex.Value)
                .ToArray();
            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting),
                (result.Diagnostics.Count == 0 ? string.Empty : result.Diagnostics[0].Message)
                + "; queued=" + string.Join(",", queued)
                + "; evaluated=" + string.Join(",", evaluated));
            Assert.That(queued, Is.EqualTo(new uint[] { 1, 2 }),
                "queued=" + string.Join(",", queued));
            Assert.That(evaluated, Is.EqualTo(new uint[] { 1, 2 }),
                "evaluated=" + string.Join(",", evaluated));
        }

        [Test]
        public void FirstEvaluationWithoutNormalSeed_DoesNotTriggerTransition()
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success, observerIsActive: false);

            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(fixture.Evaluator.Calls, Is.EqualTo(1));
            Assert.That(fixture.Writer.AbortReason, Is.Null);
        }

        [Test]
        public void EvaluatorReadFailureFaultsBeforeObserverEvaluated()
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success, invalidEvaluatorReadOrdinal: true);

            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.Diagnostics[0].Code, Is.EqualTo(BlackboardStorageDiagnosticCodes.UndeclaredAccess));
            Assert.That(fixture.Trace.Records.Any(record => record.Kind == ReferenceTraceEventKind.ObserverEvaluated), Is.False);
        }

        [TestCase(NodeStatus.Running)]
        [TestCase((NodeStatus)99)]
        public void InvalidEvaluatorResultFaults(NodeStatus status)
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, status);
            Assert.That(fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1)).Progress,
                Is.EqualTo(ReferenceExecutionProgress.Faulted));
        }

        [Test]
        public void MissingEvaluatorBindingFaults()
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success, omitEvaluator: true);
            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.Diagnostics[0].Code, Is.EqualTo(ReferenceExecutionDiagnosticCodes.MissingHandler));
        }

        [Test]
        public void ResetIsIdleOnlyAndChangedResetPublishesOnNextUpdate()
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success, observerIsActive: false);
            Assert.That(fixture.Machine.BeginUpdate(ReferenceExecutionTestProgram.Update(1)).Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(fixture.Machine.ResetBlackboard().Success, Is.False);
            while (fixture.Machine.HasOpenUpdate)
                fixture.Machine.AdvanceOneStep();
            var reset = fixture.Machine.ResetBlackboard();
            Assert.That(reset.Success, Is.True);
            Assert.That(reset.Changed, Is.True);

            fixture.Trace.Records.Clear();
            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(2));
            CollectionAssert.IsSubsetOf(
                new[] { ReferenceTraceEventKind.BlackboardChanged, ReferenceTraceEventKind.ObserverQueued, ReferenceTraceEventKind.ObserverEvaluated },
                fixture.Trace.Records.Select(record => record.Kind));
        }

        [Test]
        public void ReentrantResetFromTickIsRejectedWithoutUndoingTheWrite()
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success, resetDuringTick: true);
            fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            Assert.That(fixture.Writer.ReentrantResetSuccess, Is.False);
            Assert.That(fixture.Writer.ReadBack, Is.EqualTo(BlackboardValue.FromBool(true)));
        }

        [Test]
        public void BlackboardConstructionFailureMakesUpdateResetAndRestartStructured()
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success, mismatchedRegistryHash: true);
            Assert.That(fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1)).Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(fixture.Machine.ResetBlackboard().Success, Is.False);
            Assert.That(fixture.Machine.Restart().Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
        }

        [Test]
        public void LowerPriorityModeOnReactiveSequenceFaultsBeforeNodeEnter()
        {
            var fixture = ObserverMachineFixture.Create(
                NodeStatus.Success,
                NodeStatus.Failure,
                mode: CompiledObserverMode.Both,
                sequenceOwner: true);
            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(fixture.Trace.Records.Any(record => record.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
        }

        [Test]
        public void NoChangeResetIsNoOp()
        {
            var fixture = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success);
            var reset = fixture.Machine.ResetBlackboard();
            Assert.That(reset.Success, Is.True);
            Assert.That(reset.Changed, Is.False);
            Assert.That(reset.Changes, Is.Empty);
        }

        [Test]
        public void TerminalExitWriteDrainsObserverBeforeCompletingRetainedResult()
        {
            var fixture = ObserverMachineFixture.Create(
                NodeStatus.Failure,
                NodeStatus.Success,
                observerIsActive: false,
                writerStatus: NodeStatus.Success,
                writeOnExit: true);
            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Success));
            var kinds = fixture.Trace.Records.Select(record => record.Kind).ToArray();
            var writerExit = fixture.Trace.Records.FindIndex(record => record.Kind == ReferenceTraceEventKind.NodeExited && record.NodeIndex == new RuntimeNodeIndex(2));
            Assert.That(writerExit, Is.LessThan(Array.IndexOf(kinds, ReferenceTraceEventKind.ObserverEvaluated)));
            Assert.That(Array.IndexOf(kinds, ReferenceTraceEventKind.ObserverEvaluated), Is.LessThan(Array.LastIndexOf(kinds, ReferenceTraceEventKind.UpdateCompleted)));
        }

        [Test]
        public void TerminalPendingObserverResetSuppressesOldParentAcceptance()
        {
            var fixture = ObserverMachineFixture.Create(
                NodeStatus.Failure,
                NodeStatus.Success,
                writerStatus: NodeStatus.Success);
            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            Assert.That(result.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(fixture.Writer.AbortReason, Is.Null);
            Assert.That(fixture.Writer.ExitReason, Is.EqualTo(NodeExitReason.Success));
            var records = fixture.Trace.Records;
            Assert.That(records.Count(record => record.Kind == ReferenceTraceEventKind.NodeEntered && record.NodeIndex == new RuntimeNodeIndex(1)), Is.EqualTo(2));
            Assert.That(
                records.FindLastIndex(record => record.Kind == ReferenceTraceEventKind.NodeEntered && record.NodeIndex == new RuntimeNodeIndex(1)),
                Is.GreaterThan(records.FindIndex(record => record.Kind == ReferenceTraceEventKind.NodeExited && record.NodeIndex == new RuntimeNodeIndex(2))));
        }

        [Test]
        public void ObserverRestoresAndAbortsTargetInRetainedParallelBranch()
        {
            var fixture = ObserverMachineFixture.CreateParallelRetainedOwner();
            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting),
                result.Diagnostics.Count == 0 ? string.Empty : result.Diagnostics[0].Message);
            Assert.That(fixture.RetainedLower.Calls, Does.Contain("Abort:ObserverLowerPriority"));
            var abort = fixture.Trace.Records.Single(record => record.Kind == ReferenceTraceEventKind.NodeAbortStarted);
            Assert.That(abort.SourceNodeIndex, Is.EqualTo(new RuntimeNodeIndex(2)));
        }

        [Test]
        public void NonMatchingRetainedObserverDoesNotSwitchOrAbortBranch()
        {
            var fixture = ObserverMachineFixture.CreateParallelRetainedOwner(
                evaluatorStatus: NodeStatus.Success,
                initialCondition: NodeStatus.Failure,
                observerMode: CompiledObserverMode.Self,
                writerStatus: NodeStatus.Success);
            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(fixture.RetainedLower.Calls.Any(call => call.StartsWith("Abort:", StringComparison.Ordinal)), Is.False);
            Assert.That(fixture.Writer.AbortReason, Is.Null);
            Assert.That(fixture.Writer.ExitReason, Is.EqualTo(NodeExitReason.Success));
        }

        [Test]
        public void ObserverRestoresNestedOuterAndInnerRetainedBranchChain()
        {
            var fixture = ObserverMachineFixture.CreateNestedParallelRetainedOwner();
            var result = fixture.Machine.Update(ReferenceExecutionTestProgram.Update(1));
            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting),
                result.Diagnostics.Count == 0 ? string.Empty : result.Diagnostics[0].Message);
            Assert.That(fixture.RetainedLower.Calls, Does.Contain("Abort:ObserverLowerPriority"));
        }
    }

    internal sealed class ObserverMachineFixture
    {
        private static readonly CompiledHash Hash = new CompiledHash(new string('e', CompiledHash.HexLength));
        private const ulong ObserverType = 7001;
        private const ulong WriterType = 7002;

        private ObserverMachineFixture(ReferenceExecutionMachine machine, RecordingReferenceTraceSink trace, BlackboardWriterLeaf writer, BoolObserverEvaluator evaluator, ScriptedReferenceLeaf retainedLower = null)
        {
            Machine = machine;
            Trace = trace;
            Writer = writer;
            Evaluator = evaluator;
            RetainedLower = retainedLower;
        }

        internal ReferenceExecutionMachine Machine { get; }
        internal RecordingReferenceTraceSink Trace { get; }
        internal BlackboardWriterLeaf Writer { get; }
        internal BoolObserverEvaluator Evaluator { get; }
        internal ScriptedReferenceLeaf RetainedLower { get; }

        internal static ObserverMachineFixture Create(
            NodeStatus initialCondition,
            NodeStatus evaluatorResult,
            bool writeValue = true,
            bool observerIsActive = true,
            bool invalidEvaluatorReadOrdinal = false,
            bool omitEvaluator = false,
            CompiledObserverMode mode = CompiledObserverMode.LowerPriority,
            bool sequenceOwner = false,
            bool doubleWrite = false,
            bool resetDuringTick = false,
            bool mismatchedRegistryHash = false,
            NodeStatus writerStatus = NodeStatus.Running,
            bool writeOnExit = false)
        {
            var condition = new ScriptedReferenceLeaf(initialCondition, NodeStatus.Success);
            var evaluator = new BoolObserverEvaluator(evaluatorResult, invalidEvaluatorReadOrdinal);
            var writer = new BlackboardWriterLeaf(writeValue, evaluator, doubleWrite)
            {
                Status = writerStatus,
                WriteOnExit = writeOnExit,
            };
            var selectorType = StableHash.Fnv1A64(sequenceOwner ? "aibt.core.reactive-sequence" : "aibt.core.reactive-selector");
            var nodes = new[]
            {
                Node(selectorType, new CompiledRange(0, 2), 0, 4, new CompiledRange(0, 0), new CompiledRange(0, 0)),
                Node(ObserverType, new CompiledRange(0, 0), 0, 0, new CompiledRange(0, 1), new CompiledRange(0, 0)),
                Node(WriterType, new CompiledRange(0, 0), 0, 0, new CompiledRange(1, 1), new CompiledRange(0, 1)),
            };
            var slot = new CompiledBlackboardSlotRecord(
                101, BuiltInBlackboardTypes.Bool.TypeId, BuiltInBlackboardTypes.Bool.Version, 0,
                BlackboardScope.Tree, 0, 1, 1, 0,
                CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write | CompiledBlackboardAccessFlags.Observed);
            var header = new CompiledProgramHeader(
                1, 1, new CompiledCompilerVersion(1, 0, 0, 0), Hash, Hash, Hash, 1, Hash,
                0, 3, 2, 1, 0, 0, 4, 4, 0, true);
            var program = new CompiledProgram(
                header, nodes, new uint[] { 1, 2 }, new uint[] { 0, 0 }, new uint[] { 0 }, new[] { slot },
                new[] { new CompiledObserverRecord(observerIsActive ? 1u : 2u, 0,
                    observerIsActive ? mode : CompiledObserverMode.Self,
                    new CompiledRange(0, 1)) },
                new uint[] { 0 }, Array.Empty<byte>(), new byte[] { 0 }, Array.Empty<CompiledDebugMapEntry>());
            var trace = new RecordingReferenceTraceSink();
            var evaluatorBindings = omitEvaluator
                ? Array.Empty<ReferenceObserverConditionBinding>()
                : new[] { new ReferenceObserverConditionBinding(observerIsActive ? ObserverType : WriterType, 1, evaluator) };
            var machine = new ReferenceExecutionMachine(
                program, new TreeInstanceId(91),
                new ReferenceLeafRegistry(new[]
                {
                    new ReferenceLeafBinding(ObserverType, 1, condition),
                    new ReferenceLeafBinding(WriterType, 1, writer),
                }), trace, ReferenceMemoryCompositeRegistry.Empty,
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                observerRegistry: new ReferenceObserverConditionRegistry(evaluatorBindings),
                expectedRegisteredBlackboardRegistryHash: mismatchedRegistryHash
                    ? new CompiledHash(new string('f', CompiledHash.HexLength))
                    : default);
            writer.ResetMachine = resetDuringTick ? machine : null;
            return new ObserverMachineFixture(machine, trace, writer, evaluator);
        }

        internal static ObserverMachineFixture CreateParallelRetainedOwner(
            NodeStatus evaluatorStatus = NodeStatus.Success,
            NodeStatus initialCondition = NodeStatus.Failure,
            CompiledObserverMode observerMode = CompiledObserverMode.LowerPriority,
            NodeStatus writerStatus = NodeStatus.Running)
        {
            var parallelType = StableHash.Fnv1A64("aibt.core.parallel");
            var selectorType = StableHash.Fnv1A64("aibt.core.reactive-selector");
            var retainedType = 7003UL;
            var condition = new ScriptedReferenceLeaf(initialCondition, NodeStatus.Success);
            var retained = new ScriptedReferenceLeaf(NodeStatus.Running);
            var evaluator = new BoolObserverEvaluator(evaluatorStatus);
            var writer = new BlackboardWriterLeaf(true, evaluator) { Status = writerStatus };
            var nodes = new[]
            {
                new CompiledNodeRecord(parallelType, 1, 0, 16, 4, 0, 8, 4, NodeMemoryLifetime.Activation,
                    new CompiledRange(0, 2), CompiledNodeFlags.BurstDomain, CompiledIndex.Invalid,
                    new CompiledRange(0, 0), new CompiledRange(0, 0)),
                Node(selectorType, new CompiledRange(2, 2), 8, 4, new CompiledRange(0, 0), new CompiledRange(0, 0)),
                Node(ObserverType, new CompiledRange(0, 0), 0, 0, new CompiledRange(0, 1), new CompiledRange(0, 0)),
                Node(retainedType, new CompiledRange(0, 0), 0, 0, new CompiledRange(0, 0), new CompiledRange(0, 0)),
                Node(WriterType, new CompiledRange(0, 0), 0, 0, new CompiledRange(1, 1), new CompiledRange(0, 1)),
            };
            var slot = new CompiledBlackboardSlotRecord(
                101, BuiltInBlackboardTypes.Bool.TypeId, BuiltInBlackboardTypes.Bool.Version, 0,
                BlackboardScope.Tree, 0, 1, 1, 0,
                CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write | CompiledBlackboardAccessFlags.Observed);
            var config = P113TestProgram.ParallelConfiguration(ReferenceParallelPolicy.RequireAllSuccess);
            var header = new CompiledProgramHeader(
                1, 1, new CompiledCompilerVersion(1, 0, 0, 0), Hash, Hash, Hash, 1, Hash,
                0, 5, 4, 1, 0, 16, 12, 4, 0, true);
            var program = new CompiledProgram(
                header, nodes, new uint[] { 1, 4, 2, 3 }, new uint[] { 0, 0 }, new uint[] { 0 }, new[] { slot },
                new[] { new CompiledObserverRecord(2, 1, observerMode, new CompiledRange(0, 1)) },
                new uint[] { 0 }, config, new byte[] { 0 }, Array.Empty<CompiledDebugMapEntry>());
            var trace = new RecordingReferenceTraceSink();
            var machine = new ReferenceExecutionMachine(
                program, new TreeInstanceId(92),
                new ReferenceLeafRegistry(new[]
                {
                    new ReferenceLeafBinding(ObserverType, 1, condition),
                    new ReferenceLeafBinding(retainedType, 1, retained),
                    new ReferenceLeafBinding(WriterType, 1, writer),
                }), trace, ReferenceMemoryCompositeRegistry.Empty,
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                parallelRegistry: ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                observerRegistry: new ReferenceObserverConditionRegistry(new[]
                {
                    new ReferenceObserverConditionBinding(ObserverType, 1, evaluator),
                }));
            return new ObserverMachineFixture(machine, trace, writer, evaluator, retained);
        }

        internal static ObserverMachineFixture CreateNestedParallelRetainedOwner()
        {
            var parallelType = StableHash.Fnv1A64("aibt.core.parallel");
            var selectorType = StableHash.Fnv1A64("aibt.core.reactive-selector");
            var retainedType = 7013UL;
            var otherType = 7014UL;
            var condition = new ScriptedReferenceLeaf(NodeStatus.Failure, NodeStatus.Success);
            var retained = new ScriptedReferenceLeaf(NodeStatus.Running);
            var other = new ScriptedReferenceLeaf(NodeStatus.Running);
            var evaluator = new BoolObserverEvaluator(NodeStatus.Success);
            var writer = new BlackboardWriterLeaf(true, evaluator);
            var nodes = new[]
            {
                new CompiledNodeRecord(parallelType, 1, 0, 16, 4, 0, 8, 4, NodeMemoryLifetime.Activation,
                    new CompiledRange(0, 2), CompiledNodeFlags.BurstDomain, CompiledIndex.Invalid,
                    new CompiledRange(0, 0), new CompiledRange(0, 0)),
                new CompiledNodeRecord(parallelType, 1, 16, 16, 4, 8, 8, 4, NodeMemoryLifetime.Activation,
                    new CompiledRange(2, 2), CompiledNodeFlags.BurstDomain, CompiledIndex.Invalid,
                    new CompiledRange(0, 0), new CompiledRange(0, 0)),
                Node(selectorType, new CompiledRange(4, 2), 16, 4, new CompiledRange(0, 0), new CompiledRange(0, 0)),
                Node(ObserverType, new CompiledRange(0, 0), 0, 0, new CompiledRange(0, 1), new CompiledRange(0, 0)),
                Node(retainedType, new CompiledRange(0, 0), 0, 0, new CompiledRange(0, 0), new CompiledRange(0, 0)),
                Node(otherType, new CompiledRange(0, 0), 0, 0, new CompiledRange(0, 0), new CompiledRange(0, 0)),
                Node(WriterType, new CompiledRange(0, 0), 0, 0, new CompiledRange(1, 1), new CompiledRange(0, 1)),
            };
            var slot = new CompiledBlackboardSlotRecord(
                101, BuiltInBlackboardTypes.Bool.TypeId, BuiltInBlackboardTypes.Bool.Version, 0,
                BlackboardScope.Tree, 0, 1, 1, 0,
                CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write | CompiledBlackboardAccessFlags.Observed);
            var config = new byte[32];
            var parallelConfig = P113TestProgram.ParallelConfiguration(ReferenceParallelPolicy.RequireAllSuccess);
            Array.Copy(parallelConfig, 0, config, 0, 16);
            Array.Copy(parallelConfig, 0, config, 16, 16);
            var header = new CompiledProgramHeader(
                1, 1, new CompiledCompilerVersion(1, 0, 0, 0), Hash, Hash, Hash, 1, Hash,
                0, 7, 6, 1, 0, 32, 20, 4, 0, true);
            var program = new CompiledProgram(
                header, nodes, new uint[] { 1, 6, 2, 5, 3, 4 }, new uint[] { 0, 0 }, new uint[] { 0 }, new[] { slot },
                new[] { new CompiledObserverRecord(3, 2, CompiledObserverMode.LowerPriority, new CompiledRange(0, 1)) },
                new uint[] { 0 }, config, new byte[] { 0 }, Array.Empty<CompiledDebugMapEntry>());
            var trace = new RecordingReferenceTraceSink();
            var machine = new ReferenceExecutionMachine(
                program, new TreeInstanceId(93),
                new ReferenceLeafRegistry(new[]
                {
                    new ReferenceLeafBinding(ObserverType, 1, condition),
                    new ReferenceLeafBinding(retainedType, 1, retained),
                    new ReferenceLeafBinding(otherType, 1, other),
                    new ReferenceLeafBinding(WriterType, 1, writer),
                }), trace, ReferenceMemoryCompositeRegistry.Empty,
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                parallelRegistry: ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                observerRegistry: new ReferenceObserverConditionRegistry(new[]
                {
                    new ReferenceObserverConditionBinding(ObserverType, 1, evaluator),
                }));
            return new ObserverMachineFixture(machine, trace, writer, evaluator, retained);
        }

        internal static ObserverMachineFixture CreateMultipleObservers()
        {
            var writerType = 7022UL;
            var firstType = 7021UL;
            var secondType = 7020UL;
            var first = new BoolObserverEvaluator(NodeStatus.Success, skipRead: true);
            var second = new BoolObserverEvaluator(NodeStatus.Success, skipRead: true);
            var firstLeaf = new ScriptedReferenceLeaf(NodeStatus.Success);
            var secondLeaf = new ScriptedReferenceLeaf(NodeStatus.Success);
            var writer = new BlackboardWriterLeaf(true, first, doubleWrite: true);
            writer.SkipReadBack = true;
            var selectorType = StableHash.Fnv1A64("aibt.core.reactive-sequence");
            var nodes = new[]
            {
                Node(selectorType, new CompiledRange(0, 3), 0, 4, new CompiledRange(0, 0), new CompiledRange(0, 0)),
                Node(firstType, new CompiledRange(0, 0), 0, 0, new CompiledRange(0, 1), new CompiledRange(0, 0)),
                Node(secondType, new CompiledRange(0, 0), 0, 0, new CompiledRange(1, 1), new CompiledRange(0, 0)),
                Node(writerType, new CompiledRange(0, 0), 0, 0, new CompiledRange(2, 1), new CompiledRange(0, 1)),
            };
            var slot = new CompiledBlackboardSlotRecord(
                101, BuiltInBlackboardTypes.Bool.TypeId, BuiltInBlackboardTypes.Bool.Version, 0,
                BlackboardScope.Tree, 0, 1, 1, 0,
                CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write | CompiledBlackboardAccessFlags.Observed);
            var header = new CompiledProgramHeader(
                1, 1, new CompiledCompilerVersion(1, 0, 0, 0), Hash, Hash, Hash, 1, Hash,
                0, 4, 3, 1, 0, 0, 4, 4, 0, true);
            var program = new CompiledProgram(
                header, nodes, new uint[] { 1, 2, 3 }, new uint[] { 0, 0, 0 }, new uint[] { 0 }, new[] { slot },
                new[]
                {
                    new CompiledObserverRecord(2, 0, CompiledObserverMode.Self, new CompiledRange(0, 1)),
                    new CompiledObserverRecord(1, 0, CompiledObserverMode.Self, new CompiledRange(1, 1)),
                },
                new uint[] { 0, 0 }, Array.Empty<byte>(), new byte[] { 0 }, Array.Empty<CompiledDebugMapEntry>());
            var trace = new RecordingReferenceTraceSink();
            var machine = new ReferenceExecutionMachine(
                program, new TreeInstanceId(94),
                new ReferenceLeafRegistry(new[]
                {
                    new ReferenceLeafBinding(firstType, 1, firstLeaf),
                    new ReferenceLeafBinding(secondType, 1, secondLeaf),
                    new ReferenceLeafBinding(writerType, 1, writer),
                }), trace, ReferenceMemoryCompositeRegistry.Empty,
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                observerRegistry: new ReferenceObserverConditionRegistry(new[]
                {
                    new ReferenceObserverConditionBinding(firstType, 1, first),
                    new ReferenceObserverConditionBinding(secondType, 1, second),
                }));
            return new ObserverMachineFixture(machine, trace, writer, first);
        }

        private static CompiledNodeRecord Node(ulong type, CompiledRange children, uint memoryOffset, uint memorySize, CompiledRange reads, CompiledRange writes)
            => new CompiledNodeRecord(
                type, 1, 0, 0, 1, memoryOffset, memorySize, memorySize == 0 ? 1u : 4u,
                NodeMemoryLifetime.Activation, children,
                CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                CompiledIndex.Invalid, reads, writes);
    }

    internal sealed class BlackboardWriterLeaf : IReferenceLeafHandler
    {
        private readonly bool _value;
        private readonly BoolObserverEvaluator _evaluator;
        private readonly bool _doubleWrite;
        internal BlackboardWriterLeaf(bool value, BoolObserverEvaluator evaluator, bool doubleWrite = false)
        { _value = value; _evaluator = evaluator; _doubleWrite = doubleWrite; }
        internal NodeStatus Status { get; set; } = NodeStatus.Running;
        internal int EvaluatorCallsDuringTick { get; private set; }
        internal BlackboardValue ReadBack { get; private set; }
        internal NodeAbortReason? AbortReason { get; private set; }
        internal NodeExitReason? ExitReason { get; private set; }
        internal bool WriteOnExit { get; set; }
        internal bool SkipReadBack { get; set; }
        internal ReferenceExecutionMachine ResetMachine { get; set; }
        internal bool? ReentrantResetSuccess { get; private set; }
        public void Enter(ref ReferenceNodeContext context) { }
        public NodeStatus Tick(ref ReferenceNodeContext context)
        {
            var before = _evaluator.Calls;
            if (!WriteOnExit)
            {
                context.TryWriteBlackboard(0, BlackboardValue.FromBool(_value));
                if (_doubleWrite) context.TryWriteBlackboard(0, BlackboardValue.FromBool(!_value));
            }
            if (ResetMachine != null) ReentrantResetSuccess = ResetMachine.ResetBlackboard().Success;
            if (!SkipReadBack)
            {
                context.TryReadBlackboard(0, out var value);
                ReadBack = value;
            }
            EvaluatorCallsDuringTick += _evaluator.Calls - before;
            return Status;
        }
        public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason) => AbortReason = reason;
        public void Exit(ref ReferenceNodeContext context, NodeExitReason reason)
        {
            ExitReason = reason;
            if (WriteOnExit) context.TryWriteBlackboard(0, BlackboardValue.FromBool(_value));
        }
    }

    internal sealed class BoolObserverEvaluator : IReferenceObserverConditionEvaluator
    {
        private readonly NodeStatus _result;
        private readonly bool _invalidRead;
        private readonly bool _skipRead;
        internal BoolObserverEvaluator(NodeStatus result, bool invalidRead = false, bool skipRead = false)
        { _result = result; _invalidRead = invalidRead; _skipRead = skipRead; }
        internal int Calls { get; private set; }
        public NodeStatus Evaluate(ref ReferenceObserverConditionContext context)
        {
            Calls++;
            if (!_skipRead) context.TryRead(_invalidRead ? 1u : 0u, out _);
            return _result;
        }
    }
}
