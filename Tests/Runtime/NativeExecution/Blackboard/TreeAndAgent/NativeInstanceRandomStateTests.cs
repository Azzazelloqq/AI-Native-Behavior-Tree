using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.NativeExecution.Blackboard.TreeAndAgent
{
    public sealed class NativeInstanceRandomStateTests
    {
        [Test]
        public void InstanceArena_OwnsCompactRandomStreamsAndResetRederivesThem()
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding<int>(BuiltInBlackboardTypes.Int32, 5);
            Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                out var program, out var failure), Is.True, failure.Code.ToString());
            try
            {
                Assert.That(program.TryAcquireReadLeaseV2(out var programLease, out failure), Is.True, failure.Code.ToString());
                try
                {
                    Assert.That(NativeInstanceArenaCapacityV2.TryDerive(programLease.View, out var derived, out failure), Is.True, failure.Code.ToString());
                    var capacity = new NativeInstanceArenaCapacityV2(
                        derived.Semantic, derived.TreeSlotVersions, derived.TreeRevisionCount, 1);
                    Assert.That(NativeInstanceArenaOwnerV1.TryCreateV2(
                        programLease, capacity, Allocator.Persistent, out var instance, out failure), Is.True, failure.Code.ToString());
                    try
                    {
                        using var randomNodes = new NativeArray<uint>(new uint[] { 0 }, Allocator.Persistent);
                        Assert.That(instance.TryInitializeRandomStreams(
                            programLease, 0x0123456789abcdefUL, 17UL, randomNodes, out failure), Is.True, failure.Code.ToString());
                        Assert.That(instance.TryAcquireExecutionLeaseV2(programLease, out var firstLease, out failure), Is.True, failure.Code.ToString());
                        var initialState = firstLease.View.RandomStates[0];
                        var increment = firstLease.View.RandomIncrements[0];
                        var states = firstLease.View.RandomStates;
                        states[0] = initialState ^ 0x9e3779b97f4a7c15UL;
                        Assert.That(instance.TryReleaseExecutionLease(firstLease, out failure), Is.True, failure.Code.ToString());

                        Assert.That(instance.TryReset(out failure), Is.True, failure.Code.ToString());
                        Assert.That(instance.TryAcquireExecutionLeaseV2(programLease, out var resetLease, out failure), Is.True, failure.Code.ToString());
                        Assert.That(resetLease.View.RandomStates[0], Is.EqualTo(initialState));
                        Assert.That(resetLease.View.RandomIncrements[0], Is.EqualTo(increment));
                        Assert.That(resetLease.View.RandomNodeIndices[0], Is.Zero);
                        Assert.That(instance.TryReleaseExecutionLease(resetLease, out failure), Is.True, failure.Code.ToString());
                    }
                    finally
                    {
                        Assert.That(instance.TryDispose(out failure), Is.True, failure.Code.ToString());
                    }
                }
                finally
                {
                    Assert.That(program.TryReleaseReadLease(programLease, out failure), Is.True, failure.Code.ToString());
                }
            }
            finally
            {
                Assert.That(program.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }
    }
}
