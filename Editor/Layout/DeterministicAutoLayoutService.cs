using System;
using System.Collections.Generic;
using AIBT.Authoring;

namespace AIBT.Editor.Layout
{
    /// <summary>
    /// Implements editor-layout-v1.md's deterministic auto-layout contract:
    /// Layout(semanticTree, layoutInputs) -> layoutOutput. Never reads or writes
    /// .aibt.json; consumes an already-loaded <see cref="TreeDocument"/> only.
    /// </summary>
    public static class DeterministicAutoLayoutService
    {
        private const float HorizontalUnit = 160f;
        private const float VerticalUnit = 120f;

        /// <summary>
        /// Computes a deterministic node arrangement for <paramref name="document"/>. Every
        /// node already present in <paramref name="existingLayout"/> keeps its exact recorded
        /// position unchanged (the "scoped re-layout" / "does not reposition previously-placed
        /// nodes" requirement); only nodes absent from it are freshly placed.
        /// </summary>
        public static LayoutDocument Layout(TreeDocument document, LayoutDocument existingLayout = null)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var byId = new Dictionary<NodeId, NodeDocument>();
            foreach (var node in document.Nodes)
            {
                byId[node.Id] = node;
            }

            var freshPositions = new Dictionary<NodeId, LayoutPoint>();
            var visited = new HashSet<NodeId>();
            var nextLeafSlot = 0;

            if (document.Root.IsValid && byId.ContainsKey(document.Root))
            {
                AssignPositions(document.Root, 0, byId, freshPositions, visited, ref nextLeafSlot);
            }

            // Nodes unreachable from the declared root (not expected for a valid tree, but this
            // service must not silently drop them) still get placed, each as its own leaf slot.
            foreach (var node in document.Nodes)
            {
                if (visited.Contains(node.Id))
                {
                    continue;
                }

                AssignPositions(node.Id, 0, byId, freshPositions, visited, ref nextLeafSlot);
            }

            var placements = new Dictionary<NodeId, LayoutNodePlacement>();
            foreach (var node in document.Nodes)
            {
                if (existingLayout != null && existingLayout.Nodes.TryGetValue(node.Id, out var existing))
                {
                    placements[node.Id] = existing;
                }
                else
                {
                    placements[node.Id] = new LayoutNodePlacement(freshPositions[node.Id]);
                }
            }

            var direction = existingLayout?.Direction ?? LayoutDirection.TopToBottom;

            // Auto-layout only ever (re)computes node positions; groups, notes, and reroutes
            // are user-authored (P3-005) and must pass through unchanged.
            return new LayoutDocument(
                document.TreeId,
                direction,
                placements,
                existingLayout?.Groups,
                existingLayout?.Notes,
                existingLayout?.Reroutes);
        }

        public static byte[] LayoutToBytes(TreeDocument document, LayoutDocument existingLayout = null)
        {
            return CanonicalLayoutJsonWriter.Write(Layout(document, existingLayout));
        }

        /// <summary>
        /// Post-order tidy-tree placement: a leaf gets the next sequential horizontal slot in
        /// semantic (traversal) order; an internal node is centered over its children's x. Depth
        /// (y) is fixed by distance from the traversal root. Sibling subtrees never share a leaf
        /// slot, so this cannot produce two nodes at the exact same position for a connected tree.
        /// </summary>
        private static LayoutPoint AssignPositions(
            NodeId id,
            int depth,
            Dictionary<NodeId, NodeDocument> byId,
            Dictionary<NodeId, LayoutPoint> positions,
            HashSet<NodeId> visited,
            ref int nextLeafSlot)
        {
            if (!visited.Add(id))
            {
                // Defensive only: a well-formed tree has no shared/cyclic children. Return
                // whatever was already computed rather than recursing forever.
                return positions.TryGetValue(id, out var already) ? already : default;
            }

            var y = depth * VerticalUnit;

            if (!byId.TryGetValue(id, out var node) || node.Children.Count == 0)
            {
                var leafPoint = new LayoutPoint(nextLeafSlot * HorizontalUnit, y);
                nextLeafSlot++;
                positions[id] = leafPoint;
                return leafPoint;
            }

            var sumX = 0f;
            for (var index = 0; index < node.Children.Count; index++)
            {
                sumX += AssignPositions(node.Children[index], depth + 1, byId, positions, visited, ref nextLeafSlot).X;
            }

            var point = new LayoutPoint(sumX / node.Children.Count, y);
            positions[id] = point;
            return point;
        }
    }
}
