using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.NativeExecution.Budgeting
{
    public sealed class NativeLifecycleBudgetDriverTests
    {
        [Test]
        public void ZeroAndOneStepSegmentsPreserveTheExactLifecycleCursor()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                    StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1,
                    0, 4, 4, NodeMemoryLifetime.Activation,
                    new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain, 0,
                    new CompiledRange(0, 0), new CompiledRange(0, 0)))
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(0, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.MemorySequence)
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(4, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(1, Allocator.Persistent);
            using var generations = new NativeArray<uint>(1, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            Assert.That(NativeLifecycleMachineV1.TryCreate(nodes, children, bindings, memory, frames, generations, control,
                out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());

            var budget = default(NativeBudgetStateV1);
            Assert.That(NativeLifecycleBudgetDriverV1.TryBeginSegment(0, ref budget), Is.True);
            Assert.That(NativeLifecycleBudgetDriverV1.TryAdvance(ref machine, ref budget, out var kind, out _, out failure), Is.True);
            Assert.That(kind, Is.EqualTo(NativeBudgetAdvanceKindV1.Suspended));
            Assert.That(control[0].SemanticSteps, Is.Zero);

            var expected = new[]
            {
                NativeLifecycleStepKindV1.CompositeEntered,
                NativeLifecycleStepKindV1.ChildAccepted,
                NativeLifecycleStepKindV1.CompositeExited,
                NativeLifecycleStepKindV1.Completed,
            };
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.That(NativeLifecycleBudgetDriverV1.TryBeginSegment(1, ref budget), Is.True);
                Assert.That(NativeLifecycleBudgetDriverV1.TryAdvance(ref machine, ref budget, out kind, out var step, out failure), Is.True, failure.Code.ToString());
                Assert.That(step.Kind, Is.EqualTo(expected[index]));
                Assert.That(kind, Is.EqualTo(index == expected.Length - 1 ? NativeBudgetAdvanceKindV1.Completed : NativeBudgetAdvanceKindV1.Step));
            }
            Assert.That(budget.ResumeCursor, Is.EqualTo(4));
            Assert.That(control[0].SemanticSteps, Is.EqualTo(3));
        }

        [Test]
        public void CounterOverflowRejectsWithoutAdvancingTheMachine()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                    StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1,
                    0, 4, 4, NodeMemoryLifetime.Activation,
                    new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain, 0,
                    new CompiledRange(0, 0), new CompiledRange(0, 0)))
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(0, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.MemorySequence)
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(4, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(1, Allocator.Persistent);
            using var generations = new NativeArray<uint>(1, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);
            Assert.That(NativeLifecycleMachineV1.TryCreate(nodes, children, bindings, memory, frames, generations, control,
                out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
            var budget = new NativeBudgetStateV1 { ResumeCursor = uint.MaxValue };
            Assert.That(NativeLifecycleBudgetDriverV1.TryBeginSegment(1, ref budget), Is.True);
            Assert.That(NativeLifecycleBudgetDriverV1.TryAdvance(
                ref machine, ref budget, out _, out _, out failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow));
            Assert.That(control[0].SemanticSteps, Is.Zero);
            Assert.That(generations[0], Is.Zero);
        }
    }
}
