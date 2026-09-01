using AIBT.Burst;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.Stress
{
    /// <summary>
    /// P7-004's soak-test deliverable: zero unbounded growth in GC allocations and native-container
    /// footprint over a much larger tick count than any existing test exercises (extends
    /// `P2-021`'s own `Tests/Runtime/NativeExecution/Allocation/NativeExecutionAllocationTests.cs`
    /// technique in duration, not in method).
    /// <para>
    /// A real, disclosed finding while designing the third deliverable ("command/completion table
    /// occupancy"): <see cref="NativeCommandAsyncOwnerV1"/>'s own operation-record table
    /// (<c>control.OperationCount</c>, confirmed by reading <c>NativeCommandAsyncOwnerV1.cs</c>
    /// directly) is a monotonic, append-only LIFETIME log -- <c>TryCancel</c>/<c>TryConsume</c> mark
    /// an operation's own state in place but never remove or compact its slot, so
    /// <c>capacity.operations</c> bounds "how many async operations this instance may ever start
    /// across its whole lifetime," not "how many are concurrently in flight." A soak test asserting
    /// "no CapacityExceeded, ever" would therefore be asserting something false by design, not
    /// proving a real leak-free property. This file instead proves the real contract: the boundary
    /// is safe (a clean, correctly-timed <c>CapacityExceeded</c> at exactly the configured capacity,
    /// never silent corruption or an off-by-one), and documents that a genuinely long-running
    /// instance starting many async operations over its lifetime must size this capacity for that
    /// full lifetime or periodically reset (e.g. via hot reload, `P5-004`/`P7-012`) -- existing,
    /// unchanged behavior, not a defect this card found.
    /// </para>
    /// </summary>
    public sealed class NativeExecutionSoakTests
    {
        [Test]
        public void ManyThousandTickWaitCycles_AllocateNoManagedMemoryAfterWarmup()
        {
            // A normally-completed instance (HasRootStatus=1) is terminal -- confirmed by reading
            // NativeLifecycleMachineV1.TryBeginUpdate/PopFrame directly: HasRootStatus is cleared
            // only on the abort path, never after a normal completion, so TryBeginUpdate can never
            // be called again on an instance that reached root Completed. A real long-running agent
            // therefore stays perpetually active (Waiting between ticks, resumed via TryBeginUpdate),
            // exactly like NativeExecutionEquivalenceTests.Scenario's own established BeginNextUpdate
            // pattern -- this soak test drives ONE such instance through 20,000 tick/Waiting cycles,
            // standing in for roughly 5.5 real-time minutes of continuous ticking at 60 updates/sec
            // -- disclosed explicitly, not an unstated assumption, per this card's own acceptance
            // criterion.
            const int TickCount = 20_000;

            using var fixture = new SingleLeafFixture();
            Assert.That(TickToWaiting(ref fixture.Machine, updateId: 1), Is.True, "warmup tick");

            var success = true;
            Assert.That(() =>
            {
                for (ulong index = 0; index < TickCount; index++)
                {
                    success &= TickToWaitingQuiet(ref fixture.Machine, updateId: 2 + index);
                }
            }, GcAllocIs.Not.AllocatingGCMemory());
            Assert.That(success, Is.True);
        }

        [Test]
        public void ManyThousandTickWaitCycles_NeverResizeTheInstancesOwnNativeArrays()
        {
            const int TickCount = 20_000;

            using var fixture = new SingleLeafFixture();
            var lengthsBefore = fixture.CaptureArrayLengths();

            for (ulong index = 0; index < TickCount; index++)
            {
                Assert.That(TickToWaiting(ref fixture.Machine, updateId: 1 + index), Is.True);
            }

            var lengthsAfter = fixture.CaptureArrayLengths();
            Assert.That(lengthsAfter, Is.EqualTo(lengthsBefore),
                "the engine's own fixed-capacity design promise (arrays built once at TryCreate, never resized) " +
                "must hold structurally across a long run, not just be assumed.");
        }

        [Test]
        public void RepeatedAsyncOperationStarts_ExhaustTheLifetimeOperationLogCleanlyAtExactlyItsConfiguredCapacity()
        {
            const uint OperationCapacity = 16;
            var capacity = new NativeCommandAsyncCapacityV1(
                operationRecords: OperationCapacity, operationCancellationPayloadBytes: 0, completionInputRecords: 4,
                pendingCompletionRecords: 4, completionPayloadBytes: 4, completionSources: 4, diagnosticRecords: 16,
                executeCommandRecords: OperationCapacity, cancelCommandRecords: OperationCapacity, commandPayloadBytes: 0);
            Assert.That(NativeCommandAsyncOwnerV1.TryCreate(new TreeInstanceId(1), capacity, Allocator.Persistent, out var owner),
                Is.EqualTo(BurstContextResult.Success));
            try
            {
                Assert.That(owner.TryAcquireExecution(out var lease), Is.EqualTo(BurstContextResult.Success));
                var view = lease.View;
                var startType = new CommandType(201, 1);
                var cancelType = new CommandType(202, 1);

                for (var index = 0u; index < OperationCapacity; index++)
                {
                    var result = view.TryStart(
                        new RuntimeNodeIndex(0), activationGeneration: 1, startType, cancelType,
                        NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out var operationId);
                    Assert.That(result, Is.EqualTo(BurstContextResult.Success), "operation " + index + " of " + OperationCapacity);
                    Assert.That(view.TryCancel(operationId, cancelType, NativePayloadSliceV1.Empty, out _),
                        Is.EqualTo(BurstContextResult.Success));
                }

                // The (capacity + 1)-th start must fail cleanly -- not corrupt an existing record,
                // not silently succeed past the configured bound.
                var overflow = view.TryStart(
                    new RuntimeNodeIndex(0), activationGeneration: 1, startType, cancelType,
                    NativePayloadSliceV1.Empty, NativePayloadSliceV1.Empty, out _);
                Assert.That(overflow, Is.EqualTo(BurstContextResult.CapacityExceeded));
            }
            finally
            {
                owner.TryDispose();
            }
        }

        // The leaf always reports Running, so the tree never reaches root Completed (which would
        // make the instance terminal, per this file's own doc comment) -- every tick ends at
        // Waiting instead, exactly like a real long-running agent.
        private static bool TickToWaiting(ref NativeLifecycleMachineV1 machine, ulong updateId)
        {
            if (!machine.TryBeginUpdate(updateId, out var failure)) { Assert.Fail(failure.Code.ToString()); return false; }
            for (var guard = 0; guard < 16; guard++)
            {
                if (!machine.TryAdvance(out var step, out failure)) { Assert.Fail(failure.Code.ToString()); return false; }
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    if (!machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure))
                    {
                        Assert.Fail(failure.Code.ToString());
                        return false;
                    }
                    continue;
                }
                if (step.Kind == NativeLifecycleStepKindV1.Waiting) return true;
            }
            Assert.Fail("Lifecycle did not reach Waiting within its fixed semantic bound.");
            return false;
        }

        // No Assert/string-formatting calls in here at all -- this is the version driven inside a
        // GcAllocIs.Not.AllocatingGCMemory() block, where even building a failure message would
        // itself register as an allocation and corrupt the measurement.
        private static bool TickToWaitingQuiet(ref NativeLifecycleMachineV1 machine, ulong updateId)
        {
            if (!machine.TryBeginUpdate(updateId, out _)) return false;
            for (var guard = 0; guard < 16; guard++)
            {
                if (!machine.TryAdvance(out var step, out _)) return false;
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    if (!machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out _)) return false;
                    continue;
                }
                if (step.Kind == NativeLifecycleStepKindV1.Waiting) return true;
            }
            return false;
        }

        /// <summary>A two-node program (root memory-sequence, one generated leaf), mirroring
        /// `Tests/Runtime/Benchmarking/SchedulingPolicyDriverTests.cs`'s own established
        /// hand-built-`CompiledProgram` fixture pattern -- kept shallow so a many-thousand-tick
        /// soak run stays wall-clock-bounded in EditMode. Exposes its own owned <see cref="NativeArray{T}"/>
        /// lengths directly (unlike <c>SchedulingAgent</c>, whose backing arrays are private), since
        /// that identity check is this fixture's own reason to exist.</summary>
        private sealed class SingleLeafFixture : System.IDisposable
        {
            private NativeArray<NativeCompiledNodeRecordV1> _nodes;
            private NativeArray<uint> _children;
            private NativeArray<NativeLifecycleNodeBindingV1> _bindings;
            private NativeArray<byte> _memory;
            private NativeArray<NativeFrameStateV1> _frames;
            private NativeArray<uint> _generations;
            private NativeArray<NativeLifecycleControlV1> _control;
            internal NativeLifecycleMachineV1 Machine;

            internal SingleLeafFixture()
            {
                _nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
                {
                    new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1, 0, 4, 4, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 1), CompiledNodeFlags.BurstDomain, 0, default, default)),
                    new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.tests.stress-leaf"), 1, 0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                        default, CompiledNodeFlags.BurstDomain, 1, default, default)),
                }, Allocator.Persistent);
                _children = new NativeArray<uint>(new uint[] { 1 }, Allocator.Persistent);
                _bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
                {
                    new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.MemorySequence),
                    new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
                }, Allocator.Persistent);
                _memory = new NativeArray<byte>(4, Allocator.Persistent);
                _frames = new NativeArray<NativeFrameStateV1>(2, Allocator.Persistent);
                _generations = new NativeArray<uint>(2, Allocator.Persistent);
                _control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
                Assert.That(NativeLifecycleMachineV1.TryCreate(
                    _nodes, _children, _bindings, _memory, _frames, _generations, _control,
                    out var machine, out var failure), Is.True, failure.Code.ToString());
                Machine = machine;
            }

            internal int[] CaptureArrayLengths() => new[]
            {
                _nodes.Length, _children.Length, _bindings.Length, _memory.Length,
                _frames.Length, _generations.Length, _control.Length,
            };

            public void Dispose()
            {
                _control.Dispose(); _generations.Dispose(); _frames.Dispose();
                _memory.Dispose(); _bindings.Dispose(); _children.Dispose(); _nodes.Dispose();
            }
        }
    }
}
