using System;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Spikes.P6022NativeDispatchHarness
{
    // P6-022 disposable spike: proves GenericNativeDispatchTranslatorV1 against a real, compiled
    // SpikeCatalogShard/SpikeNativeDispatchCatalog produced by AIBT's own Roslyn generator inside
    // this isolated project -- never a hand-authored workspace shape. SpikeThresholdConditionNode
    // is a dedicated single-node copy of Samples~/BurstNodes' own ThresholdConditionNode: the real
    // sample shard has two nodes (AsyncWriteAction at real dispatch index 0, ThresholdCondition at
    // index 1), and NativeBurstDispatchWorkspaceOwnerV2.TryCreate's own validation requires a
    // workspace's Cases array to be positionally self-consistent (Cases[i].CatalogCaseIndex == i)
    // starting from 0 -- discovered empirically in this spike's first real TryCreate attempt, which
    // failed InvalidEncoding. Isolating ThresholdCondition alone from the real two-node catalog is
    // therefore impossible without also translating AsyncWriteAction's case (async/Completion
    // bindings, explicitly out of this card's decided scope); a single-node shard sidesteps that by
    // construction, keeping the proven scope to non-async single-case shapes as ADR-P6-022 decided.
    // Cross-checked against the real PublicBurstNodeSampleGoldenTests.ThresholdCondition_...'s own
    // independently hardcoded result for the identical semantics (Enter succeeds; Tick below the
    // threshold returns Failure; Tick at/above the threshold returns Success).
    [TestFixture]
    public sealed class GenericNativeDispatchSpikeTests
    {
        private const string ThresholdTypeId = "aibt.spikes.p6022-threshold-condition";
        private const uint RuntimeNodeIndex = 10u;
        private const uint ActivationGeneration = 9u;

        [Test]
        public void ThresholdCondition_GenericallyTranslatedDispatch_ReadsTypedBlackboardValue()
        {
            var artifact = GeneratedShardMetadataMaterializer.MaterializeArtifact(
                SpikeCatalogShard.AibtGeneratedMetadata.ShardId,
                SpikeCatalogShard.AibtGeneratedMetadata.ShardVersion,
                SpikeCatalogShard.AibtGeneratedMetadata.CanonicalDescriptorJson,
                SpikeCatalogShard.AibtGeneratedMetadata.DescriptorHash,
                SpikeCatalogShard.AibtGeneratedMetadata.ManifestRegistryJson,
                SpikeCatalogShard.AibtGeneratedMetadata.NodeRegistryHash);

            var handshake = GeneratedHandshake();
            using (var built = GenericNativeDispatchTranslatorV1.Build(
                artifact, ThresholdTypeId, handshake, Allocator.Temp))
            {
                var capacity = new NativeBurstDispatchWorkspaceCapacityV2(
                    32u, new NativeBurstDispatchBindingCapacityV2(8u, 32u, 8u, 64u, 4u, 5UL));
                var shape = built.Shape;
                Assert.That(NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                    in shape, in capacity, Allocator.Persistent, out var owner, out var createFailure),
                    Is.True, createFailure.ToString());
                try
                {
                    using (var request = new ThresholdRequestBuffers(built, minimum: 7u, current: 6u))
                    {
                        var entered = Execute(owner, request.Views(BurstCallbackPhase.Enter));
                        AssertSuccessful(entered);

                        var below = Execute(owner, request.Views(BurstCallbackPhase.Tick));
                        AssertSuccessful(below);
                        Assert.That(below.Status, Is.EqualTo(NodeStatus.Failure),
                            "current(6) < minimum(7) must read Failure, matching the golden test's own hardcoded result.");

                        request.SetCurrent(8u);
                        var reached = Execute(owner, request.Views(BurstCallbackPhase.Tick));
                        AssertSuccessful(reached);
                        Assert.That(reached.Status, Is.EqualTo(NodeStatus.Success),
                            "current(8) >= minimum(7) must read Success, matching the golden test's own hardcoded result.");
                    }
                }
                finally
                {
                    Assert.That(owner.TryDispose(out var disposeFailure), Is.True, disposeFailure.ToString());
                }
            }
        }

        private static NativeBurstDispatchWorkspaceResultV2 Execute(
            NativeBurstDispatchWorkspaceOwnerV2 owner,
            NativeBurstDispatchWorkspaceRequestViewsV2 views)
        {
            Assert.That(owner.TryBeginRequest(in views, out var lease, out var failure), Is.True, failure.ToString());
            Assert.That(owner.TryAcquireImmediateBatch(in lease, out var batch, out failure), Is.True, failure.ToString());

            var execution = SpikeNativeDispatchCatalog.ExecuteImmediate(ref batch);

            Assert.That(execution.Code, Is.EqualTo(BurstExecutionCode.Success));
            Assert.That(owner.TryConsumeResult(in lease, out var result, out failure), Is.True, failure.ToString());
            Assert.That(owner.TryReset(in lease, out failure), Is.True, failure.ToString());
            return result;
        }

        private static void AssertSuccessful(NativeBurstDispatchWorkspaceResultV2 result)
        {
            Assert.That(result.Execution.Code, Is.EqualTo(BurstExecutionCode.Success));
            Assert.That(result.CallbackFailure, Is.EqualTo(BurstContextResult.Success));
            Assert.That(result.FrameAcquired, Is.True);
        }

        // Mirrors PublicBurstNodeSampleGoldenTests.GeneratedHandshake() exactly: the handshake is
        // read from the real generated catalog's own reflected fingerprint properties, never
        // recomputed by the translator (ADR-P6-022 decision 1).
        private static BurstCatalogHandshake GeneratedHandshake()
        {
            var catalogType = typeof(SpikeNativeDispatchCatalog);
            return new BurstCatalogHandshake(
                2u,
                SpikeNativeDispatchCatalog.Fingerprint,
                GeneratedHash(catalogType, "NodeRegistryFingerprint"),
                1u,
                1u,
                GeneratedHash(catalogType, "ConfigurationLayoutFingerprint"),
                GeneratedHash(catalogType, "MemoryLayoutFingerprint"),
                GeneratedHash(catalogType, "AccessLayoutFingerprint"));
        }

        private static BurstHash256 GeneratedHash(Type catalogType, string propertyName)
        {
            var property = catalogType.GetProperty(propertyName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, "Generated catalog fingerprint is missing: " + propertyName);
            return (BurstHash256)property.GetValue(null);
        }

        private static NativeArray<T> Array<T>(int length, Allocator allocator) where T : struct
            => new NativeArray<T>(length, allocator, NativeArrayOptions.ClearMemory);

        private static void WriteUInt32(NativeArray<byte> bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        // Per ADR-P6-022 decision 4, the request (per-call buffers) is a harness-authored concern,
        // never derived from compiled metadata -- this mirrors the golden test's own
        // ThresholdRequestBuffers, indexed against the translator's own case/binding ordinals instead
        // of hand-copied numbers.
        private sealed class ThresholdRequestBuffers : IDisposable
        {
            private readonly GenericNativeDispatchTranslatorV1.BuiltShape _built;
            private NativeBurstDispatchRequestV2 _request;
            private readonly NativeArray<byte> _configurationBytes;
            private readonly NativeArray<byte> _memoryBytes;
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

            internal ThresholdRequestBuffers(GenericNativeDispatchTranslatorV1.BuiltShape built, uint minimum, uint current)
            {
                _built = built;
                var configurationCase = built.Shape.Cases[0];
                _configurationBytes = Array<byte>((int)configurationCase.ConfigurationSize, Allocator.Persistent);
                _memoryBytes = Array<byte>((int)configurationCase.MemorySize, Allocator.Persistent);
                _randomStates = Array<ulong>(0, Allocator.Persistent);
                _randomIncrements = Array<ulong>(0, Allocator.Persistent);
                _resolvedBindings = Array<NativeBurstDispatchResolvedBindingV2>(1, Allocator.Persistent);
                _liveValueBytes = Array<byte>(4, Allocator.Persistent);
                _completions = Array<NativeBurstDispatchCompletionV2>(0, Allocator.Persistent);
                _completionPayloadBytes = Array<byte>(0, Allocator.Persistent);
                _commands = Array<NativeBurstDispatchCommandV2>(8, Allocator.Persistent);
                _commandPayloadBytes = Array<byte>(64, Allocator.Persistent);
                _operations = Array<NativeBurstDispatchOperationV2>(4, Allocator.Persistent);
                _transactionControl = Array<NativeBurstDispatchTransactionControlV2>(1, Allocator.Persistent);

                WriteUInt32(_configurationBytes, (int)(built.CurrentBindingConfigurationFieldOrdinal * 4u), 0u);
                WriteUInt32(_configurationBytes, 4, minimum);
                SetCurrent(current);
                _resolvedBindings[0] = new NativeBurstDispatchResolvedBindingV2(0u, 800u, 0u);
                var transaction = new NativeBurstDispatchTransactionControlV2
                {
                    LedgerToken = 0xa1b72031UL,
                    TreeInstanceId = new TreeInstanceId(19UL),
                    NextOperationSequence = 5UL
                };
                NativeBurstDispatchTransactionLedgerV2.Initialize(ref transaction);
                _transactionControl[0] = transaction;
            }

            internal void SetCurrent(uint value) => WriteUInt32(_liveValueBytes, 0, value);

            internal NativeBurstDispatchWorkspaceRequestViewsV2 Views(BurstCallbackPhase phase)
            {
                var caseValue = _built.Shape.Cases[0];
                _request = new NativeBurstDispatchRequestV2(
                    0u,
                    RuntimeNodeIndex,
                    caseValue.TypeNumericId,
                    caseValue.TypeVersion,
                    _built.CatalogCaseIndex,
                    phase,
                    0u, 0u, 0u,
                    123_456L,
                    new TreeInstanceId(19UL),
                    ActivationGeneration,
                    0u, 1u);
                return new NativeBurstDispatchWorkspaceRequestViewsV2(
                    _request,
                    _configurationBytes,
                    _memoryBytes,
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

            public void Dispose()
            {
                _transactionControl.Dispose();
                _operations.Dispose();
                _commandPayloadBytes.Dispose();
                _commands.Dispose();
                _completionPayloadBytes.Dispose();
                _completions.Dispose();
                _liveValueBytes.Dispose();
                _resolvedBindings.Dispose();
                _randomIncrements.Dispose();
                _randomStates.Dispose();
                _memoryBytes.Dispose();
                _configurationBytes.Dispose();
            }
        }
    }
}
