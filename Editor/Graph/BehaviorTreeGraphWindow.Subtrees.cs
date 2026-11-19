using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Agents;
using AIBT.Authoring;
using AIBT.Editor.Registry;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    // Subtrees in the graph editor (ADR-P11-001 §4.7): reference cards, the "Subtree…" palette
    // entry, the key-map inspector, navigation into a subtree with a breadcrumb back, Extract and
    // Unpack, and live debugging of one subtree instance. Navigation is view state of this window
    // only; it is never written to the tree or its layout. (A plain comment: the class summary is
    // the one in BehaviorTreeGraphWindow.cs.)
    public sealed partial class BehaviorTreeGraphWindow
    {
        private const string NotConnectedChoice = "(not connected)";
        private const string ConstantChoice = "Constant value";

        [SerializeField] private List<string> _crumbPaths = new List<string>();
        [SerializeField] private List<string> _crumbLabels = new List<string>();
        [SerializeField] private List<string> _crumbPrefixes = new List<string>();
        [SerializeField] private string _instancePrefix = string.Empty;
        [SerializeField] private List<string> _liveKeyFrom = new List<string>();
        [SerializeField] private List<string> _liveKeyTo = new List<string>();
        [SerializeField] private List<string> _pendingSubtreePaths = new List<string>();
        [SerializeField] private List<string> _pendingSubtreeBytes = new List<string>();

        private ProjectTreeIndex.Snapshot _treeIndex;
        private List<KeyValuePair<string, TreeDocument>> _pendingSubtreeDocuments;
        private SubtreePickerProvider _pickerProvider;
        private VisualElement _breadcrumb;

        /// <summary>Resolves subtree tree IDs; null uses the project's tree files (Assets/ and Packages/).</summary>
        internal ISubtreeResolver SubtreeResolver { get; set; }

        /// <summary>"" for a tree opened on its own; "r:" or "r1:r2:" for a subtree opened from a reference.</summary>
        internal string InstancePrefix => _instancePrefix ?? string.Empty;

        internal IReadOnlyList<string> BreadcrumbLabels => _crumbLabels;

        internal IReadOnlyList<string> BreadcrumbPaths => _crumbPaths;

        /// <summary>Subtree files made by Extract that the next Save writes (when the tree still uses them).</summary>
        internal IReadOnlyList<string> PendingSubtreePaths => _pendingSubtreePaths;

        // ---------------------------------------------------------------- resolution

        /// <summary>The resolver for this window's references: unsaved extracted subtrees first, then the project.</summary>
        internal ISubtreeResolver CurrentSubtreeResolver()
        {
            var inner = SubtreeResolver ?? (_treeIndex ?? (_treeIndex = ProjectTreeIndex.Scan()));
            var pending = PendingSubtrees();
            return pending.Count == 0 ? inner : new PendingSubtreeResolver(pending, inner);
        }

        private void ForgetTreeIndex() => _treeIndex = null;

        private SubtreeResolution ResolveSubtree(TreeId treeId)
        {
            try
            {
                return CurrentSubtreeResolver().Resolve(treeId);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return SubtreeResolution.NotFound(exception.Message);
            }
        }

        private SubtreeCardInfo DescribeSubtree(NodeDocument node)
        {
            if (node?.Subtree == null) return null;
            var resolution = ResolveSubtree(node.Subtree.TreeId);
            switch (resolution.Status)
            {
                case SubtreeResolutionStatus.Found:
                    return new SubtreeCardInfo(resolution.Document.Name, resolution.SourceId, SubtreeResolutionStatus.Found, resolution.Document);
                case SubtreeResolutionStatus.Ambiguous:
                    return new SubtreeCardInfo(null, string.Join(", ", resolution.CandidateSourceIds), SubtreeResolutionStatus.Ambiguous, null);
                default:
                    return new SubtreeCardInfo(null, null, SubtreeResolutionStatus.NotFound, null);
            }
        }

        /// <summary>
        /// Validation of the open document: a tree with references is expanded first (structural
        /// checks, as at import) and, when that succeeds, its expansion is validated like any tree.
        /// </summary>
        private DiagnosticCollection ValidateExpandedDocument(TreeDocument document, Func<TreeDocument, DiagnosticCollection> validate)
        {
            if (!SubtreeExpander.RequiresExpansion(document)) return validate(document);
            var catalog = _projectCatalog ?? ProjectNodeCatalogCache.Current;
            var expansion = ProjectTreeIndex.Expand(SubtreeGraphEdits.EditorDocumentId, document, _registry, catalog.RegisteredTypes,
                CurrentSubtreeResolver());
            if (!expansion.Success) return expansion.Diagnostics;
            var validation = validate(expansion.Document);
            return expansion.Diagnostics.Count == 0 ? validation : new DiagnosticCollection(expansion.Diagnostics.Concat(validation));
        }

        /// <summary>
        /// The node of this window a diagnostic belongs to: a subtree's own problem goes to the
        /// outermost reference that uses it, an expanded node <c>r:n</c> to its reference <c>r</c>.
        /// </summary>
        internal NodeId? ViewNodeOf(Diagnostic diagnostic)
        {
            var location = diagnostic.Location;
            if (Document != null && location.HasDocumentId && location.DocumentId != SubtreeGraphEdits.EditorDocumentId)
            {
                foreach (var related in diagnostic.RelatedLocations)
                {
                    if (related.DocumentId != SubtreeGraphEdits.EditorDocumentId || !related.NodeId.IsValid) continue;
                    var mapped = SubtreeGraphEdits.MapExpandedId(Document, string.Empty, related.NodeId.Value);
                    if (mapped.HasValue) return mapped;
                }
            }
            var node = NodeOf(diagnostic);
            if (!node.HasValue || Document == null) return node;
            return SubtreeGraphEdits.MapExpandedId(Document, string.Empty, node.Value.Value) ?? node;
        }

        private static string ProblemText(Diagnostic diagnostic)
        {
            var text = diagnostic.Code.Value + ": " + diagnostic.Message;
            var location = diagnostic.Location;
            return location.HasDocumentId && location.DocumentId != SubtreeGraphEdits.EditorDocumentId
                ? text + " (in " + location.DocumentId + ")"
                : text;
        }

        // ---------------------------------------------------------------- pending extracted subtrees

        private IReadOnlyList<KeyValuePair<string, TreeDocument>> PendingSubtrees()
        {
            if (_pendingSubtreeDocuments != null) return _pendingSubtreeDocuments;
            _pendingSubtreeDocuments = new List<KeyValuePair<string, TreeDocument>>();
            for (var index = 0; index < Math.Min(_pendingSubtreePaths.Count, _pendingSubtreeBytes.Count); index++)
            {
                var parsed = CanonicalTreeJson.Parse(Convert.FromBase64String(_pendingSubtreeBytes[index]), _pendingSubtreePaths[index]);
                if (parsed.Success) _pendingSubtreeDocuments.Add(new KeyValuePair<string, TreeDocument>(_pendingSubtreePaths[index], parsed.Document));
            }
            return _pendingSubtreeDocuments;
        }

        private void AddPendingSubtree(string path, byte[] bytes)
        {
            _pendingSubtreePaths.Add(path);
            _pendingSubtreeBytes.Add(Convert.ToBase64String(bytes));
            _pendingSubtreeDocuments = null;
        }

        private void RemovePendingSubtree(string path)
        {
            var index = _pendingSubtreePaths.FindIndex(candidate => SamePath(candidate, path));
            if (index < 0) return;
            _pendingSubtreePaths.RemoveAt(index);
            _pendingSubtreeBytes.RemoveAt(index);
            _pendingSubtreeDocuments = null;
        }

        private void ClearPendingSubtrees()
        {
            _pendingSubtreePaths.Clear();
            _pendingSubtreeBytes.Clear();
            _pendingSubtreeDocuments = null;
        }

        /// <summary>The pending subtree files the current document reaches through its references (directly or through other pending files).</summary>
        private List<string> ReachablePendingSubtrees()
        {
            var result = new List<string>();
            var pending = PendingSubtrees();
            if (Document == null || pending.Count == 0) return result;
            var queue = new Queue<TreeDocument>();
            queue.Enqueue(Document);
            var seen = new HashSet<TreeId>();
            while (queue.Count > 0)
            {
                foreach (var node in queue.Dequeue().Nodes.Where(node => node.Subtree != null))
                {
                    if (!seen.Add(node.Subtree.TreeId)) continue;
                    foreach (var item in pending.Where(item => item.Value.TreeId == node.Subtree.TreeId))
                    {
                        result.Add(item.Key);
                        queue.Enqueue(item.Value);
                    }
                }
            }
            return result;
        }

        /// <summary>Writes the new subtree files the document uses (never overwriting a file) and imports them; Save calls this before writing the tree.</summary>
        private void WritePendingSubtrees()
        {
            foreach (var path in ReachablePendingSubtrees())
            {
                var index = _pendingSubtreePaths.FindIndex(candidate => SamePath(candidate, path));
                GraphEditSession.WriteIfUnchanged(path, null, Convert.FromBase64String(_pendingSubtreeBytes[index]));
                RemovePendingSubtree(path);
                Import(path);
            }
            ForgetTreeIndex();
        }

        private sealed class PendingSubtreeResolver : ISubtreeResolver
        {
            private readonly IReadOnlyList<KeyValuePair<string, TreeDocument>> _pending;
            private readonly ISubtreeResolver _inner;

            internal PendingSubtreeResolver(IReadOnlyList<KeyValuePair<string, TreeDocument>> pending, ISubtreeResolver inner)
            {
                _pending = pending;
                _inner = inner;
            }

            public SubtreeResolution Resolve(TreeId treeId)
            {
                foreach (var item in _pending)
                    if (item.Value.TreeId == treeId) return SubtreeResolution.Found(item.Value, item.Key);
                return _inner.Resolve(treeId);
            }
        }

        // ---------------------------------------------------------------- add a reference

        /// <summary>Opens the tree picker; the chosen tree becomes a reference (a child of <paramref name="parent"/>, or unattached).</summary>
        internal bool OpenSubtreePicker(NodeId? parent, Vector2 screenPosition, Vector2? graphPosition)
        {
            if (this == null || _session == null) return false;
            if (_pickerProvider == null)
            {
                _pickerProvider = CreateInstance<SubtreePickerProvider>();
                _pickerProvider.hideFlags = HideFlags.HideAndDontSave;
            }
            _pickerProvider.Entries = _catalog;
            _pickerProvider.CurrentPath = _sourcePath;
            _pickerProvider.CurrentTreeId = Document?.TreeId.Value;
            _pickerProvider.Selected = entry => AddSubtreeReference(new TreeId(entry.TreeId), parent, graphPosition);
            if (SearchWindow.Open(new SearchWindowContext(screenPosition, 360f, 360f), _pickerProvider)) return true;
            Notify("The tree list could not open. Click the graph and try again.");
            return false;
        }

        /// <summary>
        /// Adds a reference to <paramref name="treeId"/> with a short ID made from the subtree's
        /// name; interface keys with a same-named parent Tree key of the same type are connected.
        /// One undo step (it also moves the tree to format version 3 when needed).
        /// </summary>
        internal NodeId? AddSubtreeReference(TreeId treeId, NodeId? parent, Vector2? graphPosition = null)
        {
            if (_session == null) return null;
            if (treeId == Document.TreeId)
            {
                Notify("A tree cannot use itself as a subtree.");
                return null;
            }
            if (parent.HasValue && !CanAddChild(parent.Value))
            {
                Notify("This node cannot take another child.");
                return null;
            }
            var resolution = ResolveSubtree(treeId);
            if (resolution.Status != SubtreeResolutionStatus.Found)
            {
                Notify(resolution.Status == SubtreeResolutionStatus.Ambiguous
                    ? "Tree ID '" + treeId.Value + "' is declared by several files."
                    : "No tree file declares '" + treeId.Value + "'.");
                return null;
            }
            var subtree = resolution.Document;
            var seed = string.IsNullOrWhiteSpace(subtree.Name) ? SubtreeGraphEdits.SeedFor(treeId) : subtree.Name;
            var id = SubtreeGraphEdits.NewReferenceId(Document, seed);
            var reference = SubtreeGraphEdits.CreateReference(Document, subtree, id);
            if (!parent.HasValue && graphPosition.HasValue) _pendingPlacement[id] = graphPosition.Value;
            if (!ApplyEdit(document => SubtreeGraphEdits.AddReference(document, reference, parent))) return null;
            SelectNode(id, frame: false);
            return id;
        }

        // ---------------------------------------------------------------- key map

        internal bool SetSubtreeKeyBinding(NodeId referenceId, string interfaceKey, SubtreeKeyBinding binding)
            => ApplyInspectorEdit(document => SubtreeGraphEdits.SetKeyBinding(document, referenceId, interfaceKey, binding));

        internal bool SetSubtreeInterfaceVersion(NodeId referenceId, uint version)
            => ApplyInspectorEdit(document => SubtreeGraphEdits.SetInterfaceVersion(document, referenceId, version));

        /// <summary>Connects every unconnected interface key that has a same-named parent Tree key of the same type.</summary>
        internal bool ConnectSameNamedSubtreeKeys(NodeId referenceId)
        {
            var subtree = SubtreeOf(referenceId);
            return subtree != null && ApplyInspectorEdit(document => SubtreeGraphEdits.ConnectSameNamedKeys(document, referenceId, subtree));
        }

        /// <summary>Adds the parent Tree key the interface key needs (same ID, type and default) and connects it; one undo step.</summary>
        internal bool CreateParentKeyFor(NodeId referenceId, string interfaceKey)
        {
            var definition = SubtreeOf(referenceId)?.Blackboard.FirstOrDefault(key => key.Id == interfaceKey);
            if (definition == null)
            {
                Notify("The subtree has no key '" + interfaceKey + "'.");
                return false;
            }
            return ApplyInspectorEdit(document => SubtreeGraphEdits.CreateParentKey(document, referenceId, definition));
        }

        private bool ApplyInspectorEdit(Func<TreeDocument, TreeDocument> edit)
        {
            if (_session == null) return false;
            _inspectorEditDepth++;
            try { return ApplyEdit(edit); }
            finally { _inspectorEditDepth--; }
        }

        private TreeDocument SubtreeOf(NodeId referenceId)
        {
            var node = Document == null ? null : GraphDocumentEdits.Find(Document, referenceId);
            if (node?.Subtree == null) return null;
            var resolution = ResolveSubtree(node.Subtree.TreeId);
            return resolution.Status == SubtreeResolutionStatus.Found ? resolution.Document : null;
        }

        // ---------------------------------------------------------------- extract and unpack

        private void ChooseExtractTarget(NodeId top)
        {
            var node = GraphDocumentEdits.Find(Document, top);
            var name = SubtreeGraphEdits.Slug(node?.DisplayName ?? BehaviorTreeNode.ReadableTitle(node?.TypeId), 40);
            var folder = string.IsNullOrEmpty(_sourcePath) ? "Assets" : Path.GetDirectoryName(_sourcePath)?.Replace('\\', '/');
            var path = EditorUtility.SaveFilePanelInProject("Extract to subtree", (name.Length == 0 ? "subtree" : name) + ".aibt", "aibt",
                "Choose the new subtree file. It is written when you save this tree.", folder);
            if (!string.IsNullOrEmpty(path)) ExtractToSubtree(top, path);
        }

        /// <summary>
        /// Moves <paramref name="top"/> and the nodes below it into a new tree at
        /// <paramref name="path"/> and puts a reference in their place: one undo step. The new file
        /// is written by the next Save (before the tree), so Undo leaves no file behind.
        /// </summary>
        internal NodeId? ExtractToSubtree(NodeId top, string path)
        {
            if (_session == null) return null;
            path = (path ?? string.Empty).Replace('\\', '/');
            if (!TreeSourcePaths.IsTree(path))
            {
                Notify("Choose an .aibt or .aibt.json file for the subtree.");
                return null;
            }
            if (File.Exists(path) || _pendingSubtreePaths.Any(candidate => SamePath(candidate, path)) || SamePath(path, _sourcePath))
            {
                Notify("Choose a new file; existing trees are not overwritten.");
                return null;
            }

            var name = TreeNameOf(path);
            var catalog = _projectCatalog ?? ProjectNodeCatalogCache.Current;
            SubtreeGraphEdits.Extraction extraction;
            try
            {
                extraction = SubtreeGraphEdits.Extract(Document, top, UniqueTreeId(name), name, _registry);
            }
            catch (ArgumentException exception)
            {
                Notify(exception.Message);
                return null;
            }
            var encoded = CanonicalTreeJson.Serialize(extraction.Subtree, catalog.RegisteredTypes);
            if (!encoded.Success)
            {
                Notify("The subtree cannot be written: " + string.Join("\n", encoded.Diagnostics.Select(d => d.Message)));
                return null;
            }

            if (_view.NodesById.TryGetValue(top, out var topView)) _pendingPlacement[extraction.ReferenceId] = BehaviorTreeGraphView.NodePosition(topView);
            AddPendingSubtree(path, encoded.Utf8);
            if (!ApplyEdit(_ => extraction.Parent))
            {
                RemovePendingSubtree(path);
                return null;
            }
            SelectNode(extraction.ReferenceId, frame: false);
            Notify("Extracted to " + Path.GetFileName(path) + ". Save writes the new file.");
            return extraction.ReferenceId;
        }

        private TreeId UniqueTreeId(string name)
        {
            var slug = SubtreeGraphEdits.Slug(name, 48);
            if (slug.Length == 0) slug = "subtree";
            var stem = Document.TreeId.Value + "." + slug;
            if (!TreeId.TryParse(stem, out _)) stem = "subtree." + slug;
            for (var number = 1; ; number++)
            {
                var candidate = number == 1 ? stem : stem + "-" + number;
                if (!TreeId.TryParse(candidate, out var treeId)) continue;
                if (treeId == Document.TreeId || ResolveSubtree(treeId).Status != SubtreeResolutionStatus.NotFound) continue;
                return treeId;
            }
        }

        private static string TreeNameOf(string path)
        {
            var file = Path.GetFileName(path);
            if (file.EndsWith(".aibt.json", StringComparison.OrdinalIgnoreCase)) return file.Substring(0, file.Length - ".aibt.json".Length);
            return Path.GetFileNameWithoutExtension(file);
        }

        /// <summary>
        /// Replaces the reference by a copy of its expansion (inner nodes <c>r:n</c>, private keys
        /// <c>r:key</c>); one undo step. The new nodes are placed where the reference was.
        /// </summary>
        internal bool UnpackSubtree(NodeId referenceId)
        {
            if (_session == null) return false;
            var catalog = _projectCatalog ?? ProjectNodeCatalogCache.Current;
            TreeDocument result;
            DiagnosticCollection diagnostics;
            NodeId innerRoot;
            try
            {
                result = SubtreeGraphEdits.Unpack(Document, referenceId, CurrentSubtreeResolver(), _registry, catalog.RegisteredTypes,
                    out diagnostics, out innerRoot);
            }
            catch (ArgumentException exception)
            {
                Notify(exception.Message);
                return false;
            }
            if (result == null)
            {
                LastSubtreeCommandProblem = string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(ProblemText));
                Notify("Cannot unpack: " + LastSubtreeCommandProblem);
                return false;
            }

            var arranged = GraphLayout.Arrange(result);
            if (_view.NodesById.TryGetValue(referenceId, out var referenceView) && arranged.TryGetValue(innerRoot, out var rootAt))
            {
                var offset = BehaviorTreeGraphView.NodePosition(referenceView) - rootAt;
                foreach (var node in result.Nodes)
                    if (GraphDocumentEdits.Find(Document, node.Id) == null && arranged.TryGetValue(node.Id, out var at)) _pendingPlacement[node.Id] = at + offset;
            }
            if (!ApplyEdit(_ => result)) return false;
            SelectNode(innerRoot, frame: false);
            return true;
        }

        /// <summary>Why the last Unpack was refused; for tests and the notification.</summary>
        internal string LastSubtreeCommandProblem { get; private set; }

        // ---------------------------------------------------------------- navigation

        /// <summary>
        /// Opens the subtree of reference <paramref name="referenceId"/> in its own window, with a
        /// breadcrumb back to this tree. When this window shows a live agent, the subtree window
        /// shows the same agent's instance of that reference only (node IDs <c>prefix + id</c>).
        /// </summary>
        internal BehaviorTreeGraphWindow OpenSubtree(NodeId referenceId)
        {
            var node = Document == null ? null : GraphDocumentEdits.Find(Document, referenceId);
            if (node?.Subtree == null)
            {
                Notify("Select a subtree reference to open its subtree.");
                return null;
            }
            var resolution = ResolveSubtree(node.Subtree.TreeId);
            if (resolution.Status != SubtreeResolutionStatus.Found)
            {
                Notify(resolution.Status == SubtreeResolutionStatus.Ambiguous
                    ? "Tree ID '" + node.Subtree.TreeId.Value + "' is declared by several files: " + string.Join(", ", resolution.CandidateSourceIds)
                    : "No tree file declares '" + node.Subtree.TreeId.Value + "'.");
                return null;
            }
            var path = resolution.SourceId;
            if (_pendingSubtreePaths.Any(candidate => SamePath(candidate, path)))
            {
                Notify("Save this tree first: " + Path.GetFileName(path) + " is written on Save.");
                return null;
            }
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Notify("The subtree file " + path + " cannot be found.");
                return null;
            }

            var prefix = InstancePrefix + referenceId.Value + ":";
            var paths = new List<string>(_crumbPaths) { _sourcePath };
            var labels = new List<string>(_crumbLabels) { CrumbLabel() };
            var prefixes = new List<string>(_crumbPrefixes) { InstancePrefix };
            var keys = ChildLiveKeys(node, resolution.Document, prefix);

            var target = Resources.FindObjectsOfTypeAll<BehaviorTreeGraphWindow>()
                             .FirstOrDefault(window => window != this && SamePath(window._sourcePath, path) && window.InstancePrefix == prefix)
                         ?? CreateWindow<BehaviorTreeGraphWindow>(WindowTitle, typeof(BehaviorTreeGraphWindow));
            target.titleContent = new GUIContent(WindowTitle);
            target.SubtreeResolver = SubtreeResolver;
            target.Show();
            target.EnsureView();
            target.RestorePendingDocument();
            if (!SamePath(target._sourcePath, path) || target.Document == null)
                target.OpenFromPathNow(path, _registry ?? (_projectCatalog ?? ProjectNodeCatalogCache.Current).Registry);
            target.SetNavigation(paths, labels, prefixes, prefix, keys);
            target.Focus();
            if (_live != null && _live.IsAttached) target.TryAttachInstance(_live.Target, out _);
            return target;
        }

        /// <summary>Goes back to breadcrumb <paramref name="index"/>: focuses the window that shows it, or opens it here.</summary>
        internal BehaviorTreeGraphWindow NavigateBack(int index)
        {
            if (index < 0 || index >= _crumbPaths.Count) return null;
            var path = _crumbPaths[index];
            var prefix = _crumbPrefixes[index] ?? string.Empty;
            var existing = Resources.FindObjectsOfTypeAll<BehaviorTreeGraphWindow>()
                .FirstOrDefault(window => window != this && SamePath(window._sourcePath, path) && window.InstancePrefix == prefix);
            if (existing != null)
            {
                existing.Show();
                existing.Focus();
                return existing;
            }
            if (HasPendingChanges)
            {
                Notify("Save or discard the changes here before going back.");
                return null;
            }
            var paths = _crumbPaths.Take(index).ToList();
            var labels = _crumbLabels.Take(index).ToList();
            var prefixes = _crumbPrefixes.Take(index).ToList();
            var live = _live != null && _live.IsAttached ? _live.Target : null;
            OpenFromPathNow(path, _registry ?? (_projectCatalog ?? ProjectNodeCatalogCache.Current).Registry);
            SetNavigation(paths, labels, prefixes, prefix, new List<KeyValuePair<string, string>>());
            if (live != null) TryAttachInstance(live, out _);
            return this;
        }

        private string CrumbLabel()
            => Document != null && !string.IsNullOrEmpty(Document.Name) ? Document.Name
                : string.IsNullOrEmpty(_sourcePath) ? "Tree" : TreeNameOf(_sourcePath);

        private void SetNavigation(List<string> paths, List<string> labels, List<string> prefixes, string prefix,
            List<KeyValuePair<string, string>> liveKeys)
        {
            _crumbPaths = paths;
            _crumbLabels = labels;
            _crumbPrefixes = prefixes;
            _instancePrefix = prefix ?? string.Empty;
            _liveKeyFrom = liveKeys.Select(pair => pair.Key).ToList();
            _liveKeyTo = liveKeys.Select(pair => pair.Value).ToList();
            _live?.InvalidateMapping();
            RefreshBreadcrumb();
        }

        private void ClearNavigation()
        {
            _crumbPaths.Clear();
            _crumbLabels.Clear();
            _crumbPrefixes.Clear();
            _instancePrefix = string.Empty;
            _liveKeyFrom.Clear();
            _liveKeyTo.Clear();
            RefreshBreadcrumb();
        }

        private VisualElement BuildBreadcrumb()
        {
            _breadcrumb = new VisualElement { name = "aibt-breadcrumb" };
            _breadcrumb.AddToClassList("aibt-breadcrumb");
            return _breadcrumb;
        }

        private void RefreshBreadcrumb()
        {
            if (_breadcrumb == null) return;
            _breadcrumb.Clear();
            _breadcrumb.EnableInClassList("aibt-breadcrumb--hidden", _crumbPaths.Count == 0);
            for (var index = 0; index < _crumbPaths.Count; index++)
            {
                var crumb = index;
                var button = new Button(() => NavigateBack(crumb))
                    { text = _crumbLabels[index], name = "aibt-crumb-" + index, tooltip = "Back to " + _crumbPaths[index] };
                button.AddToClassList("aibt-breadcrumb__crumb");
                _breadcrumb.Add(button);
                var separator = new Label("›");
                separator.AddToClassList("aibt-breadcrumb__separator");
                _breadcrumb.Add(separator);
            }
            if (_crumbPaths.Count == 0) return;
            var current = new Label(CrumbLabel() + "   (instance " + InstancePrefix.TrimEnd(':') + ")") { name = "aibt-crumb-current" };
            current.AddToClassList("aibt-breadcrumb__current");
            _breadcrumb.Add(current);
        }

        // ---------------------------------------------------------------- live instance

        /// <summary>The node of this window that shows expanded node <paramref name="expandedId"/> of the program, or null.</summary>
        internal NodeId? MapLiveNode(string expandedId) => SubtreeGraphEdits.MapExpandedId(Document, InstancePrefix, expandedId);

        /// <summary>The live blackboard key that holds this window's key <paramref name="key"/>.</summary>
        internal string LiveKeyName(string key)
        {
            var index = _liveKeyFrom.IndexOf(key);
            if (index >= 0 && index < _liveKeyTo.Count) return _liveKeyTo[index];
            if (InstancePrefix.Length == 0) return key;
            var definition = Document?.Blackboard.FirstOrDefault(candidate => candidate.Id == key);
            return definition != null && definition.Scope != BlackboardScope.Tree ? key : InstancePrefix + key;
        }

        // An interface key bound to a parent key reads that key; every other Tree key of the
        // instance is the private key prefix + id (ADR-P11-001 §4.4).
        private List<KeyValuePair<string, string>> ChildLiveKeys(NodeDocument reference, TreeDocument subtree, string prefix)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (subtree == null) return result;
            foreach (var key in subtree.Blackboard)
            {
                if (key.Scope != BlackboardScope.Tree) continue;
                var isInterface = subtree.Interface != null && subtree.Interface.Keys.Contains(key.Id);
                result.Add(isInterface && reference.Subtree.TryGetBinding(key.Id, out var binding) && !binding.IsConstant
                    ? new KeyValuePair<string, string>(key.Id, LiveKeyName(binding.ParentKey))
                    : new KeyValuePair<string, string>(key.Id, prefix + key.Id));
            }
            return result;
        }

        /// <summary>
        /// Shows <paramref name="target"/> (a <see cref="BehaviorTreeAgent"/> or
        /// <see cref="NativeTreeAgent"/>) on this window. A subtree window accepts it only when the
        /// program contains this window's instance (every node as <c>prefix + id</c>).
        /// </summary>
        internal bool TryAttachInstance(Component target, out string reason)
        {
            reason = null;
            var ids = ExpandedIdsOf(target);
            if (ids == null)
            {
                reason = "The agent is not running yet.";
                return false;
            }
            var matches = InstancePrefix.Length == 0
                ? target is BehaviorTreeAgent managed ? GraphLiveDebugger.Matches(managed, Document) : MatchesProgram(((NativeTreeAgent)target).Program)
                : SubtreeGraphEdits.InstanceMatches(Document, InstancePrefix, ids);
            if (!matches)
            {
                reason = "The agent's program does not contain this subtree instance (" + InstancePrefix.TrimEnd(':') + ").";
                return false;
            }
            if (target is BehaviorTreeAgent agent) ShowLive(agent);
            else ShowLive((NativeTreeAgent)target);
            return true;
        }

        private static ICollection<string> ExpandedIdsOf(Component target)
        {
            if (target is BehaviorTreeAgent agent) return agent.Instance?.NodeIds;
            if (target is NativeTreeAgent native && native.Program != null)
                return new HashSet<string>(native.Program.DebugMap.Select(entry => entry.AuthoringNodeId.Value), StringComparer.Ordinal);
            return null;
        }

        // ---------------------------------------------------------------- inspector

        private void BuildReferenceInspector(VisualElement section, NodeDocument node, BehaviorTreeNode view)
        {
            var editable = _session != null;
            var reference = node.Subtree;
            var info = DescribeSubtree(node);
            var subtree = info?.Document;
            Text(section, "SUBTREE · " + reference.TreeId.Value + "  ·  id " + node.Id.Value, "aibt-inspector__type", "aibt-subtree-type");
            var pending = info?.SourcePath != null && _pendingSubtreePaths.Any(path => SamePath(path, info.SourcePath));
            Text(section, info == null ? string.Empty
                : info.Status == SubtreeResolutionStatus.Found ? "From " + info.SourcePath + (pending ? " (written on Save)" : string.Empty)
                : info.Status == SubtreeResolutionStatus.Ambiguous ? "Several files declare this tree ID: " + info.SourcePath
                : "No tree file under Assets/ or Packages/ declares this tree ID.",
                "aibt-inspector__summary", "aibt-subtree-source");
            if (view.ProblemText != null) Text(section, view.ProblemText, "aibt-field-help").style.color = new Color(1f, 0.55f, 0.5f);

            var latest = subtree?.Interface?.Version;
            Text(section, "Interface version " + reference.InterfaceVersion
                          + (latest.HasValue && latest.Value != reference.InterfaceVersion ? "; the subtree is at version " + latest.Value + "." : string.Empty),
                "aibt-field-help", "aibt-subtree-version");
            if (editable && latest.HasValue && latest.Value != reference.InterfaceVersion)
            {
                var version = latest.Value;
                section.Add(new Button(() => SetSubtreeInterfaceVersion(node.Id, version)) { text = "Use interface version " + version, name = "aibt-subtree-use-version" });
            }

            Text(section, "Key map", "aibt-section__title");
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            if (subtree?.Interface != null) foreach (var key in subtree.Interface.Keys) keys.Add(key);
            foreach (var binding in reference.Keys) keys.Add(binding.InterfaceKey);
            if (keys.Count == 0) Text(section, "This subtree has no interface keys.", "aibt-inspector__empty");
            foreach (var key in keys) section.Add(KeyMapRow(node.Id, reference, subtree, key, editable));

            if (editable && subtree != null && !SubtreeGraphEdits.SameNamedBindings(Document, subtree, reference.Keys).SequenceEqual(reference.Keys))
                section.Add(new Button(() => ConnectSameNamedSubtreeKeys(node.Id)) { text = "Connect same-named keys", name = "aibt-subtree-connect-same" });

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
            actions.Add(new Button(() => OpenSubtree(node.Id)) { text = "Open Subtree", name = "aibt-subtree-open" });
            var unpack = new Button(() => UnpackSubtree(node.Id)) { text = "Unpack", name = "aibt-subtree-unpack" };
            unpack.SetEnabled(editable);
            actions.Add(unpack);
            section.Add(actions);
        }

        private VisualElement KeyMapRow(NodeId referenceId, SubtreeReference reference, TreeDocument subtree, string key, bool editable)
        {
            var row = new VisualElement { name = "aibt-subtree-key-" + key };
            row.AddToClassList("aibt-bb-row");
            var definition = subtree?.Blackboard.FirstOrDefault(candidate => candidate.Id == key);
            var head = new VisualElement();
            head.AddToClassList("aibt-bb-row__head");
            Text(head, key, "aibt-bb-row__name");
            Text(head, definition?.Type.CanonicalTypeId ?? "unknown", "aibt-bb-row__type");
            row.Add(head);

            reference.TryGetBinding(key, out var binding);
            var choices = new List<string> { NotConnectedChoice };
            choices.AddRange(Document.Blackboard
                .Where(candidate => candidate.Scope == BlackboardScope.Tree && (definition == null || candidate.Type == definition.Type))
                .Select(candidate => candidate.Id));
            if (definition != null && ConstantSupported(definition.Type)) choices.Add(ConstantChoice);
            var current = binding == null ? NotConnectedChoice : binding.IsConstant ? ConstantChoice : binding.ParentKey;
            if (!choices.Contains(current)) choices.Add(current);
            var popup = new PopupField<string>("Connect to", choices, choices.IndexOf(current), ChoiceLabel, ChoiceLabel)
                { name = "aibt-subtree-bind-" + key };
            popup.SetEnabled(editable);
            popup.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == NotConnectedChoice) SetSubtreeKeyBinding(referenceId, key, null);
                else if (evt.newValue == ConstantChoice) SetSubtreeKeyBinding(referenceId, key, SubtreeKeyBinding.ToConstant(key, DefaultConstant(definition)));
                else SetSubtreeKeyBinding(referenceId, key, SubtreeKeyBinding.ToParentKey(key, evt.newValue));
            });
            row.Add(popup);
            if (binding != null && binding.IsConstant && definition != null && ConstantSupported(definition.Type))
            {
                var field = ConstantField(referenceId, key, definition.Type.ValueType, binding.Constant);
                field.SetEnabled(editable);
                row.Add(field);
            }
            if (editable && definition != null && definition.Scope == BlackboardScope.Tree && Document.Blackboard.All(candidate => candidate.Id != key))
                row.Add(new Button(() => CreateParentKeyFor(referenceId, key)) { text = "Create parent key '" + key + "'", name = "aibt-subtree-create-key-" + key });
            if (definition == null)
                Text(row, subtree == null ? "The subtree is not available." : "The subtree has no key '" + key + "'.", "aibt-field-help");
            return row;
        }

        private string ChoiceLabel(string choice)
        {
            if (choice == NotConnectedChoice || choice == ConstantChoice) return choice;
            return Document != null && Document.Blackboard.Any(key => key.Id == choice) ? "Parent key " + choice : choice + " (missing or incompatible)";
        }

        private static bool ConstantSupported(BlackboardTypeReference type)
        {
            if (!type.IsValid || type.IsRegistered) return false;
            switch (type.ValueType)
            {
                case BlackboardValueType.Bool:
                case BlackboardValueType.Int32:
                case BlackboardValueType.Int64:
                case BlackboardValueType.Float32:
                case BlackboardValueType.Float64:
                    return true;
                default:
                    return false;
            }
        }

        private static SemanticValue DefaultConstant(BlackboardKeyDefinition definition)
        {
            var type = definition.Type.ValueType;
            if (definition.DefaultValue != null && definition.DefaultValue.TryGetRuntimeValue(out var value))
            {
                switch (type)
                {
                    case BlackboardValueType.Bool when value.TryGetBool(out var flag): return SemanticValue.FromBoolean(flag);
                    case BlackboardValueType.Int32 when value.TryGetInt32(out var integer): return SemanticValue.FromInt64(integer);
                    case BlackboardValueType.Int64 when value.TryGetInt64(out var wide): return SemanticValue.FromInt64(wide);
                    case BlackboardValueType.Float32 when value.TryGetFloat32(out var single): return SemanticValue.FromNumber(ShortestDouble(single));
                    case BlackboardValueType.Float64 when value.TryGetFloat64(out var number): return SemanticValue.FromNumber(number);
                }
            }
            switch (type)
            {
                case BlackboardValueType.Bool: return SemanticValue.FromBoolean(false);
                case BlackboardValueType.Int32:
                case BlackboardValueType.Int64: return SemanticValue.FromInt64(0);
                default: return SemanticValue.FromNumber(0d);
            }
        }

        private VisualElement ConstantField(NodeId referenceId, string key, BlackboardValueType type, SemanticValue constant)
        {
            VisualElement field;
            switch (type)
            {
                case BlackboardValueType.Bool:
                {
                    var toggle = new Toggle("Value");
                    toggle.SetValueWithoutNotify(constant.TryGetBoolean(out var flag) && flag);
                    toggle.RegisterValueChangedCallback(evt => SetSubtreeKeyBinding(referenceId, key, SubtreeKeyBinding.ToConstant(key, SemanticValue.FromBoolean(evt.newValue))));
                    field = toggle;
                    break;
                }
                case BlackboardValueType.Int32:
                {
                    var integer = new IntegerField("Value") { isDelayed = true };
                    if (TryNumber(constant, out var number)) integer.SetValueWithoutNotify((int)number);
                    integer.RegisterValueChangedCallback(evt => SetSubtreeKeyBinding(referenceId, key, SubtreeKeyBinding.ToConstant(key, SemanticValue.FromInt64(evt.newValue))));
                    field = integer;
                    break;
                }
                case BlackboardValueType.Int64:
                {
                    var wide = new LongField("Value") { isDelayed = true };
                    if (constant.TryGetInt64(out var signed)) wide.SetValueWithoutNotify(signed);
                    else if (TryNumber(constant, out var number)) wide.SetValueWithoutNotify((long)number);
                    wide.RegisterValueChangedCallback(evt => SetSubtreeKeyBinding(referenceId, key, SubtreeKeyBinding.ToConstant(key, SemanticValue.FromInt64(evt.newValue))));
                    field = wide;
                    break;
                }
                case BlackboardValueType.Float32:
                {
                    var single = new FloatField("Value") { isDelayed = true };
                    if (TryNumber(constant, out var number)) single.SetValueWithoutNotify((float)number);
                    single.RegisterValueChangedCallback(evt =>
                    {
                        if (float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue)) { _view.schedule.Execute(RefreshInspector); return; }
                        SetSubtreeKeyBinding(referenceId, key, SubtreeKeyBinding.ToConstant(key, SemanticValue.FromNumber(ShortestDouble(evt.newValue))));
                    });
                    field = single;
                    break;
                }
                default:
                {
                    var wide = new DoubleField("Value") { isDelayed = true };
                    if (TryNumber(constant, out var number)) wide.SetValueWithoutNotify(number);
                    wide.RegisterValueChangedCallback(evt =>
                    {
                        if (double.IsNaN(evt.newValue) || double.IsInfinity(evt.newValue)) { _view.schedule.Execute(RefreshInspector); return; }
                        SetSubtreeKeyBinding(referenceId, key, SubtreeKeyBinding.ToConstant(key, SemanticValue.FromNumber(evt.newValue)));
                    });
                    field = wide;
                    break;
                }
            }
            field.name = "aibt-subtree-constant-" + key;
            return field;
        }
    }
}
