using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Registry;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Callbacks;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    /// <summary>
    /// Imports an <c>.aibt</c> tree. The main object is a Player-loadable
    /// <see cref="BehaviorTreeSource"/> that a <see cref="BehaviorTreeAgent"/> runs directly; the
    /// Editor-only <see cref="BehaviorTreeAsset"/> summary is kept as a sub-object.
    /// </summary>
    [ScriptedImporter(2, "aibt")]
    public sealed class BehaviorTreeImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var entry = TreeCatalogEntry.Read(context.assetPath);
            var source = ScriptableObject.CreateInstance<BehaviorTreeSource>();
            source.SetContent(File.ReadAllText(context.assetPath), context.assetPath, entry.TreeId, entry.Name,
                entry.NodeCount, entry.Status, entry.Details);
            context.AddObjectToAsset("source", source, EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D);
            context.SetMainObject(source);

            var asset = ScriptableObject.CreateInstance<BehaviorTreeAsset>();
            asset.name = "Editor Summary";
            asset.hideFlags = HideFlags.HideInHierarchy;
            asset.Initialize(entry);
            context.AddObjectToAsset("tree", asset);
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

    [CustomEditor(typeof(BehaviorTreeSource))]
    internal sealed class BehaviorTreeSourceInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var source = (BehaviorTreeSource)target;
            var path = AssetDatabase.GetAssetPath(source);
            EditorGUILayout.LabelField(string.IsNullOrEmpty(source.TreeName) ? source.name : source.TreeName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Tree ID", source.TreeId ?? string.Empty);
            EditorGUILayout.LabelField("Nodes", source.NodeCount.ToString());

            // The import-time summary knows only built-in nodes; the project catalog also knows
            // this project's own nodes, so validate against it for the status shown here.
            var catalog = ProjectNodeCatalogCache.Current;
            var parsed = CanonicalTreeJson.Parse(source.Json ?? string.Empty, path);
            if (!parsed.Success)
            {
                EditorGUILayout.HelpBox(string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)), MessageType.Error);
            }
            else
            {
                var unknown = parsed.Document.Nodes.Where(node => !catalog.Registry.TryGet(node.TypeId, out _))
                    .Select(node => node.TypeId).Distinct().ToArray();
                if (unknown.Length != 0)
                {
                    EditorGUILayout.HelpBox("Unknown node types: " + string.Join(", ", unknown), MessageType.Warning);
                }
                else
                {
                    var managed = catalog.UsesManagedNodes(parsed.Document);
                    EditorGUILayout.LabelField("Runs in", managed ? "Behavior Tree Agent (managed)" : "Behavior Tree Agent or native host");
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(path)))
            {
                if (GUILayout.Button("Open Graph")) BehaviorTreeGraphWindow.ShowWindow().OpenFromPath(path);
                if (GUILayout.Button("Open Text Source")) UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(path, 1);
            }
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
