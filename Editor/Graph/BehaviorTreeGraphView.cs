using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Layout;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    /// <summary>
    /// Renders a canonical <see cref="TreeDocument"/> as a top-down graph. Never mutates the
    /// document and never writes to disk. It is read-only until a host enables editing; edits are
    /// requested through callbacks and applied by the host, which then calls <see cref="Sync"/>.
    /// Node movement is presentation only; child order always comes from the document.
    /// </summary>
    public sealed class BehaviorTreeGraphView : GraphView
    {
        private readonly Dictionary<NodeId, BehaviorTreeNode> _nodesById = new Dictionary<NodeId, BehaviorTreeNode>();
        private readonly Dictionary<(NodeId Parent, NodeId Child), BehaviorTreeEdge> _edgesByKey = new Dictionary<(NodeId, NodeId), BehaviorTreeEdge>();
        private readonly Dictionary<Edge, (NodeId Parent, NodeId Child)> _keysByEdge = new Dictionary<Edge, (NodeId, NodeId)>();
        private readonly EdgeListener _edgeListener;
        private TreeDocument _document;
        private NodeRegistry _registry;
        private bool _editable;

        public BehaviorTreeGraphView()
        {
            AddToClassList("aibt-graph");
            GraphStyles.ApplyGraph(this);
            _edgeListener = new EdgeListener(this);
            SetupZoom(0.2f, 2f);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground { name = "aibt-grid" };
            Insert(0, grid);
            grid.StretchToParentSize();

            MiniMap = new MiniMap { anchored = true, name = "aibt-minimap", maxWidth = 180f, maxHeight = 120f };
            MiniMap.AddToClassList("aibt-minimap");
            Add(MiniMap);
            // MiniMap positions itself through SetPosition, which overrides USS offsets; keep it
            // in the bottom-right corner, clear of the side panels.
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (float.IsNaN(layout.width) || layout.width < 260f || layout.height < 180f) return;
                MiniMap.SetPosition(new Rect(layout.width - 190f, layout.height - 130f, 180f, 120f));
            });
        }

        public IReadOnlyDictionary<NodeId, BehaviorTreeNode> NodesById => _nodesById;

        internal MiniMap MiniMap { get; }

        internal IReadOnlyDictionary<(NodeId Parent, NodeId Child), BehaviorTreeEdge> EdgesByKey => _edgesByKey;

        internal Func<NodeId, NodeId, bool> CanConnectNodes { get; set; }

        /// <summary>A dropped edge: the edge it replaces (if it was an existing one), then parent and child.</summary>
        internal Action<(NodeId Parent, NodeId Child)?, NodeId, NodeId> EdgeDropped { get; set; }

        /// <summary>An existing edge was dragged off its port and released on empty canvas.</summary>
        internal Action<(NodeId Parent, NodeId Child)> EdgeDetached { get; set; }

        /// <summary>Create a node (as a child of the given parent, if any) at a panel position.</summary>
        internal Action<NodeId?, Vector2> CreateNodeRequested { get; set; }

        /// <summary>Builds the right-click menu while editing is enabled.</summary>
        internal Action<ContextualMenuPopulateEvent> ContextMenuBuilder { get; set; }

        /// <summary>Lets the host enable connecting, deleting and duplicating. Read-only by default.</summary>
        internal bool Editable
        {
            get => _editable;
            set
            {
                _editable = value;
                foreach (var node in _nodesById.Values) node.SetEditable(value);
                foreach (var edge in _edgesByKey.Values) edge.capabilities = EdgeCapabilities();
            }
        }

        /// <summary>Raised after the selection changes (coalesced by the host if needed).</summary>
        internal event Action SelectionChanged;

        public override void AddToSelection(ISelectable selectable)
        {
            base.AddToSelection(selectable);
            SelectionChanged?.Invoke();
        }

        public override void RemoveFromSelection(ISelectable selectable)
        {
            base.RemoveFromSelection(selectable);
            SelectionChanged?.Invoke();
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            SelectionChanged?.Invoke();
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatible = new List<Port>();
            if (!_editable || CanConnectNodes == null || !(startPort.node is BehaviorTreeNode start)) return compatible;
            foreach (var port in ports)
            {
                if (port.direction == startPort.direction || !(port.node is BehaviorTreeNode other)) continue;
                var parent = startPort.direction == Direction.Output ? start : other;
                var child = startPort.direction == Direction.Input ? start : other;
                if (CanConnectNodes(parent.NodeId, child.NodeId)) compatible.Add(port);
            }
            return compatible;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (_editable && ContextMenuBuilder != null) ContextMenuBuilder(evt);
            else if (evt.target is GraphView) evt.menu.AppendAction("Frame All", _ => FrameAll());
        }

        public void Populate(TreeDocument document, NodeRegistry registry)
            => Populate(document, registry, null);

        internal void ClearDocument()
        {
            ClearSelection();
            DeleteElements(graphElements.ToList());
            _nodesById.Clear();
            _edgesByKey.Clear();
            _keysByEdge.Clear();
            _document = null;
        }

        /// <summary>Shows the document with positions from the layout, or the default tidy tree.</summary>
        internal void Populate(TreeDocument document, NodeRegistry registry, LayoutDocument layout)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (_document != null && _document.TreeId != document.TreeId) ClearDocument();
            var positions = layout == null
                ? GraphLayout.Arrange(document)
                : document.Nodes.ToDictionary(node => node.Id, node => layout.Nodes.TryGetValue(node.Id, out var placement)
                    ? new Vector2(placement.Position.X, placement.Position.Y) : Vector2.zero);
            Sync(document, registry, id => positions.TryGetValue(id, out var position) ? position : Vector2.zero);
            foreach (var pair in _nodesById)
                if (positions.TryGetValue(pair.Key, out var position)) SetNodePosition(pair.Value, position);
        }

        /// <summary>
        /// Brings the graph in line with the document without rebuilding unchanged elements: cards
        /// are rebound, added or removed, edges follow the document's child lists, and only new
        /// nodes get a position (from <paramref name="placeNew"/>).
        /// </summary>
        internal void Sync(TreeDocument document, NodeRegistry registry, Func<NodeId, Vector2> placeNew)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _registry = registry;
            var wanted = new HashSet<NodeId>(document.Nodes.Select(node => node.Id));
            foreach (var stale in _nodesById.Keys.Where(id => !wanted.Contains(id)).ToList()) RemoveNodeView(stale);

            foreach (var node in document.Nodes)
            {
                NodeManifest manifest = null;
                if (registry != null && registry.TryGet(node.TypeId, out var entry)) manifest = entry.Manifest;
                if (_nodesById.TryGetValue(node.Id, out var view))
                {
                    if (view.TypeId == node.TypeId && BehaviorTreeNode.AcceptsChildren(view.Manifest) == BehaviorTreeNode.AcceptsChildren(manifest))
                    {
                        view.Bind(node, manifest);
                        continue;
                    }

                    var kept = NodePosition(view);
                    var wasSelected = selection.Contains(view);
                    RemoveNodeView(node.Id);
                    view = AddNodeView(node, manifest, kept);
                    if (wasSelected) AddToSelection(view);
                    continue;
                }

                AddNodeView(node, manifest, placeNew?.Invoke(node.Id) ?? Vector2.zero);
            }

            SyncEdges();
        }

        /// <summary>Highlights edges whose parent and child are both on the active path.</summary>
        internal void SetActiveEdges(ICollection<(NodeId Parent, NodeId Child)> active)
        {
            foreach (var pair in _edgesByKey) pair.Value.Active = active != null && active.Contains(pair.Key);
        }

        internal void ClearExecutionHighlight()
        {
            foreach (var node in _nodesById.Values) node.SetHighlight(Color.clear, 0f);
            SetActiveEdges(null);
        }

        internal static Vector2 NodePosition(BehaviorTreeNode node)
            => new Vector2(node.style.left.value.value, node.style.top.value.value);

        internal static void SetNodePosition(BehaviorTreeNode node, Vector2 position)
            => node.SetPosition(new Rect(position, node.GetPosition().size));

        private BehaviorTreeNode AddNodeView(NodeDocument node, NodeManifest manifest, Vector2 position)
        {
            var view = new BehaviorTreeNode(node, manifest, _edgeListener);
            view.SetEditable(_editable);
            AddElement(view);
            view.SetPosition(new Rect(position, Vector2.zero));
            _nodesById[node.Id] = view;
            return view;
        }

        private void RemoveNodeView(NodeId id)
        {
            if (!_nodesById.TryGetValue(id, out var view)) return;
            foreach (var key in _edgesByKey.Keys.Where(key => key.Parent == id || key.Child == id).ToList()) RemoveEdge(key);
            RemoveFromSelection(view);
            RemoveElement(view);
            _nodesById.Remove(id);
        }

        private void SyncEdges()
        {
            var wanted = new Dictionary<(NodeId, NodeId), int>();
            var orderByChild = new Dictionary<NodeId, int>();
            if (_document != null)
            {
                foreach (var node in _document.Nodes)
                {
                    for (var index = 0; index < node.Children.Count; index++)
                    {
                        wanted[(node.Id, node.Children[index])] = index;
                        orderByChild[node.Children[index]] = index + 1;
                    }
                }
            }

            foreach (var key in _edgesByKey.Keys.ToList())
            {
                var edge = _edgesByKey[key];
                var intact = wanted.ContainsKey(key) && _nodesById.TryGetValue(key.Item1, out var parentView) &&
                             _nodesById.TryGetValue(key.Item2, out var childView) &&
                             edge.output == parentView.OutputPort && edge.input == childView.InputPort;
                if (!intact) RemoveEdge(key);
            }

            // Edges the drag helper left behind (dropped candidates, detached copies) are not ours.
            foreach (var stray in edges.ToList().Where(edge => !_keysByEdge.ContainsKey(edge)))
            {
                stray.output?.Disconnect(stray);
                stray.input?.Disconnect(stray);
                RemoveElement(stray);
            }

            foreach (var pair in wanted)
            {
                if (_edgesByKey.ContainsKey(pair.Key)) continue;
                if (!_nodesById.TryGetValue(pair.Key.Item1, out var parentView) || parentView.OutputPort == null ||
                    !_nodesById.TryGetValue(pair.Key.Item2, out var childView)) continue;
                var edge = new BehaviorTreeEdge { output = parentView.OutputPort, input = childView.InputPort, capabilities = EdgeCapabilities() };
                parentView.OutputPort.Connect(edge);
                childView.InputPort.Connect(edge);
                AddElement(edge);
                _edgesByKey[pair.Key] = edge;
                _keysByEdge[edge] = pair.Key;
            }

            foreach (var pair in _nodesById)
            {
                pair.Value.SetRoot(_document != null && _document.Root == pair.Key);
                pair.Value.SetOrder(orderByChild.TryGetValue(pair.Key, out var order) ? order : (int?)null);
            }
        }

        private void RemoveEdge((NodeId, NodeId) key)
        {
            if (!_edgesByKey.TryGetValue(key, out var edge)) return;
            _edgesByKey.Remove(key);
            _keysByEdge.Remove(edge);
            if (_nodesById.TryGetValue(key.Item1, out var parentView)) parentView.OutputPort?.Disconnect(edge);
            if (_nodesById.TryGetValue(key.Item2, out var childView)) childView.InputPort.Disconnect(edge);
            edge.output?.Disconnect(edge);
            edge.input?.Disconnect(edge);
            RemoveFromSelection(edge);
            RemoveElement(edge);
        }

        private Capabilities EdgeCapabilities()
            => _editable ? Capabilities.Selectable | Capabilities.Deletable : Capabilities.Selectable;

        private void OnEdgeDropped(Edge edge)
        {
            var existing = _keysByEdge.TryGetValue(edge, out var key) ? key : ((NodeId, NodeId)?)null;
            if (_editable && edge.output?.node is BehaviorTreeNode parent && edge.input?.node is BehaviorTreeNode child)
                EdgeDropped?.Invoke(existing, parent.NodeId, child.NodeId);
            schedule.Execute(SyncEdges);
        }

        private void OnEdgeDroppedOutside(Edge edge, Vector2 position)
        {
            if (_editable)
            {
                if (_keysByEdge.TryGetValue(edge, out var key)) EdgeDetached?.Invoke(key);
                else if (edge.input == null && edge.output?.node is BehaviorTreeNode parent) CreateNodeRequested?.Invoke(parent.NodeId, position);
            }

            schedule.Execute(SyncEdges);
        }

        private sealed class EdgeListener : IEdgeConnectorListener
        {
            private readonly BehaviorTreeGraphView _view;

            internal EdgeListener(BehaviorTreeGraphView view) => _view = view;

            public void OnDropOutsidePort(Edge edge, Vector2 position) => _view.OnEdgeDroppedOutside(edge, position);

            public void OnDrop(GraphView graphView, Edge edge) => _view.OnEdgeDropped(edge);
        }
    }
}
