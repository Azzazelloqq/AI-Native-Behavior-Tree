using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Mcp
{
    /// <summary>
    /// Explicit start/stop workflow for the MCP bridge (mirrors P5-008's HotReloadWorkflowWindow
    /// own-window, explicit-trigger-only pattern). Never starts the bridge automatically.
    /// </summary>
    public sealed class McpBridgeWindow : EditorWindow
    {
        private Label _statusLabel;

        [MenuItem("AIBT/MCP/Bridge")]
        public static McpBridgeWindow ShowWindow()
        {
            var window = GetWindow<McpBridgeWindow>();
            window.titleContent = new GUIContent("AIBT MCP Bridge");
            return window;
        }

        private void OnEnable()
        {
            BuildLayout();
            RefreshStatus();
        }

        private void OnInspectorUpdate() => RefreshStatus();

        private void BuildLayout()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(OnStartClicked) { text = "Start" });
            toolbar.Add(new ToolbarButton(OnStopClicked) { text = "Stop" });
            rootVisualElement.Add(toolbar);

            _statusLabel = new Label();
            _statusLabel.style.paddingLeft = 4;
            _statusLabel.style.paddingTop = 4;
            rootVisualElement.Add(_statusLabel);
        }

        private void OnStartClicked()
        {
            McpBridgeAutoRestart.EnsureRunning();
            RefreshStatus();
        }

        private void OnStopClicked()
        {
            McpBridgeAutoRestart.Stop();
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (_statusLabel == null)
            {
                return;
            }

            var listener = McpBridgeAutoRestart.Listener;
            _statusLabel.text = listener != null && listener.IsRunning
                ? "Running on port " + listener.Port
                : "Stopped";
        }
    }
}
