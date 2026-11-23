using System.Collections.Generic;
using AIBT.Editor.Registry;
using UnityEditor;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    [CustomEditor(typeof(NativeTreeAgent))]
    internal sealed class NativeTreeAgentInspector : UnityEditor.Editor
    {
        private static ProjectNodeCatalog s_randomTypesCatalog;
        private static HashSet<(ulong TypeId, uint Version)> s_randomTypes;
        private ProductionTreeAsset _hintAsset;
        private string _hintPayloadHash;
        private ProjectNodeCatalog _hintCatalog;
        private bool _hintAssetHasRandomNodes;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var agent = (NativeTreeAgent)target;
            if (agent.Tree == null)
            {
                EditorGUILayout.HelpBox("Assign a runtime asset: open the tree in AIBT > Graph Editor and use Prepare Runtime.", MessageType.Info);
                return;
            }
            var state = ProductionTreeAssetStaleness.EvaluateForInspector(agent.Tree);
            if (state != ProductionTreeAssetState.Current && state != ProductionTreeAssetState.Unknown)
                EditorGUILayout.HelpBox(ProductionTreeAssetStaleness.Describe(state), MessageType.Warning);
            if (TreeHasRandomNodes(agent.Tree, ProjectNodeCatalogCache.Current) && agent.RandomStreamId == 0)
                EditorGUILayout.HelpBox(RandomStreamHintText, MessageType.Info);

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

        /// <summary>
        /// Whether the agent's tree has random nodes, cached per inspector so it is not decoded every repaint.
        /// The key includes the payload hash: Prepare Runtime copies new content into the existing asset.
        /// </summary>
        internal bool TreeHasRandomNodes(ProductionTreeAsset asset, ProjectNodeCatalog catalog)
        {
            var payloadHash = asset == null ? null : asset.PayloadHash;
            if (!ReferenceEquals(_hintAsset, asset) || !ReferenceEquals(_hintCatalog, catalog)
                || !string.Equals(_hintPayloadHash, payloadHash, System.StringComparison.Ordinal))
            {
                _hintAsset = asset;
                _hintPayloadHash = payloadHash;
                _hintCatalog = catalog;
                _hintAssetHasRandomNodes = HasRandomNodes(asset, catalog);
            }
            return _hintAssetHasRandomNodes;
        }

        private const string RandomStreamHintText =
            "This tree has random nodes and Random Stream Id is 0: every run draws other numbers in every session"
            + " (the streams use the host's process-wide instance id). To repeat a run's draws, set a nonzero Random"
            + " Stream Id, distinct per agent (prefab instances share this value; set it from code when you spawn).";

        /// <summary>
        /// P11-012: the inspector's hint when the agent's tree has random nodes and its
        /// <see cref="NativeTreeAgent.RandomStreamId"/> is 0; null otherwise.
        /// </summary>
        internal static string RandomStreamHint(NativeTreeAgent agent, ProjectNodeCatalog catalog)
            => agent != null && agent.RandomStreamId == 0 && HasRandomNodes(agent.Tree, catalog) ? RandomStreamHintText : null;

        /// <summary>
        /// P11-012: whether a prepared tree uses a node that declares a random stream
        /// (<c>[AibtRandomStream]</c>), by the project's generated node descriptors. False when the asset
        /// cannot be read.
        /// </summary>
        internal static bool HasRandomNodes(ProductionTreeAsset asset, ProjectNodeCatalog catalog)
        {
            if (asset == null || catalog == null || !asset.TryReadSemanticProgram(out var program)) return false;
            if (!ReferenceEquals(s_randomTypesCatalog, catalog))
            {
                var types = new HashSet<(ulong, uint)>();
                foreach (var descriptor in catalog.Generated.Values)
                    if (descriptor.HasRandomStream)
                        types.Add((StableHash.Fnv1A64(descriptor.Manifest.TypeId), descriptor.Manifest.Version));
                s_randomTypes = types;
                s_randomTypesCatalog = catalog;
            }
            for (var index = 0; index < program.Nodes.Count; index++)
                if (s_randomTypes.Contains((program.Nodes[index].NodeTypeId, program.Nodes[index].NodeTypeVersion))) return true;
            return false;
        }
    }
}
