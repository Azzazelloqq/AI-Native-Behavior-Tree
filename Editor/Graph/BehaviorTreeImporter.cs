using System;
using System.IO;
using System.Linq;
using System.Text;
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
    /// <para>
    /// A tree that uses subtrees (ADR-P11-001) is expanded against the project's trees (every
    /// <c>.aibt</c> and <c>.aibt.json</c> file under <c>Assets/</c> and the project's packages) and
    /// the source holds the expanded tree. Such an import
    /// depends on every subtree file it read, on the tree-ID index entry of every tree ID it looked
    /// up (so a new, removed or duplicate file with that ID re-imports it) and on the project node
    /// registry. Version 3: version-2 imports never expanded.
    /// </para>
    /// </summary>
    [ScriptedImporter(3, "aibt")]
    public sealed class BehaviorTreeImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var path = context.assetPath;
            var json = File.ReadAllText(path);
            var expansion = ExpandSubtrees(context, path);
            var entry = expansion == null
                ? TreeCatalogEntry.Read(path)
                : TreeCatalogEntry.Read(path, NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry, expansion);
            var source = ScriptableObject.CreateInstance<BehaviorTreeSource>();
            if (expansion != null && expansion.Success)
            {
                var expanded = CanonicalTreeJson.Serialize(expansion.Document);
                source.SetContent(expanded.Success ? Encoding.UTF8.GetString(expanded.Utf8) : json, path, entry.TreeId, entry.Name,
                    entry.NodeCount, entry.Status, entry.Details, expanded.Success ? expansion.NodeSourceIds : null);
            }
            else
            {
                source.SetContent(json, path, entry.TreeId, entry.Name, entry.NodeCount, entry.Status, entry.Details);
            }
            context.AddObjectToAsset("source", source, EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D);
            context.SetMainObject(source);

            var asset = ScriptableObject.CreateInstance<BehaviorTreeAsset>();
            asset.name = "Editor Summary";
            asset.hideFlags = HideFlags.HideInHierarchy;
            asset.Initialize(entry);
            context.AddObjectToAsset("tree", asset);
        }

        // Null for a tree without references and interface (imported exactly as before).
        private static SubtreeExpansion ExpandSubtrees(AssetImportContext context, string path)
        {
            TreeJsonReadResult parsed;
            try { parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path); }
            catch (IOException) { return null; }
            if (!parsed.Success || !SubtreeExpander.RequiresExpansion(parsed.Document)) return null;
            var recorder = new RecordingSubtreeResolver(ProjectTreeIndex.Scan());
            var catalog = ProjectNodeCatalog.Discover();
            var expansion = ProjectTreeIndex.Expand(path, parsed.Document, catalog.Registry, catalog.RegisteredTypes, recorder);
            // The expansion stops at the first failing level; the walk also names the files a later
            // fix would make it read, so editing any of them re-imports this tree.
            recorder.Walk(parsed.Document);
            foreach (var treeId in recorder.Requested) context.DependsOnCustomDependency(ProjectTreeIndex.DependencyName(treeId));
            foreach (var source in recorder.Sources)
                if (!string.Equals(source, path, StringComparison.Ordinal)) context.DependsOnSourceAsset(source);
            context.DependsOnCustomDependency(ProjectTreeIndex.RegistryDependencyName);
            return expansion;
        }

        [OnOpenAsset(0)]
#if UNITY_6000_6_OR_NEWER
        private static bool OpenTree(UnityEngine.EntityId instanceId, int line)
#else
        private static bool OpenTree(int instanceId, int line)
#endif
        {
            var path = AssetDatabase.GetAssetPath(instanceId);
            if (!TreeSourcePaths.IsTree(path)) return false;
            BehaviorTreeGraphWindow.OpenTree(path);
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
                var unknown = parsed.Document.Nodes.Where(node => node.Subtree == null && !catalog.Registry.TryGet(node.TypeId, out _))
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
                if (GUILayout.Button("Open Graph")) BehaviorTreeGraphWindow.OpenTree(path);
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
            if (GUILayout.Button("Open Graph")) BehaviorTreeGraphWindow.OpenTree(AssetDatabase.GetAssetPath(asset));
            if (GUILayout.Button("Open JSON Source")) UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(AssetDatabase.GetAssetPath(asset), 1);
        }
    }
}
