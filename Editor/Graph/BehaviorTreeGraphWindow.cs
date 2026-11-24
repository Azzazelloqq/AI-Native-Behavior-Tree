using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Layout;
using AIBT.Editor.Organization;
using AIBT.Editor.Registry;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    /// <summary>
    /// Hosts an authoring graph over an AIBT tree document. One toolbar, one Save for the tree and
    /// its layout, one undo history (wired into Unity's Edit &gt; Undo), and no modal dialogs on the
    /// open path: unsaved work is protected by a non-blocking banner or a second window.
    /// </summary>
    public sealed partial class BehaviorTreeGraphWindow : EditorWindow
    {
        private const string WindowTitle = "AIBT Graph";

        private BehaviorTreeGraphView _view;
        private GraphEditSession _session;
        private LayoutHistory _layoutHistory;
        private NodeRegistry _registry;
        private ProjectNodeCatalog _projectCatalog;
        private readonly GraphEditHistory _history = new GraphEditHistory();
        private readonly HashSet<NodeId> _pinned = new HashSet<NodeId>();
        private readonly Dictionary<NodeId, Vector2> _pendingPlacement = new Dictionary<NodeId, Vector2>();
        private bool _layoutIsDefault;
        private bool _restoringFromUndo;
        private bool _restorePending;
        [SerializeField] private GraphUndoMarker _undoMarker;
        [SerializeField] private string _sourcePath;
        [SerializeField] private string _sourceGuid;
        [SerializeField] private string _draftBytes;
        [SerializeField] private string _draftBaseBytes;
        [SerializeField] private string _layoutBaseBytes;
        [SerializeField] private bool _layoutDirty;
        [SerializeField] private bool _hideTreeList;
        [SerializeField] private List<string> _layoutNodeIds = new List<string>();
        [SerializeField] private List<Vector2> _layoutPositions = new List<Vector2>();

        public TreeDocument Document { get; private set; }

        public DiagnosticCollection Diagnostics { get; private set; }

        public BehaviorTreeGraphView View { get { EnsureView(); return _view; } }
        internal event Action ViewInvalidated;
        internal NodeRegistry CurrentRegistry => _registry;
        internal string SourcePath => _sourcePath;
        internal bool HasPendingChanges => Document != null && (_session?.IsDirty == true || _layoutDirty);
        internal bool CanUndoEdit => _history.CanUndo;
        internal bool CanRedoEdit => _history.CanRedo;
        internal void ReleaseExecutionView() => ViewInvalidated?.Invoke();

        [MenuItem("AIBT/Graph Editor")]
        public static BehaviorTreeGraphWindow ShowWindow()
        {
            var window = GetWindow<BehaviorTreeGraphWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            return window;
        }

        /// <summary>
        /// Opens a tree without ever blocking: reuses the window that shows it, else a window
        /// without unsaved changes, else opens a new graph window next to the others.
        /// </summary>
        public static BehaviorTreeGraphWindow OpenTree(string path)
        {
            var windows = Resources.FindObjectsOfTypeAll<BehaviorTreeGraphWindow>();
            foreach (var window in windows)
            {
                window.EnsureView();
                window.RestorePendingDocument();
            }
            var target = windows.FirstOrDefault(window => SamePath(window._sourcePath, path))
                         ?? windows.FirstOrDefault(window => !window.HasPendingChanges)
                         ?? CreateWindow<BehaviorTreeGraphWindow>(WindowTitle, typeof(BehaviorTreeGraphWindow));
            target.titleContent = new GUIContent(WindowTitle);
            target.Show();
            target.Focus();
            if (!SamePath(target._sourcePath, path) || target.Document == null) target.OpenFromPath(path);
            return target;
        }

        public void OpenFromPath(string path) => OpenFromPath(path, (_projectCatalog ?? ProjectNodeCatalogCache.Current).Registry);

        public void OpenFromPath(string path, NodeRegistry registry)
        {
            EnsureView();
            RestorePendingDocument();
            if (SamePath(path, _sourcePath) && Document != null && HasPendingChanges) return;
            if (HasPendingChanges)
            {
                OfferPendingOpen(Path.GetFileName(path), () => OpenFromPathNow(path, registry));
                return;
            }

            OpenFromPathNow(path, registry);
        }

        public void OpenFromBytes(byte[] utf8, string documentId, NodeRegistry registry)
        {
            EnsureView();
            RestorePendingDocument();
            if (HasPendingChanges)
            {
                OfferPendingOpen(documentId, () => OpenFromBytesNow(utf8, documentId, registry));
                return;
            }

            OpenFromBytesNow(utf8, documentId, registry);
        }

        private void OpenFromPathNow(string path, NodeRegistry registry)
        {
            ReleaseExecutionView();
            ResetLayoutChanges();
            HideBanner();
            // Reloading the same file keeps the breadcrumb; opening another tree here leaves the subtree view.
            if (!SamePath(path, _sourcePath)) ClearNavigation();
            _sourcePath = path;
            _sourceGuid = AssetDatabase.AssetPathToGUID(path);
            try
            {
                if (!TreeSourcePaths.IsTree(path)) throw new ArgumentException("Choose an .aibt or .aibt.json tree.");
                OpenDocument(File.ReadAllBytes(path), path, registry, path);
                FrameAllSoon();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                _view.ClearDocument();
                Document = null;
                _session = null;
                _draftBytes = null;
                _draftBaseBytes = null;
                hasUnsavedChanges = false;
                Diagnostics = DiagnosticCollection.Empty;
                _layoutHistory = null;
                _extraMessages.Clear();
                _extraMessages.Add(new GraphMessage(DiagnosticSeverity.Error, exception.Message));
                RefreshChrome();
                _emptyLabel.text = "The tree could not be opened. See the problem below.";
                _emptyLabel.style.display = DisplayStyle.Flex;
            }
        }

        private void OpenFromBytesNow(byte[] utf8, string documentId, NodeRegistry registry)
        {
            ResetLayoutChanges();
            HideBanner();
            ClearNavigation();
            _sourcePath = null;
            _sourceGuid = null;
            OpenDocument(utf8, documentId, registry, null);
        }

        private void OpenDocument(byte[] utf8, string documentId, NodeRegistry registry, string treePath)
        {
            EnsureView();
            ReleaseExecutionView();
            _live?.Detach();
            _view.ClearDocument();
            _nativeMessage = null;
            _extraMessages.Clear();
            _pinned.Clear();
            _pendingPlacement.Clear();
            ClearPendingSubtrees();
            ForgetTreeIndex();
            ResetHistory();

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

            LayoutDocument layout = null;
            var canRender = Document != null;
            if (canRender && treePath != null)
            {
                var layoutPath = LayoutPersistenceController.LayoutPathFor(treePath);
                _layoutBaseBytes = File.Exists(layoutPath) ? Convert.ToBase64String(File.ReadAllBytes(layoutPath)) : null;
                var loaded = LayoutPersistenceController.Load(treePath, Document);
                Diagnostics = new DiagnosticCollection(Diagnostics.Concat(loaded.Diagnostics));
                // A broken presentation file must not hide an otherwise readable tree.
                // Keep its diagnostics visible and use the default placement.
                if (loaded.Success && !loaded.UsedDefault) layout = loaded.Document;
            }

            _layoutIsDefault = layout == null;
            if (layout != null)
                foreach (var pair in layout.Nodes.Where(pair => pair.Value.Pinned)) _pinned.Add(pair.Key);
            if (canRender)
            {
                _view.Editable = _session != null;
                _view.Populate(Document, registry, layout);
                _layoutHistory = new LayoutHistory(LayoutSnapshot());
                if (registry != null)
                {
                    var missing = Document.Nodes.Where(n => !n.IsSubtreeReference && !registry.TryGet(n.TypeId, out _)).Select(n => n.TypeId).Distinct().ToArray();
                    if (missing.Length > 0)
                        _extraMessages.Add(new GraphMessage(DiagnosticSeverity.Info,
                            "Project node contracts unresolved in this view (not a runtime failure): " + string.Join(", ", missing)));
                }
            }
            else _layoutHistory = null;

            RefreshChrome();
            TryAutoAttachSelectedAgent();
        }

        private void OnEnable()
        {
            EnsureView();
            if (_undoMarker == null) _undoMarker = CreateInstance<GraphUndoMarker>();
            _undoMarker.hideFlags = HideFlags.HideAndDontSave;
            _undoMarker.Position = 0;
            Undo.ClearUndo(_undoMarker);
            Undo.undoRedoPerformed += OnUnityUndoRedo;
            ProjectNodeCatalogCache.Changed += RefreshCatalog;
            EditorApplication.projectChanged += ProjectChanged;
            _restorePending = true;
            EditorApplication.update += RestoreDocument;
            EnableLiveDebugging();
        }

        private void OnDisable()
        {
            ReleaseExecutionView();
            DisableLiveDebugging();
            Undo.undoRedoPerformed -= OnUnityUndoRedo;
            ProjectNodeCatalogCache.Changed -= RefreshCatalog;
            EditorApplication.projectChanged -= ProjectChanged;
            EditorApplication.update -= RestoreDocument;
            EditorApplication.update -= FrameWhenLaidOut;
        }

        private void OnDestroy()
        {
            if (_undoMarker == null) return;
            Undo.ClearUndo(_undoMarker);
            DestroyImmediate(_undoMarker);
            _undoMarker = null;
        }

        // A domain reload restores the window before its delayed restore runs; an open request in
        // between must see the restored draft, not an empty window.
        private void RestorePendingDocument()
        {
            EditorApplication.update -= RestoreDocument;
            if (_restorePending) RestoreDocument();
        }

        private void RestoreDocument()
        {
            // Runs once, on the first Editor update after enable (delayCall is not guaranteed to run promptly).
            EditorApplication.update -= RestoreDocument;
            if (this == null || !_restorePending) return;
            _restorePending = false;
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
                    Document = _session.Document;
                    Diagnostics = _session.Diagnostics;
                    _view.Editable = true;
                    _view.Populate(Document, _registry, null);
                    _layoutIsDefault = !restoreLayout;
                    _layoutHistory = new LayoutHistory(LayoutSnapshot());
                    ResetHistory();
                    RestoreLayoutPositions(savedIds, savedPositions, restoreLayout);
                    OnDocumentChanged();
                    FrameAllSoon();
                    return;
                }
            }
            _layoutDirty = false;
            if (!string.IsNullOrEmpty(_sourcePath)) OpenFromPathNow(_sourcePath, (_projectCatalog ?? ProjectNodeCatalogCache.Current).Registry);
            if (restoreLayout) _layoutBaseBytes = layoutBaseline;
            RestoreLayoutPositions(savedIds, savedPositions, restoreLayout);
            RefreshChrome();
        }

        private void ProjectChanged()
        {
            RefreshCatalog();
            ForgetTreeIndex();
            RevalidateSubtrees();
            if (!string.IsNullOrEmpty(_sourceGuid))
            {
                var moved = AssetDatabase.GUIDToAssetPath(_sourceGuid);
                if (!string.IsNullOrEmpty(moved) && moved != _sourcePath)
                {
                    _sourcePath = moved;
                    RefreshChrome();
                }
            }
            if (string.IsNullOrEmpty(_sourcePath)) return;
            if (_session == null)
            {
                if (File.Exists(_sourcePath)) OpenFromPathNow(_sourcePath, _registry ?? (_projectCatalog ?? ProjectNodeCatalogCache.Current).Registry);
                return;
            }
            var baseline = _session.SavedBytes;
            try
            {
                var changedOnDisk = baseline != null && (!File.Exists(_sourcePath) || !File.ReadAllBytes(_sourcePath).SequenceEqual(baseline));
                if (!changedOnDisk) return;
                if (!HasPendingChanges) OpenFromPathNow(_sourcePath, _registry ?? (_projectCatalog ?? ProjectNodeCatalogCache.Current).Registry);
                else ShowBanner("The tree changed on disk while you have unsaved edits here. Reloading discards your edits; saving is refused until you reload.",
                    BannerKind.Warning, ("Reload from disk", ReloadDiscardingChanges), ("Keep editing", HideBanner));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // Keep the currently displayed document until the source can be read again.
            }
        }

        // ADR-P9-001, second amendment 2026-11-24: the edit session checks a tree with the default the tree list
        // uses (the native default; ValidateAuthoring switches a tree with a managed node to the managed policy).
        // The Graph window does not read .aibt/policy.json.
        internal static ReferenceCompilerOptions CompilerOptions() => new ReferenceCompilerOptions(
            "trees/editor.aibt.json", ProjectNodeCatalog.NativeDefaultPolicy, new CompiledCompilerVersion(1, 0, 0, 0));

        // ---------------------------------------------------------------- open / new / save

        private void OfferPendingOpen(string target, Action open)
        {
            var current = string.IsNullOrEmpty(_sourcePath) ? "this tree" : Path.GetFileName(_sourcePath);
            ShowBanner("Unsaved changes in " + current + ". Open " + target + "?", BannerKind.Warning,
                ("Save and open", () => { if (SaveAll()) open(); }),
                ("Discard and open", () => { DiscardPendingChanges(); open(); }),
                ("Cancel", HideBanner));
        }

        private void ReloadDiscardingChanges()
        {
            var path = _sourcePath;
            DiscardPendingChanges();
            if (!string.IsNullOrEmpty(path)) OpenFromPathNow(path, _registry ?? (_projectCatalog ?? ProjectNodeCatalogCache.Current).Registry);
        }

        private void DiscardPendingChanges()
        {
            _draftBytes = null;
            _draftBaseBytes = null;
            _session = null;
            ResetLayoutChanges();
            hasUnsavedChanges = false;
            HideBanner();
        }

        private void NewTree()
        {
            RestorePendingDocument();
            if (HasPendingChanges)
            {
                OfferPendingOpen("a new tree", NewTreeNow);
                return;
            }

            NewTreeNow();
        }

        private void NewTreeNow()
        {
            var path = EditorUtility.SaveFilePanelInProject("Create AIBT tree", "NewTree.aibt", "aibt", "Choose a tree location.");
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path))
            {
                ShowBanner("Choose a new file; existing trees are not overwritten.", BannerKind.Error, ("OK", HideBanner));
                return;
            }
            CreateTreeAt(path, (_projectCatalog ?? ProjectNodeCatalogCache.Current).Registry);
        }

        /// <summary>Starts an unsaved tree with a memory-sequence root; Save writes it to <paramref name="path"/>.</summary>
        internal void CreateTreeAt(string path, NodeRegistry registry)
        {
            EnsureView();
            ReleaseExecutionView();
            _registry = registry;
            var root = new NodeDocument(new NodeId("root"), "aibt.core.memory-sequence", 1,
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var document = TreeDocument.CreateVersion2(
                new TreeId("tree." + Guid.NewGuid().ToString("N")), Path.GetFileNameWithoutExtension(path),
                root.Id, new[] { root }, null, null, tags: TagSet.Empty, metadata: SemanticObject.Empty);
            _sourcePath = path;
            _sourceGuid = null;
            ClearNavigation();
            ClearPendingSubtrees();
            ResetLayoutChanges();
            HideBanner();
            _layoutBaseBytes = null;
            _session = new GraphEditSession(document, _registry, CompilerOptions(), null, ValidateProjectDocument);
            Document = _session.Document;
            _view.ClearDocument();
            _view.Editable = true;
            _view.Populate(Document, _registry, null);
            _layoutIsDefault = true;
            _layoutHistory = new LayoutHistory(LayoutSnapshot());
            ResetHistory();
            OnDocumentChanged();
            FrameAllSoon();
        }

        /// <summary>Saves the tree (when edited) and then its layout (when moved). Returns false and shows why when it cannot.</summary>
        internal bool SaveAll()
        {
            if (_session == null || string.IsNullOrEmpty(_sourcePath))
            {
                Notify(Document == null ? "Nothing to save." : "This view is read-only; open the tree from a file to edit it.");
                return false;
            }

            try
            {
                var savedTree = false;
                if (_session.IsDirty)
                {
                    // New subtree files from Extract are written (and imported) before the tree that uses them.
                    WritePendingSubtrees();
                    _session.Save(_sourcePath);
                    savedTree = true;
                }
                var savedLayout = _layoutDirty && WriteLayout();
                if (savedTree || savedLayout)
                {
                    Import(_sourcePath);
                    if (savedLayout) Import(LayoutPersistenceController.LayoutPathFor(_sourcePath));
                    _sourceGuid = AssetDatabase.AssetPathToGUID(_sourcePath);
                }
                _draftBytes = null;
                _draftBaseBytes = null;
                HideBanner();
                UpdateDirtyState();
                RefreshChrome();
                Notify(savedTree || savedLayout ? "Saved " + Path.GetFileName(_sourcePath) : "No changes to save");
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                              exception is InvalidOperationException || exception is ArgumentException)
            {
                var conflict = exception is IOException && exception.Message.StartsWith("The file changed outside", StringComparison.Ordinal);
                if (conflict)
                    ShowBanner("Not saved: " + exception.Message, BannerKind.Error,
                        ("Reload from disk", ReloadDiscardingChanges), ("Keep editing", HideBanner));
                else ShowBanner("Not saved: " + exception.Message, BannerKind.Error, ("OK", HideBanner));
                return false;
            }
        }

        private static void Import(string path)
        {
            var relative = path.Replace('\\', '/');
            if (Path.IsPathRooted(relative)) relative = FileUtil.GetProjectRelativePath(relative);
            if (!string.IsNullOrEmpty(relative) && (relative.StartsWith("Assets/", StringComparison.Ordinal) || relative.StartsWith("Packages/", StringComparison.Ordinal)))
                AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceSynchronousImport);
        }

        private bool WriteLayout()
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
                var position = BehaviorTreeGraphView.NodePosition(pair.Value);
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
            return true;
        }

        public override void SaveChanges()
        {
            SaveAll();
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            _draftBytes = null;
            _draftBaseBytes = null;
            ResetLayoutChanges();
            hasUnsavedChanges = false;
            base.DiscardChanges();
        }

        [Shortcut("AIBT Graph/Save", typeof(BehaviorTreeGraphWindow), KeyCode.S, ShortcutModifiers.Action)]
        private static void SaveShortcut(ShortcutArguments arguments) => (arguments.context as BehaviorTreeGraphWindow)?.SaveAll();

        [Shortcut("AIBT Graph/Auto Layout", typeof(BehaviorTreeGraphWindow), KeyCode.L, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        private static void AutoLayoutShortcut(ShortcutArguments arguments) => (arguments.context as BehaviorTreeGraphWindow)?.AutoLayout();

        // ---------------------------------------------------------------- semantic edits and history

        /// <summary>Applies one semantic edit as one undo step. Returns false when nothing changed or the edit was refused.</summary>
        internal bool ApplyEdit(Func<TreeDocument, TreeDocument> edit)
        {
            if (_session == null) return false;
            TreeDocument candidate;
            try
            {
                candidate = edit(_session.Document);
                if (candidate == null || ReferenceEquals(candidate, _session.Document)) return false;
                _session.Apply(_ => candidate);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                Notify(exception.Message);
                return false;
            }

            RecordHistory(GraphEditKind.Semantic);
            OnDocumentChanged();
            return true;
        }

        /// <summary>Steps back one edit through Unity's undo stack (the same path as Ctrl+Z).</summary>
        internal void UndoEdit()
        {
            if (!_history.CanUndo) return;
            var before = _history.Position;
            Undo.PerformUndo();
            if (_history.Position == before) StepUndo(); // Unity's stack lost our record (for example, after clearing it)
            SyncMarker();
        }

        internal void RedoEdit()
        {
            if (!_history.CanRedo) return;
            var before = _history.Position;
            Undo.PerformRedo();
            if (_history.Position == before) StepRedo();
            SyncMarker();
        }

        private void RecordHistory(GraphEditKind kind)
        {
            _history.Record(kind);
            if (_undoMarker == null) return;
            Undo.RegisterCompleteObjectUndo(_undoMarker, kind == GraphEditKind.Semantic ? "Edit AIBT Tree" : "Move AIBT Nodes");
            _undoMarker.Position = _history.Position;
            Undo.IncrementCurrentGroup();
        }

        private void OnUnityUndoRedo()
        {
            if (_undoMarker == null || _restoringFromUndo || (_session == null && _layoutHistory == null)) return;
            var target = _undoMarker.Position;
            _restoringFromUndo = true;
            try
            {
                while (_history.Position > target && _history.CanUndo) StepUndo();
                while (_history.Position < target && _history.CanRedo) StepRedo();
            }
            finally
            {
                _restoringFromUndo = false;
            }
        }

        private void StepUndo()
        {
            if (_history.Undo() == GraphEditKind.Semantic)
            {
                if (_session != null && _session.CanUndo) _session.Undo();
                OnDocumentChanged();
            }
            else if (_layoutHistory != null && _layoutHistory.CanUndo) ApplyLayout(_layoutHistory.Undo());
        }

        private void StepRedo()
        {
            if (_history.Redo() == GraphEditKind.Semantic)
            {
                if (_session != null && _session.CanRedo) _session.Redo();
                OnDocumentChanged();
            }
            else if (_layoutHistory != null && _layoutHistory.CanRedo) ApplyLayout(_layoutHistory.Redo());
        }

        private void SyncMarker()
        {
            if (_undoMarker != null) _undoMarker.Position = _history.Position;
        }

        private void ResetHistory()
        {
            _history.Clear();
            if (_undoMarker == null) return;
            Undo.ClearUndo(_undoMarker);
            _undoMarker.Position = 0;
        }

        /// <summary>Brings the graph and panels in line with the session's current document, without a rebuild.</summary>
        private void OnDocumentChanged()
        {
            if (_session == null) return;
            ReleaseExecutionView();
            Document = _session.Document;
            Diagnostics = _session.Diagnostics;
            _live?.InvalidateMapping();
            _nativeMessage = null;
            var positions = CurrentPositions();
            var placedNew = false;
            _view.Sync(Document, _registry, id =>
            {
                placedNew = true;
                if (_pendingPlacement.TryGetValue(id, out var requested)) return requested;
                if (_layoutHistory != null && _layoutHistory.Current.Nodes.TryGetValue(id, out var previous))
                    return new Vector2(previous.Position.X, previous.Position.Y);
                var parent = GraphDocumentEdits.ParentOf(Document, id);
                var place = parent != null ? GraphLayout.PlaceChild(Document, parent.Id, id, positions) : Vector2.zero;
                positions[id] = place;
                return place;
            });
            _pendingPlacement.Clear();
            if (_layoutIsDefault) ApplyPositions(GraphLayout.Arrange(Document));
            else if (placedNew) _layoutDirty = true;
            if (_layoutHistory == null || _layoutHistory.Current.TreeId != Document.TreeId) _layoutHistory = new LayoutHistory(LayoutSnapshot());
            else _layoutHistory.IncludeNodes(LayoutSnapshot().Nodes);
            CaptureLayoutPositions();
            UpdateDirtyState();
            RefreshChrome();
        }

        private void UpdateDirtyState()
        {
            var dirty = _session?.IsDirty == true;
            hasUnsavedChanges = dirty || _layoutDirty;
            saveChangesMessage = "Save the AIBT tree and its layout before closing?";
            _draftBytes = dirty ? Convert.ToBase64String(CanonicalTreeJson.Serialize(Document).Utf8) : null;
            var savedBytes = _session?.SavedBytes;
            _draftBaseBytes = dirty && savedBytes != null ? Convert.ToBase64String(savedBytes) : null;
        }

        // ---------------------------------------------------------------- layout

        /// <summary>Re-arranges every unpinned node as a tidy top-down tree. One undo step; never changes child order.</summary>
        internal void AutoLayout()
        {
            if (Document == null || _layoutHistory == null) return;
            var keep = _view.NodesById.Where(pair => _pinned.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => BehaviorTreeGraphView.NodePosition(pair.Value));
            ApplyPositions(GraphLayout.Arrange(Document, keep));
            _layoutIsDefault = keep.Count == 0;
            RecordLayoutChange();
            FrameAllSoon();
        }

        private void RecordLayoutChange()
        {
            if (_layoutHistory == null) return;
            _layoutDirty = _session != null;
            _layoutHistory.Do(LayoutSnapshot());
            RecordHistory(GraphEditKind.Layout);
            CaptureLayoutPositions();
            UpdateDirtyState();
            RefreshToolbarState();
        }

        private void OnNodesMoved()
        {
            _layoutIsDefault = false;
            RecordLayoutChange();
        }

        private void ApplyLayout(LayoutDocument layout)
        {
            ApplyPositions(layout.Nodes.ToDictionary(pair => pair.Key, pair => new Vector2(pair.Value.Position.X, pair.Value.Position.Y)));
            _layoutDirty = _session != null;
            _layoutIsDefault = false;
            CaptureLayoutPositions();
            UpdateDirtyState();
            RefreshToolbarState();
        }

        private void ApplyPositions(IReadOnlyDictionary<NodeId, Vector2> positions)
        {
            foreach (var pair in positions)
                if (_view.NodesById.TryGetValue(pair.Key, out var node)) BehaviorTreeGraphView.SetNodePosition(node, pair.Value);
            _view.MarkDirtyRepaint();
        }

        private Dictionary<NodeId, Vector2> CurrentPositions()
            => _view.NodesById.ToDictionary(pair => pair.Key, pair => BehaviorTreeGraphView.NodePosition(pair.Value));

        private void CaptureLayoutPositions()
        {
            _layoutNodeIds.Clear();
            _layoutPositions.Clear();
            if (!_layoutDirty) return;
            foreach (var pair in _view.NodesById)
            {
                _layoutNodeIds.Add(pair.Key.Value);
                _layoutPositions.Add(BehaviorTreeGraphView.NodePosition(pair.Value));
            }
        }

        private LayoutDocument LayoutSnapshot()
        {
            var nodes = _view.NodesById.ToDictionary(pair => pair.Key, pair =>
            {
                var position = BehaviorTreeGraphView.NodePosition(pair.Value);
                return new LayoutNodePlacement(new LayoutPoint(position.x, position.y), _pinned.Contains(pair.Key));
            });
            return new LayoutDocument(Document.TreeId, LayoutDirection.TopToBottom, nodes);
        }

        private void RestoreLayoutPositions(string[] ids, Vector2[] positions, bool wasDirty)
        {
            if (!wasDirty || Document == null) return;
            for (var index = 0; index < Math.Min(ids.Length, positions.Length); index++)
            {
                var id = new NodeId(ids[index]);
                if (_view.NodesById.TryGetValue(id, out var node)) BehaviorTreeGraphView.SetNodePosition(node, positions[index]);
            }
            _layoutDirty = true;
            _layoutIsDefault = false;
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

        private void FrameAllSoon()
        {
            // Framing needs a laid-out view and laid-out cards; after a domain reload the window may not have a size yet.
            _framePending = true;
            EditorApplication.update -= FrameWhenLaidOut;
            EditorApplication.update += FrameWhenLaidOut;
        }

        private bool _framePending;
        private int _frameTicks;

        // Frames the whole tree on the first Editor update where the view has a size.
        private void FrameWhenLaidOut()
        {
            if (this == null || _view == null || !_framePending || ++_frameTicks > 200)
            {
                EditorApplication.update -= FrameWhenLaidOut;
                _frameTicks = 0;
                return;
            }
            if (float.IsNaN(_view.layout.width) || _view.layout.width < 1f || _view.NodesById.Count == 0) return;
            if (_view.NodesById.Values.Any(node => float.IsNaN(node.layout.width) || node.layout.width < 1f)) return;
            _framePending = false;
            _frameTicks = 0;
            EditorApplication.update -= FrameWhenLaidOut;
            _view.FrameAll();
        }

        private static bool SamePath(string left, string right)
            => !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right) &&
               string.Equals(left.Replace('\\', '/'), right.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

        private DiagnosticCollection ValidateProjectDocument(TreeDocument document)
            => ValidateExpandedDocument(document, candidate => _projectCatalog != null && ReferenceEquals(_registry, _projectCatalog.Registry)
                ? _projectCatalog.ValidateAuthoring(candidate, CompilerOptions())
                : ReferenceCompiler.Compile(candidate, _registry, CompilerOptions()).Diagnostics);

        // A subtree file changed: validation and reference cards read it again.
        private void RevalidateSubtrees()
        {
            if (Document == null || _view == null || !SubtreeExpander.RequiresExpansion(Document)) return;
            if (_session != null)
            {
                _session.RebindRegistry(_registry, ValidateProjectDocument);
                Document = _session.Document;
                Diagnostics = _session.Diagnostics;
            }
            _view.Sync(Document, _registry, _ => Vector2.zero);
            RefreshChrome();
        }

        private void Notify(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            ShowNotification(new GUIContent(message), 2.5);
        }
    }
}
