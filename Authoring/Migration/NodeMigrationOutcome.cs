using System.Collections.Generic;

namespace AIBT.Authoring.Migration
{
    /// <summary>One field-level change a migration rule hop applied, for diagnostic reporting.</summary>
    public sealed class NodeMigrationChange
    {
        public NodeMigrationChange(string description)
        {
            Description = description;
        }

        public string Description { get; }
    }

    /// <summary>
    /// Records that one node in a document was migrated from <see cref="FromVersion"/> to
    /// <see cref="ToVersion"/>, and exactly what changed -- the shape <c>ADR-P7-005</c>'s proposed
    /// <c>AIBT2042</c> diagnostic and the Editor/MCP persist surfaces both consume.
    /// </summary>
    public sealed class NodeMigrationOutcome
    {
        public NodeMigrationOutcome(
            TreeId treeId, NodeId nodeId, string typeId, uint fromVersion, uint toVersion,
            IReadOnlyList<NodeMigrationChange> changes)
        {
            TreeId = treeId;
            NodeId = nodeId;
            TypeId = typeId;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            Changes = changes;
        }

        public TreeId TreeId { get; }
        public NodeId NodeId { get; }
        public string TypeId { get; }
        public uint FromVersion { get; }
        public uint ToVersion { get; }
        public IReadOnlyList<NodeMigrationChange> Changes { get; }
    }
}
