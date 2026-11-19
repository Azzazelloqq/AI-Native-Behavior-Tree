using System;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;

namespace AIBT
{
    /// <summary>
    /// Instance-owned, allocation-free-after-bootstrap bridge used by the standalone production
    /// host. Population grouping remains the responsibility of the P7-033 coordinator.
    /// </summary>
    internal sealed class GeneratedTreeDispatchAdapterV2 : IDisposable
    {
        private static readonly ProfilerMarker DispatchMarker = new ProfilerMarker("AIBT.Production.GeneratedDispatch");
        private readonly GeneratedTreeRuntimeDefinitionV2 _definition;
        private readonly GeneratedBurstCatalogV2 _catalog;
        private TreeInstanceId _treeInstanceId;
        private readonly DispatchSlot[] _slots;
        private readonly uint _agentBaseOffset;
        private NativeArray<byte> _treeValues;
        private NativeArray<ulong> _treeVersions;
        private UnsafeList<AgentFrameSlotV1> _agentFrameSlots;
        // P10-010: set only for simulation-container agents; hosts never have one.
        private readonly AIBT.Simulation.SimulationAgentBridgeV1 _simulation;
        private bool _disposed;

        private GeneratedTreeDispatchAdapterV2(
            GeneratedTreeRuntimeDefinitionV2 definition,
            GeneratedBurstCatalogV2 catalog,
            TreeInstanceId treeInstanceId,
            DispatchSlot[] slots,
            NativeArray<byte> treeValues,
            NativeArray<ulong> treeVersions,
            uint agentBaseOffset,
            AIBT.Simulation.SimulationAgentBridgeV1 simulation)
        {
            _simulation = simulation;
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
            => TryCreate(definition, catalog, treeInstanceId, null, null, out adapter, out failure);

        /// <summary>
        /// P10-010: with a <paramref name="simulationMap"/> and <paramref name="simulation"/> bridge
        /// (simulation container only), SnapshotRead bindings resolve to the agent's row of an input
        /// channel and EffectCommand bindings to command channels. Without them (every host) both stay
        /// exactly as before: snapshot bindings are refused with PhaseViolation.
        /// </summary>
        internal static bool TryCreate(
            GeneratedTreeRuntimeDefinitionV2 definition,
            GeneratedBurstCatalogV2 catalog,
            TreeInstanceId treeInstanceId,
            AIBT.Simulation.SimulationProgramMapV1 simulationMap,
            AIBT.Simulation.SimulationAgentBridgeV1 simulation,
            out GeneratedTreeDispatchAdapterV2 adapter,
            out BurstContextResult failure)
        {
            adapter = null;
            if ((simulationMap == null) != (simulation == null))
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }
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
                            definition, catalog, (uint)nodeIndex, caseIndex, treeInstanceId, simulationMap,
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
                    definition, catalog, treeInstanceId, slots, values, versions, agentBaseOffset, simulation);
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

        internal bool TryReserveEffectCapacity(uint nodeIndex, ProductionTreeEffectQueue queue)
            => !_disposed && queue != null && nodeIndex < _slots.Length && _slots[nodeIndex] != null
                && queue.CanAccept(checked((int)_slots[nodeIndex].MaximumEffectCommandCount));

        internal bool TryConsumePublishedEffects(
            uint nodeIndex,
            ProductionTreeEffectQueue queue,
            out BurstContextResult failure)
        {
            failure = BurstContextResult.InvalidHandle;
            if (_disposed || queue == null || nodeIndex >= _slots.Length || _slots[nodeIndex] == null)
                return false;
            var slot = _slots[nodeIndex];
            if (!queue.CanAccept(checked((int)slot.LastPublishedCommandCount)))
            {
                failure = BurstContextResult.CapacityExceeded;
                return false;
            }
            for (uint index = 0; index < slot.LastPublishedCommandCount; index++)
                if (!TryReadPublishedEffect(slot, index, out _, out failure))
                {
                    return false;
                }
            for (uint index = 0; index < slot.LastPublishedCommandCount; index++)
            {
                if (!TryReadPublishedEffect(slot, index, out var effect, out failure)
                    || !queue.TryEnqueue(in effect))
                {
                    failure = BurstContextResult.CapacityExceeded;
                    return false;
                }
            }
            slot.ClearPublishedCommands();
            failure = BurstContextResult.Success;
            return true;
        }

        /// <summary>
        /// P10-010: the container's command quota check before node <paramref name="nodeIndex"/> runs
        /// (same routine as the agent-frame job). A node without a slot passes, so Dispatch reports it
        /// as it does for hosts.
        /// </summary>
        internal BurstContextResult TryReserveSimulationCommands(uint nodeIndex)
        {
            if (_disposed || _simulation == null) return BurstContextResult.InvalidHandle;
            if (nodeIndex >= _slots.Length || _slots[nodeIndex] == null) return BurstContextResult.Success;
            return AIBT.Simulation.SimulationAgentRoutinesV1.TryReserve(ref _simulation.Agent, _slots[nodeIndex].CommandChannels);
        }

        /// <summary>P10-010: moves the commands node <paramref name="nodeIndex"/> just published into the agent's step log.</summary>
        internal BurstContextResult TryCaptureSimulationCommands(uint nodeIndex)
        {
            if (_disposed || _simulation == null || nodeIndex >= _slots.Length || _slots[nodeIndex] == null)
                return BurstContextResult.InvalidHandle;
            return _slots[nodeIndex].CaptureSimulationCommands(ref _simulation.Agent);
        }

        internal bool HasSimulation => _simulation != null;

        internal bool TryPublishGroupCommand(
            uint nodeIndex,
            in NativeBurstDispatchCommandV2 command,
            NativeArray<byte>.ReadOnly payload,
            out BurstContextResult failure)
        {
            failure = BurstContextResult.InvalidHandle;
            if (_disposed || nodeIndex >= _slots.Length || _slots[nodeIndex] == null) return false;
            return _slots[nodeIndex].TryAppendPublishedCommand(in command, payload, out failure);
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

        internal NativeArray<byte> TreeValues => _treeValues;
        internal NativeArray<ulong> TreeVersions => _treeVersions;

        /// <summary>
        /// P10-004: whether a JobsAgentFrame job can run this tree, and if not why. Generated hosts
        /// already refuse Shared-scope bindings and async/completion bindings at creation, so the
        /// last two reasons are defensive.
        /// </summary>
        internal AgentFrameFallbackReason GetAgentFrameFallback()
        {
            if (!(_catalog.Executor is AIBT.AgentFrame.IGeneratedAgentFrameExecutorV1))
                return AgentFrameFallbackReason.CatalogWithoutAgentFrameEntry;
            // P10-007: the scheduler asks every frame for every default-profile host; the binding
            // and slot tables are fixed at construction, so their answer is computed once.
            if (_agentFrameBindingFallback < 0) _agentFrameBindingFallback = (int)GetAgentFrameBindingFallback();
            return (AgentFrameFallbackReason)_agentFrameBindingFallback;
        }

        private int _agentFrameBindingFallback = -1;

        private AgentFrameFallbackReason GetAgentFrameBindingFallback()
        {
            var binding = _definition.Binding;
            for (var index = 0; index < binding.Accesses.Count; index++)
            {
                var access = binding.Accesses[index];
                if (access.Scope == BlackboardScope.Shared && access.Mode != NativeBlackboardAccessModeV2.Read)
                    return AgentFrameFallbackReason.SharedOrWorldScopeWrite;
            }
            for (var nodeIndex = 0; nodeIndex < _slots.Length; nodeIndex++)
            {
                if (_slots[nodeIndex] == null) continue;
                var dispatchCase = _catalog.Cases[(int)_slots[nodeIndex].CaseIndex];
                // P10-011: the simulation container runs external actions in the job too (its action
                // routines); hosts refuse them at creation, so for them this stays a defensive check.
                if (_simulation != null) continue;
                for (uint local = 0; local < dispatchCase.BindingCount; local++)
                {
                    var kind = _catalog.Bindings[(int)(dispatchCase.FirstBinding + local)].Kind;
                    if (kind == NativeBurstDispatchBindingKindV2.AsyncOperation
                        || kind == NativeBurstDispatchBindingKindV2.Completion)
                        return AgentFrameFallbackReason.AsyncOrManagedLeaf;
                }
            }
            return AgentFrameFallbackReason.None;
        }

        /// <summary>P10-004: this tree's slot table for the JobsAgentFrame job (indexed by node), built once.</summary>
        internal UnsafeList<AgentFrameSlotV1> GetAgentFrameSlots()
        {
            if (_disposed) return default;
            if (_agentFrameSlots.IsCreated) return _agentFrameSlots;
            var slots = new UnsafeList<AgentFrameSlotV1>(_slots.Length, Allocator.Persistent);
            for (var index = 0; index < _slots.Length; index++)
            {
                slots.Add(_slots[index] == null
                    ? default
                    : _slots[index].CreateAgentFrameSlot(_definition.Binding, _catalog, _agentBaseOffset));
            }
            _agentFrameSlots = slots;
            return _agentFrameSlots;
        }

        private void DisposeAgentFrameSlots()
        {
            if (!_agentFrameSlots.IsCreated) return;
            for (var index = 0; index < _agentFrameSlots.Length; index++)
            {
                ref var slot = ref _agentFrameSlots.ElementAt(index);
                if (slot.Resolutions.IsCreated) slot.Resolutions.Dispose();
                if (slot.EffectDescriptors.IsCreated) slot.EffectDescriptors.Dispose();
                // CommandChannels, ActionBindings, CompletionStore (and Completions, a view of it) belong to the DispatchSlot.
            }
            _agentFrameSlots.Dispose();
            _agentFrameSlots = default;
        }

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
            using var scope = DispatchMarker.Auto();
            status = NodeStatus.Running;
            if (_disposed || request.NodeIndex >= _slots.Length)
                return BurstContextResult.InvalidHandle;
            var slot = _slots[request.NodeIndex];
            if (slot == null) return BurstContextResult.TypeMismatch;
            return slot.Execute(
                _definition.Binding, _catalog, _treeValues, _treeVersions, _agentBaseOffset,
                in request, scheduled, _simulation, out status);
        }

        private bool TryReadPublishedEffect(
            DispatchSlot slot,
            uint commandIndex,
            out ProductionTreeEffect effect,
            out BurstContextResult failure)
        {
            effect = default;
            if (!slot.TryGetLastPublishedCommand(commandIndex, out var command)
                || command.Kind != NativeBurstDispatchCommandKindV2.Effect
                || !_catalog.Executor.TryGetEffectDescriptor(slot.CaseIndex, command.TargetOrdinal, out var descriptor)
                || !descriptor.IsSupported)
            {
                failure = BurstContextResult.TypeMismatch;
                return false;
            }
            switch (descriptor.ValueKind)
            {
                case ProductionTreeEffectValueKind.Int32:
                    if (command.PayloadSize != 4u || !slot.TryReadPublishedUInt32(command.PayloadOffset, out var intBits))
                    {
                        failure = BurstContextResult.InvalidEncoding;
                        return false;
                    }
                    effect = new ProductionTreeEffect(descriptor, unchecked((int)intBits));
                    failure = BurstContextResult.Success;
                    return true;
                case ProductionTreeEffectValueKind.Float32:
                    if (command.PayloadSize != 4u || !slot.TryReadPublishedUInt32(command.PayloadOffset, out var floatBits))
                    {
                        failure = BurstContextResult.InvalidEncoding;
                        return false;
                    }
                    effect = new ProductionTreeEffect(descriptor, BitConverter.Int32BitsToSingle(unchecked((int)floatBits)));
                    failure = BurstContextResult.Success;
                    return true;
                case ProductionTreeEffectValueKind.Float3:
                    if (command.PayloadSize != 12u
                        || !slot.TryReadPublishedUInt32(command.PayloadOffset, out var xBits)
                        || !slot.TryReadPublishedUInt32(command.PayloadOffset + 4u, out var yBits)
                        || !slot.TryReadPublishedUInt32(command.PayloadOffset + 8u, out var zBits))
                    {
                        failure = BurstContextResult.InvalidEncoding;
                        return false;
                    }
                    effect = new ProductionTreeEffect(descriptor, new Float3Value(
                        BitConverter.Int32BitsToSingle(unchecked((int)xBits)),
                        BitConverter.Int32BitsToSingle(unchecked((int)yBits)),
                        BitConverter.Int32BitsToSingle(unchecked((int)zBits))));
                    failure = BurstContextResult.Success;
                    return true;
                default:
                    failure = BurstContextResult.TypeMismatch;
                    return false;
            }
        }

        /// <summary>
        /// P10-009: makes this adapter a fresh instance of the same tree with
        /// <paramref name="treeInstanceId"/>, reusing every allocation: Tree/Agent-scope values back to
        /// their compiled defaults with version 0, and every generated leaf slot as
        /// <c>DispatchSlot.TryCreate</c> leaves it (memory, ledger, activation state, random
        /// stream words zero). The simulation container's restart in place; never used by hosts.
        /// </summary>
        internal bool TryResetInstance(TreeInstanceId treeInstanceId)
        {
            if (_disposed || !treeInstanceId.IsValid) return false;
            for (var index = 0; index < _slots.Length; index++)
                if (_slots[index] != null && !_slots[index].TryResetInstance(treeInstanceId)) return false;
            for (var index = 0; index < _treeValues.Length; index++) _treeValues[index] = 0;
            InitializeTreeDefaults(_definition.Binding, _treeValues, _agentBaseOffset);
            for (var index = 0; index < _treeVersions.Length; index++) _treeVersions[index] = 0;
            _treeInstanceId = treeInstanceId;
            return true;
        }

        /// <summary>
        /// P10-009: seeds every random-capable generated leaf's stream as
        /// <c>Documentation~/specifications/time-and-random-v1.md</c> requires (root seed, semantic
        /// hash, this instance's tree instance id, runtime node index). Hosts never call this: their
        /// stream words stay as created (see <c>Planning~/Evidence/P10-009/README.md</c>).
        /// </summary>
        internal void SeedRandomStreams(ulong rootSeed, NativeHash256V1 semanticHash)
        {
            if (_disposed) return;
            for (var index = 0; index < _slots.Length; index++)
                _slots[index]?.SeedRandomStream(rootSeed, semanticHash, _treeInstanceId);
        }

        internal TreeInstanceId TreeInstanceId => _treeInstanceId;

        /// <summary>P10-009: the stream words of one node's slot, for tests; false when the node has no stream.</summary>
        internal bool TryReadRandomStream(uint nodeIndex, out ulong state, out ulong increment)
        {
            state = 0;
            increment = 0;
            return !_disposed && nodeIndex < _slots.Length && _slots[nodeIndex] != null
                && _slots[nodeIndex].TryReadRandomStream(out state, out increment);
        }

        /// <summary>P10-009: test-only copy of every per-instance byte (ledger tokens excluded) for the reset-equals-fresh proof.</summary>
        internal void CopyInstanceStateForTests(System.Collections.Generic.List<byte> into)
        {
            into.Clear();
            for (var index = 0; index < _treeValues.Length; index++) into.Add(_treeValues[index]);
            for (var index = 0; index < _treeVersions.Length; index++) AppendU64(into, _treeVersions[index]);
            AppendU64(into, _treeInstanceId.Value);
            for (var index = 0; index < _slots.Length; index++) _slots[index]?.AppendInstanceStateForTests(into);
        }

        private static void AppendU64(System.Collections.Generic.List<byte> into, ulong value)
        {
            for (var shift = 0; shift < 64; shift += 8) into.Add((byte)(value >> shift));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeAgentFrameSlots();
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
            private uint _maximumEffectCommandCount;
            // P10-009: the ledger's first operation sequence, kept for a reset in place.
            private ulong _firstOperationSequence;
            // P10-004: activation generation and published-command counts live in native memory so
            // the JobsAgentFrame framing (AgentFrameCoreV1) continues exactly where this slot left off.
            private NativeArray<AgentFrameSlotStateV1> _state;
            // P10-010 (container only): by local binding ordinal, the command channel of an effect
            // binding or -1 (also -1 for a catalog without binding ids: its commands are discarded).
            private NativeArray<int> _commandChannels;
            // P10-011 (container only, nodes with async-operation or completion bindings): the action
            // routines' view of those bindings by local ordinal, the completion view's length for the
            // current dispatch, and that dispatch's phase and abort reason.
            private NativeArray<AIBT.Simulation.SimulationActionBindingV1> _actionBindings;
            private int _completionCount;
            private BurstCallbackPhase _lastPhase;
            private BurstNodeAbortReason _lastAbortReason;
            private bool _disposed;

            internal NativeArray<int> CommandChannels => _commandChannels;

            internal BurstContextResult CaptureSimulationCommands(ref AIBT.Simulation.SimulationAgentNativeV1 agent)
            {
                if (_disposed) return BurstContextResult.InvalidHandle;
                var state = _state[0];
                var actions = AIBT.Simulation.SimulationActionRoutinesV1.TryCapture(
                    ref agent, _actionBindings, _publishedCommands, _publishedPayload, in state,
                    _completions, _completionCount, _nodeIndex, _lastPhase, _lastAbortReason);
                if (actions != BurstContextResult.Success) return actions;
                var result = AIBT.Simulation.SimulationAgentRoutinesV1.TryCapture(
                    ref agent, _commandChannels, _publishedCommands, _publishedPayload, ref state);
                _state[0] = state;
                return result;
            }

            private uint _activationGeneration
            {
                get => _state[0].ActivationGeneration;
                set { var state = _state[0]; state.ActivationGeneration = value; _state[0] = state; }
            }

            private uint _publishedCommandCount
            {
                get => _disposed ? 0u : _state[0].PublishedCommandCount;
                set { var state = _state[0]; state.PublishedCommandCount = value; _state[0] = state; }
            }

            private uint _publishedPayloadByteCount
            {
                get => _disposed ? 0u : _state[0].PublishedPayloadByteCount;
                set { var state = _state[0]; state.PublishedPayloadByteCount = value; _state[0] = state; }
            }

            private DispatchSlot(uint nodeIndex, uint caseIndex, BindingResolution[] resolutions, uint maximumEffectCommandCount)
            {
                _nodeIndex = nodeIndex;
                _caseIndex = caseIndex;
                _resolutions = resolutions;
                _maximumEffectCommandCount = maximumEffectCommandCount;
            }

            internal uint LastPublishedCommandCount => _publishedCommandCount;
            internal uint MaximumEffectCommandCount => _maximumEffectCommandCount;
            internal uint CaseIndex => _caseIndex;

            internal static bool TryCreate(
                GeneratedTreeRuntimeDefinitionV2 definition,
                GeneratedBurstCatalogV2 catalog,
                uint nodeIndex,
                uint caseIndex,
                TreeInstanceId treeInstanceId,
                AIBT.Simulation.SimulationProgramMapV1 simulationMap,
                out DispatchSlot slot,
                out BurstContextResult failure)
            {
                slot = null;
                var binding = definition.Binding;
                var node = binding.SemanticProgram.Nodes[(int)nodeIndex];
                var dispatchCase = catalog.Cases[(int)caseIndex];
                var resolutions = new BindingResolution[dispatchCase.BindingCount];
                var commandChannels = simulationMap != null ? new int[dispatchCase.BindingCount] : null;
                AIBT.Simulation.SimulationActionBindingV1[] actionBindings = null;
                var completionBindingCount = 0u;
                var completionBytes = 0u;
                var liveBytes = 0u;
                var effectBindingCount = 0u;
                for (uint local = 0; local < dispatchCase.BindingCount; local++)
                {
                    var globalOrdinal = dispatchCase.FirstBinding + local;
                    var catalogBinding = catalog.Bindings[(int)globalOrdinal];
                    if (commandChannels != null) commandChannels[local] = -1;
                    if (catalogBinding.Kind == NativeBurstDispatchBindingKindV2.SnapshotRead && simulationMap != null)
                    {
                        // P10-010: the agent's row of an input channel, staged into this live region
                        // before each dispatch. Two bindings of one channel in one node share the region
                        // (the request check requires live bindings with one target to share an offset).
                        var channel = simulationMap.InputChannelByBinding[(int)globalOrdinal];
                        if (channel < 0)
                        {
                            failure = BurstContextResult.PhaseViolation;
                            return false;
                        }
                        var snapshotOffset = liveBytes;
                        var shared = false;
                        for (uint previous = 0; previous < local; previous++)
                        {
                            if (!resolutions[previous].IsSnapshot || resolutions[previous].InputChannel != channel) continue;
                            snapshotOffset = resolutions[previous].LiveOffset;
                            shared = true;
                            break;
                        }
                        resolutions[local] = BindingResolution.Snapshot(
                            simulationMap.InputTargetBase + (uint)channel, snapshotOffset, channel, catalogBinding.PrimaryValueSize);
                        if (!shared) liveBytes = checked(liveBytes + catalogBinding.PrimaryValueSize);
                        continue;
                    }
                    var resolution = new BindingResolution(catalogBinding, liveBytes);
                    if (catalogBinding.Kind == NativeBurstDispatchBindingKindV2.BlackboardRead
                        || catalogBinding.Kind == NativeBurstDispatchBindingKindV2.BlackboardWrite
                        || catalogBinding.Kind == NativeBurstDispatchBindingKindV2.BlackboardReadWrite)
                    {
                        var field = catalog.ConfigurationFields[(int)(dispatchCase.FirstConfigurationField + catalogBinding.ConfigurationFieldOrdinal)];
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
                        effectBindingCount++;
                        if (commandChannels != null) commandChannels[local] = simulationMap.CommandChannelByBinding[(int)globalOrdinal];
                    }
                    else if (simulationMap != null
                        && (catalogBinding.Kind == NativeBurstDispatchBindingKindV2.AsyncOperation
                            || catalogBinding.Kind == NativeBurstDispatchBindingKindV2.Completion))
                    {
                        // P10-011 (container only): an external action or its completion. Hosts keep
                        // refusing both below.
                        var asyncOperation = catalogBinding.Kind == NativeBurstDispatchBindingKindV2.AsyncOperation;
                        var actionChannel = asyncOperation
                            ? simulationMap.ActionChannelByBinding[(int)globalOrdinal]
                            : simulationMap.CompletionChannelByBinding[(int)globalOrdinal];
                        if (actionChannel < 0)
                        {
                            failure = BurstContextResult.PhaseViolation;
                            return false;
                        }
                        resolution = resolution.WithTarget(catalogBinding.BindingOrdinal);
                        actionBindings ??= new AIBT.Simulation.SimulationActionBindingV1[dispatchCase.BindingCount];
                        actionBindings[local] = new AIBT.Simulation.SimulationActionBindingV1
                        {
                            Kind = asyncOperation ? AIBT.Simulation.SimulationActionBindingV1.AsyncOperation : AIBT.Simulation.SimulationActionBindingV1.Completion,
                            Target = catalogBinding.BindingOrdinal,
                            Channel = actionChannel,
                            PrimaryTypeId = catalogBinding.PrimaryTypeNumericId,
                            PrimaryTypeVersion = catalogBinding.PrimaryTypeVersion,
                            PrimarySize = catalogBinding.PrimaryValueSize,
                            SecondaryTypeId = catalogBinding.SecondaryTypeNumericId,
                            SecondaryTypeVersion = catalogBinding.SecondaryTypeVersion,
                            SecondarySize = catalogBinding.SecondaryValueSize,
                        };
                        if (!asyncOperation)
                        {
                            completionBindingCount++;
                            completionBytes = checked(completionBytes + catalogBinding.PrimaryValueSize);
                        }
                    }
                    else
                    {
                        failure = BurstContextResult.PhaseViolation;
                        return false;
                    }
                    resolutions[local] = resolution;
                    if (resolution.IsBlackboard)
                        liveBytes = checked(liveBytes + catalogBinding.PrimaryValueSize);
                }

                var created = new DispatchSlot(nodeIndex, caseIndex, resolutions, effectBindingCount);
                try
                {
                    var capacity = catalog.CreateWorkspaceCapacity();
                    created._configuration = new NativeArray<byte>((int)dispatchCase.ConfigurationSize, Allocator.Persistent);
                    created._memory = new NativeArray<byte>((int)dispatchCase.MemorySize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                    created._randomStates = new NativeArray<ulong>(dispatchCase.HasRandomStream != 0 ? 1 : 0, Allocator.Persistent);
                    created._randomIncrements = new NativeArray<ulong>(dispatchCase.HasRandomStream != 0 ? 1 : 0, Allocator.Persistent);
                    created._resolvedBindings = new NativeArray<NativeBurstDispatchResolvedBindingV2>((int)dispatchCase.BindingCount, Allocator.Persistent);
                    created._bindingValues = new NativeArray<byte>((int)liveBytes, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                    // P10-011: a container node with completion bindings gets room for one completion per
                    // live operation and completion binding (the dispatch sees a prefix view of it).
                    var operationLimit = capacity.BindingCapacity.MaxOperations;
                    created._completions = new NativeArray<NativeBurstDispatchCompletionV2>(
                        checked((int)(operationLimit * completionBindingCount)), Allocator.Persistent);
                    created._completionPayload = new NativeArray<byte>(
                        checked((int)(operationLimit * completionBytes)), Allocator.Persistent, NativeArrayOptions.ClearMemory);
                    if (actionBindings != null)
                        created._actionBindings = new NativeArray<AIBT.Simulation.SimulationActionBindingV1>(actionBindings, Allocator.Persistent);
                    created._commands = new NativeArray<NativeBurstDispatchCommandV2>((int)capacity.BindingCapacity.MaxCommands, Allocator.Persistent);
                    created._commandPayload = new NativeArray<byte>((int)capacity.BindingCapacity.MaxCommandPayloadBytes, Allocator.Persistent);
                    created._operations = new NativeArray<NativeBurstDispatchOperationV2>((int)capacity.BindingCapacity.MaxOperations, Allocator.Persistent);
                    created._transaction = new NativeArray<NativeBurstDispatchTransactionControlV2>(1, Allocator.Persistent);
                    created._publishedCommands = new NativeArray<NativeBurstDispatchCommandV2>((int)capacity.BindingCapacity.MaxCommands, Allocator.Persistent);
                    created._publishedPayload = new NativeArray<byte>((int)capacity.BindingCapacity.MaxCommandPayloadBytes, Allocator.Persistent);
                    created._state = new NativeArray<AgentFrameSlotStateV1>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                    if (commandChannels != null)
                        created._commandChannels = new NativeArray<int>(commandChannels, Allocator.Persistent);
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
                    created._firstOperationSequence = capacity.BindingCapacity.FirstOperationSequence;
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
                AIBT.Simulation.SimulationAgentBridgeV1 simulation,
                out NodeStatus status)
            {
                status = NodeStatus.Running;
                if (_disposed) return BurstContextResult.InvalidHandle;
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Enter);
                ClearPublishedCommands();
                var node = programBinding.SemanticProgram.Nodes[(int)_nodeIndex];
                if (hostRequest.Phase == BurstCallbackPhase.Enter)
                {
                    if (_activationGeneration == uint.MaxValue) return BurstContextResult.Overflow;
                    _activationGeneration++;
                    if (node.MemoryLifetime == NodeMemoryLifetime.Activation) Clear(_memory);
                }
                if (_activationGeneration == 0) _activationGeneration = 1;
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Activation);
                if (!SnapshotTreeValues(programBinding, treeValues, agentBaseOffset)) return BurstContextResult.InvalidHandle;
                if (!StageInputs(simulation)) return BurstContextResult.InvalidHandle;
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Snapshot);

                var completions = _completions;
                if (simulation != null && _actionBindings.IsCreated)
                {
                    // P10-011: this node's live actions and the game's completions, from the agent's ledger.
                    _lastPhase = hostRequest.Phase;
                    _lastAbortReason = hostRequest.AbortReason;
                    var prepared = AIBT.Simulation.SimulationActionRoutinesV1.TryPrepare(
                        ref simulation.Agent, _actionBindings, _nodeIndex, _transaction[0].TreeInstanceId, _activationGeneration,
                        _operations, _transaction, _completions, _completionPayload, out _completionCount);
                    if (prepared != BurstContextResult.Success) return prepared;
                    if (!_workspace.TryRebaseOperationCountFloor(_transaction[0].OperationCount)) return BurstContextResult.PhaseViolation;
                    completions = _completions.GetSubArray(0, _completionCount);
                }

                var dispatchCase = catalog.Cases[(int)_caseIndex];
                var request = new NativeBurstDispatchRequestV2(
                    0u, _nodeIndex, node.NodeTypeId, node.NodeTypeVersion, _caseIndex,
                    hostRequest.Phase, 0u, 0u, 0u, hostRequest.TimeMicroseconds,
                    _transaction[0].TreeInstanceId, _activationGeneration, 0u,
                    dispatchCase.BindingCount, hostRequest.AbortReason, hostRequest.ExitReason);
                var views = new NativeBurstDispatchWorkspaceRequestViewsV2(
                    request, _configuration, _memory, _randomStates, _randomIncrements,
                    _resolvedBindings, _bindingValues, completions, _completionPayload,
                    _commands, _commandPayload, _operations, _transaction);
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.RequestBuild);
                if (!_workspace.TryBeginRequest(in views, out var lease, out var failure)
                    || !_workspace.TryAcquireImmediateBatch(in lease, out var batch, out failure))
                    return failure;
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.AcquireBatch);

                if (scheduled)
                {
                    var dependency = catalog.Executor.Schedule(ref batch, default);
                    if (!_workspace.TryRegisterDependency(in lease, in batch, dependency, out failure)) return failure;
                    dependency.Complete();
                    if (!_workspace.TryAcquireCompletedBatch(in lease, out _, out failure)) return failure;
                }
                else
                {
                    catalog.Executor.ExecuteImmediate(ref batch);
                }
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Body);

                if (!_workspace.TryConsumeResult(in lease, out var result, out failure)) return failure;
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Consume);
                var callback = result.CallbackFailure;
                if (!result.Execution.Success && callback == BurstContextResult.Success)
                    callback = BurstContextResult.InvalidEncoding;
                if (callback == BurstContextResult.Success
                    && !CommitTreeValues(programBinding, treeValues, treeVersions, agentBaseOffset))
                    callback = BurstContextResult.Overflow;
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Commit);
                if (callback == BurstContextResult.Success) PublishCommands(result.Transaction);
                status = result.Status;
                if (simulation != null && callback == BurstContextResult.StaleCompletion && hostRequest.Phase == BurstCallbackPhase.Tick)
                {
                    // P10-011, the container's host policy for a stale-completion failure: a Tick that
                    // polled an action the game has not completed yet keeps running. The frame published
                    // nothing (the bridge latched and discarded it), so this changes no state.
                    callback = BurstContextResult.Success;
                    status = NodeStatus.Running;
                }
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Publish);
                if (!_workspace.TryAcknowledgePublishedCommands(in lease, out failure)) return failure;
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Acknowledge);
                if (!_workspace.TryReset(in lease, out failure)) return failure;
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Reset);
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
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.GroupParticipant);
                ClearPublishedCommands();
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
                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Activation);
                if (!SnapshotTreeValues(programBinding, treeValues, agentBaseOffset))
                {
                    failure = BurstContextResult.InvalidHandle;
                    return false;
                }

                SchedulerFrameProfileV1.StepLap(SchedulerFrameProfileV1.Step.Snapshot);
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
            /// values. The caller (the group executor) has already validated and attributed any
            /// shared effect commands before calling this, so participant commit remains atomic.
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

            internal bool TryReadPublishedUInt32(uint offset, out uint value)
            {
                value = 0;
                if (_disposed || (ulong)offset + 4u > _publishedPayloadByteCount) return false;
                value = _publishedPayload[(int)offset]
                    | (uint)_publishedPayload[(int)(offset + 1u)] << 8
                    | (uint)_publishedPayload[(int)(offset + 2u)] << 16
                    | (uint)_publishedPayload[(int)(offset + 3u)] << 24;
                return true;
            }

            internal void ClearPublishedCommands()
            {
                var state = _state[0];
                state.PublishedCommandCount = 0;
                state.PublishedPayloadByteCount = 0;
                _state[0] = state;
            }

            /// <summary>
            /// P10-004: handles to this slot's storage for the JobsAgentFrame job, with its binding
            /// resolutions laid out against the tree-value buffer and its effect descriptors looked
            /// up once. The returned resolution and descriptor arrays are the caller's to dispose.
            /// </summary>
            internal AgentFrameSlotV1 CreateAgentFrameSlot(
                NativeProgramBlackboardBindingV2 programBinding,
                GeneratedBurstCatalogV2 catalog,
                uint agentBaseOffset)
            {
                var node = programBinding.SemanticProgram.Nodes[(int)_nodeIndex];
                var dispatchCase = catalog.Cases[(int)_caseIndex];
                var resolutions = new NativeArray<AgentFrameResolutionV1>(_resolutions.Length, Allocator.Persistent);
                var descriptors = new NativeArray<AgentFrameEffectDescriptorV1>((int)_maximumEffectCommandCount, Allocator.Persistent);
                var descriptorIndex = 0;
                for (var index = 0; index < _resolutions.Length; index++)
                {
                    var resolution = _resolutions[index];
                    var native = new AgentFrameResolutionV1
                    {
                        IsBlackboard = resolution.IsBlackboard ? (byte)1 : (byte)0,
                        Writes = resolution.WritesBlackboard ? (byte)1 : (byte)0,
                        TargetOrdinal = resolution.TargetOrdinal,
                        LiveOffset = resolution.LiveOffset,
                        InputChannel = -1,
                    };
                    if (resolution.IsSnapshot)
                    {
                        native.IsSnapshot = 1;
                        native.InputChannel = resolution.InputChannel;
                        native.Size = resolution.Size;
                    }
                    else if (resolution.IsBlackboard)
                    {
                        var slot = programBinding.Slots[(int)resolution.TargetOrdinal];
                        var baseOffset = slot.Scope == BlackboardScope.Agent ? agentBaseOffset : 0u;
                        native.StorageOffset = baseOffset + slot.Offset;
                        native.Size = slot.Size;
                    }
                    else if (resolution.Kind == NativeBurstDispatchBindingKindV2.EffectCommand)
                    {
                        var found = catalog.Executor.TryGetEffectDescriptor(_caseIndex, resolution.TargetOrdinal, out var descriptor);
                        descriptors[descriptorIndex++] = new AgentFrameEffectDescriptorV1
                        {
                            TargetOrdinal = resolution.TargetOrdinal,
                            Descriptor = descriptor,
                            Found = found ? (byte)1 : (byte)0,
                        };
                    }
                    resolutions[index] = native;
                }
                return new AgentFrameSlotV1
                {
                    Present = 1,
                    ActivationMemory = node.MemoryLifetime == NodeMemoryLifetime.Activation ? (byte)1 : (byte)0,
                    NodeIndex = _nodeIndex,
                    CaseIndex = _caseIndex,
                    NodeTypeId = node.NodeTypeId,
                    NodeTypeVersion = node.NodeTypeVersion,
                    BindingCount = dispatchCase.BindingCount,
                    MaximumEffectCommandCount = _maximumEffectCommandCount,
                    State = _state,
                    Resolutions = resolutions,
                    EffectDescriptors = descriptors,
                    Configuration = _configuration,
                    Memory = _memory,
                    RandomStates = _randomStates,
                    RandomIncrements = _randomIncrements,
                    ResolvedBindings = _resolvedBindings,
                    BindingValues = _bindingValues,
                    Completions = _completions,
                    CompletionPayload = _completionPayload,
                    Commands = _commands,
                    CommandPayload = _commandPayload,
                    Operations = _operations,
                    Transaction = _transaction,
                    PublishedCommands = _publishedCommands,
                    PublishedPayload = _publishedPayload,
                    CommandChannels = _commandChannels,
                    ActionBindings = _actionBindings,
                    CompletionStore = _completions,
                    Workspace = _workspace.GetAgentFrameArrays(),
                };
            }

            /// <summary>P10-009: this slot as <see cref="TryCreate"/> leaves it, under a new ledger and tree instance id.</summary>
            internal bool TryResetInstance(TreeInstanceId treeInstanceId)
            {
                if (_disposed || _workspace == null || !_workspace.TryResetLedgerBinding()) return false;
                if (!NativeOwnerIdentityV1.TryNext(out var ledgerToken)) return false;
                Clear(_memory);
                Clear(_randomStates);
                Clear(_randomIncrements);
                Clear(_bindingValues);
                Clear(_completions);
                Clear(_completionPayload);
                Clear(_commands);
                Clear(_commandPayload);
                Clear(_operations);
                Clear(_publishedCommands);
                Clear(_publishedPayload);
                _state[0] = default;
                _completionCount = 0;
                _lastPhase = default;
                _lastAbortReason = default;
                var transaction = new NativeBurstDispatchTransactionControlV2
                {
                    LedgerToken = ledgerToken,
                    TreeInstanceId = treeInstanceId,
                    NextOperationSequence = _firstOperationSequence,
                };
                NativeBurstDispatchTransactionLedgerV2.Initialize(ref transaction);
                _transaction[0] = transaction;
                return true;
            }

            internal void SeedRandomStream(ulong rootSeed, NativeHash256V1 semanticHash, TreeInstanceId treeInstanceId)
            {
                if (_disposed || _randomStates.Length == 0) return;
                if (!NativeRandomStreamDerivationV1.TryDerive(rootSeed, semanticHash, treeInstanceId.Value, _nodeIndex, out var stream))
                    return;
                _randomStates[0] = stream.State;
                _randomIncrements[0] = stream.Increment;
            }

            internal bool TryReadRandomStream(out ulong state, out ulong increment)
            {
                state = 0;
                increment = 0;
                if (_disposed || _randomStates.Length == 0) return false;
                state = _randomStates[0];
                increment = _randomIncrements[0];
                return true;
            }

            internal void AppendInstanceStateForTests(System.Collections.Generic.List<byte> into)
            {
                Append(into, _memory);
                Append(into, _randomStates);
                Append(into, _randomIncrements);
                Append(into, _bindingValues);
                Append(into, _completions);
                Append(into, _completionPayload);
                Append(into, _commands);
                Append(into, _commandPayload);
                Append(into, _operations);
                Append(into, _publishedCommands);
                Append(into, _publishedPayload);
                Append(into, _state);
                var transaction = _transaction[0];
                // The ledger token is a process-wide owner id, and the integrity tag hashes it:
                // compare both as if the token were 0 (a valid tag stays valid after re-sealing).
                var tagValid = NativeBurstDispatchTransactionLedgerV2.IsValid(in transaction);
                transaction.LedgerToken = 0;
                NativeBurstDispatchTransactionLedgerV2.Seal(ref transaction);
                if (!tagValid) transaction.IntegrityTag = ~transaction.IntegrityTag;
                var copy = new NativeArray<NativeBurstDispatchTransactionControlV2>(1, Allocator.Temp);
                copy[0] = transaction;
                Append(into, copy);
                copy.Dispose();
            }

            private static void Append<T>(System.Collections.Generic.List<byte> into, NativeArray<T> array) where T : struct
            {
                var bytes = array.Reinterpret<byte>(UnsafeUtility.SizeOf<T>());
                for (var index = 0; index < bytes.Length; index++) into.Add(bytes[index]);
            }

            internal bool TryAppendPublishedCommand(
                in NativeBurstDispatchCommandV2 command,
                NativeArray<byte>.ReadOnly payload,
                out BurstContextResult failure)
            {
                if (_disposed || command.Kind != NativeBurstDispatchCommandKindV2.Effect
                    || (ulong)command.PayloadOffset + command.PayloadSize > (uint)payload.Length
                    || _publishedCommandCount >= (uint)_publishedCommands.Length
                    || (ulong)_publishedPayloadByteCount + command.PayloadSize > (uint)_publishedPayload.Length)
                {
                    failure = BurstContextResult.InvalidEncoding;
                    return false;
                }
                var destinationOffset = _publishedPayloadByteCount;
                for (uint index = 0; index < command.PayloadSize; index++)
                    _publishedPayload[(int)(destinationOffset + index)] = payload[(int)(command.PayloadOffset + index)];
                _publishedCommands[(int)_publishedCommandCount] = new NativeBurstDispatchCommandV2(
                    command.Kind, command.TargetOrdinal, command.OperationId, destinationOffset, command.PayloadSize);
                _publishedCommandCount++;
                _publishedPayloadByteCount += command.PayloadSize;
                failure = BurstContextResult.Success;
                return true;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _workspace?.TryDispose(out _);
                _workspace = null;
                GeneratedTreeDispatchAdapterV2.Dispose(ref _state);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _actionBindings);
                GeneratedTreeDispatchAdapterV2.Dispose(ref _commandChannels);
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

            /// <summary>P10-010: writes the agent's input rows into the snapshot bindings' live regions (same routine as the job).</summary>
            private bool StageInputs(AIBT.Simulation.SimulationAgentBridgeV1 simulation)
            {
                for (var index = 0; index < _resolutions.Length; index++)
                {
                    var resolution = _resolutions[index];
                    if (!resolution.IsSnapshot) continue;
                    if (simulation == null
                        || !AIBT.Simulation.SimulationAgentRoutinesV1.TryStage(
                            ref simulation.Agent, resolution.InputChannel, _bindingValues, resolution.LiveOffset, resolution.Size))
                        return false;
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
                ClearPublishedCommands();
                if (transaction.CommandCount == 0) return;
                _publishedCommandCount = transaction.CommandCount;
                _publishedPayloadByteCount = transaction.CommandPayloadByteCount;
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
                LiveOffset = IsBlackboardKind(binding.Kind)
                    ? liveOffset
                    : NativeBurstDispatchBindingV2.NoOffset;
                InputChannel = -1;
                Size = 0;
            }
            internal NativeBurstDispatchBindingKindV2 Kind { get; }
            internal uint TargetOrdinal { get; }
            internal uint LiveOffset { get; }
            /// <summary>P10-010: the input channel of a container snapshot binding, else -1.</summary>
            internal int InputChannel { get; }
            /// <summary>P10-010: the canonical value size of a container snapshot binding, else 0.</summary>
            internal uint Size { get; }
            internal bool IsSnapshot => InputChannel >= 0;
            internal static BindingResolution Snapshot(uint target, uint liveOffset, int channel, uint size)
                => new BindingResolution(NativeBurstDispatchBindingKindV2.SnapshotRead, target, liveOffset, channel, size);
            private BindingResolution(NativeBurstDispatchBindingKindV2 kind, uint target, uint liveOffset, int channel, uint size)
            { Kind = kind; TargetOrdinal = target; LiveOffset = liveOffset; InputChannel = channel; Size = size; }
            internal bool IsBlackboard => Kind == NativeBurstDispatchBindingKindV2.BlackboardRead
                || Kind == NativeBurstDispatchBindingKindV2.BlackboardWrite
                || Kind == NativeBurstDispatchBindingKindV2.BlackboardReadWrite;
            internal bool WritesBlackboard => Kind == NativeBurstDispatchBindingKindV2.BlackboardWrite
                || Kind == NativeBurstDispatchBindingKindV2.BlackboardReadWrite;
            private static bool IsBlackboardKind(NativeBurstDispatchBindingKindV2 kind)
                => kind == NativeBurstDispatchBindingKindV2.BlackboardRead
                    || kind == NativeBurstDispatchBindingKindV2.BlackboardWrite
                    || kind == NativeBurstDispatchBindingKindV2.BlackboardReadWrite;
            internal BindingResolution WithTarget(uint target)
                => new BindingResolution(Kind, target, LiveOffset);
            private BindingResolution(NativeBurstDispatchBindingKindV2 kind, uint target, uint liveOffset)
            { Kind = kind; TargetOrdinal = target; LiveOffset = liveOffset; InputChannel = -1; Size = 0; }
        }
    }
}
