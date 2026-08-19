using System;
using System.Collections.Generic;
using System.IO;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Preview
{
    /// <summary>
    /// Steps/plays a <c>.aibt.json</c> tree through <see cref="ReferencePreviewDriver"/> (the Phase 1
    /// managed reference executor, driven as-is) and highlights the active node(s) live. Never writes
    /// to <c>.aibt.json</c> or <c>.aibt.layout.json</c> -- this window only reads a document into
    /// memory and steps a driver over it; <see cref="LoadDocument"/> can be called again with an
    /// edited <see cref="TreeDocument"/> (e.g. the result of a <c>SemanticEditTransaction</c>) to
    /// preview an edit without restarting the editor.
    /// </summary>
    public sealed class BehaviorTreePreviewWindow : EditorWindow
    {
        private static readonly Color ActiveNodeColor = new Color(0.95f, 0.85f, 0.15f);
        private static readonly Color BreakpointNodeColor = new Color(0.85f, 0.20f, 0.20f);

        private readonly HashSet<NodeId> _breakpoints = new HashSet<NodeId>();

        private BehaviorTreeGraphView _graphView;
        private Label _statusLabel;
        private ScrollView _blackboardView;
        private Button _playButton;

        private TreeDocument _document;
        private string _sourceId;
        private ReferencePreviewDriver _driver;
        private DiagnosticCollection _diagnostics;
        private bool _isPlaying;

        [MenuItem("AIBT/Behavior Tree Preview")]
        public static BehaviorTreePreviewWindow ShowWindow()
        {
            var window = GetWindow<BehaviorTreePreviewWindow>();
            window.titleContent = new GUIContent("AIBT Preview");
            return window;
        }

        /// <summary>Loads a document from disk (never mutated) and prepares a fresh driver for it.</summary>
        public void LoadFromPath(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var result = CanonicalTreeJson.Parse(bytes, path);
            if (!result.Success)
            {
                _document = null;
                _driver = null;
                _diagnostics = result.Diagnostics;
                Refresh();
                return;
            }

            LoadDocument(result.Document, ToSourceId(path));
        }

        /// <summary>
        /// Converts an absolute filesystem path to the canonical relative, forward-slash logical ID
        /// <c>ReferenceCompilerOptions.SourceId</c> requires: the path relative to the Unity project
        /// root when the file is inside it, or just its file name otherwise.
        /// </summary>
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

        /// <summary>
        /// Prepares a fresh driver for an in-memory document -- used to preview a document that was
        /// just edited (e.g. via <c>SemanticEditTransaction</c>) without touching disk or restarting
        /// the editor.
        /// </summary>
        public void LoadDocument(TreeDocument document, string sourceId)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _sourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            _isPlaying = false;
            ReferencePreviewDriver.TryCreate(_document, _sourceId, out _driver, out _diagnostics);
            _breakpoints.Clear();
            Refresh();
        }

        private void OnEnable()
        {
            BuildLayout();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void BuildLayout()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(OnLoadClicked) { text = "Load..." });
            toolbar.Add(new ToolbarButton(OnStepClicked) { text = "Step" });
            toolbar.Add(new ToolbarButton(OnRunTickClicked) { text = "Run Tick" });
            _playButton = new ToolbarButton(OnPlayClicked) { text = "Play" };
            toolbar.Add(_playButton);
            toolbar.Add(new ToolbarButton(OnRestartClicked) { text = "Restart" });
            rootVisualElement.Add(toolbar);

            _statusLabel = new Label("No document loaded.");
            _statusLabel.style.paddingLeft = 4;
            _statusLabel.style.paddingTop = 2;
            _statusLabel.style.paddingBottom = 2;
            rootVisualElement.Add(_statusLabel);

            var body = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            rootVisualElement.Add(body);

            _graphView = new BehaviorTreeGraphView { name = "aibt-preview-graph-view" };
            _graphView.style.flexGrow = 1;
            body.Add(_graphView);

            _blackboardView = new ScrollView { style = { width = 260 } };
            body.Add(_blackboardView);
        }

        private void OnLoadClicked()
        {
            var path = EditorUtility.OpenFilePanel("Load behavior tree", Application.dataPath, "json");
            if (!string.IsNullOrEmpty(path))
            {
                LoadFromPath(path);
            }
        }

        private void OnStepClicked()
        {
            if (_driver == null) return;
            if (!_driver.HasOpenTick) _driver.BeginTick();
            else _driver.StepAtomic();
            Refresh();
        }

        private void OnRunTickClicked()
        {
            if (_driver == null) return;
            _driver.RunTick(_breakpoints);
            Refresh();
        }

        private void OnPlayClicked()
        {
            _isPlaying = !_isPlaying;
            _playButton.text = _isPlaying ? "Pause" : "Play";
        }

        private void OnRestartClicked()
        {
            _isPlaying = false;
            if (_playButton != null) _playButton.text = "Play";
            _driver?.Restart();
            Refresh();
        }

        private void OnEditorUpdate()
        {
            if (!_isPlaying || _driver == null) return;
            _driver.RunTick(_breakpoints);
            if (!_driver.HasOpenTick && _driver.TerminalResult.HasValue)
            {
                _isPlaying = false;
                if (_playButton != null) _playButton.text = "Play";
            }

            Refresh();
        }

        private void Refresh()
        {
            if (_document != null)
            {
                _graphView.Populate(_document, ReferencePreviewDriver.CreatePreviewNodeRegistry());
                AttachBreakpointMenus();
            }

            RefreshHighlighting();
            RefreshStatus();
            RefreshBlackboard();
        }

        private void AttachBreakpointMenus()
        {
            foreach (var pair in _graphView.NodesById)
            {
                var nodeId = pair.Key;
                pair.Value.AddManipulator(new ContextualMenuManipulator(menuEvent =>
                {
                    var isBreakpoint = _breakpoints.Contains(nodeId);
                    menuEvent.menu.AppendAction(
                        isBreakpoint ? "Remove Breakpoint" : "Add Breakpoint",
                        _ =>
                        {
                            if (isBreakpoint) _breakpoints.Remove(nodeId);
                            else _breakpoints.Add(nodeId);
                            RefreshHighlighting();
                        });
                }));
            }
        }

        private void RefreshHighlighting()
        {
            var active = _driver != null
                ? new HashSet<NodeId>(_driver.ActiveNodeIds)
                : new HashSet<NodeId>();

            foreach (var pair in _graphView.NodesById)
            {
                var node = pair.Value;
                if (active.Contains(pair.Key))
                {
                    SetBorder(node, ActiveNodeColor, 3f);
                }
                else if (_breakpoints.Contains(pair.Key))
                {
                    SetBorder(node, BreakpointNodeColor, 2f);
                }
                else
                {
                    SetBorder(node, Color.clear, 0f);
                }
            }
        }

        private static void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }

        private void RefreshStatus()
        {
            if (_document == null)
            {
                _statusLabel.text = HasErrors(_diagnostics)
                    ? "Failed to load: " + FirstError(_diagnostics)
                    : "No document loaded.";
                return;
            }

            if (_driver == null)
            {
                _statusLabel.text = "Compilation failed: " + FirstError(_diagnostics);
                return;
            }

            var terminal = _driver.TerminalResult.HasValue ? _driver.TerminalResult.Value.ToString() : "(running)";
            _statusLabel.text = $"{_sourceId} -- open tick: {_driver.HasOpenTick} -- terminal: {terminal}";
        }

        private void RefreshBlackboard()
        {
            _blackboardView.Clear();
            if (_driver == null) return;

            var inspection = _driver.CaptureInspection();
            foreach (var value in inspection.Blackboard)
            {
                var text = value.IsRegistered
                    ? $"{value.Key} = <registered {value.RegisteredTypeId}:{value.RegisteredTypeVersion}>"
                    : $"{value.Key} = {value.BuiltInValue}";
                _blackboardView.Add(new Label(text));
            }
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
