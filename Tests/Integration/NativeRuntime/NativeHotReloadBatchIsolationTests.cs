using System;
using System.Collections.Generic;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P7-012's own batch-isolation proof: hot-reloading (full restart) exactly one instance among
    /// several sharing a <c>NativeBatchedLifecycleOwnerV1</c> batch must not perturb its untouched
    /// siblings' own results at all -- each lane owns its own independent NativeArrays (confirmed by
    /// reading <c>NativeBatchedLifecycleOwnerV1</c> directly: no shared arena or storage crosses
    /// lanes), so this is a structural guarantee the test makes explicit and verifies empirically
    /// rather than argues from code-reading alone.
    /// </summary>
    internal sealed class NativeHotReloadBatchIsolationTests
    {
        private static readonly ulong RootTypeId = StableHash.Fnv1A64("aibt.core.memory-sequence");
        private static readonly ulong SuccessLeafTypeId = StableHash.Fnv1A64("aibt.tests.p7012.success");
        private static readonly ulong FailureLeafTypeId = StableHash.Fnv1A64("aibt.tests.p7012.failure");

        [Test]
        public void ReloadingOneBatchLane_LeavesTheOtherLanesBitIdenticalToAnUntouchedControlBatch()
        {
            var program = BuildProgram();

            // Control: three fresh instances, none ever reloaded, driven together as one batch.
            var controlInstances = new[]
            {
                BuildBegun(program), BuildBegun(program), BuildBegun(program),
            };
            List<TraceEntry>[] controlTraces;
            try
            {
                controlTraces = DriveBatch(new[] { controlInstances[0].Machine, controlInstances[1].Machine, controlInstances[2].Machine });
            }
            finally
            {
                foreach (var instance in controlInstances) instance.Dispose();
            }

            // Experiment: lane 1 (the middle one) is driven partway, then full-restarted, before the
            // batch is created -- everything else about the three instances is identical to Control.
            var laneA = BuildBegun(program);
            var laneBOld = BuildBegun(program);
            var laneC = BuildBegun(program);
            NativeLifecycleStepResultV1 firstStep = default;
            for (var guard = 0; guard < 64; guard++)
            {
                Assert.That(laneBOld.Machine.TryAdvance(out firstStep, out var firstFailure), Is.True, firstFailure.Code.ToString());
                if (firstStep.Kind == NativeLifecycleStepKindV1.DispatchRequired) break;
            }
            Assert.That(firstStep.Kind, Is.EqualTo(NativeLifecycleStepKindV1.DispatchRequired));
            Assert.That(laneBOld.Machine.TryCompleteDispatch(firstStep.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out var completeFailure),
                Is.True, completeFailure.Code.ToString());
            Assert.That(
                NativeHotReloadFullRestart.TryRestart(laneBOld, program, 2, Allocator.Persistent, out var laneB, out _, out var restartFailure),
                Is.True, restartFailure.Code.ToString());
            // TryRestart disposes laneBOld on success -- do not dispose it again.
            Assert.That(laneB.Machine.TryBeginUpdate(1, out var laneBBeginFailure), Is.True, laneBBeginFailure.Code.ToString());

            List<TraceEntry>[] experimentTraces;
            try
            {
                experimentTraces = DriveBatch(new[] { laneA.Machine, laneB.Machine, laneC.Machine });
            }
            finally
            {
                laneA.Dispose();
                laneB.Dispose();
                laneC.Dispose();
            }

            Assert.That(experimentTraces[0], Is.EqualTo(controlTraces[0]),
                "the untouched sibling BEFORE the reloaded lane must be bit-identical to the control batch's own.");
            Assert.That(experimentTraces[2], Is.EqualTo(controlTraces[2]),
                "the untouched sibling AFTER the reloaded lane must be bit-identical to the control batch's own.");
            // The reloaded lane itself, restarted then driven fresh, must also reach the same
            // deterministic shape as a never-touched instance -- restart-equivalence proven directly.
            Assert.That(experimentTraces[1], Is.EqualTo(controlTraces[1]),
                "the reloaded lane itself, once restarted, must behave exactly like a never-reloaded instance too.");
        }

        private static NativeHotReloadInstance BuildBegun(CompiledProgram program)
        {
            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var instance, out var buildFailure),
                Is.True, buildFailure.Code.ToString());
            Assert.That(instance.Machine.TryBeginUpdate(1, out var beginFailure), Is.True, beginFailure.Code.ToString());
            return instance;
        }

        // Every lane advances exactly one atomic step per round regardless of the others' own
        // progress (NativeBatchedLifecycleOwnerV1.TrySchedule schedules one TryAdvance per lane,
        // unconditionally) -- since all three instances here are built from the identical compiled
        // program and driven by the identical deterministic status rule, they reach Completed on
        // the same round, so this loop never has to special-case a lane finishing early.
        private static List<TraceEntry>[] DriveBatch(NativeLifecycleMachineV1[] machines)
        {
            var traces = new List<TraceEntry>[machines.Length];
            for (var index = 0; index < machines.Length; index++) traces[index] = new List<TraceEntry>();

            Assert.That(NativeBatchedLifecycleOwnerV1.TryCreate(machines, Allocator.Persistent, out var owner, out var failure),
                Is.True, failure.Code.ToString());
            using var results = new NativeArray<NativeLifecycleStepResultV1>(machines.Length, Allocator.Persistent);
            using var failures = new NativeArray<NativeRuntimeFailureV1>(machines.Length, Allocator.Persistent);
            try
            {
                var completedCount = 0;
                while (completedCount < machines.Length)
                {
                    Assert.That(owner.TrySchedule(1, default, out var dependency, out failure), Is.True, failure.Code.ToString());
                    dependency.Complete();
                    Assert.That(owner.TryComplete(results, failures, out failure), Is.True, failure.Code.ToString());
                    completedCount = 0;
                    for (var index = 0; index < machines.Length; index++)
                    {
                        var step = results[index];
                        traces[index].Add(new TraceEntry(step));
                        if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired) CompleteDispatch(ref machines[index], step);
                        if (step.Kind == NativeLifecycleStepKindV1.Completed) completedCount++;
                    }
                }
            }
            finally
            {
                Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
            return traces;
        }

        private static void CompleteDispatch(ref NativeLifecycleMachineV1 machine, NativeLifecycleStepResultV1 step)
        {
            var status = step.Phase != BurstCallbackPhase.Tick
                ? NodeStatus.Running
                : step.NodeIndex == 1u ? NodeStatus.Success : NodeStatus.Failure;
            Assert.That(machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, status, out var failure),
                Is.True, failure.Code.ToString());
        }

        private static CompiledProgram BuildProgram()
        {
            var nodes = new[]
            {
                new CompiledNodeRecord(RootTypeId, 1, 0, 0, 1, 0, 4, 4,
                    NodeMemoryLifetime.Activation, new CompiledRange(0, 2), CompiledNodeFlags.BurstDomain, 0, default, default),
                new CompiledNodeRecord(SuccessLeafTypeId, 1, 0, 0, 1, 0, 0, 1,
                    NodeMemoryLifetime.Activation, default, CompiledNodeFlags.BurstDomain, 1, default, default),
                new CompiledNodeRecord(FailureLeafTypeId, 1, 0, 0, 1, 0, 0, 1,
                    NodeMemoryLifetime.Activation, default, CompiledNodeFlags.BurstDomain, 2, default, default),
            };
            var children = new uint[] { 1, 2 };
            var debug = new[]
            {
                new CompiledDebugMapEntry(0, new NodeId("root"), "test/root"),
                new CompiledDebugMapEntry(1, new NodeId("success"), "test/success"),
                new CompiledDebugMapEntry(2, new NodeId("failure"), "test/failure"),
            };

            var preliminary = Build(new CompiledHash(new string('d', 64)), nodes, children, debug);
            var contentHash = CompiledProgramContentHashV1.Compute(preliminary);
            return Build(contentHash, nodes, children, debug);
        }

        private static CompiledProgram Build(CompiledHash contentHash, CompiledNodeRecord[] nodes, uint[] children, CompiledDebugMapEntry[] debug)
        {
            var header = new CompiledProgramHeader(
                1, 1, new CompiledCompilerVersion(1, 0, 0, 0),
                new CompiledHash(new string('a', 64)), new CompiledHash(new string('b', 64)), new CompiledHash(new string('c', 64)),
                1, contentHash,
                0, (uint)nodes.Length, (uint)children.Length, 0, (uint)debug.Length,
                0, 4, 4, 0, true);
            return new CompiledProgram(
                header, nodes, children,
                Array.Empty<uint>(), Array.Empty<uint>(),
                Array.Empty<CompiledBlackboardSlotRecord>(), Array.Empty<CompiledObserverRecord>(),
                Array.Empty<uint>(), Array.Empty<byte>(), Array.Empty<byte>(), debug);
        }

        private readonly struct TraceEntry : IEquatable<TraceEntry>
        {
            internal TraceEntry(NativeLifecycleStepResultV1 step)
            {
                Kind = step.Kind;
                NodeIndex = step.NodeIndex;
                Phase = step.Phase;
                HasRoot = step.HasRootStatus;
                Root = step.RootStatus;
            }

            private NativeLifecycleStepKindV1 Kind { get; }
            private uint NodeIndex { get; }
            private BurstCallbackPhase Phase { get; }
            private bool HasRoot { get; }
            private NodeStatus Root { get; }

            public bool Equals(TraceEntry other)
                => Kind == other.Kind && NodeIndex == other.NodeIndex && Phase == other.Phase && HasRoot == other.HasRoot && Root == other.Root;
            public override bool Equals(object obj) => obj is TraceEntry other && Equals(other);
            public override int GetHashCode() => ((int)Kind * 397) ^ (int)NodeIndex ^ (int)Phase;
            public override string ToString() => $"{Kind}:{NodeIndex}:{Phase}:{(HasRoot ? Root.ToString() : "-")}";
        }
    }
}
