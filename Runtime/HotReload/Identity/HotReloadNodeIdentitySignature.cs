namespace AIBT
{
    /// <summary>
    /// The per-node facts <c>Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md</c>
    /// classifies a node's hot-reload compatibility from: its compiled node type identity and the
    /// instance-memory layout that identity implies. Computed directly from an existing
    /// <see cref="CompiledNodeRecord"/> -- no new field is added to the accepted
    /// <c>compiled-program-v1.md</c> format.
    /// </summary>
    public readonly struct HotReloadNodeIdentitySignature
    {
        internal HotReloadNodeIdentitySignature(CompiledNodeRecord record)
        {
            TypeId = record.NodeTypeId;
            TypeVersion = record.NodeTypeVersion;
            InstanceMemorySize = record.InstanceMemorySize;
            InstanceMemoryAlignment = record.InstanceMemoryAlignment;
            Lifetime = record.MemoryLifetime;
        }

        /// <summary>The compiled node's numeric type ID (<see cref="CompiledNodeRecord.NodeTypeId"/>).</summary>
        public ulong TypeId { get; }

        /// <summary>The compiled node's type version (<see cref="CompiledNodeRecord.NodeTypeVersion"/>).</summary>
        public uint TypeVersion { get; }

        /// <summary>The compiled node's per-instance memory size in bytes.</summary>
        public uint InstanceMemorySize { get; }

        /// <summary>The compiled node's per-instance memory alignment in bytes.</summary>
        public uint InstanceMemoryAlignment { get; }

        /// <summary>The compiled node's declared instance-memory lifetime.</summary>
        public NodeMemoryLifetime Lifetime { get; }

        /// <summary>
        /// <c>true</c> when both signatures name the same node type at the same version. A
        /// <c>false</c> result is <c>ADR-P5-001</c>'s "type or version changed" verdict --
        /// always incompatible, regardless of layout.
        /// </summary>
        public bool HasSameTypeAndVersion(HotReloadNodeIdentitySignature other)
        {
            return TypeId == other.TypeId && TypeVersion == other.TypeVersion;
        }

        /// <summary>
        /// <c>true</c> when both signatures describe the same per-instance memory shape (size,
        /// alignment, lifetime). Only meaningful to check once <see cref="HasSameTypeAndVersion"/>
        /// is already <c>true</c> -- a correctly authored node manifest derives layout
        /// deterministically from type and version, so this should never disagree when type and
        /// version match; if it ever does, <c>ADR-P5-001</c> requires treating the mismatch as
        /// incompatible, not proceeding.
        /// </summary>
        public bool HasCompatibleLayout(HotReloadNodeIdentitySignature other)
        {
            return InstanceMemorySize == other.InstanceMemorySize
                && InstanceMemoryAlignment == other.InstanceMemoryAlignment
                && Lifetime == other.Lifetime;
        }
    }
}
