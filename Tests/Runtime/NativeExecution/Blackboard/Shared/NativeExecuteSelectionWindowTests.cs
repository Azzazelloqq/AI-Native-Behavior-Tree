using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.NativeExecution.Blackboard.Shared
{
    public sealed class NativeExecuteSelectionWindowTests
    {
        [Test]
        public void BeginValidatesWholeAscendingPlanThenOwnsImmutableCopy()
        {
            Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                new NativeExecuteSelectionCapacityV1(3, 2), Allocator.Persistent,
                out var owner, out var failure), Is.True, failure.Code.ToString());
            try
            {
                AssertBeginRejected(owner, Entries(
                    Entry(8, 2, 16), Entry(2, 2, 16)),
                    NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch);
                AssertBeginRejected(owner, Entries(
                    Entry(2, 2, 16), Entry(2, 2, 16)),
                    NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch);
                AssertBeginRejected(owner, Entries(
                    new NativeExecuteSelectionEntryV1(default, 2, 16)),
                    NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch);
                AssertBeginRejected(owner, Entries(
                    Entry(2, 0, 16)),
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid);

                var input = Entries(Entry(2, 0, 0), Entry(8, 3, 24));
                try
                {
                    Assert.That(owner.TryBegin(input, out var window, out failure),
                        Is.True, failure.Code.ToString());
                    input[0] = Entry(99, 1, 1);

                    Assert.That(owner.TryAcquireReadLease(window, out var lease, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(lease.View.Count, Is.EqualTo(2));
                    Assert.That(lease.View.Entries[0].TreeInstanceId, Is.EqualTo(new TreeInstanceId(2)));
                    Assert.That(lease.View.Entries[0].SharedRecordCapacity, Is.Zero);
                    Assert.That(lease.View.Entries[1].TreeInstanceId, Is.EqualTo(new TreeInstanceId(8)));
                    Assert.That(lease.View.Entries[1].SharedRecordCapacity, Is.EqualTo(3));
                    Assert.That(lease.View.Entries[1].SharedPayloadCapacity, Is.EqualTo(24));
                    Assert.That(owner.TryReleaseReadLease(lease, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(owner.TryEnd(window, out failure), Is.True, failure.Code.ToString());
                }
                finally
                {
                    input.Dispose();
                }
            }
            finally
            {
                Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void ReadersBlockMutationAndStaleTokensCannotReleaseAnotherWindow()
        {
            Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                new NativeExecuteSelectionCapacityV1(2, 2), Allocator.Persistent,
                out var owner, out var failure), Is.True, failure.Code.ToString());
            using (var entries = Entries(Entry(2, 1, 4), Entry(8, 1, 4)))
            {
                try
                {
                    Assert.That(owner.TryBegin(entries, out var firstWindow, out failure), Is.True);
                    Assert.That(owner.TryAcquireReadLease(firstWindow, out var first, out failure), Is.True);
                    Assert.That(owner.TryAcquireReadLease(firstWindow, out var second, out failure), Is.True);
                    Assert.That(owner.TryAcquireReadLease(firstWindow, out _, out failure), Is.False);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded));

                    AssertLiveReadersReject(owner, firstWindow, ref failure);
                    Assert.That(owner.TryReleaseReadLease(first, out failure), Is.True);
                    Assert.That(owner.TryReleaseReadLease(first, out failure), Is.False);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                    Assert.That(owner.TryReleaseReadLease(second, out failure), Is.True);
                    Assert.That(owner.TryEnd(firstWindow, out failure), Is.True);

                    Assert.That(owner.TryAcquireReadLease(firstWindow, out _, out failure), Is.False);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                    Assert.That(owner.TryBegin(entries, out var secondWindow, out failure), Is.True);
                    Assert.That(secondWindow.WindowId, Is.Not.EqualTo(firstWindow.WindowId));
                    Assert.That(owner.TryAbort(secondWindow, out failure), Is.True);
                }
                finally
                {
                    Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
                }
            }
        }

        [Test]
        public void InvalidCapacityAndEveryAllocationFailureLeaveNoPublishedOwner()
        {
            Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                default, Allocator.Persistent, out var invalid, out var failure), Is.False);
            Assert.That(invalid, Is.Null);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));

            MethodInfo injected = null;
            foreach (var candidate in typeof(NativeExecuteSelectionWindowOwnerV1)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
            {
                var parameters = candidate.GetParameters();
                if (candidate.Name == "TryCreate" && parameters.Length == 5
                    && parameters[2].ParameterType == typeof(int))
                { injected = candidate; break; }
            }
            Assert.That(injected, Is.Not.Null);
            for (var ordinal = 0; ordinal < 2; ordinal++)
            {
                var arguments = new object[]
                {
                    new NativeExecuteSelectionCapacityV1(2, 2), Allocator.Persistent,
                    ordinal, null, default(NativeRuntimeFailureV1)
                };
                Assert.That((bool)injected.Invoke(null, arguments), Is.False, "allocation " + ordinal);
                Assert.That(arguments[3], Is.Null);
                Assert.That(((NativeRuntimeFailureV1)arguments[4]).Code,
                    Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded));
            }
        }

        [Test]
        public void WarmBeginReadReleaseEndAllocatesZeroManagedBytes()
        {
            Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                new NativeExecuteSelectionCapacityV1(2, 1), Allocator.Persistent,
                out var owner, out var failure), Is.True, failure.Code.ToString());
            using (var entries = Entries(Entry(2, 1, 8), Entry(8, 2, 16)))
            {
                try
                {
                    Assert.That(RunWindow(owner, entries), Is.True);
                    Assert.That(() =>
                    {
                        var controlled = new byte[64];
                        GC.KeepAlive(controlled);
                    }, GcAllocIs.AllocatingGCMemory());
                    var allSucceeded = true;
                    Assert.That(
                        () =>
                        {
                            for (var index = 0; index < 32; index++)
                                allSucceeded &= RunWindow(owner, entries);
                        },
                        GcAllocIs.Not.AllocatingGCMemory());
                    Assert.That(allSucceeded, Is.True);
                }
                finally
                {
                    Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
                }
            }
        }

        [Test]
        public void ResourceEnumPreservesExistingOrdinalsAndAppendsSelectionKinds()
        {
            Assert.That((byte)NativeResourceKindV1.AgentExecuteWindowOwners, Is.EqualTo(34));
            Assert.That((byte)NativeResourceKindV1.ExecuteSelectionEntries, Is.EqualTo(35));
            Assert.That((byte)NativeResourceKindV1.ExecuteSelectionReaders, Is.EqualTo(36));
        }

        [Test]
        public void WindowAndReaderCounterOverflowRejectBeforePublication()
        {
            Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                new NativeExecuteSelectionCapacityV1(1, 1), Allocator.Persistent,
                out var owner, out var failure), Is.True, failure.Code.ToString());
            using (var entries = Entries(Entry(2, 1, 4)))
            {
                try
                {
                    SetCounter(owner, "_nextWindowId", ulong.MaxValue);
                    Assert.That(owner.TryBegin(entries, out _, out failure), Is.False);
                    Assert.That(failure.Code,
                        Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow));
                    Assert.That(owner.State, Is.EqualTo(NativeOwnerStateV1.Initialized));

                    SetCounter(owner, "_nextWindowId", 0);
                    Assert.That(owner.TryBegin(entries, out var window, out failure), Is.True);
                    SetCounter(owner, "_nextReaderLeaseId", ulong.MaxValue);
                    Assert.That(owner.TryAcquireReadLease(window, out _, out failure), Is.False);
                    Assert.That(failure.Code,
                        Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow));
                    Assert.That(owner.TryAbort(window, out failure), Is.True);
                }
                finally
                {
                    Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
                }
            }
        }

        private static bool RunWindow(
            NativeExecuteSelectionWindowOwnerV1 owner,
            NativeArray<NativeExecuteSelectionEntryV1> entries)
        {
            return owner.TryBegin(entries, out var window, out _)
                && owner.TryAcquireReadLease(window, out var lease, out _)
                && lease.View.Count == (uint)entries.Length
                && owner.TryReleaseReadLease(lease, out _)
                && owner.TryEnd(window, out _);
        }

        private static void AssertLiveReadersReject(
            NativeExecuteSelectionWindowOwnerV1 owner,
            NativeExecuteSelectionWindowV1 window,
            ref NativeRuntimeFailureV1 failure)
        {
            Assert.That(owner.TryEnd(window, out failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
            Assert.That(owner.TryAbort(window, out failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
            Assert.That(owner.TryDispose(out failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
        }

        private static void AssertBeginRejected(
            NativeExecuteSelectionWindowOwnerV1 owner,
            NativeArray<NativeExecuteSelectionEntryV1> entries,
            NativeRuntimeDiagnosticCodeV1 expected)
        {
            using (entries)
            {
                Assert.That(owner.TryBegin(entries, out _, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(expected));
            }
        }

        private static NativeExecuteSelectionEntryV1 Entry(ulong id, uint records, uint payload)
            => new NativeExecuteSelectionEntryV1(new TreeInstanceId(id), records, payload);

        private static NativeArray<NativeExecuteSelectionEntryV1> Entries(
            params NativeExecuteSelectionEntryV1[] values)
            => new NativeArray<NativeExecuteSelectionEntryV1>(values, Allocator.TempJob);

        private static void SetCounter(
            NativeExecuteSelectionWindowOwnerV1 owner,
            string fieldName,
            ulong value)
        {
            var field = typeof(NativeExecuteSelectionWindowOwnerV1).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(owner, value);
        }
    }
}
