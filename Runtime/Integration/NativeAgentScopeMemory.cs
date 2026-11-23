using System;

namespace AIBT
{
    /// <summary>
    /// P11-013 (ADR-P11-013): the Agent-scope values and slot versions a <see cref="NativeTreeAgent"/>
    /// carries from one run's host to the next, with the Agent contract they belong to. Filled from an idle
    /// host when a run ends and copied into the next host after its bootstrap and before its first update,
    /// on the main thread. The buffers are reused; they grow only when a larger contract is captured.
    /// Bytes are canonical slot bytes relative to the start of the Agent range, versions are indexed by
    /// scope-local slot index, so a tree with other Tree keys (another Agent base offset) still matches.
    /// </summary>
    internal sealed class NativeAgentScopeMemory
    {
        private byte[] _values = Array.Empty<byte>();
        private ulong[] _versions = Array.Empty<ulong>();

        /// <summary>True when values were captured and not reset since.</summary>
        internal bool HasValues { get; private set; }

        internal ulong ContractNumericId { get; private set; }
        internal uint ContractVersion { get; private set; }
        internal CompiledHash SchemaHash { get; private set; }
        internal CompiledHash LayoutHash { get; private set; }
        internal int ValueCount { get; private set; }
        internal int SlotCount { get; private set; }

        internal byte[] Values => _values;
        internal ulong[] Versions => _versions;

        /// <summary>Forgets the values: the next run starts from the Agent defaults.</summary>
        internal void Reset()
        {
            HasValues = false;
            ValueCount = 0;
            SlotCount = 0;
        }

        /// <summary>Sizes the buffers for <paramref name="scope"/> and records its identity; the caller fills them.</summary>
        internal void BeginCapture(NativeBlackboardScopeBindingV2 scope, int valueCount)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (valueCount < 0 || scope.SlotCount > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(valueCount));
            if (_values.Length < valueCount) _values = new byte[valueCount];
            if (_versions.Length < (int)scope.SlotCount) _versions = new ulong[scope.SlotCount];
            ContractNumericId = scope.ContractNumericId;
            ContractVersion = scope.ContractVersion;
            SchemaHash = scope.SchemaHash;
            LayoutHash = scope.LayoutHash;
            ValueCount = valueCount;
            SlotCount = (int)scope.SlotCount;
            Array.Clear(_versions, 0, SlotCount);
            HasValues = false;
        }

        internal void EndCapture() => HasValues = true;

        /// <summary>
        /// True when <paramref name="scope"/> is the captured contract: the exact contract id, version,
        /// schema hash and layout hash (agent-shared-blackboard-v1, "Agent context"), and the same range size
        /// and slot count.
        /// </summary>
        internal bool Matches(NativeBlackboardScopeBindingV2 scope, int valueCount)
            => HasValues && scope != null
               && scope.ContractNumericId == ContractNumericId && scope.ContractVersion == ContractVersion
               && scope.SchemaHash.Equals(SchemaHash) && scope.LayoutHash.Equals(LayoutHash)
               && valueCount == ValueCount && scope.SlotCount == (uint)SlotCount;
    }
}
