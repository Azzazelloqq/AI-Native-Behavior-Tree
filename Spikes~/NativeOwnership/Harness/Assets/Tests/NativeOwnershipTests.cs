using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT.NativeOwnership.Spike.Tests
{
    [TestFixture]
    public sealed class NativeOwnershipTests
    {
        public static IEnumerable CapacityKinds
        {
            get
            {
                foreach (NativeResourceKind kind in Enum.GetValues(typeof(NativeResourceKind)))
                {
                    if (kind != NativeResourceKind.Alignment)
                    {
                        yield return new TestCaseData(kind, NativeCapacityPlan.CapacityFailureCode(kind));
                    }
                }
            }
        }

        public static IEnumerable PassAllocationFailurePoints
        {
            get
            {
                var kinds = new[]
                {
                    NativeResourceKind.InstanceBytes,
                    NativeResourceKind.WorkItems,
                    NativeResourceKind.ScratchBytes,
                    NativeResourceKind.CommandRecords,
                    NativeResourceKind.CommandPayloadBytes,
                    NativeResourceKind.CompletionRecords,
                    NativeResourceKind.CompletionPayloadBytes,
                    NativeResourceKind.SharedContributionRecords,
                    NativeResourceKind.SharedContributionPayloadBytes,
                    NativeResourceKind.DiagnosticRecords,
                    NativeResourceKind.DiagnosticPayloadBytes,
                    NativeResourceKind.TraceRecords,
                    NativeResourceKind.TracePayloadBytes,
                    NativeResourceKind.DiagnosticRecords
                };
                for (var index = 0; index < kinds.Length; index++)
                {
                    yield return new TestCaseData(index + 1, kinds[index]);
                }
            }
        }

        [SetUp]
        public void SetUp()
        {
            Assert.That(NativeAllocationProbe.LiveAllocations, Is.Zero, "previous test leaked spike-owned native storage");
        }

        [TearDown]
        public void TearDown()
        {
            Assert.That(NativeAllocationProbe.LiveAllocations, Is.Zero, "test leaked spike-owned native storage");
        }

        [Test]
        public void CreateScheduleCompleteCommitsStagingAndPublishesWholeOutput()
        {
            var plan = NativeOwnershipScenario.CreateValidPlan();
            using (var program = new NativeProgramImageOwner(plan, 3))
            using (var instance = new NativeInstanceArenaOwner(plan, program.Binding, 10))
            using (var input = new NativeInputFrameOwner(plan, 5))
            using (var pass = new NativeExecutionPassOwner(plan))
            {
                pass.Schedule(program, instance, input, NativeOwnershipScenario.CreateValidRequest());
                Assert.That(instance.State, Is.EqualTo(NativeOwnerState.Executing));
                pass.Complete();

                Assert.That(pass.State, Is.EqualTo(NativeOwnerState.Completed));
                Assert.That(pass.CallbackCount, Is.EqualTo(1));
                Assert.That(instance.Value, Is.EqualTo(18));
                Assert.That(pass.PublishedCommandCount, Is.EqualTo(2));
                Assert.That(pass.GetPublishedCommand(0).Value, Is.EqualTo(18));
                Assert.That(pass.GetPublishedCommand(1).Value, Is.EqualTo(19));
            }
        }

        [TestCaseSource(nameof(CapacityKinds))]
        public void EveryNormativeCapacityLimitRejectsWithItsStableCode(NativeResourceKind kind, int expectedCode)
        {
            var requirements = BaseRequirements().With(kind, 5);
            var limits = BaseLimits().With(kind, 4);

            Assert.That(NativeCapacityPlan.TryCreate(requirements, limits, out _, out var diagnostic), Is.False);
            Assert.That(diagnostic.Code, Is.EqualTo(expectedCode));
            Assert.That(diagnostic.ResourceKind, Is.EqualTo(kind));
            Assert.That(diagnostic.Requested, Is.EqualTo(5));
            Assert.That(diagnostic.Capacity, Is.EqualTo(4));
        }

        [TestCaseSource(nameof(CapacityKinds))]
        public void EveryNormativeCapacityFieldConvertsRepresentationOverflowToAibt4303(NativeResourceKind kind, int expectedCodeIgnored)
        {
            _ = expectedCodeIgnored;
            var overflow = (ulong)int.MaxValue + 1;
            var requirements = BaseRequirements().With(kind, overflow);
            var limits = BaseLimits().With(kind, overflow);

            Assert.That(NativeCapacityPlan.TryCreate(requirements, limits, out _, out var diagnostic), Is.False);
            Assert.That(diagnostic.Code, Is.EqualTo(NativeDiagnosticCodes.CapacityArithmeticOverflow));
            Assert.That(diagnostic.ResourceKind, Is.EqualTo(kind));
        }

        [Test]
        public void InvalidAlignmentAndCheckedCounterOverflowUseAibt4302AndAibt4303()
        {
            var invalidAlignment = BaseRequirements().With(NativeResourceKind.Alignment, 3);
            Assert.That(NativeCapacityPlan.TryCreate(invalidAlignment, BaseLimits(), out _, out var invalid), Is.False);
            Assert.That(invalid.Code, Is.EqualTo(NativeDiagnosticCodes.CapacityPlanInvalid));
            Assert.That(invalid.ResourceKind, Is.EqualTo(NativeResourceKind.Alignment));

            Assert.That(
                NativeCapacityPlan.TryCheckedAdd(
                    uint.MaxValue,
                    1,
                    NativeResourceKind.SharedContributionRecords,
                    out _,
                    out var overflow),
                Is.False);
            Assert.That(overflow.Code, Is.EqualTo(NativeDiagnosticCodes.CapacityArithmeticOverflow));
        }

        [Test]
        public void PreflightCapacityFailureSchedulesNoCallbackAndAcquiresNoLease()
        {
            var plan = NativeOwnershipScenario.CreateValidPlan(commandRecords: 1);
            using (var program = new NativeProgramImageOwner(plan, 3))
            using (var instance = new NativeInstanceArenaOwner(plan, program.Binding, 10))
            using (var input = new NativeInputFrameOwner(plan, 5))
            using (var pass = new NativeExecutionPassOwner(plan))
            {
                var request = NativeOwnershipScenario.CreateValidRequest(commandRecords: 2);
                pass.Schedule(program, instance, input, request);

                Assert.That(pass.State, Is.EqualTo(NativeOwnerState.Faulted));
                Assert.That(pass.Rejection.Code, Is.EqualTo(NativeDiagnosticCodes.OutputCapacityExceeded));
                Assert.That(pass.Rejection.ResourceKind, Is.EqualTo(NativeResourceKind.CommandRecords));
                Assert.That(pass.CallbackCount, Is.Zero);
                Assert.That(instance.State, Is.EqualTo(NativeOwnerState.Initialized));
                Assert.That(input.State, Is.EqualTo(NativeOwnerState.Initialized));
                Assert.That(instance.Value, Is.EqualTo(10));
                Assert.That(pass.PublishedCommandCount, Is.Zero);
            }
        }

        [Test]
        public void SharedStreamsRequireTreeInstanceOrderAndRejectWholeReduceBeforeCallback()
        {
            var plan = NativeOwnershipScenario.CreateValidPlan();
            using (var program = new NativeProgramImageOwner(plan, 3))
            using (var instance = new NativeInstanceArenaOwner(plan, program.Binding, 10))
            using (var input = new NativeInputFrameOwner(plan, 5))
            using (var pass = new NativeExecutionPassOwner(plan))
            {
                var request = NativeOwnershipScenario.CreateValidRequest();
                request.SharedStreams = new[]
                {
                    new NativeSharedStreamReservation(2, 1, 1),
                    new NativeSharedStreamReservation(1, 1, 1)
                };
                pass.Schedule(program, instance, input, request);

                Assert.That(pass.State, Is.EqualTo(NativeOwnerState.Faulted));
                Assert.That(pass.Rejection.Code, Is.EqualTo(NativeDiagnosticCodes.CapacityPlanInvalid));
                Assert.That(pass.Rejection.ResourceKind, Is.EqualTo(NativeResourceKind.SharedContributionRecords));
                Assert.That(pass.CallbackCount, Is.Zero);
                Assert.That(instance.Value, Is.EqualTo(10));
            }
        }

        [Test]
        public void WrongProgramGenerationAndForeignProgramRejectBeforeCallback()
        {
            var plan = NativeOwnershipScenario.CreateValidPlan();
            using (var program = new NativeProgramImageOwner(plan, 3, generation: 7))
            using (var foreignProgram = new NativeProgramImageOwner(plan, 3, generation: 7))
            using (var wrongGenerationInstance = new NativeInstanceArenaOwner(
                       plan,
                       new NativeProgramBinding(program.OwnerId, program.Generation + 1),
                       10))
            using (var input = new NativeInputFrameOwner(plan, 5))
            using (var generationPass = new NativeExecutionPassOwner(plan))
            using (var foreignPass = new NativeExecutionPassOwner(plan))
            {
                var generationError = Assert.Throws<NativeOwnershipException>(() =>
                    generationPass.Schedule(program, wrongGenerationInstance, input, NativeOwnershipScenario.CreateValidRequest()));
                Assert.That(generationError.DiagnosticCode, Is.EqualTo(NativeDiagnosticCodes.LifetimeStateInvalid));
                Assert.That(generationPass.CallbackCount, Is.Zero);

                var correctlyBoundInstance = new NativeInstanceArenaOwner(plan, program.Binding, 10);
                try
                {
                    var foreignError = Assert.Throws<NativeOwnershipException>(() =>
                        foreignPass.Schedule(foreignProgram, correctlyBoundInstance, input, NativeOwnershipScenario.CreateValidRequest()));
                    Assert.That(foreignError.DiagnosticCode, Is.EqualTo(NativeDiagnosticCodes.LifetimeStateInvalid));
                    Assert.That(foreignPass.CallbackCount, Is.Zero);
                    Assert.That(correctlyBoundInstance.State, Is.EqualTo(NativeOwnerState.Initialized));
                }
                finally
                {
                    correctlyBoundInstance.Dispose();
                }
            }
        }

        [Test]
        public void StaleAndForeignLeaseTokensAlwaysUseAibt4311()
        {
            var plan = NativeOwnershipScenario.CreateValidPlan();
            using (var first = new NativeProgramImageOwner(plan, 1))
            using (var second = new NativeProgramImageOwner(plan, 1))
            {
                var firstToken = first.AcquireLeaseForProbe();
                var secondToken = second.AcquireLeaseForProbe();
                var foreign = Assert.Throws<NativeOwnershipException>(() => second.ReleaseLeaseForProbe(firstToken));
                Assert.That(foreign.DiagnosticCode, Is.EqualTo(NativeDiagnosticCodes.LifetimeStateInvalid));

                var wrongGeneration = new NativeLeaseToken(firstToken.OwnerId, firstToken.Generation + 1, firstToken.LeaseId);
                var generationError = Assert.Throws<NativeOwnershipException>(() => first.ReleaseLeaseForProbe(wrongGeneration));
                Assert.That(generationError.DiagnosticCode, Is.EqualTo(NativeDiagnosticCodes.LifetimeStateInvalid));

                first.ReleaseLeaseForProbe(firstToken);
                var stale = Assert.Throws<NativeOwnershipException>(() => first.ReleaseLeaseForProbe(firstToken));
                Assert.That(stale.DiagnosticCode, Is.EqualTo(NativeDiagnosticCodes.LifetimeStateInvalid));
                second.ReleaseLeaseForProbe(secondToken);
            }
        }

        [Test]
        public void MissingOwnerRejectsWithAibt4311BeforeCallback()
        {
            var plan = NativeOwnershipScenario.CreateValidPlan();
            using (var program = new NativeProgramImageOwner(plan, 3))
            using (var instance = new NativeInstanceArenaOwner(plan, program.Binding, 10))
            using (var pass = new NativeExecutionPassOwner(plan))
            {
                var error = Assert.Throws<NativeOwnershipException>(() =>
                    pass.Schedule(program, instance, null, NativeOwnershipScenario.CreateValidRequest()));
                Assert.That(error.DiagnosticCode, Is.EqualTo(NativeDiagnosticCodes.LifetimeStateInvalid));
                Assert.That(pass.CallbackCount, Is.Zero);
                Assert.That(instance.State, Is.EqualTo(NativeOwnerState.Initialized));
            }
        }

        [Test]
        public void ValidLiveLeaseConflictsAlwaysUseAibt4312()
        {
            var plan = NativeOwnershipScenario.CreateValidPlan();
            using (var program = new NativeProgramImageOwner(plan, 3))
            using (var instance = new NativeInstanceArenaOwner(plan, program.Binding, 10))
            using (var input = new NativeInputFrameOwner(plan, 5))
            using (var pass = new NativeExecutionPassOwner(plan))
            {
                pass.Schedule(program, instance, input, NativeOwnershipScenario.CreateValidRequest());

                AssertCode4312(() => { _ = instance.Value; });
                AssertCode4312(instance.Dispose);
                AssertCode4312(program.Dispose);
                AssertCode4312(input.Dispose);
                AssertCode4312(() => pass.Schedule(program, instance, input, NativeOwnershipScenario.CreateValidRequest()));
                AssertCode4312(pass.Dispose);

                pass.Complete();
            }
        }

        [Test]
        public void ScheduleExceptionRollsBackEveryLeaseAndLeavesOwnersReusable()
        {
            var plan = NativeOwnershipScenario.CreateValidPlan();
            using (var program = new NativeProgramImageOwner(plan, 3))
            using (var instance = new NativeInstanceArenaOwner(plan, program.Binding, 10))
            using (var input = new NativeInputFrameOwner(plan, 5))
            using (var pass = new NativeExecutionPassOwner(plan))
            {
                Assert.Throws<InvalidOperationException>(() =>
                    pass.Schedule(program, instance, input, NativeOwnershipScenario.CreateValidRequest(), injectScheduleFailure: true));

                Assert.That(pass.State, Is.EqualTo(NativeOwnerState.Building));
                Assert.That(program.State, Is.EqualTo(NativeOwnerState.Initialized));
                Assert.That(instance.State, Is.EqualTo(NativeOwnerState.Initialized));
                Assert.That(input.State, Is.EqualTo(NativeOwnerState.Initialized));
                Assert.That(instance.Value, Is.EqualTo(10));
                Assert.That(pass.CallbackCount, Is.Zero);

                pass.Schedule(program, instance, input, NativeOwnershipScenario.CreateValidRequest());
                pass.Complete();
                Assert.That(pass.State, Is.EqualTo(NativeOwnerState.Completed));
            }
        }

        [Test]
        public void ScheduledAbortIsDistinctFromFaultAndCleansUpWithoutCommit()
        {
            AssertTerminalCleanup(NativeExecutionMode.Abort, NativeOwnerState.Aborted, expectedDiagnostic: 0);
        }

        [Test]
        public void ScheduledFaultCleansUpWithoutCommitAndCarriesDiagnostic()
        {
            AssertTerminalCleanup(NativeExecutionMode.Fault, NativeOwnerState.Faulted, NativeDiagnosticCodes.LifetimeStateInvalid);
        }

        [TestCase(1, NativeResourceKind.ProgramRecords)]
        [TestCase(2, NativeResourceKind.ConfigBytes)]
        public void ProgramInitializationFailureRollsBackAllNativeArrays(int failurePoint, NativeResourceKind kind)
        {
            var plan = NativeOwnershipScenario.CreateValidPlan();
            var error = Assert.Throws<NativeOwnershipException>(() =>
                new NativeProgramImageOwner(plan, 1, injector: new NativeAllocationFailureInjector(failurePoint)));
            Assert.That(error.Diagnostic.ResourceKind, Is.EqualTo(kind));
            Assert.That(NativeAllocationProbe.LiveAllocations, Is.Zero);
        }

        [TestCase(1, NativeResourceKind.InputRecords)]
        [TestCase(2, NativeResourceKind.InputPayloadBytes)]
        [TestCase(3, NativeResourceKind.CompletionRecords)]
        [TestCase(4, NativeResourceKind.CompletionPayloadBytes)]
        public void InputInitializationFailureRollsBackAllNativeArrays(int failurePoint, NativeResourceKind kind)
        {
            var plan = NativeOwnershipScenario.CreateValidPlan();
            var error = Assert.Throws<NativeOwnershipException>(() =>
                new NativeInputFrameOwner(plan, 1, injector: new NativeAllocationFailureInjector(failurePoint)));
            Assert.That(error.Diagnostic.ResourceKind, Is.EqualTo(kind));
            Assert.That(NativeAllocationProbe.LiveAllocations, Is.Zero);
        }

        [TestCaseSource(nameof(PassAllocationFailurePoints))]
        public void PassInitializationFailureRollsBackEveryPartialNativeArray(int failurePoint, NativeResourceKind kind)
        {
            var plan = NativeOwnershipScenario.CreateValidPlan();
            var error = Assert.Throws<NativeOwnershipException>(() =>
                new NativeExecutionPassOwner(plan, injector: new NativeAllocationFailureInjector(failurePoint)));
            Assert.That(error.Diagnostic.ResourceKind, Is.EqualTo(kind));
            Assert.That(NativeAllocationProbe.LiveAllocations, Is.Zero);
        }

        [Test]
        public void InstanceInitializationFailureAndAllocatorFailureDoNotLeak()
        {
            var plan = NativeOwnershipScenario.CreateValidPlan();
            using (var program = new NativeProgramImageOwner(plan, 1))
            {
                Assert.Throws<NativeOwnershipException>(() =>
                    new NativeInstanceArenaOwner(plan, program.Binding, 1, injector: new NativeAllocationFailureInjector(1)));
                var allocator = Assert.Throws<NativeOwnershipException>(() =>
                    new NativeInputFrameOwner(plan, 1, allocator: Allocator.TempJob));
                Assert.That(allocator.DiagnosticCode, Is.EqualTo(NativeDiagnosticCodes.AllocatorInvalid));
                Assert.That(NativeAllocationProbe.LiveAllocations, Is.EqualTo(2), "only the live program arrays may remain");
            }
        }

        [Test]
        public void DiagnosticCatalogIsExactContiguousAndExercised()
        {
            Assert.That(NativeDiagnosticCodes.All.Distinct().Count(), Is.EqualTo(12));
            Assert.That(NativeDiagnosticCodes.All, Is.EqualTo(Enumerable.Range(4301, 12)));
        }

        [Test]
        public void FocusedSafetyRunHasUnityCollectionChecksEnabled()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Assert.Pass();
#else
            Assert.Fail("Focused safety verification requires ENABLE_UNITY_COLLECTIONS_CHECKS.");
#endif
        }

        [Test]
        public void UnitySafetyChecksRejectDisposalWhileScheduledJobOwnsView()
        {
            var values = new NativeArray<int>(1, Allocator.Persistent);
            values[0] = 7;
            var dependency = new ReadOnlyProbeJob { Values = values }.Schedule();
            try
            {
                Assert.Throws<InvalidOperationException>(() => values.Dispose());
            }
            finally
            {
                dependency.Complete();
                if (values.IsCreated)
                {
                    values.Dispose();
                }
            }
        }

        private static NativeCapacityValues BaseRequirements()
        {
            return NativeCapacityValues.Uniform(1)
                .With(NativeResourceKind.Alignment, 4)
                .With(NativeResourceKind.InstanceBytes, 4);
        }

        private static NativeCapacityValues BaseLimits()
        {
            return NativeCapacityValues.Uniform(4)
                .With(NativeResourceKind.Alignment, 8)
                .With(NativeResourceKind.InstanceBytes, 4);
        }

        private static void AssertCode4312(TestDelegate operation)
        {
            var error = Assert.Throws<NativeOwnershipException>(operation);
            Assert.That(error.DiagnosticCode, Is.EqualTo(NativeDiagnosticCodes.LiveJobOwnershipViolation));
        }

        private static void AssertTerminalCleanup(NativeExecutionMode mode, NativeOwnerState expectedState, int expectedDiagnostic)
        {
            var plan = NativeOwnershipScenario.CreateValidPlan();
            using (var program = new NativeProgramImageOwner(plan, 3))
            using (var instance = new NativeInstanceArenaOwner(plan, program.Binding, 10))
            using (var input = new NativeInputFrameOwner(plan, 5))
            using (var pass = new NativeExecutionPassOwner(plan))
            {
                pass.Schedule(program, instance, input, NativeOwnershipScenario.CreateValidRequest(mode: mode));
                pass.Complete();

                Assert.That(pass.State, Is.EqualTo(expectedState));
                Assert.That(pass.CallbackCount, Is.EqualTo(1));
                Assert.That(pass.Rejection.Code, Is.EqualTo(expectedDiagnostic));
                Assert.That(instance.Value, Is.EqualTo(10));
                Assert.That(pass.PublishedCommandCount, Is.Zero);
                Assert.That(program.State, Is.EqualTo(NativeOwnerState.Initialized));
                Assert.That(input.State, Is.EqualTo(NativeOwnerState.Initialized));
            }
        }

        [Unity.Burst.BurstCompile(CompileSynchronously = true)]
        private struct ReadOnlyProbeJob : IJob
        {
            [ReadOnly] public NativeArray<int> Values;

            public void Execute()
            {
                _ = Values[0];
            }
        }
    }
}
