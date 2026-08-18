using AIBT.Burst;
using Unity.Collections;

namespace AIBT
{
    public readonly struct NativeSharedContextCapacityV1
    {
        public NativeSharedContextCapacityV1(
            uint valueBytes,
            uint slotVersions,
            uint maximumBindings,
            uint maximumSelectedInstances,
            uint contributionRecords,
            uint contributionPayloadBytes)
        {
            ValueBytes = valueBytes;
            SlotVersions = slotVersions;
            MaximumBindings = maximumBindings;
            MaximumSelectedInstances = maximumSelectedInstances;
            ContributionRecords = contributionRecords;
            ContributionPayloadBytes = contributionPayloadBytes;
        }

        public uint ValueBytes { get; }
        public uint SlotVersions { get; }
        public uint MaximumBindings { get; }
        public uint MaximumSelectedInstances { get; }
        public uint ContributionRecords { get; }
        public uint ContributionPayloadBytes { get; }

        public static bool TryDerive(
            NativeProgramImageViewV2 program,
            uint maximumBindings,
            uint maximumSelectedInstances,
            uint contributionRecords,
            uint contributionPayloadBytes,
            out NativeSharedContextCapacityV1 capacity,
            out NativeRuntimeFailureV1 failure)
        {
            capacity = default;
            if (!FitsNativeArray(maximumBindings) || !FitsNativeArray(maximumSelectedInstances)
                || !FitsNativeArray(contributionRecords) || !FitsNativeArray(contributionPayloadBytes))
            {
                var requested = Maximum(maximumBindings, maximumSelectedInstances, contributionRecords, contributionPayloadBytes);
                failure = new NativeRuntimeFailureV1(
                    requested > int.MaxValue
                        ? NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow
                        : NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.SharedBindings,
                    requested,
                    int.MaxValue);
                return false;
            }

            if (!TryFindDescriptor(program, out var descriptor))
            {
                failure = RegistryFailure();
                return false;
            }

            ulong valueBytes = 0;
            uint sharedSlots = 0;
            for (var index = 0; index < program.Slots.Length; index++)
            {
                var slot = program.Slots[index];
                if (slot.Scope != BlackboardScope.Shared) continue;
                sharedSlots++;
                var end = (ulong)slot.Offset + slot.Size;
                if (end > valueBytes) valueBytes = end;
            }
            if (sharedSlots == 0 || valueBytes > int.MaxValue
                || descriptor.SlotCount != sharedSlots)
            {
                failure = valueBytes > int.MaxValue
                    ? new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                        NativeResourceKindV1.InstanceSharedBlackboard,
                        valueBytes,
                        int.MaxValue)
                    : RegistryFailure();
                return false;
            }

            capacity = new NativeSharedContextCapacityV1(
                (uint)valueBytes,
                (uint)program.Slots.Length,
                maximumBindings,
                maximumSelectedInstances,
                contributionRecords,
                contributionPayloadBytes);
            failure = default;
            return true;
        }

        private static bool FitsNativeArray(uint value) => value != 0 && value <= int.MaxValue;

        private static ulong Maximum(uint first, uint second, uint third, uint fourth)
        {
            var value = first > second ? first : second;
            value = value > third ? value : third;
            return value > fourth ? value : fourth;
        }

        internal static bool TryFindDescriptor(
            NativeProgramImageViewV2 program,
            out NativeBlackboardScopeRecordV2 descriptor)
        {
            var found = false;
            descriptor = default;
            for (var index = 0; index < program.Scopes.Length; index++)
            {
                if (program.Scopes[index].Scope != BlackboardScope.Shared) continue;
                if (found) return false;
                descriptor = program.Scopes[index];
                found = true;
            }
            return found;
        }

        internal static NativeRuntimeFailureV1 RegistryFailure()
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch,
                NativeResourceKindV1.SharedBindings);
    }

    public readonly struct NativeSharedBindingV1
    {
        internal NativeSharedBindingV1(
            ulong ownerId,
            uint generation,
            ulong bindingId,
            TreeInstanceId treeInstanceId)
        {
            OwnerId = ownerId;
            Generation = generation;
            BindingId = bindingId;
            TreeInstanceId = treeInstanceId;
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong BindingId { get; }
        public TreeInstanceId TreeInstanceId { get; }
        public bool IsValid => OwnerId != 0 && Generation != 0 && BindingId != 0 && TreeInstanceId.IsValid;
    }

    public readonly struct NativeSharedContextViewV1
    {
        internal NativeSharedContextViewV1(
            NativeArray<byte> values,
            NativeArray<ulong> versions,
            NativeArray<ulong> revision)
        {
            MutableValues = values;
            MutableSlotVersions = versions;
            MutableRevision = revision;
        }

        internal NativeArray<byte> MutableValues { get; }
        internal NativeArray<ulong> MutableSlotVersions { get; }
        internal NativeArray<ulong> MutableRevision { get; }
        public NativeArray<byte>.ReadOnly Values => MutableValues.AsReadOnly();
        public NativeArray<ulong>.ReadOnly SlotVersions => MutableSlotVersions.AsReadOnly();
        public NativeArray<ulong>.ReadOnly Revision => MutableRevision.AsReadOnly();
    }

    public readonly struct NativeSharedExecutionViewV1
    {
        internal NativeSharedExecutionViewV1(
            TreeInstanceId treeInstanceId,
            NativeProgramImageViewV2 program,
            NativeSharedContextViewV1 context)
        {
            TreeInstanceId = treeInstanceId;
            Program = program;
            Context = context;
        }

        public TreeInstanceId TreeInstanceId { get; }
        public NativeProgramImageViewV2 Program { get; }
        public NativeSharedContextViewV1 Context { get; }
        public bool IsValid => TreeInstanceId.IsValid
            && Context.Values.IsCreated
            && Context.SlotVersions.IsCreated
            && Context.Revision.IsCreated;
    }

    public readonly struct NativeSharedUpdateWindowV1
    {
        internal NativeSharedUpdateWindowV1(
            ulong ownerId, uint generation, ulong updateId, uint count,
            ulong selectionOwnerId, uint selectionGeneration, ulong selectionWindowId)
        {
            OwnerId = ownerId; Generation = generation; UpdateId = updateId; Count = count;
            SelectionOwnerId = selectionOwnerId; SelectionGeneration = selectionGeneration;
            SelectionWindowId = selectionWindowId;
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong UpdateId { get; }
        public uint Count { get; }
        public ulong SelectionOwnerId { get; }
        public uint SelectionGeneration { get; }
        public ulong SelectionWindowId { get; }
        public bool IsValid => OwnerId != 0 && Generation != 0 && UpdateId != 0 && Count != 0
            && SelectionOwnerId != 0 && SelectionGeneration != 0 && SelectionWindowId != 0;
    }

    internal enum NativeSharedContributionStreamStateV1 : byte
    {
        Empty = 0,
        Reserved = 1,
        Active = 2,
        Sealed = 3,
        Canceled = 4,
    }

    public struct NativeSharedContributionStreamV1
    {
        internal ulong UpdateId;
        internal ulong OwnerTreeInstanceId;
        internal uint FirstRecord;
        internal uint RecordCapacity;
        internal uint RecordCount;
        internal uint PayloadOffset;
        internal uint PayloadCapacity;
        internal uint PayloadCount;
        internal ulong NextSequence;
        internal ulong ActiveLeaseId;
        internal NativeSharedContributionStreamStateV1 State;
        internal byte Valid;
        internal NativeResourceKindV1 FailureResource;
        internal BurstContextResult ReductionResult;
        internal NativeRuntimeDiagnosticCodeV1 ReductionFailureCode;
        internal NativeResourceKindV1 ReductionFailureResource;
        internal uint ChangedSlotCount;

        public TreeInstanceId TreeInstanceId => new TreeInstanceId(OwnerTreeInstanceId);
        public uint Capacity => RecordCapacity;
        public uint Count => RecordCount;
        public uint PayloadByteCapacity => PayloadCapacity;
        public uint PayloadByteCount => PayloadCount;
        public ulong ContributionSequence => NextSequence;
        public bool IsValid => Valid != 0;
    }

    public readonly struct NativeSharedContributionRecordV1
    {
        internal NativeSharedContributionRecordV1(
            uint slotIndex, ulong treeInstanceId, ulong sequence,
            ulong typeId, uint typeVersion, ulong enumContractId, uint registeredTypeIndex,
            uint recordCapacity, uint payloadCapacity, uint payloadOffset, uint payloadLength)
        {
            ScopeSlotIndex = slotIndex; TreeInstanceIdValue = treeInstanceId; Sequence = sequence;
            TypeId = typeId; TypeVersion = typeVersion; EnumContractId = enumContractId;
            RegisteredTypeIndex = registeredTypeIndex; RecordCapacity = recordCapacity;
            PayloadCapacity = payloadCapacity; PayloadOffset = payloadOffset; PayloadLength = payloadLength;
        }

        public uint ScopeSlotIndex { get; }
        internal ulong TreeInstanceIdValue { get; }
        public TreeInstanceId TreeInstanceId => new TreeInstanceId(TreeInstanceIdValue);
        public ulong Sequence { get; }
        public ulong TypeId { get; }
        public uint TypeVersion { get; }
        public ulong EnumContractId { get; }
        public uint RegisteredTypeIndex { get; }
        public uint RecordCapacity { get; }
        public uint PayloadCapacity { get; }
        public uint PayloadOffset { get; }
        public uint PayloadLength { get; }
    }

    public readonly struct NativeSharedContributionWriterV1
    {
        internal NativeSharedContributionWriterV1(
            NativeProgramImageViewV2 program,
            NativeArray<NativeSharedContributionStreamV1> streams,
            NativeArray<NativeSharedContributionRecordV1> records,
            NativeArray<byte> payload,
            uint streamIndex,
            ulong updateId,
            ulong leaseId)
        {
            Program = program; Streams = streams; Records = records; Payload = payload;
            StreamIndex = streamIndex; UpdateId = updateId; LeaseId = leaseId;
        }

        internal NativeProgramImageViewV2 Program { get; }
        internal NativeArray<NativeSharedContributionStreamV1> Streams { get; }
        internal NativeArray<NativeSharedContributionRecordV1> Records { get; }
        internal NativeArray<byte> Payload { get; }
        internal uint StreamIndex { get; }
        internal ulong UpdateId { get; }
        internal ulong LeaseId { get; }
        public bool IsCreated => Streams.IsCreated && Records.IsCreated && Payload.IsCreated
            && StreamIndex < Streams.Length && UpdateId != 0 && LeaseId != 0;
    }

    public readonly struct NativeSharedContributionLeaseV1
    {
        internal NativeSharedContributionLeaseV1(
            NativeSharedContextOwnerV1 owner,
            NativeLeaseTokenV1 token,
            NativeSharedUpdateWindowV1 update,
            TreeInstanceId treeInstanceId,
            NativeInstanceExecutionLeaseV2 execution,
            NativeSharedContributionWriterV1 writer)
        {
            Owner = owner; Token = token; Update = update; TreeInstanceId = treeInstanceId;
            Execution = execution; Writer = writer;
        }

        internal NativeSharedContextOwnerV1 Owner { get; }
        public NativeLeaseTokenV1 Token { get; }
        public NativeSharedUpdateWindowV1 Update { get; }
        public TreeInstanceId TreeInstanceId { get; }
        public NativeInstanceExecutionLeaseV2 Execution { get; }
        public NativeSharedContributionWriterV1 Writer { get; }
        public bool IsValid => Owner != null && Token.IsValid && Update.IsValid
            && TreeInstanceId.IsValid && Execution.IsValid && Writer.IsCreated;
    }

    public readonly struct NativeSharedContributionStreamViewV1
    {
        internal NativeSharedContributionStreamViewV1(
            NativeArray<NativeSharedContributionRecordV1>.ReadOnly records,
            NativeArray<byte>.ReadOnly payload,
            NativeSharedContributionStreamV1 stream)
        { Records = records; Payload = payload; Stream = stream; }

        public NativeArray<NativeSharedContributionRecordV1>.ReadOnly Records { get; }
        public NativeArray<byte>.ReadOnly Payload { get; }
        public NativeSharedContributionStreamV1 Stream { get; }
        public bool IsCreated => Records.IsCreated && Payload.IsCreated && Stream.TreeInstanceId.IsValid;
    }

    public struct NativeSharedReductionViewV1
    {
        internal NativeSharedReductionViewV1(
            NativeProgramImageViewV2 program,
            NativeArray<NativeSharedContributionStreamV1> streams,
            NativeArray<NativeSharedContributionRecordV1> records,
            NativeArray<byte> payload,
            NativeArray<uint> sortEntries,
            NativeArray<byte> stagedValues,
            NativeArray<uint> changedSlots,
            NativeArray<byte> values,
            NativeArray<ulong> versions,
            NativeArray<ulong> revision,
            ulong updateId,
            uint streamCount)
        {
            Program = program; Streams = streams; Records = records; Payload = payload;
            SortEntries = sortEntries; StagedValues = stagedValues; ChangedSlots = changedSlots;
            Values = values; Versions = versions; Revision = revision;
            UpdateId = updateId; StreamCount = streamCount;
        }

        internal NativeProgramImageViewV2 Program;
        internal NativeArray<NativeSharedContributionStreamV1> Streams;
        internal NativeArray<NativeSharedContributionRecordV1> Records;
        internal NativeArray<byte> Payload;
        internal NativeArray<uint> SortEntries;
        internal NativeArray<byte> StagedValues;
        internal NativeArray<uint> ChangedSlots;
        internal NativeArray<byte> Values;
        internal NativeArray<ulong> Versions;
        internal NativeArray<ulong> Revision;
        internal ulong UpdateId;
        internal uint StreamCount;
        public bool IsCreated => Streams.IsCreated && Records.IsCreated && Payload.IsCreated
            && SortEntries.IsCreated && StagedValues.IsCreated && ChangedSlots.IsCreated
            && Values.IsCreated && Versions.IsCreated && Revision.IsCreated
            && UpdateId != 0 && StreamCount != 0;
    }

    public readonly struct NativeSharedReductionLeaseV1
    {
        internal NativeSharedReductionLeaseV1(
            NativeSharedContextOwnerV1 owner,
            NativeLeaseTokenV1 token,
            NativeSharedUpdateWindowV1 update,
            NativeSharedReductionViewV1 view)
        { Owner = owner; Token = token; Update = update; View = view; }

        internal NativeSharedContextOwnerV1 Owner { get; }
        public NativeLeaseTokenV1 Token { get; }
        public NativeSharedUpdateWindowV1 Update { get; }
        public NativeSharedReductionViewV1 View { get; }
        public bool IsValid => Owner != null && Token.IsValid && Update.IsValid && View.IsCreated;
    }

    public readonly struct NativeSharedCommitReportV1
    {
        internal NativeSharedCommitReportV1(
            ulong ownerId,
            uint generation,
            ulong reportId,
            ulong sourceUpdateId,
            ulong eligibleUpdateId,
            ulong revision,
            NativeArray<uint>.ReadOnly changedScopeSlots,
            uint changedSlotCount)
        {
            OwnerId = ownerId; Generation = generation; ReportId = reportId;
            SourceUpdateId = sourceUpdateId; EligibleUpdateId = eligibleUpdateId;
            Revision = revision; ChangedScopeSlots = changedScopeSlots;
            ChangedSlotCount = changedSlotCount;
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong ReportId { get; }
        public ulong SourceUpdateId { get; }
        public ulong EligibleUpdateId { get; }
        public ulong Revision { get; }
        public NativeArray<uint>.ReadOnly ChangedScopeSlots { get; }
        public uint ChangedSlotCount { get; }
        public bool IsValid => OwnerId != 0 && Generation != 0 && ReportId != 0
            && SourceUpdateId != 0 && EligibleUpdateId != 0 && ChangedScopeSlots.IsCreated
            && (ulong)ChangedScopeSlots.Length >= (ulong)ChangedSlotCount + 2
            && ChangedScopeSlots[ChangedScopeSlots.Length - 2] == (uint)ReportId
            && ChangedScopeSlots[ChangedScopeSlots.Length - 1] == (uint)(ReportId >> 32);
    }
}
