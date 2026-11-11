using AIBT.Authoring;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Callbacks;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    [ScriptedImporter(1, "aibt")]
    public sealed class BehaviorTreeImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var asset = ScriptableObject.CreateInstance<BehaviorTreeAsset>();
            asset.Initialize(TreeCatalogEntry.Read(context.assetPath));
            context.AddObjectToAsset("tree", asset, EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D);
            context.SetMainObject(asset);
        }

        [OnOpenAsset(0)]
#if UNITY_6000_7_OR_NEWER
        private static bool OpenTree(UnityEngine.EntityId instanceId, int line)
#else
        private static bool OpenTree(int instanceId, int line)
#endif
        {
            var path = AssetDatabase.GetAssetPath(instanceId);
            if (!TreeSourcePaths.IsTree(path)) return false;
            BehaviorTreeGraphWindow.ShowWindow().OpenFromPath(path);
            return true;
        }
    }

    [CustomEditor(typeof(BehaviorTreeAsset))]
    internal sealed class BehaviorTreeAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (BehaviorTreeAsset)target;
            EditorGUILayout.LabelField(asset.TreeName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Tree ID", asset.TreeId);
            EditorGUILayout.LabelField("Nodes", asset.NodeCount.ToString());
            EditorGUILayout.LabelField("Validation", asset.Status);
            if (!string.IsNullOrEmpty(asset.Details)) EditorGUILayout.HelpBox(asset.Details, MessageType.Info);
            if (GUILayout.Button("Open Graph")) BehaviorTreeGraphWindow.ShowWindow().OpenFromPath(AssetDatabase.GetAssetPath(asset));
            if (GUILayout.Button("Open JSON Source")) UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(AssetDatabase.GetAssetPath(asset), 1);
        }
    }
}
