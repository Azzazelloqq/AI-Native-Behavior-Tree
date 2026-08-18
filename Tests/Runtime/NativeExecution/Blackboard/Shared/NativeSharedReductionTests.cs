using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using AIBT.Burst;
using AIBT.Tests.Runtime.NativeExecution.Blackboard.TreeAndAgent;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.NativeExecution.Blackboard.Shared
{
    public sealed class NativeSharedReductionTests
    {
        [Test]
        public void NumericReducerTypeMatrixIsInvariantToCompletionOrder()
        {
            AssertNumeric(BuiltInBlackboardTypes.Int32, 99, NativeBlackboardReductionKindV2.Min,
                new[] { Pair(3, 7), Pair(1, -2), Pair(2, 4) }, -2);
            AssertNumeric(BuiltInBlackboardTypes.Int32, -99, NativeBlackboardReductionKindV2.Max,
                new[] { Pair(3, 7), Pair(1, -2), Pair(2, 4) }, 7);
            AssertNumeric(BuiltInBlackboardTypes.Int32, 99, NativeBlackboardReductionKindV2.Sum,
                new[] { Pair(3, 5), Pair(1, -2), Pair(2, 4) }, 7);
            AssertNumeric(BuiltInBlackboardTypes.Int64, 99L, NativeBlackboardReductionKindV2.Min,
                new[] { Pair(3, 7L), Pair(1, -2L), Pair(2, 4L) }, -2L);
            AssertNumeric(BuiltInBlackboardTypes.Int64, -99L, NativeBlackboardReductionKindV2.Max,
                new[] { Pair(3, 7L), Pair(1, -2L), Pair(2, 4L) }, 7L);
            AssertNumeric(BuiltInBlackboardTypes.Int64, 99L, NativeBlackboardReductionKindV2.Sum,
                new[] { Pair(3, 5L), Pair(1, -2L), Pair(2, 4L) }, 7L);
            AssertNumeric(BuiltInBlackboardTypes.Float32, 99f, NativeBlackboardReductionKindV2.Min,
                new[] { Pair(3, 7f), Pair(1, -2f), Pair(2, 4f) }, -2f);
            AssertNumeric(BuiltInBlackboardTypes.Float32, -99f, NativeBlackboardReductionKindV2.Max,
                new[] { Pair(3, 7f), Pair(1, -2f), Pair(2, 4f) }, 7f);
            var f32 = (0.1f + 0.2f) + 0.3f;
            AssertNumeric(BuiltInBlackboardTypes.Float32, 99f, NativeBlackboardReductionKindV2.Sum,
                new[] { Pair(3, 0.3f), Pair(1, 0.1f), Pair(2, 0.2f) }, f32);
            AssertNumeric(BuiltInBlackboardTypes.Float64, 99d, NativeBlackboardReductionKindV2.Min,
                new[] { Pair(3, 7d), Pair(1, -2d), Pair(2, 4d) }, -2d);
            AssertNumeric(BuiltInBlackboardTypes.Float64, -99d, NativeBlackboardReductionKindV2.Max,
                new[] { Pair(3, 7d), Pair(1, -2d), Pair(2, 4d) }, 7d);
            AssertNumeric(BuiltInBlackboardTypes.Float64, 99d, NativeBlackboardReductionKindV2.Sum,
                new[] { Pair(3, 0.3d), Pair(1, 0.1d), Pair(2, 0.2d) }, (0.1d + 0.2d) + 0.3d);
        }

        [Test]
        public void BooleanReducersUseCanonicalValues()
        {
            AssertNumeric(BuiltInBlackboardTypes.Bool, false, NativeBlackboardReductionKindV2.Any,
                new[] { Pair(3, false), Pair(1, false), Pair(2, true) }, true);
            AssertNumeric(BuiltInBlackboardTypes.Bool, true, NativeBlackboardReductionKindV2.All,
                new[] { Pair(3, true), Pair(1, true), Pair(2, false) }, false);
        }

        [Test]
        public void FirstAndLastUseStableSemanticOrderForFixedStrings()
        {
            AssertNumeric(BuiltInBlackboardTypes.FixedString32, new FixedString32Bytes("default"),
                NativeBlackboardReductionKindV2.First,
                new[] { Pair(3, new FixedString32Bytes("last")), Pair(1, new FixedString32Bytes("first")), Pair(2, new FixedString32Bytes("middle")) },
                new FixedString32Bytes("first"));
            AssertNumeric(BuiltInBlackboardTypes.FixedString32, new FixedString32Bytes("default"),
                NativeBlackboardReductionKindV2.Last,
                new[] { Pair(3, new FixedString32Bytes("last")), Pair(1, new FixedString32Bytes("first")), Pair(2, new FixedString32Bytes("middle")) },
                new FixedString32Bytes("last"));
        }

        [Test]
        public void FirstAndLastPreserveEnumContractAndValue()
        {
            var enumContract = StableHash.Fnv1A64("aibt.test.enum");
            AssertNumeric(BuiltInBlackboardTypes.Enum32, new Enum32Value(enumContract, 0),
                NativeBlackboardReductionKindV2.First,
                new[] { Pair(2, new Enum32Value(enumContract, 2)), Pair(1, new Enum32Value(enumContract, 1)) },
                new Enum32Value(enumContract, 1), enumContract);
            AssertNumeric(BuiltInBlackboardTypes.Enum32, new Enum32Value(enumContract, 0),
                NativeBlackboardReductionKindV2.Last,
                new[] { Pair(2, new Enum32Value(enumContract, 2)), Pair(1, new Enum32Value(enumContract, 1)) },
                new Enum32Value(enumContract, 2), enumContract);
        }

        [Test]
        public void FirstAndLastUseCanonicalRegisteredBytes()
        {
            var descriptor = RegisteredDescriptor();
            var fields = RegisteredFields();
            var defaultValue = new RegisteredValue { Value = 0, Weight = 0f };
            var values = new[]
            {
                Pair(2, new RegisteredValue { Value = 2, Weight = 2f }),
                Pair(1, new RegisteredValue { Value = 1, Weight = 1f }),
            };
            var first = Reduce(
                NativeBlackboardProgramBindingTests.Fixture.CreateRegisteredSharedReductionBinding(
                    "aibt.test.registered", "aibt.test.registered-schema", descriptor,
                    defaultValue, NativeBlackboardReductionKindV2.First, fields),
                values, new ulong[] { 2, 1 });
            Assert.That(first.Value.Value, Is.EqualTo(1));
            Assert.That(first.Value.Weight, Is.EqualTo(1f));
            var last = Reduce(
                NativeBlackboardProgramBindingTests.Fixture.CreateRegisteredSharedReductionBinding(
                    "aibt.test.registered", "aibt.test.registered-schema", descriptor,
                    defaultValue, NativeBlackboardReductionKindV2.Last, fields),
                values, new ulong[] { 1, 2 });
            Assert.That(last.Value.Value, Is.EqualTo(2));
            Assert.That(last.Value.Weight, Is.EqualTo(2f));
        }

        [Test]
        public void EqualReplacementIsNoOpAndRegisteredEqualityUsesCanonicalBytes()
        {
            var equal = Reduce(
                NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                    BuiltInBlackboardTypes.Int32, 5, NativeBlackboardReductionKindV2.First),
                new[] { Pair(1, 5) }, new ulong[] { 1 });
            Assert.That(equal.Value, Is.EqualTo(5));
            Assert.That(equal.Version, Is.Zero);
            Assert.That(equal.Revision, Is.Zero);
            Assert.That(equal.Report.ChangedSlotCount, Is.Zero);

            var descriptor = RegisteredDescriptor();
            var fields = RegisteredFields();
            var value = new RegisteredValue { Value = 7, Weight = 1.5f };
            var registered = Reduce(
                NativeBlackboardProgramBindingTests.Fixture.CreateRegisteredSharedReductionBinding(
                    "aibt.test.registered", "aibt.test.registered-schema", descriptor,
                    value, NativeBlackboardReductionKindV2.First, fields),
                new[] { Pair(1, value) }, new ulong[] { 1 });
            Assert.That(registered.Version, Is.Zero);
            Assert.That(registered.Revision, Is.Zero);
        }

        [Test]
        public void IntegerAndFloatingIntermediateOverflowRejectWholeContextWithoutMutation()
        {
            AssertReductionRejected(
                BuiltInBlackboardTypes.Int32, 5, NativeBlackboardReductionKindV2.Sum,
                new[] { Pair(1, int.MaxValue), Pair(2, 1) },
                NativeRuntimeDiagnosticCodeV1.BlackboardInvalidValue);
            AssertReductionRejected(
                BuiltInBlackboardTypes.Int64, 5L, NativeBlackboardReductionKindV2.Sum,
                new[] { Pair(1, long.MinValue), Pair(2, -1L) },
                NativeRuntimeDiagnosticCodeV1.BlackboardInvalidValue);
            AssertReductionRejected(
                BuiltInBlackboardTypes.Float32, 5f, NativeBlackboardReductionKindV2.Sum,
                new[] { Pair(1, float.MaxValue), Pair(2, float.MaxValue) },
                NativeRuntimeDiagnosticCodeV1.BlackboardInvalidValue);
            AssertReductionRejected(
                BuiltInBlackboardTypes.Float64, 5d, NativeBlackboardReductionKindV2.Sum,
                new[] { Pair(1, double.MaxValue), Pair(2, double.MaxValue) },
                NativeRuntimeDiagnosticCodeV1.BlackboardInvalidValue);
        }

        [Test]
        public void CanceledEmptyStreamIsNoOpAndReportIsEligibleOnlyForNextUpdate()
        {
            using (var harness = UpdateHarness.Create(
                NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                    BuiltInBlackboardTypes.Int32, 5, NativeBlackboardReductionKindV2.Sum),
                new ulong[] { 1 }, 1, 4))
            {
                harness.Cancel(1);
                Assert.That(harness.Reduce(out var report, out var failure), Is.True, failure.Code.ToString());
                Assert.That(ReadValue<int>(harness.Scenario.Context), Is.EqualTo(5));
                Assert.That(GetArray<ulong>(harness.Scenario.Context, "_versions")[0], Is.Zero);
                Assert.That(GetArray<ulong>(harness.Scenario.Context, "_revision")[0], Is.Zero);
                Assert.That(report.ChangedSlotCount, Is.Zero);
                Assert.That(report.EligibleUpdateId, Is.EqualTo(report.SourceUpdateId + 1));
                Assert.That(report.IsValid, Is.True);
                harness.BeginAndAbortNextUpdate();
                Assert.That(report.IsValid, Is.False);
            }
        }

        [Test]
        public void MultiSlotCommitUsesScopeOrderAndOneContextRevision()
        {
            using (var harness = UpdateHarness.Create(
                NativeBlackboardProgramBindingTests.Fixture.CreateTwoSharedReductionBinding(),
                new ulong[] { 1 }, 2, 8))
            {
                harness.Append(1, new[] { new Contribution<int>(0, 11), new Contribution<int>(1, 13) });
                Assert.That(harness.Reduce(out var report, out var failure), Is.True, failure.Code.ToString());
                var values = GetArray<byte>(harness.Scenario.Context, "_values");
                Assert.That(new NativeSlice<byte>(values, 0, 4).SliceConvert<int>()[0], Is.EqualTo(11));
                Assert.That(new NativeSlice<byte>(values, 4, 4).SliceConvert<int>()[0], Is.EqualTo(13));
                CollectionAssert.AreEqual(new ulong[] { 1, 1 }, GetArray<ulong>(harness.Scenario.Context, "_versions"));
                Assert.That(GetArray<ulong>(harness.Scenario.Context, "_revision")[0], Is.EqualTo(1));
                Assert.That(report.ChangedSlotCount, Is.EqualTo(2));
                Assert.That(report.ChangedScopeSlots[0], Is.EqualTo(0));
                Assert.That(report.ChangedScopeSlots[1], Is.EqualTo(1));
            }
        }

        [Test]
        public void InvalidUnselectedFirstInputRejectsWholeContextWithoutMutation()
        {
            using (var harness = UpdateHarness.Create(
                NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                    BuiltInBlackboardTypes.Float32, 5f, NativeBlackboardReductionKindV2.First),
                new ulong[] { 1 }, 2, 8))
            {
                harness.Append(1, new[] { new Contribution<float>(0, 1f), new Contribution<float>(0, 2f) });
                var streams = GetArray<NativeSharedContributionStreamV1>(harness.Scenario.Context, "_streamHeaders");
                var records = GetArray<NativeSharedContributionRecordV1>(harness.Scenario.Context, "_records");
                var payload = GetArray<byte>(harness.Scenario.Context, "_payload");
                var unselected = records[1];
                var nonFinite = BitConverter.GetBytes(float.NaN);
                for (var index = 0; index < nonFinite.Length; index++)
                    payload[(int)unselected.PayloadOffset + index] = nonFinite[index];
                Assert.That(harness.Reduce(out _, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardInvalidValue));
                Assert.That(ReadValue<float>(harness.Scenario.Context), Is.EqualTo(5f));
                Assert.That(GetArray<ulong>(harness.Scenario.Context, "_versions")[0], Is.Zero);
                Assert.That(GetArray<ulong>(harness.Scenario.Context, "_revision")[0], Is.Zero);
            }
        }

        [Test]
        public void GlobalDuplicateSemanticKeyRejectsWholeContextBeforePublish()
        {
            using (var harness = UpdateHarness.Create(
                NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                    BuiltInBlackboardTypes.Int32, 5, NativeBlackboardReductionKindV2.Sum),
                new ulong[] { 1, 2 }, 1, 4))
            {
                harness.Append(1, new[] { new Contribution<int>(0, 3) });
                harness.Append(2, new[] { new Contribution<int>(0, 4) });
                var streams = GetArray<NativeSharedContributionStreamV1>(harness.Scenario.Context, "_streamHeaders");
                var records = GetArray<NativeSharedContributionRecordV1>(harness.Scenario.Context, "_records");
                var secondRecordIndex = (int)streams[0].Capacity;
                streams[1] = WithField(streams[1], "OwnerTreeInstanceId", streams[0].TreeInstanceId.Value);
                records[secondRecordIndex] = WithBackingField(
                    records[secondRecordIndex], "TreeInstanceIdValue", streams[0].TreeInstanceId.Value);
                Assert.That(harness.Reduce(out _, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch));
                Assert.That(ReadValue<int>(harness.Scenario.Context), Is.EqualTo(5));
                Assert.That(GetArray<ulong>(harness.Scenario.Context, "_versions")[0], Is.Zero);
                Assert.That(GetArray<ulong>(harness.Scenario.Context, "_revision")[0], Is.Zero);
            }
        }

        [Test]
        public void BurstReductionLeaseCommitsThroughRegisteredDependency()
        {
            using (var harness = UpdateHarness.Create(
                NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                    BuiltInBlackboardTypes.Int32, 5, NativeBlackboardReductionKindV2.Sum),
                new ulong[] { 1 }, 1, 4))
            using (var result = new NativeArray<BurstContextResult>(1, Allocator.TempJob))
            {
                harness.Append(1, new[] { new Contribution<int>(0, 9) });
                Assert.That(harness.Scenario.Context.TryAcquireReductionLease(
                    harness.Update, out var lease, out var failure), Is.True, failure.Code.ToString());
                var handle = new ReduceJob { View = lease.View, Result = result }.Schedule();
                Assert.That(harness.Scenario.Context.TryRegisterReductionDependency(
                    lease, handle, out failure), Is.True, failure.Code.ToString());
                handle.Complete();
                Assert.That(harness.Scenario.Context.TryCompleteReduction(
                    lease, out var report, out failure), Is.True, failure.Code.ToString());
                harness.EndSelection();
                Assert.That(result[0], Is.EqualTo(BurstContextResult.Success));
                Assert.That(ReadValue<int>(harness.Scenario.Context), Is.EqualTo(9));
                Assert.That(report.ChangedSlotCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void RepeatedReduceWindowsAllocateZeroManagedBytesAfterInitialization()
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                BuiltInBlackboardTypes.Int32, 5, NativeBlackboardReductionKindV2.Sum);
            using (var scenario = NativeSharedContextTests.SharedScenario.Create(binding, 1, 1, 1, 4))
            {
                var entries = new NativeArray<NativeExecuteSelectionEntryV1>(1, Allocator.Persistent);
                var candidate = new NativeArray<int>(1, Allocator.Persistent);
                var tree = scenario.Bind(1);
                Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                    new NativeExecuteSelectionCapacityV1(1, 1), Allocator.Persistent,
                    out var selectionOwner, out var failure), Is.True, failure.Code.ToString());
                entries[0] = new NativeExecuteSelectionEntryV1(new TreeInstanceId(1), 1, 4);
                candidate[0] = 7;
                try
                {
                    Assert.That(ExecuteOneReductionWindow(
                        scenario, tree, selectionOwner, entries, candidate), Is.True);
                    Assert.That(
                        () =>
                        {
                            var controlled = new byte[64];
                            GC.KeepAlive(controlled);
                        },
                        GcAllocIs.AllocatingGCMemory(),
                        "Allocation probe must detect a controlled managed allocation.");

                    var success = true;
                    Assert.That(
                        () =>
                        {
                            for (var index = 0; index < 16; index++)
                                success &= ExecuteOneReductionWindow(
                                    scenario, tree, selectionOwner, entries, candidate);
                        },
                        GcAllocIs.Not.AllocatingGCMemory());
                    Assert.That(success, Is.True);
                }
                finally
                {
                    Assert.That(selectionOwner.TryDispose(out failure), Is.True, failure.Code.ToString());
                    candidate.Dispose();
                    entries.Dispose();
                }
            }
        }

        [Test]
        public void ReportEpochCrossesUIntBoundaryAndUlongMaxFailsWithoutMutation()
        {
            using (var harness = UpdateHarness.Create(
                NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                    BuiltInBlackboardTypes.Int32, 5, NativeBlackboardReductionKindV2.Sum),
                new ulong[] { 1 }, 1, 4))
            {
                SetPrivateField(harness.Scenario.Context, "_nextReportId", (ulong)uint.MaxValue - 1);
                harness.Cancel(1);
                Assert.That(harness.Reduce(out var atBoundary, out var failure), Is.True, failure.Code.ToString());
                Assert.That(atBoundary.ReportId, Is.EqualTo((ulong)uint.MaxValue));
                Assert.That(atBoundary.IsValid, Is.True);

                Assert.That(harness.TryReduceNextCanceledUpdate(out var beyondBoundary, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(atBoundary.IsValid, Is.False);
                Assert.That(beyondBoundary.ReportId, Is.EqualTo((ulong)uint.MaxValue + 1));
                Assert.That(beyondBoundary.IsValid, Is.True);

                var revision = GetArray<ulong>(harness.Scenario.Context, "_revision")[0];
                var version = GetArray<ulong>(harness.Scenario.Context, "_versions")[0];
                SetPrivateField(harness.Scenario.Context, "_nextReportId", ulong.MaxValue);
                Assert.That(harness.TryReduceNextCanceledUpdate(out var rejected, out failure), Is.False);
                Assert.That(rejected.IsValid, Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow));
                Assert.That(GetArray<ulong>(harness.Scenario.Context, "_revision")[0], Is.EqualTo(revision));
                Assert.That(GetArray<ulong>(harness.Scenario.Context, "_versions")[0], Is.EqualTo(version));
            }
        }

        private static void AssertNumeric<T>(
            BlackboardTypeDescriptor descriptor,
            T defaultValue,
            NativeBlackboardReductionKindV2 reduction,
            PairValue<T>[] values,
            T expected,
            ulong enumContract = 0)
            where T : unmanaged
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                descriptor, defaultValue, reduction, enumContract);
            var forward = new ulong[values.Length];
            var reverse = new ulong[values.Length];
            for (var index = 0; index < values.Length; index++)
            { forward[index] = values[index].TreeId; reverse[index] = values[values.Length - 1 - index].TreeId; }
            var first = Reduce(binding, values, forward);
            var second = Reduce(binding, values, reverse);
            Assert.That(first.Value, Is.EqualTo(expected));
            Assert.That(second.Value, Is.EqualTo(expected));
            Assert.That(first.Version, Is.EqualTo(second.Version));
            Assert.That(first.Revision, Is.EqualTo(second.Revision));
        }

        private static void AssertReductionRejected<T>(
            BlackboardTypeDescriptor descriptor,
            T defaultValue,
            NativeBlackboardReductionKindV2 reduction,
            PairValue<T>[] values,
            NativeRuntimeDiagnosticCodeV1 code)
            where T : unmanaged
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                descriptor, defaultValue, reduction);
            var order = new ulong[values.Length];
            for (var index = 0; index < values.Length; index++) order[index] = values[index].TreeId;
            var rejected = Reduce(binding, values, order, false);
            Assert.That(rejected.Success, Is.False);
            Assert.That(rejected.Failure.Code, Is.EqualTo(code));
            Assert.That(rejected.Value, Is.EqualTo(defaultValue));
            Assert.That(rejected.Version, Is.Zero);
            Assert.That(rejected.Revision, Is.Zero);
        }

        private static ReductionResult<T> Reduce<T>(
            NativeProgramBlackboardBindingV2 binding,
            PairValue<T>[] values,
            ulong[] completionOrder,
            bool expectSuccess = true)
            where T : unmanaged
        {
            var payloadBytes = checked((uint)(values.Length * UnsafeUtility.SizeOf<T>()));
            using (var scenario = NativeSharedContextTests.SharedScenario.Create(
                binding, (uint)values.Length, (uint)values.Length, (uint)values.Length, payloadBytes))
            {
                var byId = new Dictionary<ulong, NativeSharedContextTests.BoundTree>();
                var sorted = (PairValue<T>[])values.Clone();
                Array.Sort(sorted, (left, right) => left.TreeId.CompareTo(right.TreeId));
                for (var index = 0; index < sorted.Length; index++) byId.Add(sorted[index].TreeId, scenario.Bind(sorted[index].TreeId));
                Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                    new NativeExecuteSelectionCapacityV1((uint)values.Length, 1), Allocator.Persistent,
                    out var selectionOwner, out var failure), Is.True, failure.Code.ToString());
                var entries = new NativeArray<NativeExecuteSelectionEntryV1>(values.Length, Allocator.Temp);
                try
                {
                    for (var index = 0; index < sorted.Length; index++)
                        entries[index] = new NativeExecuteSelectionEntryV1(
                            new TreeInstanceId(sorted[index].TreeId), 1, (uint)UnsafeUtility.SizeOf<T>());
                    Assert.That(selectionOwner.TryBegin(entries, out var selection, out failure), Is.True, failure.Code.ToString());
                    Assert.That(scenario.Context.TryBeginUpdate(
                        selectionOwner, selection, out var update, out failure), Is.True, failure.Code.ToString());
                    for (var orderIndex = 0; orderIndex < completionOrder.Length; orderIndex++)
                    {
                        var id = completionOrder[orderIndex];
                        var pair = Find(values, id);
                        var tree = byId[id];
                        Assert.That(tree.Instance.TryAcquireExecutionLeaseV2(
                            scenario.ProgramLease, out var execution, out failure), Is.True, failure.Code.ToString());
                        Assert.That(scenario.Context.TryAcquireContributionStream(
                            update, tree.Binding, execution, out var contribution, out failure), Is.True, failure.Code.ToString());
                        var candidate = new NativeArray<T>(1, Allocator.TempJob);
                        try
                        {
                            candidate[0] = pair.Value;
                            var type = binding.RegisteredTypes.Count == 0
                                ? NativeBlackboardTypeIdV2.BuiltIn(Descriptor(pair.Value, binding), binding.Slots[0].EnumContractId)
                                : NativeBlackboardTypeIdV2.Registered(0, execution.Program.RegisteredTypes[0]);
                            Assert.That(NativeSharedBlackboardV1.TryContribute(
                                contribution.Writer, 0, 0, type, candidate), Is.EqualTo(BurstContextResult.Success));
                        }
                        finally { candidate.Dispose(); }
                        Assert.That(scenario.Context.TryRegisterDependency(contribution, default, out failure), Is.True, failure.Code.ToString());
                        Assert.That(scenario.Context.TrySealContributionStream(contribution, out _, out failure), Is.True, failure.Code.ToString());
                        Assert.That(tree.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
                    }
                    var success = scenario.Context.TryReduceUpdate(update, out var report, out failure);
                    Assert.That(selectionOwner.TryEnd(selection, out var closeFailure), Is.True, closeFailure.Code.ToString());
                    var actual = ReadValue<T>(scenario.Context);
                    var versions = GetArray<ulong>(scenario.Context, "_versions");
                    var revision = GetArray<ulong>(scenario.Context, "_revision");
                    if (expectSuccess) Assert.That(success, Is.True, failure.Code.ToString());
                    return new ReductionResult<T>(success, actual, versions[0], revision[0], report, failure);
                }
                finally
                {
                    entries.Dispose();
                    Assert.That(selectionOwner.TryDispose(out failure), Is.True, failure.Code.ToString());
                }
            }
        }

        private static BlackboardTypeDescriptor Descriptor<T>(T value, NativeProgramBlackboardBindingV2 binding)
            where T : unmanaged
        {
            var slot = binding.Slots[0];
            foreach (var descriptor in new[]
            {
                BuiltInBlackboardTypes.Bool, BuiltInBlackboardTypes.Int32, BuiltInBlackboardTypes.Int64,
                BuiltInBlackboardTypes.Float32, BuiltInBlackboardTypes.Float64,
                BuiltInBlackboardTypes.FixedString32, BuiltInBlackboardTypes.Enum32,
            }) if (descriptor.TypeId == slot.TypeId) return descriptor;
            throw new InvalidOperationException("Unexpected reduction test type.");
        }

        private static PairValue<T> Find<T>(PairValue<T>[] values, ulong id) where T : unmanaged
        { for (var index = 0; index < values.Length; index++) if (values[index].TreeId == id) return values[index]; throw new InvalidOperationException(); }
        private static PairValue<T> Pair<T>(ulong treeId, T value) where T : unmanaged => new PairValue<T>(treeId, value);
        private static T ReadValue<T>(NativeSharedContextOwnerV1 context) where T : unmanaged
        { var values = GetArray<byte>(context, "_values"); return new NativeSlice<byte>(values, 0, UnsafeUtility.SizeOf<T>()).SliceConvert<T>()[0]; }
        private static NativeArray<T> GetArray<T>(object owner, string name) where T : struct
            => (NativeArray<T>)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static T WithField<T, TValue>(T value, string name, TValue replacement) where T : struct
        {
            object boxed = value;
            typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(boxed, replacement);
            return (T)boxed;
        }
        private static T WithBackingField<T, TValue>(T value, string name, TValue replacement) where T : struct
            => WithField(value, "<" + name + ">k__BackingField", replacement);
        private static void SetPrivateField(object owner, string name, object value)
            => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);

        private static bool ExecuteOneReductionWindow(
            NativeSharedContextTests.SharedScenario scenario,
            NativeSharedContextTests.BoundTree tree,
            NativeExecuteSelectionWindowOwnerV1 selectionOwner,
            NativeArray<NativeExecuteSelectionEntryV1> entries,
            NativeArray<int> candidate)
        {
            if (!selectionOwner.TryBegin(entries, out var selection, out _)) return false;
            if (!scenario.Context.TryBeginUpdate(selectionOwner, selection, out var update, out _)) return false;
            if (!tree.Instance.TryAcquireExecutionLeaseV2(scenario.ProgramLease, out var execution, out _)) return false;
            if (!scenario.Context.TryAcquireContributionStream(
                    update, tree.Binding, execution, out var stream, out _)) return false;
            var type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32);
            if (NativeSharedBlackboardV1.TryContribute(
                    stream.Writer, 0, 0, type, candidate) != BurstContextResult.Success) return false;
            if (!scenario.Context.TryRegisterDependency(stream, default, out _)) return false;
            if (!scenario.Context.TrySealContributionStream(stream, out _, out _)) return false;
            if (!tree.Instance.TryReleaseExecutionLease(execution, out _)) return false;
            if (!scenario.Context.TryReduceUpdate(update, out _, out _)) return false;
            return selectionOwner.TryEnd(selection, out _);
        }

        private static RegisteredUnmanagedTypeDescriptor RegisteredDescriptor()
            => new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("aibt.test.registered"), 1, 8, 4,
                NativeBlackboardProgramBindingTests.Fixture.CanonicalEquality,
                StableHash.Fnv1A64("aibt.test.registered-schema"));
        private static NativeRegisteredBlackboardFieldBindingV2[] RegisteredFields()
            => new[]
            {
                new NativeRegisteredBlackboardFieldBindingV2(
                    StableHash.Fnv1A64("value"), BuiltInBlackboardTypes.Int32.TypeId, 1,
                    0, 4, 4, NativeBlackboardFieldEncodingV2.Int32LE, 0, default, 0),
                new NativeRegisteredBlackboardFieldBindingV2(
                    StableHash.Fnv1A64("weight"), BuiltInBlackboardTypes.Float32.TypeId, 1,
                    4, 4, 4, NativeBlackboardFieldEncodingV2.Float32BitsLE, 0, default, 0),
            };

        private readonly struct PairValue<T> where T : unmanaged
        { internal PairValue(ulong treeId, T value) { TreeId = treeId; Value = value; } internal ulong TreeId { get; } internal T Value { get; } }
        private readonly struct ReductionResult<T> where T : unmanaged
        {
            internal ReductionResult(bool success, T value, ulong version, ulong revision, NativeSharedCommitReportV1 report, NativeRuntimeFailureV1 failure)
            { Success = success; Value = value; Version = version; Revision = revision; Report = report; Failure = failure; }
            internal bool Success { get; } internal T Value { get; } internal ulong Version { get; }
            internal ulong Revision { get; } internal NativeSharedCommitReportV1 Report { get; } internal NativeRuntimeFailureV1 Failure { get; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RegisteredValue { internal int Value; internal float Weight; }

        private readonly struct Contribution<T> where T : unmanaged
        {
            internal Contribution(uint accessOrdinal, T value)
            { AccessOrdinal = accessOrdinal; Value = value; }
            internal uint AccessOrdinal { get; }
            internal T Value { get; }
        }

        private sealed class UpdateHarness : IDisposable
        {
            private readonly Dictionary<ulong, NativeSharedContextTests.BoundTree> _trees;
            private readonly ulong[] _treeIds;
            private readonly uint _perTreeRecordCapacity;
            private readonly uint _perTreePayloadCapacity;
            private bool _selectionEnded;

            private UpdateHarness(
                NativeSharedContextTests.SharedScenario scenario,
                NativeExecuteSelectionWindowOwnerV1 selectionOwner,
                NativeExecuteSelectionWindowV1 selection,
                NativeSharedUpdateWindowV1 update,
                Dictionary<ulong, NativeSharedContextTests.BoundTree> trees,
                ulong[] treeIds,
                uint perTreeRecordCapacity,
                uint perTreePayloadCapacity)
            {
                Scenario = scenario; SelectionOwner = selectionOwner; Selection = selection; Update = update;
                _trees = trees; _treeIds = treeIds; _perTreeRecordCapacity = perTreeRecordCapacity;
                _perTreePayloadCapacity = perTreePayloadCapacity;
            }

            internal NativeSharedContextTests.SharedScenario Scenario { get; }
            internal NativeExecuteSelectionWindowOwnerV1 SelectionOwner { get; }
            internal NativeExecuteSelectionWindowV1 Selection { get; }
            internal NativeSharedUpdateWindowV1 Update { get; }

            internal static UpdateHarness Create(
                NativeProgramBlackboardBindingV2 binding,
                ulong[] treeIds,
                uint perTreeRecordCapacity,
                uint perTreePayloadCapacity)
            {
                var scenario = NativeSharedContextTests.SharedScenario.Create(
                    binding, (uint)treeIds.Length, (uint)treeIds.Length,
                    checked((uint)treeIds.Length * perTreeRecordCapacity),
                    checked((uint)treeIds.Length * perTreePayloadCapacity));
                var trees = new Dictionary<ulong, NativeSharedContextTests.BoundTree>();
                var sorted = (ulong[])treeIds.Clone();
                Array.Sort(sorted);
                for (var index = 0; index < sorted.Length; index++) trees.Add(sorted[index], scenario.Bind(sorted[index]));
                Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                    new NativeExecuteSelectionCapacityV1((uint)treeIds.Length, 1), Allocator.Persistent,
                    out var selectionOwner, out var failure), Is.True, failure.Code.ToString());
                var entries = new NativeArray<NativeExecuteSelectionEntryV1>(sorted.Length, Allocator.Temp);
                try
                {
                    for (var index = 0; index < sorted.Length; index++)
                        entries[index] = new NativeExecuteSelectionEntryV1(
                            new TreeInstanceId(sorted[index]), perTreeRecordCapacity, perTreePayloadCapacity);
                    Assert.That(selectionOwner.TryBegin(entries, out var selection, out failure), Is.True, failure.Code.ToString());
                    Assert.That(scenario.Context.TryBeginUpdate(
                        selectionOwner, selection, out var update, out failure), Is.True, failure.Code.ToString());
                    return new UpdateHarness(
                        scenario, selectionOwner, selection, update, trees, sorted,
                        perTreeRecordCapacity, perTreePayloadCapacity);
                }
                finally { entries.Dispose(); }
            }

            internal void Append<T>(ulong treeId, Contribution<T>[] contributions) where T : unmanaged
            {
                var tree = _trees[treeId];
                Assert.That(tree.Instance.TryAcquireExecutionLeaseV2(
                    Scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                Assert.That(Scenario.Context.TryAcquireContributionStream(
                    Update, tree.Binding, execution, out var stream, out failure), Is.True, failure.Code.ToString());
                var candidate = new NativeArray<T>(1, Allocator.TempJob);
                try
                {
                    for (var index = 0; index < contributions.Length; index++)
                    {
                        var contribution = contributions[index];
                        candidate[0] = contribution.Value;
                        var access = Scenario.ProgramLease.View.Accesses[(int)contribution.AccessOrdinal];
                        var type = access.RegisteredTypeIndex == CompiledIndex.Invalid
                            ? NativeBlackboardTypeIdV2.BuiltIn(
                                BuiltInDescriptor(Scenario.ProgramLease.View.Slots[(int)access.ResolvedSlotIndex].TypeId),
                                access.EnumContractId)
                            : NativeBlackboardTypeIdV2.Registered(
                                access.RegisteredTypeIndex,
                                Scenario.ProgramLease.View.RegisteredTypes[(int)access.RegisteredTypeIndex]);
                        Assert.That(NativeSharedBlackboardV1.TryContribute(
                            stream.Writer, 0, contribution.AccessOrdinal, type, candidate),
                            Is.EqualTo(BurstContextResult.Success));
                    }
                }
                finally { candidate.Dispose(); }
                Assert.That(Scenario.Context.TryRegisterDependency(stream, default, out failure), Is.True, failure.Code.ToString());
                Assert.That(Scenario.Context.TrySealContributionStream(stream, out _, out failure), Is.True, failure.Code.ToString());
                Assert.That(tree.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
            }

            internal void Cancel(ulong treeId)
            {
                var tree = _trees[treeId];
                Assert.That(tree.Instance.TryAcquireExecutionLeaseV2(
                    Scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                Assert.That(Scenario.Context.TryAcquireContributionStream(
                    Update, tree.Binding, execution, out var stream, out failure), Is.True, failure.Code.ToString());
                Assert.That(Scenario.Context.TryCancelContributionStream(stream, out failure), Is.True, failure.Code.ToString());
                Assert.That(tree.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
            }

            internal bool Reduce(out NativeSharedCommitReportV1 report, out NativeRuntimeFailureV1 failure)
            {
                var success = Scenario.Context.TryReduceUpdate(Update, out report, out failure);
                EndSelection();
                return success;
            }

            internal void EndSelection()
            {
                if (_selectionEnded) return;
                Assert.That(SelectionOwner.TryEnd(Selection, out var failure), Is.True, failure.Code.ToString());
                _selectionEnded = true;
            }

            internal void BeginAndAbortNextUpdate()
            {
                var entries = new NativeArray<NativeExecuteSelectionEntryV1>(_treeIds.Length, Allocator.Temp);
                try
                {
                    for (var index = 0; index < _treeIds.Length; index++)
                        entries[index] = new NativeExecuteSelectionEntryV1(
                            new TreeInstanceId(_treeIds[index]), _perTreeRecordCapacity, _perTreePayloadCapacity);
                    Assert.That(SelectionOwner.TryBegin(entries, out var selection, out var failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(Scenario.Context.TryBeginUpdate(
                        SelectionOwner, selection, out var update, out failure), Is.True, failure.Code.ToString());
                    Assert.That(Scenario.Context.TryAbortUpdate(update, out failure), Is.True, failure.Code.ToString());
                    Assert.That(SelectionOwner.TryEnd(selection, out failure), Is.True, failure.Code.ToString());
                }
                finally { entries.Dispose(); }
            }

            internal bool TryReduceNextCanceledUpdate(
                out NativeSharedCommitReportV1 report,
                out NativeRuntimeFailureV1 failure)
            {
                report = default;
                failure = default;
                var entries = new NativeArray<NativeExecuteSelectionEntryV1>(_treeIds.Length, Allocator.Temp);
                try
                {
                    for (var index = 0; index < _treeIds.Length; index++)
                        entries[index] = new NativeExecuteSelectionEntryV1(
                            new TreeInstanceId(_treeIds[index]), _perTreeRecordCapacity, _perTreePayloadCapacity);
                    if (!SelectionOwner.TryBegin(entries, out var selection, out failure)) return false;
                    if (!Scenario.Context.TryBeginUpdate(
                            SelectionOwner, selection, out var update, out failure)) return false;
                    var tree = _trees[_treeIds[0]];
                    if (!tree.Instance.TryAcquireExecutionLeaseV2(
                            Scenario.ProgramLease, out var execution, out failure)) return false;
                    if (!Scenario.Context.TryAcquireContributionStream(
                            update, tree.Binding, execution, out var stream, out failure)) return false;
                    if (!Scenario.Context.TryCancelContributionStream(stream, out failure)) return false;
                    if (!tree.Instance.TryReleaseExecutionLease(execution, out failure)) return false;
                    var success = Scenario.Context.TryReduceUpdate(update, out report, out failure);
                    if (!success)
                    {
                        var reductionFailure = failure;
                        if (!Scenario.Context.TryAbortUpdate(update, out _)) return false;
                        failure = reductionFailure;
                    }
                    if (!SelectionOwner.TryEnd(selection, out var closeFailure))
                    { failure = closeFailure; return false; }
                    return success;
                }
                finally { entries.Dispose(); }
            }

            public void Dispose()
            {
                EndSelection();
                Assert.That(SelectionOwner.TryDispose(out var failure), Is.True, failure.Code.ToString());
                Scenario.Dispose();
            }

            private static BlackboardTypeDescriptor BuiltInDescriptor(ulong typeId)
            {
                foreach (var descriptor in new[]
                {
                    BuiltInBlackboardTypes.Bool, BuiltInBlackboardTypes.Int32, BuiltInBlackboardTypes.Int64,
                    BuiltInBlackboardTypes.Float32, BuiltInBlackboardTypes.Float64,
                    BuiltInBlackboardTypes.FixedString32, BuiltInBlackboardTypes.Enum32,
                }) if (descriptor.TypeId == typeId) return descriptor;
                throw new InvalidOperationException("Unexpected reduction test type.");
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
        private struct ReduceJob : IJob
        {
            internal NativeSharedReductionViewV1 View;
            internal NativeArray<BurstContextResult> Result;
            public void Execute() => Result[0] = NativeSharedReductionV1.TryReduce(View);
        }
    }
}
