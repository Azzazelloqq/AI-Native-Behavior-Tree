using System;
using Unity.Collections;

namespace AIBT
{
    [Flags]
    public enum NativeDiagnosticLocationFlagsV1 : byte
    {
        None = 0,
        TreeInstance = 1 << 0,
        RuntimeNode = 1 << 1,
        DebugIdentity = 1 << 2,
    }

    public enum NativeUpdatePhaseV1 : byte
    {
        CollectInput = 1,
        NormalizeInput = 2,
        SelectWork = 3,
        Execute = 4,
        ReduceSharedWrites = 5,
        PublishCommands = 6,
        ApplyIntegrations = 7,
        PublishTraceAndMetrics = 8,
    }

    public enum NativeDiagnosticResourceKindV1 : byte
    {
        None = 0,
        ProgramRecords,
        ProgramConfigOrDefaultBytes,
        InstanceBytes,
        WorkItems,
        ScratchBytes,
        SnapshotRecords,
        SnapshotPayload,
        CommandRecords,
        CommandPayload,
        SharedContributionRecords,
        SharedContributionPayload,
        CompletionRecords,
        CompletionPayload,
        DiagnosticRecords,
        DiagnosticLocations,
        TraceRecords,
        TracePayload,
        MaximumAlignment,
        LeaseCounter,
    }

    public enum NativeDiagnosticOperationV1 : byte
    {
        None = 0,
        Initialize,
        Acquire,
        Schedule,
        RegisterDependency,
        Release,
        Mutate,
        Reset,
        Dispose,
        Append,
        Merge,
        Project,
    }

    public readonly struct NativeDiagnosticLocationV1 : IEquatable<NativeDiagnosticLocationV1>
    {
        public NativeDiagnosticLocationV1(
            NativeDiagnosticLocationFlagsV1 flags,
            ulong treeInstanceId = 0,
            uint runtimeNodeIndex = CompiledIndex.Invalid,
            uint debugIdentityIndex = CompiledIndex.Invalid)
        {
            Flags = flags;
            TreeInstanceId = treeInstanceId;
            RuntimeNodeIndex = runtimeNodeIndex;
            DebugIdentityIndex = debugIdentityIndex;
        }

        public NativeDiagnosticLocationFlagsV1 Flags { get; }
        public ulong TreeInstanceId { get; }
        public uint RuntimeNodeIndex { get; }
        public uint DebugIdentityIndex { get; }

        public bool IsValid
        {
            get
            {
                const NativeDiagnosticLocationFlagsV1 known = NativeDiagnosticLocationFlagsV1.TreeInstance
                    | NativeDiagnosticLocationFlagsV1.RuntimeNode
                    | NativeDiagnosticLocationFlagsV1.DebugIdentity;
                return (Flags & ~known) == 0
                    && ((Flags & NativeDiagnosticLocationFlagsV1.TreeInstance) == 0) == (TreeInstanceId == 0)
                    && ((Flags & NativeDiagnosticLocationFlagsV1.RuntimeNode) == 0) == (RuntimeNodeIndex == CompiledIndex.Invalid)
                    && ((Flags & NativeDiagnosticLocationFlagsV1.DebugIdentity) == 0) == (DebugIdentityIndex == CompiledIndex.Invalid)
                    && ((Flags & NativeDiagnosticLocationFlagsV1.DebugIdentity) == 0
                        || (Flags & NativeDiagnosticLocationFlagsV1.RuntimeNode) != 0);
            }
        }

        public bool Equals(NativeDiagnosticLocationV1 other)
            => Flags == other.Flags
                && TreeInstanceId == other.TreeInstanceId
                && RuntimeNodeIndex == other.RuntimeNodeIndex
                && DebugIdentityIndex == other.DebugIdentityIndex;

        public override bool Equals(object obj) => obj is NativeDiagnosticLocationV1 other && Equals(other);
        public override int GetHashCode() => (int)Flags ^ TreeInstanceId.GetHashCode() ^ (int)RuntimeNodeIndex ^ (int)DebugIdentityIndex;
        public static bool operator ==(NativeDiagnosticLocationV1 left, NativeDiagnosticLocationV1 right) => left.Equals(right);
        public static bool operator !=(NativeDiagnosticLocationV1 left, NativeDiagnosticLocationV1 right) => !left.Equals(right);
    }

    public enum NativeDiagnosticFieldIdV1 : byte
    {
        None = 0,
        OwnerKind = 1,
        Allocator = 2,
        ResourceKind = 3,
        Requested = 4,
        Capacity = 5,
        Alignment = 6,
        Operation = 7,
        Left = 8,
        Right = 9,
        OwnerId = 10,
        Generation = 11,
        LeaseId = 12,
        OwnerState = 13,
        DroppedCount = 14,
        Custom0 = 32,
        Custom1 = 33,
        Custom2 = 34,
        Custom3 = 35,
    }

    public enum NativeDiagnosticValueKindV1 : byte
    {
        Unsigned = 1,
        Signed = 2,
        Boolean = 3,
        Enum = 4,
        Identity = 5,
    }

    public readonly struct NativeDiagnosticFieldPairV1 : IEquatable<NativeDiagnosticFieldPairV1>
    {
        public NativeDiagnosticFieldPairV1(
            NativeDiagnosticFieldIdV1 fieldId,
            NativeDiagnosticValueKindV1 valueKind,
            ulong value)
        {
            FieldId = fieldId;
            ValueKind = valueKind;
            Value = value;
        }

        public NativeDiagnosticFieldIdV1 FieldId { get; }
        public NativeDiagnosticValueKindV1 ValueKind { get; }
        public ulong Value { get; }

        public bool IsValid
            => FieldId != NativeDiagnosticFieldIdV1.None
                && (byte)ValueKind >= (byte)NativeDiagnosticValueKindV1.Unsigned
                && (byte)ValueKind <= (byte)NativeDiagnosticValueKindV1.Identity;

        public bool Equals(NativeDiagnosticFieldPairV1 other)
            => FieldId == other.FieldId && ValueKind == other.ValueKind && Value == other.Value;

        public override bool Equals(object obj) => obj is NativeDiagnosticFieldPairV1 other && Equals(other);
        public override int GetHashCode() => ((int)FieldId * 397) ^ ((int)ValueKind * 31) ^ Value.GetHashCode();
    }

    public struct NativeDiagnosticRecordV1
    {
        public ushort CodeNumber;
        public DiagnosticSeverity Severity;
        public NativeUpdatePhaseV1 Phase;
        public ulong UpdateId;
        public ulong SnapshotRevision;
        public ulong Sequence;
        public uint WorkerOrdinal;
        public NativeDiagnosticLocationV1 PrimaryLocation;
        public byte FieldCount;
        public NativeDiagnosticFieldPairV1 Field0;
        public NativeDiagnosticFieldPairV1 Field1;
        public NativeDiagnosticFieldPairV1 Field2;
        public NativeDiagnosticFieldPairV1 Field3;
        public NativeDiagnosticFieldPairV1 Field4;
        public NativeDiagnosticFieldPairV1 Field5;
        public NativeDiagnosticFieldPairV1 Field6;
        public NativeDiagnosticFieldPairV1 Field7;
        public uint RelatedLocationOffset;
        public uint RelatedLocationCount;

        public bool HasValidHeader
            => CodeNumber >= 1 && CodeNumber <= 9999
                && (byte)Severity <= (byte)DiagnosticSeverity.Info
                && (byte)Phase >= (byte)NativeUpdatePhaseV1.CollectInput
                && (byte)Phase <= (byte)NativeUpdatePhaseV1.PublishTraceAndMetrics
                && UpdateId != 0
                && Sequence != 0
                && PrimaryLocation.IsValid
                && FieldCount <= 8;

        public NativeDiagnosticFieldPairV1 GetField(int index)
        {
            switch (index)
            {
                case 0: return Field0;
                case 1: return Field1;
                case 2: return Field2;
                case 3: return Field3;
                case 4: return Field4;
                case 5: return Field5;
                case 6: return Field6;
                case 7: return Field7;
                default: return default;
            }
        }
    }

    public readonly struct NativeDiagnosticChannelCapacityV1
    {
        public NativeDiagnosticChannelCapacityV1(uint recordCapacity, uint relatedLocationCapacity)
        {
            RecordCapacity = recordCapacity;
            RelatedLocationCapacity = relatedLocationCapacity;
        }

        public uint RecordCapacity { get; }
        public uint RelatedLocationCapacity { get; }
    }

    public enum NativeDiagnosticAppendResultV1 : byte
    {
        Written = 0,
        InvalidRecord = 1,
        ChannelFaulted = 2,
    }

    public readonly struct NativeDiagnosticChannelFailureV1
    {
        public NativeDiagnosticChannelFailureV1(
            NativeRuntimeDiagnosticCodeV1 code,
            NativeDiagnosticResourceKindV1 resourceKind = NativeDiagnosticResourceKindV1.None,
            ulong requested = 0,
            ulong capacity = 0,
            ulong ownerId = 0,
            uint generation = 0,
            ulong leaseId = 0)
        {
            Code = code;
            ResourceKind = resourceKind;
            Requested = requested;
            Capacity = capacity;
            OwnerId = ownerId;
            Generation = generation;
            LeaseId = leaseId;
        }

        public NativeRuntimeDiagnosticCodeV1 Code { get; }
        public NativeDiagnosticResourceKindV1 ResourceKind { get; }
        public ulong Requested { get; }
        public ulong Capacity { get; }
        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong LeaseId { get; }
        public bool IsSuccess => Code == NativeRuntimeDiagnosticCodeV1.None;
    }

    public readonly struct NativeDiagnosticChannelSnapshotV1
    {
        internal NativeDiagnosticChannelSnapshotV1(
            NativeArray<NativeDiagnosticRecordV1>.ReadOnly records,
            NativeArray<NativeDiagnosticLocationV1>.ReadOnly relatedLocations,
            uint recordCount,
            uint relatedLocationCount,
            bool faulted,
            NativeDiagnosticRecordV1 rejection)
        {
            Records = records;
            RelatedLocations = relatedLocations;
            RecordCount = recordCount;
            RelatedLocationCount = relatedLocationCount;
            IsFaulted = faulted;
            Rejection = rejection;
        }

        public NativeArray<NativeDiagnosticRecordV1>.ReadOnly Records { get; }
        public NativeArray<NativeDiagnosticLocationV1>.ReadOnly RelatedLocations { get; }
        public uint RecordCount { get; }
        public uint RelatedLocationCount { get; }
        public bool IsFaulted { get; }
        public NativeDiagnosticRecordV1 Rejection { get; }
    }
}
