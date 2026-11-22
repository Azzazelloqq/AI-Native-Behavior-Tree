using UnityEditor;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    [CustomEditor(typeof(ProductionTreeAsset))]
    internal sealed class ProductionTreeAssetInspector : UnityEditor.Editor
    {
        private string _lastError;

        public override void OnInspectorGUI()
        {
            var asset = (ProductionTreeAsset)target;
            var state = ProductionTreeAssetStaleness.EvaluateForInspector(asset);
            var sourcePath = ProductionTreeAssetStaleness.SourcePath(asset);
            var catalogType = ProductionTreeAssetStaleness.CatalogType(asset);

            EditorGUILayout.LabelField("Prepared runtime tree", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Source", sourcePath ?? asset.SourceId + " (missing)", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Catalog", catalogType != null ? catalogType.FullName : "(unknown)", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(4);

            var message = ProductionTreeAssetStaleness.Describe(state);
            if (state == ProductionTreeAssetState.Current)
                message += " Saving the source or changing the catalog re-prepares it automatically; running agents pick it up on their next start.";
            EditorGUILayout.HelpBox(message, state == ProductionTreeAssetState.Current ? MessageType.Info
                : state == ProductionTreeAssetState.Unknown ? MessageType.Info : MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(sourcePath == null || catalogType == null))
                {
                    var label = state == ProductionTreeAssetState.Current ? "Prepare Again" : "Prepare Now";
                    if (GUILayout.Button(new GUIContent(label, "Recompile this snapshot from its source tree and node catalog.")))
                        _lastError = ProductionTreeAssetStaleness.TryPrepareAgain(asset, out var error) ? null : error;
                }
                using (new EditorGUI.DisabledScope(sourcePath == null))
                {
                    if (GUILayout.Button("Open Source Graph")) BehaviorTreeGraphWindow.OpenTree(sourcePath);
                }
            }
            if (!string.IsNullOrEmpty(_lastError)) EditorGUILayout.HelpBox("Prepare failed: " + _lastError, MessageType.Error);
        }
    }
}
