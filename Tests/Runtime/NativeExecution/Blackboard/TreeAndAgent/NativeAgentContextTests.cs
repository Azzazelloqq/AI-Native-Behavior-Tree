using NUnit.Framework;
using System;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.NativeExecution.Blackboard.TreeAndAgent
{
    public sealed class NativeAgentContextTests
    {
        [Test]
        public void RegistryRejectsDuplicateAgentAndContextBindsCompatibleTreesOnly()
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                BuiltInBlackboardTypes.Int32, 5, BlackboardScope.Agent);
            Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                out var program, out var failure), Is.True, failure.Code.ToString());
            Assert.That(program.TryAcquireReadLeaseV2(out var programLease, out failure), Is.True, failure.Code.ToString());
            Assert.That(NativeAgentContextCapacityV1.TryDerive(
                programLease.View, 2, out var capacity, out failure), Is.True, failure.Code.ToString());
            Assert.That(NativeAgentContextRegistryV1.TryCreate(2, Allocator.Persistent, out var registry, out failure),
                Is.True, failure.Code.ToString());
            try
            {
                Assert.That(registry.TryCreateContext(new AgentId(7), programLease, capacity, out var context, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(registry.TryCreateContext(new AgentId(7), programLease, capacity, out _, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch));

                using (var first = TreeScenario.Create(programLease))
                using (var second = TreeScenario.Create(programLease))
                {
                    Assert.That(context.TryBind(
                        new TreeInstanceId(3), programLease, first.Instance, out var firstBinding, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(context.TryBind(
                        new TreeInstanceId(9), programLease, second.Instance, out var secondBinding, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(context.TryUnbind(secondBinding, out failure), Is.True, failure.Code.ToString());
                    Assert.That(context.TryUnbind(firstBinding, out failure), Is.True, failure.Code.ToString());
                }
                Assert.That(registry.TryDestroyContext(context, out failure), Is.True, failure.Code.ToString());
            }
            finally
            {
                Assert.That(registry.TryDispose(out failure), Is.True, failure.Code.ToString());
                Assert.That(program.TryReleaseReadLease(programLease, out failure), Is.True, failure.Code.ToString());
                Assert.That(program.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void ExecuteWindowValidatesAndCopiesWholeAscendingEligibleListBeforeCursorPublication()
        {
            using (var scenario = AgentScenario.Create(2, new TreeInstanceId(2), new TreeInstanceId(8)))
            using (var reversed = Ids(8, 2))
            using (var valid = Ids(2, 8))
            {
                Assert.That(scenario.Context.TryBeginExecuteWindow(reversed, out _, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch));
                Assert.That(scenario.Context.TryBeginExecuteWindow(valid, out var window, out failure),
                    Is.True, failure.Code.ToString());

                SetFirst(valid, new TreeInstanceId(99)); // Begin owns an atomic copy.
                Assert.That(scenario.Context.TryAcquireNext(window, out var first, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(first.TreeInstanceId, Is.EqualTo(new TreeInstanceId(2)));
                Assert.That(scenario.Context.TryReleaseExecuteLease(first, out failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryAcquireNext(window, out var second, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(second.TreeInstanceId, Is.EqualTo(new TreeInstanceId(8)));
                Assert.That(scenario.Context.TryReleaseExecuteLease(second, out failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryEndExecuteWindow(window, out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void ExecuteWindowV2FiltersOneInterleavedSelectionAuthorityInExactOrder()
        {
            using (var firstAgent = AgentScenario.Create(2, new TreeInstanceId(2), new TreeInstanceId(8)))
            using (var secondAgent = AgentScenario.Create(2, new TreeInstanceId(4), new TreeInstanceId(10)))
            using (var entries = SelectionEntries(2, 4, 8, 10))
            {
                Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                    new NativeExecuteSelectionCapacityV1(4, 2), Allocator.Persistent,
                    out var selectionOwner, out var failure), Is.True, failure.Code.ToString());
                try
                {
                    Assert.That(selectionOwner.TryBegin(entries, out var selection, out failure), Is.True);
                    Assert.That(firstAgent.Context.TryBeginExecuteWindow(
                        selectionOwner, selection, out var firstWindow, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(secondAgent.Context.TryBeginExecuteWindow(
                        selectionOwner, selection, out var secondWindow, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(selectionOwner.TryEnd(selection, out failure), Is.False);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));

                    AssertV2Order(firstAgent.Context, firstWindow, 2, 8);
                    AssertV2Order(secondAgent.Context, secondWindow, 4, 10);
                    Assert.That(selectionOwner.TryEnd(selection, out failure), Is.True, failure.Code.ToString());
                }
                finally
                {
                    Assert.That(selectionOwner.TryDispose(out failure), Is.True, failure.Code.ToString());
                }
            }
        }

        [Test]
        public void ExecuteWindowV2RollbackMutualExclusionAbortAndStaleTokensAreExplicit()
        {
            using (var scenario = AgentScenario.Create(1, new TreeInstanceId(2)))
            using (var missing = SelectionEntries(99))
            using (var selected = SelectionEntries(2))
            using (var legacy = Ids(2))
            {
                Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                    new NativeExecuteSelectionCapacityV1(1, 1), Allocator.Persistent,
                    out var selectionOwner, out var failure), Is.True, failure.Code.ToString());
                try
                {
                    Assert.That(selectionOwner.TryBegin(missing, out var missingWindow, out failure), Is.True);
                    Assert.That(scenario.Context.TryBeginExecuteWindow(
                        selectionOwner, missingWindow, out NativeAgentExecuteWindowV2 _, out failure), Is.False);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch));
                    Assert.That(selectionOwner.TryEnd(missingWindow, out failure),
                        Is.True, "failed Agent Begin must release the temporary selection reader");

                    Assert.That(selectionOwner.TryBegin(selected, out var selection, out failure), Is.True);
                    Assert.That(scenario.Context.TryBeginExecuteWindow(legacy, out var legacyWindow, out failure), Is.True);
                    Assert.That(scenario.Context.TryBeginExecuteWindow(
                        selectionOwner, selection, out NativeAgentExecuteWindowV2 _, out failure), Is.False);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
                    Assert.That(scenario.Context.TryAbortExecuteWindow(legacyWindow, out failure), Is.True);

                    Assert.That(scenario.Context.TryBeginExecuteWindow(
                        selectionOwner, selection, out var v2Window, out failure), Is.True);
                    Assert.That(scenario.Context.TryBeginExecuteWindow(legacy, out _, out failure), Is.False);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
                    Assert.That(scenario.Context.TryAbortExecuteWindow(v2Window, out failure), Is.True);
                    Assert.That(scenario.Context.TryAcquireNext(v2Window, out NativeAgentExecuteLeaseV2 _, out failure), Is.False);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                    Assert.That(selectionOwner.TryEnd(selection, out failure), Is.True);
                }
                finally
                {
                    Assert.That(selectionOwner.TryDispose(out failure), Is.True, failure.Code.ToString());
                }
            }
        }

        [Test]
        public void ExecuteWindowV2DependencyBlocksReleaseAndEndUntilCompletion()
        {
            using (var scenario = AgentScenario.Create(1, new TreeInstanceId(2)))
            using (var selected = SelectionEntries(2))
            using (var output = new NativeArray<int>(1, Allocator.TempJob))
            {
                Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                    new NativeExecuteSelectionCapacityV1(1, 1), Allocator.Persistent,
                    out var selectionOwner, out var failure), Is.True, failure.Code.ToString());
                try
                {
                    Assert.That(selectionOwner.TryBegin(selected, out var selection, out failure), Is.True);
                    Assert.That(scenario.Context.TryBeginExecuteWindow(
                        selectionOwner, selection, out var window, out failure), Is.True);
                    Assert.That(scenario.Context.TryAcquireNext(
                        window, out NativeAgentExecuteLeaseV2 lease, out failure), Is.True);
                    var dependency = new BurstProbeJob { Output = output, Iterations = 100000000 }.Schedule();
                    Assert.That(scenario.Context.TryRegisterDependency(lease, dependency, out failure), Is.True);
                    Assert.That(scenario.Context.TryReleaseExecuteLease(lease, out failure), Is.False);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
                    dependency.Complete();
                    Assert.That(scenario.Context.TryReleaseExecuteLease(lease, out failure), Is.True);
                    Assert.That(scenario.Context.TryEndExecuteWindow(window, out failure), Is.True);
                    Assert.That(selectionOwner.TryEnd(selection, out failure), Is.True);
                }
                finally
                {
                    Assert.That(selectionOwner.TryDispose(out failure), Is.True, failure.Code.ToString());
                }
            }
        }

        [Test]
        public void SameAgentSharesChangedStateInOwnerOrder_DifferentAgentIsIsolated_AndResetIsExact()
        {
            using (var same = AgentScenario.Create(2, new TreeInstanceId(2), new TreeInstanceId(8)))
            using (var ids = Ids(2, 8))
            using (var changed = One(9))
            {
                Assert.That(same.Context.TryBeginExecuteWindow(ids, out var window, out var failure), Is.True, failure.Code.ToString());
                Assert.That(same.Context.TryAcquireNext(window, out var first, out failure), Is.True, failure.Code.ToString());
                var type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32);
                Assert.That(NativeAgentBlackboardV1.TryWrite(first, 0, 0, type, changed, out var didChange),
                    Is.EqualTo(AIBT.Burst.BurstContextResult.Success));
                Assert.That(didChange, Is.True);
                Assert.That(same.Context.TryReleaseExecuteLease(first, out failure), Is.True, failure.Code.ToString());
                Assert.That(same.Context.TryAcquireNext(window, out var second, out failure), Is.True, failure.Code.ToString());
                Assert.That(NativeAgentBlackboardV1.TryRead(second, 0, 0, type, out int value, out var version),
                    Is.EqualTo(AIBT.Burst.BurstContextResult.Success));
                Assert.That(value, Is.EqualTo(9));
                Assert.That(version, Is.EqualTo(1));
                Assert.That(same.Context.TryReleaseExecuteLease(second, out failure), Is.True, failure.Code.ToString());
                Assert.That(same.Context.TryEndExecuteWindow(window, out failure), Is.True, failure.Code.ToString());
                Assert.That(same.Context.TryReset(out failure), Is.True, failure.Code.ToString());

                using (var one = Ids(2))
                {
                    Assert.That(same.Context.TryBeginExecuteWindow(one, out window, out failure), Is.True, failure.Code.ToString());
                    Assert.That(same.Context.TryAcquireNext(window, out first, out failure), Is.True, failure.Code.ToString());
                    Assert.That(NativeAgentBlackboardV1.TryRead(first, 0, 0, type, out value, out version),
                        Is.EqualTo(AIBT.Burst.BurstContextResult.Success));
                    Assert.That(value, Is.EqualTo(5));
                    Assert.That(version, Is.EqualTo(2));
                    Assert.That(same.Context.TryReleaseExecuteLease(first, out failure), Is.True, failure.Code.ToString());
                    Assert.That(same.Context.TryEndExecuteWindow(window, out failure), Is.True, failure.Code.ToString());
                }
            }

            using (var isolated = AgentScenario.Create(1, new TreeInstanceId(4)))
            using (var ids = Ids(4))
            {
                Assert.That(isolated.Context.TryBeginExecuteWindow(ids, out var window, out var failure), Is.True, failure.Code.ToString());
                Assert.That(isolated.Context.TryAcquireNext(window, out var lease, out failure), Is.True, failure.Code.ToString());
                var type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32);
                Assert.That(NativeAgentBlackboardV1.TryRead(lease, 0, 0, type, out int value, out _),
                    Is.EqualTo(AIBT.Burst.BurstContextResult.Success));
                Assert.That(value, Is.EqualTo(5));
                Assert.That(isolated.Context.TryReleaseExecuteLease(lease, out failure), Is.True, failure.Code.ToString());
                Assert.That(isolated.Context.TryEndExecuteWindow(window, out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void WindowDependencyCancelAbortAndStaleTokensUseExplicitTransitions()
        {
            using (var scenario = AgentScenario.Create(2, new TreeInstanceId(2), new TreeInstanceId(8)))
            using (var ids = Ids(2, 8))
            using (var output = new NativeArray<int>(1, Allocator.TempJob))
            {
                Assert.That(scenario.Context.TryBeginExecuteWindow(ids, out var window, out var failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryAcquireNext(window, out var lease, out failure), Is.True, failure.Code.ToString());
                var dependency = new BurstProbeJob { Output = output, Iterations = 100000000 }.Schedule();
                Assert.That(scenario.Context.TryRegisterDependency(lease, dependency, out failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryReleaseExecuteLease(lease, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
                dependency.Complete();
                Assert.That(output[0], Is.Not.Zero);
                Assert.That(scenario.Context.TryReleaseExecuteLease(lease, out failure), Is.True, failure.Code.ToString());
                Assert.That(NativeAgentBlackboardV1.TryRead(
                    lease, 0, 0, NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32), out int _, out _),
                    Is.EqualTo(AIBT.Burst.BurstContextResult.PhaseViolation));
                Assert.That(scenario.Context.TryCancelNext(window, new TreeInstanceId(8), out failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryEndExecuteWindow(window, out failure), Is.True, failure.Code.ToString());

                Assert.That(scenario.Context.TryBeginExecuteWindow(ids, out window, out failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryAbortExecuteWindow(window, out failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryAcquireNext(window, out _, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
            }
        }

        [Test]
        public void WarmExecuteWindowReadAndEqualWriteAllocateZeroManagedBytes()
        {
            using (var scenario = AgentScenario.Create(1, new TreeInstanceId(2)))
            using (var ids = Ids(2))
            using (var equal = One(5))
            {
                var type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32);
                RunWarmPath(scenario.Context, ids, type, equal, out var warmResult);
                Assert.That(warmResult, Is.True, "Warm-up must establish the positive path before allocation sampling.");

                Assert.That(
                    () =>
                    {
                        var controlled = new byte[64];
                        GC.KeepAlive(controlled);
                    },
                    GcAllocIs.AllocatingGCMemory(),
                    "Allocation probe must detect a controlled managed allocation.");

                var allSucceeded = true;
                Assert.That(
                    () =>
                    {
                        for (var index = 0; index < 32; index++)
                            RunWarmPath(scenario.Context, ids, type, equal, out allSucceeded);
                    },
                    GcAllocIs.Not.AllocatingGCMemory());
                Assert.That(allSucceeded, Is.True);
            }
        }

        [Test]
        public void InvalidIdsCapacityAndLiveWindowMutationsRejectWithoutChangingLifecycle()
        {
            using (var scenario = AgentScenario.Create(1, new TreeInstanceId(2)))
            using (var zero = Ids(0))
            using (var oversized = Ids(2, 8))
            using (var valid = Ids(2))
            {
                Assert.That(scenario.Context.TryBeginExecuteWindow(zero, out _, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch));
                Assert.That(scenario.Context.TryBeginExecuteWindow(oversized, out _, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded));
                Assert.That(scenario.Context.TryBeginExecuteWindow(valid, out var window, out failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryReset(out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
                Assert.That(scenario.Context.TryUnbind(scenario.Bindings[0], out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
                Assert.That(scenario.Context.TryAbortExecuteWindow(window, out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void BurstJobConsumesPureAgentViewAndPublishesTypedReadWrite()
        {
            using (var scenario = AgentScenario.Create(1, new TreeInstanceId(2)))
            using (var ids = Ids(2))
            using (var candidate = new NativeArray<int>(1, Allocator.TempJob))
            using (var results = new NativeArray<AIBT.Burst.BurstContextResult>(2, Allocator.TempJob))
            using (var values = new NativeArray<int>(1, Allocator.TempJob))
            using (var versions = new NativeArray<ulong>(1, Allocator.TempJob))
            {
                SetFirst(candidate, 12);
                Assert.That(scenario.Context.TryBeginExecuteWindow(ids, out var window, out var failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryAcquireNext(window, out var lease, out failure), Is.True, failure.Code.ToString());
                var job = new AgentAccessJob
                {
                    View = lease.View,
                    Type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32),
                    Candidate = candidate,
                    Results = results,
                    Values = values,
                    Versions = versions,
                }.Schedule();
                Assert.That(scenario.Context.TryRegisterDependency(lease, job, out failure), Is.True, failure.Code.ToString());
                job.Complete();
                Assert.That(scenario.Context.TryReleaseExecuteLease(lease, out failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryEndExecuteWindow(window, out failure), Is.True, failure.Code.ToString());
                Assert.That(results[0], Is.EqualTo(AIBT.Burst.BurstContextResult.Success));
                Assert.That(results[1], Is.EqualTo(AIBT.Burst.BurstContextResult.Success));
                Assert.That(values[0], Is.EqualTo(5));
                Assert.That(versions[0], Is.Zero);
            }
        }

        [Test]
        public void AgentContextInitializationFailureRollsBackEachNativeAllocation()
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                BuiltInBlackboardTypes.Int32, 5, BlackboardScope.Agent);
            Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                out var program, out var failure), Is.True, failure.Code.ToString());
            Assert.That(program.TryAcquireReadLeaseV2(out var lease, out failure), Is.True, failure.Code.ToString());
            Assert.That(NativeAgentContextCapacityV1.TryDerive(lease.View, 2, out var capacity, out failure),
                Is.True, failure.Code.ToString());
            try
            {
                MethodInfo injected = null;
                foreach (var candidate in typeof(NativeAgentContextOwnerV1).GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
                {
                    var parameters = candidate.GetParameters();
                    if (candidate.Name == "TryCreate" && parameters.Length == 7 && parameters[4].ParameterType == typeof(int))
                    { injected = candidate; break; }
                }
                Assert.That(injected, Is.Not.Null);
                for (var ordinal = 0; ordinal < 4; ordinal++)
                {
                    var arguments = new object[]
                    { new AgentId(77), lease, capacity, Allocator.Persistent, ordinal, null, default(NativeRuntimeFailureV1) };
                    Assert.That((bool)injected.Invoke(null, arguments), Is.False, "failure ordinal " + ordinal);
                    Assert.That(arguments[5], Is.Null);
                    Assert.That(((NativeRuntimeFailureV1)arguments[6]).Code,
                        Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded));
                }
            }
            finally
            {
                Assert.That(program.TryReleaseReadLease(lease, out failure), Is.True, failure.Code.ToString());
                Assert.That(program.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void AgentContextCapacityIsFullyPreflightedBeforeFirstAllocation()
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                BuiltInBlackboardTypes.Int32, 5, BlackboardScope.Agent);
            Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                out var program, out var failure), Is.True, failure.Code.ToString());
            Assert.That(program.TryAcquireReadLeaseV2(out var lease, out failure), Is.True, failure.Code.ToString());
            try
            {
                var method = FindInjectedCreate();
                AssertInvalidCapacityBeforeAllocation(method, lease,
                    new NativeAgentContextCapacityV1(4, 0, 1));
                AssertInvalidCapacityBeforeAllocation(method, lease,
                    new NativeAgentContextCapacityV1(0, (uint)lease.View.Slots.Length, 1));
            }
            finally
            {
                Assert.That(program.TryReleaseReadLease(lease, out failure), Is.True, failure.Code.ToString());
                Assert.That(program.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        private static MethodInfo FindInjectedCreate()
        {
            foreach (var candidate in typeof(NativeAgentContextOwnerV1).GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
            {
                var parameters = candidate.GetParameters();
                if (candidate.Name == "TryCreate" && parameters.Length == 7 && parameters[4].ParameterType == typeof(int))
                    return candidate;
            }
            return null;
        }

        private static void AssertInvalidCapacityBeforeAllocation(
            MethodInfo method,
            NativeProgramReadLeaseV2 lease,
            NativeAgentContextCapacityV1 capacity)
        {
            var arguments = new object[]
            { new AgentId(77), lease, capacity, Allocator.Persistent, 0, null, default(NativeRuntimeFailureV1) };
            Assert.That((bool)method.Invoke(null, arguments), Is.False);
            Assert.That(arguments[5], Is.Null);
            Assert.That(((NativeRuntimeFailureV1)arguments[6]).Code,
                Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch));
        }

        private static void RunWarmPath(
            NativeAgentContextOwnerV1 context,
            NativeArray<TreeInstanceId> ids,
            NativeBlackboardTypeIdV2 type,
            NativeArray<int> equal,
            out bool succeeded)
        {
            succeeded = context.TryBeginExecuteWindow(ids, out var window, out _)
                && context.TryAcquireNext(window, out var lease, out _)
                && NativeAgentBlackboardV1.TryRead(lease, 0, 0, type, out int value, out _) == AIBT.Burst.BurstContextResult.Success
                && value == 5
                && NativeAgentBlackboardV1.TryWrite(lease, 0, 0, type, equal, out var changed) == AIBT.Burst.BurstContextResult.Success
                && !changed
                && context.TryReleaseExecuteLease(lease, out _)
                && context.TryEndExecuteWindow(window, out _);
        }

        private static void AssertV2Order(
            NativeAgentContextOwnerV1 context,
            NativeAgentExecuteWindowV2 window,
            params ulong[] expected)
        {
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.That(context.TryAcquireNext(
                    window, out NativeAgentExecuteLeaseV2 lease, out var failure),
                    Is.True, failure.Code.ToString());
                Assert.That(lease.TreeInstanceId, Is.EqualTo(new TreeInstanceId(expected[index])));
                Assert.That(context.TryReleaseExecuteLease(lease, out failure),
                    Is.True, failure.Code.ToString());
            }
            Assert.That(context.TryEndExecuteWindow(window, out var endFailure),
                Is.True, endFailure.Code.ToString());
        }

        private static NativeArray<NativeExecuteSelectionEntryV1> SelectionEntries(params ulong[] ids)
        {
            var entries = new NativeArray<NativeExecuteSelectionEntryV1>(ids.Length, Allocator.TempJob);
            for (var index = 0; index < ids.Length; index++) entries[index] = SelectionEntry(ids[index]);
            return entries;
        }

        private static NativeExecuteSelectionEntryV1 SelectionEntry(ulong id)
            => new NativeExecuteSelectionEntryV1(new TreeInstanceId(id), 0, 0);

        [BurstCompile]
        private struct BurstProbeJob : IJob
        {
            public NativeArray<int> Output;
            public int Iterations;
            public void Execute()
            {
                var value = 17;
                for (var index = 0; index < Iterations; index++) value = unchecked(value * 397 ^ index);
                Output[0] = value;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct AgentAccessJob : IJob
        {
            public NativeAgentExecutionViewV1 View;
            public NativeBlackboardTypeIdV2 Type;
            public NativeArray<int> Candidate;
            public NativeArray<AIBT.Burst.BurstContextResult> Results;
            public NativeArray<int> Values;
            public NativeArray<ulong> Versions;

            public void Execute()
            {
                Results[0] = NativeAgentBlackboardV1.TryRead(
                    View, 0, 0, Type, out int value, out var version);
                Results[1] = NativeAgentBlackboardV1.TryWrite(
                    View, 0, 0, Type, Candidate, out _);
                Values[0] = value;
                Versions[0] = version;
            }
        }

        private static NativeArray<TreeInstanceId> Ids(params ulong[] values)
        {
            var result = new NativeArray<TreeInstanceId>(values.Length, Allocator.Temp);
            for (var index = 0; index < values.Length; index++) result[index] = new TreeInstanceId(values[index]);
            return result;
        }

        private static NativeArray<T> One<T>(T value) where T : struct
        {
            var result = new NativeArray<T>(1, Allocator.Temp);
            result[0] = value;
            return result;
        }

        private static void SetFirst(NativeArray<TreeInstanceId> values, TreeInstanceId value)
            => values[0] = value;

        private static void SetFirst<T>(NativeArray<T> values, T value) where T : struct
            => values[0] = value;

        private sealed class TreeScenario : System.IDisposable
        {
            internal NativeInstanceArenaOwnerV1 Instance;

            internal static TreeScenario Create(NativeProgramReadLeaseV2 programLease)
            {
                Assert.That(NativeInstanceArenaCapacityV2.TryDerive(
                    programLease.View, out var capacity, out var failure), Is.True, failure.Code.ToString());
                Assert.That(NativeInstanceArenaOwnerV1.TryCreateV2(
                    programLease, capacity, Allocator.Persistent, out var instance, out failure),
                    Is.True, failure.Code.ToString());
                return new TreeScenario { Instance = instance };
            }

            public void Dispose()
                => Assert.That(Instance.TryDispose(out var failure), Is.True, failure.Code.ToString());
        }

        private sealed class AgentScenario : System.IDisposable
        {
            internal NativeProgramImageOwnerV1 Program;
            internal NativeProgramReadLeaseV2 ProgramLease;
            internal NativeAgentContextRegistryV1 Registry;
            internal NativeAgentContextOwnerV1 Context;
            internal TreeScenario[] Trees;
            internal NativeAgentBindingV1[] Bindings;

            internal static AgentScenario Create(uint maximumBindings, params TreeInstanceId[] ids)
            {
                var binding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                    BuiltInBlackboardTypes.Int32, 5, BlackboardScope.Agent);
                Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                    binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                    out var program, out var failure), Is.True, failure.Code.ToString());
                Assert.That(program.TryAcquireReadLeaseV2(out var lease, out failure), Is.True, failure.Code.ToString());
                Assert.That(NativeAgentContextCapacityV1.TryDerive(
                    lease.View, maximumBindings, out var capacity, out failure), Is.True, failure.Code.ToString());
                Assert.That(NativeAgentContextRegistryV1.TryCreate(1, Allocator.Persistent, out var registry, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(registry.TryCreateContext(new AgentId(1), lease, capacity, out var context, out failure),
                    Is.True, failure.Code.ToString());
                var trees = new TreeScenario[ids.Length];
                var bindings = new NativeAgentBindingV1[ids.Length];
                for (var index = 0; index < ids.Length; index++)
                {
                    trees[index] = TreeScenario.Create(lease);
                    Assert.That(context.TryBind(ids[index], lease, trees[index].Instance, out bindings[index], out failure),
                        Is.True, failure.Code.ToString());
                }
                return new AgentScenario
                {
                    Program = program, ProgramLease = lease, Registry = registry,
                    Context = context, Trees = trees, Bindings = bindings,
                };
            }

            public void Dispose()
            {
                for (var index = Bindings.Length - 1; index >= 0; index--)
                    Assert.That(Context.TryUnbind(Bindings[index], out var failure), Is.True, failure.Code.ToString());
                for (var index = Trees.Length - 1; index >= 0; index--) Trees[index].Dispose();
                Assert.That(Registry.TryDestroyContext(Context, out var contextFailure), Is.True, contextFailure.Code.ToString());
                Assert.That(Registry.TryDispose(out contextFailure), Is.True, contextFailure.Code.ToString());
                Assert.That(Program.TryReleaseReadLease(ProgramLease, out contextFailure), Is.True, contextFailure.Code.ToString());
                Assert.That(Program.TryDispose(out contextFailure), Is.True, contextFailure.Code.ToString());
            }
        }
    }
}
