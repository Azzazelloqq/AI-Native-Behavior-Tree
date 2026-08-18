using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.TestTools.Constraints;
using Unity.Jobs;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.NativeExecution.Snapshots
{
    public sealed class NativeSnapshotTests
    {
        private const ulong ValueBindingId = 0x1001;
        private const ulong MissingBindingId = 0x1002;
        private const ulong ValueTypeId = 0x2001;
        private static readonly BurstHash256 SchemaHash = Hash(1);
        private static readonly BurstHash256 LayoutHash = Hash(11);
        private readonly List<NativeSnapshotBuilderV1> _builders = new List<NativeSnapshotBuilderV1>();
        private readonly List<NativeSnapshotOwnerV1> _owners = new List<NativeSnapshotOwnerV1>();

        [StructLayout(LayoutKind.Sequential)]
        private struct PaddedValue
        {
            public byte Flag;
            public int Count;
            public long Stamp;
        }

        private struct JobResult
        {
            public BurstContextResult Result;
            public PaddedValue Value;
            public ulong Revision;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct SnapshotReadJob : IJob
        {
            [ReadOnly] public NativeSnapshotViewV1 View;
            public NativeSnapshotReadHandleV1<PaddedValue> Handle;
            public NativeArray<JobResult> Output;
            public int OutputIndex;

            public void Execute()
            {
                var result = View.TryRead(Handle, out PaddedValue value);
                Output[OutputIndex] = new JobResult
                {
                    Result = result,
                    Value = value,
                    Revision = View.Revision,
                };
            }
        }

        private struct DelayJob : IJob
        {
            public int Milliseconds;
            public void Execute() => Thread.Sleep(Milliseconds);
        }

        [TearDown]
        public void TearDown()
        {
            for (var index = _owners.Count - 1; index >= 0; index--)
            {
                _owners[index]?.TryDispose();
            }

            for (var index = _builders.Count - 1; index >= 0; index--)
            {
                _builders[index]?.TryDispose();
            }

            _owners.Clear();
            _builders.Clear();
        }

        [Test]
        public void Registry_ResolvesOnlyExactTypedDescriptorsAndOrdinals()
        {
            var descriptor = Descriptor<PaddedValue>(ValueBindingId);
            var missing = Descriptor<int>(MissingBindingId, typeId: 0x2002);
            var value = new PaddedValue { Flag = 1, Count = 42, Stamp = 9001 };
            var owner = Freeze(7, 2, builder =>
            {
                Assert.That(builder.TryAdd(descriptor, value), Is.EqualTo(BurstContextResult.Success));
                Assert.That(builder.TryDeclareMissing<int>(missing), Is.EqualTo(BurstContextResult.Success));
            });

            Assert.That(owner.TryResolve(descriptor, out NativeSnapshotReadHandleV1<PaddedValue> handle),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(handle.IsValid, Is.True);
            Assert.That(owner.TryResolve(missing, out NativeSnapshotReadHandleV1<int> missingHandle),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(missingHandle.IsValid, Is.True);
            Assert.That(owner.TryAcquireRead(out var missingLease), Is.EqualTo(BurstContextResult.Success));
            Assert.That(missingLease.View.TryRead(missingHandle, out int missingValue),
                Is.EqualTo(BurstContextResult.IncompleteValue));
            Assert.That(missingValue, Is.Zero);
            Assert.That(owner.TryRegisterDependency(missingLease, default), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryRelease(missingLease), Is.EqualTo(BurstContextResult.Success));

            var unknown = Descriptor<PaddedValue>(0x9999);
            Assert.That(owner.TryResolve(unknown, out NativeSnapshotReadHandleV1<PaddedValue> _),
                Is.EqualTo(BurstContextResult.InvalidHandle));

            AssertDescriptorMismatch(owner, descriptor, new NativeSnapshotTypeDescriptorV1(
                descriptor.BindingId, descriptor.TypeId + 1, descriptor.TypeVersion,
                descriptor.SchemaHash, descriptor.LayoutHash, descriptor.Size, descriptor.Alignment));
            AssertDescriptorMismatch(owner, descriptor, new NativeSnapshotTypeDescriptorV1(
                descriptor.BindingId, descriptor.TypeId, descriptor.TypeVersion + 1,
                descriptor.SchemaHash, descriptor.LayoutHash, descriptor.Size, descriptor.Alignment));
            AssertDescriptorMismatch(owner, descriptor, new NativeSnapshotTypeDescriptorV1(
                descriptor.BindingId, descriptor.TypeId, descriptor.TypeVersion,
                Hash(2), descriptor.LayoutHash, descriptor.Size, descriptor.Alignment));
            AssertDescriptorMismatch(owner, descriptor, new NativeSnapshotTypeDescriptorV1(
                descriptor.BindingId, descriptor.TypeId, descriptor.TypeVersion,
                descriptor.SchemaHash, Hash(12), descriptor.Size, descriptor.Alignment));
            AssertDescriptorMismatch(owner, descriptor, new NativeSnapshotTypeDescriptorV1(
                descriptor.BindingId, descriptor.TypeId, descriptor.TypeVersion,
                descriptor.SchemaHash, descriptor.LayoutHash, descriptor.Size + 1, descriptor.Alignment));
            AssertDescriptorMismatch(owner, descriptor, new NativeSnapshotTypeDescriptorV1(
                descriptor.BindingId, descriptor.TypeId, descriptor.TypeVersion,
                descriptor.SchemaHash, descriptor.LayoutHash, descriptor.Size, descriptor.Alignment * 2));
            Assert.That(owner.TryResolve(descriptor, out NativeSnapshotReadHandleV1<long> _),
                Is.EqualTo(BurstContextResult.TypeMismatch));

            var samePhysicalDescriptor = Descriptor<int>(0x1010, typeId: 0x2010);
            var samePhysicalOwner = Freeze(8, 1, builder =>
                Assert.That(builder.TryAdd(samePhysicalDescriptor, 123), Is.EqualTo(BurstContextResult.Success)));
            Assert.That(samePhysicalOwner.TryResolve(
                    samePhysicalDescriptor,
                    out NativeSnapshotReadHandleV1<float> sameSizeWrongType),
                Is.EqualTo(BurstContextResult.TypeMismatch));
            Assert.That(sameSizeWrongType.IsValid, Is.False);
        }

        [Test]
        public void ImmediateAndBurstJobs_ReadSameImmutableRevisionAcrossResume()
        {
            var descriptor = Descriptor<PaddedValue>(ValueBindingId);
            var original = new PaddedValue { Flag = 1, Count = 10, Stamp = 100 };
            var replacement = new PaddedValue { Flag = 0, Count = 20, Stamp = 200 };
            var oldOwner = Freeze(11, 1, builder =>
                Assert.That(builder.TryAdd(descriptor, original), Is.EqualTo(BurstContextResult.Success)));
            var newOwner = Freeze(12, 1, builder =>
                Assert.That(builder.TryAdd(descriptor, replacement), Is.EqualTo(BurstContextResult.Success)));

            Assert.That(oldOwner.TryResolve(descriptor, out NativeSnapshotReadHandleV1<PaddedValue> oldHandle),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(newOwner.TryResolve(descriptor, out NativeSnapshotReadHandleV1<PaddedValue> newHandle),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(oldOwner.TryAcquireRead(out var lease), Is.EqualTo(BurstContextResult.Success));
            Assert.That(lease.View.TryRead(oldHandle, out var immediate), Is.EqualTo(BurstContextResult.Success));
            AssertValue(immediate, original);
            Assert.That(lease.View.Revision, Is.EqualTo(11));

            using (var output = new NativeArray<JobResult>(2, Allocator.TempJob, NativeArrayOptions.ClearMemory))
            {
                var first = new SnapshotReadJob { View = lease.View, Handle = oldHandle, Output = output, OutputIndex = 0 }.Schedule();
                var second = new SnapshotReadJob { View = lease.View, Handle = oldHandle, Output = output, OutputIndex = 1 }.Schedule(first);
                Assert.That(oldOwner.TryRegisterDependency(lease, second), Is.EqualTo(BurstContextResult.Success));
                second.Complete();

                for (var index = 0; index < output.Length; index++)
                {
                    Assert.That(output[index].Result, Is.EqualTo(BurstContextResult.Success));
                    Assert.That(output[index].Revision, Is.EqualTo(11));
                    AssertValue(output[index].Value, original);
                }
            }

            Assert.That(oldOwner.TryRelease(lease), Is.EqualTo(BurstContextResult.Success));
            Assert.That(newOwner.TryAcquireRead(out var newLease), Is.EqualTo(BurstContextResult.Success));
            Assert.That(newLease.View.TryRead(newHandle, out var latest), Is.EqualTo(BurstContextResult.Success));
            AssertValue(latest, replacement);
            Assert.That(newLease.View.Revision, Is.EqualTo(12));
            Assert.That(newOwner.TryRegisterDependency(newLease, default), Is.EqualTo(BurstContextResult.Success));
            Assert.That(newOwner.TryRelease(newLease), Is.EqualTo(BurstContextResult.Success));
        }

        [Test]
        public void ReaderLeases_AreFixedForeignSafeAndStaleAfterRelease()
        {
            var descriptor = Descriptor<PaddedValue>(ValueBindingId);
            var value = new PaddedValue { Count = 3 };
            var firstOwner = Freeze(20, 2, builder =>
                Assert.That(builder.TryAdd(descriptor, value), Is.EqualTo(BurstContextResult.Success)));
            var secondOwner = Freeze(21, 1, builder =>
                Assert.That(builder.TryAdd(descriptor, value), Is.EqualTo(BurstContextResult.Success)));
            Assert.That(firstOwner.TryResolve(descriptor, out NativeSnapshotReadHandleV1<PaddedValue> firstHandle),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(secondOwner.TryResolve(descriptor, out NativeSnapshotReadHandleV1<PaddedValue> secondHandle),
                Is.EqualTo(BurstContextResult.Success));

            Assert.That(firstOwner.TryAcquireRead(out var first), Is.EqualTo(BurstContextResult.Success));
            Assert.That(firstOwner.TryAcquireRead(out var second), Is.EqualTo(BurstContextResult.Success));
            Assert.That(firstOwner.TryAcquireRead(out _), Is.EqualTo(BurstContextResult.CapacityExceeded));
            Assert.That(secondOwner.TryRegisterDependency(first, default), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(first.View.TryRead(secondHandle, out PaddedValue _), Is.EqualTo(BurstContextResult.InvalidHandle));

            Assert.That(firstOwner.TryRegisterDependency(first, default), Is.EqualTo(BurstContextResult.Success));
            Assert.That(firstOwner.TryRelease(first), Is.EqualTo(BurstContextResult.Success));
            Assert.That(first.View.TryRead(firstHandle, out PaddedValue _), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(firstOwner.TryRelease(first), Is.EqualTo(BurstContextResult.InvalidHandle));

            Assert.That(firstOwner.TryAcquireRead(out var reused), Is.EqualTo(BurstContextResult.Success));
            Assert.That(first.View.TryRead(firstHandle, out PaddedValue _), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(reused.View.TryRead(firstHandle, out var read), Is.EqualTo(BurstContextResult.Success));
            AssertValue(read, value);
            Assert.That(firstOwner.TryRegisterDependency(reused, default), Is.EqualTo(BurstContextResult.Success));
            Assert.That(firstOwner.TryRelease(reused), Is.EqualTo(BurstContextResult.Success));
            Assert.That(firstOwner.TryRegisterDependency(second, default), Is.EqualTo(BurstContextResult.Success));
            Assert.That(firstOwner.TryRelease(second), Is.EqualTo(BurstContextResult.Success));
        }

        [Test]
        public void LiveJob_BlocksReleaseAndDisposeUntilRegisteredDependencyCompletes()
        {
            var descriptor = Descriptor<PaddedValue>(ValueBindingId);
            var owner = Freeze(30, 1, builder =>
                Assert.That(builder.TryAdd(descriptor, new PaddedValue { Count = 1 }), Is.EqualTo(BurstContextResult.Success)));
            Assert.That(owner.TryResolve(descriptor, out NativeSnapshotReadHandleV1<PaddedValue> handle),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryAcquireRead(out var lease), Is.EqualTo(BurstContextResult.Success));
            using (var output = new NativeArray<JobResult>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory))
            {
                var delay = new DelayJob { Milliseconds = 150 }.Schedule();
                var dependency = new SnapshotReadJob
                {
                    View = lease.View,
                    Handle = handle,
                    Output = output,
                }.Schedule(delay);
                Assert.That(owner.TryRegisterDependency(lease, dependency), Is.EqualTo(BurstContextResult.Success));
                Assert.That(owner.TryRegisterDependency(lease, dependency), Is.EqualTo(BurstContextResult.PhaseViolation));
                Assert.That(owner.TryRelease(lease), Is.EqualTo(BurstContextResult.PhaseViolation));
                Assert.That(owner.TryDispose(), Is.EqualTo(BurstContextResult.PhaseViolation));
                dependency.Complete();
                Assert.That(output[0].Result, Is.EqualTo(BurstContextResult.Success));
                Assert.That(owner.TryRelease(lease), Is.EqualTo(BurstContextResult.Success));
            }

            Assert.That(owner.TryDispose(), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryDispose(), Is.EqualTo(BurstContextResult.InvalidHandle));
        }

        [Test]
        public void BuilderAndOwnerLifecycle_MapStableFailuresWithoutPartialPublication()
        {
            Assert.That(NativeSnapshotBuilderV1.TryCreate(uint.MaxValue, 0, out _),
                Is.EqualTo(BurstContextResult.Overflow));
            var limited = CreateBuilder(1, 3);
            var descriptor = Descriptor<int>(ValueBindingId);
            Assert.That(limited.TryAdd(descriptor, 7), Is.EqualTo(BurstContextResult.CapacityExceeded));
            Assert.That(limited.EntryCount, Is.Zero);
            Assert.That(limited.PayloadBytesUsed, Is.Zero);

            var builder = CreateBuilder(1, 16);
            var invalidDescriptor = new NativeSnapshotTypeDescriptorV1(
                ValueBindingId, ValueTypeId, 1, default, LayoutHash, 4, 4);
            Assert.That(builder.TryAdd(invalidDescriptor, 1), Is.EqualTo(BurstContextResult.TypeMismatch));
            Assert.That(builder.TryAdd(descriptor, 1), Is.EqualTo(BurstContextResult.Success));
            Assert.That(builder.TryAdd(descriptor, 2), Is.EqualTo(BurstContextResult.PhaseViolation));
            var changedDescriptor = Descriptor<int>(ValueBindingId, typeId: ValueTypeId + 1);
            Assert.That(builder.TryAdd(changedDescriptor, 2), Is.EqualTo(BurstContextResult.TypeMismatch));
            Assert.That(builder.TryFreeze(0, 1, out _), Is.EqualTo(BurstContextResult.PhaseViolation));
            Assert.That(builder.TryFreeze(1, 0, out _), Is.EqualTo(BurstContextResult.CapacityExceeded));
            Assert.That(builder.TryFreeze(1, 1, out var owner), Is.EqualTo(BurstContextResult.Success));
            _owners.Add(owner);
            Assert.That(builder.TryAdd(descriptor, 2), Is.EqualTo(BurstContextResult.PhaseViolation));
            Assert.That(builder.TryFreeze(2, 1, out _), Is.EqualTo(BurstContextResult.PhaseViolation));
            Assert.That(builder.TryDispose(), Is.EqualTo(BurstContextResult.Success));
            Assert.That(builder.TryDispose(), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(owner.TryAcquireRead(out var lease), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryDispose(), Is.EqualTo(BurstContextResult.PhaseViolation));
            Assert.That(owner.TryRegisterDependency(lease, default), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryRelease(lease), Is.EqualTo(BurstContextResult.Success));
        }

        [Test]
        public void InitializedTypedReads_AllocateNoManagedMemory()
        {
            var descriptor = Descriptor<PaddedValue>(ValueBindingId);
            var owner = Freeze(40, 1, builder =>
                Assert.That(builder.TryAdd(descriptor, new PaddedValue { Flag = 1, Count = 4, Stamp = 8 }),
                    Is.EqualTo(BurstContextResult.Success)));
            Assert.That(owner.TryResolve(descriptor, out NativeSnapshotReadHandleV1<PaddedValue> handle),
                Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryAcquireRead(out var lease), Is.EqualTo(BurstContextResult.Success));
            Assert.That(lease.View.TryRead(handle, out PaddedValue _), Is.EqualTo(BurstContextResult.Success));

            var successes = 0;
            Assert.That(
                () =>
                {
                    var allocationCanary = new byte[128];
                    GC.KeepAlive(allocationCanary);
                },
                GcAllocIs.AllocatingGCMemory(),
                "GC allocation instrumentation must observe a controlled allocation.");

            Assert.That(
                () =>
                {
                    for (var index = 0; index < 10_000; index++)
                    {
                        if (lease.View.TryRead(handle, out PaddedValue value) == BurstContextResult.Success
                            && value.Count == 4)
                        {
                            successes++;
                        }
                    }
                },
                GcAllocIs.Not.AllocatingGCMemory());

            Assert.That(successes, Is.EqualTo(10_000));
            Assert.That(owner.TryRegisterDependency(lease, default), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryRelease(lease), Is.EqualTo(BurstContextResult.Success));

            Assert.That(owner.TryAcquireRead(out var warmupLease), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryRegisterDependency(warmupLease, default), Is.EqualTo(BurstContextResult.Success));
            Assert.That(owner.TryRelease(warmupLease), Is.EqualTo(BurstContextResult.Success));
            successes = 0;
            Assert.That(
                () =>
                {
                    for (var index = 0; index < 1_000; index++)
                    {
                        if (owner.TryAcquireRead(out var nextLease) == BurstContextResult.Success
                            && owner.TryRegisterDependency(nextLease, default) == BurstContextResult.Success
                            && owner.TryRelease(nextLease) == BurstContextResult.Success)
                        {
                            successes++;
                        }
                    }
                },
                GcAllocIs.Not.AllocatingGCMemory());

            Assert.That(successes, Is.EqualTo(1_000));
        }

        [Test]
        public void SnapshotOwnerIdentity_IsOwnedUniqueAndNonzero()
        {
            const int ownerCount = 64;
            var identities = new HashSet<ulong>();
            for (var index = 0; index < ownerCount; index++)
            {
                var owner = Freeze((ulong)index + 1, 1, builder =>
                    Assert.That(builder.TryAdd(Descriptor<int>(ValueBindingId), index),
                        Is.EqualTo(BurstContextResult.Success)));
                Assert.That(owner.OwnerId, Is.Not.Zero);
                Assert.That(identities.Add(owner.OwnerId), Is.True, "Snapshot owner IDs must never repeat.");
            }

            Assert.That(identities.Count, Is.EqualTo(ownerCount));
        }

        private NativeSnapshotOwnerV1 Freeze(
            ulong revision,
            uint readerCapacity,
            Action<NativeSnapshotBuilderV1> populate)
        {
            var builder = CreateBuilder(8, 256);
            populate(builder);
            Assert.That(builder.TryFreeze(revision, readerCapacity, out var owner), Is.EqualTo(BurstContextResult.Success));
            _owners.Add(owner);
            return owner;
        }

        private NativeSnapshotBuilderV1 CreateBuilder(uint bindings, uint bytes)
        {
            Assert.That(NativeSnapshotBuilderV1.TryCreate(bindings, bytes, out var builder),
                Is.EqualTo(BurstContextResult.Success));
            _builders.Add(builder);
            return builder;
        }

        private static NativeSnapshotTypeDescriptorV1 Descriptor<T>(
            ulong bindingId,
            ulong typeId = ValueTypeId,
            uint typeVersion = 1,
            BurstHash256? schemaHash = null,
            BurstHash256? layoutHash = null)
            where T : unmanaged
            => new NativeSnapshotTypeDescriptorV1(
                bindingId,
                typeId,
                typeVersion,
                schemaHash ?? SchemaHash,
                layoutHash ?? LayoutHash,
                (uint)UnsafeUtility.SizeOf<T>(),
                (uint)UnsafeUtility.AlignOf<T>());

        private static BurstHash256 Hash(uint seed)
            => new BurstHash256(seed, seed + 1, seed + 2, seed + 3, seed + 4, seed + 5, seed + 6, seed + 7);

        private static void AssertDescriptorMismatch(
            NativeSnapshotOwnerV1 owner,
            NativeSnapshotTypeDescriptorV1 original,
            NativeSnapshotTypeDescriptorV1 mismatch)
        {
            Assert.That(mismatch.BindingId, Is.EqualTo(original.BindingId));
            Assert.That(owner.TryResolve(mismatch, out NativeSnapshotReadHandleV1<PaddedValue> _),
                Is.EqualTo(BurstContextResult.TypeMismatch));
        }

        private static void AssertValue(PaddedValue actual, PaddedValue expected)
        {
            Assert.That(actual.Flag, Is.EqualTo(expected.Flag));
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            Assert.That(actual.Stamp, Is.EqualTo(expected.Stamp));
        }
    }
}
