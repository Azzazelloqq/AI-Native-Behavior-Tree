using System;
using System.Threading;
using Unity.Collections;

namespace AIBT
{
    public enum NativeRuntimeDiagnosticCodeV1 : ushort
    {
        None = 0,
        BlackboardInvalidSlot = 4201,
        BlackboardUndeclaredAccess = 4202,
        BlackboardTypeMismatch = 4203,
        BlackboardUnsupportedScope = 4204,
        BlackboardInvalidValue = 4205,
        BlackboardVersionOverflow = 4206,
        BlackboardMissingTypeBinding = 4207,
        BlackboardRegistryMismatch = 4208,
        BlackboardEqualityFault = 4209,
        NativeAllocatorInvalid = 4301,
        NativeCapacityPlanInvalid = 4302,
        NativeCapacityArithmeticOverflow = 4303,
        NativeProgramCapacityExceeded = 4304,
        NativeInstanceCapacityExceeded = 4305,
        NativeSnapshotCapacityExceeded = 4306,
        NativeOutputCapacityExceeded = 4307,
        NativeCompletionCapacityExceeded = 4308,
        NativeDiagnosticCapacityExceeded = 4309,
        NativeTraceCapacityExceeded = 4310,
        NativeLifetimeStateInvalid = 4311,
        NativeLiveJobOwnershipViolation = 4312,
    }

    public enum NativeResourceKindV1 : byte
    {
        None = 0,
        ProgramNodes,
        ProgramChildIndices,
        ProgramReadSlotIndices,
        ProgramWriteSlotIndices,
        ProgramBlackboardSlots,
        ProgramObservers,
        ProgramWatchedSlotIndices,
        ProgramConfigBytes,
        ProgramDefaultBytes,
        ProgramDebugOrdinals,
        ProgramHash,
        MaximumAlignment,
        InstanceNodeMemory,
        InstanceTreeBlackboard,
        InstanceFrames,
        InstanceGenerations,
        InstanceParallelBranches,
        InstanceObservers,
        InstanceUpdateState,
        InstanceBudgetState,
        LeaseCounter,
        ProgramScopeDescriptors,
        ProgramScopeLayoutBytes,
        ProgramBlackboardAccesses,
        ProgramNodeAccessRanges,
        ProgramRegisteredTypes,
        ProgramRegisteredFields,
        InstanceTreeSlotVersions,
        InstanceTreeRevision,
        InstanceAgentBlackboard,
        InstanceAgentSlotVersions,
        InstanceAgentRevision,
        AgentBindings,
        AgentExecuteWindowOwners,
        ExecuteSelectionEntries,
        ExecuteSelectionReaders,
        InstanceSharedBlackboard,
        InstanceSharedSlotVersions,
        InstanceSharedRevision,
        SharedBindings,
        SharedContributionStreams,
        SharedContributionRecords,
        SharedContributionPayload,
        SharedReductionScratch,
        SharedCommitReport,
        InstanceRandomStates,
        InstanceRandomIncrements,
        InstanceRandomNodeIndices,
        LifecycleBatchLanes,
    }

    public enum NativeOwnerStateV1 : byte
    {
        Uninitialized = 0,
        Initialized = 1,
        Executing = 2,
        Disposed = 3,
    }

    public readonly struct NativeRuntimeFailureV1 : IEquatable<NativeRuntimeFailureV1>
    {
        public NativeRuntimeFailureV1(
            NativeRuntimeDiagnosticCodeV1 code,
            NativeResourceKindV1 resourceKind = NativeResourceKindV1.None,
            ulong requested = 0,
            ulong capacity = 0,
            uint alignment = 0,
            ulong ownerId = 0,
            uint generation = 0,
            ulong leaseId = 0)
        {
            Code = code;
            ResourceKind = resourceKind;
            Requested = requested;
            Capacity = capacity;
            Alignment = alignment;
            OwnerId = ownerId;
            Generation = generation;
            LeaseId = leaseId;
        }

        public NativeRuntimeDiagnosticCodeV1 Code { get; }
        public NativeResourceKindV1 ResourceKind { get; }
        public ulong Requested { get; }
        public ulong Capacity { get; }
        public uint Alignment { get; }
        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong LeaseId { get; }
        public bool IsSuccess => Code == NativeRuntimeDiagnosticCodeV1.None;

        public bool Equals(NativeRuntimeFailureV1 other)
            => Code == other.Code
                && ResourceKind == other.ResourceKind
                && Requested == other.Requested
                && Capacity == other.Capacity
                && Alignment == other.Alignment
                && OwnerId == other.OwnerId
                && Generation == other.Generation
                && LeaseId == other.LeaseId;

        public override bool Equals(object obj) => obj is NativeRuntimeFailureV1 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Code;
                hash = (hash * 397) ^ (int)ResourceKind;
                hash = (hash * 397) ^ Requested.GetHashCode();
                hash = (hash * 397) ^ Capacity.GetHashCode();
                hash = (hash * 397) ^ (int)Alignment;
                hash = (hash * 397) ^ OwnerId.GetHashCode();
                hash = (hash * 397) ^ (int)Generation;
                return (hash * 397) ^ LeaseId.GetHashCode();
            }
        }

        public static bool operator ==(NativeRuntimeFailureV1 left, NativeRuntimeFailureV1 right) => left.Equals(right);
        public static bool operator !=(NativeRuntimeFailureV1 left, NativeRuntimeFailureV1 right) => !left.Equals(right);
    }

    public readonly struct NativeLeaseTokenV1 : IEquatable<NativeLeaseTokenV1>
    {
        internal NativeLeaseTokenV1(ulong ownerId, uint generation, ulong leaseId)
        {
            OwnerId = ownerId;
            Generation = generation;
            LeaseId = leaseId;
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong LeaseId { get; }
        public bool IsValid => OwnerId != 0 && Generation != 0 && LeaseId != 0;

        public bool Equals(NativeLeaseTokenV1 other)
            => OwnerId == other.OwnerId && Generation == other.Generation && LeaseId == other.LeaseId;

        public override bool Equals(object obj) => obj is NativeLeaseTokenV1 other && Equals(other);
        public override int GetHashCode() => OwnerId.GetHashCode() ^ (int)Generation ^ LeaseId.GetHashCode();
        public static bool operator ==(NativeLeaseTokenV1 left, NativeLeaseTokenV1 right) => left.Equals(right);
        public static bool operator !=(NativeLeaseTokenV1 left, NativeLeaseTokenV1 right) => !left.Equals(right);
    }

    public readonly struct NativeHash256V1 : IEquatable<NativeHash256V1>
    {
        private readonly ulong _word0;
        private readonly ulong _word1;
        private readonly ulong _word2;
        private readonly ulong _word3;

        public NativeHash256V1(CompiledHash hash)
        {
            if (!hash.IsValid)
            {
                throw new ArgumentException("A canonical SHA-256 hash is required.", nameof(hash));
            }

            _word0 = ParseWord(hash.HexadecimalValue, 0);
            _word1 = ParseWord(hash.HexadecimalValue, 16);
            _word2 = ParseWord(hash.HexadecimalValue, 32);
            _word3 = ParseWord(hash.HexadecimalValue, 48);
        }

        public byte GetByte(int index)
        {
            if ((uint)index >= 32)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var word = index < 8 ? _word0 : index < 16 ? _word1 : index < 24 ? _word2 : _word3;
            var ordinal = index & 7;
            return (byte)(word >> ((7 - ordinal) * 8));
        }

        public bool Equals(NativeHash256V1 other)
            => _word0 == other._word0 && _word1 == other._word1 && _word2 == other._word2 && _word3 == other._word3;

        public override bool Equals(object obj) => obj is NativeHash256V1 other && Equals(other);
        public override int GetHashCode() => _word0.GetHashCode() ^ _word1.GetHashCode() ^ _word2.GetHashCode() ^ _word3.GetHashCode();
        public static bool operator ==(NativeHash256V1 left, NativeHash256V1 right) => left.Equals(right);
        public static bool operator !=(NativeHash256V1 left, NativeHash256V1 right) => !left.Equals(right);

        private static ulong ParseWord(string value, int offset)
        {
            ulong result = 0;
            for (var index = 0; index < 16; index++)
            {
                var character = value[offset + index];
                var nibble = character <= '9' ? character - '0' : character - 'a' + 10;
                result = (result << 4) | (uint)nibble;
            }

            return result;
        }
    }

    public readonly struct NativeCompiledProgramHeaderV1
    {
        internal NativeCompiledProgramHeaderV1(CompiledProgramHeader value)
        {
            Magic = value.Magic;
            CompiledFormatVersion = value.CompiledFormatVersion;
            ExecutionSemanticsVersion = value.ExecutionSemanticsVersion;
            CompilerMajor = value.CompilerVersion.Major;
            CompilerMinor = value.CompilerVersion.Minor;
            CompilerPatch = value.CompilerVersion.Patch;
            CompilerBuildRevision = value.CompilerVersion.BuildRevision;
            CanonicalSemanticHash = new NativeHash256V1(value.CanonicalSemanticHash);
            NodeRegistryHash = new NativeHash256V1(value.NodeRegistryHash);
            CanonicalPolicyHash = new NativeHash256V1(value.CanonicalPolicyHash);
            PolicyFormatVersion = value.PolicyFormatVersion;
            CompiledContentHash = new NativeHash256V1(value.CompiledContentHash);
            RootNodeIndex = value.RootNodeIndex;
            NodeCount = value.NodeCount;
            ChildIndexCount = value.ChildIndexCount;
            BlackboardSlotCount = value.BlackboardSlotCount;
            DebugMapCount = value.DebugMapCount;
            ConfigBlobSize = value.ConfigBlobSize;
            InstanceNodeMemorySize = value.InstanceNodeMemorySize;
            RequiredMaximumAlignment = value.RequiredMaximumAlignment;
            CapabilityFlags = value.CapabilityFlags;
            DeterministicModeCompatible = value.DeterministicModeCompatible ? (byte)1 : (byte)0;
        }

        public uint Magic { get; }
        public uint CompiledFormatVersion { get; }
        public uint ExecutionSemanticsVersion { get; }
        public ushort CompilerMajor { get; }
        public ushort CompilerMinor { get; }
        public ushort CompilerPatch { get; }
        public uint CompilerBuildRevision { get; }
        public NativeHash256V1 CanonicalSemanticHash { get; }
        public NativeHash256V1 NodeRegistryHash { get; }
        public NativeHash256V1 CanonicalPolicyHash { get; }
        public uint PolicyFormatVersion { get; }
        public NativeHash256V1 CompiledContentHash { get; }
        public uint RootNodeIndex { get; }
        public uint NodeCount { get; }
        public uint ChildIndexCount { get; }
        public uint BlackboardSlotCount { get; }
        public uint DebugMapCount { get; }
        public uint ConfigBlobSize { get; }
        public uint InstanceNodeMemorySize { get; }
        public uint RequiredMaximumAlignment { get; }
        public uint CapabilityFlags { get; }
        public byte DeterministicModeCompatible { get; }
    }

    public readonly struct NativeCompiledNodeRecordV1
    {
        internal NativeCompiledNodeRecordV1(CompiledNodeRecord value)
        {
            NodeTypeId = value.NodeTypeId;
            NodeTypeVersion = value.NodeTypeVersion;
            ConfigOffset = value.ConfigOffset;
            ConfigSize = value.ConfigSize;
            ConfigAlignment = value.ConfigAlignment;
            InstanceMemoryOffset = value.InstanceMemoryOffset;
            InstanceMemorySize = value.InstanceMemorySize;
            InstanceMemoryAlignment = value.InstanceMemoryAlignment;
            MemoryLifetime = value.MemoryLifetime;
            ChildOffset = value.Children.Offset;
            ChildCount = value.Children.Count;
            Flags = value.Flags;
            DebugIdentityIndex = value.DebugIdentityIndex;
            ReadSlotOffset = value.ReadSlots.Offset;
            ReadSlotCount = value.ReadSlots.Count;
            WriteSlotOffset = value.WriteSlots.Offset;
            WriteSlotCount = value.WriteSlots.Count;
        }

        public ulong NodeTypeId { get; }
        public uint NodeTypeVersion { get; }
        public uint ConfigOffset { get; }
        public uint ConfigSize { get; }
        public uint ConfigAlignment { get; }
        public uint InstanceMemoryOffset { get; }
        public uint InstanceMemorySize { get; }
        public uint InstanceMemoryAlignment { get; }
        public NodeMemoryLifetime MemoryLifetime { get; }
        public uint ChildOffset { get; }
        public uint ChildCount { get; }
        public CompiledNodeFlags Flags { get; }
        public uint DebugIdentityIndex { get; }
        public uint ReadSlotOffset { get; }
        public uint ReadSlotCount { get; }
        public uint WriteSlotOffset { get; }
        public uint WriteSlotCount { get; }
    }

    public readonly struct NativeCompiledBlackboardSlotRecordV1
    {
        internal NativeCompiledBlackboardSlotRecordV1(CompiledBlackboardSlotRecord value)
        {
            StableKeyId = value.StableKeyId;
            TypeId = value.TypeId;
            TypeVersion = value.TypeVersion;
            EnumContractId = value.EnumContractId;
            Scope = value.Scope;
            Offset = value.Offset;
            Size = value.Size;
            Alignment = value.Alignment;
            DefaultValueOffset = value.DefaultValueOffset;
            AccessFlags = value.AccessFlags;
        }

        public ulong StableKeyId { get; }
        public ulong TypeId { get; }
        public uint TypeVersion { get; }
        public ulong EnumContractId { get; }
        public BlackboardScope Scope { get; }
        public uint Offset { get; }
        public uint Size { get; }
        public uint Alignment { get; }
        public uint DefaultValueOffset { get; }
        public CompiledBlackboardAccessFlags AccessFlags { get; }
    }

    public readonly struct NativeCompiledObserverRecordV1
    {
        internal NativeCompiledObserverRecordV1(CompiledObserverRecord value)
        {
            ObserverNodeIndex = value.ObserverNodeIndex;
            OwningReactiveCompositeIndex = value.OwningReactiveCompositeIndex;
            Mode = value.Mode;
            WatchedSlotOffset = value.WatchedSlots.Offset;
            WatchedSlotCount = value.WatchedSlots.Count;
        }

        public uint ObserverNodeIndex { get; }
        public uint OwningReactiveCompositeIndex { get; }
        public CompiledObserverMode Mode { get; }
        public uint WatchedSlotOffset { get; }
        public uint WatchedSlotCount { get; }
    }

    public readonly struct NativeProgramImageCapacityV1
    {
        public NativeProgramImageCapacityV1(
            uint nodeRecords,
            uint childIndices,
            uint readSlotIndices,
            uint writeSlotIndices,
            uint blackboardSlots,
            uint observers,
            uint watchedSlotIndices,
            uint configBytes,
            uint defaultBytes,
            uint debugOrdinals,
            uint maximumAlignment)
        {
            NodeRecords = nodeRecords;
            ChildIndices = childIndices;
            ReadSlotIndices = readSlotIndices;
            WriteSlotIndices = writeSlotIndices;
            BlackboardSlots = blackboardSlots;
            Observers = observers;
            WatchedSlotIndices = watchedSlotIndices;
            ConfigBytes = configBytes;
            DefaultBytes = defaultBytes;
            DebugOrdinals = debugOrdinals;
            MaximumAlignment = maximumAlignment;
        }

        public uint NodeRecords { get; }
        public uint ChildIndices { get; }
        public uint ReadSlotIndices { get; }
        public uint WriteSlotIndices { get; }
        public uint BlackboardSlots { get; }
        public uint Observers { get; }
        public uint WatchedSlotIndices { get; }
        public uint ConfigBytes { get; }
        public uint DefaultBytes { get; }
        public uint DebugOrdinals { get; }
        public uint MaximumAlignment { get; }

        public static NativeProgramImageCapacityV1 Exact(CompiledProgram program)
        {
            if (program == null)
            {
                throw new ArgumentNullException(nameof(program));
            }

            return new NativeProgramImageCapacityV1(
                (uint)program.Nodes.Count,
                (uint)program.ChildIndices.Count,
                (uint)program.ReadSlotIndices.Count,
                (uint)program.WriteSlotIndices.Count,
                (uint)program.BlackboardSlots.Count,
                (uint)program.Observers.Count,
                (uint)program.WatchedSlotIndices.Count,
                (uint)program.ConfigBlob.Count,
                (uint)program.DefaultValueBlob.Count,
                (uint)program.DebugMap.Count,
                program.Header.RequiredMaximumAlignment);
        }
    }

    public readonly struct NativeProgramImageViewV1
    {
        internal NativeProgramImageViewV1(
            NativeCompiledProgramHeaderV1 header,
            NativeArray<NativeCompiledNodeRecordV1>.ReadOnly nodes,
            NativeArray<uint>.ReadOnly childIndices,
            NativeArray<uint>.ReadOnly readSlotIndices,
            NativeArray<uint>.ReadOnly writeSlotIndices,
            NativeArray<NativeCompiledBlackboardSlotRecordV1>.ReadOnly blackboardSlots,
            NativeArray<NativeCompiledObserverRecordV1>.ReadOnly observers,
            NativeArray<uint>.ReadOnly watchedSlotIndices,
            NativeArray<byte>.ReadOnly configBlob,
            NativeArray<byte>.ReadOnly defaultValueBlob,
            NativeArray<uint>.ReadOnly debugRuntimeNodeIndices)
        {
            Header = header;
            Nodes = nodes;
            ChildIndices = childIndices;
            ReadSlotIndices = readSlotIndices;
            WriteSlotIndices = writeSlotIndices;
            BlackboardSlots = blackboardSlots;
            Observers = observers;
            WatchedSlotIndices = watchedSlotIndices;
            ConfigBlob = configBlob;
            DefaultValueBlob = defaultValueBlob;
            DebugRuntimeNodeIndices = debugRuntimeNodeIndices;
        }

        public NativeCompiledProgramHeaderV1 Header { get; }
        public NativeArray<NativeCompiledNodeRecordV1>.ReadOnly Nodes { get; }
        public NativeArray<uint>.ReadOnly ChildIndices { get; }
        public NativeArray<uint>.ReadOnly ReadSlotIndices { get; }
        public NativeArray<uint>.ReadOnly WriteSlotIndices { get; }
        public NativeArray<NativeCompiledBlackboardSlotRecordV1>.ReadOnly BlackboardSlots { get; }
        public NativeArray<NativeCompiledObserverRecordV1>.ReadOnly Observers { get; }
        public NativeArray<uint>.ReadOnly WatchedSlotIndices { get; }
        public NativeArray<byte>.ReadOnly ConfigBlob { get; }
        public NativeArray<byte>.ReadOnly DefaultValueBlob { get; }
        public NativeArray<uint>.ReadOnly DebugRuntimeNodeIndices { get; }
    }

    internal static class NativeCheckedMathV1
    {
        internal static bool TryAdd(uint left, uint right, NativeResourceKindV1 resource, out uint result, out NativeRuntimeFailureV1 failure)
        {
            var sum = (ulong)left + right;
            if (sum > uint.MaxValue)
            {
                result = 0;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    resource,
                    left,
                    right);
                return false;
            }

            result = (uint)sum;
            failure = default;
            return true;
        }

        internal static bool TryAlignUp(uint value, uint alignment, NativeResourceKindV1 resource, out uint result, out NativeRuntimeFailureV1 failure)
        {
            if (!IsPowerOfTwo(alignment))
            {
                result = 0;
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    resource,
                    value,
                    0,
                    alignment);
                return false;
            }

            if (!TryAdd(value, alignment - 1, resource, out var rounded, out failure))
            {
                result = 0;
                return false;
            }

            result = rounded & ~(alignment - 1);
            return true;
        }

        internal static bool IsPowerOfTwo(uint value) => value != 0 && (value & (value - 1)) == 0;
    }

    internal static class NativeOwnerIdentityV1
    {
        private static long s_nextOwnerId;

        internal static bool TryNext(out ulong ownerId)
        {
            ownerId = unchecked((ulong)Interlocked.Increment(ref s_nextOwnerId));
            return ownerId != 0;
        }
    }
}
