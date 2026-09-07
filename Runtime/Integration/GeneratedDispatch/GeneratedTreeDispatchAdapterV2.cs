using System;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace AIBT
{
    /// <summary>
    /// Instance-owned, allocation-free-after-bootstrap bridge used by the standalone production
    /// host. Population grouping remains the responsibility of the P7-033 coordinator.
    /// </summary>
    internal sealed class GeneratedTreeDispatchAdapterV2 : IDisposable
    {
        private readonly GeneratedTreeRuntimeDefinitionV2 _definition;
        private readonly GeneratedBurstCatalogV2 _catalog;
        private readonly TreeInstanceId _treeInstanceId;
        private readonly DispatchSlot[] _slots;
        private readonly uint _agentBaseOffset;
        private NativeArray<byte> _treeValues;
        private NativeArray<ulong> _treeVersions;
        private bool _disposed;

        private GeneratedTreeDispatchAdapterV2(
            GeneratedTreeRuntimeDefinitionV2 definition,
            GeneratedBurstCatalogV2 catalog,
            TreeInstanceId treeInstanceId,
            DispatchSlot[] slots,
            NativeArray<byte> treeValues,
            NativeArray<ulong> treeVersions,
            uint agentBaseOffset)
        {
            _definition = definition;
            _catalog = catalog;
            _treeInstanceId = treeInstanceId;
            _slots = slots;
            _treeValues = treeValues;
            _treeVersions = treeVersions;
            _agentBaseOffset = agentBaseOffset;
        }

        internal static bool TryCreate(
            GeneratedTreeRuntimeDefinitionV2 definition,
            GeneratedBurstCatalogV2 catalog,
            TreeInstanceId treeInstanceId,
            out GeneratedTreeDispatchAdapterV2 adapter,
            out BurstContextResult failure)
        {
            adapter = null;
            if (definition == null || catalog == null || !treeInstanceId.IsValid
                || !definition.Matches(catalog) || !catalog.TryRetain())
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

            var binding = definition.Binding;
            var values = default(NativeArray<byte>);
            var versions = default(NativeArray<ulong>);
            DispatchSlot[] slots = null;
            try
            {
                var valueBytes = TreeValueByteCount(binding, out var agentBaseOffset);
                values = new NativeArray<byte>(checked((int)valueBytes), Allocator.Persistent, NativeArrayOptions.ClearMemory);
                versions = new NativeArray<ulong>(binding.Slots.Count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                InitializeTreeDefaults(binding, values, agentBaseOffset);
                slots = new DispatchSlot[binding.SemanticProgram.Nodes.Count];
                for (var nodeIndex = 0; nodeIndex < binding.SemanticProgram.Nodes.Count; nodeIndex++)
                {
                    var node = binding.SemanticProgram.Nodes[nodeIndex];
                    if (NativeHotReloadInstance.ClassifyKind(node.NodeTypeId)
                        != NativeLifecycleNodeKindV1.GeneratedLeaf) continue;
                    if (!catalog.TryFindCase(node.NodeTypeId, node.NodeTypeVersion, out var caseIndex))
                    {
                        DisposeSlots(slots);
                        Dispose(ref versions);
                        Dispose(ref values);
                        catalog.Release();
                        failure = BurstContextResult.TypeMismatch;
                        return false;
                    }
                    if (!DispatchSlot.TryCreate(
                            definition, catalog, (uint)nodeIndex, caseIndex, treeInstanceId,
                            out slots[nodeIndex], out failure))
                    {
                        DisposeSlots(slots);
                        Dispose(ref versions);
                        Dispose(ref values);
                        catalog.Release();
                        return false;
                    }
                }
                adapter = new GeneratedTreeDispatchAdapterV2(
                    definition, catalog, treeInstanceId, slots, values, versions, agentBaseOffset);
                failure = BurstContextResult.Success;
                return true;
            }
            catch (Exception)
            {
                DisposeSlots(slots);
                Dispose(ref versions);
                Dispose(ref values);
                catalog.Release();
                failure = BurstContextResult.CapacityExceeded;
                return false;
            }
        }

        internal BurstContextResult Dispatch(
            in ProductionTreeHost.DispatchRequest request,
            out NodeStatus status)
            => Dispatch(in request, false, out status);

        internal BurstContextResult DispatchScheduledForTests(
            in ProductionTreeHost.DispatchRequest request,
            out NodeStatus status)
            => Dispatch(in request, true, out status);

        internal uint LastPublishedCommandCount(uint nodeIndex)
            => nodeIndex < _slots.Length && _slots[nodeIndex] != null
                ? _slots[nodeIndex].LastPublishedCommandCount : 0u;

        internal bool TryGetLastPublishedCommand(
            uint nodeIndex,
            uint commandIndex,
            out NativeBurstDispatchCommandV2 command)
        {
            command = default;
            return nodeIndex < _slots.Length && _slots[nodeIndex] != null
                && _slots[nodeIndex].TryGetLastPublishedCommand(commandIndex, out command);
        }

        internal bool TryReadTreeValueByte(uint offset, out byte value)
        {
            value = 0;
            if (_disposed || offset >= _treeValues.Length) return false;
            value = _treeValues[(int)offset];
            return true;
        }

        /// <summary>
        /// Resolves a Tree-scope, built-in-typed slot for repeated external writes from ordinary C#
        /// code (ADR-P7-039), addressed by <paramref name="stableKeyId"/> instead of a compiled
        /// node's own access record. Succeeds only when no node in this tree ever declares Write
        /// access to the slot -- a slot some node writes, an unknown key, a type mismatch, or a
        /// registered (non-built-in) value type are all refused, never silently accepted. Registered
        /// types are refused because their canonical-encoding check needs the native program's own
        /// registered-type/field tables as <c>NativeArray</c>s, which this managed-list-backed
        /// binding does not carry.
        /// </summary>
        internal bool TryResolveExternalTreeWrite(
            ulong stableKeyId,
            NativeBlackboardTypeIdV2 expectedType,
            out uint slotIndex,
            out NativeBlackboardSlotBindingV2 slot,
            out BurstContextResult failure)
        {
            slotIndex = 0;
            slot = default;
            if (_disposed)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }
            var slots = _definition.Binding.Slots;
            for (var index = 0; index < slots.Count; index++)
            {
                var candidate = slots[index];
                if (candidate.Scope != BlackboardScope.Tree || candidate.StableKeyId != stableKeyId) continue;
                if (candidate.RegisteredTypeIndex != CompiledIndex.Invalid)
                {
                    failure = BurstContextResult.TypeMismatch;
                    return false;
                }
                if (expectedType.TypeId != candidate.TypeId || expectedType.Version != candidate.TypeVersion
                    || expectedType.Size != candidate.Size || expectedType.Alignment != candidate.Alignment
                    || expectedType.EnumContractId != candidate.EnumContractId)
                {
                    failure = BurstContextResult.TypeMismatch;
                    return false;
                }
                if ((candidate.AccessFlags & CompiledBlackboardAccessFlags.Write) != CompiledBlackboardAccessFlags.None)
                {
                    failure = BurstContextResult.PhaseViolation;
                    return false;
                }
                slotIndex = (uint)index;
                slot = candidate;
                failure = BurstContextResult.Success;
                return true;
            }
            failure = BurstContextResult.InvalidHandle;
            return false;
        }

        /// <summary>
        /// Writes one external value into the Tree-scope slot <paramref name="slot"/> already
        /// resolved by <see cref="TryResolveExternalTreeWrite"/>. Mirrors
        /// <c>NativeTreeBlackboardV1.TryWrite</c>'s own canonical-encoding/negative-zero-
        /// normalization/version-bump body exactly, adapted to this adapter's own
        /// <see cref="_treeValues"/>/<see cref="_treeVersions"/> storage.
        /// </summary>
        internal bool TryWriteExternalTreeValue<T>(
            uint slotIndex,
            NativeBlackboardSlotBindingV2 slot,
            NativeArray<T> candidate,
            out bool changed,
            out BurstContextResult failure)
            where T : unmanaged
        {
            changed = false;
            if (_disposed || !candidate.IsCreated || candidate.Length != 1
                || UnsafeUtility.SizeOf<T>() != slot.Size || UnsafeUtility.AlignOf<T>() != slot.Alignment
                || slotIndex >= (uint)_treeVersions.Length
                || (ulong)slot.Offset + slot.Size > (uint)_treeValues.Length)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

            var bytes = candidate.Reinterpret<byte>(UnsafeUtility.SizeOf<T>());
            if (!NativeBlackboardCanonicalV1.IsCanonicalBuiltInOnly(slot, bytes.AsReadOnly()))
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }
            if (NativeBlackboardCanonicalV1.EqualsCanonicalBuiltInOnly(slot, _treeValues, bytes.AsReadOnly()))
            {
                failure = BurstContextResult.Success;
                return true;
            }
            if (_treeVersions[(int)slotIndex] == ulong.MaxValue)
            {
                failure = BurstContextResult.Overflow;
                return false;
            }

            NativeBlackboardCanonicalV1.CopyCanonicalBuiltInOnly(slot, bytes.AsReadOnly(), _treeValues);
            _treeVersions[(int)slotIndex]++;
            changed = true;
            failure = BurstContextResult.Success;
            return true;
        }

        /// <summary>The exact catalog identity this instance was bound to -- a group wave's own grouping key (only instances sharing one catalog can share one batch call).</summary>
        internal GeneratedBurstCatalogV2 Catalog => _catalog;

        /// <summary>
        /// Prepares one node's own request/config/memory/binding data for a
        /// <see cref="ProductionTreeScheduler"/> group wave, without touching this slot's own
        /// single-request workspace. See <see cref="DispatchSlot.TryPrepareForGroup"/>.
        /// </summary>
        internal bool TryPrepareGroupParticipant(
            uint nodeIndex,
            in ProductionTreeHost.DispatchRequest hostRequest,
            out GeneratedDispatchGroupParticipantV2 participant,
            out BurstContextResult failure)
        {
            participant = default;
            if (_disposed || nodeIndex >= _slots.Length)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }
            var slot = _slots[nodeIndex];
            if (slot == null)
            {
                failure = BurstContextResult.TypeMismatch;
                return false;
            }
            return slot.TryPrepareForGroup(
                _definition.Binding, _catalog, _treeValues, _agentBaseOffset,
                in hostRequest, _treeInstanceId, out participant, out failure);
        }

        /// <summary>Commits one node's own portion of a completed group wave. See <see cref="DispatchSlot.TryCommitFromGroup"/>.</summary>
        internal bool TryCommitGroupParticipant(
            uint nodeIndex,
            NativeArray<byte>.ReadOnly committedMemory,
            NativeArray<byte>.ReadOnly committedBindingValues,
            out BurstContextResult failure)
        {
            if (_disposed || nodeIndex >= _slots.Length)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }
            var slot = _slots[nodeIndex];
            if (slot == null)
            {
                failure = BurstContextResult.TypeMismatch;
                return false;
            }
            return slot.TryCommitFromGroup(
                _definition.Binding, _treeValues, _treeVersions, _agentBaseOffset,
                committedMemory, committedBindingValues, out failure);
        }

        /// <summary>Validates a group result before any participant commits its own bytes.</summary>
        internal bool TryValidateGroupParticipantCommit(
            uint nodeIndex,
            NativeArray<byte>.ReadOnly committedMemory,
            NativeArray<byte>.ReadOnly committedBindingValues,
            out BurstContextResult failure)
        {
            if (_disposed || nodeIndex >= _slots.Length)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }
            var slot = _slots[nodeIndex];
            if (slot == null)
            {
                failure = BurstContextResult.TypeMismatch;
                return false;
            }
            return slot.TryValidateGroupCommit(
                _definition.Binding, _treeValues, _treeVersions, _agentBaseOffset,
                committedMemory, committedBindingValues, out failure);
        }

        private BurstContextResult Dispatch(
            in ProductionTreeHost.DispatchRequest request,
            bool scheduled,
            out NodeStatus status)
        {
            status = NodeStatus.Running;
            if (_disposed || request.NodeIndex >= _slots.Length)
                return BurstContextResult.InvalidHandle;
            var slot = _slots[request.NodeIndex];
            if (slot == null) return BurstContextResult.TypeMismatch;
            return slot.Execute(
                _definition.Binding, _catalog, _treeValues, _treeVersions, _agentBaseOffset,
                in request, scheduled, out status);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeSlots(_slots);
            Dispose(ref _treeVersions);
            Dispose(ref _treeValues);
            _catalog.Release();
        }

        // Tree-scope and Agent-scope slots each have their own, independently zero-based Offset
        // space (ReferenceCompiler.BuildBlackboardSlots: one running offset counter per
        // BlackboardScope). A single tree instance is also the implicit sole "agent" for this
        // standalone host (no cross-instance reduction happens outside P7-033's own population
        // scheduling), so Agent-scope storage is safe to host in the same per-instance buffer as
        // Tree-scope storage -- but only past agentBaseOffset, so the two scopes' own independently
        // numbered offsets can never collide. Shared scope is not included: it is genuinely
        // cross-instance and stays out of this standalone adapter's scope.
        private static uint TreeValueByteCount(NativeProgramBlackboardBindingV2 binding, out uint agentBaseOffset)
        {
            var length = 0u;
            for (var index = 0; index < binding.Slots.Count; index++)
            {
                var slot = binding.Slots[index];
                if (slot.Scope != BlackboardScope.Tree) continue;
                var end = checked(slot.Offset + slot.Size);
                if (end > length) length = end;
            }
            agentBaseOffset = length;
            for (var index = 0; index < binding.Slots.Count; index++)
            {
                var slot = binding.Slots[index];
                if (slot.Scope != BlackboardScope.Agent) continue;
                var end = checked(agentBaseOffset + slot.Offset + slot.Size);
                if (end > length) length = end;
            }
            return length;
        }

        private static void InitializeTreeDefaults(
            NativeProgramBlackboardBindingV2 binding,
            NativeArray<byte> destination,
            uint agentBaseOffset)
        {
            var defaults = binding.SemanticProgram.DefaultValueBlob;
            for (var index = 0; index < binding.Slots.Count; index++)
            {
                var slot = binding.Slots[index];
                if (slot.Scope != BlackboardScope.Tree && slot.Scope != BlackboardScope.Agent) continue;
                var baseOffset = slot.Scope == BlackboardScope.Agent ? agentBaseOffset : 0u;
                if (slot.DefaultSize != slot.Size
                    || (ulong)slot.DefaultOffset + slot.DefaultSize > (uint)defaults.Count
                    || (ulong)baseOffset + slot.Offset + slot.Size > (uint)destination.Length)
                    throw new InvalidOperationException("Compiled Tree blackboard defaults are invalid.");
                for (uint offset = 0; offset < slot.Size; offset++)
                    destination[(int)(baseOffset + slot.Offset + offset)] = defaults[(int)(slot.DefaultOffset + offset)];
            }
        }

        private static void DisposeSlots(DispatchSlot[] slots)
        {
            if (slots == null) return;
            for (var index = slots.Length - 1; index >= 0; index--) slots[index]?.Dispose();
        }

        private static void Dispose<T>(ref NativeArray<T> array) where T : struct
        {
            if (!array.IsCreated) return;
            array.Dispose();
            array = default;
        }

        private sealed class DispatchSlot : IDisposable
        {
            private readonly uint _nodeIndex;
            private readonly uint _caseIndex;
            private readonly BindingResolution[] _resolutions;
            private NativeBurstDispatchWorkspaceOwnerV2 _workspace;
            private NativeArray<byte> _configuration;
            private NativeArray<byte> _memory;
            private NativeArray<ulong> _randomStates;
            private NativeArray<ulong> _randomIncrements;
            private NativeArray<NativeBurstDispatchResolvedBindingV2> _resolvedBindings;
            private NativeArray<byte> _bindingValues;
            private NativeArray<NativeBurstDispatchCompletionV2> _completions;
            private NativeArray<byte> _completionPayload;
            private NativeArray<NativeBurstDispatchCommandV2> _commands;
            private NativeArray<byte> _commandPayload;
            private NativeArray<NativeBurstDispatchOperationV2> _operations;
            private NativeArray<NativeBurstDispatchTransactionControlV2> _transaction;
            private NativeArray<NativeBurstDispatchCommandV2> _publishedCommands;
            private NativeArray<byte> _publishedPayload;
            private uint _publishedCommandCount;
            private uint _activationGeneration;
            private bool _disposed;

            private DispatchSlot(uint nodeIndex, uint caseIndex, BindingResolution[] resolutions)
            {
                _nodeIndex = nodeIndex;
                _caseIndex = caseIndex;
                _resolutions = resolutions;
            }

            internal uint LastPublishedCommandCount => _publishedCommandCount;

            internal static bool TryCreate(
                GeneratedTreeRuntimeDefinitionV2 definition,
                GeneratedBurstCatalogV2 catalog,
                uint nodeIndex,
                uint caseIndex,
                TreeInstanceId treeInstanceId,
                out DispatchSlot slot,
                out BurstContextResult failure)
            {
                slot = null;
                var binding = definition.Binding;
                var node = binding.SemanticProgram.Nodes[(int)nodeIndex];
                var dispatchCase = catalog.Cases[(int)caseIndex];
                var resolutions = new BindingResolution[dispatchCase.BindingCount];
                var liveBytes = 0u;
                for (uint local = 0; local < dispatchCase.BindingCount; local++)
                {
                    var catalogBinding = catalog.Bindings[(int)(dispatchCase.FirstBinding + local)];
                    var resolution = new BindingResolution(catalogBinding, liveBytes);
                    if (catalogBinding.Kind == NativeBurstDispatchBindingKindV2.BlackboardRead
                        || catalogBinding.Kind == NativeBurstDispatchBindingKindV2.BlackboardWrite
                        || catalogBinding.Kind == NativeBurstDispatchBindingKindV2.BlackboardReadWrite)
                    {
                        var field = catalog.ConfigurationFields[(int)catalogBinding.ConfigurationFieldOrdinal];
                        var accessOrdinal = ReadU32(binding.SemanticProgram.ConfigBlob, node.ConfigOffset + field.ByteOffset);
                        if (!TryResolveTreeSlot(binding, nodeIndex, accessOrdinal, out var slotIndex))
                        {
                            failure = BurstContextResult.PhaseViolation;
                            return false;
                        }
                        resolution = resolution.WithTarget(slotIndex);
                    }
                    else if (catalogBinding.Kind == NativeBurstDispatchBindingKindV2.EffectCommand)
                    {
                        resolution = resolution.WithTarget(catalogBinding.BindingOrdinal);
                    }
                    else
                    {
                        failure = BurstContextResult.PhaseViolation;
                        return false;
                    }
                    resolutions[local] = resolution;
                    liveBytes = checked(liveBytes + catalogBinding.PrimaryValueSize);
                }

                var created = new DispatchSlot(nodeIndex, caseIndex, resolutions);
                try
                {
                    var capacity = catalog.CreateWorkspaceCapacity();
                    created._configuration = new NativeArray<byte>((int)dispatchCase.ConfigurationSize, Allocator.Persistent);
                    created._memory = new NativeArray<byte>((int)dispatchCase.MemorySize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                    created._randomStates = new NativeArray<ulong>(dispatchCase.HasRandomStream != 0 ? 1 : 0, Allocator.Persistent);
                    created._randomIncrements = new NativeArray<ulong>(dispatchCase.HasRandomStream != 0 ? 1 : 0, Allocator.Persistent);
                    created._resolvedBindings = new NativeArray<NativeBurstDispatchResolvedBindingV2>((int)dispatchCase.BindingCount, Allocator.Persistent);
                    created._bindingValues = new NativeArray<byte>((int)liveBytes, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                    created._completions = new NativeArray<NativeBurstDispatchCompletionV2>(0, Allocator.Persistent);
                    created._completionPayload = new NativeArray<byte>(0, Allocator.Persistent);
                    created._commands = new NativeArray<NativeBurstDispatchCommandV2>((int)capacity.BindingCapacity.MaxCommands, Allocator.Persistent);
                    created._commandPayload = new NativeArray<byte>((int)capacity.BindingCapacity.MaxCommandPayloadBytes, Allocator.Persistent);
                    created._operations = new NativeArray<NativeBurstDispatchOperationV2>((int)capacity.BindingCapacity.MaxOperations, Allocator.Persistent);
                    created._transaction = new NativeArray<NativeBurstDispatchTransactionControlV2>(1, Allocator.Persistent);
                    created._publishedCommands = new NativeArray<NativeBurstDispatchCommandV2>((int)capacity.BindingCapacity.MaxCommands, Allocator.Persistent);
                    created._publishedPayload = new NativeArray<byte>((int)capacity.BindingCapacity.MaxCommandPayloadBytes, Allocator.Persistent);
                    for (uint offset = 0; offset < dispatchCase.ConfigurationSize; offset++)
                        created._configuration[(int)offset] = binding.SemanticProgram.ConfigBlob[(int)(node.ConfigOffset + offset)];
                    for (var index = 0; index < resolutions.Length; index++)
                        created._resolvedBindings[index] = new NativeBurstDispatchResolvedBindingV2(
                            catalog.Bindings[(int)(dispatchCase.FirstBinding + (uint)index)].BindingOrdinal,
                            resolutions[index].TargetOrdinal,
                            resolutions[index].LiveOffset);
                    if (!NativeOwnerIdentityV1.TryNext(out var ledgerToken))
                        throw new OverflowException();
                    var transaction = new NativeBurstDispatchTransactionControlV2
                    {
                        LedgerToken = ledgerToken,
                        TreeInstanceId = treeInstanceId,
                        NextOperationSequence = capacity.BindingCapacity.FirstOperationSequence,
                    };
                    NativeBurstDispatchTransactionLedgerV2.Initialize(ref transaction);
                    created._transaction[0] = transaction;
                    var shape = catalog.CreateWorkspaceShape();
                    if (!NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                            in shape, in capacity, Allocator.Persistent,
                            out created._workspace, out failure))
                    {
                        created.Dispose();
                        return false;
                    }
                    slot = created;
                    failure = BurstContextResult.Success;
                    return true;
                }
                catch (Exception)
                {
                    created.Dispose();
                    failure = BurstContextResult.CapacityExceeded;
                    return false;
                }
            }

            internal BurstContextResult Execute(
                NativeProgramBlackboardBindingV2 programBinding,
                GeneratedBurstCatalogV2 catalog,
                NativeArray<byte> treeValues,
                NativeArray<ulong> treeVersions,
                uint agentBaseOffset,
                in ProductionTreeHost.DispatchRequest hostRequest,
                bool scheduled,
                out NodeStatus status)
            {
                status = NodeStatus.Running;
                if (_disposed) return BurstContextResult.InvalidHandle;
                var node = programBinding.SemanticProgram.Nodes[(int)_nodeIndex];
                if (hostRequest.Phase == BurstCallbackPhase.Enter)
                {
                    if (_activationGeneration == uint.MaxValue) return BurstContextResult.Overflow;
                    _activationGeneration++;
                    if (node.MemoryLifetime == NodeMemoryLifetime.Activation) Clear(_memory);
                }
                if (_activationGeneration == 0) _activationGeneration = 1;
                if (!SnapshotTreeValues(programBinding, treeValues, agentBaseOffset)) return BurstContextResult.InvalidHandle;

                var dispatchCase = catalog.Cases[(int)_caseIndex];
                var request = new NativeBurstDispatchRequestV2(
                    0u, _nodeIndex, node.NodeTypeId, node.NodeTypeVersion, _caseIndex,
                    hostRequest.Phase, 0u, 0u, 0u, hostRequest.TimeMicroseconds,
                    _transaction[0].TreeInstanceId, _activationGeneration, 0u,
                    dispatchCase.BindingCount, hostRequest.AbortReason, hostRequest.ExitReason);
                var views = new NativeBurstDispatchWorkspaceRequestViewsV2(
                    request, _configuration, _memory, _randomStates, _randomIncrements,
                    _resolvedBindings, _bindingValues, _completions, _completionPayload,
                    _commands, _commandPayload, _operations, _transaction);
                if (!_workspace.TryBeginRequest(in views, out var lease, out var failure)
                    || !_workspace.TryAcquireImmediateBatch(in lease, out var batch, out failure))
                    return failure;

                if (scheduled)
                {
                    var dependency = catalog.Executor.Schedule(ref batch, default);
                    if (!_workspace.TryRegisterDependency(in lease, in batch, dependency, out failure)) return failure;
                    dependency.Complete();
                    if (!_workspace.TryAcquireCompletedBatch(in lease, out _, out failure)) return failure;
                }
                else
                {
                    var execution = catalog.Executor.ExecuteImmediate(ref batch);
                    if (!execution.Success) return BurstContextResult.InvalidEncoding;
                }

                if (!_workspace.TryConsumeResult(in lease, out var result, out failure)) return failure;
                var callback = result.Execution.Success ? result.CallbackFailure : BurstContextResult.InvalidEncoding;
                if (callback == BurstContextResult.Success
                    && !CommitTreeValues(programBinding, treeValues, treeVersions, agentBaseOffset))
                    callback = BurstContextResult.Overflow;
                if (callback == BurstContextResult.Success) PublishCommands(result.Transaction);
                status = result.Status;
                if (!_workspace.TryAcknowledgePublishedCommands(in lease, out failure)
                    || !_workspace.TryReset(in lease, out failure)) return failure;
                return callback;
            }

            /// <summary>
            /// Same activation/memory-clear/tree-value-snapshot preamble as <see cref="Execute"/>,
            /// but stops before touching this slot's own <see cref="_workspace"/> -- the returned
            /// participant exposes this slot's own persistent storage directly (not a copy) for a
            /// <see cref="ProductionTreeScheduler"/> group wave to fold into one shared batch.
            /// </summary>
            internal bool TryPrepareForGroup(
                NativeProgramBlackboardBindingV2 programBinding,
                GeneratedBurstCatalogV2 catalog,
                NativeArray<byte> treeValues,
                uint agentBaseOffset,
                in ProductionTreeHost.DispatchRequest hostRequest,
                TreeInstanceId treeInstanceId,
                out GeneratedDispatchGroupParticipantV2 participant,
                out BurstContextResult failure)
            {
                participant = default;
                if (_disposed)
                {
                    failure = BurstContextResult.InvalidHandle;
                    return false;
                }
                var node = programBinding.SemanticProgram.Nodes[(int)_nodeIndex];
                if (hostRequest.Phase == BurstCallbackPhase.Enter)
                {
                    if (_activationGeneration == uint.MaxValue)
                    {
                        failure = BurstContextResult.Overflow;
                        return false;
                    }
                    _activationGeneration++;
                    if (node.MemoryLifetime == NodeMemoryLifetime.Activation) Clear(_memory);
                }
                if (_activationGeneration == 0) _activationGeneration = 1;
                if (!SnapshotTreeValues(programBinding, treeValues, agentBaseOffset))
                {
                    failure = BurstContextResult.InvalidHandle;
                    return false;
                }

                participant = new GeneratedDispatchGroupParticipantV2(
                    node.NodeTypeId, node.NodeTypeVersion, _caseIndex, hostRequest.Phase,
                    _configuration, _memory, _randomStates, _randomIncrements,
                    _resolvedBindings, _bindingValues, treeInstanceId, _activationGeneration,
                    hostRequest.AbortReason, hostRequest.ExitReason);
                failure = BurstContextResult.Success;
                return true;
            }

            /// <summary>
            /// Writes a group wave's own committed memory/binding-value bytes for this one
            /// participant back into this slot's persistent storage (so the next tick sees it, same
            /// as <see cref="Execute"/>'s own single-request path) and commits tree/blackboard
            /// values. The caller (the group executor) has already verified the shared batch
            /// succeeded and carries no commands before calling this -- command publication is not
            /// yet supported for grouped dispatch, a disclosed P7-033 gap, not silently dropped.
            /// </summary>
            internal bool TryCommitFromGroup(
                NativeProgramBlackboardBindingV2 programBinding,
                NativeArray<byte> treeValues,
                NativeArray<ulong> treeVersions,
                uint agentBaseOffset,
                NativeArray<byte>.ReadOnly committedMemory,
                NativeArray<byte>.ReadOnly committedBindingValues,
                out BurstContextResult failure)
            {
                if (_disposed || committedMemory.Length != _memory.Length || committedBindingValues.Length != _bindingValues.Length)
                {
                    failure = BurstContextResult.InvalidHandle;
                    return false;
                }
                for (var index = 0; index < _memory.Length; index++) _memory[index] = committedMemory[index];
                for (var index = 0; index < _bindingValues.Length; index++) _bindingValues[index] = committedBindingValues[index];
                if (!CommitTreeValues(programBinding, treeValues, treeVersions, agentBaseOffset))
                {
                    failure = BurstContextResult.Overflow;
                    return false;
                }
                failure = BurstContextResult.Success;
                return true;
            }

            internal bool TryValidateGroupCommit(
                NativeProgramBlackboardBindingV2 programBinding,
                NativeArray<byte> treeValues,
                NativeArray<ulong> treeVersions,
                uint agentBaseOffset,
                NativeArray<byte>.ReadOnly committedMemory,
                NativeArray<byte>.ReadOnly committedBindingValues,
                out BurstContextResult failure)
            {
                if (_disposed || committedMemory.Length != _memory.Length
                    || committedBindingValues.Length != _bindingValues.Length)
                {
                    failure = BurstContextResult.InvalidHandle;
                    return false;
                }
                if (!CanCommitTreeValues(
                        programBinding, treeValues, treeVersions, agentBaseOffset,
                        committedBindingValues))
                {
                    failure = BurstContextResult.Overflow;
                    return false;
                }
                failure = BurstContextResult.Success;
                return true;
            }

            internal bool TryGetLastPublishedCommand(uint index, out NativeBurstDispatchCommandV2 command)
            {
                command = default;
                if (_disposed || index >= _publishedCommandCount) return false;
                command = _publishedCommands[(int)index];
                return true;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _workspace?.TryDispose(out _);
                _workspace = null;
                GeneratedTreeDispatchAdapterV2.Dispose(ref _publishedPayload);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _publishedCommands);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _transaction);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _operations);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _commandPayload);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _commands);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _completionPayload);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _completions);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _bindingValues);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _resolvedBindings);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _randomIncrements);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _randomStates);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _memory);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _configuration);
            }

            private bool SnapshotTreeValues(
                NativeProgramBlackboardBindingV2 binding, NativeArray<byte> values, uint agentBaseOffset)
            {
                for (var index = 0; index < _resolutions.Length; index++)
                {
                    var resolution = _resolutions[index];
                    if (!resolution.IsBlackboard) continue;
                    var slot = binding.Slots[(int)resolution.TargetOrdinal];
                    var baseOffset = slot.Scope == BlackboardScope.Agent ? agentBaseOffset : 0u;
                    if ((ulong)baseOffset + slot.Offset + slot.Size > (uint)values.Length
                        || (ulong)resolution.LiveOffset + slot.Size > (uint)_bindingValues.Length)
                        return false;
                    for (uint offset = 0; offset < slot.Size; offset++)
                        _bindingValues[(int)(resolution.LiveOffset + offset)] = values[(int)(baseOffset + slot.Offset + offset)];
                }
                return true;
            }

            private bool CommitTreeValues(
                NativeProgramBlackboardBindingV2 binding,
                NativeArray<byte> values,
                NativeArray<ulong> versions,
                uint agentBaseOffset)
            {
                if (!CanCommitTreeValues(
                        binding, values, versions, agentBaseOffset, _bindingValues.AsReadOnly()))
                    return false;
                for (var index = 0; index < _resolutions.Length; index++)
                {
                    var resolution = _resolutions[index];
                    if (!resolution.WritesBlackboard) continue;
                    var slotIndex = resolution.TargetOrdinal;
                    var slot = binding.Slots[(int)slotIndex];
                    var baseOffset = slot.Scope == BlackboardScope.Agent ? agentBaseOffset : 0u;
                    var changed = false;
                    for (uint offset = 0; offset < slot.Size; offset++)
                    {
                        var candidate = _bindingValues[(int)(resolution.LiveOffset + offset)];
                        var target = (int)(baseOffset + slot.Offset + offset);
                        if (values[target] == candidate) continue;
                        values[target] = candidate;
                        changed = true;
                    }
                    if (changed) versions[(int)slotIndex]++;
                }
                return true;
            }

            private bool CanCommitTreeValues(
                NativeProgramBlackboardBindingV2 binding,
                NativeArray<byte> values,
                NativeArray<ulong> versions,
                uint agentBaseOffset,
                NativeArray<byte>.ReadOnly candidateBindingValues)
            {
                for (var index = 0; index < _resolutions.Length; index++)
                {
                    var resolution = _resolutions[index];
                    if (!resolution.WritesBlackboard) continue;
                    var slotIndex = resolution.TargetOrdinal;
                    var slot = binding.Slots[(int)slotIndex];
                    var baseOffset = slot.Scope == BlackboardScope.Agent ? agentBaseOffset : 0u;
                    var changed = false;
                    for (uint offset = 0; offset < slot.Size; offset++)
                        if (values[(int)(baseOffset + slot.Offset + offset)]
                            != candidateBindingValues[(int)(resolution.LiveOffset + offset)])
                        { changed = true; break; }
                    if (changed && versions[(int)slotIndex] == ulong.MaxValue) return false;
                }
                return true;
            }

            private void PublishCommands(NativeBurstDispatchTransactionSnapshotV2 transaction)
            {
                if (transaction.CommandCount == 0) return;
                _publishedCommandCount = transaction.CommandCount;
                for (uint index = 0; index < transaction.CommandCount; index++)
                    _publishedCommands[(int)index] = _commands[(int)index];
                for (uint index = 0; index < transaction.CommandPayloadByteCount; index++)
                    _publishedPayload[(int)index] = _commandPayload[(int)index];
            }

            private static bool TryResolveTreeSlot(
                NativeProgramBlackboardBindingV2 binding,
                uint nodeIndex,
                uint accessOrdinal,
                out uint slotIndex)
            {
                slotIndex = 0;
                for (var accessIndex = 0; accessIndex < binding.Accesses.Count; accessIndex++)
                {
                    var access = binding.Accesses[accessIndex];
                    if (access.NodeIndex != nodeIndex || access.AccessOrdinal != accessOrdinal) continue;
                    if (access.Scope != BlackboardScope.Tree && access.Scope != BlackboardScope.Agent) return false;
                    for (var index = 0; index < binding.Slots.Count; index++)
                    {
                        var slot = binding.Slots[index];
                        if (slot.Scope != access.Scope || slot.ScopeSlotIndex != access.SlotIndex) continue;
                        slotIndex = (uint)index;
                        return true;
                    }
                }
                return false;
            }

            private static uint ReadU32(System.Collections.Generic.IReadOnlyList<byte> bytes, uint offset)
                => bytes[(int)offset] | (uint)bytes[(int)(offset + 1)] << 8
                    | (uint)bytes[(int)(offset + 2)] << 16 | (uint)bytes[(int)(offset + 3)] << 24;

            private static void Clear<T>(NativeArray<T> array) where T : struct
            {
                for (var index = 0; index < array.Length; index++) array[index] = default;
            }
        }

        private readonly struct BindingResolution
        {
            internal BindingResolution(NativeBurstDispatchBindingV2 binding, uint liveOffset)
            {
                Kind = binding.Kind;
                TargetOrdinal = 0;
                LiveOffset = liveOffset;
            }
            internal NativeBurstDispatchBindingKindV2 Kind { get; }
            internal uint TargetOrdinal { get; }
            internal uint LiveOffset { get; }
            internal bool IsBlackboard => Kind == NativeBurstDispatchBindingKindV2.BlackboardRead
                || Kind == NativeBurstDispatchBindingKindV2.BlackboardWrite
                || Kind == NativeBurstDispatchBindingKindV2.BlackboardReadWrite;
            internal bool WritesBlackboard => Kind == NativeBurstDispatchBindingKindV2.BlackboardWrite
                || Kind == NativeBurstDispatchBindingKindV2.BlackboardReadWrite;
            internal BindingResolution WithTarget(uint target)
                => new BindingResolution(Kind, target, LiveOffset);
            private BindingResolution(NativeBurstDispatchBindingKindV2 kind, uint target, uint liveOffset)
            { Kind = kind; TargetOrdinal = target; LiveOffset = liveOffset; }
        }
    }
}
