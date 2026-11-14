using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;

namespace AIBT.Editor.Graph
{
    /// <summary>
    /// Graph-editor edits composed over <see cref="SemanticEditOperations"/>. Pure functions on
    /// immutable documents; child order changes only through explicit reorder operations.
    /// </summary>
    internal static class GraphDocumentEdits
    {
        internal static NodeDocument Find(TreeDocument document, NodeId id) => document.Nodes.FirstOrDefault(node => node.Id == id);

        internal static NodeDocument ParentOf(TreeDocument document, NodeId id) => document.Nodes.FirstOrDefault(node => node.Children.Contains(id));

        internal static HashSet<NodeId> Subtree(TreeDocument document, NodeId id)
        {
            var result = new HashSet<NodeId>();
            var pending = new Stack<NodeId>();
            pending.Push(id);
            while (pending.Count > 0)
            {
                var next = pending.Pop();
                if (!result.Add(next)) continue;
                var node = Find(document, next);
                if (node != null) foreach (var child in node.Children) pending.Push(child);
            }

            return result;
        }

        internal static NodeDocument CreateNode(NodeManifest manifest)
            => new NodeDocument(NewId(), manifest.TypeId, checked((int)manifest.Version), parameters: SemanticObject.Empty, tags: TagSet.Empty);

        internal static NodeId NewId() => new NodeId("node-" + Guid.NewGuid().ToString("N").Substring(0, 12));

        /// <summary>Adds a node that has no parent yet; it is reported by validation until connected.</summary>
        internal static TreeDocument AddDetachedNode(TreeDocument document, NodeDocument node)
        {
            if (Find(document, node.Id) != null) throw new ArgumentException($"A node with id '{node.Id}' already exists.", nameof(node));
            return Rebuild(document, document.Nodes.Concat(new[] { node }));
        }

        /// <summary>Moves a child one place earlier (negative) or later (positive) among its siblings.</summary>
        internal static bool TryMoveChild(TreeDocument document, NodeId childId, int delta, out TreeDocument result)
        {
            result = document;
            var parent = ParentOf(document, childId);
            if (parent == null) return false;
            var index = parent.Children.ToList().IndexOf(childId);
            var next = index + delta;
            if (next < 0 || next >= parent.Children.Count) return false;
            result = SemanticEditOperations.Connect(SemanticEditOperations.Disconnect(document, parent.Id, childId), parent.Id, childId, next);
            return true;
        }

        /// <summary>Detaches a node from its parent; the subtree stays in the document.</summary>
        internal static bool TryDetach(TreeDocument document, NodeId childId, out TreeDocument result)
        {
            result = document;
            var parent = ParentOf(document, childId);
            if (parent == null) return false;
            result = SemanticEditOperations.Disconnect(document, parent.Id, childId);
            return true;
        }

        /// <summary>
        /// Copies a node and its subtree with new IDs. The copy is inserted right after the original
        /// under the same parent, or left detached when the original has no parent.
        /// </summary>
        internal static TreeDocument DuplicateSubtree(TreeDocument document, NodeId id, out NodeId copyId)
        {
            var source = Find(document, id) ?? throw new ArgumentException($"Node '{id}' does not exist.", nameof(id));
            var parent = ParentOf(document, id);
            var copies = new List<NodeDocument>();
            copyId = Copy(document, source, copies);
            var copiedRoot = copies[0];
            var remaining = copies.Skip(1).ToList();
            TreeDocument result;
            if (parent == null) result = AddDetachedNode(document, copiedRoot);
            else result = SemanticEditOperations.AddNode(document, copiedRoot, parent.Id, parent.Children.ToList().IndexOf(id) + 1);
            return Rebuild(result, result.Nodes.Concat(remaining));
        }

        internal static TreeDocument SetDisplayName(TreeDocument document, NodeId id, string displayName)
        {
            var node = Find(document, id) ?? throw new ArgumentException($"Node '{id}' does not exist.", nameof(id));
            var value = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            if (value == node.DisplayName) return document;
            var updated = node.WithDetails(value, node.Description, node.Tags);
            return Rebuild(document, document.Nodes.Select(candidate => candidate.Id == id ? updated : candidate));
        }

        private static NodeId Copy(TreeDocument document, NodeDocument source, List<NodeDocument> output)
        {
            var id = NewId();
            var index = output.Count;
            output.Add(null);
            var children = new List<NodeId>();
            foreach (var child in source.Children)
            {
                var node = Find(document, child);
                if (node != null) children.Add(Copy(document, node, output));
            }

            output[index] = new NodeDocument(id, source.TypeId, source.TypeVersion, children, source.Parameters, source.Observer,
                source.DisplayName, source.Description, source.Tags, source.Bindings);
            return id;
        }

        private static TreeDocument Rebuild(TreeDocument source, IEnumerable<NodeDocument> nodes)
            => new TreeDocument(source.Format, source.FormatVersion, source.TreeId, source.Name, source.Root, nodes,
                source.Blackboard, source.Description, source.Tags, source.Metadata, new Revision(source.Revision.Value + 1),
                source.AgentContract, source.SharedContract);
    }
}
