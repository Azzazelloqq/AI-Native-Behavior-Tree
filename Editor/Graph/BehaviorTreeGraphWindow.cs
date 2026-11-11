using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Editor.Layout;
using AIBT.Editor.Organization;
using AIBT.Editor.Registry;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    /// <summary>Hosts an authoring graph over an AIBT tree document.</summary>
    public sealed partial class BehaviorTreeGraphWindow : EditorWindow
    {
        private BehaviorTreeGraphView _view;
        private Label _diagnosticLabel;
        private Label _validationLabel;
        private Label _nativeValidationLabel;
        private Label _documentLabel;
        private Label _emptyLabel;
        private ListView _treeList;
        private ToolbarSearchField _search;
        private PopupField<string> _tag;
        private PopupField<string> _status;
        private ScrollView _inspector;
        private Label _catalogDiagnostics;
        private ProjectNodeCatalog _projectCatalog;
        private GraphEditSession _session;
        private LayoutHistory _layoutHistory;
        private NodeRegistry _registry;
        private readonly List<TreeCatalogEntry> _catalog = new List<TreeCatalogEntry>();
        private readonly List<TreeCatalogEntry> _filtered = new List<TreeCatalogEntry>();
        [SerializeField] private string _sourcePath;
        [SerializeField] private string _sourceGuid;
        [SerializeField] private string _draftBytes;
        [SerializeField] private string _draftBaseBytes;
        [SerializeField] private string _layoutBaseBytes;
        [SerializeField] private bool _layoutDirty;
        [SerializeField] private List<string> _layoutNodeIds = new List<string>();
        [SerializeField] private List<Vector2> _layoutPositions = new List<Vector2>();

        public TreeDocument Document { get; private set; }

        public DiagnosticCollection Diagnostics { get; private set; }

        public BehaviorTreeGraphView View { get { EnsureView(); return _view; } }
        internal event Action ViewInvalidated;
        internal NodeRegistry CurrentRegistry => _registry;
        internal void ReleaseExecutionView() => ViewInvalidated?.Invoke();

        [MenuItem("AIBT/Graph Editor")]
        public static BehaviorTreeGraphWindow ShowWindow()
        {
            var window = GetWindow<BehaviorTreeGraphWindow>();
            window.titleContent = new GUIContent("AIBT Graph");
            return window;
        }

        public void OpenFromPath(string path, NodeRegistry registry)
        {
            EnsureView();
            EditorApplication.delayCall -= RestoreDocument;
            if (!ConfirmDiscard()) return;
            ReleaseExecutionView();
            ResetLayoutChanges();
            _sourcePath = path;
            _sourceGuid = AssetDatabase.AssetPathToGUID(path);
            _documentLabel.text = path;
            try
            {
                if (!TreeSourcePaths.IsTree(path)) throw new ArgumentException("Choose an .aibt or .aibt.json tree.");
                OpenDocument(File.ReadAllBytes(path), path, registry, path);
                _emptyLabel.style.display = DisplayStyle.None;
                _view.schedule.Execute(() => _view.FrameAll()).ExecuteLater(100);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                _view.ClearDocument();
                Document = null;
                _session = null;
                _draftBytes = null;
                _draftBaseBytes = null;
                hasUnsavedChanges = false;
                _diagnosticLabel.text = exception.Message;
                _diagnosticLabel.style.display = DisplayStyle.Flex;
                _emptyLabel.text = "Tree could not be opened. Check the source path and diagnostics above.";
                _emptyLabel.style.display = DisplayStyle.Flex;
            }
        }

        public void OpenFromPath(string path) => OpenFromPath(path, (_projectCatalog ?? ProjectNodeCatalogCache.Current).Registry);

        public void OpenFromBytes(byte[] utf8, string documentId, NodeRegistry registry)
        {
            EnsureView();
            if (!ConfirmDiscard()) return;
            EditorApplication.delayCall -= RestoreDocument;
            ResetLayoutChanges();
            _sourcePath = null;
            _sourceGuid = null;
            OpenDocument(utf8, documentId, registry, null);
        }

        private void OpenDocument(byte[] utf8, string documentId, NodeRegistry registry, string treePath)
        {
            EnsureView();
            ReleaseExecutionView();
            _view.ClearDocument();
            _nativeValidationLabel.text = string.Empty;
            _nativeValidationLabel.style.display = DisplayStyle.None;

            var result = CanonicalTreeJson.Parse(utf8, documentId);
            Diagnostics = result.Diagnostics;
            Document = result.Success ? result.Document : null;
            _registry = registry;
            _session = Document != null && registry != null && treePath != null
                ? new GraphEditSession(Document, registry, CompilerOptions(), utf8, ValidateProjectDocument)
                : null;
            _draftBytes = null;
            _draftBaseBytes = null;
            hasUnsavedChanges = false;
            RefreshValidation();

            LayoutDocument layout = null;
            var canRender = Document != null;
            if (canRender && treePath != null)
            {
                var layoutPath = LayoutPersistenceController.LayoutPathFor(treePath);
                _layoutBaseBytes = File.Exists(layoutPath) ? Convert.ToBase64String(File.ReadAllBytes(layoutPath)) : null;
                var loaded = LayoutPersistenceController.Load(treePath, Document);
                Diagnostics = new DiagnosticCollection(Diagnostics.Concat(loaded.Diagnostics));
                // A broken presentation file must not hide an otherwise readable tree.
                // Keep its diagnostics visible and use the graph's default placement.
                if (loaded.Success && !loaded.UsedDefault) layout = loaded.Document;
            }

            _diagnosticLabel.text = string.Join("\n", Diagnostics.Select(d => d.Code.Value + ": " + d.Message));
            _diagnosticLabel.style.display = Diagnostics.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (canRender) _view.Populate(Document, registry, layout);
            _layoutHistory = canRender ? new LayoutHistory(LayoutSnapshot()) : null;
            if (canRender && registry != null)
            {
                var missing = Document.Nodes.Where(n => !registry.TryGet(n.TypeId, out _)).Select(n => n.TypeId).Distinct().ToArray();
                if (missing.Length > 0)
                {
                    _diagnosticLabel.text += "\nProject node contracts unresolved in this view (not a runtime failure): " + string.Join(", ", missing);
                    _diagnosticLabel.style.display = DisplayStyle.Flex;
                }
            }
            RefreshInspector();
        }

        private void OnEnable()
        {
            EnsureView();
            ProjectNodeCatalogCache.Changed += RefreshCatalog;
            EditorApplication.projectChanged += ProjectChanged;
            EditorApplication.delayCall += RestoreDocument;
        }

        private void OnDisable()
        {
            ReleaseExecutionView();
            ProjectNodeCatalogCache.Changed -= RefreshCatalog;
            EditorApplication.projectChanged -= ProjectChanged;
            EditorApplication.delayCall -= RestoreDocument;
        }

        private void RestoreDocument()
        {
            if (this == null) return;
            RefreshCatalog();
            var restoreLayout = _layoutDirty;
            var savedIds = _layoutNodeIds.ToArray();
            var savedPositions = _layoutPositions.ToArray();
            var layoutBaseline = _layoutBaseBytes;
            if (!string.IsNullOrEmpty(_draftBytes))
            {
                var result = CanonicalTreeJson.Parse(Convert.FromBase64String(_draftBytes));
                if (result.Success)
                {
                    _registry = (_projectCatalog ?? ProjectNodeCatalogCache.Current).Registry;
                    var baseline = string.IsNullOrEmpty(_draftBaseBytes) ? null : Convert.FromBase64String(_draftBaseBytes);
                    _session = new GraphEditSession(result.Document, _registry, CompilerOptions(), baseline, ValidateProjectDocument);
                    RefreshSession();
                    RestoreLayoutPositions(savedIds, savedPositions, restoreLayout);
                    return;
                }
            }
            _layoutDirty = false;
            if (!string.IsNullOrEmpty(_sourcePath)) OpenFromPath(_sourcePath);
            if (restoreLayout) _layoutBaseBytes = layoutBaseline;
            RestoreLayoutPositions(savedIds, savedPositions, restoreLayout);
        }

        private void ProjectChanged()
        {
            RefreshCatalog();
            if (!string.IsNullOrEmpty(_sourceGuid))
            {
                var moved = AssetDatabase.GUIDToAssetPath(_sourceGuid);
                if (!string.IsNullOrEmpty(moved))
                {
                    _sourcePath = moved;
                    _documentLabel.text = moved + (_session?.IsDirty == true ? " *" : "");
                }
            }
            if (string.IsNullOrEmpty(_sourcePath) || _layoutDirty || _session?.IsDirty == true) return;
            var baseline = _session?.SavedBytes;
            try
            {
                if (_session == null || (baseline != null && (!File.Exists(_sourcePath) || !File.ReadAllBytes(_sourcePath).SequenceEqual(baseline))))
                    OpenFromPath(_sourcePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // Keep the currently displayed document until the source can be read again.
            }
        }

        private static ReferenceCompilerOptions CompilerOptions() => new ReferenceCompilerOptions(
            "trees/editor.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0));

        private bool ConfirmDiscard()
        {
            return (_session == null || !_session.IsDirty) && !_layoutDirty || EditorUtility.DisplayDialog(
                "Unsaved AIBT tree", "Discard unsaved tree or layout changes?", "Discard", "Cancel");
        }

        private void NewTree()
        {
            EditorApplication.delayCall -= RestoreDocument;
            if (!ConfirmDiscard()) return;
            var path = EditorUtility.SaveFilePanelInProject("Create AIBT tree", "NewTree.aibt", "aibt", "Choose a tree location.");
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path))
            {
                EditorUtility.DisplayDialog("AIBT", "Choose a new file; existing trees are not overwritten.", "OK");
                return;
            }
            _registry = (_projectCatalog ?? ProjectNodeCatalogCache.Current).Registry;
            var root = new NodeDocument(new NodeId("root"), "aibt.core.memory-sequence", 1,
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var document = TreeDocument.CreateVersion2(
                new TreeId("tree." + Guid.NewGuid().ToString("N")), Path.GetFileNameWithoutExtension(path),
                root.Id, new[] { root }, null, null, tags: TagSet.Empty, metadata: SemanticObject.Empty);
            _sourcePath = path;
            _sourceGuid = null;
            ResetLayoutChanges();
            _layoutBaseBytes = null;
            _session = new GraphEditSession(document, _registry, CompilerOptions(), null, ValidateProjectDocument);
            _view.ClearDocument();
            RefreshSession();
        }

        private void SaveTree()
        {
            if (_session == null || string.IsNullOrEmpty(_sourcePath)) return;
            try
            {
                _session.Save(_sourcePath);
                hasUnsavedChanges = _layoutDirty;
                AssetDatabase.Refresh();
                _sourceGuid = AssetDatabase.AssetPathToGUID(_sourcePath);
                RefreshSession();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                EditorUtility.DisplayDialog("Unable to save tree", exception.Message, "OK");
            }
        }

        private void AddChild()
        {
            var selected = _view.selection.OfType<BehaviorTreeNode>().FirstOrDefault();
            if (_session == null || selected == null) return;
            var parent = _session.Document.Nodes.First(n => n.Id == selected.NodeId);
            if (selected.Manifest == null || (selected.Manifest.ChildPolicy.Maximum.HasValue &&
                parent.Children.Count >= selected.Manifest.ChildPolicy.Maximum.Value)) return;
            var menu = new GenericMenu();
            var labels = _registry.Select(entry => entry.Manifest.Category + "/" + BehaviorTreeNode.ReadableTitle(entry.Manifest.TypeId)).ToList();
            var index = 0;
            foreach (var entry in _registry)
            {
                var manifest = entry.Manifest;
                var label = labels[index++];
                if (labels.Count(other => string.Equals(other, label, StringComparison.Ordinal)) > 1)
                    label += " [" + manifest.TypeId + "]";
                menu.AddItem(new GUIContent(label, manifest.Summary), false, () => AddChildTo(parent.Id, manifest));
            }
            menu.ShowAsContext();
        }

        internal void AddChildTo(NodeId parentId, NodeManifest manifest)
        {
            var id = new NodeId("node-" + Guid.NewGuid().ToString("N"));
            var node = new NodeDocument(id, manifest.TypeId, checked((int)manifest.Version),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            _session.Apply(document => SemanticEditOperations.AddNode(document, node, parentId));
            RefreshSession();
        }

        private void SaveLayout()
        {
            if (_session == null || string.IsNullOrEmpty(_sourcePath)) return;
            if (_session.IsDirty)
            {
                EditorUtility.DisplayDialog("Save tree first", "Save the tree before saving its layout.", "OK");
                return;
            }
            try
            {
                var layoutPath = LayoutPersistenceController.LayoutPathFor(_sourcePath);
                var expected = string.IsNullOrEmpty(_layoutBaseBytes) ? null : Convert.FromBase64String(_layoutBaseBytes);
                LayoutDocument old;
                if (expected == null) old = DeterministicAutoLayoutService.Layout(Document);
                else
                {
                    var parsed = CanonicalLayoutJson.Parse(expected, layoutPath);
                    if (!parsed.Success || parsed.Document.TreeId != Document.TreeId)
                        throw new InvalidOperationException("The existing layout cannot be read for this tree. Resolve its diagnostics before saving a layout.");
                    old = parsed.Document;
                }
                var nodes = new Dictionary<NodeId, LayoutNodePlacement>();
                foreach (var pair in _view.NodesById)
                {
                    var position = NodePosition(pair.Value);
                    var pinned = old.Nodes.TryGetValue(pair.Key, out var placement) && placement.Pinned;
                    nodes.Add(pair.Key, new LayoutNodePlacement(new LayoutPoint(position.x, position.y), pinned));
                }
                var groups = old.Groups.ToDictionary(pair => pair.Key, pair => new LayoutGroup(
                    pair.Value.Title, pair.Value.MemberNodeIds.Where(nodes.ContainsKey), pair.Value.Description,
                    pair.Value.Color, pair.Value.Locked));
                var edges = new HashSet<LayoutEdgeKey>(Document.Nodes.SelectMany(node =>
                    node.Children.Select(child => new LayoutEdgeKey(node.Id, child))));
                var reroutes = old.Reroutes.Where(pair => edges.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
                var layout = new LayoutDocument(Document.TreeId, old.Direction, nodes, groups, old.Notes, reroutes);
                var bytes = CanonicalLayoutJsonWriter.Write(layout);
                var validation = CanonicalLayoutJson.Parse(bytes, layoutPath, Document);
                if (!validation.Success)
                    throw new InvalidOperationException(string.Join("\n", validation.Diagnostics.Select(d => d.Message)));
                GraphEditSession.WriteIfUnchanged(layoutPath, expected, bytes);
                _layoutBaseBytes = Convert.ToBase64String(bytes);
                ResetLayoutChanges();
                hasUnsavedChanges = _session.IsDirty;
                AssetDatabase.Refresh();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is ArgumentException)
            {
                EditorUtility.DisplayDialog("Unable to save layout", exception.Message, "OK");
            }
        }

        private void RemoveSelected()
        {
            var node = _view.selection.OfType<BehaviorTreeNode>().FirstOrDefault();
            if (_session == null || node == null || node.NodeId == _session.Document.Root) return;
            if (!EditorUtility.DisplayDialog("Remove branch", "Remove this node and all its descendants?", "Remove", "Cancel")) return;
            _session.Apply(document => SemanticEditOperations.RemoveNode(document, node.NodeId));
            RefreshSession();
        }

        private void MoveSelected(int direction)
        {
            var selected = _view.selection.OfType<BehaviorTreeNode>().FirstOrDefault();
            if (_session == null || selected == null) return;
            var parent = Document.Nodes.FirstOrDefault(node => node.Children.Contains(selected.NodeId));
            if (parent == null) return;
            var children = parent.Children.ToList();
            var index = children.IndexOf(selected.NodeId);
            var next = index + direction;
            if (next < 0 || next >= children.Count) return;
            _session.Apply(document => SemanticEditOperations.Connect(
                SemanticEditOperations.Disconnect(document, parent.Id, selected.NodeId), parent.Id, selected.NodeId, next));
            RefreshSession();
        }

        private void DetachSelected()
        {
            var selected = _view.selection.OfType<BehaviorTreeNode>().FirstOrDefault();
            if (_session == null || selected == null || selected.NodeId == Document.Root) return;
            var parent = Document.Nodes.FirstOrDefault(node => node.Children.Contains(selected.NodeId));
            if (parent == null) return;
            _session.Apply(document => SemanticEditOperations.Disconnect(document, parent.Id, selected.NodeId));
            RefreshSession();
        }

        private void SetSelectedRoot()
        {
            var selected = _view.selection.OfType<BehaviorTreeNode>().FirstOrDefault();
            if (_session == null || selected == null || selected.NodeId == Document.Root) return;
            _session.Apply(document => SemanticEditOperations.SetRoot(document, selected.NodeId));
            RefreshSession();
        }

        private void AttachSelected()
        {
            var selected = _view.selection.OfType<BehaviorTreeNode>().FirstOrDefault();
            if (_session == null || selected == null || selected.NodeId == Document.Root) return;
            var previousParent = Document.Nodes.FirstOrDefault(node => node.Children.Contains(selected.NodeId));
            var subtree = new HashSet<NodeId>();
            var pending = new Stack<NodeId>();
            pending.Push(selected.NodeId);
            while (pending.Count > 0)
            {
                var id = pending.Pop();
                if (!subtree.Add(id)) continue;
                var node = Document.Nodes.FirstOrDefault(candidate => candidate.Id == id);
                if (node != null) foreach (var child in node.Children) pending.Push(child);
            }
            var menu = new GenericMenu();
            var found = false;
            foreach (var parent in Document.Nodes)
            {
                if (subtree.Contains(parent.Id) || (previousParent != null && previousParent.Id == parent.Id) ||
                    !_registry.TryGet(parent.TypeId, out var entry) ||
                    (entry.Manifest.ChildPolicy.Maximum.HasValue && parent.Children.Count >= entry.Manifest.ChildPolicy.Maximum.Value)) continue;
                var candidate = parent;
                found = true;
                menu.AddItem(new GUIContent(candidate.Id.Value + " (" + candidate.TypeId + ")"), false, () =>
                {
                    _session.Apply(document =>
                    {
                        var detached = previousParent == null ? document : SemanticEditOperations.Disconnect(document, previousParent.Id, selected.NodeId);
                        return SemanticEditOperations.Connect(detached, candidate.Id, selected.NodeId);
                    });
                    RefreshSession();
                });
            }
            if (!found) menu.AddDisabledItem(new GUIContent("No compatible parent"));
            menu.ShowAsContext();
        }

        private void RefreshSession()
        {
            ReleaseExecutionView();
            var selectedId = _view.selection.OfType<BehaviorTreeNode>().FirstOrDefault()?.NodeId;
            var positions = _view.NodesById.ToDictionary(pair => pair.Key,
                pair => new Rect(NodePosition(pair.Value), pair.Value.GetPosition().size));
            Document = _session.Document;
            Diagnostics = _session.Diagnostics;
            _nativeValidationLabel.text = string.Empty;
            _nativeValidationLabel.style.display = DisplayStyle.None;
            hasUnsavedChanges = _session.IsDirty || _layoutDirty;
            _draftBytes = _session.IsDirty ? Convert.ToBase64String(CanonicalTreeJson.Serialize(Document).Utf8) : null;
            var savedBytes = _session.SavedBytes;
            _draftBaseBytes = _session.IsDirty && savedBytes != null ? Convert.ToBase64String(savedBytes) : null;
            saveChangesMessage = "Save the AIBT tree before closing?";
            _documentLabel.text = _sourcePath + (_session.IsDirty ? " *" : "");
            _emptyLabel.style.display = DisplayStyle.None;
            _diagnosticLabel.text = string.Join("\n", Diagnostics.Select(d => d.Code.Value + ": " + d.Message));
            _diagnosticLabel.style.display = Diagnostics.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            RefreshValidation();
            _view.Populate(Document, _registry);
            foreach (var pair in _view.NodesById)
            {
                if (positions.TryGetValue(pair.Key, out var position)) pair.Value.SetPosition(position);
                else if (_layoutHistory != null && _layoutHistory.Current.TreeId == Document.TreeId &&
                         _layoutHistory.Current.Nodes.TryGetValue(pair.Key, out var previous))
                    pair.Value.SetPosition(new Rect(previous.Position.X, previous.Position.Y, 0, 0));
            }
            if (_layoutHistory == null || _layoutHistory.Current.TreeId != Document.TreeId)
                _layoutHistory = new LayoutHistory(LayoutSnapshot());
            else _layoutHistory.IncludeNodes(LayoutSnapshot().Nodes);
            if (selectedId.HasValue && _view.NodesById.TryGetValue(selectedId.Value, out var selected))
                _view.AddToSelection(selected);
            RefreshInspector();
        }

        private void ValidateNative()
        {
            if (_session == null) return;
            var catalog = _projectCatalog ?? ProjectNodeCatalogCache.Current;
            if (catalog.Problems.Count > 0)
            {
                _nativeValidationLabel.text = "Native compilation unavailable: " + string.Join("\n", catalog.Problems);
            }
            else
            {
                var result = GeneratedCompiledProgramV2Compiler.Compile(_session.Document, catalog.Generated.Values, catalog.RegisteredTypes,
                    new ReferenceCompilerOptions("trees/editor.aibt.json", ReferenceCompilationPolicy.Phase1,
                        new CompiledCompilerVersion(1, 0, 0, 5)));
                _nativeValidationLabel.text = result.Success
                    ? "Native program compiled. Player dispatch and game integration still require verification."
                    : "Native compilation: " + string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message));
            }
            _nativeValidationLabel.style.display = DisplayStyle.Flex;
        }

        private void RefreshValidation()
        {
            if (_validationLabel == null) return;
            _validationLabel.text = _session == null || _session.CanCompile ? string.Empty :
                "Reference validation: " + string.Join("\n", _session.Diagnostics.Select(d => d.Code.Value + ": " + d.Message));
            _validationLabel.style.display = string.IsNullOrEmpty(_validationLabel.text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void RefreshInspector()
        {
            if (_inspector == null) return;
            _inspector.Clear();
            if (_session != null) AddBlackboardInspector();
            var view = _view.selection.OfType<BehaviorTreeNode>().FirstOrDefault();
            if (_session == null || view == null) { _inspector.Add(new Label("Select a node to edit its parameters.")); return; }
            var node = Document.Nodes.First(n => n.Id == view.NodeId);
            _inspector.Add(new Label(view.title) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            _inspector.Add(new Label(node.TypeId) { style = { whiteSpace = WhiteSpace.Normal } });
            if (view.Manifest == null) { _inspector.Add(new Label("Node contract is unavailable.")); return; }
            _inspector.Add(new Label(view.Manifest.Summary) { style = { whiteSpace = WhiteSpace.Normal } });
            AddBindingInspector(node);
            foreach (var parameter in view.Manifest.Parameters)
            {
                node.Parameters.TryGetValue(parameter.Name, out var value);
                var label = parameter.Name + (parameter.Required ? " *" : "");
                if (parameter.Type == NodeParameterType.Boolean)
                {
                    var field = new Toggle(label);
                    field.SetValueWithoutNotify(value != null && value.TryGetBoolean(out var flag) && flag);
                    field.RegisterValueChangedCallback(evt => SetParameter(node.Id, parameter.Name, SemanticValue.FromBoolean(evt.newValue)));
                    _inspector.Add(field);
                }
                else if (parameter.Type == NodeParameterType.Float32)
                {
                    var field = new FloatField(label) { isDelayed = true };
                    if (value != null)
                    {
                        if (value.TryGetNumber(out var number)) field.SetValueWithoutNotify((float)number);
                        else if (value.TryGetInt64(out var signed)) field.SetValueWithoutNotify(signed);
                        else if (value.TryGetUInt64(out var unsigned)) field.SetValueWithoutNotify(unsigned);
                    }
                    field.RegisterValueChangedCallback(evt =>
                    {
                        if (!float.IsNaN(evt.newValue) && !float.IsInfinity(evt.newValue))
                            SetParameter(node.Id, parameter.Name, SemanticValue.FromNumber(evt.newValue == 0f ? 0d : evt.newValue));
                        else _view.schedule.Execute(RefreshInspector);
                    });
                    _inspector.Add(field);
                    if (value == null)
                        _inspector.Add(new Button(() => SetParameter(node.Id, parameter.Name, SemanticValue.FromNumber(0d))) { text = "Set " + parameter.Name + " to zero" });
                }
                else if (parameter.Type == NodeParameterType.StringEnum)
                {
                    var choices = parameter.AllowedValues.ToList();
                    var current = value != null && value.TryGetString(out var text) && choices.Contains(text) ? text : choices[0];
                    var field = new PopupField<string>(label, choices, choices.IndexOf(current));
                    field.RegisterValueChangedCallback(evt => SetParameter(node.Id, parameter.Name, SemanticValue.FromString(evt.newValue)));
                    _inspector.Add(field);
                }
                else
                {
                    var field = new TextField(label);
                    if (value != null && value.TryGetUInt64(out var number)) field.SetValueWithoutNotify(number.ToString());
                    else if (value != null && value.TryGetInt64(out var signed) && signed >= 0) field.SetValueWithoutNotify(signed.ToString());
                    field.RegisterCallback<FocusOutEvent>(_ =>
                    {
                        if (ulong.TryParse(field.value, out var number) &&
                            (parameter.Type != NodeParameterType.UInt32 || number <= uint.MaxValue) &&
                            !(value != null && ((value.TryGetUInt64(out var previous) && previous == number) ||
                                (value.TryGetInt64(out var previousSigned) && previousSigned >= 0 && (ulong)previousSigned == number))))
                            SetParameter(node.Id, parameter.Name, SemanticValue.FromUInt64(number));
                    });
                    _inspector.Add(field);
                }
                if (value == null) _inspector.Add(new Label("Not set") { style = { color = new Color(1f, 0.65f, 0.25f) } });
                if (value == null && parameter.Type == NodeParameterType.Boolean)
                    _inspector.Add(new Button(() => SetParameter(node.Id, parameter.Name, SemanticValue.FromBoolean(false))) { text = "Set " + parameter.Name + " to false" });
                if (value == null && parameter.Type == NodeParameterType.StringEnum)
                    _inspector.Add(new Button(() => SetParameter(node.Id, parameter.Name, SemanticValue.FromString(parameter.AllowedValues[0]))) { text = "Set " + parameter.Name + " default" });
                if (value != null)
                    _inspector.Add(new Button(() =>
                    {
                        _session.Apply(document => SemanticEditOperations.RemoveParameter(document, node.Id, parameter.Name));
                        RefreshSession();
                    }) { text = "Clear " + parameter.Name });
            }
        }

        private void SetParameter(NodeId node, string name, SemanticValue value)
        {
            _session.Apply(document => SemanticEditOperations.SetParameter(document, node, name, value));
            // The field may still be dispatching FocusOut; rebuild after that event completes.
            _view.schedule.Execute(RefreshSession);
        }

        public override void SaveChanges()
        {
            if (_session?.IsDirty == true) SaveTree();
            if (_session != null && !_session.IsDirty && _layoutDirty) SaveLayout();
        }

        public override void DiscardChanges()
        {
            _draftBytes = null;
            _draftBaseBytes = null;
            ResetLayoutChanges();
            hasUnsavedChanges = false;
            base.DiscardChanges();
        }

        private void CaptureLayoutPositions()
        {
            if (!_layoutDirty) return;
            _layoutNodeIds.Clear();
            _layoutPositions.Clear();
            foreach (var pair in _view.NodesById)
            {
                _layoutNodeIds.Add(pair.Key.Value);
                _layoutPositions.Add(NodePosition(pair.Value));
            }
        }

        private LayoutDocument LayoutSnapshot()
        {
            var nodes = _view.NodesById.ToDictionary(pair => pair.Key,
                pair => new LayoutNodePlacement(new LayoutPoint(NodePosition(pair.Value).x, NodePosition(pair.Value).y)));
            return new LayoutDocument(Document.TreeId, LayoutDirection.TopToBottom, nodes);
        }

        private void RecordLayoutMove()
        {
            if (_session == null || _layoutHistory == null) return;
            _layoutHistory.Do(LayoutSnapshot());
            CaptureLayoutPositions();
        }

        private void ChangeLayoutHistory(bool redo)
        {
            if (_layoutHistory == null || (redo ? !_layoutHistory.CanRedo : !_layoutHistory.CanUndo)) return;
            var layout = redo ? _layoutHistory.Redo() : _layoutHistory.Undo();
            foreach (var pair in layout.Nodes)
                if (_view.NodesById.TryGetValue(pair.Key, out var node))
                    node.SetPosition(new Rect(pair.Value.Position.X, pair.Value.Position.Y, node.GetPosition().width, node.GetPosition().height));
            _layoutDirty = true;
            hasUnsavedChanges = true;
            CaptureLayoutPositions();
            _view.MarkDirtyRepaint();
            Repaint();
        }

        private void RestoreLayoutPositions(string[] ids, Vector2[] positions, bool wasDirty)
        {
            if (!wasDirty) return;
            for (var index = 0; index < Math.Min(ids.Length, positions.Length); index++)
            {
                var id = new NodeId(ids[index]);
                if (_view.NodesById.TryGetValue(id, out var node))
                    node.SetPosition(new Rect(positions[index], node.GetPosition().size));
            }
            _layoutDirty = true;
            hasUnsavedChanges = true;
            CaptureLayoutPositions();
            _layoutHistory = new LayoutHistory(LayoutSnapshot());
        }

        private void ResetLayoutChanges()
        {
            _layoutDirty = false;
            _layoutNodeIds.Clear();
            _layoutPositions.Clear();
        }

        private static Vector2 NodePosition(BehaviorTreeNode node)
            => new Vector2(node.style.left.value.value, node.style.top.value.value);

        private void RefreshCatalog()
        {
            var previous = _projectCatalog;
            _projectCatalog = ProjectNodeCatalogCache.Current;
            if (_session != null && previous != _projectCatalog && ReferenceEquals(_registry, previous?.Registry))
            {
                _registry = _projectCatalog.Registry;
                _session.RebindRegistry(_registry, ValidateProjectDocument);
                if (previous.Registry.Hash != _projectCatalog.Registry.Hash || !previous.Problems.SequenceEqual(_projectCatalog.Problems)) RefreshSession();
                else RefreshValidation();
            }
            _catalogDiagnostics.text = string.Join("\n", _projectCatalog.Problems);
            _catalogDiagnostics.style.display = _projectCatalog.Problems.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _catalog.Clear();
            foreach (var path in AssetDatabase.GetAllAssetPaths().Where(TreeSourcePaths.IsTree)
                         .Where(path => !path.StartsWith("Assets/AIBT/Tests/", StringComparison.OrdinalIgnoreCase) &&
                                        !path.StartsWith("Packages/com.azzazello.aibt/Tests/", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(p => p, StringComparer.Ordinal))
                _catalog.Add(TreeCatalogEntry.Read(path, _projectCatalog.Registry));
            var selectedTag = _tag.value;
            _tag.choices = new[] { "All tags" }.Concat(_catalog.SelectMany(e => e.Tags).Distinct().OrderBy(t => t)).ToList();
            _tag.SetValueWithoutNotify(_tag.choices.Contains(selectedTag) ? selectedTag : "All tags");
            FilterCatalog();
        }

        private DiagnosticCollection ValidateProjectDocument(TreeDocument document)
            => _projectCatalog != null && ReferenceEquals(_registry, _projectCatalog.Registry)
                ? _projectCatalog.ValidateAuthoring(document, CompilerOptions())
                : ReferenceCompiler.Compile(document, _registry, CompilerOptions()).Diagnostics;

        private void FilterCatalog()
        {
            _filtered.Clear();
            _filtered.AddRange(_catalog.Where(e => e.Matches(_search.value, _tag.value, _status.value)));
            _treeList.Rebuild();
        }

        private void ChooseTree()
        {
            var path = EditorUtility.OpenFilePanelWithFilters("Open AIBT Tree", Application.dataPath,
                new[] { "AIBT trees", "aibt,json" });
            if (string.IsNullOrEmpty(path)) return;
            var relative = FileUtil.GetProjectRelativePath(path);
            OpenFromPath(string.IsNullOrEmpty(relative) ? path : relative);
        }

        private void EnsureView()
        {
            if (_view != null)
            {
                return;
            }

            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(NewTree) { text = "New" });
            toolbar.Add(new ToolbarButton(ChooseTree) { text = "Open Tree", name = "aibt-open-tree" });
            toolbar.Add(new ToolbarButton(SaveTree) { text = "Save", name = "aibt-save-tree" });
            toolbar.Add(new ToolbarButton(SaveLayout) { text = "Save Layout", name = "aibt-save-layout" });
            toolbar.Add(new ToolbarButton(ValidateNative) { text = "Check Native", name = "aibt-check-native" });
            toolbar.Add(new ToolbarButton(BakeRuntimeAsset) { text = "Prepare Runtime", name = "aibt-prepare-runtime" });
            toolbar.Add(new ToolbarButton(PreviewCurrent) { text = "Preview", name = "aibt-preview-current" });
            toolbar.Add(new ToolbarButton(AttachSelectedHost) { text = "Debug Selected", name = "aibt-debug-selected" });
            toolbar.Add(new ToolbarButton(() => { RefreshCatalog(); if (!string.IsNullOrEmpty(_sourcePath)) OpenFromPath(_sourcePath); }) { text = "Reload" });
            toolbar.Add(new ToolbarButton(() => _view.FrameAll()) { text = "Frame All", name = "aibt-frame-all" });
            rootVisualElement.Add(toolbar);
            var editToolbar = new Toolbar();
            editToolbar.Add(new ToolbarButton(AddChild) { text = "Add Child" });
            editToolbar.Add(new ToolbarButton(RemoveSelected) { text = "Remove Branch" });
            editToolbar.Add(new ToolbarButton(DetachSelected) { text = "Detach" });
            editToolbar.Add(new ToolbarButton(AttachSelected) { text = "Attach To" });
            editToolbar.Add(new ToolbarButton(SetSelectedRoot) { text = "Set Root" });
            editToolbar.Add(new ToolbarButton(() => MoveSelected(-1)) { text = "Earlier" });
            editToolbar.Add(new ToolbarButton(() => MoveSelected(1)) { text = "Later" });
            editToolbar.Add(new ToolbarButton(() => { if (_session?.CanUndo == true) { _session.Undo(); RefreshSession(); } }) { text = "Undo" });
            editToolbar.Add(new ToolbarButton(() => { if (_session?.CanRedo == true) { _session.Redo(); RefreshSession(); } }) { text = "Redo" });
            editToolbar.Add(new ToolbarButton(() => ChangeLayoutHistory(false)) { text = "Undo Layout", name = "aibt-undo-layout" });
            editToolbar.Add(new ToolbarButton(() => ChangeLayoutHistory(true)) { text = "Redo Layout", name = "aibt-redo-layout" });
            rootVisualElement.Add(editToolbar);

            var body = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            var sidebar = new VisualElement { name = "aibt-tree-browser", style = { width = 320, minWidth = 220, flexShrink = 0 } };
            sidebar.Add(new Label("PROJECT TREES") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            _catalogDiagnostics = new Label { style = { whiteSpace = WhiteSpace.Normal, color = new Color(1f, 0.65f, 0.25f) } };
            sidebar.Add(_catalogDiagnostics);
            _search = new ToolbarSearchField { name = "aibt-tree-search" };
            _search.RegisterValueChangedCallback(_ => FilterCatalog());
            sidebar.Add(_search);
            _tag = new PopupField<string>("Tag", new List<string> { "All tags" }, 0);
            _status = new PopupField<string>("State", new List<string> { "All states", "Valid", "Warnings", "Errors", "Needs registry", "Needs native compile" }, 0);
            _tag.RegisterValueChangedCallback(_ => FilterCatalog());
            _status.RegisterValueChangedCallback(_ => FilterCatalog());
            sidebar.Add(_tag);
            sidebar.Add(_status);
            _treeList = new ListView(_filtered, 66,
                () => new Label { style = { whiteSpace = WhiteSpace.Normal } },
                (element, index) =>
                {
                    var entry = _filtered[index];
                    ((Label)element).text = entry.Name + "  [" + entry.Status + "]\n" + entry.TreeId + "\n" + entry.Path;
                    element.tooltip = entry.Details;
                }) { name = "aibt-tree-list", virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight, style = { flexGrow = 1 } };
            _treeList.selectionChanged += selected =>
            {
                var entry = selected.OfType<TreeCatalogEntry>().FirstOrDefault();
                if (entry != null) OpenFromPath(entry.Path);
            };
            sidebar.Add(_treeList);
            sidebar.Add(new Label("Search: name, ID or path.\nSamples~ appear after import; Open Tree can read them directly.") { style = { whiteSpace = WhiteSpace.Normal } });
            body.Add(sidebar);
            var content = new VisualElement { style = { flexGrow = 1, minWidth = 0 } };
            _documentLabel = new Label("No tree selected") { name = "aibt-source-path" };
            content.Add(_documentLabel);
            _emptyLabel = new Label("Select a tree on the left or click Open Tree.\nPan: middle mouse • Zoom: wheel • Select: click • Frame: Frame All")
                { name = "aibt-empty-state", style = { whiteSpace = WhiteSpace.Normal, paddingTop = 12, paddingBottom = 12 } };
            content.Add(_emptyLabel);
            _view = new BehaviorTreeGraphView { name = "aibt-graph-view" };
            _view.CanConnectNodes = (parent, child) => _session != null && _session.CanConnect(parent, child);
            _view.style.flexGrow = 1;
            _view.graphViewChanged = change =>
            {
                if (_session != null && change.edgesToCreate != null && change.edgesToCreate.Count > 0)
                {
                    foreach (var edge in change.edgesToCreate)
                        if (edge.output?.node is BehaviorTreeNode parent && edge.input?.node is BehaviorTreeNode child)
                            _session.TryConnect(parent.NodeId, child.NodeId);
                    change.edgesToCreate.Clear();
                    _view.schedule.Execute(RefreshSession);
                }
                if (_session != null && change.movedElements != null && change.movedElements.OfType<BehaviorTreeNode>().Any())
                {
                    _layoutDirty = true;
                    hasUnsavedChanges = true;
                    RecordLayoutMove();
                }
                return change;
            };
            _view.RegisterCallback<MouseUpEvent>(_ => _view.schedule.Execute(RefreshInspector));
            _diagnosticLabel = new Label { name = "aibt-graph-diagnostics" };
            _diagnosticLabel.style.whiteSpace = WhiteSpace.Normal;
            _diagnosticLabel.style.display = DisplayStyle.None;
            _validationLabel = new Label { name = "aibt-validation" };
            _validationLabel.style.whiteSpace = WhiteSpace.Normal;
            _validationLabel.style.display = DisplayStyle.None;
            content.Add(_diagnosticLabel);
            content.Add(_validationLabel);
            _nativeValidationLabel = new Label { name = "aibt-native-validation", style = { whiteSpace = WhiteSpace.Normal, display = DisplayStyle.None } };
            content.Add(_nativeValidationLabel);
            content.Add(_view);
            body.Add(content);
            _inspector = new ScrollView { style = { width = 260, flexShrink = 0 } };
            body.Add(_inspector);
            rootVisualElement.Add(body);
            RefreshCatalog();
        }
    }
}
