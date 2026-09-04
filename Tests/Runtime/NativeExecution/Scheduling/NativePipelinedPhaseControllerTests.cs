using NUnit.Framework;
using Unity.Collections;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.NativeExecution.Scheduling
{
    public sealed class NativePipelinedPhaseControllerTests
    {
        [Test]
        public void CompletingARoundWithinTheSameStageItWasScheduledIsRefused()
        {
            using var first = new Scenario();
            using var second = new Scenario();
            Assert.That(NativePipelinedPhaseControllerV1.TryCreate(
                new[] { first.Machine, second.Machine }, Allocator.Persistent,
                out var controller, out var failure), Is.True, failure.Code.ToString());
            using var results = new NativeArray<NativeLifecycleStepResultV1>(2, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(2, Allocator.Persistent);
            try
            {
                Assert.That(controller.TryBeginSnapshot(1, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompleteSnapshot(11, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryScheduleExecuteRound(2, default, out var dependency, out failure), Is.True, failure.Code.ToString());
                dependency.Complete();

                Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.False,
                    "A round scheduled this stage must never complete within the same stage -- that would silently collapse pipelined latency to same-frame.");
                Assert.That(controller.TryAdvanceStage(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.True, failure.Code.ToString());
                Assert.That(first.Control[0].SemanticSteps, Is.EqualTo(1));
                Assert.That(second.Control[0].SemanticSteps, Is.EqualTo(1));

                Assert.That(controller.TrySealExecute(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompleteReduce(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompletePublish(out var metrics, out failure), Is.True, failure.Code.ToString());
                Assert.That(metrics.StagesElapsed, Is.EqualTo(1));
                Assert.That(metrics.ExecuteRounds, Is.EqualTo(1));
            }
            finally
            {
                Assert.That(controller.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void MultiRoundPipelinedUpdateAccumulatesRoundsAndTotalStagesElapsedAcrossTheWholeUpdate()
        {
            using var first = new Scenario();
            using var second = new Scenario();
            Assert.That(NativePipelinedPhaseControllerV1.TryCreate(
                new[] { first.Machine, second.Machine }, Allocator.Persistent,
                out var controller, out var failure), Is.True, failure.Code.ToString());
            using var results = new NativeArray<NativeLifecycleStepResultV1>(2, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(2, Allocator.Persistent);
            try
            {
                Assert.That(controller.TryBeginSnapshot(7, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompleteSnapshot(13, out failure), Is.True, failure.Code.ToString());

                for (var round = 0; round < 2; round++)
                {
                    Assert.That(controller.TryScheduleExecuteRound(
                        round == 0 ? 1u : 2u, default, out var dependency, out failure), Is.True, failure.Code.ToString());
                    Assert.That(controller.TryAdvanceStage(out failure), Is.True, failure.Code.ToString());
                    dependency.Complete();
                    Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.True, failure.Code.ToString());
                }

                Assert.That(controller.TrySealExecute(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompleteReduce(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompletePublish(out var metrics, out failure), Is.True, failure.Code.ToString());
                Assert.That(metrics.UpdateId, Is.EqualTo(7));
                Assert.That(metrics.SnapshotRevision, Is.EqualTo(13));
                Assert.That(metrics.LaneCount, Is.EqualTo(2));
                Assert.That(metrics.ExecuteRounds, Is.EqualTo(2));
                Assert.That(metrics.ExecutedAtomicSteps, Is.EqualTo(4));
                Assert.That(metrics.StagesElapsed, Is.EqualTo(2),
                    "Two rounds, each crossing exactly one stage boundary, span two stages end-to-end for this update.");
            }
            finally
            {
                Assert.That(controller.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void PhaseOrderAndUpdateIdMonotonicityAreEnforced()
        {
            using var scenario = new Scenario();
            Assert.That(NativePipelinedPhaseControllerV1.TryCreate(
                new[] { scenario.Machine }, Allocator.Persistent,
                out var controller, out var failure), Is.True, failure.Code.ToString());
            try
            {
                Assert.That(controller.TryCompleteSnapshot(1, out failure), Is.False,
                    "Snapshot cannot complete before it begins.");
                Assert.That(controller.TryScheduleExecuteRound(1, default, out _, out failure), Is.False,
                    "Execute cannot be scheduled before a snapshot completes.");
                Assert.That(controller.TryBeginSnapshot(5, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryAbortUpdate(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryBeginSnapshot(5, out failure), Is.False,
                    "Aborted update IDs are consumed and cannot be replayed.");
                Assert.That(controller.TryBeginSnapshot(6, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompleteSnapshot(9, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryScheduleExecuteRound(1, default, out var dependency, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryAbortUpdate(out failure), Is.False,
                    "Abort cannot observe a live Execute dependency.");
                Assert.That(controller.TryAdvanceStage(out failure), Is.True, failure.Code.ToString());
                dependency.Complete();
                using var results = new NativeArray<NativeLifecycleStepResultV1>(1, Allocator.Persistent);
                using var failures = new NativeArray<NativeRuntimeFailureV1>(1, Allocator.Persistent);
                Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompletePublish(out _, out failure), Is.False,
                    "Publish cannot happen before Reduce is sealed.");
            }
            finally
            {
                Assert.That(controller.TryAbortUpdate(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void SameInstanceCannotOccupyTwoConcurrentPipelinedBatchLanes()
        {
            using var scenario = new Scenario();
            Assert.That(NativePipelinedPhaseControllerV1.TryCreate(
                new[] { scenario.Machine, scenario.Machine }, Allocator.Persistent,
                out var controller, out var failure), Is.False,
                "Ownership guards from NativeBatchedLifecycleOwnerV1 must hold unchanged under pipelining.");
            Assert.That(controller, Is.Null);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
            Assert.That(failure.ResourceKind, Is.EqualTo(NativeResourceKindV1.LifecycleBatchLanes));
        }

        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(3u)]
        [TestCase(4u)]
        public void BatchPartitionsProduceTheSamePerInstanceAtomicOrderAcrossAPipelineStageBoundary(uint batchSize)
        {
            using var first = new Scenario();
            using var second = new Scenario();
            using var third = new Scenario();
            using var fourth = new Scenario();
            Assert.That(NativePipelinedPhaseControllerV1.TryCreate(
                new[] { first.Machine, second.Machine, third.Machine, fourth.Machine }, Allocator.Persistent,
                out var controller, out var failure), Is.True, failure.Code.ToString());
            using var results = new NativeArray<NativeLifecycleStepResultV1>(4, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(4, Allocator.Persistent);
            try
            {
                Assert.That(controller.TryBeginSnapshot(1, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompleteSnapshot(1, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryScheduleExecuteRound(batchSize, default, out var dependency, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryAdvanceStage(out failure), Is.True, failure.Code.ToString());
                dependency.Complete();
                Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.True, failure.Code.ToString());
                for (var index = 0; index < results.Length; index++)
                    Assert.That(results[index].Kind, Is.EqualTo(NativeLifecycleStepKindV1.CompositeEntered));
                Assert.That(first.Control[0].SemanticSteps, Is.EqualTo(1));
                Assert.That(second.Control[0].SemanticSteps, Is.EqualTo(1));
                Assert.That(third.Control[0].SemanticSteps, Is.EqualTo(1));
                Assert.That(fourth.Control[0].SemanticSteps, Is.EqualTo(1));
            }
            finally
            {
                Assert.That(controller.TryAbortUpdate(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void SteadyStatePipelineDrivingIntroducesNoManagedAllocation()
        {
            using var first = new Scenario();
            using var second = new Scenario();
            Assert.That(NativePipelinedPhaseControllerV1.TryCreate(
                new[] { first.Machine, second.Machine }, Allocator.Persistent,
                out var controller, out var failure), Is.True, failure.Code.ToString());
            using var results = new NativeArray<NativeLifecycleStepResultV1>(2, Allocator.Persistent);
            using var failuresArray = new NativeArray<NativeRuntimeFailureV1>(2, Allocator.Persistent);
            try
            {
                var success = false;
                Assert.That(() =>
                {
                    success = controller.TryBeginSnapshot(1, out var stepFailure);
                    success &= controller.TryCompleteSnapshot(1, out stepFailure);
                    success &= controller.TryScheduleExecuteRound(2, default, out var dependency, out stepFailure);
                    success &= controller.TryAdvanceStage(out stepFailure);
                    dependency.Complete();
                    success &= controller.TryCompleteExecuteRound(results, failuresArray, out stepFailure);
                }, GcAllocIs.Not.AllocatingGCMemory());
                Assert.That(success, Is.True);
            }
            finally
            {
                Assert.That(controller.TrySealExecute(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompleteReduce(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompletePublish(out _, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }


        [TestCase(-1, 1)]
        [TestCase(0, 1)]
        [TestCase(2, 1)]
        [TestCase(1, -1)]
        [TestCase(1, 0)]
        [TestCase(1, 2)]
        public void RejectedCompletionBuffersPreserveRoundForRetry(int resultLength, int failureLength)
        {
            using var scenario = new Scenario();
            Assert.That(NativePipelinedPhaseControllerV1.TryCreate(new[] { scenario.Machine },
                Allocator.Persistent, out var controller, out var failure), Is.True);
            using var results = new NativeArray<NativeLifecycleStepResultV1>(1, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(1, Allocator.Persistent);
            using var rejectedResults = resultLength < 0 ? default : new NativeArray<NativeLifecycleStepResultV1>(resultLength, Allocator.Persistent);
            using var rejectedFailures = failureLength < 0 ? default : new NativeArray<NativeRuntimeFailureV1>(failureLength, Allocator.Persistent);
            if (rejectedResults.IsCreated && rejectedResults.Length > 0)
            {
                var writableResults = rejectedResults;
                writableResults[0] = new NativeLifecycleStepResultV1(NativeLifecycleStepKindV1.Completed, 777u);
            }
            try
            {
                Assert.That(controller.TryBeginSnapshot(1, out failure), Is.True);
                Assert.That(controller.TryCompleteSnapshot(1, out failure), Is.True);
                Assert.That(controller.TryScheduleExecuteRound(1, default, out _, out failure), Is.True);
                Assert.That(controller.TryAdvanceStage(out failure), Is.True);
                Assert.That(controller.TryCompleteExecuteRound(rejectedResults, rejectedFailures, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                Assert.That(controller.Phase, Is.EqualTo(NativePipelinedPhaseV1.ExecuteScheduled));
                if (rejectedResults.IsCreated && rejectedResults.Length > 0)
                    Assert.That(rejectedResults[0].NodeIndex, Is.EqualTo(777u), "Rejected buffers must not receive output.");
                Assert.That(controller.TryAbortUpdate(out failure), Is.False);
                Assert.That(controller.TryDispose(out failure), Is.False);
                Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.True);
                Assert.That(results[0].Kind, Is.EqualTo(NativeLifecycleStepKindV1.CompositeEntered));
                Assert.That(scenario.Control[0].SemanticSteps, Is.EqualTo(1), "Retry consumes the same job, without rescheduling.");

                Assert.That(controller.TryScheduleExecuteRound(1, default, out _, out failure), Is.True);
                Assert.That(controller.TryAdvanceStage(out failure), Is.True);
                Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.True);
                Assert.That(scenario.Control[0].SemanticSteps, Is.EqualTo(2));
                Assert.That(controller.TrySealExecute(out failure), Is.True);
                Assert.That(controller.TryCompleteReduce(out failure), Is.True);
                Assert.That(controller.TryCompletePublish(out var metrics, out failure), Is.True);
                Assert.That(metrics.ExecuteRounds, Is.EqualTo(2));
                Assert.That(metrics.ExecutedAtomicSteps, Is.EqualTo(2));
                Assert.That(controller.TryBeginSnapshot(2, out failure), Is.True);
                Assert.That(controller.TryAbortUpdate(out failure), Is.True);
            }
            finally
            {
                if (controller.Phase == NativePipelinedPhaseV1.ExecuteScheduled)
                    controller.TryCompleteExecuteRound(results, failures, out _);
                if (controller.Phase != NativePipelinedPhaseV1.Idle) controller.TryAbortUpdate(out _);
                Assert.That(controller.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void CompletedLaneFailureReleasesRoundAndPreservesDiagnostic()
        {
            using var scenario = new Scenario();
            var controlStorage = scenario.Control;
            var control = controlStorage[0];
            control.UpdateOpen = 0; // The actual job must reject Advance outside an update.
            controlStorage[0] = control;
            Assert.That(NativePipelinedPhaseControllerV1.TryCreate(new[] { scenario.Machine },
                Allocator.Persistent, out var controller, out var failure), Is.True);
            using var results = new NativeArray<NativeLifecycleStepResultV1>(1, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(1, Allocator.Persistent);
            try
            {
                Assert.That(controller.TryBeginSnapshot(1, out failure), Is.True);
                Assert.That(controller.TryCompleteSnapshot(1, out failure), Is.True);
                Assert.That(controller.TryScheduleExecuteRound(1, default, out _, out failure), Is.True);
                Assert.That(controller.TryAdvanceStage(out failure), Is.True);
                Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                Assert.That(failures[0].Code, Is.EqualTo(failure.Code));
                Assert.That(controller.Phase, Is.EqualTo(NativePipelinedPhaseV1.ExecuteReady));
                Assert.That(controller.TryAbortUpdate(out failure), Is.True);
            }
            finally { Assert.That(controller.TryDispose(out failure), Is.True, failure.Code.ToString()); }
        }

        private sealed class Scenario : System.IDisposable
        {
            internal readonly NativeArray<NativeCompiledNodeRecordV1> Nodes;
            internal readonly NativeArray<uint> Children;
            internal readonly NativeArray<NativeLifecycleNodeBindingV1> Bindings;
            internal readonly NativeArray<byte> Memory;
            internal readonly NativeArray<byte> Configuration;
            internal readonly NativeArray<NativeFrameStateV1> Frames;
            internal readonly NativeArray<uint> Generations;
            internal readonly NativeArray<NativeLifecycleControlV1> Control;
            internal readonly NativeLifecycleMachineV1 Machine;

            internal Scenario()
            {
                Nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
                {
                    new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1,
                        0, 4, 4, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain, 0,
                        new CompiledRange(0, 0), new CompiledRange(0, 0)))
                }, Allocator.Persistent);
                Children = new NativeArray<uint>(0, Allocator.Persistent);
                Bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
                {
                    new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.MemorySequence)
                }, Allocator.Persistent);
                Memory = new NativeArray<byte>(4, Allocator.Persistent);
                Configuration = new NativeArray<byte>(0, Allocator.Persistent);
                Frames = new NativeArray<NativeFrameStateV1>(1, Allocator.Persistent);
                Generations = new NativeArray<uint>(1, Allocator.Persistent);
                Control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
                Assert.That(NativeLifecycleMachineV1.TryCreate(
                    Nodes, Children, Bindings, Memory, Frames, Generations, Control, Configuration,
                    out var machine, out var failure), Is.True, failure.Code.ToString());
                Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
                Machine = machine;
            }

            public void Dispose()
            {
                Control.Dispose(); Generations.Dispose(); Frames.Dispose(); Configuration.Dispose();
                Memory.Dispose(); Bindings.Dispose(); Children.Dispose(); Nodes.Dispose();
            }
        }
    }
}
