using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Editor.Layout
{
    /// <summary>Direction values from editor-layout-v1.md's "direction" header field.</summary>
    public enum LayoutDirection
    {
        TopToBottom,
        LeftToRight,
    }

    /// <summary>A Float32 canvas point, per editor-layout-v1.md's Float2 position/size fields.</summary>
    public readonly struct LayoutPoint : IEquatable<LayoutPoint>
    {
        public LayoutPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }

        public float Y { get; }

        public bool Equals(LayoutPoint other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object obj) => obj is LayoutPoint other && Equals(other);

        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 16);
    }

    /// <summary>One entry of editor-layout-v1.md's "nodes" map.</summary>
    public sealed class LayoutNodePlacement
    {
        public LayoutNodePlacement(LayoutPoint position, bool pinned = false)
        {
            Position = position;
            Pinned = pinned;
        }

        public LayoutPoint Position { get; }

        public bool Pinned { get; }
    }

    /// <summary>One entry of editor-layout-v1.md's "groups" map.</summary>
    public sealed class LayoutGroup
    {
        public LayoutGroup(string title, IEnumerable<NodeId> memberNodeIds, string description = null, string color = null, bool locked = false)
        {
            if (string.IsNullOrEmpty(title))
            {
                throw new ArgumentException("A group requires a non-empty title.", nameof(title));
            }

            Title = title;
            Description = description;
            Color = color;
            Locked = locked;
            MemberNodeIds = new List<NodeId>(memberNodeIds ?? throw new ArgumentNullException(nameof(memberNodeIds))).AsReadOnly();
        }

        public string Title { get; }

        public string Description { get; }

        public string Color { get; }

        public bool Locked { get; }

        public IReadOnlyList<NodeId> MemberNodeIds { get; }
    }

    /// <summary>One entry of editor-layout-v1.md's "notes" map.</summary>
    public sealed class LayoutNote
    {
        public LayoutNote(string text, LayoutPoint position, LayoutPoint size, string color = null)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Position = position;
            Size = size;
            Color = color;
        }

        public string Text { get; }

        public LayoutPoint Position { get; }

        public LayoutPoint Size { get; }

        public string Color { get; }
    }

    /// <summary>The key of editor-layout-v1.md's "reroutes" map: an edge identity, "fromNodeId|toNodeId".</summary>
    public readonly struct LayoutEdgeKey : IEquatable<LayoutEdgeKey>
    {
        public LayoutEdgeKey(NodeId fromNodeId, NodeId toNodeId)
        {
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
        }

        public NodeId FromNodeId { get; }

        public NodeId ToNodeId { get; }

        public string ToKeyString() => FromNodeId.Value + "|" + ToNodeId.Value;

        public bool Equals(LayoutEdgeKey other) => FromNodeId == other.FromNodeId && ToNodeId == other.ToNodeId;

        public override bool Equals(object obj) => obj is LayoutEdgeKey other && Equals(other);

        public override int GetHashCode() => FromNodeId.GetHashCode() ^ (ToNodeId.GetHashCode() << 16);
    }

    /// <summary>One entry of editor-layout-v1.md's "reroutes" map.</summary>
    public sealed class LayoutReroute
    {
        public LayoutReroute(IEnumerable<LayoutPoint> waypoints)
        {
            Waypoints = new List<LayoutPoint>(waypoints ?? throw new ArgumentNullException(nameof(waypoints))).AsReadOnly();
            if (Waypoints.Count == 0)
            {
                throw new ArgumentException("A reroute requires at least one waypoint.", nameof(waypoints));
            }
        }

        public IReadOnlyList<LayoutPoint> Waypoints { get; }
    }

    /// <summary>editor-layout-v1.md's GroupId/NoteId identity rule: the same authoring-identity grammar as NodeId.</summary>
    public static class LayoutIdentity
    {
        public static bool IsValid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128 || !IsFirstCharacter(value[0]))
            {
                return false;
            }

            for (var index = 1; index < value.Length; index++)
            {
                if (!IsSubsequentCharacter(value[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFirstCharacter(char value)
        {
            return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z') || (value >= '0' && value <= '9');
        }

        private static bool IsSubsequentCharacter(char value)
        {
            return IsFirstCharacter(value) || value == '.' || value == '_' || value == ':' || value == '-';
        }
    }

    /// <summary>
    /// In-memory model of a *.aibt.layout.json document: node positions/pinning (P3-004),
    /// plus groups, sticky notes, and edge reroutes (P3-005) -- all user-authored,
    /// presentation-only data, per editor-layout-v1.md.
    /// </summary>
    public sealed class LayoutDocument
    {
        public const string CurrentFormat = "aibt.layout";
        public const int CurrentFormatVersion = 1;

        private readonly ReadOnlyDictionary<NodeId, LayoutNodePlacement> _nodes;
        private readonly ReadOnlyDictionary<string, LayoutGroup> _groups;
        private readonly ReadOnlyDictionary<string, LayoutNote> _notes;
        private readonly ReadOnlyDictionary<LayoutEdgeKey, LayoutReroute> _reroutes;

        public LayoutDocument(
            TreeId treeId,
            LayoutDirection direction,
            IReadOnlyDictionary<NodeId, LayoutNodePlacement> nodes,
            IReadOnlyDictionary<string, LayoutGroup> groups = null,
            IReadOnlyDictionary<string, LayoutNote> notes = null,
            IReadOnlyDictionary<LayoutEdgeKey, LayoutReroute> reroutes = null)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            TreeId = treeId;
            Direction = direction;
            _nodes = new ReadOnlyDictionary<NodeId, LayoutNodePlacement>(new Dictionary<NodeId, LayoutNodePlacement>(nodes));
            _groups = new ReadOnlyDictionary<string, LayoutGroup>(new Dictionary<string, LayoutGroup>(groups ?? EmptyGroups, StringComparer.Ordinal));
            _notes = new ReadOnlyDictionary<string, LayoutNote>(new Dictionary<string, LayoutNote>(notes ?? EmptyNotes, StringComparer.Ordinal));
            _reroutes = new ReadOnlyDictionary<LayoutEdgeKey, LayoutReroute>(new Dictionary<LayoutEdgeKey, LayoutReroute>(reroutes ?? EmptyReroutes));
        }

        private static readonly Dictionary<string, LayoutGroup> EmptyGroups = new Dictionary<string, LayoutGroup>();
        private static readonly Dictionary<string, LayoutNote> EmptyNotes = new Dictionary<string, LayoutNote>();
        private static readonly Dictionary<LayoutEdgeKey, LayoutReroute> EmptyReroutes = new Dictionary<LayoutEdgeKey, LayoutReroute>();

        public TreeId TreeId { get; }

        public LayoutDirection Direction { get; }

        public IReadOnlyDictionary<NodeId, LayoutNodePlacement> Nodes => _nodes;

        public IReadOnlyDictionary<string, LayoutGroup> Groups => _groups;

        public IReadOnlyDictionary<string, LayoutNote> Notes => _notes;

        public IReadOnlyDictionary<LayoutEdgeKey, LayoutReroute> Reroutes => _reroutes;
    }
}
