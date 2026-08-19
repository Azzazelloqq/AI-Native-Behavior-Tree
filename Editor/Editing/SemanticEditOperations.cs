using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;

namespace AIBT.Editor.Editing
{
    /// <summary>
    /// Mechanical, pure semantic-tree edits: add/remove node, connect/disconnect, reconfigure a
    /// parameter. Every operation returns a new <see cref="TreeDocument"/>; none validate or
    /// compile -- acceptance is decided uniformly by <see cref="SemanticEditTransaction"/> through
    /// the existing Authoring/compiler pipeline, per editor-and-layout.md's "AI tools modify
    /// semantics through domain operations" rule (human edits use the same operations).
    /// </summary>
    public static class SemanticEditOperations
    {
        public static TreeDocument AddNode(TreeDocument document, NodeDocument newNode, NodeId parentId, int? insertIndex = null)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (newNode == null)
            {
                throw new ArgumentNullException(nameof(newNode));
            }

            if (FindNode(document, newNode.Id) != null)
            {
                throw new ArgumentException($"A node with id '{newNode.Id}' already exists.", nameof(newNode));
            }

            var parent = FindNode(document, parentId)
                ?? throw new ArgumentException($"Parent node '{parentId}' does not exist.", nameof(parentId));

            var newNodes = new List<NodeDocument>(document.Nodes) { newNode };
            var children = new List<NodeId>(parent.Children);
            var index = insertIndex.HasValue ? Math.Max(0, Math.Min(insertIndex.Value, children.Count)) : children.Count;
            children.Insert(index, newNode.Id);
            ReplaceInList(newNodes, parent.WithChildren(children));

            return Rebuild(document, newNodes);
        }

        /// <summary>Removes a node and its entire subtree, and strips it from its parent's children.</summary>
        public static TreeDocument RemoveNode(TreeDocument document, NodeId nodeId)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (document.Root == nodeId)
            {
                throw new ArgumentException("The root node cannot be removed.", nameof(nodeId));
            }

            var subtree = CollectSubtree(document, nodeId);
            var newNodes = new List<NodeDocument>();
            foreach (var node in document.Nodes)
            {
                if (subtree.Contains(node.Id))
                {
                    continue;
                }

                var hasRemovedChild = false;
                for (var index = 0; index < node.Children.Count; index++)
                {
                    if (subtree.Contains(node.Children[index]))
                    {
                        hasRemovedChild = true;
                        break;
                    }
                }

                newNodes.Add(hasRemovedChild
                    ? node.WithChildren(node.Children.Where(childId => !subtree.Contains(childId)))
                    : node);
            }

            return Rebuild(document, newNodes);
        }

        public static TreeDocument Connect(TreeDocument document, NodeId parentId, NodeId childId, int? insertIndex = null)
        {
            var parent = FindNode(document, parentId)
                ?? throw new ArgumentException($"Parent node '{parentId}' does not exist.", nameof(parentId));

            if (FindNode(document, childId) == null)
            {
                throw new ArgumentException($"Child node '{childId}' does not exist.", nameof(childId));
            }

            var children = new List<NodeId>(parent.Children);
            var index = insertIndex.HasValue ? Math.Max(0, Math.Min(insertIndex.Value, children.Count)) : children.Count;
            children.Insert(index, childId);

            var newNodes = new List<NodeDocument>(document.Nodes);
            ReplaceInList(newNodes, parent.WithChildren(children));
            return Rebuild(document, newNodes);
        }

        public static TreeDocument Disconnect(TreeDocument document, NodeId parentId, NodeId childId)
        {
            var parent = FindNode(document, parentId)
                ?? throw new ArgumentException($"Parent node '{parentId}' does not exist.", nameof(parentId));

            var children = parent.Children.Where(id => id != childId).ToList();
            var newNodes = new List<NodeDocument>(document.Nodes);
            ReplaceInList(newNodes, parent.WithChildren(children));
            return Rebuild(document, newNodes);
        }

        public static TreeDocument SetParameter(TreeDocument document, NodeId nodeId, string parameterName, SemanticValue value)
        {
            var node = FindNode(document, nodeId)
                ?? throw new ArgumentException($"Node '{nodeId}' does not exist.", nameof(nodeId));

            var current = node.Parameters ?? SemanticObject.Empty;
            var properties = new List<SemanticProperty>();
            var replaced = false;
            foreach (var property in current.Properties)
            {
                if (string.Equals(property.Name, parameterName, StringComparison.Ordinal))
                {
                    properties.Add(new SemanticProperty(parameterName, value));
                    replaced = true;
                }
                else
                {
                    properties.Add(property);
                }
            }

            if (!replaced)
            {
                properties.Add(new SemanticProperty(parameterName, value));
            }

            var updatedNode = node.WithParameters(new SemanticObject(properties));
            var newNodes = new List<NodeDocument>(document.Nodes);
            ReplaceInList(newNodes, updatedNode);
            return Rebuild(document, newNodes);
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

        private static TreeDocument Rebuild(TreeDocument source, IEnumerable<NodeDocument> nodes)
        {
            return new TreeDocument(
                source.Format,
                source.FormatVersion,
                source.TreeId,
                source.Name,
                source.Root,
                nodes,
                source.Blackboard,
                source.Description,
                source.Tags,
                source.Metadata,
                new Revision(source.Revision.Value + 1),
                source.AgentContract,
                source.SharedContract);
        }
    }
}
