using System;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT.Tests.Runtime
{
    public sealed class NativeProgramAndStateTests
    {
        [Test]
        public void Binder_ProjectsEveryLogicalTableAndKeepsDebugStringsHostSide()
        {
            var program = Fixture.CreateProgram();
            Assert.That(NativeProgramImageOwnerV1.TryCreate(
                program,
                NativeProgramImageCapacityV1.Exact(program),
                Allocator.Persistent,
                out var owner,
                out var failure), Is.True, failure.Code.ToString());

            try
            {
                Assert.That(owner.TryAcquireReadLease(out var lease, out failure), Is.True, failure.Code.ToString());
                var view = lease.View;
                Assert.That(view.Header.Magic, Is.EqualTo(program.Header.Magic));
                Assert.That(view.Header.CompiledFormatVersion, Is.EqualTo(program.Header.CompiledFormatVersion));
                Assert.That(view.Header.ExecutionSemanticsVersion, Is.EqualTo(program.Header.ExecutionSemanticsVersion));
                Assert.That(view.Header.CompilerMajor, Is.EqualTo(program.Header.CompilerVersion.Major));
                Assert.That(view.Header.CompilerBuildRevision, Is.EqualTo(program.Header.CompilerVersion.BuildRevision));
                Assert.That(view.Header.CanonicalSemanticHash.GetByte(0), Is.EqualTo(0xaa));
                Assert.That(view.Header.NodeRegistryHash.GetByte(0), Is.EqualTo(0xbb));
                Assert.That(view.Header.CanonicalPolicyHash.GetByte(0), Is.EqualTo(0xcc));
                Assert.That(view.Header.RootNodeIndex, Is.Zero);
                Assert.That(view.Header.NodeCount, Is.EqualTo(2));
                Assert.That(view.Header.InstanceNodeMemorySize, Is.EqualTo(16));
                Assert.That(view.Header.RequiredMaximumAlignment, Is.EqualTo(8));
                Assert.That(view.Header.DeterministicModeCompatible, Is.EqualTo(1));

                Assert.That(view.Nodes.Length, Is.EqualTo(program.Nodes.Count));
                Assert.That(view.Nodes[0].NodeTypeId, Is.EqualTo(program.Nodes[0].NodeTypeId));
                Assert.That(view.Nodes[0].ChildOffset, Is.EqualTo(program.Nodes[0].Children.Offset));
                Assert.That(view.Nodes[0].ChildCount, Is.EqualTo(program.Nodes[0].Children.Count));
                Assert.That(view.Nodes[1].MemoryLifetime, Is.EqualTo(NodeMemoryLifetime.Instance));
                Assert.That(view.ChildIndices[0], Is.EqualTo(program.ChildIndices[0]));
                Assert.That(view.ReadSlotIndices[0], Is.EqualTo(program.ReadSlotIndices[0]));
                Assert.That(view.WriteSlotIndices[0], Is.EqualTo(program.WriteSlotIndices[0]));
                Assert.That(view.BlackboardSlots[0].StableKeyId, Is.EqualTo(program.BlackboardSlots[0].StableKeyId));
                Assert.That(view.Observers[0].ObserverNodeIndex, Is.EqualTo(program.Observers[0].ObserverNodeIndex));
                Assert.That(view.WatchedSlotIndices[0], Is.EqualTo(program.WatchedSlotIndices[0]));
                Assert.That(view.ConfigBlob[7], Is.EqualTo(program.ConfigBlob[7]));
                Assert.That(view.DefaultValueBlob[3], Is.EqualTo(program.DefaultValueBlob[3]));
                Assert.That(view.DebugRuntimeNodeIndices[1], Is.EqualTo(program.DebugMap[1].RuntimeNodeIndex));
                Assert.That(owner.HostDebugMap[0].AuthoringNodeId, Is.EqualTo(new NodeId("root")));
                Assert.That(owner.HostDebugMap[0].SourcePath, Is.EqualTo("/tree/root"));
                Assert.That(owner.HostDebugMap[0].DisplayName, Is.EqualTo("Root display"));

                Assert.That(owner.TryReleaseReadLease(lease, out failure), Is.True, failure.Code.ToString());
            }
            finally
            {
                Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void Binder_RejectsContentHashMismatchBeforeAllocation()
        {
            var program = Fixture.CreateProgram(useIncorrectContentHash: true);
            Assert.That(NativeProgramImageOwnerV1.TryCreate(
                program,
                NativeProgramImageCapacityV1.Exact(program),
                Allocator.Persistent,
                out var owner,
                out var failure), Is.False);
            Assert.That(owner, Is.Null);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
            Assert.That(failure.ResourceKind, Is.EqualTo(NativeResourceKindV1.ProgramHash));
        }

        [TestCase(NativeResourceKindV1.ProgramNodes)]
        [TestCase(NativeResourceKindV1.ProgramChildIndices)]
        [TestCase(NativeResourceKindV1.ProgramReadSlotIndices)]
        [TestCase(NativeResourceKindV1.ProgramWriteSlotIndices)]
        [TestCase(NativeResourceKindV1.ProgramBlackboardSlots)]
        [TestCase(NativeResourceKindV1.ProgramObservers)]
        [TestCase(NativeResourceKindV1.ProgramWatchedSlotIndices)]
        [TestCase(NativeResourceKindV1.ProgramConfigBytes)]
        [TestCase(NativeResourceKindV1.ProgramDefaultBytes)]
        [TestCase(NativeResourceKindV1.ProgramDebugOrdinals)]
        public void Binder_RejectsEveryInsufficientProgramCapacity(NativeResourceKindV1 resource)
        {
            var program = Fixture.CreateProgram();
            var exact = NativeProgramImageCapacityV1.Exact(program);
            var capacity = Fixture.Reduce(exact, resource);

            Assert.That(NativeProgramImageOwnerV1.TryCreate(
                program,
                capacity,
                Allocator.Persistent,
                out var owner,
                out var failure), Is.False);
            Assert.That(owner, Is.Null);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeProgramCapacityExceeded));
            Assert.That(failure.ResourceKind, Is.EqualTo(resource));
        }

        [Test]
        public void CapacityAndAlignmentFailuresUseStableDiagnosticsWithoutOverflowException()
        {
            var program = Fixture.CreateProgram();
            var exact = NativeProgramImageCapacityV1.Exact(program);
            var badAlignment = new NativeProgramImageCapacityV1(
                exact.NodeRecords, exact.ChildIndices, exact.ReadSlotIndices, exact.WriteSlotIndices,
                exact.BlackboardSlots, exact.Observers, exact.WatchedSlotIndices, exact.ConfigBytes,
                exact.DefaultBytes, exact.DebugOrdinals, 3);

            Assert.That(NativeProgramImageOwnerV1.TryCreate(
                program, badAlignment, Allocator.Persistent, out _, out var failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
            Assert.That(failure.ResourceKind, Is.EqualTo(NativeResourceKindV1.MaximumAlignment));

            Assert.That(NativeCheckedMathV1.TryAlignUp(
                uint.MaxValue,
                8,
                NativeResourceKindV1.InstanceNodeMemory,
                out _,
                out failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow));
        }

        [Test]
        public void OwnersRejectNonPersistentAllocatorAndUnrepresentableInstanceCapacity()
        {
            var program = Fixture.CreateProgram();
            Assert.That(NativeProgramImageOwnerV1.TryCreate(
                program, NativeProgramImageCapacityV1.Exact(program), Allocator.Temp,
                out var rejectedProgram, out var failure), Is.False);
            Assert.That(rejectedProgram, Is.Null);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeAllocatorInvalid));

            Assert.That(NativeProgramImageOwnerV1.TryCreate(
                program, NativeProgramImageCapacityV1.Exact(program), Allocator.Persistent,
                out var owner, out failure), Is.True);
            try
            {
                Assert.That(owner.TryAcquireReadLease(out var lease, out failure), Is.True);
                Assert.That(NativeInstanceArenaCapacityV1.TryDerive(lease.View, out var exact, out failure), Is.True);

                Assert.That(NativeInstanceArenaOwnerV1.TryCreate(
                    lease, exact, Allocator.TempJob, out var rejectedInstance, out failure), Is.False);
                Assert.That(rejectedInstance, Is.Null);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeAllocatorInvalid));

                var overflow = new NativeInstanceArenaCapacityV1(
                    uint.MaxValue, exact.TreeBlackboardBytes, exact.FrameCount, exact.GenerationCount,
                    exact.ParallelBranchCapacity, exact.ObserverCount, exact.UpdateStateCount,
                    exact.BudgetStateCount, exact.MaximumAlignment);
                Assert.That(NativeInstanceArenaOwnerV1.TryCreate(
                    lease, overflow, Allocator.Persistent, out rejectedInstance, out failure), Is.False);
                Assert.That(rejectedInstance, Is.Null);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow));
                Assert.That(failure.ResourceKind, Is.EqualTo(NativeResourceKindV1.InstanceNodeMemory));

                var badAlignment = new NativeInstanceArenaCapacityV1(
                    exact.NodeMemoryBytes, exact.TreeBlackboardBytes, exact.FrameCount, exact.GenerationCount,
                    exact.ParallelBranchCapacity, exact.ObserverCount, exact.UpdateStateCount,
                    exact.BudgetStateCount, 3);
                Assert.That(NativeInstanceArenaOwnerV1.TryCreate(
                    lease, badAlignment, Allocator.Persistent, out rejectedInstance, out failure), Is.False);
                Assert.That(rejectedInstance, Is.Null);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
                Assert.That(failure.ResourceKind, Is.EqualTo(NativeResourceKindV1.MaximumAlignment));

                Assert.That(owner.TryReleaseReadLease(lease, out failure), Is.True);
            }
            finally
            {
                Assert.That(owner.TryDispose(out failure), Is.True);
            }
        }

        [Test]
        public void InstanceCapacity_IsFixedAndDerivedFromValidatedProgramBounds()
        {
            using (var scenario = Fixture.CreateScenario())
            {
                var capacity = scenario.Instance.Capacity;
                Assert.That(capacity.NodeMemoryBytes, Is.EqualTo(16));
                Assert.That(capacity.TreeBlackboardBytes, Is.EqualTo(4));
                Assert.That(capacity.FrameCount, Is.EqualTo(2));
                Assert.That(capacity.GenerationCount, Is.EqualTo(2));
                Assert.That(capacity.ParallelBranchCapacity, Is.EqualTo(1),
                    "Child-index count is a conservative fixed capacity, not a semantic parallel-child count.");
                Assert.That(capacity.ObserverCount, Is.EqualTo(1));
                Assert.That(capacity.UpdateStateCount, Is.EqualTo(1));
                Assert.That(capacity.BudgetStateCount, Is.EqualTo(1));

                Assert.That(scenario.Instance.TryAcquireExecutionLease(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                Assert.That(execution.View.NodeMemory.Length, Is.EqualTo(16));
                Assert.That(execution.View.Frames.Length, Is.EqualTo(2));
                Assert.That(execution.View.ParallelBranches.Length, Is.EqualTo(1));
                Assert.That(execution.View.ParallelBranches[0].CapacityOrdinal, Is.Zero);
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
            }
        }

        [TestCase(NativeResourceKindV1.InstanceNodeMemory)]
        [TestCase(NativeResourceKindV1.InstanceTreeBlackboard)]
        [TestCase(NativeResourceKindV1.InstanceFrames)]
        [TestCase(NativeResourceKindV1.InstanceGenerations)]
        [TestCase(NativeResourceKindV1.InstanceParallelBranches)]
        [TestCase(NativeResourceKindV1.InstanceObservers)]
        [TestCase(NativeResourceKindV1.InstanceUpdateState)]
        [TestCase(NativeResourceKindV1.InstanceBudgetState)]
        public void InstanceFactory_RejectsEveryInsufficientArenaCapacity(NativeResourceKindV1 resource)
        {
            var program = Fixture.CreateProgram();
            Assert.That(NativeProgramImageOwnerV1.TryCreate(
                program, NativeProgramImageCapacityV1.Exact(program), Allocator.Persistent,
                out var programOwner, out var failure), Is.True, failure.Code.ToString());
            try
            {
                Assert.That(programOwner.TryAcquireReadLease(out var programLease, out failure), Is.True);
                Assert.That(NativeInstanceArenaCapacityV1.TryDerive(programLease.View, out var exact, out failure), Is.True);
                var capacity = Fixture.Reduce(exact, resource);

                Assert.That(NativeInstanceArenaOwnerV1.TryCreate(
                    programLease, capacity, Allocator.Persistent, out var instance, out failure), Is.False);
                Assert.That(instance, Is.Null);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded));
                Assert.That(failure.ResourceKind, Is.EqualTo(resource));
                Assert.That(programOwner.TryReleaseReadLease(programLease, out failure), Is.True);
            }
            finally
            {
                Assert.That(programOwner.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void ActivationMemoryClearsOnTerminalAndAbortedExit_InstanceMemoryOnlyOnRestartOrReset()
        {
            using (var scenario = Fixture.CreateScenario())
            {
                WriteNodeMemory(scenario, 0x41, 0x72);
                Assert.That(scenario.Instance.TryCompleteTerminalExit(0, out var failure), Is.True, failure.Code.ToString());
                AssertMemory(scenario, 0, 8, 0);
                AssertMemory(scenario, 8, 8, 0x72);

                WriteNodeMemory(scenario, 0x33, 0x72);
                Assert.That(scenario.Instance.TryCompleteAbortedExit(0, out failure), Is.True, failure.Code.ToString());
                AssertMemory(scenario, 0, 8, 0);
                AssertMemory(scenario, 8, 8, 0x72);

                Assert.That(scenario.Instance.TryRestart(out failure), Is.True, failure.Code.ToString());
                AssertMemory(scenario, 0, 16, 0);
                AssertObserverBinding(scenario);

                WriteNodeMemory(scenario, 0x55, 0x66);
                Assert.That(scenario.Instance.TryReset(out failure), Is.True, failure.Code.ToString());
                AssertMemory(scenario, 0, 16, 0);
                AssertObserverBinding(scenario);
            }
        }

        [Test]
        public void ActiveJobLeaseRejectsMutationDisposeAndEarlyRelease()
        {
            using (var scenario = Fixture.CreateScenario())
            {
                Assert.That(scenario.Instance.TryAcquireExecutionLease(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                var job = new DelayedProjectionJob
                {
                    Program = scenario.ProgramLease.View,
                    Instance = execution.View,
                };
                var handle = job.Schedule();
                Assert.That(scenario.Instance.TryRegisterDependency(execution, handle, out failure), Is.True);
                Assert.That(scenario.ProgramOwner.TryRegisterDependency(scenario.ProgramLease, handle, out failure), Is.True);
                Assert.That(scenario.Instance.TryRegisterDependency(execution, default, out failure), Is.True);
                Assert.That(scenario.ProgramOwner.TryRegisterDependency(scenario.ProgramLease, default, out failure), Is.True);

                Assert.That(scenario.Instance.TryDispose(out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
                Assert.That(scenario.Instance.TryReset(out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
                Assert.That(scenario.ProgramOwner.TryReleaseReadLease(scenario.ProgramLease, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));

                handle.Complete();
                Assert.That(execution.View.NodeMemory[0], Is.EqualTo(2));
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.ProgramOwner.TryReleaseReadLease(scenario.ProgramLease, out failure), Is.True, failure.Code.ToString());
                scenario.ProgramLeaseReleased = true;
            }
        }

        [Test]
        public void StaleForeignAndDoubleUseAreRejectedBeforeStateChanges()
        {
            using (var left = Fixture.CreateScenario())
            using (var right = Fixture.CreateScenario())
            {
                Assert.That(left.Instance.TryAcquireExecutionLease(
                    right.ProgramLease, out _, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                Assert.That(left.Instance.State, Is.EqualTo(NativeOwnerStateV1.Initialized));

                Assert.That(left.Instance.TryAcquireExecutionLease(
                    left.ProgramLease, out var execution, out failure), Is.True);
                Assert.That(left.Instance.TryAcquireExecutionLease(
                    left.ProgramLease, out _, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
                Assert.That(left.Instance.TryReleaseExecutionLease(execution, out failure), Is.True);
                Assert.That(left.Instance.TryReleaseExecutionLease(execution, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
            }
        }

        [Test]
        public void DisposeAndUseAfterDisposeAreDeterministicallyRejected()
        {
            var scenario = Fixture.CreateScenario();
            Assert.That(scenario.Instance.TryDispose(out var failure), Is.True);
            scenario.InstanceDisposed = true;
            Assert.That(scenario.Instance.TryDispose(out failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
            Assert.That(scenario.Instance.TryAcquireExecutionLease(
                scenario.ProgramLease, out _, out failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));

            Assert.That(scenario.ProgramOwner.TryReleaseReadLease(scenario.ProgramLease, out failure), Is.True);
            scenario.ProgramLeaseReleased = true;
            Assert.That(scenario.ProgramOwner.TryDispose(out failure), Is.True);
            Assert.That(scenario.ProgramOwner.TryDispose(out failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
        }

        [Test]
        public void FailedInitializationRollsBackEveryPartialNativeAllocation()
        {
            var program = Fixture.CreateProgram();
            for (var failAfter = 0; failAfter < 10; failAfter++)
            {
                Assert.That(NativeProgramImageOwnerV1.TryCreate(
                    program,
                    NativeProgramImageCapacityV1.Exact(program),
                    Allocator.Persistent,
                    failAfter,
                    out var failedOwner,
                    out var failure), Is.False);
                Assert.That(failedOwner, Is.Null);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeProgramCapacityExceeded));
            }

            Assert.That(NativeProgramImageOwnerV1.TryCreate(
                program, NativeProgramImageCapacityV1.Exact(program), Allocator.Persistent,
                out var programOwner, out var programFailure), Is.True);
            try
            {
                Assert.That(programOwner.TryAcquireReadLease(out var lease, out programFailure), Is.True);
                Assert.That(NativeInstanceArenaCapacityV1.TryDerive(lease.View, out var capacity, out programFailure), Is.True);
                for (var failAfter = 0; failAfter < 8; failAfter++)
                {
                    Assert.That(NativeInstanceArenaOwnerV1.TryCreate(
                        lease, capacity, Allocator.Persistent, failAfter,
                        out var failedInstance, out var failure), Is.False);
                    Assert.That(failedInstance, Is.Null);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded));
                }

                Assert.That(programOwner.TryReleaseReadLease(lease, out programFailure), Is.True);
            }
            finally
            {
                Assert.That(programOwner.TryDispose(out programFailure), Is.True);
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [Test]
        public void DisposedArenaViewIsRejectedByUnitySafetyChecks()
        {
            var scenario = Fixture.CreateScenario();
            Assert.That(scenario.Instance.TryAcquireExecutionLease(
                scenario.ProgramLease, out var execution, out var failure), Is.True);
            var staleView = execution.View;
            Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True);
            Assert.That(scenario.Instance.TryDispose(out failure), Is.True);
            scenario.InstanceDisposed = true;
            Assert.Catch<InvalidOperationException>(() => _ = staleView.NodeMemory[0]);
            scenario.Dispose();
        }
#endif

        private static void WriteNodeMemory(Scenario scenario, byte activation, byte instance)
        {
            Assert.That(scenario.Instance.TryAcquireExecutionLease(
                scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
            var nodeMemory = execution.View.NodeMemory;
            for (var index = 0; index < 8; index++) nodeMemory[index] = activation;
            for (var index = 8; index < 16; index++) nodeMemory[index] = instance;
            Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
        }

        private static void AssertMemory(Scenario scenario, int offset, int count, byte expected)
        {
            Assert.That(scenario.Instance.TryAcquireExecutionLease(
                scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
            for (var index = offset; index < offset + count; index++)
            {
                Assert.That(execution.View.NodeMemory[index], Is.EqualTo(expected), "byte " + index);
            }

            Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
        }

        private static void AssertObserverBinding(Scenario scenario)
        {
            Assert.That(scenario.Instance.TryAcquireExecutionLease(
                scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
            Assert.That(execution.View.Observers[0].ObserverNodeIndex, Is.EqualTo(1));
            Assert.That(execution.View.Observers[0].OwningReactiveCompositeIndex, Is.Zero);
            Assert.That(execution.View.Observers[0].HasLastConditionResult, Is.Zero);
            Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
        }

        private struct DelayedProjectionJob : IJob
        {
            [ReadOnly] public NativeProgramImageViewV1 Program;
            public NativeInstanceArenaViewV1 Instance;

            public void Execute()
            {
                Thread.Sleep(100);
                var nodeMemory = Instance.NodeMemory;
                nodeMemory[0] = (byte)Program.Nodes.Length;
            }
        }

        private sealed class Scenario : IDisposable
        {
            public NativeProgramImageOwnerV1 ProgramOwner;
            public NativeProgramReadLeaseV1 ProgramLease;
            public NativeInstanceArenaOwnerV1 Instance;
            public bool ProgramLeaseReleased;
            public bool InstanceDisposed;

            public void Dispose()
            {
                if (!InstanceDisposed)
                {
                    Assert.That(Instance.TryDispose(out var failure), Is.True, failure.Code.ToString());
                    InstanceDisposed = true;
                }

                if (!ProgramLeaseReleased)
                {
                    Assert.That(ProgramOwner.TryReleaseReadLease(ProgramLease, out var failure), Is.True, failure.Code.ToString());
                    ProgramLeaseReleased = true;
                }

                Assert.That(ProgramOwner.TryDispose(out var disposeFailure), Is.True, disposeFailure.Code.ToString());
            }
        }

        private static class Fixture
        {
            internal static Scenario CreateScenario()
            {
                var program = CreateProgram();
                Assert.That(NativeProgramImageOwnerV1.TryCreate(
                    program, NativeProgramImageCapacityV1.Exact(program), Allocator.Persistent,
                    out var owner, out var failure), Is.True, failure.Code.ToString());
                Assert.That(owner.TryAcquireReadLease(out var lease, out failure), Is.True, failure.Code.ToString());
                Assert.That(NativeInstanceArenaCapacityV1.TryDerive(lease.View, out var capacity, out failure), Is.True);
                Assert.That(NativeInstanceArenaOwnerV1.TryCreate(
                    lease, capacity, Allocator.Persistent, out var instance, out failure), Is.True, failure.Code.ToString());
                return new Scenario { ProgramOwner = owner, ProgramLease = lease, Instance = instance };
            }

            internal static CompiledProgram CreateProgram(bool useIncorrectContentHash = false)
            {
                var nodes = new[]
                {
                    new CompiledNodeRecord(
                        7, 1, 0, 4, 4, 0, 8, 8, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 1), CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                        0, new CompiledRange(0, 1), default),
                    new CompiledNodeRecord(
                        8, 1, 4, 4, 4, 8, 8, 8, NodeMemoryLifetime.Instance,
                        default, CompiledNodeFlags.BurstDomain, 1, default, new CompiledRange(0, 1)),
                };
                var children = new uint[] { 1 };
                var readSlots = new uint[] { 0 };
                var slots = new[]
                {
                    new CompiledBlackboardSlotRecord(
                        10, BuiltInBlackboardTypes.Int32.TypeId, BuiltInBlackboardTypes.Int32.Version,
                        0, BlackboardScope.Tree, 0, 4, 4, 0,
                        CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write | CompiledBlackboardAccessFlags.Observed),
                };
                var observers = new[]
                {
                    new CompiledObserverRecord(1, 0, CompiledObserverMode.Self, new CompiledRange(0, 1)),
                };
                var watchedSlots = new uint[] { 0 };
                var config = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
                var defaults = new byte[] { 9, 10, 11, 12 };
                var debug = new[]
                {
                    new CompiledDebugMapEntry(0, new NodeId("root"), "/tree/root", "Root display"),
                    new CompiledDebugMapEntry(1, new NodeId("leaf"), "/tree/leaf", "Leaf display"),
                };

                var preliminary = BuildProgram(Hash('d'), nodes, children, readSlots, slots, observers, watchedSlots, config, defaults, debug);
                var contentHash = useIncorrectContentHash ? Hash('d') : CompiledProgramContentHashV1.Compute(preliminary);
                return BuildProgram(contentHash, nodes, children, readSlots, slots, observers, watchedSlots, config, defaults, debug);
            }

            internal static NativeProgramImageCapacityV1 Reduce(
                NativeProgramImageCapacityV1 value,
                NativeResourceKindV1 resource)
                => new NativeProgramImageCapacityV1(
                    resource == NativeResourceKindV1.ProgramNodes ? value.NodeRecords - 1 : value.NodeRecords,
                    resource == NativeResourceKindV1.ProgramChildIndices ? value.ChildIndices - 1 : value.ChildIndices,
                    resource == NativeResourceKindV1.ProgramReadSlotIndices ? value.ReadSlotIndices - 1 : value.ReadSlotIndices,
                    resource == NativeResourceKindV1.ProgramWriteSlotIndices ? value.WriteSlotIndices - 1 : value.WriteSlotIndices,
                    resource == NativeResourceKindV1.ProgramBlackboardSlots ? value.BlackboardSlots - 1 : value.BlackboardSlots,
                    resource == NativeResourceKindV1.ProgramObservers ? value.Observers - 1 : value.Observers,
                    resource == NativeResourceKindV1.ProgramWatchedSlotIndices ? value.WatchedSlotIndices - 1 : value.WatchedSlotIndices,
                    resource == NativeResourceKindV1.ProgramConfigBytes ? value.ConfigBytes - 1 : value.ConfigBytes,
                    resource == NativeResourceKindV1.ProgramDefaultBytes ? value.DefaultBytes - 1 : value.DefaultBytes,
                    resource == NativeResourceKindV1.ProgramDebugOrdinals ? value.DebugOrdinals - 1 : value.DebugOrdinals,
                    value.MaximumAlignment);

            internal static NativeInstanceArenaCapacityV1 Reduce(
                NativeInstanceArenaCapacityV1 value,
                NativeResourceKindV1 resource)
                => new NativeInstanceArenaCapacityV1(
                    resource == NativeResourceKindV1.InstanceNodeMemory ? value.NodeMemoryBytes - 1 : value.NodeMemoryBytes,
                    resource == NativeResourceKindV1.InstanceTreeBlackboard ? value.TreeBlackboardBytes - 1 : value.TreeBlackboardBytes,
                    resource == NativeResourceKindV1.InstanceFrames ? value.FrameCount - 1 : value.FrameCount,
                    resource == NativeResourceKindV1.InstanceGenerations ? value.GenerationCount - 1 : value.GenerationCount,
                    resource == NativeResourceKindV1.InstanceParallelBranches ? value.ParallelBranchCapacity - 1 : value.ParallelBranchCapacity,
                    resource == NativeResourceKindV1.InstanceObservers ? value.ObserverCount - 1 : value.ObserverCount,
                    resource == NativeResourceKindV1.InstanceUpdateState ? value.UpdateStateCount - 1 : value.UpdateStateCount,
                    resource == NativeResourceKindV1.InstanceBudgetState ? value.BudgetStateCount - 1 : value.BudgetStateCount,
                    value.MaximumAlignment);

            private static CompiledProgram BuildProgram(
                CompiledHash contentHash,
                CompiledNodeRecord[] nodes,
                uint[] children,
                uint[] readSlots,
                CompiledBlackboardSlotRecord[] slots,
                CompiledObserverRecord[] observers,
                uint[] watchedSlots,
                byte[] config,
                byte[] defaults,
                CompiledDebugMapEntry[] debug)
            {
                var header = new CompiledProgramHeader(
                    1, 1, new CompiledCompilerVersion(1, 2, 3, 4),
                    Hash('a'), Hash('b'), Hash('c'), 1, contentHash,
                    0, (uint)nodes.Length, (uint)children.Length, (uint)slots.Length, (uint)debug.Length,
                    (uint)config.Length, 16, 8, 3, true);
                return new CompiledProgram(
                    header, nodes, children, readSlots, readSlots, slots, observers,
                    watchedSlots, config, defaults, debug);
            }

            private static CompiledHash Hash(char value) => new CompiledHash(new string(value, 64));
        }
    }
}
