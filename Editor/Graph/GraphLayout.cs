using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Layout;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    /// <summary>
    /// Card-sized placement over <see cref="DeterministicAutoLayoutService"/>: the service's tidy
    /// top-down tree, spread to the graph editor's card spacing. Layout never reorders children.
    /// </summary>
    internal static class GraphLayout
    {
        // The service places leaves 160 x 120 units apart; the editor's cards need more room.
        internal const float ServiceHorizontalUnit = 160f;
        internal const float ServiceVerticalUnit = 120f;
        internal const float HorizontalSpacing = 196f;
        internal const float VerticalSpacing = 130f;

        /// <summary>
        /// Positions for every node: <paramref name="keep"/> entries stay exactly where they are,
        /// every other node gets its tidy-tree position.
        /// </summary>
        internal static Dictionary<NodeId, Vector2> Arrange(TreeDocument document, IReadOnlyDictionary<NodeId, Vector2> keep = null)
        {
            var computed = DeterministicAutoLayoutService.Layout(document);
            var result = new Dictionary<NodeId, Vector2>();
            foreach (var pair in computed.Nodes)
            {
                if (keep != null && keep.TryGetValue(pair.Key, out var kept)) result[pair.Key] = kept;
                else result[pair.Key] = Scale(pair.Value.Position);
            }

            return result;
        }

        /// <summary>A place for a new child under its parent: right of the parent's last other child, one row down.</summary>
        internal static Vector2 PlaceChild(TreeDocument document, NodeId parentId, NodeId childId, IReadOnlyDictionary<NodeId, Vector2> positions)
        {
            var parent = document.Nodes.FirstOrDefault(node => node.Id == parentId);
            if (parent == null || !positions.TryGetValue(parentId, out var parentPosition)) return Vector2.zero;
            var siblings = parent.Children.Where(id => id != childId && positions.ContainsKey(id)).Select(id => positions[id]).ToList();
            if (siblings.Count == 0) return parentPosition + new Vector2(0f, VerticalSpacing);
            var right = siblings.Max(position => position.x);
            var row = siblings.Max(position => position.y);
            return new Vector2(right + HorizontalSpacing, row);
        }

        private static Vector2 Scale(LayoutPoint point)
            => new Vector2(point.X / ServiceHorizontalUnit * HorizontalSpacing, point.Y / ServiceVerticalUnit * VerticalSpacing);
    }
}
