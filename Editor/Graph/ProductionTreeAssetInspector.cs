using UnityEditor;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    [CustomEditor(typeof(ProductionTreeAsset))]
    internal sealed class ProductionTreeAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (ProductionTreeAsset)target;
            EditorGUILayout.LabelField("Prepared runtime tree", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Source", asset.SourceId, EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox("This is a compiled snapshot. After editing the source or node catalog, use Prepare Runtime in the graph and restart its hosts.", MessageType.Info);
            if (GUILayout.Button("Open Source Graph"))
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(asset.SourceId) == null)
                    EditorUtility.DisplayDialog("AIBT", "The original source path is unavailable. Open its current location in the graph editor.", "OK");
                else BehaviorTreeGraphWindow.OpenTree(asset.SourceId);
            }
        }
    }
}
