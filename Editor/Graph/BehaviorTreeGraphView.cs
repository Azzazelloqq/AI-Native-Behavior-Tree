using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    /// <summary>
    /// Read-only rendering of a canonical <see cref="TreeDocument"/> as a graph. Never mutates
    /// the document and never writes to disk; positions are a transient default until a real
    /// *.aibt.layout.json reader exists (P3-005).
    /// </summary>
    public sealed class BehaviorTreeGraphView : GraphView
    {
        private const float HorizontalSpacing = 220f;
        private const float VerticalSpacing = 140f;

        private readonly Dictionary<NodeId, BehaviorTreeNode> _nodesById = new Dictionary<NodeId, BehaviorTreeNode>();

        public IReadOnlyDictionary<NodeId, BehaviorTreeNode> NodesById => _nodesById;

        public void Populate(TreeDocument document, NodeRegistry registry)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            DeleteElements(graphElements.ToList());
            _nodesById.Clear();

            var documentNodesById = new Dictionary<NodeId, NodeDocument>();
            foreach (var node in document.Nodes)
            {
                documentNodesById[node.Id] = node;
            }

            foreach (var node in document.Nodes)
            {
                NodeManifest manifest = null;
                if (registry != null && registry.TryGet(node.TypeId, out var entry))
                {
                    manifest = entry.Manifest;
                }

                var view = new BehaviorTreeNode(node, manifest);
                AddElement(view);
                _nodesById[node.Id] = view;
            }

            AssignDefaultPositions(document, documentNodesById);
            ConnectEdges(document);
        }

        private void ConnectEdges(TreeDocument document)
        {
            foreach (var node in document.Nodes)
            {
                if (!_nodesById.TryGetValue(node.Id, out var parentView) || parentView.OutputPort == null)
                {
                    continue;
                }

                foreach (var childId in node.Children)
                {
                    if (!_nodesById.TryGetValue(childId, out var childView))
                    {
                        continue;
                    }

                    var edge = new Edge { output = parentView.OutputPort, input = childView.InputPort };
                    parentView.OutputPort.Connect(edge);
                    childView.InputPort.Connect(edge);
                    AddElement(edge);
                }
            }
        }

        private void AssignDefaultPositions(TreeDocument document, Dictionary<NodeId, NodeDocument> documentNodesById)
        {
            var nextColumnByDepth = new Dictionary<int, int>();
            var visited = new HashSet<NodeId>();

            void Visit(NodeId id, int depth)
            {
                if (!visited.Add(id) || !_nodesById.TryGetValue(id, out var view))
                {
                    return;
                }

                var column = nextColumnByDepth.TryGetValue(depth, out var existingColumn) ? existingColumn : 0;
                nextColumnByDepth[depth] = column + 1;
                view.SetPosition(new Rect(column * HorizontalSpacing, depth * VerticalSpacing, 0f, 0f));

                if (!documentNodesById.TryGetValue(id, out var nodeDocument))
                {
                    return;
                }

                foreach (var childId in nodeDocument.Children)
                {
                    Visit(childId, depth + 1);
                }
            }

            if (document.Root.IsValid)
            {
                Visit(document.Root, 0);
            }

            // Nodes unreachable from the declared root (not expected for a valid tree, but this
            // adapter must not silently drop them) still get placed, at depth 0.
            foreach (var node in document.Nodes)
            {
                if (visited.Contains(node.Id) || !_nodesById.TryGetValue(node.Id, out var view))
                {
                    continue;
                }

                var column = nextColumnByDepth.TryGetValue(0, out var existingColumn) ? existingColumn : 0;
                nextColumnByDepth[0] = column + 1;
                view.SetPosition(new Rect(column * HorizontalSpacing, 0f, 0f, 0f));
                visited.Add(node.Id);
            }
        }
    }
}
