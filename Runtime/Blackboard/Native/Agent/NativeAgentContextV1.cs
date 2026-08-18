using System;
using System.Threading;
using AIBT.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AIBT
{
    public readonly struct NativeAgentContextCapacityV1
    {
        public NativeAgentContextCapacityV1(uint valueBytes, uint slotVersions, uint maximumBindings)
        { ValueBytes = valueBytes; SlotVersions = slotVersions; MaximumBindings = maximumBindings; }

        public uint ValueBytes { get; }
        public uint SlotVersions { get; }
        public uint MaximumBindings { get; }

        public static bool TryDerive(
            NativeProgramImageViewV2 program,
            uint maximumBindings,
            out NativeAgentContextCapacityV1 capacity,
            out NativeRuntimeFailureV1 failure)
        {
            capacity = default;
            if (maximumBindings == 0 || maximumBindings > int.MaxValue)
            {
                failure = new NativeRuntimeFailureV1(
                    maximumBindings > int.MaxValue
                        ? NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow
                        : NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.AgentBindings,
                    maximumBindings,
                    int.MaxValue);
                return false;
            }

            var foundDescriptor = false;
            ulong valueBytes = 0;
            uint agentSlots = 0;
            for (var index = 0; index < program.Scopes.Length; index++)
            {
                if (program.Scopes[index].Scope != BlackboardScope.Agent) continue;
                if (foundDescriptor)
                {
                    failure = RegistryFailure();
                    return false;
                }
                foundDescriptor = true;
            }
            for (var index = 0; index < program.Slots.Length; index++)
            {
                var slot = program.Slots[index];
                if (slot.Scope != BlackboardScope.Agent) continue;
                agentSlots++;
                var end = (ulong)slot.Offset + slot.Size;
                if (end > valueBytes) valueBytes = end;
            }
            if (!foundDescriptor || agentSlots == 0 || valueBytes > int.MaxValue)
            {
                failure = valueBytes > int.MaxValue
                    ? new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                        NativeResourceKindV1.InstanceAgentBlackboard, valueBytes, int.MaxValue)
                    : RegistryFailure();
                return false;
            }
            capacity = new NativeAgentContextCapacityV1((uint)valueBytes, (uint)program.Slots.Length, maximumBindings);
            failure = default;
            return true;
        }

        private static NativeRuntimeFailureV1 RegistryFailure()
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch,
                NativeResourceKindV1.ProgramScopeDescriptors);
    }

    public readonly struct NativeAgentBindingV1
    {
        internal NativeAgentBindingV1(ulong ownerId, uint generation, ulong bindingId, TreeInstanceId treeInstanceId)
        { OwnerId = ownerId; Generation = generation; BindingId = bindingId; TreeInstanceId = treeInstanceId; }
        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong BindingId { get; }
        public TreeInstanceId TreeInstanceId { get; }
        public bool IsValid => OwnerId != 0 && Generation != 0 && BindingId != 0 && TreeInstanceId.IsValid;
    }

    public readonly struct NativeAgentExecuteWindowV1
    {
        internal NativeAgentExecuteWindowV1(ulong ownerId, uint generation, ulong windowId, uint count)
        { OwnerId = ownerId; Generation = generation; WindowId = windowId; Count = count; }
        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong WindowId { get; }
        public uint Count { get; }
        public bool IsValid => OwnerId != 0 && Generation != 0 && WindowId != 0 && Count != 0;
    }

    public readonly struct NativeAgentExecuteWindowV2
    {
        internal NativeAgentExecuteWindowV2(
            ulong ownerId,
            uint generation,
            ulong windowId,
            uint count,
            ulong selectionOwnerId,
            uint selectionGeneration,
            ulong selectionWindowId)
        {
            OwnerId = ownerId; Generation = generation; WindowId = windowId; Count = count;
            SelectionOwnerId = selectionOwnerId; SelectionGeneration = selectionGeneration;
            SelectionWindowId = selectionWindowId;
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong WindowId { get; }
        public uint Count { get; }
        public ulong SelectionOwnerId { get; }
        public uint SelectionGeneration { get; }
        public ulong SelectionWindowId { get; }
        public bool IsValid => OwnerId != 0 && Generation != 0 && WindowId != 0 && Count != 0
            && SelectionOwnerId != 0 && SelectionGeneration != 0 && SelectionWindowId != 0;
    }

    public readonly struct NativeAgentContextViewV1
    {
        internal NativeAgentContextViewV1(NativeArray<byte> values, NativeArray<ulong> versions, NativeArray<ulong> revision)
        { Values = values; SlotVersions = versions; Revision = revision; }
        public NativeArray<byte> Values { get; }
        public NativeArray<ulong> SlotVersions { get; }
        public NativeArray<ulong> Revision { get; }
    }

    public readonly struct NativeAgentExecuteLeaseV1
    {
        internal NativeAgentExecuteLeaseV1(
            NativeAgentContextOwnerV1 owner,
            NativeLeaseTokenV1 token,
            NativeAgentExecuteWindowV1 window,
            TreeInstanceId treeInstanceId,
            NativeInstanceExecutionLeaseV2 treeLease,
            NativeAgentContextViewV1 context)
        {
            Owner = owner; Token = token; Window = window; TreeInstanceId = treeInstanceId;
            TreeLease = treeLease; Context = context;
            View = new NativeAgentExecutionViewV1(treeInstanceId, treeLease.Program, context);
        }
        internal NativeAgentContextOwnerV1 Owner { get; }
        public NativeLeaseTokenV1 Token { get; }
        public NativeAgentExecuteWindowV1 Window { get; }
        public TreeInstanceId TreeInstanceId { get; }
        public NativeInstanceExecutionLeaseV2 TreeLease { get; }
        public NativeAgentContextViewV1 Context { get; }
        public NativeAgentExecutionViewV1 View { get; }
        public bool IsValid => Owner != null && Token.IsValid && Window.IsValid && TreeInstanceId.IsValid && TreeLease.IsValid;
    }

    public readonly struct NativeAgentExecuteLeaseV2
    {
        internal NativeAgentExecuteLeaseV2(
            NativeAgentContextOwnerV1 owner,
            NativeLeaseTokenV1 token,
            NativeAgentExecuteWindowV2 window,
            TreeInstanceId treeInstanceId,
            NativeInstanceExecutionLeaseV2 treeLease,
            NativeAgentContextViewV1 context)
        {
            Owner = owner; Token = token; Window = window; TreeInstanceId = treeInstanceId;
            TreeLease = treeLease; Context = context;
            View = new NativeAgentExecutionViewV1(treeInstanceId, treeLease.Program, context);
        }

        internal NativeAgentContextOwnerV1 Owner { get; }
        public NativeLeaseTokenV1 Token { get; }
        public NativeAgentExecuteWindowV2 Window { get; }
        public TreeInstanceId TreeInstanceId { get; }
        public NativeInstanceExecutionLeaseV2 TreeLease { get; }
        public NativeAgentContextViewV1 Context { get; }
        public NativeAgentExecutionViewV1 View { get; }
        public bool IsValid => Owner != null && Token.IsValid && Window.IsValid
            && TreeInstanceId.IsValid && TreeLease.IsValid;
    }

    public readonly struct NativeAgentExecutionViewV1
    {
        internal NativeAgentExecutionViewV1(
            TreeInstanceId treeInstanceId,
            NativeProgramImageViewV2 program,
            NativeAgentContextViewV1 context)
        { TreeInstanceId = treeInstanceId; Program = program; Context = context; }
        public TreeInstanceId TreeInstanceId { get; }
        public NativeProgramImageViewV2 Program { get; }
        public NativeAgentContextViewV1 Context { get; }
        public bool IsValid => TreeInstanceId.IsValid && Context.Values.IsCreated
            && Context.SlotVersions.IsCreated && Context.Revision.IsCreated;
    }

    public sealed class NativeAgentContextRegistryV1
    {
        private NativeAgentContextOwnerV1[] _contexts;
        private readonly Allocator _allocator;
        private NativeOwnerStateV1 _state;

        private NativeAgentContextRegistryV1(uint capacity, Allocator allocator)
        { _contexts = new NativeAgentContextOwnerV1[capacity]; _allocator = allocator; _state = NativeOwnerStateV1.Initialized; }

        public static bool TryCreate(
            uint maximumContexts,
            Allocator allocator,
            out NativeAgentContextRegistryV1 registry,
            out NativeRuntimeFailureV1 failure)
        {
            registry = null;
            if (maximumContexts == 0 || maximumContexts > int.MaxValue
                || allocator == Allocator.Invalid || allocator == Allocator.None)
            {
                failure = new NativeRuntimeFailureV1(
                    maximumContexts > int.MaxValue
                        ? NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow
                        : NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.AgentBindings, maximumContexts, int.MaxValue);
                return false;
            }
            registry = new NativeAgentContextRegistryV1(maximumContexts, allocator);
            failure = default;
            return true;
        }

        public bool TryCreateContext(
            AgentId agentId,
            NativeProgramReadLeaseV2 programLease,
            NativeAgentContextCapacityV1 capacity,
            out NativeAgentContextOwnerV1 context,
            out NativeRuntimeFailureV1 failure)
        {
            context = null;
            if (_state != NativeOwnerStateV1.Initialized || !agentId.IsValid)
            {
                failure = new NativeRuntimeFailureV1(
                    !agentId.IsValid ? NativeRuntimeDiagnosticCodeV1.BlackboardInvalidValue : NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                    NativeResourceKindV1.AgentBindings);
                return false;
            }
            var free = -1;
            for (var index = 0; index < _contexts.Length; index++)
            {
                var current = _contexts[index];
                if (current == null) { if (free < 0) free = index; continue; }
                if (current.AgentId == agentId)
                {
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch,
                        NativeResourceKindV1.AgentBindings);
                    return false;
                }
            }
            if (free < 0)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    NativeResourceKindV1.AgentBindings, (uint)_contexts.Length + 1, (uint)_contexts.Length);
                return false;
            }
            if (!NativeAgentContextOwnerV1.TryCreate(agentId, programLease, capacity, _allocator, out context, out failure)) return false;
            _contexts[free] = context;
            return true;
        }

        public bool TryDestroyContext(NativeAgentContextOwnerV1 context, out NativeRuntimeFailureV1 failure)
        {
            if (_state != NativeOwnerStateV1.Initialized || context == null)
            { failure = LifetimeFailure(); return false; }
            for (var index = 0; index < _contexts.Length; index++)
            {
                if (!ReferenceEquals(_contexts[index], context)) continue;
                if (!context.TryDispose(out failure)) return false;
                _contexts[index] = null;
                return true;
            }
            failure = LifetimeFailure();
            return false;
        }

        public bool TryDispose(out NativeRuntimeFailureV1 failure)
        {
            if (_state != NativeOwnerStateV1.Initialized) { failure = LifetimeFailure(); return false; }
            for (var index = 0; index < _contexts.Length; index++)
                if (_contexts[index] != null)
                { failure = new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation, NativeResourceKindV1.AgentBindings); return false; }
            _contexts = null;
            _state = NativeOwnerStateV1.Disposed;
            failure = default;
            return true;
        }

        private static NativeRuntimeFailureV1 LifetimeFailure()
            => new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid, NativeResourceKindV1.AgentBindings);
    }

    public sealed class NativeAgentContextOwnerV1
    {
        private struct BindingEntry
        {
            internal bool Active;
            internal ulong Id;
            internal TreeInstanceId TreeInstanceId;
            internal NativeProgramReadLeaseV2 ProgramLease;
            internal NativeInstanceArenaOwnerV1 Instance;
        }

        private static long s_nextOwnerId;
        private NativeArray<byte> _values;
        private NativeArray<ulong> _versions;
        private NativeArray<ulong> _revision;
        private NativeArray<TreeInstanceId> _eligible;
        private BindingEntry[] _bindings;
        private NativeProgramImageViewV2 _authority;
        private NativeBlackboardScopeRecordV2 _descriptor;
        private NativeOwnerStateV1 _state;
        private uint _eligibleCount;
        private uint _cursor;
        private ulong _nextBindingId;
        private ulong _nextWindowId;
        private ulong _nextLeaseId;
        private NativeAgentExecuteWindowV1 _window;
        private NativeAgentExecuteLeaseV1 _activeLease;
        private NativeExecuteSelectionReadLeaseV1 _selectionLease;
        private NativeAgentExecuteWindowV2 _windowV2;
        private NativeAgentExecuteLeaseV2 _activeLeaseV2;
        private uint _selectionScanCursor;
        private uint _selectionConsumedCount;

        private NativeAgentContextOwnerV1() { }
        public AgentId AgentId { get; private set; }
        public ulong OwnerId { get; private set; }
        public uint Generation { get; private set; }
        public NativeOwnerStateV1 State => _state;

        internal static bool TryCreate(
            AgentId agentId,
            NativeProgramReadLeaseV2 programLease,
            NativeAgentContextCapacityV1 capacity,
            Allocator allocator,
            out NativeAgentContextOwnerV1 context,
            out NativeRuntimeFailureV1 failure)
            => TryCreate(agentId, programLease, capacity, allocator, -1, out context, out failure);

        private static bool TryCreate(
            AgentId agentId,
            NativeProgramReadLeaseV2 programLease,
            NativeAgentContextCapacityV1 capacity,
            Allocator allocator,
            int failAfterSuccessfulAllocations,
            out NativeAgentContextOwnerV1 context,
            out NativeRuntimeFailureV1 failure)
        {
            context = null;
            if (!agentId.IsValid || capacity.MaximumBindings == 0
                || capacity.ValueBytes > int.MaxValue || capacity.SlotVersions > int.MaxValue)
            { failure = RegistryFailure(); return false; }
            if (!TryFindDescriptor(programLease.View, out var descriptor))
            { failure = RegistryFailure(); return false; }
            if (!TryPreflightStorage(programLease.View, capacity))
            { failure = RegistryFailure(); return false; }
            var values = default(NativeArray<byte>);
            var versions = default(NativeArray<ulong>);
            var revision = default(NativeArray<ulong>);
            var eligible = default(NativeArray<TreeInstanceId>);
            var allocations = 0;
            try
            {
                values = Allocate<byte>(capacity.ValueBytes, allocator, failAfterSuccessfulAllocations, ref allocations);
                versions = Allocate<ulong>(capacity.SlotVersions, allocator, failAfterSuccessfulAllocations, ref allocations);
                revision = Allocate<ulong>(1, allocator, failAfterSuccessfulAllocations, ref allocations);
                eligible = Allocate<TreeInstanceId>(capacity.MaximumBindings, allocator, failAfterSuccessfulAllocations, ref allocations);
                for (var index = 0; index < programLease.View.Slots.Length; index++)
                {
                    var slot = programLease.View.Slots[index];
                    if (slot.Scope != BlackboardScope.Agent) continue;
                    for (var item = 0; item < slot.Size; item++)
                        values[(int)slot.Offset + item] = programLease.View.Semantic.DefaultValueBlob[(int)slot.DefaultOffset + item];
                }
                var rawOwner = Interlocked.Increment(ref s_nextOwnerId);
                if (rawOwner <= 0) throw new OverflowException();
                context = new NativeAgentContextOwnerV1
                {
                    AgentId = agentId, OwnerId = (ulong)rawOwner, Generation = 1,
                    _state = NativeOwnerStateV1.Initialized,
                    _values = values, _versions = versions, _revision = revision, _eligible = eligible,
                    _bindings = new BindingEntry[capacity.MaximumBindings],
                    _authority = programLease.View, _descriptor = descriptor,
                };
                failure = default;
                return true;
            }
            catch (Exception)
            {
                Dispose(ref eligible); Dispose(ref revision); Dispose(ref versions); Dispose(ref values);
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    NativeResourceKindV1.InstanceAgentBlackboard);
                return false;
            }
        }

        private static bool TryPreflightStorage(
            NativeProgramImageViewV2 program,
            NativeAgentContextCapacityV1 capacity)
        {
            if (capacity.SlotVersions < program.Slots.Length) return false;
            var foundAgentSlot = false;
            for (var index = 0; index < program.Slots.Length; index++)
            {
                var slot = program.Slots[index];
                if (slot.Scope != BlackboardScope.Agent) continue;
                foundAgentSlot = true;
                if ((ulong)slot.Offset + slot.Size > capacity.ValueBytes
                    || (ulong)slot.DefaultOffset + slot.DefaultSize > (uint)program.Semantic.DefaultValueBlob.Length
                    || slot.DefaultSize != slot.Size
                    || !NativeBlackboardCanonicalV1.IsCanonical(
                        program, slot, program.Semantic.DefaultValueBlob, slot.DefaultOffset))
                    return false;
            }
            return foundAgentSlot;
        }

        public bool TryBind(
            TreeInstanceId treeInstanceId,
            NativeProgramReadLeaseV2 programLease,
            NativeInstanceArenaOwnerV1 instance,
            out NativeAgentBindingV1 binding,
            out NativeRuntimeFailureV1 failure)
        {
            binding = default;
            if (_state != NativeOwnerStateV1.Initialized || HasWindow || !treeInstanceId.IsValid
                || instance == null || instance.State != NativeOwnerStateV1.Initialized
                || !Compatible(programLease.View))
            { failure = HasWindow ? LiveFailure() : RegistryFailure(); return false; }
            var free = -1;
            for (var index = 0; index < _bindings.Length; index++)
            {
                if (!_bindings[index].Active) { if (free < 0) free = index; continue; }
                if (_bindings[index].TreeInstanceId == treeInstanceId || ReferenceEquals(_bindings[index].Instance, instance))
                { failure = RegistryFailure(); return false; }
            }
            if (free < 0 || _nextBindingId == ulong.MaxValue)
            {
                failure = new NativeRuntimeFailureV1(
                    free < 0 ? NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded : NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.AgentBindings, (uint)_bindings.Length + 1, (uint)_bindings.Length);
                return false;
            }
            var id = ++_nextBindingId;
            _bindings[free] = new BindingEntry
            { Active = true, Id = id, TreeInstanceId = treeInstanceId, ProgramLease = programLease, Instance = instance };
            binding = new NativeAgentBindingV1(OwnerId, Generation, id, treeInstanceId);
            failure = default;
            return true;
        }

        public bool TryUnbind(NativeAgentBindingV1 binding, out NativeRuntimeFailureV1 failure)
        {
            if (_state != NativeOwnerStateV1.Initialized || HasWindow)
            { failure = HasWindow ? LiveFailure() : LifetimeFailure(binding.BindingId); return false; }
            var index = FindBinding(binding.TreeInstanceId);
            if (index < 0 || binding.OwnerId != OwnerId || binding.Generation != Generation
                || _bindings[index].Id != binding.BindingId || _bindings[index].Instance.State != NativeOwnerStateV1.Initialized)
            { failure = LifetimeFailure(binding.BindingId); return false; }
            _bindings[index] = default;
            failure = default;
            return true;
        }

        public bool TryBeginExecuteWindow(
            NativeArray<TreeInstanceId> eligible,
            out NativeAgentExecuteWindowV1 window,
            out NativeRuntimeFailureV1 failure)
        {
            window = default;
            if (_state != NativeOwnerStateV1.Initialized || HasWindow)
            { failure = HasWindow ? LiveFailure() : LifetimeFailure(0); return false; }
            if (!eligible.IsCreated || eligible.Length == 0 || eligible.Length > _eligible.Length)
            {
                failure = new NativeRuntimeFailureV1(
                    eligible.IsCreated && eligible.Length > _eligible.Length
                        ? NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded
                        : NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch,
                    NativeResourceKindV1.AgentExecuteWindowOwners,
                    eligible.IsCreated ? (uint)eligible.Length : 0, (uint)_eligible.Length);
                return false;
            }
            TreeInstanceId previous = default;
            for (var index = 0; index < eligible.Length; index++)
            {
                var id = eligible[index];
                var bindingIndex = FindBinding(id);
                if (!id.IsValid || index != 0 && id <= previous || bindingIndex < 0
                    || _bindings[bindingIndex].Instance.State != NativeOwnerStateV1.Initialized
                    || !Compatible(_bindings[bindingIndex].ProgramLease.View))
                { failure = RegistryFailure(NativeResourceKindV1.AgentExecuteWindowOwners); return false; }
                previous = id;
            }
            if (_nextWindowId == ulong.MaxValue)
            { failure = OverflowFailure(NativeResourceKindV1.AgentExecuteWindowOwners); return false; }
            for (var index = 0; index < eligible.Length; index++) _eligible[index] = eligible[index];
            _eligibleCount = (uint)eligible.Length;
            _cursor = 0;
            _window = new NativeAgentExecuteWindowV1(OwnerId, Generation, ++_nextWindowId, _eligibleCount);
            window = _window;
            failure = default;
            return true;
        }

        public bool TryBeginExecuteWindow(
            NativeExecuteSelectionWindowOwnerV1 selectionOwner,
            NativeExecuteSelectionWindowV1 selectionWindow,
            out NativeAgentExecuteWindowV2 window,
            out NativeRuntimeFailureV1 failure)
        {
            window = default;
            if (_state != NativeOwnerStateV1.Initialized || HasWindow)
            {
                failure = HasWindow ? LiveFailure() : LifetimeFailure(0);
                return false;
            }
            if (selectionOwner == null)
            {
                failure = RegistryFailure(NativeResourceKindV1.AgentExecuteWindowOwners);
                return false;
            }
            if (!selectionOwner.TryAcquireReadLease(selectionWindow, out var selectionLease, out failure))
                return false;

            uint eligibleCount = 0;
            var valid = true;
            for (var index = 0; index < selectionLease.View.Count; index++)
            {
                var id = selectionLease.View.Entries[index].TreeInstanceId;
                var bindingIndex = FindBinding(id);
                if (bindingIndex < 0) continue;
                if (_bindings[bindingIndex].Instance.State != NativeOwnerStateV1.Initialized
                    || !Compatible(_bindings[bindingIndex].ProgramLease.View))
                { valid = false; break; }
                eligibleCount++;
            }
            if (!valid || eligibleCount == 0 || eligibleCount > _eligible.Length)
            {
                selectionOwner.TryReleaseReadLease(selectionLease, out _);
                failure = !valid || eligibleCount == 0
                    ? RegistryFailure(NativeResourceKindV1.AgentExecuteWindowOwners)
                    : new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                        NativeResourceKindV1.AgentExecuteWindowOwners,
                        eligibleCount,
                        (uint)_eligible.Length);
                return false;
            }
            if (_nextWindowId == ulong.MaxValue)
            {
                selectionOwner.TryReleaseReadLease(selectionLease, out _);
                failure = OverflowFailure(NativeResourceKindV1.AgentExecuteWindowOwners);
                return false;
            }

            _selectionLease = selectionLease;
            _selectionScanCursor = 0;
            _selectionConsumedCount = 0;
            _windowV2 = new NativeAgentExecuteWindowV2(
                OwnerId, Generation, ++_nextWindowId, eligibleCount,
                selectionWindow.OwnerId, selectionWindow.Generation, selectionWindow.WindowId);
            window = _windowV2;
            failure = default;
            return true;
        }

        public bool TryAcquireNext(
            NativeAgentExecuteWindowV1 window,
            out NativeAgentExecuteLeaseV1 lease,
            out NativeRuntimeFailureV1 failure)
        {
            lease = default;
            if (!IsWindow(window) || _activeLease.IsValid || _cursor >= _eligibleCount)
            { failure = _activeLease.IsValid ? LiveFailure() : LifetimeFailure(window.WindowId); return false; }
            var id = _eligible[(int)_cursor];
            var bindingIndex = FindBinding(id);
            if (bindingIndex < 0 || _bindings[bindingIndex].Instance.State != NativeOwnerStateV1.Initialized
                || !Compatible(_bindings[bindingIndex].ProgramLease.View))
            { failure = RegistryFailure(NativeResourceKindV1.AgentExecuteWindowOwners); return false; }
            if (_nextLeaseId == ulong.MaxValue)
            { failure = OverflowFailure(NativeResourceKindV1.LeaseCounter); return false; }
            if (!_bindings[bindingIndex].Instance.TryAcquireExecutionLeaseV2(
                _bindings[bindingIndex].ProgramLease, out var treeLease, out failure)) return false;
            var token = new NativeLeaseTokenV1(OwnerId, Generation, ++_nextLeaseId);
            _activeLease = new NativeAgentExecuteLeaseV1(
                this, token, _window, id, treeLease,
                new NativeAgentContextViewV1(_values, _versions, _revision));
            lease = _activeLease;
            return true;
        }

        public bool TryAcquireNext(
            NativeAgentExecuteWindowV2 window,
            out NativeAgentExecuteLeaseV2 lease,
            out NativeRuntimeFailureV1 failure)
        {
            lease = default;
            if (!IsWindow(window) || HasActiveLease || _selectionConsumedCount >= window.Count)
            {
                failure = HasActiveLease ? LiveFailure() : LifetimeFailure(window.WindowId);
                return false;
            }
            if (!TryFindNextSelectionBinding(_selectionScanCursor, out var selectionIndex, out var bindingIndex))
            {
                failure = RegistryFailure(NativeResourceKindV1.AgentExecuteWindowOwners);
                return false;
            }
            var id = _selectionLease.View.Entries[(int)selectionIndex].TreeInstanceId;
            if (_bindings[bindingIndex].Instance.State != NativeOwnerStateV1.Initialized
                || !Compatible(_bindings[bindingIndex].ProgramLease.View))
            {
                failure = RegistryFailure(NativeResourceKindV1.AgentExecuteWindowOwners);
                return false;
            }
            if (_nextLeaseId == ulong.MaxValue)
            {
                failure = OverflowFailure(NativeResourceKindV1.LeaseCounter);
                return false;
            }
            if (!_bindings[bindingIndex].Instance.TryAcquireExecutionLeaseV2(
                _bindings[bindingIndex].ProgramLease, out var treeLease, out failure)) return false;
            var token = new NativeLeaseTokenV1(OwnerId, Generation, ++_nextLeaseId);
            _selectionScanCursor = selectionIndex + 1;
            _activeLeaseV2 = new NativeAgentExecuteLeaseV2(
                this, token, _windowV2, id, treeLease,
                new NativeAgentContextViewV1(_values, _versions, _revision));
            lease = _activeLeaseV2;
            return true;
        }

        public bool TryRegisterDependency(
            NativeAgentExecuteLeaseV1 lease,
            JobHandle dependency,
            out NativeRuntimeFailureV1 failure)
        {
            if (!IsActive(lease)) { failure = LifetimeFailure(lease.Token.LeaseId); return false; }
            return lease.TreeLease.SemanticLease.Owner.TryRegisterDependency(lease.TreeLease, dependency, out failure);
        }

        public bool TryRegisterDependency(
            NativeAgentExecuteLeaseV2 lease,
            JobHandle dependency,
            out NativeRuntimeFailureV1 failure)
        {
            if (!IsActive(lease)) { failure = LifetimeFailure(lease.Token.LeaseId); return false; }
            return lease.TreeLease.SemanticLease.Owner.TryRegisterDependency(lease.TreeLease, dependency, out failure);
        }

        public bool TryReleaseExecuteLease(NativeAgentExecuteLeaseV1 lease, out NativeRuntimeFailureV1 failure)
        {
            if (!IsActive(lease)) { failure = LifetimeFailure(lease.Token.LeaseId); return false; }
            if (!lease.TreeLease.SemanticLease.Owner.TryReleaseExecutionLease(lease.TreeLease, out failure)) return false;
            _activeLease = default;
            _cursor++;
            return true;
        }

        public bool TryReleaseExecuteLease(NativeAgentExecuteLeaseV2 lease, out NativeRuntimeFailureV1 failure)
        {
            if (!IsActive(lease)) { failure = LifetimeFailure(lease.Token.LeaseId); return false; }
            if (!lease.TreeLease.SemanticLease.Owner.TryReleaseExecutionLease(lease.TreeLease, out failure)) return false;
            _activeLeaseV2 = default;
            _selectionConsumedCount++;
            return true;
        }

        public bool TryEndExecuteWindow(NativeAgentExecuteWindowV1 window, out NativeRuntimeFailureV1 failure)
        {
            if (!IsWindow(window) || _activeLease.IsValid || _cursor != _eligibleCount)
            { failure = _activeLease.IsValid ? LiveFailure() : LifetimeFailure(window.WindowId); return false; }
            ClearWindow();
            failure = default;
            return true;
        }

        public bool TryEndExecuteWindow(NativeAgentExecuteWindowV2 window, out NativeRuntimeFailureV1 failure)
        {
            if (!IsWindow(window) || HasActiveLease || _selectionConsumedCount != window.Count)
            {
                failure = HasActiveLease ? LiveFailure() : LifetimeFailure(window.WindowId);
                return false;
            }
            return TryCloseSelectionWindow(out failure);
        }

        public bool TryCancelNext(
            NativeAgentExecuteWindowV1 window,
            TreeInstanceId expectedTreeInstanceId,
            out NativeRuntimeFailureV1 failure)
        {
            if (!IsWindow(window) || _activeLease.IsValid || _cursor >= _eligibleCount
                || !expectedTreeInstanceId.IsValid || _eligible[(int)_cursor] != expectedTreeInstanceId)
            { failure = _activeLease.IsValid ? LiveFailure() : LifetimeFailure(window.WindowId); return false; }
            _cursor++;
            failure = default;
            return true;
        }

        public bool TryCancelNext(
            NativeAgentExecuteWindowV2 window,
            TreeInstanceId expectedTreeInstanceId,
            out NativeRuntimeFailureV1 failure)
        {
            if (!IsWindow(window) || HasActiveLease || _selectionConsumedCount >= window.Count
                || !expectedTreeInstanceId.IsValid
                || !TryFindNextSelectionBinding(_selectionScanCursor, out var selectionIndex, out _)
                || _selectionLease.View.Entries[(int)selectionIndex].TreeInstanceId != expectedTreeInstanceId)
            {
                failure = HasActiveLease ? LiveFailure() : LifetimeFailure(window.WindowId);
                return false;
            }
            _selectionScanCursor = selectionIndex + 1;
            _selectionConsumedCount++;
            failure = default;
            return true;
        }

        public bool TryAbortExecuteWindow(NativeAgentExecuteWindowV1 window, out NativeRuntimeFailureV1 failure)
        {
            if (!IsWindow(window) || _activeLease.IsValid)
            { failure = _activeLease.IsValid ? LiveFailure() : LifetimeFailure(window.WindowId); return false; }
            ClearWindow();
            failure = default;
            return true;
        }

        public bool TryAbortExecuteWindow(NativeAgentExecuteWindowV2 window, out NativeRuntimeFailureV1 failure)
        {
            if (!IsWindow(window) || HasActiveLease)
            {
                failure = HasActiveLease ? LiveFailure() : LifetimeFailure(window.WindowId);
                return false;
            }
            return TryCloseSelectionWindow(out failure);
        }

        public bool TryReset(out NativeRuntimeFailureV1 failure)
        {
            if (_state != NativeOwnerStateV1.Initialized || HasWindow)
            { failure = HasWindow ? LiveFailure() : LifetimeFailure(0); return false; }
            var changed = false;
            for (var index = 0; index < _authority.Slots.Length; index++)
            {
                var slot = _authority.Slots[index];
                if (slot.Scope != BlackboardScope.Agent) continue;
                if (!NativeBlackboardCanonicalV1.IsCanonical(
                    _authority, slot, _authority.Semantic.DefaultValueBlob, slot.DefaultOffset))
                { failure = new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.BlackboardInvalidValue, NativeResourceKindV1.InstanceAgentBlackboard); return false; }
                if (!DefaultEquals(slot))
                {
                    if (_versions[index] == ulong.MaxValue || _revision[0] == ulong.MaxValue)
                    { failure = OverflowFailure(NativeResourceKindV1.InstanceAgentSlotVersions); return false; }
                    changed = true;
                }
            }
            if (!changed) { failure = default; return true; }
            for (var index = 0; index < _authority.Slots.Length; index++)
            {
                var slot = _authority.Slots[index];
                if (slot.Scope != BlackboardScope.Agent || DefaultEquals(slot)) continue;
                for (var item = 0; item < slot.Size; item++)
                    _values[(int)slot.Offset + item] = _authority.Semantic.DefaultValueBlob[(int)slot.DefaultOffset + item];
                _versions[index]++;
            }
            _revision[0]++;
            failure = default;
            return true;
        }

        public bool TryDispose(out NativeRuntimeFailureV1 failure)
        {
            if (_state != NativeOwnerStateV1.Initialized || HasWindow)
            { failure = HasWindow ? LiveFailure() : LifetimeFailure(0); return false; }
            for (var index = 0; index < _bindings.Length; index++)
                if (_bindings[index].Active) { failure = LiveFailure(); return false; }
            Dispose(ref _eligible); Dispose(ref _revision); Dispose(ref _versions); Dispose(ref _values);
            _bindings = null;
            _state = NativeOwnerStateV1.Disposed;
            failure = default;
            return true;
        }

        private bool Compatible(NativeProgramImageViewV2 candidate)
        {
            if (!TryFindDescriptor(candidate, out var descriptor)
                || descriptor.ContractId != _descriptor.ContractId
                || descriptor.ContractVersion != _descriptor.ContractVersion
                || descriptor.SchemaHash != _descriptor.SchemaHash
                || descriptor.LayoutHash != _descriptor.LayoutHash
                || descriptor.RawLayoutLength != _descriptor.RawLayoutLength)
                return false;
            for (var index = 0; index < descriptor.RawLayoutLength; index++)
                if (candidate.ScopeLayoutBytes[(int)descriptor.RawLayoutOffset + index]
                    != _authority.ScopeLayoutBytes[(int)_descriptor.RawLayoutOffset + index]) return false;
            for (var index = 0; index < candidate.Accesses.Length; index++)
            {
                var access = candidate.Accesses[index];
                if (access.Scope != BlackboardScope.Agent) continue;
                if (access.ResolvedSlotIndex >= candidate.Slots.Length) return false;
                var slot = candidate.Slots[(int)access.ResolvedSlotIndex];
                if (access.ScopeSlotIndex != slot.ScopeSlotIndex || access.TypeId != slot.TypeId
                    || access.TypeVersion != slot.TypeVersion || access.EnumContractId != slot.EnumContractId
                    || access.RegisteredTypeIndex != slot.RegisteredTypeIndex) return false;
                if (!AuthorityHasSlot(slot)) return false;
            }
            return true;
        }

        private bool AuthorityHasSlot(NativeBlackboardSlotBindingV2 candidate)
        {
            for (var index = 0; index < _authority.Slots.Length; index++)
            {
                var slot = _authority.Slots[index];
                if (slot.Scope == BlackboardScope.Agent && slot.ScopeSlotIndex == candidate.ScopeSlotIndex)
                    return slot.TypeId == candidate.TypeId && slot.TypeVersion == candidate.TypeVersion
                        && slot.EnumContractId == candidate.EnumContractId
                        && slot.RegisteredTypeIndex == candidate.RegisteredTypeIndex
                        && slot.Offset == candidate.Offset && slot.Size == candidate.Size && slot.Alignment == candidate.Alignment;
            }
            return false;
        }

        private static bool TryFindDescriptor(NativeProgramImageViewV2 program, out NativeBlackboardScopeRecordV2 descriptor)
        {
            for (var index = 0; index < program.Scopes.Length; index++)
                if (program.Scopes[index].Scope == BlackboardScope.Agent)
                { descriptor = program.Scopes[index]; return true; }
            descriptor = default;
            return false;
        }

        private int FindBinding(TreeInstanceId id)
        {
            for (var index = 0; index < _bindings.Length; index++)
                if (_bindings[index].Active && _bindings[index].TreeInstanceId == id) return index;
            return -1;
        }

        private bool TryFindNextSelectionBinding(
            uint start,
            out uint selectionIndex,
            out int bindingIndex)
        {
            selectionIndex = 0;
            bindingIndex = -1;
            if (!_selectionLease.IsValid
                || !_selectionLease.Owner.IsLeaseActive(_selectionLease)) return false;
            for (var index = start; index < _selectionLease.View.Count; index++)
            {
                var candidate = FindBinding(_selectionLease.View.Entries[(int)index].TreeInstanceId);
                if (candidate < 0) continue;
                selectionIndex = index;
                bindingIndex = candidate;
                return true;
            }
            return false;
        }

        private bool IsWindow(NativeAgentExecuteWindowV1 value)
            => _window.IsValid && value.OwnerId == OwnerId && value.Generation == Generation
                && value.WindowId == _window.WindowId && value.Count == _window.Count;

        private bool IsWindow(NativeAgentExecuteWindowV2 value)
            => _windowV2.IsValid && value.OwnerId == OwnerId && value.Generation == Generation
                && value.WindowId == _windowV2.WindowId && value.Count == _windowV2.Count
                && value.SelectionOwnerId == _windowV2.SelectionOwnerId
                && value.SelectionGeneration == _windowV2.SelectionGeneration
                && value.SelectionWindowId == _windowV2.SelectionWindowId
                && _selectionLease.IsValid
                && _selectionLease.Owner.IsLeaseActive(_selectionLease);

        private bool IsActive(NativeAgentExecuteLeaseV1 value)
            => _activeLease.IsValid && ReferenceEquals(value.Owner, this)
                && value.Token == _activeLease.Token && IsWindow(value.Window)
                && value.TreeInstanceId == _activeLease.TreeInstanceId;

        private bool IsActive(NativeAgentExecuteLeaseV2 value)
            => _activeLeaseV2.IsValid && ReferenceEquals(value.Owner, this)
                && value.Token == _activeLeaseV2.Token && IsWindow(value.Window)
                && value.TreeInstanceId == _activeLeaseV2.TreeInstanceId;

        internal bool IsLeaseActive(NativeAgentExecuteLeaseV1 value) => IsActive(value);
        internal bool IsLeaseActive(NativeAgentExecuteLeaseV2 value) => IsActive(value);

        private bool HasWindow => _window.IsValid || _windowV2.IsValid;
        private bool HasActiveLease => _activeLease.IsValid || _activeLeaseV2.IsValid;

        private bool DefaultEquals(NativeBlackboardSlotBindingV2 slot)
        {
            for (var index = 0; index < slot.Size; index++)
                if (_values[(int)slot.Offset + index]
                    != _authority.Semantic.DefaultValueBlob[(int)slot.DefaultOffset + index]) return false;
            return true;
        }

        private void ClearWindow()
        {
            for (var index = 0; index < _eligibleCount; index++) _eligible[index] = default;
            _eligibleCount = 0; _cursor = 0; _window = default;
        }

        private bool TryCloseSelectionWindow(out NativeRuntimeFailureV1 failure)
        {
            var selectionOwner = _selectionLease.Owner;
            var selectionLease = _selectionLease;
            if (selectionOwner == null)
            {
                failure = LifetimeFailure(selectionLease.LeaseId);
                return false;
            }
            if (!selectionOwner.TryReleaseReadLease(selectionLease, out failure)) return false;
            _selectionLease = default;
            _selectionScanCursor = 0;
            _selectionConsumedCount = 0;
            _windowV2 = default;
            failure = default;
            return true;
        }

        private NativeRuntimeFailureV1 LifetimeFailure(ulong leaseId)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                NativeResourceKindV1.AgentExecuteWindowOwners,
                ownerId: OwnerId, generation: Generation, leaseId: leaseId);
        private NativeRuntimeFailureV1 LiveFailure()
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation,
                NativeResourceKindV1.AgentExecuteWindowOwners,
                ownerId: OwnerId, generation: Generation);
        private static NativeRuntimeFailureV1 RegistryFailure(NativeResourceKindV1 resource = NativeResourceKindV1.AgentBindings)
            => new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch, resource);
        private NativeRuntimeFailureV1 OverflowFailure(NativeResourceKindV1 resource)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow, resource,
                ownerId: OwnerId, generation: Generation);

        private static void Dispose<T>(ref NativeArray<T> value) where T : struct
        { if (!value.IsCreated) return; value.Dispose(); value = default; }

        private static NativeArray<T> Allocate<T>(
            uint count,
            Allocator allocator,
            int failAfterSuccessfulAllocations,
            ref int allocations)
            where T : struct
        {
            if (failAfterSuccessfulAllocations >= 0 && allocations >= failAfterSuccessfulAllocations)
                throw new InvalidOperationException("Injected native Agent context allocation failure.");
            var value = new NativeArray<T>((int)count, allocator, NativeArrayOptions.ClearMemory);
            allocations++;
            return value;
        }
    }

    public static class NativeAgentBlackboardV1
    {
        public static BurstContextResult TryRead<T>(
            NativeAgentExecuteLeaseV2 lease,
            uint nodeIndex,
            uint accessOrdinal,
            NativeBlackboardTypeIdV2 expectedType,
            out T value,
            out ulong version)
            where T : unmanaged
        {
            value = default;
            version = 0;
            if (!lease.IsValid || !lease.Owner.IsLeaseActive(lease)) return BurstContextResult.PhaseViolation;
            return TryRead(lease.View, nodeIndex, accessOrdinal, expectedType, out value, out version);
        }

        public static BurstContextResult TryRead<T>(
            NativeAgentExecuteLeaseV1 lease,
            uint nodeIndex,
            uint accessOrdinal,
            NativeBlackboardTypeIdV2 expectedType,
            out T value,
            out ulong version)
            where T : unmanaged
        {
            value = default;
            version = 0;
            if (!lease.IsValid || !lease.Owner.IsLeaseActive(lease)) return BurstContextResult.PhaseViolation;
            return TryRead(lease.View, nodeIndex, accessOrdinal, expectedType, out value, out version);
        }

        public static BurstContextResult TryRead<T>(
            NativeAgentExecutionViewV1 view,
            uint nodeIndex,
            uint accessOrdinal,
            NativeBlackboardTypeIdV2 expectedType,
            out T value,
            out ulong version)
            where T : unmanaged
        {
            value = default;
            version = 0;
            if (!view.IsValid) return BurstContextResult.PhaseViolation;
            var program = view.Program;
            var validation = NativeTreeBlackboardV1.TryResolve(
                program, nodeIndex, accessOrdinal, expectedType, BlackboardScope.Agent, false,
                out var access, out var slot);
            if (validation != BurstContextResult.Success) return validation;
            if (UnsafeUtility.SizeOf<T>() != slot.Size || UnsafeUtility.AlignOf<T>() != slot.Alignment)
                return BurstContextResult.TypeMismatch;
            if ((ulong)slot.Offset + slot.Size > (uint)view.Context.Values.Length
                || access.ResolvedSlotIndex >= view.Context.SlotVersions.Length)
                return BurstContextResult.InvalidHandle;
            value = new NativeSlice<byte>(view.Context.Values, (int)slot.Offset, (int)slot.Size).SliceConvert<T>()[0];
            version = view.Context.SlotVersions[(int)access.ResolvedSlotIndex];
            return BurstContextResult.Success;
        }

        public static BurstContextResult TryWrite<T>(
            NativeAgentExecuteLeaseV2 lease,
            uint nodeIndex,
            uint accessOrdinal,
            NativeBlackboardTypeIdV2 expectedType,
            NativeArray<T> candidate,
            out bool changed)
            where T : unmanaged
        {
            changed = false;
            if (!lease.IsValid || !lease.Owner.IsLeaseActive(lease)) return BurstContextResult.PhaseViolation;
            return TryWrite(lease.View, nodeIndex, accessOrdinal, expectedType, candidate, out changed);
        }

        public static BurstContextResult TryWrite<T>(
            NativeAgentExecuteLeaseV1 lease,
            uint nodeIndex,
            uint accessOrdinal,
            NativeBlackboardTypeIdV2 expectedType,
            NativeArray<T> candidate,
            out bool changed)
            where T : unmanaged
        {
            changed = false;
            if (!lease.IsValid || !lease.Owner.IsLeaseActive(lease)) return BurstContextResult.PhaseViolation;
            return TryWrite(lease.View, nodeIndex, accessOrdinal, expectedType, candidate, out changed);
        }

        public static BurstContextResult TryWrite<T>(
            NativeAgentExecutionViewV1 view,
            uint nodeIndex,
            uint accessOrdinal,
            NativeBlackboardTypeIdV2 expectedType,
            NativeArray<T> candidate,
            out bool changed)
            where T : unmanaged
        {
            changed = false;
            if (!view.IsValid) return BurstContextResult.PhaseViolation;
            var program = view.Program;
            var validation = NativeTreeBlackboardV1.TryResolve(
                program, nodeIndex, accessOrdinal, expectedType, BlackboardScope.Agent, true,
                out var access, out var slot);
            if (validation != BurstContextResult.Success) return validation;
            if (!candidate.IsCreated || candidate.Length != 1 || UnsafeUtility.SizeOf<T>() != slot.Size
                || UnsafeUtility.AlignOf<T>() != slot.Alignment)
                return BurstContextResult.TypeMismatch;
            if ((ulong)slot.Offset + slot.Size > (uint)view.Context.Values.Length
                || access.ResolvedSlotIndex >= view.Context.SlotVersions.Length || view.Context.Revision.Length != 1)
                return BurstContextResult.InvalidHandle;
            var bytes = candidate.Reinterpret<byte>(UnsafeUtility.SizeOf<T>());
            if (!NativeBlackboardCanonicalV1.IsCanonical(program, slot, bytes.AsReadOnly()))
                return BurstContextResult.InvalidEncoding;
            if (NativeBlackboardCanonicalV1.EqualsCanonical(program, slot, view.Context.Values, bytes.AsReadOnly()))
                return BurstContextResult.Success;
            if (view.Context.SlotVersions[(int)access.ResolvedSlotIndex] == ulong.MaxValue
                || view.Context.Revision[0] == ulong.MaxValue)
                return BurstContextResult.Overflow;
            NativeBlackboardCanonicalV1.CopyCanonical(program, slot, bytes.AsReadOnly(), view.Context.Values);
            var versions = view.Context.SlotVersions;
            versions[(int)access.ResolvedSlotIndex]++;
            var revision = view.Context.Revision;
            revision[0]++;
            changed = true;
            return BurstContextResult.Success;
        }
    }
}
