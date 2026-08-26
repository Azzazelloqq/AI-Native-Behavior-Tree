using System;
using System.Collections.Generic;

namespace AIBT
{
    /// <summary>
    /// Maps every node in a <see cref="CompiledProgram"/> to its stable authoring <see cref="NodeId"/>
    /// (via <see cref="CompiledProgram.DebugMap"/>), its <see cref="HotReloadNodeIdentitySignature"/>,
    /// and its current compiled index -- the data <c>ADR-P5-001</c>'s hot-reload compatibility model
    /// classifies from. Immutable once built; safe to keep for the lifetime of the
    /// <see cref="CompiledProgram"/> it was built from. Never mutated to track a live instance's own
    /// state -- this type is a read-only view of one compiled program, nothing more.
    /// </summary>
    public sealed class HotReloadProgramIdentityMap
    {
        private readonly Dictionary<NodeId, HotReloadNodeIdentitySignature> _signatures;
        private readonly Dictionary<NodeId, uint> _runtimeIndices;

        private HotReloadProgramIdentityMap(
            Dictionary<NodeId, HotReloadNodeIdentitySignature> signatures,
            Dictionary<NodeId, uint> runtimeIndices)
        {
            _signatures = signatures;
            _runtimeIndices = runtimeIndices;
        }

        /// <summary>Every stable authoring node ID this program's debug map records.</summary>
        public IReadOnlyCollection<NodeId> NodeIds => _signatures.Keys;

        /// <summary>
        /// Builds the map from a compiled program's <see cref="CompiledProgram.DebugMap"/> and
        /// <see cref="CompiledProgram.Nodes"/>. A program with no debug-map entries (none exist
        /// unless a node opted into one) produces an empty, valid map, not an error -- hot reload
        /// cannot classify a node it has no stable identity for, and this is a data fact to
        /// surface at the classification layer (<c>P5-003</c>), not a failure here.
        /// </summary>
        public static HotReloadProgramIdentityMap Build(CompiledProgram program)
        {
            if (program == null)
            {
                throw new ArgumentNullException(nameof(program));
            }

            var signatures = new Dictionary<NodeId, HotReloadNodeIdentitySignature>(program.DebugMap.Count);
            var runtimeIndices = new Dictionary<NodeId, uint>(program.DebugMap.Count);
            foreach (var entry in program.DebugMap)
            {
                var record = program.Nodes[(int)entry.RuntimeNodeIndex];
                signatures[entry.AuthoringNodeId] = new HotReloadNodeIdentitySignature(record);
                runtimeIndices[entry.AuthoringNodeId] = entry.RuntimeNodeIndex;
            }

            return new HotReloadProgramIdentityMap(signatures, runtimeIndices);
        }

        /// <summary>The identity signature for <paramref name="nodeId"/>, if this program has one.</summary>
        public bool TryGetSignature(NodeId nodeId, out HotReloadNodeIdentitySignature signature)
        {
            return _signatures.TryGetValue(nodeId, out signature);
        }

        /// <summary>
        /// The current compiled node index for <paramref name="nodeId"/> in this program, if any.
        /// Callers must never assume this index is stable across a different
        /// <see cref="HotReloadProgramIdentityMap"/> built from a different program -- compiled
        /// index is a fresh pre-order-DFS artifact recomputed on every compile
        /// (<c>ADR-P5-001</c>), and the same <see cref="NodeId"/> almost always resolves to a
        /// different index in the other program.
        /// </summary>
        public bool TryGetRuntimeIndex(NodeId nodeId, out uint runtimeIndex)
        {
            return _runtimeIndices.TryGetValue(nodeId, out runtimeIndex);
        }
    }
}
