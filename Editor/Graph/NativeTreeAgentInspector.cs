using UnityEditor;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    [CustomEditor(typeof(NativeTreeAgent))]
    internal sealed class NativeTreeAgentInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var agent = (NativeTreeAgent)target;
            if (agent.Tree == null)
            {
                EditorGUILayout.HelpBox("Assign a runtime asset: open the tree in AIBT > Graph Editor and use Prepare Runtime.", MessageType.Info);
                return;
            }
            var state = ProductionTreeAssetStaleness.Evaluate(agent.Tree);
            if (state != ProductionTreeAssetState.Current && state != ProductionTreeAssetState.Unknown)
                EditorGUILayout.HelpBox(ProductionTreeAssetStaleness.Describe(state), MessageType.Warning);

            if (!EditorApplication.isPlaying) return;
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Live", EditorStyles.boldLabel);
            if (agent.LastError != null)
                EditorGUILayout.HelpBox(agent.LastError, MessageType.Error);
            EditorGUILayout.LabelField("Status", agent.IsRunning ? "Running" : agent.Host == null ? "Stopped" : "Finished");
            EditorGUILayout.LabelField("Finished runs", agent.CompletedRuns.ToString());
            EditorGUILayout.LabelField("Last result", agent.LastResult.HasValue ? agent.LastResult.Value.ToString() : "—");
            if (agent.Host != null) EditorGUILayout.LabelField("Updates this run", agent.Host.TotalUpdates.ToString());
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Restart")) agent.Restart();
                if (GUILayout.Button("Stop")) agent.Stop();
                var source = ProductionTreeAssetStaleness.SourcePath(agent.Tree);
                using (new EditorGUI.DisabledScope(source == null))
                {
                    if (GUILayout.Button("Debug in Graph"))
                    {
                        var window = BehaviorTreeGraphWindow.OpenTree(source);
                        if (window != null && !window.TryAttachNativeAgent(agent, openMatchingTree: false, out var reason))
                            Debug.LogWarning("AIBT: " + reason, agent);
                    }
                }
            }
            Repaint();
        }
    }
}
