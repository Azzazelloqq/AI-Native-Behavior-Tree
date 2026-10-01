using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Layout;
using AIBT.Editor.Organization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    /// <summary>Hosts a <see cref="BehaviorTreeGraphView"/> over an existing .aibt.json document. Read-only.</summary>
    public sealed class BehaviorTreeGraphWindow : EditorWindow
    {
        private BehaviorTreeGraphView _view;
        private Label _diagnosticLabel;
        private Label _documentLabel;
        private Label _emptyLabel;
        private ListView _treeList;
        private ToolbarSearchField _search;
        private PopupField<string> _tag;
        private PopupField<string> _status;
        private readonly List<TreeCatalogEntry> _catalog = new List<TreeCatalogEntry>();
        private readonly List<TreeCatalogEntry> _filtered = new List<TreeCatalogEntry>();
        [SerializeField] private string _sourcePath;
        [SerializeField] private string _sourceGuid;

        public TreeDocument Document { get; private set; }

        public DiagnosticCollection Diagnostics { get; private set; }

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
                _diagnosticLabel.text = exception.Message;
                _diagnosticLabel.style.display = DisplayStyle.Flex;
                _emptyLabel.text = "Tree could not be opened. Check the source path and diagnostics above.";
                _emptyLabel.style.display = DisplayStyle.Flex;
            }
        }

        public void OpenFromPath(string path) => OpenFromPath(path, NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry);

        public void OpenFromBytes(byte[] utf8, string documentId, NodeRegistry registry)
            => OpenDocument(utf8, documentId, registry, null);

        private void OpenDocument(byte[] utf8, string documentId, NodeRegistry registry, string treePath)
        {
            EnsureView();
            _view.ClearDocument();

            var result = CanonicalTreeJson.Parse(utf8, documentId);
            Diagnostics = result.Diagnostics;
            Document = result.Success ? result.Document : null;

            LayoutDocument layout = null;
            var canRender = Document != null;
            if (canRender && treePath != null)
            {
                var loaded = LayoutPersistenceController.Load(treePath, Document);
                Diagnostics = new DiagnosticCollection(Diagnostics.Concat(loaded.Diagnostics));
                canRender = loaded.Success;
                if (!loaded.UsedDefault) layout = loaded.Document;
            }

            _diagnosticLabel.text = string.Join("\n", Diagnostics.Select(d => d.Code.Value + ": " + d.Message));
            _diagnosticLabel.style.display = Diagnostics.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (canRender) _view.Populate(Document, registry, layout);
            if (canRender && registry != null)
            {
                var missing = Document.Nodes.Where(n => !registry.TryGet(n.TypeId, out _)).Select(n => n.TypeId).Distinct().ToArray();
                if (missing.Length > 0)
                {
                    _diagnosticLabel.text += "\nProject node contracts unresolved in this view (not a runtime failure): " + string.Join(", ", missing);
                    _diagnosticLabel.style.display = DisplayStyle.Flex;
                }
            }
        }

        private void OnEnable()
        {
            EnsureView();
            EditorApplication.projectChanged += ProjectChanged;
            EditorApplication.delayCall += RestoreDocument;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= ProjectChanged;
            EditorApplication.delayCall -= RestoreDocument;
        }

        private void RestoreDocument()
        {
            if (this == null) return;
            RefreshCatalog();
            if (!string.IsNullOrEmpty(_sourcePath)) OpenFromPath(_sourcePath);
        }

        private void ProjectChanged()
        {
            RefreshCatalog();
            if (!string.IsNullOrEmpty(_sourceGuid))
            {
                var moved = AssetDatabase.GUIDToAssetPath(_sourceGuid);
                if (!string.IsNullOrEmpty(moved)) _sourcePath = moved;
            }
            if (!string.IsNullOrEmpty(_sourcePath)) OpenFromPath(_sourcePath);
        }

        private void RefreshCatalog()
        {
            _catalog.Clear();
            foreach (var path in AssetDatabase.GetAllAssetPaths().Where(TreeSourcePaths.IsTree).OrderBy(p => p, StringComparer.Ordinal))
                _catalog.Add(TreeCatalogEntry.Read(path));
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
            toolbar.Add(new ToolbarButton(ChooseTree) { text = "Open Tree", name = "aibt-open-tree" });
            toolbar.Add(new ToolbarButton(() => { RefreshCatalog(); if (!string.IsNullOrEmpty(_sourcePath)) OpenFromPath(_sourcePath); }) { text = "Reload" });
            toolbar.Add(new ToolbarButton(() => _view.FrameAll()) { text = "Frame All", name = "aibt-frame-all" });
            toolbar.Add(new Label("Read-only viewer • movement is temporary"));
            rootVisualElement.Add(toolbar);

            var body = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            var sidebar = new VisualElement { name = "aibt-tree-browser", style = { width = 320, minWidth = 220, flexShrink = 0 } };
            sidebar.Add(new Label("PROJECT TREES") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            _search = new ToolbarSearchField { name = "aibt-tree-search" };
            _search.RegisterValueChangedCallback(_ => FilterCatalog());
            sidebar.Add(_search);
            _tag = new PopupField<string>("Tag", new List<string> { "All tags" }, 0);
            _status = new PopupField<string>("State", new List<string> { "All states", "Valid", "Warnings", "Errors", "Needs registry" }, 0);
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
                }) { name = "aibt-tree-list", style = { flexGrow = 1 } };
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
            _view.style.flexGrow = 1;
            _diagnosticLabel = new Label { name = "aibt-graph-diagnostics" };
            _diagnosticLabel.style.whiteSpace = WhiteSpace.Normal;
            _diagnosticLabel.style.display = DisplayStyle.None;
            content.Add(_diagnosticLabel);
            content.Add(_view);
            body.Add(content);
            rootVisualElement.Add(body);
            RefreshCatalog();
        }
    }
}
