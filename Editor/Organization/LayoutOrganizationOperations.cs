using System;
using System.Collections.Generic;
using AIBT.Editor.Layout;

namespace AIBT.Editor.Organization
{
    /// <summary>
    /// Pure, immutable manual-organization operations over a <see cref="LayoutDocument"/>: pin,
    /// group, comment (sticky note), and reroute. Every operation returns a new document; none
    /// ever touches the semantic tree, per editor-and-layout.md's "presentation-only" rule.
    /// </summary>
    public static class LayoutOrganizationOperations
    {
        public static LayoutDocument SetNodePosition(LayoutDocument document, NodeId nodeId, LayoutPoint position)
        {
            var pinned = document.Nodes.TryGetValue(nodeId, out var existing) && existing.Pinned;
            return WithNode(document, nodeId, new LayoutNodePlacement(position, pinned));
        }

        public static LayoutDocument Pin(LayoutDocument document, NodeId nodeId)
        {
            var position = document.Nodes.TryGetValue(nodeId, out var existing) ? existing.Position : default;
            return WithNode(document, nodeId, new LayoutNodePlacement(position, pinned: true));
        }

        public static LayoutDocument Unpin(LayoutDocument document, NodeId nodeId)
        {
            var position = document.Nodes.TryGetValue(nodeId, out var existing) ? existing.Position : default;
            return WithNode(document, nodeId, new LayoutNodePlacement(position, pinned: false));
        }

        public static LayoutDocument AddOrUpdateGroup(
            LayoutDocument document,
            string groupId,
            string title,
            IEnumerable<NodeId> memberNodeIds,
            string description = null,
            string color = null,
            bool locked = false)
        {
            RequireIdentity(groupId, nameof(groupId));
            var members = new List<NodeId>(memberNodeIds ?? throw new ArgumentNullException(nameof(memberNodeIds)));

            foreach (var kvp in document.Groups)
            {
                if (string.Equals(kvp.Key, groupId, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var memberId in members)
                {
                    for (var index = 0; index < kvp.Value.MemberNodeIds.Count; index++)
                    {
                        if (kvp.Value.MemberNodeIds[index] == memberId)
                        {
                            throw new ArgumentException($"Node '{memberId}' already belongs to group '{kvp.Key}'.", nameof(memberNodeIds));
                        }
                    }
                }
            }

            var groups = new Dictionary<string, LayoutGroup>(document.Groups, StringComparer.Ordinal)
            {
                [groupId] = new LayoutGroup(title, members, description, color, locked),
            };

            return Rebuild(document, groups: groups);
        }

        public static LayoutDocument RemoveGroup(LayoutDocument document, string groupId)
        {
            if (!document.Groups.ContainsKey(groupId))
            {
                return document;
            }

            var groups = new Dictionary<string, LayoutGroup>(document.Groups, StringComparer.Ordinal);
            groups.Remove(groupId);
            return Rebuild(document, groups: groups);
        }

        public static LayoutDocument AddOrUpdateNote(
            LayoutDocument document,
            string noteId,
            string text,
            LayoutPoint position,
            LayoutPoint size,
            string color = null)
        {
            RequireIdentity(noteId, nameof(noteId));
            var notes = new Dictionary<string, LayoutNote>(document.Notes, StringComparer.Ordinal)
            {
                [noteId] = new LayoutNote(text, position, size, color),
            };

            return Rebuild(document, notes: notes);
        }

        public static LayoutDocument RemoveNote(LayoutDocument document, string noteId)
        {
            if (!document.Notes.ContainsKey(noteId))
            {
                return document;
            }

            var notes = new Dictionary<string, LayoutNote>(document.Notes, StringComparer.Ordinal);
            notes.Remove(noteId);
            return Rebuild(document, notes: notes);
        }

        public static LayoutDocument AddOrUpdateReroute(LayoutDocument document, NodeId fromNodeId, NodeId toNodeId, IEnumerable<LayoutPoint> waypoints)
        {
            var key = new LayoutEdgeKey(fromNodeId, toNodeId);
            var reroutes = new Dictionary<LayoutEdgeKey, LayoutReroute>(document.Reroutes)
            {
                [key] = new LayoutReroute(waypoints),
            };

            return Rebuild(document, reroutes: reroutes);
        }

        public static LayoutDocument RemoveReroute(LayoutDocument document, NodeId fromNodeId, NodeId toNodeId)
        {
            var key = new LayoutEdgeKey(fromNodeId, toNodeId);
            if (!document.Reroutes.ContainsKey(key))
            {
                return document;
            }

            var reroutes = new Dictionary<LayoutEdgeKey, LayoutReroute>(document.Reroutes);
            reroutes.Remove(key);
            return Rebuild(document, reroutes: reroutes);
        }

        private static LayoutDocument WithNode(LayoutDocument document, NodeId nodeId, LayoutNodePlacement placement)
        {
            var nodes = new Dictionary<NodeId, LayoutNodePlacement>(document.Nodes) { [nodeId] = placement };
            return Rebuild(document, nodes: nodes);
        }

        private static LayoutDocument Rebuild(
            LayoutDocument document,
            IReadOnlyDictionary<NodeId, LayoutNodePlacement> nodes = null,
            IReadOnlyDictionary<string, LayoutGroup> groups = null,
            IReadOnlyDictionary<string, LayoutNote> notes = null,
            IReadOnlyDictionary<LayoutEdgeKey, LayoutReroute> reroutes = null)
        {
            return new LayoutDocument(
                document.TreeId,
                document.Direction,
                nodes ?? document.Nodes,
                groups ?? document.Groups,
                notes ?? document.Notes,
                reroutes ?? document.Reroutes);
        }

        private static void RequireIdentity(string id, string parameterName)
        {
            if (!LayoutIdentity.IsValid(id))
            {
                throw new ArgumentException("Must match ^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$.", parameterName);
            }
        }
    }
}
