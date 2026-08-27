namespace AIBT.Editor.Patching
{
    public enum LayoutDiffTarget
    {
        Node,
        Group,
        Note,
        Reroute,
    }

    public enum LayoutDiffKind
    {
        Added,
        Removed,
        Moved,
        PinChanged,
        Changed,
    }

    public sealed class LayoutDiffEntry
    {
        public LayoutDiffEntry(LayoutDiffTarget target, string key, LayoutDiffKind kind)
        {
            Target = target;
            Key = key;
            Kind = kind;
        }

        public LayoutDiffTarget Target { get; }

        /// <summary>NodeId/GroupId/NoteId value, or "fromNodeId|toNodeId" for a reroute.</summary>
        public string Key { get; }

        public LayoutDiffKind Kind { get; }
    }
}
