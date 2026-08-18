using System;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using AIBT.Tests.CodeGen.Generation;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.NativeExecution.Dispatch
{
    [AibtCatalogSet("aibt.tests.dispatch-canary", 1u, typeof(GenerationShard))]
    public static partial class GeneratedDispatchCanaryCatalog
    {
        internal static BurstCatalogHandshake HandshakeForTests()
        {
            return new BurstCatalogHandshake(
                2u,
                Fingerprint,
                NodeRegistryFingerprint,
                1u,
                1u,
                ConfigurationLayoutFingerprint,
                MemoryLayoutFingerprint,
                AccessLayoutFingerprint);
        }
    }

    [TestFixture]
    public sealed class GeneratedCatalogDispatchCanaryTests
    {
        private const string TypeId = "aibt.tests.generated-node";
        private const uint TypeVersion = 1u;
        private const int LiveValue = 37;

        [Test]
        public void GeneratedExecuteImmediate_DecodesConfigurationAndHandle_ThenCompletesCallback()
        {
            using (var scenario = new Scenario())
            {
                var views = scenario.Views;
                Assert.That(scenario.Owner.TryBeginRequest(
                    in views,
                    out var lease,
                    out var failure), Is.True, failure.ToString());
                Assert.That(scenario.Owner.TryAcquireImmediateBatch(
                    in lease,
                    out var batch,
                    out failure), Is.True, failure.ToString());

                var execution = GeneratedDispatchCanaryCatalog.ExecuteImmediate(ref batch);

                Assert.That(execution.Code, Is.EqualTo(BurstExecutionCode.Success));
                Assert.That(execution.InstancesVisited, Is.EqualTo(1u));
                Assert.That(execution.SegmentSteps, Is.EqualTo(1UL));
                Assert.That(scenario.Owner.TryConsumeResult(
                    in lease,
                    out var result,
                    out failure), Is.True, failure.ToString());
                Assert.That(result.Execution.Code, Is.EqualTo(BurstExecutionCode.Success));
                Assert.That(result.FrameAcquired, Is.True);
                Assert.That(result.CallbackFailure, Is.EqualTo(BurstContextResult.Success));
                Assert.That(result.Status, Is.EqualTo(NodeStatus.Success));
                Assert.That(ReadUInt32(scenario.MemoryBytes, 0), Is.EqualTo((uint)LiveValue + 1u),
                    "The user callback must observe both the generated Bool config decode and generated live-handle decode.");
                Assert.That(scenario.Owner.TryReset(in lease, out failure), Is.True, failure.ToString());
            }
        }

        [Test]
        public void GeneratedSchedule_BurstJobExecutesTheSameGeneratedFacade()
        {
            using (var scenario = new Scenario())
            {
                var views = scenario.Views;
                Assert.That(scenario.Owner.TryBeginRequest(
                    in views,
                    out var lease,
                    out var failure), Is.True, failure.ToString());
                Assert.That(scenario.Owner.TryAcquireImmediateBatch(
                    in lease,
                    out var batch,
                    out failure), Is.True, failure.ToString());

                var dependency = GeneratedDispatchCanaryCatalog.Schedule(ref batch, default);
                Assert.That(scenario.Owner.TryRegisterDependency(
                    in lease,
                    in batch,
                    dependency,
                    out failure), Is.True, failure.ToString());
                dependency.Complete();
                Assert.That(scenario.Owner.TryAcquireCompletedBatch(
                    in lease,
                    out _,
                    out failure), Is.True, failure.ToString());
                Assert.That(scenario.Owner.TryConsumeResult(
                    in lease,
                    out var result,
                    out failure), Is.True, failure.ToString());
                Assert.That(result.Execution.Code, Is.EqualTo(BurstExecutionCode.Success));
                Assert.That(result.FrameAcquired, Is.True);
                Assert.That(result.CallbackFailure, Is.EqualTo(BurstContextResult.Success));
                Assert.That(result.Status, Is.EqualTo(NodeStatus.Success));
                Assert.That(ReadUInt32(scenario.MemoryBytes, 0), Is.EqualTo((uint)LiveValue + 1u));
                Assert.That(scenario.Owner.TryReset(in lease, out failure), Is.True, failure.ToString());
            }
        }

        private sealed class Scenario : IDisposable
        {
            private readonly NativeArray<NativeBurstDispatchCaseV2> _cases;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
            private readonly NativeArray<NativeBurstDispatchBindingV2> _bindings;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _valueFields;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRuleV2> _rules;
            private readonly NativeArray<byte> _configurationBytes;
            internal readonly NativeArray<byte> MemoryBytes;
            private readonly NativeArray<ulong> _randomStates;
            private readonly NativeArray<ulong> _randomIncrements;
            private readonly NativeArray<NativeBurstDispatchResolvedBindingV2> _resolvedBindings;
            private readonly NativeArray<byte> _liveValueBytes;
            private readonly NativeArray<NativeBurstDispatchCompletionV2> _completions;
            private readonly NativeArray<byte> _completionPayloadBytes;
            private readonly NativeArray<NativeBurstDispatchCommandV2> _commands;
            private readonly NativeArray<byte> _commandPayloadBytes;
            private readonly NativeArray<NativeBurstDispatchOperationV2> _operations;
            private readonly NativeArray<NativeBurstDispatchTransactionControlV2> _transactionControl;
            private bool _ownerDisposed;

            internal Scenario()
            {
                _cases = Array<NativeBurstDispatchCaseV2>(1);
                _configurationFields = Array<NativeBurstDispatchFieldV2>(2);
                _memoryFields = Array<NativeBurstDispatchFieldV2>(6);
                _bindings = Array<NativeBurstDispatchBindingV2>(1);
                _valueFields = Array<NativeBurstDispatchFieldV2>(1);
                _caseRanges = Array<NativeBurstDispatchCanonicalRangeV2>(2);
                _bindingRanges = Array<NativeBurstDispatchCanonicalRangeV2>(2);
                _rules = Array<NativeBurstDispatchCanonicalRuleV2>(1);

                _cases[0] = new NativeBurstDispatchCaseV2(
                    StableHash.Fnv1A64(TypeId),
                    TypeVersion,
                    0u,
                    0u,
                    2u,
                    8u,
                    0u,
                    6u,
                    48u,
                    NativeBurstDispatchPhaseMaskV2.Enter
                        | NativeBurstDispatchPhaseMaskV2.Tick
                        | NativeBurstDispatchPhaseMaskV2.Abort
                        | NativeBurstDispatchPhaseMaskV2.Exit
                        | NativeBurstDispatchPhaseMaskV2.Observer,
                    BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure,
                    false,
                    0u,
                    1u);
                _configurationFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 1u, NativeBurstDispatchFieldEncodingV2.Boolean);
                _configurationFields[1] = new NativeBurstDispatchFieldV2(
                    1u, 4u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.GeneratedHandle);
                _memoryFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.UInt32);
                _memoryFields[1] = new NativeBurstDispatchFieldV2(
                    1u, 0u, 8u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64,
                    NativeBurstDispatchCanonicalRuleKindV2.AssetId);
                _memoryFields[2] = new NativeBurstDispatchFieldV2(
                    1u, 1u, 16u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64);
                _memoryFields[3] = new NativeBurstDispatchFieldV2(
                    1u, 2u, 24u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.Int64);
                _memoryFields[4] = new NativeBurstDispatchFieldV2(
                    1u, 3u, 32u, 1u, 1u, NativeBurstDispatchFieldEncodingV2.Boolean);
                _memoryFields[5] = new NativeBurstDispatchFieldV2(
                    1u, 4u, 40u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);
                _bindings[0] = new NativeBurstDispatchBindingV2(
                    0u,
                    1u,
                    NativeBurstDispatchBindingKindV2.BlackboardRead,
                    (byte)BlackboardScope.Agent,
                    NativeBurstDispatchBindingPhaseMaskV2.None,
                    NativeBuiltInBlackboardTypeIdsV1.Int32,
                    1u,
                    0u,
                    1u,
                    4u,
                    0UL,
                    0u,
                    0u,
                    0u,
                    0u);
                _valueFields[0] = new NativeBurstDispatchFieldV2(
                    0u, 0u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.Int32);
                _caseRanges[0] = new NativeBurstDispatchCanonicalRangeV2(0u, 0u);
                _caseRanges[1] = new NativeBurstDispatchCanonicalRangeV2(0u, 1u);
                _bindingRanges[0] = new NativeBurstDispatchCanonicalRangeV2(1u, 0u);
                _bindingRanges[1] = new NativeBurstDispatchCanonicalRangeV2(1u, 0u);
                _rules[0] = new NativeBurstDispatchCanonicalRuleV2(
                    NativeBurstDispatchCanonicalRuleKindV2.AssetId, 8u);

                var canonical = new NativeBurstDispatchCanonicalInputV2(
                    _caseRanges.AsReadOnly(),
                    _bindingRanges.AsReadOnly(),
                    _rules.AsReadOnly());
                var shape = new NativeBurstDispatchWorkspaceShapeV2(
                    GeneratedDispatchCanaryCatalog.HandshakeForTests(),
                    _cases.AsReadOnly(),
                    _configurationFields.AsReadOnly(),
                    _memoryFields.AsReadOnly(),
                    _bindings.AsReadOnly(),
                    _valueFields.AsReadOnly(),
                    canonical);
                var capacity = new NativeBurstDispatchWorkspaceCapacityV2(
                    48u,
                    new NativeBurstDispatchBindingCapacityV2(1u, 4u, 0u, 0u, 0u, 7UL));
                Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                    in shape,
                    in capacity,
                    Allocator.Persistent,
                    out var owner,
                    out var failure), Is.True, failure.ToString());
                Owner = owner;

                _configurationBytes = Bytes(8);
                _configurationBytes[0] = 1;
                WriteUInt32(_configurationBytes, 4, 0u);
                MemoryBytes = Bytes(48);
                _randomStates = Array<ulong>(0);
                _randomIncrements = Array<ulong>(0);
                _resolvedBindings = Array<NativeBurstDispatchResolvedBindingV2>(1);
                _resolvedBindings[0] = new NativeBurstDispatchResolvedBindingV2(0u, 23u, 0u);
                _liveValueBytes = Bytes(4);
                WriteUInt32(_liveValueBytes, 0, LiveValue);
                _completions = Array<NativeBurstDispatchCompletionV2>(0);
                _completionPayloadBytes = Bytes(0);
                _commands = Array<NativeBurstDispatchCommandV2>(0);
                _commandPayloadBytes = Bytes(0);
                _operations = Array<NativeBurstDispatchOperationV2>(0);
                _transactionControl = Array<NativeBurstDispatchTransactionControlV2>(1);
                var transaction = new NativeBurstDispatchTransactionControlV2
                {
                    LedgerToken = 0xd15ca7c1UL,
                    TreeInstanceId = new TreeInstanceId(19UL),
                    NextOperationSequence = 7UL
                };
                NativeBurstDispatchTransactionLedgerV2.Initialize(ref transaction);
                _transactionControl[0] = transaction;

                var request = new NativeBurstDispatchRequestV2(
                    0u,
                    7u,
                    StableHash.Fnv1A64(TypeId),
                    TypeVersion,
                    0u,
                    BurstCallbackPhase.Tick,
                    0u,
                    0u,
                    0u,
                    123_456L,
                    new TreeInstanceId(19UL),
                    1u,
                    0u,
                    1u);
                Views = new NativeBurstDispatchWorkspaceRequestViewsV2(
                    request,
                    _configurationBytes,
                    MemoryBytes,
                    _randomStates,
                    _randomIncrements,
                    _resolvedBindings,
                    _liveValueBytes,
                    _completions,
                    _completionPayloadBytes,
                    _commands,
                    _commandPayloadBytes,
                    _operations,
                    _transactionControl);
            }

            internal NativeBurstDispatchWorkspaceOwnerV2 Owner { get; }
            internal NativeBurstDispatchWorkspaceRequestViewsV2 Views { get; }

            public void Dispose()
            {
                if (!_ownerDisposed && Owner != null)
                {
                    Assert.That(Owner.TryDispose(out var failure), Is.True, failure.ToString());
                    _ownerDisposed = true;
                }

                Dispose(_transactionControl);
                Dispose(_operations);
                Dispose(_commandPayloadBytes);
                Dispose(_commands);
                Dispose(_completionPayloadBytes);
                Dispose(_completions);
                Dispose(_liveValueBytes);
                Dispose(_resolvedBindings);
                Dispose(_randomIncrements);
                Dispose(_randomStates);
                Dispose(MemoryBytes);
                Dispose(_configurationBytes);
                Dispose(_rules);
                Dispose(_bindingRanges);
                Dispose(_caseRanges);
                Dispose(_valueFields);
                Dispose(_bindings);
                Dispose(_memoryFields);
                Dispose(_configurationFields);
                Dispose(_cases);
            }

            private static NativeArray<T> Array<T>(int length) where T : struct
                => new NativeArray<T>(length, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            private static NativeArray<byte> Bytes(int length)
                => new NativeArray<byte>(length, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            private static void Dispose<T>(NativeArray<T> value) where T : struct
            {
                if (value.IsCreated)
                {
                    value.Dispose();
                }
            }
        }

        private static uint ReadUInt32(NativeArray<byte> bytes, int offset)
        {
            return bytes[offset]
                | (uint)bytes[offset + 1] << 8
                | (uint)bytes[offset + 2] << 16
                | (uint)bytes[offset + 3] << 24;
        }

        private static void WriteUInt32(NativeArray<byte> bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt32(NativeArray<byte> bytes, int offset, int value)
            => WriteUInt32(bytes, offset, unchecked((uint)value));
    }
}
