using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Layout;
using AIBT.Editor.Organization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    /// <summary>Hosts a <see cref="BehaviorTreeGraphView"/> over an existing .aibt.json document. Read-only.</summary>
    public sealed class BehaviorTreeGraphWindow : EditorWindow
    {
        private BehaviorTreeGraphView _view;
        private Label _diagnosticLabel;

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
            OpenDocument(File.ReadAllBytes(path), path, registry, path);
        }

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
        }

        private void OnEnable()
        {
            EnsureView();
        }

        private void EnsureView()
        {
            if (_view != null)
            {
                return;
            }

            _view = new BehaviorTreeGraphView { name = "aibt-graph-view" };
            _view.style.flexGrow = 1;
            _diagnosticLabel = new Label { name = "aibt-graph-diagnostics" };
            _diagnosticLabel.style.whiteSpace = WhiteSpace.Normal;
            _diagnosticLabel.style.display = DisplayStyle.None;
            rootVisualElement.Add(_diagnosticLabel);
            rootVisualElement.Add(_view);
        }
    }
}
