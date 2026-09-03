using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Authoring.Migration;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Migration
{
    /// <summary>
    /// P7-006 (ADR-P7-005): a non-blocking notification listing every project document with a
    /// node the migration engine could rewrite in memory, and a per-document button to persist
    /// that same migration to disk. A plain <see cref="EditorWindow"/> -- never
    /// <c>ShowModalUtility</c> -- so it never gates anything, including the MCP/AI-agent path,
    /// which already gets the same in-memory migration transparently on every
    /// <c>validate</c>/<c>compile</c> call regardless of whether this window is ever opened.
    /// </summary>
    public sealed class MigrationNotificationWindow : EditorWindow
    {
        private ScrollView _rowsView;
        private Label _statusLabel;

        [MenuItem("AIBT/Migration Notifications")]
        public static MigrationNotificationWindow ShowWindow()
        {
            var window = GetWindow<MigrationNotificationWindow>();
            window.titleContent = new GUIContent("AIBT Migrations");
            return window;
        }

        private void OnEnable()
        {
            BuildLayout();
        }

        private void BuildLayout()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(OnScanClicked) { text = "Scan Project" });
            rootVisualElement.Add(toolbar);

            _statusLabel = new Label("Not scanned yet.");
            _statusLabel.style.paddingLeft = 4;
            _statusLabel.style.paddingTop = 4;
            rootVisualElement.Add(_statusLabel);

            _rowsView = new ScrollView { style = { flexGrow = 1 } };
            rootVisualElement.Add(_rowsView);
        }

        public IReadOnlyList<TreeId> LastScanMigratableTreeIds { get; private set; } = System.Array.Empty<TreeId>();

        private void OnScanClicked() => Scan(NodeMigrationRegistry.Empty, Application.dataPath);

        /// <summary>
        /// Scans every <c>*.aibt.json</c> under <paramref name="rootDirectory"/>, migrates each in
        /// memory against <paramref name="rules"/>, and lists documents with at least one migrated
        /// node. All three of <paramref name="rules"/>/<paramref name="rootDirectory"/>/
        /// <paramref name="registry"/> are explicit parameters (rather than always
        /// <see cref="NodeMigrationRegistry.Empty"/>, <see cref="Application.dataPath"/>, and the
        /// real production registry) so tests can point this at a controlled fixture directory with
        /// a real registered node type and rule, never the live project's own real documents.
        /// </summary>
        public void Scan(NodeMigrationRegistry rules, string rootDirectory, NodeRegistry registry = null)
        {
            _rowsView.Clear();
            registry = registry ?? NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var (documents, paths) = ScanTreeDocuments(rootDirectory);

            var migratable = new List<TreeId>();
            for (var index = 0; index < documents.Count; index++)
            {
                var document = documents[index];
                var path = paths[index];
                var migrated = DocumentMigrator.TryMigrate(document, registry, rules, out var outcomes);
                if (outcomes.Count == 0) continue;

                migratable.Add(document.TreeId);
                _rowsView.Add(BuildRow(document.TreeId, path, migrated, outcomes));
            }

            LastScanMigratableTreeIds = migratable;
            _statusLabel.text = migratable.Count == 0
                ? "Scanned " + documents.Count + " document(s). Nothing to migrate."
                : "Scanned " + documents.Count + " document(s). " + migratable.Count + " have a migratable node.";
        }

        private VisualElement BuildRow(TreeId treeId, string path, TreeDocument migrated, IReadOnlyList<NodeMigrationOutcome> outcomes)
        {
            var container = new VisualElement { style = { paddingLeft = 4, paddingTop = 6, paddingBottom = 6 } };
            container.Add(new Label(treeId.Value + "  (" + path + ")") { style = { unityFontStyleAndWeight = FontStyle.Bold } });

            foreach (var outcome in outcomes)
            {
                var changeText = string.Join("; ", outcome.Changes.Select(c => c.Description));
                var line = "  " + outcome.NodeId.Value + " (" + outcome.TypeId + "): v" + outcome.FromVersion
                    + " -> v" + outcome.ToVersion + " -- " + changeText;
                var label = new Label(line) { style = { whiteSpace = WhiteSpace.Normal } };
                container.Add(label);
            }

            var persistButton = new Button(() => OnPersistClicked(path, migrated)) { text = "Persist to disk" };
            container.Add(persistButton);
            return container;
        }

        private void OnPersistClicked(string path, TreeDocument migrated)
        {
            var result = CanonicalTreeJson.Serialize(migrated);
            if (!result.Success)
            {
                EditorUtility.DisplayDialog("AIBT Migration", "Could not serialize the migrated document; nothing was written.", "OK");
                return;
            }

            File.WriteAllBytes(path, result.Utf8);
            AssetDatabase.Refresh();
            _statusLabel.text = "Wrote " + path + ".";
        }

        /// <summary>
        /// Duplicates <c>MCP/AibtTreeDiscovery.Scan</c>'s minimal glob-and-parse logic rather than
        /// referencing the <c>AIBT.Mcp</c> assembly from <c>AIBT.Editor</c> -- no existing Editor
        /// file takes that dependency, and this window must work whether or not the MCP bridge is
        /// even present.
        /// </summary>
        private static (IReadOnlyList<TreeDocument> Documents, IReadOnlyList<string> Paths) ScanTreeDocuments(string rootDirectory)
        {
            var documents = new List<TreeDocument>();
            var paths = new List<string>();
            if (!Directory.Exists(rootDirectory)) return (documents, paths);

            var files = Directory.GetFiles(rootDirectory, "*.aibt.json", SearchOption.AllDirectories);
            System.Array.Sort(files, System.StringComparer.Ordinal);

            foreach (var file in files)
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch (IOException) { continue; }

                var result = CanonicalTreeJson.Parse(text, documentId: file);
                if (!result.Success) continue;
                documents.Add(result.Document);
                paths.Add(file);
            }

            return (documents, paths);
        }
    }
}
