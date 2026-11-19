using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    public sealed partial class BehaviorTreeGraphWindow
    {
        private const string ClipboardPrefix = "aibt-nodes:";
        private NodeSearchProvider _searchProvider;

        private BehaviorTreeNode SelectedNode => _view.selection.OfType<BehaviorTreeNode>().FirstOrDefault();

        internal NodeId? SelectedNodeId => SelectedNode?.NodeId;

        private void WireGraphEditing()
        {
            _view.CanConnectNodes = (parent, child) => _session != null && _session.CanConnect(parent, child);
            _view.ContextMenuBuilder = BuildContextMenu;
            _view.nodeCreationRequest = context => OpenNodeSearch(null, context.screenMousePosition);
            _view.CreateNodeRequested = (parent, panelPosition) => OpenNodeSearch(parent, PanelToScreen(panelPosition));
            _view.EdgeDropped = OnEdgeDropped;
            _view.EdgeDetached = key => ApplyEdit(document => SemanticEditOperations.Disconnect(document, key.Parent, key.Child));
            _view.deleteSelection = (operation, askUser) => DeleteSelection();
            _view.serializeGraphElements = elements => ClipboardPrefix + string.Join(",",
                elements.OfType<BehaviorTreeNode>().Select(node => node.NodeId.Value));
            _view.canPasteSerializedData = data => data != null && data.StartsWith(ClipboardPrefix, StringComparison.Ordinal);
            _view.unserializeAndPaste = (operation, data) => DuplicateNodes(ParseClipboard(data));
            _view.graphViewChanged = change =>
            {
                if (change.movedElements != null && change.movedElements.OfType<BehaviorTreeNode>().Any()) OnNodesMoved();
                return change;
            };
            _view.SelectionChanged += () => _view.schedule.Execute(RefreshInspector);
            _view.DescribeSubtree = DescribeSubtree;
            _view.NodeOpened = id =>
            {
                if (Document != null && GraphDocumentEdits.Find(Document, id)?.Subtree != null) OpenSubtree(id);
            };
        }

        // ---------------------------------------------------------------- context menu

        private void BuildContextMenu(ContextualMenuPopulateEvent evt)
        {
            var menu = evt.menu;
            var panelPosition = evt.mousePosition;
            if (evt.target is BehaviorTreeNode node)
            {
                if (!_view.selection.Contains(node))
                {
                    _view.ClearSelection();
                    _view.AddToSelection(node);
                }

                var document = GraphDocumentEdits.Find(Document, node.NodeId);
                var parent = GraphDocumentEdits.ParentOf(Document, node.NodeId);
                var index = parent?.Children.ToList().IndexOf(node.NodeId) ?? -1;
                menu.AppendAction("Add Child…", _ => OpenNodeSearch(node.NodeId, PanelToScreen(panelPosition)),
                    CanAddChild(node.NodeId) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                if (document?.Subtree != null)
                {
                    menu.AppendAction("Open Subtree\tDouble-click", _ => OpenSubtree(node.NodeId));
                    menu.AppendAction("Unpack Subtree", _ => UnpackSubtree(node.NodeId),
                        _session != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                }
                else
                {
                    menu.AppendAction("Extract to Subtree…", _ => ChooseExtractTarget(node.NodeId),
                        _session != null && document != null && Document.Root != node.NodeId ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                }
                menu.AppendSeparator();
                menu.AppendAction("Move Earlier\tAlt+Left", _ => MoveChild(node.NodeId, -1),
                    index > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.AppendAction("Move Later\tAlt+Right", _ => MoveChild(node.NodeId, 1),
                    parent != null && index < parent.Children.Count - 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.AppendAction("Detach From Parent", _ => DetachNode(node.NodeId),
                    parent != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.AppendAction("Set As Root", _ => ApplyEdit(d => SemanticEditOperations.SetRoot(d, node.NodeId)),
                    document != null && Document.Root != node.NodeId ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.AppendSeparator();
                menu.AppendAction("Duplicate\tCtrl+D", _ => DuplicateNodes(SelectedIds()),
                    node.NodeId != Document.Root ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.AppendAction("Delete\tDel", _ => DeleteSelection(),
                    node.NodeId != Document.Root ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.AppendSeparator();
                menu.AppendAction("Frame Selection\tF", _ => _view.FrameSelection());
            }
            else if (evt.target is BehaviorTreeEdge edge && _view.EdgesByKey.Any(pair => pair.Value == edge))
            {
                var key = _view.EdgesByKey.First(pair => pair.Value == edge).Key;
                menu.AppendAction("Disconnect", _ => ApplyEdit(d => SemanticEditOperations.Disconnect(d, key.Parent, key.Child)));
                menu.AppendAction("Insert Node…", _ => OpenNodeSearch(key.Parent, PanelToScreen(panelPosition), key.Child));
            }
            else
            {
                menu.AppendAction("Create Node…\tSpace", _ => OpenNodeSearch(null, PanelToScreen(panelPosition)),
                    _session != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.AppendAction("Add Subtree…", _ => OpenSubtreePicker(null, PanelToScreen(panelPosition), ScreenToGraph(PanelToScreen(panelPosition))),
                    _session != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.AppendSeparator();
                menu.AppendAction("Auto Layout", _ => AutoLayout());
                menu.AppendAction("Frame All\tA", _ => _view.FrameAll());
            }

            menu.AppendSeparator();
            menu.AppendAction("Undo\tCtrl+Z", _ => UndoEdit(), _history.CanUndo ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendAction("Redo\tCtrl+Y", _ => RedoEdit(), _history.CanRedo ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendAction("Save\tCtrl+S", _ => SaveAll(), HasPendingChanges ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.StopPropagation();
        }

        private void OnGraphKeyDown(KeyDownEvent evt)
        {
            if (_session == null || !evt.altKey || SelectedNode == null) return;
            if (evt.keyCode == KeyCode.LeftArrow) MoveChild(SelectedNode.NodeId, -1);
            else if (evt.keyCode == KeyCode.RightArrow) MoveChild(SelectedNode.NodeId, 1);
            else return;
            evt.StopPropagation();
        }

        // ---------------------------------------------------------------- node search and creation

        private bool CanAddChild(NodeId parentId)
        {
            var parent = Document == null ? null : GraphDocumentEdits.Find(Document, parentId);
            if (_session == null || parent == null || parent.Subtree != null || _registry == null || !_registry.TryGet(parent.TypeId, out var entry)) return false;
            var maximum = entry.Manifest.ChildPolicy.Maximum;
            return !maximum.HasValue || parent.Children.Count < maximum.Value;
        }

        /// <summary>Opens the searchable node list; the chosen node becomes a child of <paramref name="parent"/> (or a free node).</summary>
        internal bool OpenNodeSearch(NodeId? parent, Vector2 screenPosition, NodeId? replaceChild = null)
        {
            if (_session == null || _registry == null)
            {
                Notify("Open a tree from a file to edit it.");
                return false;
            }
            if (parent.HasValue && !replaceChild.HasValue && !CanAddChild(parent.Value))
            {
                Notify("This node cannot take another child.");
                return false;
            }

            if (_searchProvider == null)
            {
                _searchProvider = CreateInstance<NodeSearchProvider>();
                _searchProvider.hideFlags = HideFlags.HideAndDontSave;
            }
            _searchProvider.Registry = _registry;
            _searchProvider.Title = parent.HasValue ? "Add Child" : "Create Node";
            var graphPosition = ScreenToGraph(screenPosition);
            // A reference has no children, so it cannot be inserted into an edge.
            _searchProvider.IncludeSubtree = !replaceChild.HasValue;
            _searchProvider.SubtreeSelected = _ => EditorApplication.delayCall += () => OpenSubtreePicker(parent, screenPosition, parent.HasValue ? (Vector2?)null : graphPosition);
            _searchProvider.Selected = (manifest, _) =>
            {
                if (replaceChild.HasValue) InsertNodeBetween(parent.Value, replaceChild.Value, manifest);
                else CreateNode(manifest, parent, graphPosition);
            };
            if (SearchWindow.Open(new SearchWindowContext(screenPosition, 320f, 360f), _searchProvider)) return true;
            Notify("Node search could not open. Click the graph and try again.");
            return false;
        }

        /// <summary>Adds a node of the given type, as the last child of <paramref name="parent"/> or unattached at <paramref name="graphPosition"/>.</summary>
        internal NodeId? CreateNode(NodeManifest manifest, NodeId? parent, Vector2? graphPosition = null)
        {
            var node = GraphDocumentEdits.CreateNode(manifest);
            if (!parent.HasValue && graphPosition.HasValue) _pendingPlacement[node.Id] = graphPosition.Value;
            var applied = parent.HasValue
                ? ApplyEdit(document => SemanticEditOperations.AddNode(document, node, parent.Value))
                : ApplyEdit(document => GraphDocumentEdits.AddDetachedNode(document, node));
            if (!applied) return null;
            SelectNode(node.Id, frame: false);
            return node.Id;
        }

        /// <summary>Kept for existing callers: adds a child of the given type.</summary>
        internal void AddChildTo(NodeId parentId, NodeManifest manifest) => CreateNode(manifest, parentId);

        private void InsertNodeBetween(NodeId parentId, NodeId childId, NodeManifest manifest)
        {
            var node = GraphDocumentEdits.CreateNode(manifest);
            if (!BehaviorTreeNode.AcceptsChildren(manifest))
            {
                Notify(BehaviorTreeNode.ReadableTitle(manifest.TypeId) + " cannot have children.");
                return;
            }
            ApplyEdit(document =>
            {
                var parent = GraphDocumentEdits.Find(document, parentId);
                var index = parent.Children.ToList().IndexOf(childId);
                var detached = SemanticEditOperations.Disconnect(document, parentId, childId);
                var added = SemanticEditOperations.AddNode(detached, node, parentId, index);
                return SemanticEditOperations.Connect(added, node.Id, childId);
            });
            SelectNode(node.Id, frame: false);
        }

        // ---------------------------------------------------------------- structure edits

        internal bool MoveChild(NodeId child, int delta)
            => ApplyEdit(document => GraphDocumentEdits.TryMoveChild(document, child, delta, out var moved) ? moved : document);

        internal bool DetachNode(NodeId child)
            => ApplyEdit(document => GraphDocumentEdits.TryDetach(document, child, out var detached) ? detached : document);

        internal bool ConnectNodes(NodeId parent, NodeId child)
        {
            if (_session == null || !_session.CanConnect(parent, child))
            {
                Notify("Cannot connect: the parent is full, the child is the root, or it would create a cycle.");
                return false;
            }
            return ApplyEdit(document =>
            {
                var previous = GraphDocumentEdits.ParentOf(document, child);
                if (previous != null) document = SemanticEditOperations.Disconnect(document, previous.Id, child);
                return SemanticEditOperations.Connect(document, parent, child);
            });
        }

        private void OnEdgeDropped((NodeId Parent, NodeId Child)? replaced, NodeId parent, NodeId child)
        {
            if (replaced.HasValue && replaced.Value.Parent == parent && replaced.Value.Child == child) return;
            if (replaced.HasValue && replaced.Value.Child != child)
            {
                // An existing edge's child end moved to another node: one edit replaces the link.
                if (_session == null || !_session.CanConnect(parent, child)) { Notify("Cannot connect these nodes."); return; }
                ApplyEdit(document =>
                {
                    var detached = SemanticEditOperations.Disconnect(document, replaced.Value.Parent, replaced.Value.Child);
                    var previous = GraphDocumentEdits.ParentOf(detached, child);
                    if (previous != null) detached = SemanticEditOperations.Disconnect(detached, previous.Id, child);
                    return SemanticEditOperations.Connect(detached, parent, child);
                });
                return;
            }
            ConnectNodes(parent, child);
        }

        /// <summary>Deletes the selected nodes (with their subtrees) and selected edges as one undo step. The root is kept.</summary>
        internal void DeleteSelection()
        {
            if (_session == null) return;
            var nodes = SelectedIds().Where(id => id != Document.Root).ToList();
            var edges = _view.selection.OfType<BehaviorTreeEdge>()
                .Select(edge => _view.EdgesByKey.FirstOrDefault(pair => pair.Value == edge))
                .Where(pair => pair.Value != null).Select(pair => pair.Key).ToList();
            if (nodes.Count == 0 && edges.Count == 0)
            {
                if (SelectedNode != null) Notify("The root cannot be deleted. Set another node as root first.");
                return;
            }
            DeleteNodes(nodes, edges);
        }

        internal bool DeleteNodes(IEnumerable<NodeId> nodes, IEnumerable<(NodeId Parent, NodeId Child)> edges = null)
        {
            var nodeList = nodes.ToList();
            var edgeList = edges?.ToList() ?? new List<(NodeId Parent, NodeId Child)>();
            return ApplyEdit(document =>
            {
                foreach (var edge in edgeList)
                    if (GraphDocumentEdits.Find(document, edge.Parent)?.Children.Contains(edge.Child) == true)
                        document = SemanticEditOperations.Disconnect(document, edge.Parent, edge.Child);
                foreach (var id in nodeList)
                    if (id != document.Root && GraphDocumentEdits.Find(document, id) != null)
                        document = SemanticEditOperations.RemoveNode(document, id);
                return document;
            });
        }

        /// <summary>Duplicates nodes with their subtrees, placing each copy right after its original. One undo step.</summary>
        internal IReadOnlyList<NodeId> DuplicateNodes(IEnumerable<NodeId> ids)
        {
            var copies = new List<NodeId>();
            var list = ids.Where(id => Document != null && id != Document.Root).ToList();
            // Copy only the topmost selected nodes; a selected descendant is already inside its ancestor's copy.
            list = list.Where(id => !list.Any(other => other != id && GraphDocumentEdits.Subtree(Document, other).Contains(id))).ToList();
            if (list.Count == 0) return copies;
            ApplyEdit(document =>
            {
                foreach (var id in list)
                {
                    if (GraphDocumentEdits.Find(document, id) == null) continue;
                    document = GraphDocumentEdits.DuplicateSubtree(document, id, out var copy);
                    copies.Add(copy);
                }
                return document;
            });
            if (copies.Count > 0)
            {
                _view.ClearSelection();
                foreach (var id in copies)
                    if (_view.NodesById.TryGetValue(id, out var view)) _view.AddToSelection(view);
            }
            return copies;
        }

        internal void SelectNode(NodeId id, bool frame)
        {
            if (!_view.NodesById.TryGetValue(id, out var node)) return;
            _view.ClearSelection();
            _view.AddToSelection(node);
            if (frame) _view.schedule.Execute(() => _view.FrameSelection());
            RefreshInspector();
        }

        private List<NodeId> SelectedIds() => _view.selection.OfType<BehaviorTreeNode>().Select(node => node.NodeId).ToList();

        private static IEnumerable<NodeId> ParseClipboard(string data)
            => data == null || !data.StartsWith(ClipboardPrefix, StringComparison.Ordinal)
                ? Enumerable.Empty<NodeId>()
                : data.Substring(ClipboardPrefix.Length).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(id => new NodeId(id));

        private Vector2 PanelToScreen(Vector2 panelPosition) => position.position + panelPosition;

        private Vector2 ScreenToGraph(Vector2 screenPosition)
            => _view.contentViewContainer.WorldToLocal(screenPosition - position.position);
    }
}
