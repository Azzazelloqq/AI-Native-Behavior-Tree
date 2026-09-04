using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.NativeExecution.Scheduling
{
    public sealed class NativeBatchedLifecycleOwnerTests
    {
        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(3u)]
        [TestCase(4u)]
        public void BatchPartitionsProduceTheSamePerInstanceAtomicOrder(uint batchSize)
        {
            using var first = new Scenario();
            using var second = new Scenario();
            using var third = new Scenario();
            using var fourth = new Scenario();
            Assert.That(NativeBatchedLifecycleOwnerV1.TryCreate(
                new[] { first.Machine, second.Machine, third.Machine, fourth.Machine }, Allocator.Persistent, out var owner, out var failure), Is.True, failure.Code.ToString());
            using var results = new NativeArray<NativeLifecycleStepResultV1>(4, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(4, Allocator.Persistent);
            try
            {
                Assert.That(owner.TrySchedule(batchSize, default, out var dependency, out failure), Is.True, failure.Code.ToString());
                Assert.That(owner.TrySchedule(batchSize, default, out _, out failure), Is.False, "An instance batch cannot be scheduled twice concurrently.");
                Assert.That(owner.TryDispose(out failure), Is.False, "A live dependency owns every lane.");
                dependency.Complete();
                Assert.That(owner.TryComplete(results, failures, out failure), Is.True, failure.Code.ToString());
                for (var index = 0; index < results.Length; index++)
                    Assert.That(results[index].Kind, Is.EqualTo(NativeLifecycleStepKindV1.CompositeEntered));
                Assert.That(first.Control[0].SemanticSteps, Is.EqualTo(1));
                Assert.That(second.Control[0].SemanticSteps, Is.EqualTo(1));
                Assert.That(third.Control[0].SemanticSteps, Is.EqualTo(1));
                Assert.That(fourth.Control[0].SemanticSteps, Is.EqualTo(1));
            }
            finally
            {
                Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void SameInstanceCannotOccupyTwoConcurrentBatchLanes()
        {
            using var scenario = new Scenario();
            Assert.That(NativeBatchedLifecycleOwnerV1.TryCreate(
                new[] { scenario.Machine, scenario.Machine }, Allocator.Persistent, out var owner, out var failure),
                Is.False);
            Assert.That(owner, Is.Null);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
            Assert.That(failure.ResourceKind, Is.EqualTo(NativeResourceKindV1.LifecycleBatchLanes));
        }

        [Test]
        public void SameFrameControllerEnforcesSnapshotExecuteReducePublishAndReportsExactRounds()
        {
            using var first = new Scenario();
            using var second = new Scenario();
            Assert.That(NativeSameFramePhaseControllerV1.TryCreate(
                new[] { first.Machine, second.Machine }, Allocator.Persistent,
                out var controller, out var failure), Is.True, failure.Code.ToString());
            using var results = new NativeArray<NativeLifecycleStepResultV1>(2, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(2, Allocator.Persistent);
            try
            {
                Assert.That(controller.TryCompleteSnapshot(1, out failure), Is.False);
                Assert.That(controller.TryBeginSnapshot(7, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryScheduleExecuteRound(1, default, out _, out failure), Is.False);
                Assert.That(controller.TryCompleteSnapshot(13, out failure), Is.True, failure.Code.ToString());

                for (var round = 0; round < 2; round++)
                {
                    Assert.That(controller.TryScheduleExecuteRound(
                        round == 0 ? 1u : 2u, default, out var dependency, out failure), Is.True, failure.Code.ToString());
                    Assert.That(controller.TryCompleteReduce(out failure), Is.False,
                        "Reduce cannot observe a live Execute dependency.");
                    dependency.Complete();
                    Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.True, failure.Code.ToString());
                }

                Assert.That(controller.TrySealExecute(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompletePublish(out _, out failure), Is.False);
                Assert.That(controller.TryCompleteReduce(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryCompletePublish(out var metrics, out failure), Is.True, failure.Code.ToString());
                Assert.That(metrics.UpdateId, Is.EqualTo(7));
                Assert.That(metrics.SnapshotRevision, Is.EqualTo(13));
                Assert.That(metrics.LaneCount, Is.EqualTo(2));
                Assert.That(metrics.ExecuteRounds, Is.EqualTo(2));
                Assert.That(metrics.ExecutedAtomicSteps, Is.EqualTo(4));
                Assert.That(controller.TryBeginSnapshot(7, out failure), Is.False,
                    "Published update IDs are monotonic and cannot be reused.");
                Assert.That(controller.TryBeginSnapshot(8, out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryAbortUpdate(out failure), Is.True, failure.Code.ToString());
                Assert.That(controller.TryBeginSnapshot(8, out failure), Is.False,
                    "Aborted update IDs are consumed and cannot be replayed with different input.");
            }
            finally
            {
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
            Assert.That(NativeSameFramePhaseControllerV1.TryCreate(new[] { scenario.Machine },
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
                Assert.That(controller.TryCompleteExecuteRound(rejectedResults, rejectedFailures, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                Assert.That(controller.Phase, Is.EqualTo(NativeSameFramePhaseV1.ExecuteScheduled));
                if (rejectedResults.IsCreated && rejectedResults.Length > 0)
                    Assert.That(rejectedResults[0].NodeIndex, Is.EqualTo(777u), "Rejected buffers must not receive output.");
                Assert.That(controller.TryAbortUpdate(out failure), Is.False);
                Assert.That(controller.TryDispose(out failure), Is.False);
                Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.True);
                Assert.That(results[0].Kind, Is.EqualTo(NativeLifecycleStepKindV1.CompositeEntered));
                Assert.That(scenario.Control[0].SemanticSteps, Is.EqualTo(1), "Retry consumes the same job, without rescheduling.");

                Assert.That(controller.TryScheduleExecuteRound(1, default, out _, out failure), Is.True);
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
                if (controller.Phase == NativeSameFramePhaseV1.ExecuteScheduled)
                    controller.TryCompleteExecuteRound(results, failures, out _);
                if (controller.Phase != NativeSameFramePhaseV1.Idle) controller.TryAbortUpdate(out _);
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
            Assert.That(NativeSameFramePhaseControllerV1.TryCreate(new[] { scenario.Machine },
                Allocator.Persistent, out var controller, out var failure), Is.True);
            using var results = new NativeArray<NativeLifecycleStepResultV1>(1, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(1, Allocator.Persistent);
            try
            {
                Assert.That(controller.TryBeginSnapshot(1, out failure), Is.True);
                Assert.That(controller.TryCompleteSnapshot(1, out failure), Is.True);
                Assert.That(controller.TryScheduleExecuteRound(1, default, out _, out failure), Is.True);
                Assert.That(controller.TryCompleteExecuteRound(results, failures, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                Assert.That(failures[0].Code, Is.EqualTo(failure.Code));
                Assert.That(controller.Phase, Is.EqualTo(NativeSameFramePhaseV1.ExecuteReady));
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
