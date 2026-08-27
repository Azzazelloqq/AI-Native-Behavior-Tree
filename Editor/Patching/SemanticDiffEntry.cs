using AIBT.Authoring;

namespace AIBT.Editor.Patching
{
    public enum SemanticDiffKind
    {
        Added,
        Removed,
        Changed,
    }

    public sealed class SemanticDiffEntry
    {
        public SemanticDiffEntry(NodeId nodeId, SemanticDiffKind kind)
        {
            NodeId = nodeId;
            Kind = kind;
        }

        public NodeId NodeId { get; }

        public SemanticDiffKind Kind { get; }
    }
}
