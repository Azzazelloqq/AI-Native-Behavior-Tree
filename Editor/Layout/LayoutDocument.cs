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

    /// <summary>
    /// In-memory model of a *.aibt.layout.json document, scoped to what
    /// P3-004's auto-layout service produces (the "nodes" map only -- groups,
    /// notes, and reroutes are user-authored, owned by P3-005).
    /// </summary>
    public sealed class LayoutDocument
    {
        public const string CurrentFormat = "aibt.layout";
        public const int CurrentFormatVersion = 1;

        private readonly ReadOnlyDictionary<NodeId, LayoutNodePlacement> _nodes;

        public LayoutDocument(TreeId treeId, LayoutDirection direction, IReadOnlyDictionary<NodeId, LayoutNodePlacement> nodes)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            TreeId = treeId;
            Direction = direction;
            _nodes = new ReadOnlyDictionary<NodeId, LayoutNodePlacement>(new Dictionary<NodeId, LayoutNodePlacement>(nodes));
        }

        public TreeId TreeId { get; }

        public LayoutDirection Direction { get; }

        public IReadOnlyDictionary<NodeId, LayoutNodePlacement> Nodes => _nodes;
    }
}
