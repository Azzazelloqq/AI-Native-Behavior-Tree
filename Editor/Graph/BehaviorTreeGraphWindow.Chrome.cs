using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Registry;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    public sealed partial class BehaviorTreeGraphWindow
    {
        private enum BannerKind
        {
            Info,
            Warning,
            Error,
        }

        /// <summary>A message shown in the problems list that is not a compiler diagnostic.</summary>
        private readonly struct GraphMessage
        {
            internal GraphMessage(DiagnosticSeverity severity, string text)
            {
                Severity = severity;
                Text = text;
            }

            internal DiagnosticSeverity Severity { get; }

            internal string Text { get; }
        }

        /// <summary>One row of the problems list.</summary>
        internal sealed class ProblemItem
        {
            internal DiagnosticSeverity Severity;
            internal string Text;
            internal NodeId? Node;
        }

        private readonly List<GraphMessage> _extraMessages = new List<GraphMessage>();
        private readonly List<ProblemItem> _problems = new List<ProblemItem>();
        private readonly List<TreeCatalogEntry> _catalog = new List<TreeCatalogEntry>();
        private readonly List<TreeCatalogEntry> _filtered = new List<TreeCatalogEntry>();
        private string _nativeMessage;

        private Label _documentLabel;
        private Label _statusPill;
        private ToolbarButton _saveButton;
        private ToolbarToggle _treesToggle;
        private VisualElement _treePanel;
        private ListView _treeList;
        private ToolbarSearchField _search;
        private PopupField<string> _tag;
        private PopupField<string> _status;
        private Label _catalogDiagnostics;
        private VisualElement _banner;
        private Label _bannerText;
        private VisualElement _bannerButtons;
        private Label _emptyLabel;
        private VisualElement _problemsPanel;
        private Label _diagnosticLabel;
        private ListView _problemList;
        private Label _nativeValidationLabel;
        private ScrollView _inspector;
        private VisualElement _liveBadge;
        private Label _liveText;

        internal IReadOnlyList<ProblemItem> Problems => _problems;

        private void EnsureView()
        {
            if (_view != null)
            {
                return;
            }

            var root = rootVisualElement;
            root.AddToClassList("aibt-window");
            GraphStyles.ApplyWindow(root);
            root.Add(BuildToolbar());
            root.Add(BuildBreadcrumb());

            _banner = new VisualElement { name = "aibt-banner" };
            _banner.AddToClassList("aibt-banner");
            _banner.AddToClassList("aibt-banner--hidden");
            _bannerText = new Label { name = "aibt-banner-text" };
            _bannerText.AddToClassList("aibt-banner__text");
            _banner.Add(_bannerText);
            _bannerButtons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _banner.Add(_bannerButtons);
            root.Add(_banner);

            var body = new VisualElement { name = "aibt-body" };
            body.AddToClassList("aibt-body");
            body.Add(BuildTreePanel());

            var center = new VisualElement { name = "aibt-center" };
            center.AddToClassList("aibt-center");
            var host = new VisualElement { name = "aibt-graph-host" };
            host.AddToClassList("aibt-graph-host");
            _view = new BehaviorTreeGraphView { name = "aibt-graph-view" };
            _view.style.flexGrow = 1;
            host.Add(_view);
            _emptyLabel = new Label("Pick a tree on the left, or use Open.\nRight-click or press Space on the canvas to add nodes.")
                { name = "aibt-empty-state", pickingMode = PickingMode.Ignore };
            _emptyLabel.AddToClassList("aibt-empty");
            host.Add(_emptyLabel);
            _liveBadge = new VisualElement { name = "aibt-live", pickingMode = PickingMode.Ignore };
            _liveBadge.AddToClassList("aibt-live");
            _liveBadge.AddToClassList("aibt-live--hidden");
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("aibt-live__dot");
            _liveBadge.Add(dot);
            _liveText = new Label { name = "aibt-live-text", pickingMode = PickingMode.Ignore };
            _liveText.AddToClassList("aibt-live__text");
            _liveBadge.Add(_liveText);
            host.Add(_liveBadge);
            center.Add(host);
            center.Add(BuildProblemsPanel());
            body.Add(center);

            var right = new VisualElement { name = "aibt-inspector-panel" };
            right.AddToClassList("aibt-panel");
            right.AddToClassList("aibt-panel--right");
            _inspector = new ScrollView { name = "aibt-inspector" };
            _inspector.AddToClassList("aibt-inspector");
            right.Add(_inspector);
            body.Add(right);
            root.Add(body);

            WireGraphEditing();
            RefreshBreadcrumb();
            _view.RegisterCallback<MouseUpEvent>(_ => _view.schedule.Execute(RefreshInspector));
            _view.RegisterCallback<KeyDownEvent>(OnGraphKeyDown, TrickleDown.TrickleDown);
            RefreshCatalog();
            RefreshChrome();
        }

        private Toolbar BuildToolbar()
        {
            var toolbar = new Toolbar { name = "aibt-toolbar" };
            toolbar.AddToClassList("aibt-toolbar");
            _treesToggle = new ToolbarToggle { name = "aibt-toggle-trees", text = "Trees", tooltip = "Show or hide the project tree list." };
            _treesToggle.SetValueWithoutNotify(!_hideTreeList);
            _treesToggle.RegisterValueChangedCallback(evt =>
            {
                _hideTreeList = !evt.newValue;
                _treePanel.EnableInClassList("aibt-panel--hidden", _hideTreeList);
            });
            toolbar.Add(_treesToggle);
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new ToolbarButton(NewTree) { text = "New", name = "aibt-new", tooltip = "Create a new tree file." });
            toolbar.Add(new ToolbarButton(ChooseTree) { text = "Open", name = "aibt-open-tree", tooltip = "Open a tree file." });
            _saveButton = new ToolbarButton(() => SaveAll()) { text = "Save", name = "aibt-save", tooltip = "Save the tree and its layout (Ctrl+S)." };
            toolbar.Add(_saveButton);
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new ToolbarButton(AutoLayout) { text = "Auto Layout", name = "aibt-auto-layout",
                tooltip = "Arrange the tree top-down (Ctrl+Shift+L). Child order is never changed." });
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new ToolbarButton(PreviewCurrent) { text = "Preview", name = "aibt-preview-current",
                tooltip = "Step this tree in the reference preview." });
            toolbar.Add(new ToolbarButton(DebugSelected) { text = "Debug", name = "aibt-debug-selected",
                tooltip = "Show the selected BehaviorTreeAgent or ProductionTreeHost live on this graph (Play Mode)." });
            var more = new ToolbarMenu { text = "More", name = "aibt-more" };
            more.menu.AppendAction("Check Native Compile", _ => CheckNative(), _ => _session != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            more.menu.AppendAction("Prepare Runtime Asset…", _ => BakeRuntimeAsset(), _ => _session != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            more.menu.AppendSeparator();
            more.menu.AppendAction("Reload From Disk", _ => Reload());
            more.menu.AppendAction("Frame All", _ => _view.FrameAll());
            toolbar.Add(more);
            _documentLabel = new Label("No tree") { name = "aibt-source-path" };
            _documentLabel.AddToClassList("aibt-toolbar__document");
            toolbar.Add(_documentLabel);
            _statusPill = new Label { name = "aibt-status" };
            _statusPill.AddToClassList("aibt-status");
            toolbar.Add(_statusPill);
            return toolbar;
        }

        private VisualElement BuildTreePanel()
        {
            _treePanel = new VisualElement { name = "aibt-tree-browser" };
            _treePanel.AddToClassList("aibt-panel");
            _treePanel.AddToClassList("aibt-panel--left");
            _treePanel.EnableInClassList("aibt-panel--hidden", _hideTreeList);
            var header = new Label("PROJECT TREES");
            header.AddToClassList("aibt-panel__header");
            _treePanel.Add(header);
            _catalogDiagnostics = new Label { style = { whiteSpace = WhiteSpace.Normal, color = new Color(1f, 0.65f, 0.25f) } };
            _treePanel.Add(_catalogDiagnostics);
            _search = new ToolbarSearchField { name = "aibt-tree-search" };
            _search.style.width = StyleKeyword.Auto;
            _search.RegisterValueChangedCallback(_ => FilterCatalog());
            _treePanel.Add(_search);
            _tag = new PopupField<string>("Tag", new List<string> { "All tags" }, 0);
            _status = new PopupField<string>("State", new List<string> { "All states", "Valid", "Warnings", "Errors", "Needs registry", "Needs native compile" }, 0);
            _tag.RegisterValueChangedCallback(_ => FilterCatalog());
            _status.RegisterValueChangedCallback(_ => FilterCatalog());
            _treePanel.Add(_tag);
            _treePanel.Add(_status);
            _treeList = new ListView(_filtered, 40, MakeTreeItem, BindTreeItem)
                { name = "aibt-tree-list", virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight, style = { flexGrow = 1 } };
            _treeList.selectionChanged += selected =>
            {
                var entry = selected.OfType<TreeCatalogEntry>().FirstOrDefault();
                if (entry != null && !SamePath(entry.Path, _sourcePath)) OpenFromPath(entry.Path);
            };
            _treePanel.Add(_treeList);
            var hint = new Label("Search by name, ID or path. Samples appear after import; Open reads any tree file.");
            hint.AddToClassList("aibt-panel__hint");
            _treePanel.Add(hint);
            return _treePanel;
        }

        private static VisualElement MakeTreeItem()
        {
            var item = new VisualElement();
            item.AddToClassList("aibt-tree-item");
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var name = new Label { name = "name" };
            name.AddToClassList("aibt-tree-item__name");
            row.Add(name);
            var status = new Label { name = "status" };
            status.AddToClassList("aibt-tree-item__status");
            row.Add(status);
            item.Add(row);
            var meta = new Label { name = "meta" };
            meta.AddToClassList("aibt-tree-item__meta");
            item.Add(meta);
            return item;
        }

        private void BindTreeItem(VisualElement element, int index)
        {
            var entry = _filtered[index];
            element.Q<Label>("name").text = entry.Name;
            var status = element.Q<Label>("status");
            status.text = entry.Status;
            status.EnableInClassList("aibt-tree-item__status--valid", entry.Status == "Valid");
            status.EnableInClassList("aibt-tree-item__status--warnings", entry.Status == "Warnings");
            status.EnableInClassList("aibt-tree-item__status--errors", entry.Status == "Errors");
            element.Q<Label>("meta").text = entry.Path;
            element.tooltip = entry.TreeId + "\n" + entry.Details;
        }

        private VisualElement BuildProblemsPanel()
        {
            _problemsPanel = new VisualElement { name = "aibt-problems" };
            _problemsPanel.AddToClassList("aibt-diagnostics");
            _diagnosticLabel = new Label { name = "aibt-graph-diagnostics" };
            _diagnosticLabel.AddToClassList("aibt-diagnostics__header");
            _problemsPanel.Add(_diagnosticLabel);
            _nativeValidationLabel = new Label { name = "aibt-native-validation" };
            _nativeValidationLabel.AddToClassList("aibt-native");
            _problemsPanel.Add(_nativeValidationLabel);
            _problemList = new ListView(_problems, 20, MakeProblemItem, BindProblemItem)
                { name = "aibt-problem-list", selectionType = SelectionType.Single };
            _problemList.AddToClassList("aibt-diagnostics__list");
            _problemList.selectionChanged += selected =>
            {
                var item = selected.OfType<ProblemItem>().FirstOrDefault();
                if (item?.Node != null) SelectNode(item.Node.Value, frame: true);
            };
            _problemsPanel.Add(_problemList);
            return _problemsPanel;
        }

        private static VisualElement MakeProblemItem()
        {
            var row = new VisualElement();
            row.AddToClassList("aibt-diagnostic");
            var dot = new VisualElement();
            dot.AddToClassList("aibt-diagnostic__dot");
            row.Add(dot);
            var text = new Label { name = "text" };
            text.AddToClassList("aibt-diagnostic__text");
            row.Add(text);
            var node = new Label { name = "node" };
            node.AddToClassList("aibt-diagnostic__node");
            row.Add(node);
            return row;
        }

        private void BindProblemItem(VisualElement element, int index)
        {
            var item = _problems[index];
            element.EnableInClassList("aibt-diagnostic--warning", item.Severity == DiagnosticSeverity.Warning);
            element.EnableInClassList("aibt-diagnostic--info", item.Severity == DiagnosticSeverity.Info);
            element.Q<Label>("text").text = item.Text;
            element.tooltip = item.Text;
            var node = element.Q<Label>("node");
            node.text = item.Node.HasValue && _view.NodesById.TryGetValue(item.Node.Value, out var view) ? view.title : item.Node?.Value ?? string.Empty;
        }

        // ---------------------------------------------------------------- refresh

        /// <summary>Updates toolbar, problems, node badges and the inspector from the current document.</summary>
        private void RefreshChrome()
        {
            if (_view == null) return;
            RefreshProblems();
            RefreshToolbarState();
            _emptyLabel.style.display = Document == null ? DisplayStyle.Flex : DisplayStyle.None;
            if (Document == null && _problems.Count == 0)
                _emptyLabel.text = "Pick a tree on the left, or use Open.\nRight-click or press Space on the canvas to add nodes.";
            // An inspector field may still be dispatching the change that led here; rebuild after it.
            if (_inspectorEditDepth > 0) _view.schedule.Execute(RefreshInspector);
            else RefreshInspector();
        }

        private void RefreshToolbarState()
        {
            if (_documentLabel == null) return;
            var name = !string.IsNullOrEmpty(_sourcePath) ? _sourcePath : Document != null ? Document.Name + " (read-only)" : "No tree";
            _documentLabel.text = name + (HasPendingChanges ? "  •  unsaved" : string.Empty);
            _documentLabel.tooltip = _sourcePath ?? string.Empty;
            _saveButton.SetEnabled(_session != null);
            var errors = _problems.Count(item => item.Severity == DiagnosticSeverity.Error);
            var warnings = _problems.Count(item => item.Severity == DiagnosticSeverity.Warning);
            var live = _live != null && _live.IsAttached;
            _statusPill.text = live ? "LIVE" : errors > 0 ? errors + (errors == 1 ? " error" : " errors")
                : warnings > 0 ? warnings + (warnings == 1 ? " warning" : " warnings") : "Valid";
            _statusPill.EnableInClassList("aibt-status--hidden", Document == null && _problems.Count == 0);
            _statusPill.EnableInClassList("aibt-status--live", live);
            _statusPill.EnableInClassList("aibt-status--error", !live && errors > 0);
            _statusPill.EnableInClassList("aibt-status--warning", !live && errors == 0 && warnings > 0);
            _statusPill.EnableInClassList("aibt-status--valid", !live && errors == 0 && warnings == 0);
        }

        private void RefreshProblems()
        {
            _problems.Clear();
            var seen = new HashSet<string>();
            void Add(DiagnosticSeverity severity, string text, NodeId? node)
            {
                if (seen.Add(severity + "|" + text + "|" + node?.Value)) _problems.Add(new ProblemItem { Severity = severity, Text = text, Node = node });
            }

            if (Diagnostics != null) foreach (var diagnostic in Diagnostics) Add(diagnostic.Severity, ProblemText(diagnostic), ViewNodeOf(diagnostic));
            if (_session != null) foreach (var diagnostic in _session.Diagnostics) Add(diagnostic.Severity, ProblemText(diagnostic), ViewNodeOf(diagnostic));
            foreach (var message in _extraMessages) Add(message.Severity, message.Text, null);
            _problems.Sort((left, right) => left.Severity.CompareTo(right.Severity));

            var errors = _problems.Count(item => item.Severity == DiagnosticSeverity.Error);
            var warnings = _problems.Count(item => item.Severity == DiagnosticSeverity.Warning);
            var infos = _problems.Count - errors - warnings;
            var parts = new List<string>();
            if (errors > 0) parts.Add(errors + (errors == 1 ? " error" : " errors"));
            if (warnings > 0) parts.Add(warnings + (warnings == 1 ? " warning" : " warnings"));
            if (infos > 0) parts.Add(infos + (infos == 1 ? " note" : " notes"));
            _diagnosticLabel.text = parts.Count == 0 ? string.Empty : "Problems: " + string.Join(", ", parts) + ". Click one to select its node.";
            _nativeValidationLabel.text = _nativeMessage ?? string.Empty;
            _nativeValidationLabel.style.display = string.IsNullOrEmpty(_nativeMessage) ? DisplayStyle.None : DisplayStyle.Flex;
            _problemList.style.display = _problems.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _problemList.style.height = Math.Min(5, Math.Max(1, _problems.Count)) * 20 + 2;
            _problemsPanel.EnableInClassList("aibt-diagnostics--hidden", _problems.Count == 0 && string.IsNullOrEmpty(_nativeMessage));
            _problemList.Rebuild();

            var byNode = new Dictionary<NodeId, List<ProblemItem>>();
            foreach (var item in _problems.Where(item => item.Node.HasValue))
            {
                if (!byNode.TryGetValue(item.Node.Value, out var list)) byNode[item.Node.Value] = list = new List<ProblemItem>();
                list.Add(item);
            }
            foreach (var pair in _view.NodesById)
            {
                if (byNode.TryGetValue(pair.Key, out var list))
                    pair.Value.SetProblem(string.Join("\n", list.Select(item => item.Text)), list.Any(item => item.Severity == DiagnosticSeverity.Error));
                else pair.Value.SetProblem(null, false);
            }
        }

        /// <summary>The node a diagnostic points at: its location's node ID, else the "/nodes/&lt;id&gt;" JSON pointer.</summary>
        internal static NodeId? NodeOf(Diagnostic diagnostic)
        {
            var location = diagnostic.Location;
            if (location.NodeId.IsValid) return location.NodeId;
            var pointer = location.JsonPointer;
            const string prefix = "/nodes/";
            if (string.IsNullOrEmpty(pointer) || !pointer.StartsWith(prefix, StringComparison.Ordinal)) return null;
            var end = pointer.IndexOf('/', prefix.Length);
            var id = (end < 0 ? pointer.Substring(prefix.Length) : pointer.Substring(prefix.Length, end - prefix.Length))
                .Replace("~1", "/").Replace("~0", "~");
            return id.Length == 0 ? (NodeId?)null : new NodeId(id);
        }

        // ---------------------------------------------------------------- banner

        private void ShowBanner(string text, BannerKind kind, params (string Label, Action Action)[] buttons)
        {
            _bannerText.text = text;
            _banner.EnableInClassList("aibt-banner--hidden", false);
            _banner.EnableInClassList("aibt-banner--error", kind == BannerKind.Error);
            _banner.EnableInClassList("aibt-banner--info", kind == BannerKind.Info);
            _bannerButtons.Clear();
            foreach (var button in buttons)
            {
                var action = button.Action;
                _bannerButtons.Add(new Button(() => action()) { text = button.Label, name = "aibt-banner-" + button.Label.ToLowerInvariant().Replace(' ', '-') });
            }
        }

        private void HideBanner()
        {
            if (_banner == null) return;
            _banner.EnableInClassList("aibt-banner--hidden", true);
            _bannerButtons.Clear();
        }

        internal string BannerText => _banner == null || _banner.ClassListContains("aibt-banner--hidden") ? null : _bannerText.text;

        // ---------------------------------------------------------------- tree catalog

        // The cache discovers project nodes on the first delayCall after a domain reload. A window
        // can be asked to open a tree before that call runs (or while the Editor is not ticking),
        // so the first window that needs the catalog discovers it once itself.
        private static bool s_catalogDiscovered;

        private void RefreshCatalog()
        {
            if (!s_catalogDiscovered)
            {
                s_catalogDiscovered = true;
                ProjectNodeCatalogCache.Refresh();
            }
            var previous = _projectCatalog;
            _projectCatalog = ProjectNodeCatalogCache.Current;
            if (_session != null && previous != _projectCatalog && ReferenceEquals(_registry, previous?.Registry))
            {
                _registry = _projectCatalog.Registry;
                _session.RebindRegistry(_registry, ValidateProjectDocument);
                if (previous.Registry.Hash != _projectCatalog.Registry.Hash || !previous.Problems.SequenceEqual(_projectCatalog.Problems))
                {
                    Document = _session.Document;
                    Diagnostics = _session.Diagnostics;
                    _view.Sync(Document, _registry, _ => Vector2.zero);
                }
                RefreshChrome();
            }
            if (_catalogDiagnostics == null) return;
            _catalogDiagnostics.text = string.Join("\n", _projectCatalog.Problems);
            _catalogDiagnostics.style.display = _projectCatalog.Problems.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _catalog.Clear();
            var facts = TreeCatalogEntry.NativeFacts.FromProject(_projectCatalog);
            foreach (var path in AssetDatabase.GetAllAssetPaths().Where(TreeSourcePaths.IsTree)
                         .Where(path => !path.StartsWith("Assets/AIBT/Tests/", StringComparison.OrdinalIgnoreCase) &&
                                        !path.StartsWith("Packages/com.azzazello.aibt/Tests/", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(p => p, StringComparer.Ordinal))
                _catalog.Add(TreeCatalogEntry.Read(path, _projectCatalog.Registry, null, facts));
            var selectedTag = _tag.value;
            _tag.choices = new[] { "All tags" }.Concat(_catalog.SelectMany(e => e.Tags).Distinct().OrderBy(t => t)).ToList();
            _tag.SetValueWithoutNotify(_tag.choices.Contains(selectedTag) ? selectedTag : "All tags");
            FilterCatalog();
        }

        private void FilterCatalog()
        {
            _filtered.Clear();
            _filtered.AddRange(_catalog.Where(e => e.Matches(_search.value, _tag.value, _status.value)));
            _treeList.Rebuild();
            var index = _filtered.FindIndex(entry => SamePath(entry.Path, _sourcePath));
            if (index >= 0) _treeList.SetSelectionWithoutNotify(new[] { index });
            else _treeList.ClearSelection();
        }

        internal IReadOnlyList<TreeCatalogEntry> CatalogEntries => _catalog;

        private void ChooseTree()
        {
            var path = EditorUtility.OpenFilePanelWithFilters("Open AIBT Tree", Application.dataPath,
                new[] { "AIBT trees", "aibt,json" });
            if (string.IsNullOrEmpty(path)) return;
            var relative = FileUtil.GetProjectRelativePath(path);
            OpenFromPath(string.IsNullOrEmpty(relative) ? path : relative);
        }

        private void Reload()
        {
            RefreshCatalog();
            if (string.IsNullOrEmpty(_sourcePath)) return;
            if (HasPendingChanges) OfferPendingOpen(Path.GetFileName(_sourcePath) + " from disk", ReloadDiscardingChanges);
            else OpenFromPathNow(_sourcePath, (_projectCatalog ?? ProjectNodeCatalogCache.Current).Registry);
        }
    }
}
