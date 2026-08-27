using System;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.HotReload
{
    /// <summary>
    /// Surfaces hot reload as an explicit, explained Editor workflow (<c>P5-008</c>): load a tree,
    /// optionally run it, then explicitly reload it against a second file and see exactly what
    /// <see cref="HotReloadPreviewDriver.TryReload"/> actually did -- classification per node, the
    /// strategy applied, and the migrated/reset/dropped counts -- before or as the reload happens,
    /// never silently in the background. A single explicit "Reload" button is the only trigger;
    /// this window never watches files or reloads automatically.
    /// </summary>
    public sealed class HotReloadWorkflowWindow : EditorWindow
    {
        private Label _statusLabel;
        private Label _outcomeLabel;
        private ScrollView _nodeVerdictsView;

        private HotReloadPreviewDriver _driver;
        private string _currentPath;
        private DiagnosticCollection _diagnostics;

        [MenuItem("AIBT/Hot Reload Workflow")]
        public static HotReloadWorkflowWindow ShowWindow()
        {
            var window = GetWindow<HotReloadWorkflowWindow>();
            window.titleContent = new GUIContent("AIBT Hot Reload");
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
            toolbar.Add(new ToolbarButton(OnLoadClicked) { text = "Load..." });
            toolbar.Add(new ToolbarButton(OnRunTickClicked) { text = "Run Tick" });
            toolbar.Add(new ToolbarButton(OnReloadClicked) { text = "Reload From..." });
            rootVisualElement.Add(toolbar);

            _statusLabel = new Label("No document loaded.");
            _statusLabel.style.paddingLeft = 4;
            _statusLabel.style.paddingTop = 4;
            rootVisualElement.Add(_statusLabel);

            _outcomeLabel = new Label(string.Empty);
            _outcomeLabel.style.paddingLeft = 4;
            _outcomeLabel.style.paddingTop = 4;
            _outcomeLabel.style.whiteSpace = WhiteSpace.Normal;
            rootVisualElement.Add(_outcomeLabel);

            var verdictsHeader = new Label("Per-node verdicts (last reload):");
            verdictsHeader.style.paddingLeft = 4;
            verdictsHeader.style.paddingTop = 8;
            rootVisualElement.Add(verdictsHeader);

            _nodeVerdictsView = new ScrollView { style = { flexGrow = 1 } };
            rootVisualElement.Add(_nodeVerdictsView);
        }

        private void OnLoadClicked()
        {
            var path = EditorUtility.OpenFilePanel("Load behavior tree", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path)) return;

            LoadFromPath(path);
        }

        /// <summary>Loads a fresh document from disk as the currently live instance, discarding any previous one.</summary>
        public void LoadFromPath(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var parsed = CanonicalTreeJson.Parse(bytes, path);
            if (!parsed.Success)
            {
                _driver = null;
                _diagnostics = parsed.Diagnostics;
                _currentPath = null;
                RefreshStatus();
                return;
            }

            var sourceId = ToSourceId(path);
            if (!HotReloadPreviewDriver.TryCreate(parsed.Document, sourceId, out _driver, out _diagnostics))
            {
                _currentPath = null;
                RefreshStatus();
                return;
            }

            _currentPath = path;
            _outcomeLabel.text = string.Empty;
            _nodeVerdictsView.Clear();
            RefreshStatus();
        }

        private void OnRunTickClicked()
        {
            if (_driver == null) return;
            _driver.RunOneTick();
            RefreshStatus();
        }

        private void OnReloadClicked()
        {
            if (_driver == null) return;

            var path = EditorUtility.OpenFilePanel("Reload from", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path)) return;

            ReloadFromPath(path);
        }

        /// <summary>
        /// Compiles and reloads against the document at <paramref name="path"/>, replacing the
        /// currently live instance and displaying exactly what happened.
        /// </summary>
        public void ReloadFromPath(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var parsed = CanonicalTreeJson.Parse(bytes, path);
            if (!parsed.Success)
            {
                _diagnostics = parsed.Diagnostics;
                _outcomeLabel.text = "Reload failed to parse: " + FirstError(_diagnostics);
                return;
            }

            var sourceId = ToSourceId(path);
            if (!_driver.TryReload(parsed.Document, sourceId, out var outcome, out _diagnostics))
            {
                _outcomeLabel.text = "Reload failed to compile: " + FirstError(_diagnostics);
                return;
            }

            _currentPath = path;
            DisplayOutcome(outcome);
            RefreshStatus();
        }

        private void DisplayOutcome(HotReloadPreviewOutcome outcome)
        {
            var strategy = outcome.FellBackToFullRestart
                ? "Full restart (old instance was still active)"
                : outcome.RequiredFullRestart
                    ? "Full restart (an incompatible change forced it)"
                    : outcome.RestartSubtreeRootNodeIds.Count > 0
                        ? "Subtree restart (" + string.Join(", ", outcome.RestartSubtreeRootNodeIds.Select(id => id.Value)) + ")"
                        : "Compatible migration";

            _outcomeLabel.text =
                "Strategy: " + strategy + "\n" +
                "Migrated: " + outcome.MigratedNodeCount +
                "  Reset: " + outcome.ResetNodeCount +
                "  Dropped: " + outcome.DroppedNodeCount;

            _nodeVerdictsView.Clear();
            foreach (var pair in outcome.NodeVerdicts.OrderBy(p => p.Key.Value))
            {
                var isExcluded = outcome.RestartSubtreeRootNodeIds.Contains(pair.Key);
                var line = pair.Key.Value + ": " + pair.Value + (isExcluded ? " (restart root)" : string.Empty);
                _nodeVerdictsView.Add(new Label(line));
            }
        }

        private void RefreshStatus()
        {
            if (_driver == null)
            {
                _statusLabel.text = HasErrors(_diagnostics)
                    ? "Failed to load: " + FirstError(_diagnostics)
                    : "No document loaded.";
                return;
            }

            var terminal = _driver.TerminalResult.HasValue ? _driver.TerminalResult.Value.ToString() : "(running)";
            _statusLabel.text = _currentPath + " -- active nodes: " + _driver.ActiveNodeCount + " -- terminal: " + terminal;
        }

        private static string ToSourceId(string absolutePath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var full = Path.GetFullPath(absolutePath);
            if (full.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                var relative = full.Substring(projectRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return relative.Replace('\\', '/');
            }

            return Path.GetFileName(full);
        }

        private static bool HasErrors(DiagnosticCollection diagnostics)
        {
            if (diagnostics == null) return false;
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Severity == DiagnosticSeverity.Error) return true;
            }

            return false;
        }

        private static string FirstError(DiagnosticCollection diagnostics)
        {
            if (diagnostics == null) return "(no diagnostics)";
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Severity == DiagnosticSeverity.Error) return diagnostics[index].Message;
            }

            return "(no error diagnostics)";
        }
    }
}
