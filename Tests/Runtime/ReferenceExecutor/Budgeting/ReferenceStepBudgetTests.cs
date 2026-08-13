using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class ReferenceStepBudgetTests
    {
        [Test]
        public void BudgetContractDistinguishesUnlimitedFromEveryLimitedValue()
        {
            Assert.That(ReferenceStepBudget.Unlimited.IsUnlimited, Is.True);
            Assert.That(ReferenceStepBudget.Unlimited.StepLimit, Is.Zero);
            Assert.That(ReferenceStepBudget.Limited(0).IsUnlimited, Is.False);
            Assert.That(ReferenceStepBudget.Limited(ulong.MaxValue).StepLimit, Is.EqualTo(ulong.MaxValue));
            Assert.That(ReferenceStepBudget.Unlimited, Is.Not.EqualTo(ReferenceStepBudget.Limited(0)));
            Assert.That(default(ReferenceStepBudget), Is.EqualTo(ReferenceStepBudget.Limited(0)));
        }

        [Test]
        public void ZeroBudgetStartsUpdateWithoutExecutingCallbacks_AndZeroResumeIsIdempotent()
        {
            var leaf = new ScriptedReferenceLeaf(NodeStatus.Success);
            var trace = new RecordingReferenceTraceSink();
            var machine = ReferenceExecutionTestProgram.Create(leaf, trace);

            var first = machine.Update(
                ReferenceExecutionTestProgram.Update(1),
                ReferenceStepBudget.Limited(0));
            var zeroResume = machine.Resume(ReferenceStepBudget.Limited(0));
            var inspection = machine.CaptureInspection();

            Assert.That(first.Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(first.SegmentSteps, Is.Zero);
            Assert.That(first.CumulativeSteps, Is.Zero);
            Assert.That(zeroResume.Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(zeroResume.SegmentSteps, Is.Zero);
            Assert.That(zeroResume.CumulativeSteps, Is.Zero);
            Assert.That(inspection.ActiveNodeCount, Is.Zero);
            Assert.That(inspection.ActiveOperationCount, Is.Zero);
            Assert.That(inspection.Blackboard.Entries, Is.Empty);
            Assert.That(leaf.Calls, Is.Empty);
            Assert.That(trace.Records.Count(item => item.Kind == ReferenceTraceEventKind.BudgetYielded), Is.EqualTo(1));
            Assert.That(trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.ExecutionResumed), Is.False);
            Assert.That(trace.Records.Select(item => item.Kind), Is.EqualTo(new[]
            {
                ReferenceTraceEventKind.UpdateStarted,
                ReferenceTraceEventKind.BudgetYielded,
            }));
        }

        [Test]
        public void OneStepSegmentsPreserveLifecycleAndReportSegmentAndCumulativeMetrics()
        {
            var leaf = new ScriptedReferenceLeaf(NodeStatus.Success);
            var trace = new RecordingReferenceTraceSink();
            var machine = ReferenceExecutionTestProgram.Create(leaf, trace);

            var entered = machine.Update(
                ReferenceExecutionTestProgram.Update(1),
                ReferenceStepBudget.Limited(1));
            var ticked = machine.Resume(ReferenceStepBudget.Limited(1));
            var completed = machine.Resume(ReferenceStepBudget.Unlimited);

            Assert.That(entered.Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(entered.SegmentSteps, Is.EqualTo(1));
            Assert.That(entered.CumulativeSteps, Is.EqualTo(1));
            Assert.That(ticked.Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(ticked.SegmentSteps, Is.EqualTo(1));
            Assert.That(ticked.CumulativeSteps, Is.EqualTo(2));
            Assert.That(completed.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(completed.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(completed.SegmentSteps, Is.EqualTo(1));
            Assert.That(completed.CumulativeSteps, Is.EqualTo(3));
            Assert.That(completed.Steps, Is.EqualTo(completed.SegmentSteps));
            Assert.That(leaf.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Exit:Success" }));
            Assert.That(trace.Records.Select(item => item.Kind), Is.EqualTo(new[]
            {
                ReferenceTraceEventKind.UpdateStarted,
                ReferenceTraceEventKind.NodeEntered,
                ReferenceTraceEventKind.BudgetYielded,
                ReferenceTraceEventKind.ExecutionResumed,
                ReferenceTraceEventKind.NodeTicked,
                ReferenceTraceEventKind.BudgetYielded,
                ReferenceTraceEventKind.ExecutionResumed,
                ReferenceTraceEventKind.NodeExited,
                ReferenceTraceEventKind.UpdateCompleted,
            }));
        }

        [Test]
        public void FailedCallbackConsumesNoStepAndDoesNotYield()
        {
            var leaf = new ScriptedReferenceLeaf(NodeStatus.Success) { ThrowOn = "Enter" };
            var trace = new RecordingReferenceTraceSink();
            var machine = ReferenceExecutionTestProgram.Create(leaf, trace);

            var result = machine.Update(
                ReferenceExecutionTestProgram.Update(1),
                ReferenceStepBudget.Limited(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.SegmentSteps, Is.Zero);
            Assert.That(result.CumulativeSteps, Is.Zero);
            Assert.That(trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.BudgetYielded), Is.False);
        }

        [Test]
        public void FailingResumedCallbackAddsZeroSegmentAndCumulativeSteps()
        {
            var leaf = new ScriptedReferenceLeaf(NodeStatus.Success) { ThrowOn = "Tick" };
            var trace = new RecordingReferenceTraceSink();
            var machine = ReferenceExecutionTestProgram.Create(leaf, trace);
            var entered = machine.Update(
                ReferenceExecutionTestProgram.Update(1),
                ReferenceStepBudget.Limited(1));

            var faulted = machine.Resume(ReferenceStepBudget.Limited(1));

            Assert.That(entered.CumulativeSteps, Is.EqualTo(1));
            Assert.That(faulted.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(faulted.SegmentSteps, Is.Zero);
            Assert.That(faulted.CumulativeSteps, Is.EqualTo(1));
            Assert.That(trace.Records.Count(item => item.Kind == ReferenceTraceEventKind.ExecutionResumed), Is.EqualTo(1));
            Assert.That(trace.Records.Count(item => item.Kind == ReferenceTraceEventKind.BudgetYielded), Is.EqualTo(1));
        }

        [Test]
        public void SuspendedUpdateRejectsReplacementAndManualAdvance_ThenResumesFrozenInput()
        {
            var leaf = new ScriptedReferenceLeaf(NodeStatus.Success);
            var machine = ReferenceExecutionTestProgram.Create(leaf);

            machine.Update(
                new ReferenceUpdateContext(1, new Revision(101), 123),
                ReferenceStepBudget.Limited(1));
            var replacement = machine.Update(
                new ReferenceUpdateContext(2, new Revision(102), 999),
                ReferenceStepBudget.Unlimited);
            var manualAdvance = machine.AdvanceOneStep();
            var beginReplacement = machine.BeginUpdate(ReferenceExecutionTestProgram.Update(3));
            var externalAbort = machine.Abort(
                ReferenceExecutionTestProgram.Update(4),
                NodeAbortReason.Explicit,
                new RuntimeNodeIndex(0));
            var reset = machine.ResetBlackboard();
            var restart = machine.Restart();
            var completed = machine.Resume(ReferenceStepBudget.Unlimited);

            Assert.That(replacement.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(manualAdvance.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(beginReplacement.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(externalAbort.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(reset.Success, Is.False);
            Assert.That(restart.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(completed.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(leaf.TimesAtTick, Is.EqualTo(new long[] { 123 }));
        }

        [Test]
        public void ZeroBudgetPreEnterAbortHasNoLifecycleCallbacks()
        {
            var leaf = new ScriptedReferenceLeaf(NodeStatus.Running);
            var trace = new RecordingReferenceTraceSink();
            var machine = ReferenceExecutionTestProgram.Create(leaf, trace);
            machine.Update(
                ReferenceExecutionTestProgram.Update(1),
                ReferenceStepBudget.Limited(0));

            machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            var completed = machine.Resume(ReferenceStepBudget.Unlimited);

            Assert.That(completed.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(completed.CumulativeSteps, Is.Zero);
            Assert.That(leaf.Calls, Is.Empty);
            Assert.That(trace.Records.Select(item => item.Kind), Is.EqualTo(new[]
            {
                ReferenceTraceEventKind.UpdateStarted,
                ReferenceTraceEventKind.BudgetYielded,
                ReferenceTraceEventKind.UpdateCompleted,
            }));
        }

        [Test]
        public void PartitionedCompositeMatchesUnlimitedSemantics()
        {
            var unlimitedFirst = new ScriptedReferenceLeaf(NodeStatus.Success);
            var unlimitedSecond = new ScriptedReferenceLeaf(NodeStatus.Success);
            var unlimited = MemoryCompositeTestProgram.Sequence(unlimitedFirst, unlimitedSecond);
            var unlimitedResult = unlimited.Machine.Update(
                ReferenceExecutionTestProgram.Update(1),
                ReferenceStepBudget.Unlimited);

            var budgetedFirst = new ScriptedReferenceLeaf(NodeStatus.Success);
            var budgetedSecond = new ScriptedReferenceLeaf(NodeStatus.Success);
            var budgeted = MemoryCompositeTestProgram.Sequence(budgetedFirst, budgetedSecond);
            var segments = new List<ulong>();
            var budgetedResult = budgeted.Machine.Update(
                ReferenceExecutionTestProgram.Update(1),
                ReferenceStepBudget.Limited(1));
            segments.Add(budgetedResult.SegmentSteps);
            while (budgetedResult.Progress == ReferenceExecutionProgress.Suspended)
            {
                budgetedResult = budgeted.Machine.Resume(ReferenceStepBudget.Limited(1));
                segments.Add(budgetedResult.SegmentSteps);
            }

            Assert.That(budgetedResult.Progress, Is.EqualTo(unlimitedResult.Progress));
            Assert.That(budgetedResult.RootResult, Is.EqualTo(unlimitedResult.RootResult));
            Assert.That(budgetedResult.CumulativeSteps, Is.EqualTo(unlimitedResult.CumulativeSteps));
            ulong segmentTotal = 0;
            for (var index = 0; index < segments.Count; index++)
                segmentTotal = checked(segmentTotal + segments[index]);
            Assert.That(segmentTotal, Is.EqualTo(unlimitedResult.CumulativeSteps));
            Assert.That(budgetedFirst.Calls, Is.EqualTo(unlimitedFirst.Calls));
            Assert.That(budgetedSecond.Calls, Is.EqualTo(unlimitedSecond.Calls));
            Assert.That(
                SemanticTrace(budgeted.Trace),
                Is.EqualTo(SemanticTrace(unlimited.Trace)));
        }

        [Test]
        public void ReactiveAndParallelPartitionTablesMatchUnlimitedSemantics()
        {
            var partitions = new[]
            {
                new ulong[] { 1 },
                new ulong[] { 2, 3 },
                new ulong[] { 4, 1, 2 },
            };

            for (var partitionIndex = 0; partitionIndex < partitions.Length; partitionIndex++)
            {
                AssertFixtureEquivalent(
                    () => ReactiveCompositeTestProgram.Sequence(
                        new ScriptedReferenceLeaf(NodeStatus.Success),
                        new ScriptedReferenceLeaf(NodeStatus.Success)),
                    partitions[partitionIndex]);

                AssertFixtureEquivalent(
                    () => P113TestProgram.Create(P113TestProgram.Parallel(
                        P113TestProgram.ParallelConfiguration(ReferenceParallelPolicy.RequireAllSuccess),
                        P113TestProgram.Leaf("budget.parallel.first", new ScriptedReferenceLeaf(NodeStatus.Success)),
                        P113TestProgram.Leaf("budget.parallel.second", new ScriptedReferenceLeaf(NodeStatus.Success)))),
                    partitions[partitionIndex]);
            }
        }

        [Test]
        public void FixedBudgetMatrixProducesEquivalentCompositeResult()
        {
            for (ulong limit = 1; limit <= 16; limit++)
            {
                AssertFixtureEquivalent(
                    () => MemoryCompositeTestProgram.Sequence(
                        new ScriptedReferenceLeaf(NodeStatus.Success),
                        new ScriptedReferenceLeaf(NodeStatus.Success),
                        new ScriptedReferenceLeaf(NodeStatus.Success)),
                    new[] { limit });
            }
        }

        [Test]
        public void RepeaterPartitionsMatchUnlimitedSemantics()
        {
            AssertFixtureEquivalent(
                () => P113TestProgram.Create(P113TestProgram.Decorator(
                    "aibt.core.repeater",
                    P113TestProgram.RepeaterConfiguration(3, false),
                    4,
                    4,
                    4,
                    NodeMemoryLifetime.Activation,
                    P113TestProgram.Leaf(
                        "budget.repeater",
                        new ScriptedReferenceLeaf(
                            NodeStatus.Success,
                            NodeStatus.Failure,
                            NodeStatus.Success)))),
                new ulong[] { 1, 2, 3 });
        }

        [Test]
        public void ObserverReevaluationIsAnAtomicBudgetStepAndMatchesUnlimited()
        {
            var unlimited = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success);
            var unlimitedResult = unlimited.Machine.Update(
                ReferenceExecutionTestProgram.Update(1),
                ReferenceStepBudget.Unlimited);

            var budgeted = ObserverMachineFixture.Create(NodeStatus.Failure, NodeStatus.Success);
            var budgetedResult = RunPartitioned(
                budgeted.Machine,
                ReferenceExecutionTestProgram.Update(1),
                new ulong[] { 1 });

            Assert.That(budgetedResult.Progress, Is.EqualTo(unlimitedResult.Progress));
            Assert.That(budgetedResult.RootResult, Is.EqualTo(unlimitedResult.RootResult));
            Assert.That(budgetedResult.CumulativeSteps, Is.EqualTo(unlimitedResult.CumulativeSteps));
            Assert.That(budgeted.Evaluator.Calls, Is.EqualTo(1));
            Assert.That(SemanticTrace(budgeted.Trace), Is.EqualTo(SemanticTrace(unlimited.Trace)));
        }

        [Test]
        public void RequestAbortDuringBudgetSuspensionResumesWithoutTickOrCallbackSplit()
        {
            var leaf = new ScriptedReferenceLeaf(NodeStatus.Running);
            var trace = new RecordingReferenceTraceSink();
            var machine = ReferenceExecutionTestProgram.Create(leaf, trace);

            machine.Update(
                ReferenceExecutionTestProgram.Update(1),
                ReferenceStepBudget.Limited(1));
            var requested = machine.RequestAbort(NodeAbortReason.Explicit, new RuntimeNodeIndex(0));
            var aborted = machine.Resume(ReferenceStepBudget.Limited(1));
            var exited = machine.Resume(ReferenceStepBudget.Unlimited);

            Assert.That(requested.SegmentSteps, Is.Zero);
            Assert.That(aborted.Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(aborted.SegmentSteps, Is.EqualTo(1));
            Assert.That(exited.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(exited.CumulativeSteps, Is.EqualTo(3));
            Assert.That(leaf.Calls, Is.EqualTo(new[]
            {
                "Enter",
                "Abort:Explicit",
                "Exit:Aborted",
            }));
        }

        [Test]
        public void ZeroBudgetContextualAbortDefersCallbacks_AndResumeDeliversCancelExactlyOnce()
        {
            var trace = new RecordingReferenceTraceSink();
            var machine = CreateAsyncMachine(trace);
            var start = machine.Update(ReferenceExecutionTestProgram.Update(1));
            var operation = start.Commands.Records.Single().OperationId;
            trace.Records.Clear();

            var deferred = machine.Abort(
                ReferenceExecutionTestProgram.Update(2),
                NodeAbortReason.Explicit,
                new RuntimeNodeIndex(0),
                ReferenceStepBudget.Limited(0));
            var aborted = machine.Resume(ReferenceStepBudget.Limited(1));
            var completed = machine.Resume(ReferenceStepBudget.Unlimited);

            Assert.That(deferred.Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(deferred.SegmentSteps, Is.Zero);
            Assert.That(deferred.Commands.Records, Is.Empty);
            Assert.That(aborted.Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            Assert.That(aborted.SegmentSteps, Is.EqualTo(1));
            Assert.That(aborted.Commands.Records, Has.Count.EqualTo(1));
            Assert.That(aborted.Commands.Records[0].Phase, Is.EqualTo(CommandPhase.Cancel));
            Assert.That(aborted.Commands.Records[0].OperationId, Is.EqualTo(operation));
            Assert.That(completed.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(completed.Commands.Records, Is.Empty);
            Assert.That(completed.CumulativeSteps, Is.EqualTo(2));
            Assert.That(trace.Records.Select(item => item.Kind), Is.EqualTo(new[]
            {
                ReferenceTraceEventKind.UpdateStarted,
                ReferenceTraceEventKind.BudgetYielded,
                ReferenceTraceEventKind.ExecutionResumed,
                ReferenceTraceEventKind.CommandEmitted,
                ReferenceTraceEventKind.NodeAbortStarted,
                ReferenceTraceEventKind.BudgetYielded,
                ReferenceTraceEventKind.ExecutionResumed,
                ReferenceTraceEventKind.NodeExited,
                ReferenceTraceEventKind.UpdateCompleted,
            }));
        }

        [Test]
        public void AsyncStartCommandIsDeliveredExactlyOnceFromTheSegmentThatExecutesTick()
        {
            var trace = new RecordingReferenceTraceSink();
            var machine = CreateAsyncMachine(trace);

            var enter = machine.Update(
                ReferenceExecutionTestProgram.Update(1),
                ReferenceStepBudget.Limited(1));
            var tick = machine.Resume(ReferenceStepBudget.Limited(1));
            var invalidResume = machine.Resume(ReferenceStepBudget.Unlimited);

            Assert.That(enter.Commands.Records, Is.Empty);
            Assert.That(tick.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(tick.Commands.Records, Has.Count.EqualTo(1));
            Assert.That(invalidResume.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(invalidResume.Commands.Records, Is.Empty);
            Assert.That(trace.Records.Count(item => item.Kind == ReferenceTraceEventKind.CommandEmitted), Is.EqualTo(1));
        }

        [Test]
        public void MatchingAsyncCompletionPartitionsMatchUnlimitedSemantics()
        {
            var unlimitedTrace = new RecordingReferenceTraceSink();
            var unlimited = CreateAsyncMachine(unlimitedTrace);
            var unlimitedOperation = unlimited.Update(ReferenceExecutionTestProgram.Update(1))
                .Commands.Records.Single().OperationId;
            unlimitedTrace.Records.Clear();
            var unlimitedResult = unlimited.Update(
                AsyncCompletionUpdate(2, unlimitedOperation),
                ReferenceStepBudget.Unlimited);

            var budgetedTrace = new RecordingReferenceTraceSink();
            var budgeted = CreateAsyncMachine(budgetedTrace);
            var budgetedOperation = budgeted.Update(ReferenceExecutionTestProgram.Update(1))
                .Commands.Records.Single().OperationId;
            budgetedTrace.Records.Clear();
            var budgetedResult = RunPartitioned(
                budgeted,
                AsyncCompletionUpdate(2, budgetedOperation),
                new ulong[] { 1, 2 });

            Assert.That(budgetedResult.Progress, Is.EqualTo(unlimitedResult.Progress));
            Assert.That(budgetedResult.RootResult, Is.EqualTo(unlimitedResult.RootResult));
            Assert.That(budgetedResult.CumulativeSteps, Is.EqualTo(unlimitedResult.CumulativeSteps));
            Assert.That(SemanticTrace(budgetedTrace), Is.EqualTo(SemanticTrace(unlimitedTrace)));
            Assert.That(budgetedTrace.Records.Count(item => item.Kind == ReferenceTraceEventKind.CompletionConsumed), Is.EqualTo(1));
        }

        [Test]
        public void BudgetTraceUsesFrozenUpdateIdentityAndStrictSequence()
        {
            var trace = new RecordingReferenceTraceSink();
            var machine = ReferenceExecutionTestProgram.Create(new ScriptedReferenceLeaf(NodeStatus.Success), trace);
            var update = new ReferenceUpdateContext(7, new Revision(77), -123);

            machine.Update(update, ReferenceStepBudget.Limited(1));
            machine.Resume(ReferenceStepBudget.Unlimited);

            var budgetRecords = trace.Records
                .Where(item => item.Kind == ReferenceTraceEventKind.BudgetYielded
                    || item.Kind == ReferenceTraceEventKind.ExecutionResumed)
                .ToArray();
            Assert.That(budgetRecords, Has.Length.EqualTo(2));
            Assert.That(budgetRecords.All(item => item.UpdateId == 7), Is.True);
            Assert.That(budgetRecords.All(item => item.SnapshotRevision == new Revision(77)), Is.True);
            Assert.That(budgetRecords.All(item => item.TreeInstanceId == new TreeInstanceId(41)), Is.True);
            Assert.That(budgetRecords[1].Sequence, Is.GreaterThan(budgetRecords[0].Sequence));
        }

        [Test]
        public void ResumeRequiresBudgetSuspension()
        {
            var machine = ReferenceExecutionTestProgram.Create(new ScriptedReferenceLeaf(NodeStatus.Success));
            Assert.That(machine.Resume(ReferenceStepBudget.Unlimited).Progress,
                Is.EqualTo(ReferenceExecutionProgress.Rejected));
        }

        private static void AssertFixtureEquivalent(
            Func<MemoryCompositeFixture> create,
            ulong[] partition)
        {
            var unlimited = create();
            var unlimitedResult = unlimited.Machine.Update(
                ReferenceExecutionTestProgram.Update(1),
                ReferenceStepBudget.Unlimited);
            var budgeted = create();
            var budgetedResult = RunPartitioned(
                budgeted.Machine,
                ReferenceExecutionTestProgram.Update(1),
                partition);

            Assert.That(budgetedResult.Progress, Is.EqualTo(unlimitedResult.Progress));
            Assert.That(budgetedResult.RootResult, Is.EqualTo(unlimitedResult.RootResult));
            Assert.That(budgetedResult.CumulativeSteps, Is.EqualTo(unlimitedResult.CumulativeSteps));
            Assert.That(SemanticTrace(budgeted.Trace), Is.EqualTo(SemanticTrace(unlimited.Trace)));
        }

        private static void AssertFixtureEquivalent(
            Func<P113Fixture> create,
            ulong[] partition)
        {
            var unlimited = create();
            var unlimitedResult = unlimited.Machine.Update(
                ReferenceExecutionTestProgram.Update(1),
                ReferenceStepBudget.Unlimited);
            var budgeted = create();
            var budgetedResult = RunPartitioned(
                budgeted.Machine,
                ReferenceExecutionTestProgram.Update(1),
                partition);

            Assert.That(budgetedResult.Progress, Is.EqualTo(unlimitedResult.Progress));
            Assert.That(budgetedResult.RootResult, Is.EqualTo(unlimitedResult.RootResult));
            Assert.That(budgetedResult.CumulativeSteps, Is.EqualTo(unlimitedResult.CumulativeSteps));
            Assert.That(SemanticTrace(budgeted.Trace), Is.EqualTo(SemanticTrace(unlimited.Trace)));
        }

        private static ReferenceExecutionEnvelope RunPartitioned(
            ReferenceExecutionMachine machine,
            ReferenceUpdateContext update,
            ulong[] partition)
        {
            var segmentIndex = 0;
            var result = machine.Update(update, ReferenceStepBudget.Limited(partition[segmentIndex++]));
            while (result.Progress == ReferenceExecutionProgress.Suspended)
            {
                var stepLimit = partition[segmentIndex++ % partition.Length];
                result = machine.Resume(ReferenceStepBudget.Limited(stepLimit));
            }
            return result;
        }

        private static ReferenceExecutionMachine CreateAsyncMachine(RecordingReferenceTraceSink trace)
        {
            const uint memorySize = 16;
            var hash = new CompiledHash(new string('f', CompiledHash.HexLength));
            var typeId = StableHash.Fnv1A64(ReferenceAsyncActionHandler.TypeId);
            var node = new CompiledNodeRecord(
                typeId, 1, 0, 2, 1, 0, memorySize, 8,
                NodeMemoryLifetime.Activation,
                new CompiledRange(0, 0),
                CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                CompiledIndex.Invalid,
                new CompiledRange(0, 0),
                new CompiledRange(0, 0));
            var header = new CompiledProgramHeader(
                1, 1, new CompiledCompilerVersion(1, 0, 0, 0),
                hash, hash, hash, 1, hash,
                0, 1, 0, 0, 0, 2, memorySize, 8, 0, true);
            var program = new CompiledProgram(
                header,
                new[] { node },
                Array.Empty<uint>(),
                Array.Empty<uint>(),
                Array.Empty<uint>(),
                Array.Empty<CompiledBlackboardSlotRecord>(),
                Array.Empty<CompiledObserverRecord>(),
                Array.Empty<uint>(),
                new byte[] { 7, 8 },
                Array.Empty<byte>(),
                Array.Empty<CompiledDebugMapEntry>());
            var contract = new ReferenceAsyncCommandContract(
                new CommandType(StableHash.Fnv1A64("aibt.test.budget.async-start"), 1),
                new CommandType(StableHash.Fnv1A64("aibt.test.budget.async-cancel"), 1));
            return new ReferenceExecutionMachine(
                program,
                new TreeInstanceId(141),
                new ReferenceLeafRegistry(new[]
                {
                    new ReferenceLeafBinding(typeId, 1, new ReferenceAsyncActionHandler(contract)),
                }),
                trace);
        }

        private static ReferenceUpdateContext AsyncCompletionUpdate(ulong updateId, OperationId operationId)
        {
            var completion = new CompletionRecord(
                operationId,
                CompletionOutcome.Succeeded,
                default,
                0,
                0,
                1,
                1,
                new Revision(updateId + 100));
            return new ReferenceUpdateContext(
                updateId,
                new Revision(updateId + 100),
                checked((long)updateId * 10),
                new CompletionBatch(new[] { completion }, Array.Empty<byte>()));
        }

        private static ReferenceTraceEventKind[] SemanticTrace(RecordingReferenceTraceSink trace)
        {
            return trace.Records
                .Where(item => item.Kind != ReferenceTraceEventKind.BudgetYielded
                    && item.Kind != ReferenceTraceEventKind.ExecutionResumed)
                .Select(item => item.Kind)
                .ToArray();
        }
    }
}
