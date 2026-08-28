using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;

namespace AIBT.Mcp.Authoring
{
    /// <summary>
    /// Pure <c>TreeDocument -&gt; TreeDocument</c> operations this card's tools need beyond what
    /// <see cref="SemanticEditOperations"/> (P3-006, <c>Editor/Editing/</c>, outside this card's
    /// Allowed-changes fence) already provides: move, replace, blackboard-key declaration, and
    /// subtree extract/inline. <see cref="AIBT.Editor.Editing.SemanticEditTransaction.Apply"/>
    /// accepts any <c>Func&lt;TreeDocument,TreeDocument&gt;</c>, not only ones defined in
    /// <c>SemanticEditOperations</c> -- so these are defined here instead, following that type's
    /// exact pure-copy contract: every function here returns a *new* <see cref="TreeDocument"/>
    /// and never mutates its input, matching <c>SemanticEditOperations.Rebuild</c>'s pattern.
    /// This is load-bearing: <c>TreeDocument</c> also exposes legacy mutating instance methods
    /// (<c>SetBlackboard</c>, instance <c>AddNode</c>, <c>ReplaceNodeAt</c>, <c>RemoveNodeAt</c>)
    /// that would silently corrupt a *rejected* patch's "unchanged" document if used here instead
    /// -- deliberately never called by this class.
    /// </summary>
    internal static class McpAuthoringOperations
    {
        /// <summary>Moves a node under a new parent, resolving its current parent automatically (a node has exactly one).</summary>
        internal static TreeDocument Move(TreeDocument document, NodeId nodeId, NodeId newParentId, int? insertIndex = null)
        {
            var currentParent = FindParent(document, nodeId)
                ?? throw new ArgumentException($"Node '{nodeId}' has no current parent (cannot move the root).", nameof(nodeId));

            var disconnected = SemanticEditOperations.Disconnect(document, currentParent.Id, nodeId);
            return SemanticEditOperations.Connect(disconnected, newParentId, nodeId, insertIndex);
        }

        /// <summary>
        /// Replaces a node's type/version/parameters in place, keeping its <see cref="NodeId"/>
        /// and <see cref="NodeDocument.Children"/> -- and therefore its identity for layout and
        /// hot-reload's ID-keyed model. A caller wanting a full subtree swap composes remove+add
        /// in one <c>apply_domain_patch</c> call instead.
        /// </summary>
        internal static TreeDocument Replace(TreeDocument document, NodeId nodeId, string newTypeId, int newTypeVersion, SemanticObject newParameters)
        {
            var node = FindNode(document, nodeId)
                ?? throw new ArgumentException($"Node '{nodeId}' does not exist.", nameof(nodeId));

            var replaced = node.WithType(newTypeId, newTypeVersion).WithParameters(newParameters ?? SemanticObject.Empty);
            var nodes = new List<NodeDocument>(document.Nodes);
            ReplaceInList(nodes, replaced);
            return Rebuild(document, nodes: nodes);
        }

        /// <summary>Full-replacement declare/change of the tree's blackboard keys.</summary>
        internal static TreeDocument SetBlackboard(TreeDocument document, IReadOnlyList<BlackboardKeyDefinition> keys)
        {
            return Rebuild(document, blackboard: keys);
        }

        internal readonly struct SubtreeCapture
        {
            internal SubtreeCapture(IReadOnlyList<NodeDocument> nodes, NodeId parentId, int insertIndex)
            {
                Nodes = nodes;
                ParentId = parentId;
                InsertIndex = insertIndex;
            }

            /// <summary>The captured subtree's nodes, root first, in the original document's order.</summary>
            internal IReadOnlyList<NodeDocument> Nodes { get; }

            internal NodeId ParentId { get; }

            internal int InsertIndex { get; }
        }

        /// <summary>Pure read: captures a subtree's node definitions and attachment point before removal (no mutation).</summary>
        internal static SubtreeCapture CaptureSubtree(TreeDocument document, NodeId subtreeRootId)
        {
            var parent = FindParent(document, subtreeRootId)
                ?? throw new ArgumentException($"Node '{subtreeRootId}' has no parent (cannot extract the root).", nameof(subtreeRootId));

            var insertIndex = parent.Children.ToList().IndexOf(subtreeRootId);
            var subtreeIds = CollectSubtree(document, subtreeRootId);
            var nodes = document.Nodes.Where(n => subtreeIds.Contains(n.Id)).ToList();
            return new SubtreeCapture(nodes, parent.Id, insertIndex);
        }

        /// <summary>
        /// Reattaches a previously captured (or otherwise supplied) set of nodes as a unit under
        /// <paramref name="parentId"/> -- a generalization of <see cref="SemanticEditOperations.AddNode"/>'s
        /// single-node list-splice to N nodes sharing internal parent/child links already encoded
        /// in their own <see cref="NodeDocument.Children"/>.
        /// </summary>
        internal static TreeDocument AttachSubtree(TreeDocument document, IReadOnlyList<NodeDocument> subtreeNodes, NodeId subtreeRootId, NodeId parentId, int? insertIndex = null)
        {
            if (subtreeNodes == null || subtreeNodes.Count == 0)
            {
                throw new ArgumentException("At least one node is required.", nameof(subtreeNodes));
            }

            foreach (var node in subtreeNodes)
            {
                if (FindNode(document, node.Id) != null)
                {
                    throw new ArgumentException($"A node with id '{node.Id}' already exists.", nameof(subtreeNodes));
                }
            }

            var parent = FindNode(document, parentId)
                ?? throw new ArgumentException($"Parent node '{parentId}' does not exist.", nameof(parentId));

            var nodes = new List<NodeDocument>(document.Nodes);
            nodes.AddRange(subtreeNodes);

            var children = new List<NodeId>(parent.Children);
            var index = insertIndex.HasValue ? Math.Max(0, Math.Min(insertIndex.Value, children.Count)) : children.Count;
            children.Insert(index, subtreeRootId);
            ReplaceInList(nodes, parent.WithChildren(children));

            return Rebuild(document, nodes: nodes);
        }

        private static NodeDocument FindNode(TreeDocument document, NodeId id)
        {
            for (var index = 0; index < document.Nodes.Count; index++)
            {
                if (document.Nodes[index].Id == id)
                {
                    return document.Nodes[index];
                }
            }

            return null;
        }

        private static NodeDocument FindParent(TreeDocument document, NodeId childId)
        {
            for (var index = 0; index < document.Nodes.Count; index++)
            {
                var candidate = document.Nodes[index];
                for (var childIndex = 0; childIndex < candidate.Children.Count; childIndex++)
                {
                    if (candidate.Children[childIndex] == childId)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static HashSet<NodeId> CollectSubtree(TreeDocument document, NodeId rootId)
        {
            var result = new HashSet<NodeId>();

            void Visit(NodeId id)
            {
                if (!result.Add(id))
                {
                    return;
                }

                var node = FindNode(document, id);
                if (node == null)
                {
                    return;
                }

                for (var index = 0; index < node.Children.Count; index++)
                {
                    Visit(node.Children[index]);
                }
            }

            Visit(rootId);
            return result;
        }

        private static void ReplaceInList(List<NodeDocument> nodes, NodeDocument updated)
        {
            for (var index = 0; index < nodes.Count; index++)
            {
                if (nodes[index].Id == updated.Id)
                {
                    nodes[index] = updated;
                    return;
                }
            }
        }

        private static TreeDocument Rebuild(
            TreeDocument source,
            IEnumerable<NodeDocument> nodes = null,
            IEnumerable<BlackboardKeyDefinition> blackboard = null)
        {
            return new TreeDocument(
                source.Format,
                source.FormatVersion,
                source.TreeId,
                source.Name,
                source.Root,
                nodes ?? source.Nodes,
                blackboard ?? source.Blackboard,
                source.Description,
                source.Tags,
                source.Metadata,
                new Revision(source.Revision.Value + 1),
                source.AgentContract,
                source.SharedContract);
        }
    }
}
