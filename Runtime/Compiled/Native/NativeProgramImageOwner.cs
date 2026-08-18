using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT
{
    public sealed class NativeProgramImageOwnerV1
    {
        private readonly Dictionary<ulong, JobHandle> _leases = new Dictionary<ulong, JobHandle>();
        private ReadOnlyCollection<CompiledDebugMapEntry> _hostDebugMap;
        private NativeCompiledProgramHeaderV1 _header;
        private NativeArray<NativeCompiledNodeRecordV1> _nodes;
        private NativeArray<uint> _childIndices;
        private NativeArray<uint> _readSlotIndices;
        private NativeArray<uint> _writeSlotIndices;
        private NativeArray<NativeCompiledBlackboardSlotRecordV1> _blackboardSlots;
        private NativeArray<NativeCompiledObserverRecordV1> _observers;
        private NativeArray<uint> _watchedSlotIndices;
        private NativeArray<byte> _configBlob;
        private NativeArray<byte> _defaultValueBlob;
        private NativeArray<uint> _debugRuntimeNodeIndices;
        private NativeCompiledProgramHeaderV1 _headerV2;
        private NativeArray<NativeBlackboardScopeRecordV2> _scopeDescriptorsV2;
        private NativeArray<byte> _scopeLayoutBytesV2;
        private NativeArray<NativeBlackboardSlotBindingV2> _blackboardSlotsV2;
        private NativeArray<NativeBlackboardAccessRecordV2> _blackboardAccessesV2;
        private NativeArray<NativeNodeBlackboardAccessRangeV2> _nodeAccessRangesV2;
        private NativeArray<NativeRegisteredBlackboardTypeRecordV2> _registeredTypesV2;
        private NativeArray<NativeRegisteredBlackboardFieldRecordV2> _registeredFieldsV2;
        private ulong _nextLeaseId;

        private NativeProgramImageOwnerV1()
        {
        }

        public ulong OwnerId { get; private set; }
        public uint Generation { get; private set; }
        public NativeOwnerStateV1 State { get; private set; }
        public int ActiveReaderCount => _leases.Count;
        public IReadOnlyList<CompiledDebugMapEntry> HostDebugMap => _hostDebugMap;
        public bool HasBlackboardV2 => _blackboardSlotsV2.IsCreated;

        public static bool TryCreate(
            CompiledProgram program,
            NativeProgramImageCapacityV1 capacity,
            Allocator allocator,
            out NativeProgramImageOwnerV1 owner,
            out NativeRuntimeFailureV1 failure)
            => TryCreate(program, capacity, allocator, -1, out owner, out failure);

        internal static bool TryCreate(
            CompiledProgram program,
            NativeProgramImageCapacityV1 capacity,
            Allocator allocator,
            int failAfterSuccessfulAllocations,
            out NativeProgramImageOwnerV1 owner,
            out NativeRuntimeFailureV1 failure)
        {
            owner = null;
            if (allocator != Allocator.Persistent)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeAllocatorInvalid,
                    NativeResourceKindV1.None,
                    (ulong)allocator,
                    (ulong)Allocator.Persistent);
                return false;
            }

            if (!TryPreflight(program, capacity, out failure))
            {
                return false;
            }

            var nodes = default(NativeArray<NativeCompiledNodeRecordV1>);
            var childIndices = default(NativeArray<uint>);
            var readSlotIndices = default(NativeArray<uint>);
            var writeSlotIndices = default(NativeArray<uint>);
            var blackboardSlots = default(NativeArray<NativeCompiledBlackboardSlotRecordV1>);
            var observers = default(NativeArray<NativeCompiledObserverRecordV1>);
            var watchedSlotIndices = default(NativeArray<uint>);
            var configBlob = default(NativeArray<byte>);
            var defaultValueBlob = default(NativeArray<byte>);
            var debugRuntimeNodeIndices = default(NativeArray<uint>);
            var allocations = 0;
            var currentResource = NativeResourceKindV1.ProgramNodes;
            try
            {
                nodes = Allocate<NativeCompiledNodeRecordV1>(program.Nodes.Count, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.ProgramChildIndices;
                childIndices = Allocate<uint>(program.ChildIndices.Count, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.ProgramReadSlotIndices;
                readSlotIndices = Allocate<uint>(program.ReadSlotIndices.Count, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.ProgramWriteSlotIndices;
                writeSlotIndices = Allocate<uint>(program.WriteSlotIndices.Count, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.ProgramBlackboardSlots;
                blackboardSlots = Allocate<NativeCompiledBlackboardSlotRecordV1>(program.BlackboardSlots.Count, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.ProgramObservers;
                observers = Allocate<NativeCompiledObserverRecordV1>(program.Observers.Count, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.ProgramWatchedSlotIndices;
                watchedSlotIndices = Allocate<uint>(program.WatchedSlotIndices.Count, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.ProgramConfigBytes;
                configBlob = Allocate<byte>(program.ConfigBlob.Count, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.ProgramDefaultBytes;
                defaultValueBlob = Allocate<byte>(program.DefaultValueBlob.Count, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.ProgramDebugOrdinals;
                debugRuntimeNodeIndices = Allocate<uint>(program.DebugMap.Count, allocator, failAfterSuccessfulAllocations, ref allocations);

                Copy(program, nodes, childIndices, readSlotIndices, writeSlotIndices, blackboardSlots, observers,
                    watchedSlotIndices, configBlob, defaultValueBlob, debugRuntimeNodeIndices);

                var hostDebugMap = new CompiledDebugMapEntry[program.DebugMap.Count];
                for (var index = 0; index < hostDebugMap.Length; index++)
                {
                    hostDebugMap[index] = program.DebugMap[index];
                }

                if (!NativeOwnerIdentityV1.TryNext(out var ownerId))
                {
                    throw new OverflowException("The native owner ID counter overflowed.");
                }

                owner = new NativeProgramImageOwnerV1
                {
                    OwnerId = ownerId,
                    Generation = 1,
                    State = NativeOwnerStateV1.Initialized,
                    _header = new NativeCompiledProgramHeaderV1(program.Header),
                    _nodes = nodes,
                    _childIndices = childIndices,
                    _readSlotIndices = readSlotIndices,
                    _writeSlotIndices = writeSlotIndices,
                    _blackboardSlots = blackboardSlots,
                    _observers = observers,
                    _watchedSlotIndices = watchedSlotIndices,
                    _configBlob = configBlob,
                    _defaultValueBlob = defaultValueBlob,
                    _debugRuntimeNodeIndices = debugRuntimeNodeIndices,
                    _hostDebugMap = Array.AsReadOnly(hostDebugMap),
                };
                failure = default;
                return true;
            }
            catch (OverflowException)
            {
                DisposeReverse(ref debugRuntimeNodeIndices, ref defaultValueBlob, ref configBlob, ref watchedSlotIndices,
                    ref observers, ref blackboardSlots, ref writeSlotIndices, ref readSlotIndices, ref childIndices, ref nodes);
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.LeaseCounter);
                return false;
            }
            catch (Exception)
            {
                DisposeReverse(ref debugRuntimeNodeIndices, ref defaultValueBlob, ref configBlob, ref watchedSlotIndices,
                    ref observers, ref blackboardSlots, ref writeSlotIndices, ref readSlotIndices, ref childIndices, ref nodes);
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeProgramCapacityExceeded,
                    currentResource,
                    (ulong)allocations + 1,
                    (ulong)allocations);
                return false;
            }
        }

        public static bool TryCreateV2(
            NativeProgramBlackboardBindingV2 binding,
            NativeProgramImageCapacityV2 capacity,
            Allocator allocator,
            out NativeProgramImageOwnerV1 owner,
            out NativeRuntimeFailureV1 failure)
            => TryCreateV2(binding, capacity, allocator, -1, out owner, out failure);

        internal static bool TryCreateV2(
            NativeProgramBlackboardBindingV2 binding,
            NativeProgramImageCapacityV2 capacity,
            Allocator allocator,
            int failAfterSuccessfulV2Allocations,
            out NativeProgramImageOwnerV1 owner,
            out NativeRuntimeFailureV1 failure)
        {
            owner = null;
            if (!TryPreflightV2(binding, capacity, out failure)) return false;
            if (!TryCreate(binding.SemanticProgram, capacity.Semantic, allocator, out owner, out failure)) return false;

            var scopes = default(NativeArray<NativeBlackboardScopeRecordV2>);
            var layouts = default(NativeArray<byte>);
            var slots = default(NativeArray<NativeBlackboardSlotBindingV2>);
            var accesses = default(NativeArray<NativeBlackboardAccessRecordV2>);
            var ranges = default(NativeArray<NativeNodeBlackboardAccessRangeV2>);
            var types = default(NativeArray<NativeRegisteredBlackboardTypeRecordV2>);
            var fields = default(NativeArray<NativeRegisteredBlackboardFieldRecordV2>);
            var allocations = 0;
            var resource = NativeResourceKindV1.ProgramScopeDescriptors;
            try
            {
                scopes = Allocate<NativeBlackboardScopeRecordV2>(binding.Scopes.Count, allocator, failAfterSuccessfulV2Allocations, ref allocations);
                resource = NativeResourceKindV1.ProgramScopeLayoutBytes;
                var layoutLength = 0;
                for (var index = 0; index < binding.Scopes.Count; index++) layoutLength = checked(layoutLength + binding.Scopes[index].GetRawLayoutCopy().Length);
                layouts = Allocate<byte>(layoutLength, allocator, failAfterSuccessfulV2Allocations, ref allocations);
                resource = NativeResourceKindV1.ProgramBlackboardSlots;
                slots = Allocate<NativeBlackboardSlotBindingV2>(binding.Slots.Count, allocator, failAfterSuccessfulV2Allocations, ref allocations);
                resource = NativeResourceKindV1.ProgramBlackboardAccesses;
                accesses = Allocate<NativeBlackboardAccessRecordV2>(binding.Accesses.Count, allocator, failAfterSuccessfulV2Allocations, ref allocations);
                resource = NativeResourceKindV1.ProgramNodeAccessRanges;
                ranges = Allocate<NativeNodeBlackboardAccessRangeV2>(binding.SemanticProgram.Nodes.Count, allocator, failAfterSuccessfulV2Allocations, ref allocations);
                resource = NativeResourceKindV1.ProgramRegisteredTypes;
                types = Allocate<NativeRegisteredBlackboardTypeRecordV2>(binding.RegisteredTypes.Count, allocator, failAfterSuccessfulV2Allocations, ref allocations);
                resource = NativeResourceKindV1.ProgramRegisteredFields;
                fields = Allocate<NativeRegisteredBlackboardFieldRecordV2>(binding.RegisteredFields.Count, allocator, failAfterSuccessfulV2Allocations, ref allocations);

                CopyV2(binding, scopes, layouts, slots, accesses, ranges, types, fields);
                owner._headerV2 = binding.CreateHeaderProjection();
                owner._scopeDescriptorsV2 = scopes;
                owner._scopeLayoutBytesV2 = layouts;
                owner._blackboardSlotsV2 = slots;
                owner._blackboardAccessesV2 = accesses;
                owner._nodeAccessRangesV2 = ranges;
                owner._registeredTypesV2 = types;
                owner._registeredFieldsV2 = fields;
                failure = default;
                return true;
            }
            catch (Exception)
            {
                DisposeV2(ref fields, ref types, ref ranges, ref accesses, ref slots, ref layouts, ref scopes);
                owner.TryDispose(out _);
                owner = null;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeProgramCapacityExceeded,
                    resource,
                    (ulong)allocations + 1,
                    (ulong)allocations);
                return false;
            }
        }

        public bool TryAcquireReadLease(out NativeProgramReadLeaseV1 lease, out NativeRuntimeFailureV1 failure)
        {
            if (State != NativeOwnerStateV1.Initialized)
            {
                lease = default;
                failure = LifetimeFailure(0);
                return false;
            }

            if (_nextLeaseId == ulong.MaxValue || _leases.Count == int.MaxValue)
            {
                lease = default;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeResourceKindV1.LeaseCounter,
                    _nextLeaseId,
                    ulong.MaxValue,
                    ownerId: OwnerId,
                    generation: Generation);
                return false;
            }

            var leaseId = ++_nextLeaseId;
            var token = new NativeLeaseTokenV1(OwnerId, Generation, leaseId);
            _leases.Add(leaseId, default);
            lease = new NativeProgramReadLeaseV1(this, token, CreateView());
            failure = default;
            return true;
        }

        public bool TryAcquireReadLeaseV2(out NativeProgramReadLeaseV2 lease, out NativeRuntimeFailureV1 failure)
        {
            if (!HasBlackboardV2)
            {
                lease = default;
                failure = LifetimeFailure(0);
                return false;
            }

            if (!TryAcquireReadLease(out var semanticLease, out failure))
            {
                lease = default;
                return false;
            }

            lease = new NativeProgramReadLeaseV2(semanticLease, CreateViewV2());
            return true;
        }

        public bool TryRegisterDependency(
            NativeProgramReadLeaseV1 lease,
            JobHandle dependency,
            out NativeRuntimeFailureV1 failure)
        {
            if (!IsLeaseActive(lease))
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }

            _leases[lease.Token.LeaseId] = JobHandle.CombineDependencies(
                _leases[lease.Token.LeaseId],
                dependency);
            failure = default;
            return true;
        }

        public bool TryRegisterDependency(
            NativeProgramReadLeaseV2 lease,
            JobHandle dependency,
            out NativeRuntimeFailureV1 failure)
            => TryRegisterDependency(lease.SemanticLease, dependency, out failure);

        public bool TryReleaseReadLease(NativeProgramReadLeaseV1 lease, out NativeRuntimeFailureV1 failure)
        {
            if (!IsLeaseActive(lease))
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }

            var dependency = _leases[lease.Token.LeaseId];
            if (!dependency.IsCompleted)
            {
                failure = LiveJobFailure(lease.Token.LeaseId);
                return false;
            }

            _leases.Remove(lease.Token.LeaseId);
            failure = default;
            return true;
        }

        public bool TryReleaseReadLease(NativeProgramReadLeaseV2 lease, out NativeRuntimeFailureV1 failure)
            => TryReleaseReadLease(lease.SemanticLease, out failure);

        public bool TryDispose(out NativeRuntimeFailureV1 failure)
        {
            if (State != NativeOwnerStateV1.Initialized)
            {
                failure = LifetimeFailure(0);
                return false;
            }

            if (_leases.Count != 0)
            {
                failure = LiveJobFailure(0);
                return false;
            }

            DisposeV2(ref _registeredFieldsV2, ref _registeredTypesV2, ref _nodeAccessRangesV2,
                ref _blackboardAccessesV2, ref _blackboardSlotsV2, ref _scopeLayoutBytesV2, ref _scopeDescriptorsV2);
            DisposeReverse(ref _debugRuntimeNodeIndices, ref _defaultValueBlob, ref _configBlob, ref _watchedSlotIndices,
                ref _observers, ref _blackboardSlots, ref _writeSlotIndices, ref _readSlotIndices, ref _childIndices, ref _nodes);
            State = NativeOwnerStateV1.Disposed;
            failure = default;
            return true;
        }

        internal bool IsLeaseActive(NativeProgramReadLeaseV1 lease)
            => ReferenceEquals(lease.Owner, this)
                && lease.Token.OwnerId == OwnerId
                && lease.Token.Generation == Generation
                && lease.Token.LeaseId != 0
                && _leases.ContainsKey(lease.Token.LeaseId);

        private NativeProgramImageViewV1 CreateView()
            => new NativeProgramImageViewV1(
                _header,
                _nodes.AsReadOnly(),
                _childIndices.AsReadOnly(),
                _readSlotIndices.AsReadOnly(),
                _writeSlotIndices.AsReadOnly(),
                _blackboardSlots.AsReadOnly(),
                _observers.AsReadOnly(),
                _watchedSlotIndices.AsReadOnly(),
                _configBlob.AsReadOnly(),
                _defaultValueBlob.AsReadOnly(),
                _debugRuntimeNodeIndices.AsReadOnly());

        private NativeProgramImageViewV2 CreateViewV2()
            => new NativeProgramImageViewV2(
                CreateView(), _headerV2, _scopeDescriptorsV2.AsReadOnly(), _scopeLayoutBytesV2.AsReadOnly(),
                _blackboardSlotsV2.AsReadOnly(), _blackboardAccessesV2.AsReadOnly(),
                _nodeAccessRangesV2.AsReadOnly(), _registeredTypesV2.AsReadOnly(), _registeredFieldsV2.AsReadOnly());

        private NativeRuntimeFailureV1 LifetimeFailure(ulong leaseId)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                ownerId: OwnerId,
                generation: Generation,
                leaseId: leaseId);

        private NativeRuntimeFailureV1 LiveJobFailure(ulong leaseId)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation,
                ownerId: OwnerId,
                generation: Generation,
                leaseId: leaseId);

        private static bool TryPreflight(
            CompiledProgram program,
            NativeProgramImageCapacityV1 capacity,
            out NativeRuntimeFailureV1 failure)
        {
            if (program == null)
            {
                failure = new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid);
                return false;
            }

            if (!NativeCheckedMathV1.IsPowerOfTwo(capacity.MaximumAlignment))
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.MaximumAlignment,
                    program.Header.RequiredMaximumAlignment,
                    capacity.MaximumAlignment,
                    capacity.MaximumAlignment);
                return false;
            }

            if (!RequireCapacity((uint)program.Nodes.Count, capacity.NodeRecords, NativeResourceKindV1.ProgramNodes, out failure)
                || !RequireCapacity((uint)program.ChildIndices.Count, capacity.ChildIndices, NativeResourceKindV1.ProgramChildIndices, out failure)
                || !RequireCapacity((uint)program.ReadSlotIndices.Count, capacity.ReadSlotIndices, NativeResourceKindV1.ProgramReadSlotIndices, out failure)
                || !RequireCapacity((uint)program.WriteSlotIndices.Count, capacity.WriteSlotIndices, NativeResourceKindV1.ProgramWriteSlotIndices, out failure)
                || !RequireCapacity((uint)program.BlackboardSlots.Count, capacity.BlackboardSlots, NativeResourceKindV1.ProgramBlackboardSlots, out failure)
                || !RequireCapacity((uint)program.Observers.Count, capacity.Observers, NativeResourceKindV1.ProgramObservers, out failure)
                || !RequireCapacity((uint)program.WatchedSlotIndices.Count, capacity.WatchedSlotIndices, NativeResourceKindV1.ProgramWatchedSlotIndices, out failure)
                || !RequireCapacity((uint)program.ConfigBlob.Count, capacity.ConfigBytes, NativeResourceKindV1.ProgramConfigBytes, out failure)
                || !RequireCapacity((uint)program.DefaultValueBlob.Count, capacity.DefaultBytes, NativeResourceKindV1.ProgramDefaultBytes, out failure)
                || !RequireCapacity((uint)program.DebugMap.Count, capacity.DebugOrdinals, NativeResourceKindV1.ProgramDebugOrdinals, out failure)
                || !RequireCapacity(program.Header.RequiredMaximumAlignment, capacity.MaximumAlignment, NativeResourceKindV1.MaximumAlignment, out failure))
            {
                return false;
            }

            if (!CompiledProgramContentHashV1.Compute(program).Equals(program.Header.CompiledContentHash))
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.ProgramHash);
                return false;
            }

            return TryValidateLogicalProgram(program, out failure);
        }

        private static bool TryValidateLogicalProgram(CompiledProgram program, out NativeRuntimeFailureV1 failure)
        {
            var header = program.Header;
            if (header.Magic != CompiledProgramHeader.ExpectedMagic
                || header.NodeCount != program.Nodes.Count
                || header.ChildIndexCount != program.ChildIndices.Count
                || header.BlackboardSlotCount != program.BlackboardSlots.Count
                || header.DebugMapCount != program.DebugMap.Count
                || header.ConfigBlobSize != program.ConfigBlob.Count
                || header.RootNodeIndex >= program.Nodes.Count
                || !NativeCheckedMathV1.IsPowerOfTwo(header.RequiredMaximumAlignment)
                || header.InstanceNodeMemorySize % header.RequiredMaximumAlignment != 0)
            {
                failure = InvalidPlan(NativeResourceKindV1.ProgramNodes);
                return false;
            }

            uint requiredAlignment = 1;
            for (var nodeIndex = 0; nodeIndex < program.Nodes.Count; nodeIndex++)
            {
                var node = program.Nodes[nodeIndex];
                if (!ValidateStorage(node.ConfigOffset, node.ConfigSize, node.ConfigAlignment, (uint)program.ConfigBlob.Count)
                    || !ValidateStorage(node.InstanceMemoryOffset, node.InstanceMemorySize, node.InstanceMemoryAlignment, header.InstanceNodeMemorySize)
                    || !ValidateRange(node.Children.Offset, node.Children.Count, (uint)program.ChildIndices.Count)
                    || !ValidateRange(node.ReadSlots.Offset, node.ReadSlots.Count, (uint)program.ReadSlotIndices.Count)
                    || !ValidateRange(node.WriteSlots.Offset, node.WriteSlots.Count, (uint)program.WriteSlotIndices.Count)
                    || node.DebugIdentityIndex != CompiledIndex.Invalid && node.DebugIdentityIndex >= program.DebugMap.Count)
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramNodes, (uint)nodeIndex, (uint)program.Nodes.Count, node.InstanceMemoryAlignment);
                    return false;
                }

                if (node.InstanceMemorySize != 0 && node.InstanceMemoryAlignment > requiredAlignment)
                {
                    requiredAlignment = node.InstanceMemoryAlignment;
                }

                for (var otherIndex = nodeIndex + 1; otherIndex < program.Nodes.Count; otherIndex++)
                {
                    var other = program.Nodes[otherIndex];
                    if (RangesOverlap(node.ConfigOffset, node.ConfigSize, other.ConfigOffset, other.ConfigSize)
                        || RangesOverlap(node.InstanceMemoryOffset, node.InstanceMemorySize, other.InstanceMemoryOffset, other.InstanceMemorySize))
                    {
                        failure = InvalidPlan(NativeResourceKindV1.ProgramNodes, (uint)nodeIndex, (uint)program.Nodes.Count);
                        return false;
                    }
                }
            }

            if (requiredAlignment != header.RequiredMaximumAlignment)
            {
                failure = InvalidPlan(NativeResourceKindV1.MaximumAlignment, requiredAlignment, header.RequiredMaximumAlignment, header.RequiredMaximumAlignment);
                return false;
            }

            for (var index = 0; index < program.ChildIndices.Count; index++)
            {
                if (program.ChildIndices[index] >= program.Nodes.Count)
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramChildIndices, (uint)index, (uint)program.ChildIndices.Count);
                    return false;
                }
            }

            if (!ValidateSlotAccessTable(program.ReadSlotIndices, program.BlackboardSlots, CompiledBlackboardAccessFlags.Read)
                || !ValidateSlotAccessTable(program.WriteSlotIndices, program.BlackboardSlots, CompiledBlackboardAccessFlags.Write))
            {
                failure = InvalidPlan(NativeResourceKindV1.ProgramBlackboardSlots);
                return false;
            }

            for (var slotIndex = 0; slotIndex < program.BlackboardSlots.Count; slotIndex++)
            {
                var slot = program.BlackboardSlots[slotIndex];
                if (slot.Scope == BlackboardScope.NodeLocal
                    || !ValidateStorage(slot.Offset, slot.Size, slot.Alignment, uint.MaxValue)
                    || !ValidateStorage(slot.DefaultValueOffset, slot.Size, slot.Alignment, (uint)program.DefaultValueBlob.Count))
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramBlackboardSlots, (uint)slotIndex, (uint)program.BlackboardSlots.Count, slot.Alignment);
                    return false;
                }

                for (var otherIndex = slotIndex + 1; otherIndex < program.BlackboardSlots.Count; otherIndex++)
                {
                    var other = program.BlackboardSlots[otherIndex];
                    if (slot.StableKeyId == other.StableKeyId
                        || slot.Scope == other.Scope && RangesOverlap(slot.Offset, slot.Size, other.Offset, other.Size))
                    {
                        failure = InvalidPlan(NativeResourceKindV1.ProgramBlackboardSlots, (uint)slotIndex, (uint)program.BlackboardSlots.Count);
                        return false;
                    }
                }
            }

            for (var observerIndex = 0; observerIndex < program.Observers.Count; observerIndex++)
            {
                var observer = program.Observers[observerIndex];
                if (observer.ObserverNodeIndex >= program.Nodes.Count
                    || observer.OwningReactiveCompositeIndex >= program.Nodes.Count
                    || observer.ObserverNodeIndex == observer.OwningReactiveCompositeIndex
                    || !ValidateRange(observer.WatchedSlots.Offset, observer.WatchedSlots.Count, (uint)program.WatchedSlotIndices.Count))
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramObservers, (uint)observerIndex, (uint)program.Observers.Count);
                    return false;
                }

                var end = observer.WatchedSlots.Offset + observer.WatchedSlots.Count;
                ulong previousKeyId = 0;
                for (var watchedIndex = observer.WatchedSlots.Offset; watchedIndex < end; watchedIndex++)
                {
                    var slotIndex = program.WatchedSlotIndices[(int)watchedIndex];
                    if (slotIndex >= program.BlackboardSlots.Count)
                    {
                        failure = InvalidPlan(NativeResourceKindV1.ProgramWatchedSlotIndices, watchedIndex, (uint)program.WatchedSlotIndices.Count);
                        return false;
                    }

                    var keyId = program.BlackboardSlots[(int)slotIndex].StableKeyId;
                    if (watchedIndex != observer.WatchedSlots.Offset && keyId <= previousKeyId)
                    {
                        failure = InvalidPlan(NativeResourceKindV1.ProgramWatchedSlotIndices, watchedIndex, (uint)program.WatchedSlotIndices.Count);
                        return false;
                    }

                    previousKeyId = keyId;
                }
            }

            for (var debugIndex = 0; debugIndex < program.DebugMap.Count; debugIndex++)
            {
                var nodeIndex = program.DebugMap[debugIndex].RuntimeNodeIndex;
                if (nodeIndex >= program.Nodes.Count
                    || program.Nodes[(int)nodeIndex].DebugIdentityIndex != (uint)debugIndex)
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramDebugOrdinals, (uint)debugIndex, (uint)program.DebugMap.Count);
                    return false;
                }
            }

            failure = default;
            return true;
        }

        private static bool ValidateSlotAccessTable(
            IReadOnlyList<uint> indices,
            IReadOnlyList<CompiledBlackboardSlotRecord> slots,
            CompiledBlackboardAccessFlags requiredFlag)
        {
            for (var index = 0; index < indices.Count; index++)
            {
                var slotIndex = indices[index];
                if (slotIndex >= slots.Count || (slots[(int)slotIndex].AccessFlags & requiredFlag) == 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool RangesOverlap(uint leftOffset, uint leftSize, uint rightOffset, uint rightSize)
            => leftSize != 0 && rightSize != 0
                && leftOffset < (ulong)rightOffset + rightSize
                && rightOffset < (ulong)leftOffset + leftSize;

        private static NativeRuntimeFailureV1 InvalidPlan(
            NativeResourceKindV1 resource,
            ulong requested = 0,
            ulong capacity = 0,
            uint alignment = 0)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                resource,
                requested,
                capacity,
                alignment);

        private static bool TryPreflightV2(
            NativeProgramBlackboardBindingV2 binding,
            NativeProgramImageCapacityV2 capacity,
            out NativeRuntimeFailureV1 failure)
        {
            try
            {
                return TryPreflightV2Core(binding, capacity, out failure);
            }
            catch (Exception)
            {
                failure = InvalidPlan(NativeResourceKindV1.ProgramHash);
                return false;
            }
        }

        private static bool TryPreflightV2Core(
            NativeProgramBlackboardBindingV2 binding,
            NativeProgramImageCapacityV2 capacity,
            out NativeRuntimeFailureV1 failure)
        {
            if (binding == null)
            {
                failure = InvalidPlan(NativeResourceKindV1.ProgramBlackboardSlots);
                return false;
            }

            var required = NativeProgramImageCapacityV2.Exact(binding);
            if (!RequireCapacity(required.ScopeDescriptors, capacity.ScopeDescriptors, NativeResourceKindV1.ProgramScopeDescriptors, out failure)
                || !RequireCapacity(required.ScopeLayoutBytes, capacity.ScopeLayoutBytes, NativeResourceKindV1.ProgramScopeLayoutBytes, out failure)
                || !RequireCapacity(required.Slots, capacity.Slots, NativeResourceKindV1.ProgramBlackboardSlots, out failure)
                || !RequireCapacity(required.Accesses, capacity.Accesses, NativeResourceKindV1.ProgramBlackboardAccesses, out failure)
                || !RequireCapacity(required.NodeAccessRanges, capacity.NodeAccessRanges, NativeResourceKindV1.ProgramNodeAccessRanges, out failure)
                || !RequireCapacity(required.RegisteredTypes, capacity.RegisteredTypes, NativeResourceKindV1.ProgramRegisteredTypes, out failure)
                || !RequireCapacity(required.RegisteredFields, capacity.RegisteredFields, NativeResourceKindV1.ProgramRegisteredFields, out failure))
                return false;

            if (binding.SemanticProgram.Header.CompiledFormatVersion != 2
                || binding.Slots.Count != binding.SemanticProgram.BlackboardSlots.Count
                || !CompiledProgramContentHashV1.Compute(binding.SemanticProgram).Equals(binding.SemanticProgram.Header.CompiledContentHash))
            {
                failure = InvalidPlan(NativeResourceKindV1.ProgramHash);
                return false;
            }
            if (!NativeCompiledProgramV2Verifier.TryValidate(binding, out var authorityResource))
            {
                failure = InvalidPlan(authorityResource);
                return false;
            }

            BlackboardScope previousScope = BlackboardScope.NodeLocal;
            for (var index = 0; index < binding.Scopes.Count; index++)
            {
                var scope = binding.Scopes[index];
                if (index != 0 && scope.Scope <= previousScope
                    || !ValidateRange(scope.FirstSlot, scope.SlotCount, (uint)binding.Slots.Count))
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramScopeDescriptors, (uint)index, (uint)binding.Scopes.Count);
                    return false;
                }
                previousScope = scope.Scope;
            }

            for (var index = 0; index < binding.RegisteredTypes.Count; index++)
            {
                var type = binding.RegisteredTypes[index];
                if (!type.Descriptor.IsValid || !type.Descriptor.HasCanonicalSchema
                    || !ValidateRange(type.FirstField, type.FieldCount, (uint)binding.RegisteredFields.Count))
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramRegisteredTypes, (uint)index, (uint)binding.RegisteredTypes.Count);
                    return false;
                }
            }

            for (var index = 0; index < binding.RegisteredFields.Count; index++)
            {
                var field = binding.RegisteredFields[index];
                if (field.FieldId == 0 || field.ValueTypeId == 0 || field.ValueTypeVersion == 0
                    || field.Size == 0 || !NativeCheckedMathV1.IsPowerOfTwo(field.Alignment)
                    || field.Offset % field.Alignment != 0 || field.Size % field.Alignment != 0
                    || !Enum.IsDefined(typeof(NativeBlackboardFieldEncodingV2), field.Encoding))
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramRegisteredFields, (uint)index, (uint)binding.RegisteredFields.Count, field.Alignment);
                    return false;
                }
            }

            for (var index = 0; index < binding.Slots.Count; index++)
            {
                var slot = binding.Slots[index];
                if (slot.StableKeyId == 0 || slot.TypeId == 0 || slot.TypeVersion == 0
                    || slot.Size == 0 || !NativeCheckedMathV1.IsPowerOfTwo(slot.Alignment)
                    || slot.Offset % slot.Alignment != 0 || slot.Size % slot.Alignment != 0
                    || !ValidateRange(slot.DefaultOffset, slot.DefaultSize, (uint)binding.SemanticProgram.DefaultValueBlob.Count)
                    || slot.DefaultSize != slot.Size
                    || slot.Scope == BlackboardScope.NodeLocal || !Enum.IsDefined(typeof(BlackboardScope), slot.Scope))
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramBlackboardSlots, (uint)index, (uint)binding.Slots.Count, slot.Alignment);
                    return false;
                }

                if (slot.ScopeDescriptorIndex != CompiledIndex.Invalid
                    && (slot.ScopeDescriptorIndex >= binding.Scopes.Count
                        || binding.Scopes[(int)slot.ScopeDescriptorIndex].Scope != slot.Scope))
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramScopeDescriptors, slot.ScopeDescriptorIndex, (uint)binding.Scopes.Count);
                    return false;
                }

                if (slot.RegisteredTypeIndex != CompiledIndex.Invalid)
                {
                    if (slot.RegisteredTypeIndex >= binding.RegisteredTypes.Count)
                    {
                        failure = InvalidPlan(NativeResourceKindV1.ProgramRegisteredTypes, slot.RegisteredTypeIndex, (uint)binding.RegisteredTypes.Count);
                        return false;
                    }
                    var descriptor = binding.RegisteredTypes[(int)slot.RegisteredTypeIndex].Descriptor;
                    if (descriptor.TypeId != slot.TypeId || descriptor.Version != slot.TypeVersion
                        || descriptor.Size != slot.Size || descriptor.Alignment != slot.Alignment)
                    {
                        failure = InvalidPlan(NativeResourceKindV1.ProgramRegisteredTypes, slot.RegisteredTypeIndex, (uint)binding.RegisteredTypes.Count);
                        return false;
                    }
                }
                else if (!MatchesBuiltIn(slot.TypeId, slot.TypeVersion, slot.Size, slot.Alignment, slot.EnumContractId))
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramBlackboardSlots, (uint)index, (uint)binding.Slots.Count);
                    return false;
                }

                for (var otherIndex = 0; otherIndex < index; otherIndex++)
                {
                    if (binding.Slots[otherIndex].StableKeyId != slot.StableKeyId) continue;
                    failure = InvalidPlan(NativeResourceKindV1.ProgramBlackboardSlots, (uint)index, (uint)binding.Slots.Count);
                    return false;
                }
            }

            uint expectedOrdinal = 0;
            uint currentNode = 0;
            for (var index = 0; index < binding.Accesses.Count; index++)
            {
                var access = binding.Accesses[index];
                if (index == 0 || access.NodeIndex != currentNode)
                {
                    if (index != 0 && access.NodeIndex <= currentNode)
                    {
                        failure = InvalidPlan(NativeResourceKindV1.ProgramBlackboardAccesses, (uint)index, (uint)binding.Accesses.Count);
                        return false;
                    }
                    currentNode = access.NodeIndex;
                    expectedOrdinal = 0;
                }
                if (access.NodeIndex >= binding.SemanticProgram.Nodes.Count || access.AccessOrdinal != expectedOrdinal++
                    || !Enum.IsDefined(typeof(NativeBlackboardAccessModeV2), access.Mode))
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramBlackboardAccesses, (uint)index, (uint)binding.Accesses.Count);
                    return false;
                }
                if (!TryResolveSlot(binding.Slots, access.Scope, access.SlotIndex, out var resolvedSlot))
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramBlackboardAccesses, (uint)index, (uint)binding.Accesses.Count);
                    return false;
                }
                var slot = binding.Slots[(int)resolvedSlot];
                if (access.Scope != slot.Scope || access.TypeId != slot.TypeId || access.TypeVersion != slot.TypeVersion
                    || access.EnumContractId != slot.EnumContractId || access.RegisteredTypeIndex != slot.RegisteredTypeIndex
                    || access.Mode != NativeBlackboardAccessModeV2.Read && (slot.AccessFlags & CompiledBlackboardAccessFlags.Write) == 0
                    || access.Mode != NativeBlackboardAccessModeV2.Write && (slot.AccessFlags & CompiledBlackboardAccessFlags.Read) == 0)
                {
                    failure = InvalidPlan(NativeResourceKindV1.ProgramBlackboardAccesses, (uint)index, (uint)binding.Accesses.Count);
                    return false;
                }
            }

            failure = default;
            return true;
        }

        private static bool TryResolveSlot(
            IReadOnlyList<NativeBlackboardSlotBindingV2> slots,
            BlackboardScope scope,
            uint scopeSlotIndex,
            out uint resolved)
        {
            resolved = 0;
            var found = false;
            for (var index = 0; index < slots.Count; index++)
            {
                if (slots[index].Scope != scope || slots[index].ScopeSlotIndex != scopeSlotIndex) continue;
                if (found) return false;
                resolved = (uint)index;
                found = true;
            }
            return found;
        }

        private static bool MatchesBuiltIn(ulong typeId, uint version, uint size, uint alignment, ulong enumContractId)
        {
            for (var raw = (int)BlackboardValueType.Bool; raw <= (int)BlackboardValueType.AssetId; raw++)
            {
                if (!BuiltInBlackboardTypes.TryGet((BlackboardValueType)raw, out var descriptor) || descriptor.TypeId != typeId) continue;
                return descriptor.Version == version && descriptor.Size == size && descriptor.Alignment == alignment
                    && ((BlackboardValueType)raw == BlackboardValueType.Enum32 ? enumContractId != 0 : enumContractId == 0);
            }
            return false;
        }

        private static bool RequireCapacity(uint requested, uint capacity, NativeResourceKindV1 resource, out NativeRuntimeFailureV1 failure)
        {
            if (requested > capacity)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeProgramCapacityExceeded,
                    resource,
                    requested,
                    capacity);
                return false;
            }

            failure = default;
            return true;
        }

        private static bool ValidateStorage(uint offset, uint size, uint alignment, uint storageLength)
            => NativeCheckedMathV1.IsPowerOfTwo(alignment)
                && (size != 0 || offset == 0 && alignment == 1)
                && (size == 0 || offset % alignment == 0 && size % alignment == 0)
                && ValidateRange(offset, size, storageLength);

        private static bool ValidateRange(uint offset, uint count, uint tableLength)
            => offset != CompiledIndex.Invalid && (ulong)offset + count <= tableLength;

        private static NativeArray<T> Allocate<T>(int length, Allocator allocator, int failAfter, ref int allocations)
            where T : struct
        {
            if (failAfter >= 0 && allocations == failAfter)
            {
                throw new InvalidOperationException("Injected native allocation failure.");
            }

            var result = new NativeArray<T>(length, allocator, NativeArrayOptions.ClearMemory);
            allocations++;
            return result;
        }

        private static void Copy(
            CompiledProgram program,
            NativeArray<NativeCompiledNodeRecordV1> nodes,
            NativeArray<uint> childIndices,
            NativeArray<uint> readSlotIndices,
            NativeArray<uint> writeSlotIndices,
            NativeArray<NativeCompiledBlackboardSlotRecordV1> blackboardSlots,
            NativeArray<NativeCompiledObserverRecordV1> observers,
            NativeArray<uint> watchedSlotIndices,
            NativeArray<byte> configBlob,
            NativeArray<byte> defaultValueBlob,
            NativeArray<uint> debugRuntimeNodeIndices)
        {
            for (var index = 0; index < nodes.Length; index++) nodes[index] = new NativeCompiledNodeRecordV1(program.Nodes[index]);
            for (var index = 0; index < childIndices.Length; index++) childIndices[index] = program.ChildIndices[index];
            for (var index = 0; index < readSlotIndices.Length; index++) readSlotIndices[index] = program.ReadSlotIndices[index];
            for (var index = 0; index < writeSlotIndices.Length; index++) writeSlotIndices[index] = program.WriteSlotIndices[index];
            for (var index = 0; index < blackboardSlots.Length; index++) blackboardSlots[index] = new NativeCompiledBlackboardSlotRecordV1(program.BlackboardSlots[index]);
            for (var index = 0; index < observers.Length; index++) observers[index] = new NativeCompiledObserverRecordV1(program.Observers[index]);
            for (var index = 0; index < watchedSlotIndices.Length; index++) watchedSlotIndices[index] = program.WatchedSlotIndices[index];
            for (var index = 0; index < configBlob.Length; index++) configBlob[index] = program.ConfigBlob[index];
            for (var index = 0; index < defaultValueBlob.Length; index++) defaultValueBlob[index] = program.DefaultValueBlob[index];
            for (var index = 0; index < debugRuntimeNodeIndices.Length; index++) debugRuntimeNodeIndices[index] = program.DebugMap[index].RuntimeNodeIndex;
        }

        private static void CopyV2(
            NativeProgramBlackboardBindingV2 binding,
            NativeArray<NativeBlackboardScopeRecordV2> scopes,
            NativeArray<byte> layoutBytes,
            NativeArray<NativeBlackboardSlotBindingV2> slots,
            NativeArray<NativeBlackboardAccessRecordV2> accesses,
            NativeArray<NativeNodeBlackboardAccessRangeV2> ranges,
            NativeArray<NativeRegisteredBlackboardTypeRecordV2> types,
            NativeArray<NativeRegisteredBlackboardFieldRecordV2> fields)
        {
            uint rawOffset = 0;
            for (var index = 0; index < scopes.Length; index++)
            {
                var raw = binding.Scopes[index].GetRawLayoutCopy();
                scopes[index] = new NativeBlackboardScopeRecordV2(binding.Scopes[index], rawOffset, (uint)raw.Length);
                for (var byteIndex = 0; byteIndex < raw.Length; byteIndex++) layoutBytes[(int)rawOffset + byteIndex] = raw[byteIndex];
                rawOffset += (uint)raw.Length;
            }
            for (var index = 0; index < slots.Length; index++) slots[index] = binding.Slots[index];
            for (var index = 0; index < accesses.Length; index++)
            {
                if (!TryResolveSlot(binding.Slots, binding.Accesses[index].Scope, binding.Accesses[index].SlotIndex, out var resolved))
                    throw new InvalidOperationException("Validated access lost its scope-local slot mapping.");
                accesses[index] = new NativeBlackboardAccessRecordV2(binding.Accesses[index], resolved);
            }
            for (var index = 0; index < types.Length; index++) types[index] = new NativeRegisteredBlackboardTypeRecordV2(binding.RegisteredTypes[index]);
            for (var index = 0; index < fields.Length; index++) fields[index] = new NativeRegisteredBlackboardFieldRecordV2(binding.RegisteredFields[index]);
            var accessIndex = 0;
            for (var nodeIndex = 0; nodeIndex < ranges.Length; nodeIndex++)
            {
                var first = accessIndex;
                while (accessIndex < accesses.Length && accesses[accessIndex].NodeIndex == nodeIndex) accessIndex++;
                ranges[nodeIndex] = new NativeNodeBlackboardAccessRangeV2((uint)first, (uint)(accessIndex - first));
            }
        }

        private static void DisposeV2(
            ref NativeArray<NativeRegisteredBlackboardFieldRecordV2> fields,
            ref NativeArray<NativeRegisteredBlackboardTypeRecordV2> types,
            ref NativeArray<NativeNodeBlackboardAccessRangeV2> ranges,
            ref NativeArray<NativeBlackboardAccessRecordV2> accesses,
            ref NativeArray<NativeBlackboardSlotBindingV2> slots,
            ref NativeArray<byte> layouts,
            ref NativeArray<NativeBlackboardScopeRecordV2> scopes)
        {
            Dispose(ref fields); Dispose(ref types); Dispose(ref ranges); Dispose(ref accesses);
            Dispose(ref slots); Dispose(ref layouts); Dispose(ref scopes);
        }

        private static void DisposeReverse(
            ref NativeArray<uint> debugRuntimeNodeIndices,
            ref NativeArray<byte> defaultValueBlob,
            ref NativeArray<byte> configBlob,
            ref NativeArray<uint> watchedSlotIndices,
            ref NativeArray<NativeCompiledObserverRecordV1> observers,
            ref NativeArray<NativeCompiledBlackboardSlotRecordV1> blackboardSlots,
            ref NativeArray<uint> writeSlotIndices,
            ref NativeArray<uint> readSlotIndices,
            ref NativeArray<uint> childIndices,
            ref NativeArray<NativeCompiledNodeRecordV1> nodes)
        {
            Dispose(ref debugRuntimeNodeIndices);
            Dispose(ref defaultValueBlob);
            Dispose(ref configBlob);
            Dispose(ref watchedSlotIndices);
            Dispose(ref observers);
            Dispose(ref blackboardSlots);
            Dispose(ref writeSlotIndices);
            Dispose(ref readSlotIndices);
            Dispose(ref childIndices);
            Dispose(ref nodes);
        }

        private static void Dispose<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
                array = default;
            }
        }
    }

    public readonly struct NativeProgramReadLeaseV1
    {
        internal NativeProgramReadLeaseV1(
            NativeProgramImageOwnerV1 owner,
            NativeLeaseTokenV1 token,
            NativeProgramImageViewV1 view)
        {
            Owner = owner;
            Token = token;
            View = view;
        }

        internal NativeProgramImageOwnerV1 Owner { get; }
        public NativeLeaseTokenV1 Token { get; }
        public NativeProgramImageViewV1 View { get; }
        public bool IsValid => Owner != null && Token.IsValid;
    }
}
