using System;

namespace AIBT
{
    [Flags]
    public enum CompiledNodeFlags : uint
    {
        None = 0,
        BurstDomain = 1 << 0,
        ManagedDomain = 1 << 1,
        MainThreadDomain = 1 << 2,
        SupportsTracing = 1 << 3,
    }

    [Flags]
    public enum CompiledBlackboardAccessFlags : byte
    {
        None = 0,
        Read = 1 << 0,
        Write = 1 << 1,
        Observed = 1 << 2,
    }

    public enum CompiledObserverMode : byte
    {
        Self = 1,
        LowerPriority = 2,
        Both = 3,
    }

    public readonly struct CompiledNodeRecord
    {
        private const CompiledNodeFlags ExecutionDomainMask = CompiledNodeFlags.BurstDomain
            | CompiledNodeFlags.ManagedDomain
            | CompiledNodeFlags.MainThreadDomain;
        private const CompiledNodeFlags KnownFlags = ExecutionDomainMask | CompiledNodeFlags.SupportsTracing;

        public CompiledNodeRecord(
            ulong nodeTypeId,
            uint nodeTypeVersion,
            uint configOffset,
            uint configSize,
            uint configAlignment,
            uint instanceMemoryOffset,
            uint instanceMemorySize,
            uint instanceMemoryAlignment,
            NodeMemoryLifetime memoryLifetime,
            CompiledRange children,
            CompiledNodeFlags flags,
            uint debugIdentityIndex,
            CompiledRange readSlots,
            CompiledRange writeSlots)
        {
            if (nodeTypeId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeTypeId));
            }

            if (nodeTypeVersion == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeTypeVersion));
            }

            ValidateStorage(configOffset, configSize, configAlignment, "configuration");
            ValidateMemory(instanceMemoryOffset, instanceMemorySize, instanceMemoryAlignment);
            if (!Enum.IsDefined(typeof(NodeMemoryLifetime), memoryLifetime))
            {
                throw new ArgumentOutOfRangeException(nameof(memoryLifetime));
            }

            var domain = flags & ExecutionDomainMask;
            var domainBits = (uint)domain;
            if ((flags & ~KnownFlags) != 0 || domainBits == 0 || (domainBits & (domainBits - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(flags), "Exactly one known execution domain is required.");
            }

            NodeTypeId = nodeTypeId;
            NodeTypeVersion = nodeTypeVersion;
            ConfigOffset = configOffset;
            ConfigSize = configSize;
            ConfigAlignment = configAlignment;
            InstanceMemoryOffset = instanceMemoryOffset;
            InstanceMemorySize = instanceMemorySize;
            InstanceMemoryAlignment = instanceMemoryAlignment;
            MemoryLifetime = memoryLifetime;
            Children = children;
            Flags = flags;
            DebugIdentityIndex = debugIdentityIndex;
            ReadSlots = readSlots;
            WriteSlots = writeSlots;
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

        public CompiledRange Children { get; }

        public CompiledNodeFlags Flags { get; }

        public uint DebugIdentityIndex { get; }

        public CompiledRange ReadSlots { get; }

        public CompiledRange WriteSlots { get; }

        private static void ValidateMemory(uint offset, uint size, uint alignment)
            => ValidateStorage(offset, size, alignment, "instance memory");

        private static void ValidateStorage(uint offset, uint size, uint alignment, string label)
        {
            if (!IsPowerOfTwo(alignment))
            {
                throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a positive power of two.");
            }

            if (size == 0 && (offset != 0 || alignment != 1))
            {
                throw new ArgumentException("Empty " + label + " must use offset zero and alignment one.");
            }

            if (size != 0 && (offset % alignment != 0 || size % alignment != 0))
            {
                throw new ArgumentException(
                    "The " + label + " offset and size must satisfy the declared alignment.");
            }

            ValidateRange(offset, size, nameof(size));
        }

        private static void ValidateRange(uint offset, uint size, string parameterName)
        {
            if (offset == CompiledIndex.Invalid || (ulong)offset + size > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(parameterName, "The range exceeds the 32-bit address space.");
            }

            if (size == 0 && offset != 0)
            {
                throw new ArgumentException("An empty range must use offset zero.", parameterName);
            }
        }

        private static bool IsPowerOfTwo(uint value) => value != 0 && (value & (value - 1)) == 0;
    }

    public readonly struct CompiledBlackboardSlotRecord
    {
        private const CompiledBlackboardAccessFlags KnownFlags = CompiledBlackboardAccessFlags.Read
            | CompiledBlackboardAccessFlags.Write
            | CompiledBlackboardAccessFlags.Observed;

        public CompiledBlackboardSlotRecord(
            ulong stableKeyId,
            ulong typeId,
            uint typeVersion,
            ulong enumContractId,
            BlackboardScope scope,
            uint offset,
            uint size,
            uint alignment,
            uint defaultValueOffset,
            CompiledBlackboardAccessFlags accessFlags)
        {
            if (stableKeyId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stableKeyId));
            }

            if (typeId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(typeId));
            }

            if (typeVersion == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(typeVersion));
            }

            var isEnum32 = typeId == BuiltInBlackboardTypes.Enum32.TypeId
                && typeVersion == BuiltInBlackboardTypes.Enum32.Version;
            if (isEnum32 ? enumContractId == 0 : enumContractId != 0)
            {
                throw new ArgumentException(
                    "Enum32 slots require a nonzero enum contract ID and all other slots require zero.",
                    nameof(enumContractId));
            }

            if (scope == BlackboardScope.NodeLocal || !Enum.IsDefined(typeof(BlackboardScope), scope))
            {
                throw new ArgumentOutOfRangeException(nameof(scope), "Compiled tree-level slots cannot use NodeLocal or unknown scopes.");
            }

            if (size == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            if (!IsPowerOfTwo(alignment))
            {
                throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a positive power of two.");
            }

            if (offset == CompiledIndex.Invalid || offset % alignment != 0 || size % alignment != 0
                || (ulong)offset + size > uint.MaxValue)
            {
                throw new ArgumentException("The slot memory range must fit 32 bits and satisfy its alignment.");
            }

            if (defaultValueOffset == CompiledIndex.Invalid || defaultValueOffset % alignment != 0
                || (ulong)defaultValueOffset + size > uint.MaxValue)
            {
                throw new ArgumentException("The default-value range must fit 32 bits and satisfy the slot alignment.");
            }

            if ((accessFlags & ~KnownFlags) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(accessFlags));
            }

            StableKeyId = stableKeyId;
            TypeId = typeId;
            TypeVersion = typeVersion;
            EnumContractId = enumContractId;
            Scope = scope;
            Offset = offset;
            Size = size;
            Alignment = alignment;
            DefaultValueOffset = defaultValueOffset;
            AccessFlags = accessFlags;
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

        private static bool IsPowerOfTwo(uint value) => value != 0 && (value & (value - 1)) == 0;
    }

    public readonly struct CompiledObserverRecord
    {
        public CompiledObserverRecord(
            uint observerNodeIndex,
            uint owningReactiveCompositeIndex,
            CompiledObserverMode mode,
            CompiledRange watchedSlots)
        {
            if (observerNodeIndex == CompiledIndex.Invalid)
            {
                throw new ArgumentOutOfRangeException(nameof(observerNodeIndex));
            }

            if (owningReactiveCompositeIndex == CompiledIndex.Invalid)
            {
                throw new ArgumentOutOfRangeException(nameof(owningReactiveCompositeIndex));
            }

            if (!Enum.IsDefined(typeof(CompiledObserverMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            if (watchedSlots.IsEmpty)
            {
                throw new ArgumentException("An observer must watch at least one slot.", nameof(watchedSlots));
            }

            ObserverNodeIndex = observerNodeIndex;
            OwningReactiveCompositeIndex = owningReactiveCompositeIndex;
            Mode = mode;
            WatchedSlots = watchedSlots;
        }

        public uint ObserverNodeIndex { get; }

        public uint OwningReactiveCompositeIndex { get; }

        public CompiledObserverMode Mode { get; }

        public CompiledRange WatchedSlots { get; }

    }

    public readonly struct CompiledDebugMapEntry
    {
        public CompiledDebugMapEntry(
            uint runtimeNodeIndex,
            NodeId authoringNodeId,
            string sourcePath,
            string displayName = null)
        {
            if (runtimeNodeIndex == CompiledIndex.Invalid)
            {
                throw new ArgumentOutOfRangeException(nameof(runtimeNodeIndex));
            }

            if (!authoringNodeId.IsValid)
            {
                throw new ArgumentException("A valid authoring node ID is required.", nameof(authoringNodeId));
            }

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException("A source path is required.", nameof(sourcePath));
            }

            RuntimeNodeIndex = runtimeNodeIndex;
            AuthoringNodeId = authoringNodeId;
            SourcePath = sourcePath;
            DisplayName = displayName;
        }

        public uint RuntimeNodeIndex { get; }

        public NodeId AuthoringNodeId { get; }

        public string SourcePath { get; }

        public string DisplayName { get; }
    }
}
