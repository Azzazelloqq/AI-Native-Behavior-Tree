using System;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.NativeExecution.Dispatch
{
    [TestFixture]
    public sealed class NativeBurstDispatchCanonicalMemoryTests
    {
        private const ulong TypeNumericId = 0x995a_b19e_09db_a139UL;

        [Test]
        public void GeneratedOpaqueMemory_AllZeroIngressAndExplicitCommitAreAccepted()
        {
            using (var input = new GeneratedOpaqueStorageInput(false, OpaqueCorruption.None))
            {
                var value = input.Value;
                Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                    in value,
                    Allocator.Persistent,
                    out var owner,
                    out var failure), Is.True, failure.ToString());
                try
                {
                    AcquireFrame(owner, out var batch, out var frame);
                    Assert.That(WriteOpaqueMemory(ref frame, OpaqueCorruption.None),
                        Is.EqualTo(BurstContextResult.Success));
                    Assert.That(BurstGeneratedRuntimeBridge.TryCreateEnterContext(
                        in frame, out var context), Is.EqualTo(BurstContextResult.Success));
                    Assert.That(BurstGeneratedRuntimeBridge.TryCompleteEnter(
                        ref batch, in frame, ref context), Is.EqualTo(BurstContextResult.Success));

                    for (uint byteIndex = 0; byteIndex < 56u; byteIndex++)
                    {
                        Assert.That(owner.TryReadCommittedMemoryByte(0u, byteIndex, out var stored),
                            Is.True);
                        Assert.That(stored, Is.Zero, "byte " + byteIndex);
                    }
                }
                finally
                {
                    Assert.That(owner.TryDispose(out failure), Is.True, failure.ToString());
                }
            }
        }

        [TestCase(OpaqueCorruption.OperationNodeSentinel)]
        [TestCase(OpaqueCorruption.AssetAbsentLocalWithValue)]
        public void GeneratedOpaqueMemory_PartialInvalidIngressIsRejected(
            OpaqueCorruption corruption)
        {
            using (var input = new GeneratedOpaqueStorageInput(false, corruption))
            {
                var value = input.Value;
                Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                    in value,
                    Allocator.Persistent,
                    out var owner,
                    out var failure), Is.False);
                Assert.That(owner, Is.Null);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
            }
        }

        [TestCase(OpaqueCorruption.OperationNodeSentinel)]
        [TestCase(OpaqueCorruption.AssetAbsentLocalWithValue)]
        public void GeneratedOpaqueMemory_PartialInvalidCommitIsRejected(
            OpaqueCorruption corruption)
        {
            using (var input = new GeneratedOpaqueStorageInput(false, OpaqueCorruption.None))
            {
                var value = input.Value;
                Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                    in value,
                    Allocator.Persistent,
                    out var owner,
                    out var failure), Is.True, failure.ToString());
                try
                {
                    AcquireFrame(owner, out var batch, out var frame);
                    failure = WriteOpaqueMemory(ref frame, corruption);
                    Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
                    Assert.That(BurstGeneratedRuntimeBridge.TryFailDispatch(
                        ref batch, in frame, failure), Is.EqualTo(failure));
                }
                finally
                {
                    Assert.That(owner.TryDispose(out failure), Is.True, failure.ToString());
                }
            }
        }

        [Test]
        public void OpaqueConfiguration_DoesNotAcceptTheMemoryOnlyZeroSentinel()
        {
            using (var input = new GeneratedOpaqueStorageInput(true, OpaqueCorruption.None))
            {
                var value = input.Value;
                Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                    in value,
                    Allocator.Persistent,
                    out var owner,
                    out var failure), Is.False);
                Assert.That(owner, Is.Null);
                Assert.That(failure, Is.EqualTo(BurstContextResult.InvalidEncoding));
            }
        }

        [Test]
        public void OpaqueConfiguration_ValidNonzeroRootsReachStrictByteValidation()
        {
            using (var input = new GeneratedOpaqueStorageInput(true, OpaqueCorruption.ValidStrict))
            {
                var value = input.Value;
                Assert.That(NativeBurstDispatchBatchOwnerV2.TryCreate(
                    in value,
                    Allocator.Persistent,
                    out var owner,
                    out var failure), Is.True, failure.ToString());
                Assert.That(owner.TryDispose(out failure), Is.True, failure.ToString());
            }
        }

        private static void AcquireFrame(
            NativeBurstDispatchBatchOwnerV2 owner,
            out BurstExecutionBatch batch,
            out BurstDispatchFrame frame)
        {
            Assert.That(owner.TryAcquireImmediateBatch(out batch), Is.True);
            Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                in batch,
                out var instanceOrdinal,
                out var runtimeNodeIndex,
                out var catalogCaseIndex,
                out var phase,
                out var hasWork), Is.EqualTo(BurstContextResult.Success));
            Assert.That(hasWork, Is.True);
            Assert.That(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                ref batch,
                instanceOrdinal,
                runtimeNodeIndex,
                catalogCaseIndex,
                phase,
                out frame), Is.EqualTo(BurstContextResult.Success));
        }

        private static BurstContextResult WriteOpaqueMemory(
            ref BurstDispatchFrame frame,
            OpaqueCorruption corruption)
        {
            var result = BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(
                in frame, out var memory);
            if (result != BurstContextResult.Success)
            {
                return result;
            }

            var operationTree = corruption == OpaqueCorruption.OperationNodeSentinel ? 1UL : 0UL;
            var operationNode = corruption == OpaqueCorruption.OperationNodeSentinel
                ? uint.MaxValue
                : 0u;
            var assetGuidLow = corruption == OpaqueCorruption.AssetAbsentLocalWithValue ? 1UL : 0UL;
            var assetLocal = corruption == OpaqueCorruption.AssetAbsentLocalWithValue ? 1L : 0L;
            result = BurstGeneratedRuntimeBridge.TryWriteMemoryUInt64(
                ref memory, 0u, 0u, operationTree);
            if (result != BurstContextResult.Success) return result;
            result = BurstGeneratedRuntimeBridge.TryWriteMemoryUInt32(
                ref memory, 0u, 1u, operationNode);
            if (result != BurstContextResult.Success) return result;
            result = BurstGeneratedRuntimeBridge.TryWriteMemoryUInt32(
                ref memory, 0u, 2u, 0u);
            if (result != BurstContextResult.Success) return result;
            result = BurstGeneratedRuntimeBridge.TryWriteMemoryUInt64(
                ref memory, 0u, 3u, 0UL);
            if (result != BurstContextResult.Success) return result;
            result = BurstGeneratedRuntimeBridge.TryWriteMemoryUInt64(
                ref memory, 1u, 0u, assetGuidLow);
            if (result != BurstContextResult.Success) return result;
            result = BurstGeneratedRuntimeBridge.TryWriteMemoryUInt64(
                ref memory, 1u, 1u, 0UL);
            if (result != BurstContextResult.Success) return result;
            result = BurstGeneratedRuntimeBridge.TryWriteMemoryInt64(
                ref memory, 1u, 2u, assetLocal);
            if (result != BurstContextResult.Success) return result;
            result = BurstGeneratedRuntimeBridge.TryWriteMemoryBoolean(
                ref memory, 1u, 3u, false);
            return result == BurstContextResult.Success
                ? BurstGeneratedRuntimeBridge.TryCommitMemory(ref memory)
                : result;
        }

        public enum OpaqueCorruption : byte
        {
            None = 0,
            OperationNodeSentinel = 1,
            AssetAbsentLocalWithValue = 2,
            ValidStrict = 3
        }

        private sealed class GeneratedOpaqueStorageInput : IDisposable
        {
            private readonly NativeArray<NativeBurstDispatchCaseV2> _cases;
            private readonly NativeArray<NativeBurstDispatchRequestV2> _requests;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
            private readonly NativeArray<byte> _configurationBytes;
            private readonly NativeArray<byte> _memoryBytes;
            private readonly NativeArray<ulong> _randomStates;
            private readonly NativeArray<ulong> _randomIncrements;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRuleV2> _rules;

            internal GeneratedOpaqueStorageInput(
                bool configurationStorage,
                OpaqueCorruption corruption)
            {
                _cases = new NativeArray<NativeBurstDispatchCaseV2>(1, Allocator.Temp);
                _requests = new NativeArray<NativeBurstDispatchRequestV2>(1, Allocator.Temp);
                _configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(
                    configurationStorage ? 7 : 0, Allocator.Temp);
                _memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(
                    configurationStorage ? 0 : 7, Allocator.Temp);
                _configurationBytes = new NativeArray<byte>(
                    configurationStorage ? 56 : 0, Allocator.Temp);
                _memoryBytes = new NativeArray<byte>(
                    configurationStorage ? 0 : 56, Allocator.Temp);
                _randomStates = new NativeArray<ulong>(0, Allocator.Temp);
                _randomIncrements = new NativeArray<ulong>(0, Allocator.Temp);
                _caseRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(2, Allocator.Temp);
                _bindingRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(0, Allocator.Temp);
                _rules = new NativeArray<NativeBurstDispatchCanonicalRuleV2>(2, Allocator.Temp);

                _cases[0] = new NativeBurstDispatchCaseV2(
                    TypeNumericId,
                    1u,
                    0u,
                    0u,
                    configurationStorage ? 7u : 0u,
                    configurationStorage ? 56u : 0u,
                    0u,
                    configurationStorage ? 0u : 7u,
                    configurationStorage ? 0u : 56u,
                    NativeBurstDispatchPhaseMaskV2.Enter,
                    BurstNodeStatusMask.Success,
                    false);
                _requests[0] = new NativeBurstDispatchRequestV2(
                    0u,
                    7u,
                    TypeNumericId,
                    1u,
                    0u,
                    BurstCallbackPhase.Enter,
                    0u,
                    0u,
                    0u,
                    123L,
                    new TreeInstanceId(81UL),
                    1u,
                    0u,
                    0u);

                var fields = configurationStorage ? _configurationFields : _memoryFields;
                fields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 0u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64,
                    NativeBurstDispatchCanonicalRuleKindV2.OperationId);
                fields[1] = new NativeBurstDispatchFieldV2(
                    0u, 1u, 8u, 2u, 4u, NativeBurstDispatchFieldEncodingV2.UInt32);
                fields[2] = new NativeBurstDispatchFieldV2(
                    0u, 3u, 16u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64);
                fields[3] = new NativeBurstDispatchFieldV2(
                    1u, 0u, 24u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64,
                    NativeBurstDispatchCanonicalRuleKindV2.AssetId);
                fields[4] = new NativeBurstDispatchFieldV2(
                    1u, 1u, 32u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64);
                fields[5] = new NativeBurstDispatchFieldV2(
                    1u, 2u, 40u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.Int64);
                fields[6] = new NativeBurstDispatchFieldV2(
                    1u, 3u, 48u, 1u, 1u, NativeBurstDispatchFieldEncodingV2.Boolean);

                _caseRanges[0] = configurationStorage
                    ? new NativeBurstDispatchCanonicalRangeV2(0u, 2u)
                    : new NativeBurstDispatchCanonicalRangeV2(0u, 0u);
                _caseRanges[1] = configurationStorage
                    ? new NativeBurstDispatchCanonicalRangeV2(2u, 0u)
                    : new NativeBurstDispatchCanonicalRangeV2(0u, 2u);
                _rules[0] = new NativeBurstDispatchCanonicalRuleV2(
                    NativeBurstDispatchCanonicalRuleKindV2.OperationId, 0u);
                _rules[1] = new NativeBurstDispatchCanonicalRuleV2(
                    NativeBurstDispatchCanonicalRuleKindV2.AssetId, 24u);

                var storage = configurationStorage ? _configurationBytes : _memoryBytes;
                if (corruption == OpaqueCorruption.ValidStrict)
                {
                    WriteUInt64(storage, 0, 1UL);
                    WriteUInt64(storage, 24, 1UL);
                }
                else if (corruption == OpaqueCorruption.OperationNodeSentinel)
                {
                    WriteUInt64(storage, 0, 1UL);
                    WriteUInt32(storage, 8, uint.MaxValue);
                }
                else if (corruption == OpaqueCorruption.AssetAbsentLocalWithValue)
                {
                    WriteUInt64(storage, 24, 1UL);
                    WriteUInt64(storage, 40, 1UL);
                }

                var canonical = new NativeBurstDispatchCanonicalInputV2(
                    _caseRanges.AsReadOnly(),
                    _bindingRanges.AsReadOnly(),
                    _rules.AsReadOnly());
                Value = new NativeBurstDispatchCreateInputV2(
                    Handshake(),
                    _cases.AsReadOnly(),
                    _requests.AsReadOnly(),
                    _configurationFields.AsReadOnly(),
                    _memoryFields.AsReadOnly(),
                    _configurationBytes.AsReadOnly(),
                    _memoryBytes.AsReadOnly(),
                    _randomStates.AsReadOnly(),
                    _randomIncrements.AsReadOnly(),
                    default,
                    canonical);
            }

            internal NativeBurstDispatchCreateInputV2 Value { get; }

            public void Dispose()
            {
                _rules.Dispose();
                _bindingRanges.Dispose();
                _caseRanges.Dispose();
                _randomIncrements.Dispose();
                _randomStates.Dispose();
                _memoryBytes.Dispose();
                _configurationBytes.Dispose();
                _memoryFields.Dispose();
                _configurationFields.Dispose();
                _requests.Dispose();
                _cases.Dispose();
            }
        }

        private static BurstCatalogHandshake Handshake()
            => new BurstCatalogHandshake(
                2u,
                new BurstCatalogFingerprint(Hash(0x10u)),
                Hash(0x20u),
                1u,
                1u,
                Hash(0x30u),
                Hash(0x40u),
                Hash(0x50u));

        private static BurstHash256 Hash(uint firstWord)
            => new BurstHash256(
                firstWord,
                firstWord + 1u,
                firstWord + 2u,
                firstWord + 3u,
                firstWord + 4u,
                firstWord + 5u,
                firstWord + 6u,
                firstWord + 7u);

        private static void WriteUInt32(NativeArray<byte> bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt64(NativeArray<byte> bytes, int offset, ulong value)
        {
            WriteUInt32(bytes, offset, (uint)value);
            WriteUInt32(bytes, offset + 4, (uint)(value >> 32));
        }
    }
}
