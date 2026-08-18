using System.IO;
using AIBT.Authoring;
using UnityEditor;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    /// <summary>Hosts a <see cref="BehaviorTreeGraphView"/> over an existing .aibt.json document. Read-only.</summary>
    public sealed class BehaviorTreeGraphWindow : EditorWindow
    {
        private BehaviorTreeGraphView _view;

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
            OpenFromBytes(File.ReadAllBytes(path), path, registry);
        }

        public void OpenFromBytes(byte[] utf8, string documentId, NodeRegistry registry)
        {
            EnsureView();

            var result = CanonicalTreeJson.Parse(utf8, documentId);
            Diagnostics = result.Diagnostics;
            Document = result.Success ? result.Document : null;

            if (Document != null)
            {
                _view.Populate(Document, registry);
            }
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
            rootVisualElement.Add(_view);
        }
    }
}
