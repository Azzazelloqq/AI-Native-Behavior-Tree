using System;
using System.Reflection;
using AIBT.Burst;
using AIBT.Tests.Runtime.NativeExecution.Blackboard.TreeAndAgent;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.NativeExecution.Blackboard.Shared
{
    public sealed class NativeSharedContributionTests
    {
        [Test]
        public void ReducePublishesOneAtomicRevisionAndScopeOrderedCommitReport()
        {
            using (var fixture = ContributionFixture.Create(2, 8))
            using (var candidate = One(7))
            {
                fixture.Begin();
                fixture.Acquire(out var execution, out var contribution);
                Assert.That(NativeSharedBlackboardV1.TryContribute(
                    contribution.Writer, 0, 0,
                    NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32), candidate),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(fixture.Scenario.Context.TryRegisterDependency(
                    contribution, default, out var failure), Is.True, failure.Code.ToString());
                Assert.That(fixture.Scenario.Context.TrySealContributionStream(
                    contribution, out _, out failure), Is.True, failure.Code.ToString());
                Assert.That(fixture.Tree.Instance.TryReleaseExecutionLease(execution, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(fixture.Scenario.Context.TryReduceUpdate(
                    fixture.Update, out NativeSharedCommitReportV1 report, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(report.ChangedSlotCount, Is.EqualTo(1));
                Assert.That(report.ChangedScopeSlots[0], Is.Zero);
                Assert.That(report.EligibleUpdateId, Is.EqualTo(fixture.Update.UpdateId + 1));
            }
        }

        [Test]
        public void AppendCopiesCanonicalPayloadAndCommitsSequenceOnlyAfterRecordCommit()
        {
            using (var fixture = ContributionFixture.Create(2, 8))
            {
                fixture.Begin();
                fixture.Acquire(out var execution, out var contribution);
                var candidate = new NativeArray<int>(1, Allocator.TempJob);
                try
                {
                    candidate[0] = 7;
                    Assert.That(NativeSharedBlackboardV1.TryContribute(
                        contribution.Writer, 0, 0,
                        NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32), candidate),
                        Is.EqualTo(BurstContextResult.Success));
                    candidate[0] = 99;
                    Assert.That(fixture.Scenario.Context.TryRegisterDependency(
                        contribution, default, out var failure), Is.True, failure.Code.ToString());
                    Assert.That(fixture.Scenario.Context.TrySealContributionStream(
                        contribution, out var stream, out failure), Is.True, failure.Code.ToString());
                    Assert.That(stream.Stream.IsValid, Is.True);
                    Assert.That(stream.Stream.Count, Is.EqualTo(1));
                    Assert.That(stream.Stream.ContributionSequence, Is.EqualTo(1));
                    var record = stream.Records[0];
                    Assert.That(record.TreeInstanceId, Is.EqualTo(new TreeInstanceId(2)));
                    Assert.That(record.Sequence, Is.Zero);
                    Assert.That(record.RecordCapacity, Is.EqualTo(2));
                    Assert.That(record.PayloadCapacity, Is.EqualTo(8));
                    Assert.That(record.TypeId, Is.EqualTo(BuiltInBlackboardTypes.Int32.TypeId));
                    Assert.That(ReadI32(stream.Payload, record.PayloadOffset), Is.EqualTo(7));
                    Assert.That(fixture.Tree.Instance.TryReleaseExecutionLease(execution, out failure),
                        Is.True, failure.Code.ToString());
                }
                finally { candidate.Dispose(); }
                fixture.Abort();
                using (var stale = One(11))
                    Assert.That(NativeSharedBlackboardV1.TryContribute(
                        contribution.Writer, 0, 0,
                        NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32), stale),
                        Is.EqualTo(BurstContextResult.PhaseViolation));
            }
        }

        [Test]
        public void CapacityFaultPermanentlyInvalidatesStreamAndCancelAcceptsOnlyValidEmptyStream()
        {
            using (var fixture = ContributionFixture.Create(1, 4))
            using (var candidate = One(7))
            {
                fixture.Begin();
                fixture.Acquire(out var execution, out var contribution);
                Assert.That(NativeSharedBlackboardV1.TryContribute(
                    contribution.Writer, 0, 0,
                    NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32), candidate),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(NativeSharedBlackboardV1.TryContribute(
                    contribution.Writer, 0, 0,
                    NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32), candidate),
                    Is.EqualTo(BurstContextResult.CapacityExceeded));
                Assert.That(NativeSharedBlackboardV1.TryContribute(
                    contribution.Writer, 0, 0,
                    NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32), candidate),
                    Is.EqualTo(BurstContextResult.PhaseViolation));
                Assert.That(fixture.Scenario.Context.TryRegisterDependency(
                    contribution, default, out var failure), Is.True, failure.Code.ToString());
                Assert.That(fixture.Scenario.Context.TrySealContributionStream(
                    contribution, out var invalid, out failure), Is.True, failure.Code.ToString());
                Assert.That(invalid.Stream.IsValid, Is.False);
                Assert.That(invalid.Stream.Count, Is.EqualTo(1));
                Assert.That(fixture.Tree.Instance.TryReleaseExecutionLease(execution, out failure),
                    Is.True, failure.Code.ToString());
                fixture.Abort();

                fixture.Begin();
                fixture.Acquire(out execution, out contribution);
                Assert.That(fixture.Scenario.Context.TryCancelContributionStream(
                    contribution, out failure), Is.True, failure.Code.ToString());
                Assert.That(fixture.Scenario.Context.TryCancelContributionStream(
                    contribution, out failure), Is.False);
                Assert.That(fixture.Tree.Instance.TryReleaseExecutionLease(execution, out failure),
                    Is.True, failure.Code.ToString());
                fixture.Abort();
            }
        }

        [Test]
        public void CanonicalAppendNormalizesNegativeZeroAndMalformedValueInvalidatesStream()
        {
            using (var fixture = ContributionFixture.CreateFloat(2, 8))
            {
                var candidate = new NativeArray<float>(1, Allocator.TempJob);
                try
                {
                    fixture.Begin();
                    fixture.Acquire(out var execution, out var contribution);
                    candidate[0] = -0.0f;
                    Assert.That(NativeSharedBlackboardV1.TryContribute(
                        contribution.Writer, 0, 0,
                        NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32), candidate),
                        Is.EqualTo(BurstContextResult.Success));
                    Assert.That(fixture.Scenario.Context.TryRegisterDependency(
                        contribution, default, out var failure), Is.True, failure.Code.ToString());
                    Assert.That(fixture.Scenario.Context.TrySealContributionStream(
                        contribution, out var stream, out failure), Is.True, failure.Code.ToString());
                    var record = stream.Records[0];
                    Assert.That(ReadU32(stream.Payload, record.PayloadOffset), Is.Zero,
                        "accepted Float32 -0 must be stored as canonical +0");
                    Assert.That(fixture.Tree.Instance.TryReleaseExecutionLease(execution, out failure),
                        Is.True, failure.Code.ToString());
                    fixture.Abort();

                    fixture.Begin();
                    fixture.Acquire(out execution, out contribution);
                    candidate[0] = float.NaN;
                    Assert.That(NativeSharedBlackboardV1.TryContribute(
                        contribution.Writer, 0, 0,
                        NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32), candidate),
                        Is.EqualTo(BurstContextResult.InvalidEncoding));
                    Assert.That(NativeSharedBlackboardV1.TryContribute(
                        contribution.Writer, 0, 0,
                        NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32), candidate),
                        Is.EqualTo(BurstContextResult.PhaseViolation));
                    Assert.That(fixture.Scenario.Context.TryRegisterDependency(
                        contribution, default, out failure), Is.True, failure.Code.ToString());
                    Assert.That(fixture.Scenario.Context.TrySealContributionStream(
                        contribution, out stream, out failure), Is.True, failure.Code.ToString());
                    Assert.That(stream.Stream.IsValid, Is.False);
                    Assert.That(stream.Stream.Count, Is.Zero);
                    Assert.That(fixture.Tree.Instance.TryReleaseExecutionLease(execution, out failure),
                        Is.True, failure.Code.ToString());
                    fixture.Abort();
                }
                finally { candidate.Dispose(); }
            }
        }

        [Test]
        public void SequenceOverflowAndCopiedOrForeignLeaseAreRejectedWithoutCommit()
        {
            using (var fixture = ContributionFixture.Create(1, 4))
            using (var foreign = NativeSharedContextTests.SharedScenario.Create(
                NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                    BuiltInBlackboardTypes.Int32, 5, NativeBlackboardReductionKindV2.Sum)))
            using (var candidate = One(7))
            {
                fixture.Begin();
                fixture.Acquire(out var execution, out var contribution);
                SetActiveSequence(fixture.Scenario.Context, ulong.MaxValue);
                Assert.That(NativeSharedBlackboardV1.TryContribute(
                    contribution.Writer, 0, 0,
                    NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32), candidate),
                    Is.EqualTo(BurstContextResult.Overflow));
                var copied = contribution;
                Assert.That(fixture.Scenario.Context.TryRegisterDependency(
                    contribution, default, out var failure), Is.True, failure.Code.ToString());
                Assert.That(fixture.Scenario.Context.TrySealContributionStream(
                    contribution, out var stream, out failure), Is.True, failure.Code.ToString());
                Assert.That(stream.Stream.IsValid, Is.False);
                Assert.That(stream.Stream.Count, Is.Zero);
                Assert.That(stream.Stream.ContributionSequence, Is.EqualTo(ulong.MaxValue));
                Assert.That(fixture.Scenario.Context.TrySealContributionStream(
                    copied, out _, out failure), Is.False);
                Assert.That(foreign.Context.TryCancelContributionStream(copied, out failure), Is.False);
                Assert.That(fixture.Tree.Instance.TryReleaseExecutionLease(execution, out failure),
                    Is.True, failure.Code.ToString());
                fixture.Abort();
            }
        }

        [Test]
        public void BeginRejectsPerTreeAndAggregateReservationsAtomicallyAndReleasesSelectionReader()
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                BuiltInBlackboardTypes.Int32, 5, NativeBlackboardReductionKindV2.Sum);
            using (var scenario = NativeSharedContextTests.SharedScenario.Create(binding, 2, 2, 2, 8))
            using (var first = scenario.Bind(2))
            using (var second = scenario.Bind(4))
            {
                AssertBeginRejected(scenario, new[]
                {
                    new NativeExecuteSelectionEntryV1(new TreeInstanceId(2), 0, 0),
                }, NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.SharedContributionStreams);
                AssertBeginRejected(scenario, new[]
                {
                    new NativeExecuteSelectionEntryV1(new TreeInstanceId(2), 2, 8),
                    new NativeExecuteSelectionEntryV1(new TreeInstanceId(4), 1, 4),
                }, NativeRuntimeDiagnosticCodeV1.NativeOutputCapacityExceeded,
                    NativeResourceKindV1.SharedContributionRecords);
            }
        }

        [Test]
        public void BurstAppendDependencyMustCompleteBeforeSealAndAbortPublishesNothing()
        {
            using (var fixture = ContributionFixture.Create(2, 8))
            {
                fixture.Begin();
                fixture.Acquire(out var execution, out var contribution);
                var candidate = new NativeArray<int>(1, Allocator.TempJob);
                var result = new NativeArray<BurstContextResult>(1, Allocator.TempJob);
                try
                {
                    candidate[0] = 9;
                    var handle = new AppendJob
                    {
                        Writer = contribution.Writer,
                        Candidate = candidate,
                        ExpectedType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32),
                        Result = result,
                    }.Schedule();
                    Assert.That(fixture.Scenario.Context.TryRegisterDependency(
                        contribution, handle, out var failure), Is.True, failure.Code.ToString());
                    handle.Complete();
                    Assert.That(result[0], Is.EqualTo(BurstContextResult.Success));
                    Assert.That(fixture.Scenario.Context.TrySealContributionStream(
                        contribution, out var stream, out failure), Is.True, failure.Code.ToString());
                    Assert.That(stream.Stream.Count, Is.EqualTo(1));
                    Assert.That(fixture.Tree.Instance.TryReleaseExecutionLease(execution, out failure),
                        Is.True, failure.Code.ToString());
                }
                finally { result.Dispose(); candidate.Dispose(); }
                fixture.Abort();
                Assert.That(GetArray<byte>(fixture.Scenario.Context, "_values")[0], Is.EqualTo(5));
                Assert.That(GetArray<ulong>(fixture.Scenario.Context, "_versions")[0], Is.Zero);
                Assert.That(GetArray<ulong>(fixture.Scenario.Context, "_revision")[0], Is.Zero);
            }
        }

        [Test]
        public void AgentAndSharedUseTheSameExclusiveTreeExecutionLease()
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateAgentSharedReductionBinding();
            using (var scenario = NativeSharedContextTests.SharedScenario.Create(binding, 1, 1, 2, 8))
            using (var tree = scenario.Bind(2))
            {
                Assert.That(NativeAgentContextCapacityV1.TryDerive(
                    scenario.ProgramLease.View, 1, out var agentCapacity, out var failure),
                    Is.True, failure.Code.ToString());
                Assert.That(NativeAgentContextRegistryV1.TryCreate(
                    1, Allocator.Persistent, out var registry, out failure), Is.True, failure.Code.ToString());
                Assert.That(registry.TryCreateContext(
                    new AgentId(1), scenario.ProgramLease, agentCapacity,
                    out var agent, out failure), Is.True, failure.Code.ToString());
                Assert.That(agent.TryBind(
                    new TreeInstanceId(2), scenario.ProgramLease, tree.Instance,
                    out var agentBinding, out failure), Is.True, failure.Code.ToString());
                Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                    new NativeExecuteSelectionCapacityV1(1, 2), Allocator.Persistent,
                    out var selectionOwner, out failure), Is.True, failure.Code.ToString());
                var entries = new NativeArray<NativeExecuteSelectionEntryV1>(1, Allocator.Temp);
                try
                {
                    entries[0] = new NativeExecuteSelectionEntryV1(new TreeInstanceId(2), 2, 8);
                    Assert.That(selectionOwner.TryBegin(entries, out var selection, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(agent.TryBeginExecuteWindow(
                        selectionOwner, selection, out var agentWindow, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(scenario.Context.TryBeginUpdate(
                        selectionOwner, selection, out var sharedUpdate, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(agent.TryAcquireNext(agentWindow, out var agentLease, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(scenario.Context.TryAcquireContributionStream(
                        sharedUpdate, tree.Binding, agentLease,
                        out var contribution, out failure), Is.True, failure.Code.ToString());
                    Assert.That(scenario.Context.TryAcquireContributionStream(
                        sharedUpdate, tree.Binding, agentLease.TreeLease,
                        out _, out failure), Is.False, "the same Shared stream is exclusive");
                    Assert.That(scenario.Context.TryCancelContributionStream(contribution, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(agent.TryReleaseExecuteLease(agentLease, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(agent.TryEndExecuteWindow(agentWindow, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(scenario.Context.TryAbortUpdate(sharedUpdate, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(selectionOwner.TryEnd(selection, out failure), Is.True, failure.Code.ToString());
                }
                finally
                {
                    entries.Dispose();
                    Assert.That(selectionOwner.TryDispose(out failure), Is.True, failure.Code.ToString());
                }
                Assert.That(agent.TryUnbind(agentBinding, out failure), Is.True, failure.Code.ToString());
                Assert.That(registry.TryDestroyContext(agent, out failure), Is.True, failure.Code.ToString());
                Assert.That(registry.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void InitializedContributionLifecycleAllocatesNoManagedMemory()
        {
            using (var fixture = ContributionFixture.Create(1, 4))
            using (var candidate = One(7))
            {
                RunOne(fixture, candidate);
                var before = GC.GetAllocatedBytesForCurrentThread();
                var success = true;
                for (var index = 0; index < 32; index++) success &= RunOne(fixture, candidate);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(success, Is.True);
                Assert.That(allocated, Is.Zero);
            }
        }

        private static bool RunOne(ContributionFixture fixture, NativeArray<int> candidate)
        {
            if (!fixture.TryBegin()) return false;
            if (!fixture.TryAcquire(out var execution, out var contribution)) return false;
            if (NativeSharedBlackboardV1.TryContribute(
                contribution.Writer, 0, 0,
                NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32), candidate)
                != BurstContextResult.Success) return false;
            if (!fixture.Scenario.Context.TryRegisterDependency(contribution, default, out _)) return false;
            if (!fixture.Scenario.Context.TrySealContributionStream(contribution, out _, out _)) return false;
            if (!fixture.Tree.Instance.TryReleaseExecutionLease(execution, out _)) return false;
            return fixture.TryAbort();
        }

        private static void AssertBeginRejected(
            NativeSharedContextTests.SharedScenario scenario,
            NativeExecuteSelectionEntryV1[] source,
            NativeRuntimeDiagnosticCodeV1 code,
            NativeResourceKindV1 resource)
        {
            Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                new NativeExecuteSelectionCapacityV1((uint)source.Length, 1), Allocator.Persistent,
                out var owner, out var failure), Is.True, failure.Code.ToString());
            var entries = new NativeArray<NativeExecuteSelectionEntryV1>(source.Length, Allocator.Temp);
            try
            {
                for (var index = 0; index < source.Length; index++) entries[index] = source[index];
                Assert.That(owner.TryBegin(entries, out var selection, out failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryBeginUpdate(owner, selection, out _, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(code));
                Assert.That(failure.ResourceKind, Is.EqualTo(resource));
                Assert.That(owner.TryAbort(selection, out failure), Is.True,
                    "failed Shared Begin must release its neutral reader");
            }
            finally
            {
                entries.Dispose();
                Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        private static int ReadI32(NativeArray<byte>.ReadOnly bytes, uint offset)
            => bytes[(int)offset] | bytes[(int)offset + 1] << 8
                | bytes[(int)offset + 2] << 16 | bytes[(int)offset + 3] << 24;

        private static uint ReadU32(NativeArray<byte>.ReadOnly bytes, uint offset)
            => (uint)ReadI32(bytes, offset);

        private static void SetActiveSequence(NativeSharedContextOwnerV1 context, ulong value)
        {
            var streams = GetArray<NativeSharedContributionStreamV1>(context, "_streamHeaders");
            object boxed = streams[0];
            typeof(NativeSharedContributionStreamV1).GetField(
                "NextSequence", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(boxed, value);
            streams[0] = (NativeSharedContributionStreamV1)boxed;
        }

        private static NativeArray<T> GetArray<T>(object owner, string name) where T : struct
            => (NativeArray<T>)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);

        private static NativeArray<int> One(int value)
        {
            var result = new NativeArray<int>(1, Allocator.TempJob);
            result[0] = value;
            return result;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct AppendJob : IJob
        {
            internal NativeSharedContributionWriterV1 Writer;
            [ReadOnly] internal NativeArray<int> Candidate;
            internal NativeBlackboardTypeIdV2 ExpectedType;
            internal NativeArray<BurstContextResult> Result;

            public void Execute()
            {
                Result[0] = NativeSharedBlackboardV1.TryContribute(
                    Writer, 0, 0, ExpectedType, Candidate);
            }
        }

        private sealed class ContributionFixture : IDisposable
        {
            private ContributionFixture() { }
            internal NativeSharedContextTests.SharedScenario Scenario;
            internal NativeSharedContextTests.BoundTree Tree;
            internal NativeExecuteSelectionWindowOwnerV1 SelectionOwner;
            internal NativeExecuteSelectionWindowV1 Selection;
            internal NativeSharedUpdateWindowV1 Update;
            internal uint RecordCapacity;
            internal uint PayloadCapacity;

            internal static ContributionFixture Create(uint records, uint payload)
            {
                var binding = NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                    BuiltInBlackboardTypes.Int32, 5, NativeBlackboardReductionKindV2.Sum);
                return Create(binding, records, payload);
            }

            internal static ContributionFixture CreateFloat(uint records, uint payload)
            {
                var binding = NativeBlackboardProgramBindingTests.Fixture.CreateSharedReductionBinding(
                    BuiltInBlackboardTypes.Float32, 1f, NativeBlackboardReductionKindV2.Sum);
                return Create(binding, records, payload);
            }

            private static ContributionFixture Create(
                NativeProgramBlackboardBindingV2 binding, uint records, uint payload)
            {
                var scenario = NativeSharedContextTests.SharedScenario.Create(binding, 1, 1, records, payload);
                return new ContributionFixture
                {
                    Scenario = scenario,
                    Tree = scenario.Bind(2),
                    RecordCapacity = records,
                    PayloadCapacity = payload,
                };
            }

            internal void Begin() => Assert.That(TryBegin(), Is.True);
            internal bool TryBegin()
            {
                if (!NativeExecuteSelectionWindowOwnerV1.TryCreate(
                    new NativeExecuteSelectionCapacityV1(1, 1), Allocator.Persistent,
                    out SelectionOwner, out _)) return false;
                var entries = new NativeArray<NativeExecuteSelectionEntryV1>(1, Allocator.Temp);
                try
                {
                    entries[0] = new NativeExecuteSelectionEntryV1(
                        new TreeInstanceId(2), RecordCapacity, PayloadCapacity);
                    if (!SelectionOwner.TryBegin(entries, out Selection, out _)) return false;
                    return Scenario.Context.TryBeginUpdate(
                        SelectionOwner, Selection, out Update, out _);
                }
                finally { entries.Dispose(); }
            }

            internal void Acquire(
                out NativeInstanceExecutionLeaseV2 execution,
                out NativeSharedContributionLeaseV1 contribution)
                => Assert.That(TryAcquire(out execution, out contribution), Is.True);

            internal bool TryAcquire(
                out NativeInstanceExecutionLeaseV2 execution,
                out NativeSharedContributionLeaseV1 contribution)
            {
                if (!Tree.Instance.TryAcquireExecutionLeaseV2(
                    Scenario.ProgramLease, out execution, out _))
                { contribution = default; return false; }
                return Scenario.Context.TryAcquireContributionStream(
                    Update, Tree.Binding, execution, out contribution, out _);
            }

            internal void Abort() => Assert.That(TryAbort(), Is.True);
            internal bool TryAbort()
            {
                if (!Scenario.Context.TryAbortUpdate(Update, out _)) return false;
                if (!SelectionOwner.TryAbort(Selection, out _)) return false;
                if (!SelectionOwner.TryDispose(out _)) return false;
                SelectionOwner = null; Selection = default; Update = default;
                return true;
            }

            public void Dispose()
            {
                if (SelectionOwner != null)
                {
                    Scenario.Context.TryAbortUpdate(Update, out _);
                    SelectionOwner.TryAbort(Selection, out _);
                    SelectionOwner.TryDispose(out _);
                }
                Tree.Dispose();
                Scenario.Dispose();
            }
        }
    }
}
