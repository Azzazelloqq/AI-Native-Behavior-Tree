using System;
using System.IO;
using System.Linq;
using AIBT.Editor.Registry;
using AIBT.Editor.Preview;
using AIBT.Editor.Trace;
using UnityEditor;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    public sealed partial class BehaviorTreeGraphWindow
    {
        private void PreviewCurrent()
        {
            if (Document == null) return;
            var sourceId = _sourcePath ?? "trees/unsaved.aibt.json";
            if (Path.IsPathRooted(sourceId))
            {
                var relative = FileUtil.GetProjectRelativePath(sourceId);
                sourceId = string.IsNullOrEmpty(relative) ? Path.GetFileName(sourceId) : relative;
            }
            BehaviorTreePreviewWindow.ShowWindow().AttachToGraph(this, sourceId.Replace('\\', '/'));
        }

        private void AttachSelectedHost()
        {
            var host = Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponent<ProductionTreeHost>();
            if (!TryAttachHost(host, out var explanation)) EditorUtility.DisplayDialog("AIBT Debug", explanation, "OK");
        }

        internal bool TryAttachHost(ProductionTreeHost host, out string explanation)
        {
            if (!SelectedHostTraceMenu.TryCreateSession(host, out var session, out explanation)) return false;
            if (!MatchesProgram(host.Program))
            {
                explanation = "This graph does not match the selected host's compiled program. Open the matching source or prepare and restart the updated program. Raw trace remains available through Attach Selected Host Trace.";
                return false;
            }
            TraceTimelineWindow.ShowWindow().AttachToGraph(this, session, host.Program);
            return true;
        }

        internal bool MatchesProgram(CompiledProgram program)
        {
            if (Document == null || program == null) return false;
            var catalog = _projectCatalog ?? ProjectNodeCatalogCache.Current;
            var serialized = AIBT.Authoring.CanonicalTreeJson.Serialize(Document, catalog.RegisteredTypes);
            if (!serialized.Success) return false;
            var hash = program.Header.CanonicalSemanticHash.HexadecimalValue;
            var semanticHash = BitConverter.ToString(serialized.SemanticHash).Replace("-", "").ToLowerInvariant();
            if (hash != StableHash.Sha256Hex(serialized.Utf8) && hash != semanticHash) return false;
            var ids = new System.Collections.Generic.HashSet<NodeId>(Document.Nodes.Select(node => node.Id));
            return ids.Count == program.DebugMap.Count && ids.SetEquals(program.DebugMap.Select(entry => entry.AuthoringNodeId))
                && program.DebugMap.Single(entry => entry.RuntimeNodeIndex == program.Header.RootNodeIndex).AuthoringNodeId == Document.Root;
        }

        private void BakeRuntimeAsset()
        {
            var types = ProjectNodeCatalog.RuntimeCatalogTypes().ToArray();
            if (types.Length == 0)
            {
                EditorUtility.DisplayDialog("AIBT", "Create a generated catalog for your project's node shard before preparing a runtime asset.", "OK");
                return;
            }
            var menu = new GenericMenu();
            foreach (var type in types)
            {
                var selected = type;
                menu.AddItem(new GUIContent(type.FullName), false, () => BakeRuntimeAsset(selected));
            }
            menu.ShowAsContext();
        }

        private void BakeRuntimeAsset(Type catalogType)
        {
            if (_session == null || string.IsNullOrEmpty(_sourcePath)) return;
            if (_session.IsDirty)
            {
                EditorUtility.DisplayDialog("Save tree first", "Save the source tree before preparing its runtime asset.", "OK");
                return;
            }
            ProductionTreeAsset asset = null;
            try
            {
                var sourceId = _sourcePath.Replace('\\', '/');
                if (Path.IsPathRooted(sourceId))
                {
                    sourceId = FileUtil.GetProjectRelativePath(sourceId);
                    if (string.IsNullOrEmpty(sourceId)) throw new InvalidOperationException("Save the source inside the Unity project before baking.");
                }
                asset = ProductionTreeAssetBaker.Create(Document, sourceId, catalogType, out var diagnostics);
                if (asset == null)
                {
                    EditorUtility.DisplayDialog("Cannot prepare runtime asset", string.Join("\n", diagnostics.Select(d => d.Code.Value + ": " + d.Message)), "OK");
                    return;
                }
                var path = EditorUtility.SaveFilePanelInProject("Prepare AIBT runtime asset",
                    Path.GetFileNameWithoutExtension(_sourcePath) + ".runtime", "asset", "Choose the compiled runtime asset location.");
                if (string.IsNullOrEmpty(path)) return;
                var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (existing != null)
                {
                    if (!(existing is ProductionTreeAsset)) throw new InvalidOperationException("The destination is not an AIBT runtime asset.");
                    EditorUtility.CopySerialized(asset, existing);
                    EditorUtility.SetDirty(existing);
                }
                else
                {
                    AssetDatabase.CreateAsset(asset, path);
                    existing = asset;
                    asset = null;
                }
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(existing);
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is InvalidOperationException)
            {
                EditorUtility.DisplayDialog("Cannot prepare runtime asset", exception.Message, "OK");
            }
            finally
            {
                if (asset != null) DestroyImmediate(asset);
            }
        }
    }
}
