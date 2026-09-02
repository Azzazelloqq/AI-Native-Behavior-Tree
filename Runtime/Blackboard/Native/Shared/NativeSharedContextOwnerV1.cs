using System;
using System.Threading;
using AIBT.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;

namespace AIBT
{
    public sealed class NativeSharedContextOwnerV1
    {
        private struct BindingEntry
        {
            internal bool Active;
            internal ulong Id;
            internal TreeInstanceId TreeInstanceId;
            internal NativeProgramReadLeaseV2 ProgramLease;
            internal NativeInstanceArenaOwnerV1 Instance;
        }

        private static readonly ProfilerMarker s_ReduceUpdateMarker =
            new ProfilerMarker("AIBT.Native.Blackboard.Shared.ReduceUpdate");

        private static long s_nextOwnerId;
        private NativeArray<byte> _values;
        private NativeArray<ulong> _versions;
        private NativeArray<ulong> _revision;
        private NativeArray<NativeSharedContributionStreamV1> _streamHeaders;
        private NativeArray<NativeSharedContributionRecordV1> _records;
        private NativeArray<byte> _payload;
        private NativeArray<uint> _sortEntries;
        private NativeArray<byte> _stagedValues;
        private NativeArray<uint> _changedSlots;
        private BindingEntry[] _bindings;
        private NativeProgramImageViewV2 _authority;
        private NativeBlackboardScopeRecordV2 _descriptor;
        private NativeOwnerStateV1 _state;
        private ulong _nextBindingId;
        private NativeExecuteSelectionReadLeaseV1 _selectionLease;
        private NativeSharedUpdateWindowV1 _update;
        private JobHandle[] _streamDependencies;
        private byte[] _dependencyRegistered;
        private ulong[] _activeContributionLeaseIds;
        private ulong _nextUpdateId;
        private ulong _nextContributionLeaseId;
        private ulong _nextReductionLeaseId;
        private ulong _activeReductionLeaseId;
        private ulong _nextReportId;
        private JobHandle _reductionDependency;
        private byte _reductionDependencyRegistered;

        private NativeSharedContextOwnerV1() { }

        public ulong OwnerId { get; private set; }
        public uint Generation { get; private set; }
        public NativeOwnerStateV1 State => _state;
        public NativeSharedContextCapacityV1 Capacity { get; private set; }

        public static bool TryCreate(
            NativeProgramReadLeaseV2 programLease,
            NativeSharedContextCapacityV1 capacity,
            Allocator allocator,
            out NativeSharedContextOwnerV1 context,
            out NativeRuntimeFailureV1 failure)
            => TryCreate(programLease, capacity, allocator, -1, out context, out failure);

        private static bool TryCreate(
            NativeProgramReadLeaseV2 programLease,
            NativeSharedContextCapacityV1 capacity,
            Allocator allocator,
            int failAfterSuccessfulAllocations,
            out NativeSharedContextOwnerV1 context,
            out NativeRuntimeFailureV1 failure)
        {
            context = null;
            if (allocator != Allocator.Persistent)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeAllocatorInvalid,
                    NativeResourceKindV1.InstanceSharedBlackboard,
                    (ulong)allocator,
                    (ulong)Allocator.Persistent);
                return false;
            }
            var descriptor = default(NativeBlackboardScopeRecordV2);
            if (!programLease.SemanticLease.IsValid || programLease.SemanticLease.Owner == null
                || !programLease.SemanticLease.Owner.IsLeaseActive(programLease.SemanticLease)
                || !TryPreflight(programLease.View, capacity, out descriptor))
            {
                failure = RegistryFailure();
                return false;
            }

            var values = default(NativeArray<byte>);
            var versions = default(NativeArray<ulong>);
            var revision = default(NativeArray<ulong>);
            var streams = default(NativeArray<NativeSharedContributionStreamV1>);
            var records = default(NativeArray<NativeSharedContributionRecordV1>);
            var payload = default(NativeArray<byte>);
            var sortEntries = default(NativeArray<uint>);
            var stagedValues = default(NativeArray<byte>);
            var changedSlots = default(NativeArray<uint>);
            var allocations = 0;
            try
            {
                values = Allocate<byte>(capacity.ValueBytes, allocator, failAfterSuccessfulAllocations, ref allocations);
                versions = Allocate<ulong>(capacity.SlotVersions, allocator, failAfterSuccessfulAllocations, ref allocations);
                revision = Allocate<ulong>(1, allocator, failAfterSuccessfulAllocations, ref allocations);
                streams = Allocate<NativeSharedContributionStreamV1>(capacity.MaximumSelectedInstances, allocator, failAfterSuccessfulAllocations, ref allocations);
                records = Allocate<NativeSharedContributionRecordV1>(capacity.ContributionRecords, allocator, failAfterSuccessfulAllocations, ref allocations);
                payload = Allocate<byte>(capacity.ContributionPayloadBytes, allocator, failAfterSuccessfulAllocations, ref allocations);
                sortEntries = Allocate<uint>(capacity.ContributionRecords, allocator, failAfterSuccessfulAllocations, ref allocations);
                stagedValues = Allocate<byte>(capacity.ValueBytes, allocator, failAfterSuccessfulAllocations, ref allocations);
                changedSlots = Allocate<uint>(checked(descriptor.SlotCount + 2), allocator, failAfterSuccessfulAllocations, ref allocations);

                CopyDefaults(programLease.View, values);
                var rawOwner = Interlocked.Increment(ref s_nextOwnerId);
                if (rawOwner <= 0) throw new OverflowException();
                context = new NativeSharedContextOwnerV1
                {
                    OwnerId = (ulong)rawOwner,
                    Generation = 1,
                    Capacity = capacity,
                    _state = NativeOwnerStateV1.Initialized,
                    _values = values,
                    _versions = versions,
                    _revision = revision,
                    _streamHeaders = streams,
                    _records = records,
                    _payload = payload,
                    _sortEntries = sortEntries,
                    _stagedValues = stagedValues,
                    _changedSlots = changedSlots,
                    _bindings = new BindingEntry[capacity.MaximumBindings],
                    _streamDependencies = new JobHandle[capacity.MaximumSelectedInstances],
                    _dependencyRegistered = new byte[capacity.MaximumSelectedInstances],
                    _activeContributionLeaseIds = new ulong[capacity.MaximumSelectedInstances],
                    _authority = programLease.View,
                    _descriptor = descriptor,
                };
                failure = default;
                return true;
            }
            catch (Exception)
            {
                Dispose(ref changedSlots);
                Dispose(ref stagedValues);
                Dispose(ref sortEntries);
                Dispose(ref payload);
                Dispose(ref records);
                Dispose(ref streams);
                Dispose(ref revision);
                Dispose(ref versions);
                Dispose(ref values);
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    NativeResourceKindV1.InstanceSharedBlackboard);
                return false;
            }
        }

        public bool TryBind(
            TreeInstanceId treeInstanceId,
            NativeProgramReadLeaseV2 programLease,
            NativeInstanceArenaOwnerV1 instance,
            out NativeSharedBindingV1 binding,
            out NativeRuntimeFailureV1 failure)
        {
            binding = default;
            if (_state != NativeOwnerStateV1.Initialized || _update.IsValid || !treeInstanceId.IsValid
                || instance == null || instance.State != NativeOwnerStateV1.Initialized
                || !programLease.SemanticLease.IsValid || !Compatible(programLease.View))
            {
                failure = RegistryFailure();
                return false;
            }

            var free = -1;
            for (var index = 0; index < _bindings.Length; index++)
            {
                if (!_bindings[index].Active)
                {
                    if (free < 0) free = index;
                    continue;
                }
                if (_bindings[index].TreeInstanceId == treeInstanceId
                    || ReferenceEquals(_bindings[index].Instance, instance))
                {
                    failure = RegistryFailure();
                    return false;
                }
            }
            if (free < 0)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    NativeResourceKindV1.SharedBindings,
                    (uint)_bindings.Length + 1,
                    (uint)_bindings.Length);
                return false;
            }
            if (_nextBindingId == ulong.MaxValue)
            {
                failure = OverflowFailure(NativeResourceKindV1.SharedBindings);
                return false;
            }

            var id = ++_nextBindingId;
            _bindings[free] = new BindingEntry
            {
                Active = true,
                Id = id,
                TreeInstanceId = treeInstanceId,
                ProgramLease = programLease,
                Instance = instance,
            };
            binding = new NativeSharedBindingV1(OwnerId, Generation, id, treeInstanceId);
            failure = default;
            return true;
        }

        public bool TryUnbind(NativeSharedBindingV1 binding, out NativeRuntimeFailureV1 failure)
        {
            if (_state != NativeOwnerStateV1.Initialized || _update.IsValid)
            {
                failure = _update.IsValid ? LiveFailure() : LifetimeFailure(binding.BindingId);
                return false;
            }
            var index = FindBinding(binding.TreeInstanceId);
            if (index < 0 || binding.OwnerId != OwnerId || binding.Generation != Generation
                || _bindings[index].Id != binding.BindingId)
            {
                failure = LifetimeFailure(binding.BindingId);
                return false;
            }
            if (!IsQuiescent())
            {
                failure = LiveFailure();
                return false;
            }
            _bindings[index] = default;
            failure = default;
            return true;
        }

        public bool TryAcquireExecutionView(
            NativeSharedBindingV1 binding,
            NativeInstanceExecutionLeaseV2 execution,
            out NativeSharedExecutionViewV1 view,
            out NativeRuntimeFailureV1 failure)
        {
            view = default;
            if (_state != NativeOwnerStateV1.Initialized)
            {
                failure = LifetimeFailure(binding.BindingId);
                return false;
            }
            var index = FindBinding(binding.TreeInstanceId);
            if (index < 0 || binding.OwnerId != OwnerId || binding.Generation != Generation
                || _bindings[index].Id != binding.BindingId
                || !ReferenceEquals(_bindings[index].Instance, execution.SemanticLease.Owner)
                || !_bindings[index].Instance.IsExecutionLeaseActive(execution)
                || !Compatible(execution.Program))
            {
                failure = RegistryFailure();
                return false;
            }
            view = new NativeSharedExecutionViewV1(
                binding.TreeInstanceId,
                execution.Program,
                new NativeSharedContextViewV1(_values, _versions, _revision));
            failure = default;
            return true;
        }

        public bool TryReset(out NativeRuntimeFailureV1 failure)
        {
            if (_state != NativeOwnerStateV1.Initialized || _update.IsValid)
            {
                failure = _update.IsValid ? LiveFailure() : LifetimeFailure(0);
                return false;
            }
            if (!IsQuiescent())
            {
                failure = LiveFailure();
                return false;
            }

            var changed = false;
            for (var index = 0; index < _authority.Slots.Length; index++)
            {
                var slot = _authority.Slots[index];
                if (slot.Scope != BlackboardScope.Shared) continue;
                if (!NativeBlackboardCanonicalV1.IsCanonical(
                    _authority,
                    slot,
                    _authority.Semantic.DefaultValueBlob,
                    slot.DefaultOffset))
                {
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.BlackboardInvalidValue,
                        NativeResourceKindV1.InstanceSharedBlackboard);
                    return false;
                }
                if (DefaultEquals(slot)) continue;
                if (_versions[index] == ulong.MaxValue || _revision[0] == ulong.MaxValue)
                {
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.BlackboardVersionOverflow,
                        _versions[index] == ulong.MaxValue
                            ? NativeResourceKindV1.InstanceSharedSlotVersions
                            : NativeResourceKindV1.InstanceSharedRevision,
                        ownerId: OwnerId,
                        generation: Generation);
                    return false;
                }
                changed = true;
            }
            if (!changed)
            {
                failure = default;
                return true;
            }

            for (var index = 0; index < _authority.Slots.Length; index++)
            {
                var slot = _authority.Slots[index];
                if (slot.Scope != BlackboardScope.Shared || DefaultEquals(slot)) continue;
                for (var item = 0; item < slot.Size; item++)
                    _values[(int)slot.Offset + item]
                        = _authority.Semantic.DefaultValueBlob[(int)slot.DefaultOffset + item];
                _versions[index]++;
            }
            _revision[0]++;
            failure = default;
            return true;
        }

        public bool TryDispose(out NativeRuntimeFailureV1 failure)
        {
            if (_state != NativeOwnerStateV1.Initialized || _update.IsValid)
            {
                failure = _update.IsValid ? LiveFailure() : LifetimeFailure(0);
                return false;
            }
            if (!IsQuiescent() || HasBindings())
            {
                failure = LiveFailure();
                return false;
            }

            Dispose(ref _changedSlots);
            Dispose(ref _stagedValues);
            Dispose(ref _sortEntries);
            Dispose(ref _payload);
            Dispose(ref _records);
            Dispose(ref _streamHeaders);
            Dispose(ref _revision);
            Dispose(ref _versions);
            Dispose(ref _values);
            _bindings = null;
            _streamDependencies = null;
            _dependencyRegistered = null;
            _activeContributionLeaseIds = null;
            _state = NativeOwnerStateV1.Disposed;
            failure = default;
            return true;
        }

        public bool TryBeginUpdate(
            NativeExecuteSelectionWindowOwnerV1 selectionOwner,
            NativeExecuteSelectionWindowV1 selectionWindow,
            out NativeSharedUpdateWindowV1 update,
            out NativeRuntimeFailureV1 failure)
        {
            update = default;
            if (_state != NativeOwnerStateV1.Initialized || _update.IsValid)
            {
                failure = _update.IsValid ? LiveFailure() : LifetimeFailure(0);
                return false;
            }
            if (selectionOwner == null)
            {
                failure = RegistryFailure();
                return false;
            }
            if (!selectionOwner.TryAcquireReadLease(selectionWindow, out var selectionLease, out failure))
                return false;

            uint streamCount = 0;
            ulong totalRecords = 0;
            ulong totalPayload = 0;
            NativeResourceKindV1 failedResource = NativeResourceKindV1.None;
            for (var index = 0; index < selectionLease.View.Count; index++)
            {
                var selected = selectionLease.View.Entries[(int)index];
                var bindingIndex = FindBinding(selected.TreeInstanceId);
                if (bindingIndex < 0) continue;
                if (!selected.HasSharedCapacity
                    || _bindings[bindingIndex].Instance.State != NativeOwnerStateV1.Initialized
                    || !Compatible(_bindings[bindingIndex].ProgramLease.View))
                {
                    failedResource = NativeResourceKindV1.SharedContributionStreams;
                    break;
                }
                streamCount++;
                totalRecords += selected.SharedRecordCapacity;
                totalPayload += selected.SharedPayloadCapacity;
                if (streamCount > _streamHeaders.Length || totalRecords > (uint)_records.Length
                    || totalPayload > (uint)_payload.Length)
                {
                    failedResource = totalRecords > (uint)_records.Length
                        ? NativeResourceKindV1.SharedContributionRecords
                        : totalPayload > (uint)_payload.Length
                            ? NativeResourceKindV1.SharedContributionPayload
                            : NativeResourceKindV1.SharedContributionStreams;
                    break;
                }
            }
            if (failedResource != NativeResourceKindV1.None || streamCount == 0 || _nextUpdateId == ulong.MaxValue)
            {
                selectionOwner.TryReleaseReadLease(selectionLease, out _);
                if (_nextUpdateId == ulong.MaxValue)
                    failure = OverflowFailure(NativeResourceKindV1.SharedContributionStreams);
                else if (failedResource == NativeResourceKindV1.SharedContributionStreams || streamCount == 0)
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                        NativeResourceKindV1.SharedContributionStreams);
                else
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeOutputCapacityExceeded,
                        failedResource,
                        failedResource == NativeResourceKindV1.SharedContributionRecords ? totalRecords : totalPayload,
                        failedResource == NativeResourceKindV1.SharedContributionRecords
                            ? (uint)_records.Length : (uint)_payload.Length);
                return false;
            }

            var updateId = ++_nextUpdateId;
            uint streamIndex = 0;
            uint recordOffset = 0;
            uint payloadOffset = 0;
            for (var index = 0; index < selectionLease.View.Count; index++)
            {
                var selected = selectionLease.View.Entries[(int)index];
                if (FindBinding(selected.TreeInstanceId) < 0) continue;
                _streamHeaders[(int)streamIndex] = new NativeSharedContributionStreamV1
                {
                    UpdateId = updateId,
                    OwnerTreeInstanceId = selected.TreeInstanceId.Value,
                    FirstRecord = recordOffset,
                    RecordCapacity = selected.SharedRecordCapacity,
                    PayloadOffset = payloadOffset,
                    PayloadCapacity = selected.SharedPayloadCapacity,
                    State = NativeSharedContributionStreamStateV1.Reserved,
                    Valid = 1,
                };
                _streamDependencies[streamIndex] = default;
                _dependencyRegistered[streamIndex] = 0;
                recordOffset += selected.SharedRecordCapacity;
                payloadOffset += selected.SharedPayloadCapacity;
                streamIndex++;
            }
            _changedSlots[_changedSlots.Length - 2] = 0;
            _changedSlots[_changedSlots.Length - 1] = 0;
            _selectionLease = selectionLease;
            _update = new NativeSharedUpdateWindowV1(
                OwnerId, Generation, updateId, streamCount,
                selectionWindow.OwnerId, selectionWindow.Generation, selectionWindow.WindowId);
            update = _update;
            failure = default;
            return true;
        }

        public bool TryAcquireContributionStream(
            NativeSharedUpdateWindowV1 update,
            NativeSharedBindingV1 binding,
            NativeInstanceExecutionLeaseV2 execution,
            out NativeSharedContributionLeaseV1 lease,
            out NativeRuntimeFailureV1 failure)
        {
            lease = default;
            if (!IsUpdate(update))
            {
                failure = LifetimeFailure(update.UpdateId);
                return false;
            }
            var bindingIndex = FindBinding(binding.TreeInstanceId);
            var streamIndex = FindStream(binding.TreeInstanceId);
            if (bindingIndex < 0 || streamIndex < 0
                || binding.OwnerId != OwnerId || binding.Generation != Generation
                || _bindings[bindingIndex].Id != binding.BindingId
                || !ReferenceEquals(_bindings[bindingIndex].Instance, execution.SemanticLease.Owner)
                || !_bindings[bindingIndex].Instance.IsExecutionLeaseActive(execution)
                || !Compatible(execution.Program))
            {
                failure = RegistryFailure();
                return false;
            }
            var stream = _streamHeaders[streamIndex];
            if (stream.State != NativeSharedContributionStreamStateV1.Reserved)
            {
                failure = LiveFailure();
                return false;
            }
            if (_nextContributionLeaseId == ulong.MaxValue)
            {
                failure = OverflowFailure(NativeResourceKindV1.LeaseCounter);
                return false;
            }
            var leaseId = ++_nextContributionLeaseId;
            stream.State = NativeSharedContributionStreamStateV1.Active;
            stream.ActiveLeaseId = leaseId;
            _streamHeaders[streamIndex] = stream;
            _activeContributionLeaseIds[streamIndex] = leaseId;
            var token = new NativeLeaseTokenV1(OwnerId, Generation, leaseId);
            lease = new NativeSharedContributionLeaseV1(
                this, token, _update, binding.TreeInstanceId, execution,
                new NativeSharedContributionWriterV1(
                    execution.Program, _streamHeaders, _records, _payload,
                    (uint)streamIndex, update.UpdateId, leaseId));
            failure = default;
            return true;
        }

        public bool TryAcquireContributionStream(
            NativeSharedUpdateWindowV1 update,
            NativeSharedBindingV1 binding,
            NativeAgentExecuteLeaseV2 agentLease,
            out NativeSharedContributionLeaseV1 lease,
            out NativeRuntimeFailureV1 failure)
        {
            lease = default;
            if (!agentLease.IsValid || !agentLease.Owner.IsLeaseActive(agentLease)
                || agentLease.TreeInstanceId != binding.TreeInstanceId)
            {
                failure = RegistryFailure();
                return false;
            }
            return TryAcquireContributionStream(update, binding, agentLease.TreeLease, out lease, out failure);
        }

        public bool TryRegisterDependency(
            NativeSharedContributionLeaseV1 lease,
            JobHandle dependency,
            out NativeRuntimeFailureV1 failure)
        {
            if (!TryValidateLeaseManaged(lease, out var streamIndex))
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }
            if (_dependencyRegistered[streamIndex] != 0)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                    NativeResourceKindV1.SharedContributionStreams,
                    ownerId: OwnerId, generation: Generation, leaseId: lease.Token.LeaseId);
                return false;
            }
            if (!lease.Execution.SemanticLease.Owner.TryRegisterDependency(lease.Execution, dependency, out failure))
                return false;
            _streamDependencies[streamIndex] = dependency;
            _dependencyRegistered[streamIndex] = 1;
            failure = default;
            return true;
        }

        public bool TrySealContributionStream(
            NativeSharedContributionLeaseV1 lease,
            out NativeSharedContributionStreamViewV1 view,
            out NativeRuntimeFailureV1 failure)
        {
            view = default;
            if (!TryGetActiveLease(lease, out var streamIndex, out var stream))
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }
            if (_dependencyRegistered[streamIndex] == 0 || !_streamDependencies[streamIndex].IsCompleted)
            {
                failure = LiveFailure();
                return false;
            }
            _streamDependencies[streamIndex].Complete();
            stream.State = NativeSharedContributionStreamStateV1.Sealed;
            stream.ActiveLeaseId = 0;
            _streamHeaders[streamIndex] = stream;
            _activeContributionLeaseIds[streamIndex] = 0;
            _dependencyRegistered[streamIndex] = 0;
            _streamDependencies[streamIndex] = default;
            view = new NativeSharedContributionStreamViewV1(
                _records.AsReadOnly(), _payload.AsReadOnly(), stream);
            failure = default;
            return true;
        }

        public bool TryCancelContributionStream(
            NativeSharedContributionLeaseV1 lease,
            out NativeRuntimeFailureV1 failure)
        {
            if (!TryGetActiveLease(lease, out var streamIndex, out var stream))
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }
            if (stream.RecordCount != 0 || stream.PayloadCount != 0
                || _dependencyRegistered[streamIndex] != 0)
            {
                failure = LiveFailure();
                return false;
            }
            stream.State = NativeSharedContributionStreamStateV1.Canceled;
            stream.ActiveLeaseId = 0;
            _streamHeaders[streamIndex] = stream;
            _activeContributionLeaseIds[streamIndex] = 0;
            failure = default;
            return true;
        }

        public bool TryAbortUpdate(
            NativeSharedUpdateWindowV1 update,
            out NativeRuntimeFailureV1 failure)
        {
            if (!IsUpdate(update))
            {
                failure = LifetimeFailure(update.UpdateId);
                return false;
            }
            if (_activeReductionLeaseId != 0)
            {
                failure = LiveFailure();
                return false;
            }
            for (var index = 0; index < update.Count; index++)
            {
                if (_streamHeaders[index].State == NativeSharedContributionStreamStateV1.Active
                    || _dependencyRegistered[index] != 0)
                {
                    failure = LiveFailure();
                    return false;
                }
            }
            var selectionOwner = _selectionLease.Owner;
            if (selectionOwner == null)
            {
                failure = LifetimeFailure(update.UpdateId);
                return false;
            }
            if (!selectionOwner.TryReleaseReadLease(_selectionLease, out failure))
                return false;
            ClearUpdate();
            failure = default;
            return true;
        }

        public bool TryReduceUpdate(
            NativeSharedUpdateWindowV1 update,
            out NativeSharedCommitReportV1 report,
            out NativeRuntimeFailureV1 failure)
        {
            using var _ = s_ReduceUpdateMarker.Auto();
            report = default;
            if (!TryAcquireReductionLease(update, out var lease, out failure)) return false;
            NativeSharedReductionV1.TryReduce(lease.View);
            if (!TryRegisterReductionDependency(lease, default, out failure)) return false;
            return TryCompleteReduction(lease, out report, out failure);
        }

        public bool TryAcquireReductionLease(
            NativeSharedUpdateWindowV1 update,
            out NativeSharedReductionLeaseV1 lease,
            out NativeRuntimeFailureV1 failure)
        {
            lease = default;
            if (!IsUpdate(update))
            {
                failure = LifetimeFailure(update.UpdateId);
                return false;
            }
            if (_activeReductionLeaseId != 0 || !IsQuiescent())
            {
                failure = LiveFailure();
                return false;
            }
            for (var index = 0; index < update.Count; index++)
            {
                var stream = _streamHeaders[index];
                if (stream.State != NativeSharedContributionStreamStateV1.Sealed
                    && stream.State != NativeSharedContributionStreamStateV1.Canceled
                    || _dependencyRegistered[index] != 0
                    || _activeContributionLeaseIds[index] != 0)
                {
                    failure = LiveFailure();
                    return false;
                }
            }
            if (_nextReductionLeaseId == ulong.MaxValue || _nextReportId == ulong.MaxValue
                || update.UpdateId == ulong.MaxValue)
            {
                failure = OverflowFailure(NativeResourceKindV1.SharedCommitReport);
                return false;
            }
            var streamControl = _streamHeaders[0];
            streamControl.ReductionResult = BurstContextResult.IncompleteValue;
            streamControl.ReductionFailureCode = NativeRuntimeDiagnosticCodeV1.None;
            streamControl.ReductionFailureResource = NativeResourceKindV1.None;
            streamControl.ChangedSlotCount = 0;
            _streamHeaders[0] = streamControl;
            for (var index = 0; index < _changedSlots.Length; index++) _changedSlots[index] = 0;
            var leaseId = ++_nextReductionLeaseId;
            _activeReductionLeaseId = leaseId;
            _reductionDependency = default;
            _reductionDependencyRegistered = 0;
            lease = new NativeSharedReductionLeaseV1(
                this,
                new NativeLeaseTokenV1(OwnerId, Generation, leaseId),
                update,
                new NativeSharedReductionViewV1(
                    _authority, _streamHeaders, _records, _payload, _sortEntries,
                    _stagedValues, _changedSlots, _values, _versions, _revision,
                    update.UpdateId, update.Count));
            failure = default;
            return true;
        }

        public bool TryRegisterReductionDependency(
            NativeSharedReductionLeaseV1 lease,
            JobHandle dependency,
            out NativeRuntimeFailureV1 failure)
        {
            if (!IsReductionLease(lease))
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }
            if (_reductionDependencyRegistered != 0)
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }
            _reductionDependency = dependency;
            _reductionDependencyRegistered = 1;
            failure = default;
            return true;
        }

        public bool TryCompleteReduction(
            NativeSharedReductionLeaseV1 lease,
            out NativeSharedCommitReportV1 report,
            out NativeRuntimeFailureV1 failure)
        {
            report = default;
            if (!IsReductionLease(lease))
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }
            if (_reductionDependencyRegistered == 0 || !_reductionDependency.IsCompleted)
            {
                failure = LiveFailure();
                return false;
            }
            _reductionDependency.Complete();
            var control = _streamHeaders[0];
            var result = control.ReductionResult;
            var failureCode = control.ReductionFailureCode;
            var failureResource = control.ReductionFailureResource;
            var changedCount = control.ChangedSlotCount;
            var sourceUpdateId = _update.UpdateId;
            _activeReductionLeaseId = 0;
            _reductionDependency = default;
            _reductionDependencyRegistered = 0;
            if (_selectionLease.Owner == null)
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }
            if (!_selectionLease.Owner.TryReleaseReadLease(_selectionLease, out failure)) return false;

            if (result == BurstContextResult.Success)
            {
                var reportId = ++_nextReportId;
                _changedSlots[_changedSlots.Length - 2] = (uint)reportId;
                _changedSlots[_changedSlots.Length - 1] = (uint)(reportId >> 32);
                report = new NativeSharedCommitReportV1(
                    OwnerId, Generation, reportId, sourceUpdateId, sourceUpdateId + 1,
                    _revision[0], _changedSlots.AsReadOnly(), changedCount);
                ClearUpdate();
                failure = default;
                return true;
            }
            ClearUpdate();
            failure = new NativeRuntimeFailureV1(
                failureCode == NativeRuntimeDiagnosticCodeV1.None
                    ? NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch : failureCode,
                failureResource == NativeResourceKindV1.None
                    ? NativeResourceKindV1.SharedContributionStreams : failureResource,
                ownerId: OwnerId,
                generation: Generation,
                leaseId: lease.Token.LeaseId);
            return false;
        }

        internal bool IsContributionLeaseActive(NativeSharedContributionLeaseV1 lease)
            => TryGetActiveLease(lease, out _, out _);

        private bool IsReductionLease(NativeSharedReductionLeaseV1 lease)
            => lease.IsValid && ReferenceEquals(lease.Owner, this)
                && lease.Token.OwnerId == OwnerId && lease.Token.Generation == Generation
                && lease.Token.LeaseId == _activeReductionLeaseId
                && IsUpdate(lease.Update);

        private bool IsUpdate(NativeSharedUpdateWindowV1 update)
            => _update.IsValid && update.OwnerId == OwnerId && update.Generation == Generation
                && update.UpdateId == _update.UpdateId && update.Count == _update.Count
                && update.SelectionOwnerId == _update.SelectionOwnerId
                && update.SelectionGeneration == _update.SelectionGeneration
                && update.SelectionWindowId == _update.SelectionWindowId
                && _selectionLease.IsValid && _selectionLease.Owner.IsLeaseActive(_selectionLease);

        private bool TryGetActiveLease(
            NativeSharedContributionLeaseV1 lease,
            out int streamIndex,
            out NativeSharedContributionStreamV1 stream)
        {
            streamIndex = -1;
            stream = default;
            if (!TryValidateLeaseManaged(lease, out streamIndex)) return false;
            stream = _streamHeaders[streamIndex];
            return stream.State == NativeSharedContributionStreamStateV1.Active
                && stream.ActiveLeaseId == lease.Token.LeaseId
                && lease.Execution.SemanticLease.Owner.IsExecutionLeaseActive(lease.Execution);
        }

        private bool TryValidateLeaseManaged(
            NativeSharedContributionLeaseV1 lease,
            out int streamIndex)
        {
            streamIndex = -1;
            if (!lease.IsValid || !ReferenceEquals(lease.Owner, this) || !IsUpdate(lease.Update)
                || lease.Token.OwnerId != OwnerId || lease.Token.Generation != Generation) return false;
            streamIndex = (int)lease.Writer.StreamIndex;
            return streamIndex >= 0 && streamIndex < _update.Count
                && _activeContributionLeaseIds[streamIndex] == lease.Token.LeaseId
                && lease.Execution.SemanticLease.Owner.IsExecutionLeaseActive(lease.Execution);
        }

        private int FindStream(TreeInstanceId id)
        {
            if (!_update.IsValid) return -1;
            for (var index = 0; index < _update.Count; index++)
                if (_streamHeaders[index].OwnerTreeInstanceId == id.Value) return index;
            return -1;
        }

        private void ClearUpdate()
        {
            for (var streamIndex = 0; streamIndex < _update.Count; streamIndex++)
            {
                var stream = _streamHeaders[streamIndex];
                for (var record = 0; record < stream.RecordCapacity; record++)
                    _records[(int)(stream.FirstRecord + record)] = default;
                for (var item = 0; item < stream.PayloadCapacity; item++)
                    _payload[(int)(stream.PayloadOffset + item)] = 0;
                _streamHeaders[streamIndex] = default;
                _streamDependencies[streamIndex] = default;
                _dependencyRegistered[streamIndex] = 0;
                _activeContributionLeaseIds[streamIndex] = 0;
            }
            _selectionLease = default;
            _update = default;
            _activeReductionLeaseId = 0;
            _reductionDependency = default;
            _reductionDependencyRegistered = 0;
        }

        private static bool TryPreflight(
            NativeProgramImageViewV2 program,
            NativeSharedContextCapacityV1 capacity,
            out NativeBlackboardScopeRecordV2 descriptor)
        {
            descriptor = default;
            if (capacity.ValueBytes == 0 || capacity.ValueBytes > int.MaxValue
                || capacity.SlotVersions < program.Slots.Length || capacity.SlotVersions > int.MaxValue
                || capacity.MaximumBindings == 0 || capacity.MaximumBindings > int.MaxValue
                || capacity.MaximumSelectedInstances == 0 || capacity.MaximumSelectedInstances > int.MaxValue
                || capacity.ContributionRecords == 0 || capacity.ContributionRecords > int.MaxValue
                || capacity.ContributionPayloadBytes == 0 || capacity.ContributionPayloadBytes > int.MaxValue
                || !NativeSharedContextCapacityV1.TryFindDescriptor(program, out descriptor)
                || descriptor.RawLayoutLength == 0
                || (ulong)descriptor.RawLayoutOffset + descriptor.RawLayoutLength > (uint)program.ScopeLayoutBytes.Length)
                return false;

            uint sharedSlots = 0;
            for (var index = 0; index < program.Slots.Length; index++)
            {
                var slot = program.Slots[index];
                if (slot.Scope != BlackboardScope.Shared) continue;
                if (slot.ScopeDescriptorIndex >= program.Scopes.Length
                    || program.Scopes[(int)slot.ScopeDescriptorIndex].Scope != BlackboardScope.Shared
                    || slot.ScopeSlotIndex >= descriptor.SlotCount
                    || (ulong)slot.Offset + slot.Size > capacity.ValueBytes
                    || (ulong)slot.DefaultOffset + slot.DefaultSize > (uint)program.Semantic.DefaultValueBlob.Length
                    || slot.DefaultSize != slot.Size
                    || !NativeBlackboardCanonicalV1.IsCanonical(
                        program,
                        slot,
                        program.Semantic.DefaultValueBlob,
                        slot.DefaultOffset))
                    return false;
                sharedSlots++;
            }
            return sharedSlots != 0 && sharedSlots == descriptor.SlotCount;
        }

        private bool Compatible(NativeProgramImageViewV2 candidate)
        {
            if (!NativeSharedContextCapacityV1.TryFindDescriptor(candidate, out var descriptor)
                || descriptor.ContractId != _descriptor.ContractId
                || descriptor.ContractVersion != _descriptor.ContractVersion
                || descriptor.SchemaHash != _descriptor.SchemaHash
                || descriptor.LayoutHash != _descriptor.LayoutHash
                || descriptor.RawLayoutLength != _descriptor.RawLayoutLength
                || (ulong)descriptor.RawLayoutOffset + descriptor.RawLayoutLength > (uint)candidate.ScopeLayoutBytes.Length
                || (ulong)_descriptor.RawLayoutOffset + _descriptor.RawLayoutLength > (uint)_authority.ScopeLayoutBytes.Length)
                return false;
            for (var index = 0; index < descriptor.RawLayoutLength; index++)
                if (candidate.ScopeLayoutBytes[(int)descriptor.RawLayoutOffset + index]
                    != _authority.ScopeLayoutBytes[(int)_descriptor.RawLayoutOffset + index]) return false;

            uint sharedSlots = 0;
            for (var index = 0; index < candidate.Slots.Length; index++)
            {
                var slot = candidate.Slots[index];
                if (slot.Scope != BlackboardScope.Shared) continue;
                sharedSlots++;
                if (!AuthorityHasSlot(slot)) return false;
            }
            if (sharedSlots != _descriptor.SlotCount) return false;

            for (var index = 0; index < candidate.Accesses.Length; index++)
            {
                var access = candidate.Accesses[index];
                if (access.Scope != BlackboardScope.Shared) continue;
                if (access.ResolvedSlotIndex >= candidate.Slots.Length) return false;
                var slot = candidate.Slots[(int)access.ResolvedSlotIndex];
                if (slot.Scope != BlackboardScope.Shared
                    || access.ScopeSlotIndex != slot.ScopeSlotIndex
                    || access.TypeId != slot.TypeId
                    || access.TypeVersion != slot.TypeVersion
                    || access.EnumContractId != slot.EnumContractId
                    || access.RegisteredTypeIndex != slot.RegisteredTypeIndex
                    || access.Reduction != slot.Reduction
                    || !AuthorityHasSlot(slot)) return false;
            }
            return true;
        }

        private bool AuthorityHasSlot(NativeBlackboardSlotBindingV2 candidate)
        {
            for (var index = 0; index < _authority.Slots.Length; index++)
            {
                var slot = _authority.Slots[index];
                if (slot.Scope != BlackboardScope.Shared
                    || slot.ScopeSlotIndex != candidate.ScopeSlotIndex) continue;
                return slot.StableKeyId == candidate.StableKeyId
                    && slot.TypeId == candidate.TypeId
                    && slot.TypeVersion == candidate.TypeVersion
                    && slot.EnumContractId == candidate.EnumContractId
                    && slot.Offset == candidate.Offset
                    && slot.Size == candidate.Size
                    && slot.Alignment == candidate.Alignment
                    && slot.AccessFlags == candidate.AccessFlags
                    && slot.RegisteredTypeIndex == candidate.RegisteredTypeIndex
                    && slot.Reduction == candidate.Reduction;
            }
            return false;
        }

        private static void CopyDefaults(NativeProgramImageViewV2 program, NativeArray<byte> values)
        {
            for (var index = 0; index < program.Slots.Length; index++)
            {
                var slot = program.Slots[index];
                if (slot.Scope != BlackboardScope.Shared) continue;
                for (var item = 0; item < slot.Size; item++)
                    values[(int)slot.Offset + item]
                        = program.Semantic.DefaultValueBlob[(int)slot.DefaultOffset + item];
            }
        }

        private bool DefaultEquals(NativeBlackboardSlotBindingV2 slot)
        {
            for (var index = 0; index < slot.Size; index++)
                if (_values[(int)slot.Offset + index]
                    != _authority.Semantic.DefaultValueBlob[(int)slot.DefaultOffset + index]) return false;
            return true;
        }

        private bool HasBindings()
        {
            for (var index = 0; index < _bindings.Length; index++)
                if (_bindings[index].Active) return true;
            return false;
        }

        private bool IsQuiescent()
        {
            for (var index = 0; index < _bindings.Length; index++)
                if (_bindings[index].Active
                    && _bindings[index].Instance.State != NativeOwnerStateV1.Initialized) return false;
            return true;
        }

        private int FindBinding(TreeInstanceId id)
        {
            for (var index = 0; index < _bindings.Length; index++)
                if (_bindings[index].Active && _bindings[index].TreeInstanceId == id) return index;
            return -1;
        }

        private NativeRuntimeFailureV1 LifetimeFailure(ulong leaseId)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                NativeResourceKindV1.SharedBindings,
                ownerId: OwnerId,
                generation: Generation,
                leaseId: leaseId);

        private NativeRuntimeFailureV1 LiveFailure()
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation,
                NativeResourceKindV1.SharedBindings,
                ownerId: OwnerId,
                generation: Generation);

        private static NativeRuntimeFailureV1 RegistryFailure()
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch,
                NativeResourceKindV1.SharedBindings);

        private NativeRuntimeFailureV1 OverflowFailure(NativeResourceKindV1 resource)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                resource,
                ownerId: OwnerId,
                generation: Generation);

        private static NativeArray<T> Allocate<T>(
            uint count,
            Allocator allocator,
            int failAfterSuccessfulAllocations,
            ref int allocations)
            where T : struct
        {
            if (failAfterSuccessfulAllocations >= 0 && allocations >= failAfterSuccessfulAllocations)
                throw new InvalidOperationException("Injected native Shared context allocation failure.");
            var value = new NativeArray<T>((int)count, allocator, NativeArrayOptions.ClearMemory);
            allocations++;
            return value;
        }

        private static void Dispose<T>(ref NativeArray<T> value) where T : struct
        {
            if (!value.IsCreated) return;
            value.Dispose();
            value = default;
        }
    }
}
