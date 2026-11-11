using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using AIBT;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Editor.Registry;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;

namespace AIBT.Mcp.NodeDevelopment
{
    /// <summary>
    /// Drives a staged, freshly compiled node through real generated dispatch for test-node
    /// (P7-009, applying ADR-P6-022 to production). Builds a generic, zero-initialized request --
    /// no per-field-name knowledge, since this must work for any node shape within the translator's
    /// proven scope, not only ThresholdCondition-like ones -- and reports the real NodeStatus/
    /// BurstContextResult observed. Never compares against an expected value: proving dispatch
    /// actually runs is the whole point, not matching a golden result.
    /// </summary>
    internal static class GenericNodeDispatchRunner
    {
        internal readonly struct RunResult
        {
            private RunResult(bool dispatchProven, string reason, bool enteredSuccessfully, string tickStatus, string callbackFailure,
                IReadOnlyDictionary<string, string> effects = null)
            {
                DispatchProven = dispatchProven;
                Reason = reason;
                EnteredSuccessfully = enteredSuccessfully;
                TickStatus = tickStatus;
                CallbackFailure = callbackFailure;
                Effects = effects ?? new Dictionary<string, string>();
            }

            internal bool DispatchProven { get; }
            internal string Reason { get; }
            internal bool EnteredSuccessfully { get; }
            internal string TickStatus { get; }
            internal string CallbackFailure { get; }
            internal IReadOnlyDictionary<string, string> Effects { get; }

            internal static RunResult OutOfScope(string reason) => new RunResult(false, reason, false, null, null);

            internal static RunResult Proven(bool enteredSuccessfully, string tickStatus, string callbackFailure, IReadOnlyDictionary<string, string> effects = null)
                => new RunResult(true, null, enteredSuccessfully, tickStatus, callbackFailure, effects);
        }

        internal static RunResult Run(GeneratedShardMetadataArtifact artifact, Type catalogSetType, JObject scenario = null)
        {
            // Staging always compiles exactly one node into its own isolated shard (StagingSlot.
            // WriteNode clears any prior pending generation before writing a fresh one), so the
            // staged node is always the sole, index-0 entry -- the translator's own 0..targetIndex
            // prefix support exists for a real project catalog's own multi-node case, which
            // test-node itself can never reach through today's staging architecture (disclosed in
            // Planning~/Evidence/P7-009/).
            var targetTypeId = artifact.Nodes[0].Manifest.TypeId;

            if (!GeneratedNodeReflectionHarness.TryReflectHandshake(catalogSetType, out var handshake, out var handshakeFailure))
                return RunResult.OutOfScope(handshakeFailure);
            if (!GeneratedNodeReflectionHarness.TryGetExecuteImmediate(catalogSetType, out var executeImmediate, out var methodFailure))
                return RunResult.OutOfScope(methodFailure);

            GenericNativeDispatchTranslatorV1.BuiltShape built;
            try
            {
                // The TCP bridge invokes this on a managed worker thread. Storage is explicitly
                // disposed below; Temp allocations are only valid on Unity's supported threads.
                built = GenericNativeDispatchTranslatorV1.Build(artifact, targetTypeId, handshake, Allocator.Persistent);
            }
            catch (NotSupportedException ex)
            {
                return RunResult.OutOfScope(ex.Message);
            }

            try
            {
                var capacity = new NativeBurstDispatchWorkspaceCapacityV2(
                    64u, new NativeBurstDispatchBindingCapacityV2(8u, 64u, 8u, 64u, 4u, 1UL));
                var shape = built.Shape;
                if (!NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                        in shape, in capacity, Allocator.Persistent, out var owner, out var createFailure))
                {
                    return RunResult.OutOfScope("Workspace creation failed: " + createFailure);
                }

                try
                {
                    using (var request = new ZeroInitializedRequestBuffers(built))
                    {
                        if (scenario != null) request.ApplyScenario(artifact.Nodes[0], scenario);
                        var phases = built.TargetCase.Phases;
                        var enteredSuccessfully = true;
                        if ((phases & NativeBurstDispatchPhaseMaskV2.Enter) != 0)
                        {
                            var entered = Execute(owner, request.Views(built, BurstCallbackPhase.Enter), executeImmediate);
                            enteredSuccessfully = entered.Execution.Code == BurstExecutionCode.Success
                                && entered.CallbackFailure == BurstContextResult.Success;
                        }

                        if ((phases & NativeBurstDispatchPhaseMaskV2.Tick) == 0)
                        {
                            return RunResult.Proven(enteredSuccessfully, null, null);
                        }

                        var ticked = Execute(owner, request.Views(built, BurstCallbackPhase.Tick), executeImmediate);
                        var tickStatus = ticked.Execution.Code == BurstExecutionCode.Success ? ticked.Status.ToString() : null;
                        return RunResult.Proven(enteredSuccessfully, tickStatus, ticked.CallbackFailure.ToString(),
                            request.ReadEffects(artifact.Nodes[0], ticked.Transaction.CommandCount));
                    }
                }
                finally
                {
                    owner.TryDispose(out _);
                }
            }
            finally
            {
                built.Dispose();
            }
        }

        private static NativeBurstDispatchWorkspaceResultV2 Execute(
            NativeBurstDispatchWorkspaceOwnerV2 owner,
            NativeBurstDispatchWorkspaceRequestViewsV2 views,
            MethodInfo executeImmediate)
        {
            if (!owner.TryBeginRequest(in views, out var lease, out var beginFailure))
                throw new InvalidOperationException("TryBeginRequest failed: " + beginFailure);
            if (!owner.TryAcquireImmediateBatch(in lease, out var batch, out var acquireFailure))
                throw new InvalidOperationException("TryAcquireImmediateBatch failed: " + acquireFailure);

            var arguments = new object[] { batch };
            executeImmediate.Invoke(null, arguments);

            if (!owner.TryConsumeResult(in lease, out var result, out var consumeFailure))
                throw new InvalidOperationException("TryConsumeResult failed: " + consumeFailure);
            if (!owner.TryReset(in lease, out var resetFailure))
                throw new InvalidOperationException("TryReset failed: " + resetFailure);
            return result;
        }

        // No per-field-name knowledge: configuration/memory sized from the target case's own
        // declared byte sizes and zero-filled; one resolved binding per binding in the target case,
        // each pointing at its own zero-filled slot in a shared, cumulatively-sized value-byte
        // buffer -- generic across any node shape within the translator's proven scope.
        private sealed class ZeroInitializedRequestBuffers : IDisposable
        {
            private readonly NativeArray<byte> _configurationBytes;
            private readonly NativeArray<byte> _memoryBytes;
            private readonly NativeArray<ulong> _randomStates;
            private readonly NativeArray<ulong> _randomIncrements;
            private readonly NativeArray<NativeBurstDispatchResolvedBindingV2> _resolvedBindings;
            private readonly NativeArray<byte> _bindingValueBytes;
            private readonly NativeArray<NativeBurstDispatchCompletionV2> _completions;
            private readonly NativeArray<byte> _completionPayloadBytes;
            private readonly NativeArray<NativeBurstDispatchCommandV2> _commands;
            private readonly NativeArray<byte> _commandPayloadBytes;
            private readonly NativeArray<NativeBurstDispatchOperationV2> _operations;
            private readonly NativeArray<NativeBurstDispatchTransactionControlV2> _transactionControl;

            internal ZeroInitializedRequestBuffers(GenericNativeDispatchTranslatorV1.BuiltShape built)
            {
                var targetCase = built.TargetCase;
                _configurationBytes = Zeroed((int)targetCase.ConfigurationSize);
                _memoryBytes = Zeroed((int)targetCase.MemorySize);
                _randomStates = Zeroed(targetCase.HasRandomStream != 0 ? 1 : 0, forUlong: true);
                _randomIncrements = Zeroed(targetCase.HasRandomStream != 0 ? 1 : 0, forUlong: true);

                var bindingCount = (int)targetCase.BindingCount;
                _resolvedBindings = new NativeArray<NativeBurstDispatchResolvedBindingV2>(bindingCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                var totalValueBytes = 0u;
                for (var index = 0; index < bindingCount; index++)
                {
                    // Each binding's own value size (not a uniform guess) -- 64-bit value types
                    // would otherwise overlap the next binding's own live-value slot.
                    var valueSize = built.Shape.ValueFields[(int)(targetCase.FirstBinding + (uint)index)].ElementSize;
                    var binding = built.Shape.Bindings[(int)(targetCase.FirstBinding + (uint)index)];
                    var live = binding.Kind <= NativeBurstDispatchBindingKindV2.SnapshotRead;
                    _resolvedBindings[index] = new NativeBurstDispatchResolvedBindingV2((uint)index, (uint)index,
                        live ? totalValueBytes : NativeBurstDispatchBindingV2.NoOffset);
                    if (live) totalValueBytes += valueSize;
                    var handle = built.Shape.ConfigurationFields[(int)(targetCase.FirstConfigurationField + binding.ConfigurationFieldOrdinal)];
                    var ordinal = BitConverter.GetBytes((uint)index);
                    if (!BitConverter.IsLittleEndian) Array.Reverse(ordinal);
                    Copy(ordinal, _configurationBytes, (int)handle.ByteOffset, ordinal.Length);
                }
                _bindingValueBytes = Zeroed((int)Math.Max(totalValueBytes, 1u));

                _completions = new NativeArray<NativeBurstDispatchCompletionV2>(0, Allocator.Persistent);
                _completionPayloadBytes = new NativeArray<byte>(0, Allocator.Persistent);
                _commands = new NativeArray<NativeBurstDispatchCommandV2>(8, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                _commandPayloadBytes = new NativeArray<byte>(64, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                _operations = new NativeArray<NativeBurstDispatchOperationV2>(4, Allocator.Persistent, NativeArrayOptions.ClearMemory);

                _transactionControl = new NativeArray<NativeBurstDispatchTransactionControlV2>(1, Allocator.Persistent);
                var transaction = new NativeBurstDispatchTransactionControlV2
                {
                    LedgerToken = 0x7e57c0deUL,
                    TreeInstanceId = new TreeInstanceId(1UL),
                    NextOperationSequence = 1UL,
                };
                NativeBurstDispatchTransactionLedgerV2.Initialize(ref transaction);
                _transactionControl[0] = transaction;
            }

            internal NativeBurstDispatchWorkspaceRequestViewsV2 Views(GenericNativeDispatchTranslatorV1.BuiltShape built, BurstCallbackPhase phase)
            {
                var request = new NativeBurstDispatchRequestV2(
                    0u,
                    1u,
                    built.TargetCase.TypeNumericId,
                    built.TargetCase.TypeVersion,
                    built.TargetCaseIndex,
                    phase,
                    0u, 0u, 0u,
                    0L,
                    new TreeInstanceId(1UL),
                    1u,
                    0u, built.TargetCase.BindingCount);
                return new NativeBurstDispatchWorkspaceRequestViewsV2(
                    request,
                    _configurationBytes,
                    _memoryBytes,
                    _randomStates,
                    _randomIncrements,
                    _resolvedBindings,
                    _bindingValueBytes,
                    _completions,
                    _completionPayloadBytes,
                    _commands,
                    _commandPayloadBytes,
                    _operations,
                    _transactionControl);
            }

            internal void ApplyScenario(GeneratedNodeDescriptor node, JObject scenario)
            {
                foreach (var property in ((JObject)scenario["configuration"] ?? new JObject()).Properties())
                {
                    var field = node.Configuration.SingleOrDefault(candidate => candidate.FieldId == property.Name);
                    if (field == null || field.Encoding == GeneratedFieldEncoding.GeneratedHandle)
                        throw new ArgumentException("Unknown or generated configuration field: " + property.Name);
                    Copy(NativeBehaviorValues.Encode(field.ValueTypeId, property.Value), _configurationBytes, (int)field.Offset, (int)field.Size);
                }
                foreach (var property in ((JObject)scenario["inputs"] ?? new JObject()).Properties())
                {
                    var binding = node.Bindings.SingleOrDefault(candidate => candidate.BindingId == property.Name);
                    if (binding == null || !(binding.IsBlackboard || binding.Kind == GeneratedBindingKind.SnapshotRead))
                        throw new ArgumentException("Unknown input binding: " + property.Name);
                    var resolved = _resolvedBindings[(int)binding.Ordinal];
                    var bytes = NativeBehaviorValues.Encode(binding.Types[0].CanonicalTypeId, property.Value);
                    Copy(bytes, _bindingValueBytes, (int)resolved.LiveValueOffset, bytes.Length);
                }
            }

            internal IReadOnlyDictionary<string, string> ReadEffects(GeneratedNodeDescriptor node, uint count)
            {
                var effects = new Dictionary<string, string>();
                for (var index = 0; index < count; index++)
                {
                    var command = _commands[index];
                    var binding = node.Bindings[(int)command.TargetOrdinal];
                    var bytes = new byte[(int)command.PayloadSize];
                    for (var offset = 0; offset < bytes.Length; offset++) bytes[offset] = _commandPayloadBytes[(int)command.PayloadOffset + offset];
                    if (effects.ContainsKey(binding.BindingId)) throw new NotSupportedException("Behavior checks currently allow one effect per binding per tick.");
                    effects.Add(binding.BindingId, Convert.ToBase64String(bytes));
                }
                return effects;
            }

            private static void Copy(byte[] source, NativeArray<byte> destination, int offset, int expectedSize)
            {
                if (source.Length != expectedSize || offset < 0 || offset + source.Length > destination.Length)
                    throw new ArgumentException("Behavior value does not match the declared layout.");
                for (var index = 0; index < source.Length; index++) destination[offset + index] = source[index];
            }

            private static NativeArray<byte> Zeroed(int length) => new NativeArray<byte>(length, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            private static NativeArray<ulong> Zeroed(int length, bool forUlong) => new NativeArray<ulong>(length, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            public void Dispose()
            {
                _transactionControl.Dispose();
                _operations.Dispose();
                _commandPayloadBytes.Dispose();
                _commands.Dispose();
                _completionPayloadBytes.Dispose();
                _completions.Dispose();
                _bindingValueBytes.Dispose();
                _resolvedBindings.Dispose();
                _randomIncrements.Dispose();
                _randomStates.Dispose();
                _memoryBytes.Dispose();
                _configurationBytes.Dispose();
            }
        }
    }
}
