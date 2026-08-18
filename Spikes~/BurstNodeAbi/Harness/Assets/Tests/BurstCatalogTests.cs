using AIBT.Burst;
using AIBT.BurstAbi.Feasibility;
using AIBT.BurstAbi.Catalog;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using System.Reflection;

namespace AIBT.BurstAbi.Tests
{
    public sealed class BurstCatalogTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct UndefinedResultCodeJob : IJob
        {
            public NativeArray<int> Result;
            public void Execute()
            {
                var validation = new BurstCatalogValidationResult((BurstCatalogValidationCode)255, 5012);
                var execution = new BurstExecutionResult((BurstExecutionCode)255, 5007, 1u, 2UL);
                Result[0] = (byte)validation.Code;
                Result[1] = validation.Success ? 1 : 0;
                Result[2] = (byte)execution.Code;
                Result[3] = execution.Success ? 1 : 0;
            }
        }

        [Test]
        public void ExactFacade_BurstExecutesActionAndObserverCases()
        {
            Assert.That(BurstCompiler.IsEnabled, Is.True);
            AssertExecution(0u, BurstCallbackPhase.Tick, 1u, 2u, 1u, 0u, BurstExecutionCode.Success);
            AssertExecution(2u, BurstCallbackPhase.Observer, 1u, 0u, 0u, 0u, BurstExecutionCode.Success);
        }

        [Test]
        public void PublicAbiLayout_MatchesUnsafeUtilityAndFrozenOffsets()
        {
            Assert.That(UnsafeUtility.SizeOf<BurstHash256>(), Is.EqualTo(32));
            Assert.That(UnsafeUtility.SizeOf<BurstCatalogFingerprint>(), Is.EqualTo(32));
            Assert.That(UnsafeUtility.SizeOf<BurstCatalogHandshake>(), Is.EqualTo(172));
            Assert.That(UnsafeUtility.SizeOf<BurstCatalogValidationResult>(), Is.EqualTo(4));
            Assert.That(System.Runtime.InteropServices.Marshal.SizeOf<BurstCatalogValidationResult>(), Is.EqualTo(4));
            AssertOffset<BurstCatalogValidationResult>("_codeWord", 0);
            AssertOffset<BurstCatalogValidationResult>("_diagnosticNumber", 2);
            Assert.That(UnsafeUtility.SizeOf<BurstExecutionResult>(), Is.EqualTo(16));
            Assert.That(System.Runtime.InteropServices.Marshal.SizeOf<BurstExecutionResult>(), Is.EqualTo(16));
            AssertOffset<BurstExecutionResult>("_codeWord", 0);
            AssertOffset<BurstExecutionResult>("_diagnosticNumber", 2);
            AssertOffset<BurstExecutionResult>("_instancesVisited", 4);
            AssertOffset<BurstExecutionResult>("_segmentSteps", 8);
            Assert.That(UnsafeUtility.SizeOf<BurstEnterContext>(), Is.EqualTo(24));
            Assert.That(UnsafeUtility.SizeOf<BurstTickContext>(), Is.EqualTo(24));
            AssertOffset<BurstEnterContext>("_validationToken", 0); AssertOffset<BurstEnterContext>("_randomState", 8); AssertOffset<BurstEnterContext>("_randomIncrement", 16);
            AssertOffset<BurstTickContext>("_validationToken", 0); AssertOffset<BurstTickContext>("_randomState", 8); AssertOffset<BurstTickContext>("_randomIncrement", 16);
            AssertOffset<BurstCatalogHandshake>("<AbiVersion>k__BackingField", 0); AssertOffset<BurstCatalogHandshake>("<Catalog>k__BackingField", 4);
            AssertOffset<BurstCatalogHandshake>("<NodeRegistry>k__BackingField", 36); AssertOffset<BurstCatalogHandshake>("<CompiledFormatVersion>k__BackingField", 68);
            AssertOffset<BurstCatalogHandshake>("<ExecutionSemanticsVersion>k__BackingField", 72); AssertOffset<BurstCatalogHandshake>("<ConfigurationLayout>k__BackingField", 76);
            AssertOffset<BurstCatalogHandshake>("<MemoryLayout>k__BackingField", 108); AssertOffset<BurstCatalogHandshake>("<AccessLayout>k__BackingField", 140);
            AssertResultBytesHaveCanonicalZeroPadding();
        }

        [Test]
        public void ResultCodes_UndefinedValuesRemainNonSuccess_InManagedAndBurst()
        {
            var validation = new BurstCatalogValidationResult((BurstCatalogValidationCode)255, 5012);
            var execution = new BurstExecutionResult((BurstExecutionCode)255, 5007, 1u, 2UL);
            Assert.That((byte)validation.Code, Is.EqualTo(255)); Assert.That(validation.Success, Is.False);
            Assert.That((byte)execution.Code, Is.EqualTo(255)); Assert.That(execution.Success, Is.False);
            using (var result = new NativeArray<int>(4, Allocator.TempJob))
            {
                new UndefinedResultCodeJob { Result = result }.Schedule().Complete();
                Assert.That(result[0], Is.EqualTo(255)); Assert.That(result[1], Is.Zero);
                Assert.That(result[2], Is.EqualTo(255)); Assert.That(result[3], Is.Zero);
            }
        }

        [Test]
        public void ResultBridges_RejectUndefinedOrNonCanonicalCodeWords_WithoutMutation()
        {
            var handshake = ExpectedHandshake();
            var batch = Batch(in handshake, 0u, BurstCallbackPhase.Tick, 0u, 0u, 0u, 0u);
            var initialCallbacks = BurstContractTestSeam.CallbackCount(in batch);
            var undefinedValidation = BurstContractTestSeam.ForgeValidationResult(0x00ff, 5012);
            var nonCanonicalValidation = BurstContractTestSeam.ForgeValidationResult(0x0101, 5012);
            Assert.That(BurstGeneratedRuntimeBridge.TryRejectBatch(ref batch, in undefinedValidation), Is.EqualTo(BurstContextResult.InvalidStatus));
            Assert.That(BurstGeneratedRuntimeBridge.TryRejectBatch(ref batch, in nonCanonicalValidation), Is.EqualTo(BurstContextResult.InvalidStatus));
            Assert.That(BurstContractTestSeam.CallbackCount(in batch), Is.EqualTo(initialCallbacks));
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(in batch, out _, out _, out _, out _, out var hasWork), Is.EqualTo(BurstContextResult.Success));
            Assert.That(hasWork, Is.True);

            BurstContractTestSeam.SetTerminalExecutionResult(ref batch, 0x00ff, 5007, 3u, 4UL);
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in batch, out var rejected), Is.EqualTo(BurstContextResult.InvalidStatus));
            Assert.That(rejected.Code, Is.EqualTo(BurstExecutionCode.Success));
            BurstContractTestSeam.SetTerminalExecutionResult(ref batch, 0x0101, 5007, 3u, 4UL);
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in batch, out rejected), Is.EqualTo(BurstContextResult.InvalidStatus));
            Assert.That(rejected.Code, Is.EqualTo(BurstExecutionCode.Success));
            Assert.That(BurstContractTestSeam.CallbackCount(in batch), Is.EqualTo(initialCallbacks));
        }

        [TestCase(BurstCatalogValidationCode.AbiVersionMismatch)]
        [TestCase(BurstCatalogValidationCode.CatalogMismatch)]
        [TestCase(BurstCatalogValidationCode.RegistryMismatch)]
        [TestCase(BurstCatalogValidationCode.CompiledFormatMismatch)]
        [TestCase(BurstCatalogValidationCode.SemanticsMismatch)]
        [TestCase(BurstCatalogValidationCode.ConfigurationLayoutMismatch)]
        [TestCase(BurstCatalogValidationCode.MemoryLayoutMismatch)]
        [TestCase(BurstCatalogValidationCode.AccessLayoutMismatch)]
        public void HandshakeMismatch_RejectsBeforeCallbackAndSchedule(BurstCatalogValidationCode expected)
        {
            var handshake = Mutate(ExpectedHandshake(), expected);
            var validation = GeneratedCatalog.Validate(in handshake);
            Assert.That(validation.Code, Is.EqualTo(expected));
            Assert.That(validation.DiagnosticNumber, Is.EqualTo(5012));

            var batch = Batch(in handshake, 0u, BurstCallbackPhase.Tick, 1u, 2u, 1u, 0u);
            var result = GeneratedCatalog.ExecuteImmediate(ref batch);
            Assert.That(result.Code, Is.EqualTo(BurstExecutionCode.ValidationFailed));
            Assert.That(result.DiagnosticNumber, Is.EqualTo(5012));
            Assert.That(result.InstancesVisited, Is.Zero);
            Assert.That(result.SegmentSteps, Is.Zero);
            Assert.That(BurstContractTestSeam.CallbackCount(in batch), Is.Zero);

            var scheduled = Batch(in handshake, 0u, BurstCallbackPhase.Tick, 1u, 2u, 1u, 0u);
            var dependency = default(JobHandle);
            Assert.That(GeneratedCatalog.Schedule(ref scheduled, dependency), Is.EqualTo(dependency));
            Assert.That(BurstContractTestSeam.CallbackCount(in scheduled), Is.Zero);
        }

        [Test]
        public void DefaultAndForgedBatches_ReturnFixedValidationFailure()
        {
            var empty = default(BurstExecutionBatch);
            AssertValidationFailure(GeneratedCatalog.ExecuteImmediate(ref empty));
            var handshake = ExpectedHandshake();
            var forged = Batch(in handshake, 0u, BurstCallbackPhase.Tick, 0u, 0u, 0u, 0u);
            BurstContractTestSeam.InvalidateToken(ref forged);
            AssertValidationFailure(GeneratedCatalog.ExecuteImmediate(ref forged));
            Assert.That(BurstContractTestSeam.CallbackCount(in forged), Is.Zero);
        }

        [Test]
        public void Schedule_SharesRuntimeOwnedTerminalResult_AndRejectsDuplicate()
        {
            var handshake = ExpectedHandshake();
            var batch = Batch(in handshake, 0u, BurstCallbackPhase.Tick, 1u, 2u, 1u, 0u);
            var preScheduleHostCopy = batch;
            var dependency = default(JobHandle);
            var scheduled = GeneratedCatalog.Schedule(ref batch, dependency);
            Assert.That(GeneratedCatalog.Schedule(ref batch, dependency), Is.EqualTo(dependency));
            Assert.That(GeneratedCatalog.Schedule(ref preScheduleHostCopy, dependency), Is.EqualTo(dependency));
            scheduled.Complete();
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in batch, out var result), Is.EqualTo(BurstContextResult.Success));
            Assert.That(result.Code, Is.EqualTo(BurstExecutionCode.Success));
            Assert.That(result.DiagnosticNumber, Is.Zero);
            Assert.That(result.InstancesVisited, Is.EqualTo(1u));
            Assert.That(result.SegmentSteps, Is.EqualTo(1ul));
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in batch, out var repeated), Is.EqualTo(BurstContextResult.Success));
            Assert.That(repeated.Code, Is.EqualTo(result.Code)); Assert.That(repeated.InstancesVisited, Is.EqualTo(result.InstancesVisited)); Assert.That(repeated.SegmentSteps, Is.EqualTo(result.SegmentSteps));
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in preScheduleHostCopy, out var copiedHostResult), Is.EqualTo(BurstContextResult.Success));
            Assert.That(copiedHostResult.InstancesVisited, Is.EqualTo(result.InstancesVisited)); Assert.That(copiedHostResult.SegmentSteps, Is.EqualTo(result.SegmentSteps));
            BurstContractTestSeam.Release(ref batch);
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in batch, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in preScheduleHostCopy, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
        }

        [Test]
        public void ScheduledView_UsesSharedMultiRequestCursor_ThenBecomesStale()
        {
            var handshake = ExpectedHandshake();
            var host = Batch(in handshake, 0u, BurstCallbackPhase.Tick, 1u, 2u, 1u, 0u);
            BurstContractTestSeam.SetWorkCount(ref host, 2u);
            var preScheduleHostCopy = host;
            Assert.That(BurstGeneratedRuntimeBridge.TryPrepareSchedule(ref host, out var jobView), Is.EqualTo(BurstContextResult.Success));
            var copiedJobView = jobView;
            Assert.That(BurstGeneratedRuntimeBridge.TryPrepareSchedule(ref preScheduleHostCopy, out _), Is.EqualTo(BurstContextResult.PhaseViolation));
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(in host, out _, out _, out _, out _, out _), Is.EqualTo(BurstContextResult.PhaseViolation));
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(in jobView, out var firstOrdinal, out _, out _, out _, out var hasWork), Is.EqualTo(BurstContextResult.Success));
            Assert.That(firstOrdinal, Is.Zero); Assert.That(hasWork, Is.True);
            GeneratedCatalog.ExecuteImmediate(ref jobView);
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in host, out var result), Is.EqualTo(BurstContextResult.Success));
            Assert.That(result.Code, Is.EqualTo(BurstExecutionCode.Success));
            Assert.That(result.InstancesVisited, Is.EqualTo(2u)); Assert.That(result.SegmentSteps, Is.EqualTo(2UL));
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in jobView, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in copiedJobView, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(in jobView, out _, out _, out _, out _, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            BurstContractTestSeam.Release(ref host);
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in host, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in jobView, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in copiedJobView, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in preScheduleHostCopy, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
        }

        private static void AssertExecution(uint caseIndex, BurstCallbackPhase phase, ulong config0, ulong config1, ulong config2, ulong config3, BurstExecutionCode expected)
        {
            var handshake = ExpectedHandshake();
            var batch = Batch(in handshake, caseIndex, phase, config0, config1, config2, config3);
            try
            {
                GeneratedCatalog.Schedule(ref batch, default).Complete();
                Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in batch, out var result), Is.EqualTo(BurstContextResult.Success));
                Assert.That(result.Code, Is.EqualTo(expected));
                Assert.That(result.DiagnosticNumber, Is.Zero);
            }
            finally
            {
                BurstContractTestSeam.Release(ref batch);
            }
        }

        private static BurstExecutionBatch Batch(in BurstCatalogHandshake handshake, uint caseIndex, BurstCallbackPhase phase,
            ulong config0, ulong config1, ulong config2, ulong config3)
        {
            var token = ((ulong)handshake.Catalog.Value.Word1 << 32) | handshake.Catalog.Value.Word0;
            var batch = BurstContractTestSeam.RuntimeBatch(token, 3u, caseIndex, config0, config1, config2, config3, 0u, 0u, true);
            BurstContractTestSeam.SetHandshake(ref batch, in handshake);
            BurstContractTestSeam.SetExecutionRequest(ref batch, 0u, 0u, phase);
            return batch;
        }

        private static BurstCatalogHandshake ExpectedHandshake()
        {
            var catalog = new BurstHash256(0x687f9b60u, 0x037d7bf9u, 0xa1730794u, 0x129956cdu, 0xfbe03994u, 0xfd55e64bu, 0x7288bca0u, 0x22bea04bu);
            return new BurstCatalogHandshake(1u, new BurstCatalogFingerprint(catalog),
                new BurstHash256(0xf137e17eu, 0x75dc8354u, 0x641c25bdu, 0xf1f0f369u, 0xfa9d5189u, 0xf8a122c6u, 0x3f8f49e7u, 0xa6819324u),
                1u, 1u,
                new BurstHash256(0x15614b22u, 0x55ec668eu, 0x73fe8580u, 0x5dcb57dfu, 0x008c93ecu, 0x12490b41u, 0xdbcdbdbau, 0x3a4b592eu),
                new BurstHash256(0x18d88635u, 0xa48a0bdcu, 0x4082a8a1u, 0xf4bb7e7au, 0x9205b084u, 0xda8b8ba6u, 0xa2aed16eu, 0x16f2769bu),
                new BurstHash256(0x08d096f8u, 0x5c4a0d77u, 0xcab80986u, 0xcf628194u, 0xe8811010u, 0x1c6cac85u, 0x85b1b820u, 0x3c66bb61u));
        }

        private static BurstCatalogHandshake Mutate(BurstCatalogHandshake value, BurstCatalogValidationCode code)
        {
            var zero = default(BurstHash256);
            return new BurstCatalogHandshake(code == BurstCatalogValidationCode.AbiVersionMismatch ? 2u : value.AbiVersion,
                code == BurstCatalogValidationCode.CatalogMismatch ? new BurstCatalogFingerprint(zero) : value.Catalog,
                code == BurstCatalogValidationCode.RegistryMismatch ? zero : value.NodeRegistry,
                code == BurstCatalogValidationCode.CompiledFormatMismatch ? 2u : value.CompiledFormatVersion,
                code == BurstCatalogValidationCode.SemanticsMismatch ? 2u : value.ExecutionSemanticsVersion,
                code == BurstCatalogValidationCode.ConfigurationLayoutMismatch ? zero : value.ConfigurationLayout,
                code == BurstCatalogValidationCode.MemoryLayoutMismatch ? zero : value.MemoryLayout,
                code == BurstCatalogValidationCode.AccessLayoutMismatch ? zero : value.AccessLayout);
        }

        private static void AssertValidationFailure(BurstExecutionResult result)
        {
            Assert.That(result.Code, Is.EqualTo(BurstExecutionCode.ValidationFailed));
            Assert.That(result.DiagnosticNumber, Is.EqualTo(5012));
            Assert.That(result.InstancesVisited, Is.Zero);
            Assert.That(result.SegmentSteps, Is.Zero);
        }

        private static void AssertOffset<T>(string name, int expected) where T : struct
        {
            var field = typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, typeof(T).Name + "." + name);
            Assert.That(UnsafeUtility.GetFieldOffset(field), Is.EqualTo(expected), typeof(T).Name + "." + name);
        }

        private static void AssertResultBytesHaveCanonicalZeroPadding()
        {
            var validation = new BurstCatalogValidationResult((BurstCatalogValidationCode)255, 5012);
            var execution = new BurstExecutionResult((BurstExecutionCode)255, 5007, 0x11223344u, 0x0102030405060708UL);
            var validationPointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
            var executionPointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(16);
            try
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(validation, validationPointer, false);
                System.Runtime.InteropServices.Marshal.StructureToPtr(execution, executionPointer, false);
                Assert.That(System.Runtime.InteropServices.Marshal.ReadByte(validationPointer, 0), Is.EqualTo(255));
                Assert.That(System.Runtime.InteropServices.Marshal.ReadByte(validationPointer, 1), Is.Zero);
                Assert.That(System.Runtime.InteropServices.Marshal.ReadByte(executionPointer, 0), Is.EqualTo(255));
                Assert.That(System.Runtime.InteropServices.Marshal.ReadByte(executionPointer, 1), Is.Zero);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(validationPointer);
                System.Runtime.InteropServices.Marshal.FreeHGlobal(executionPointer);
            }
        }
    }
}
