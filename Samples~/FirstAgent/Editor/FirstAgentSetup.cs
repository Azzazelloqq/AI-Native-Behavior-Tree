using System;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Samples.FirstAgent.Editor
{
    /// <summary>Builds the First Agent demo: a cube driven by a native tree that walks between two goals.</summary>
    public static class FirstAgentSetup
    {
        private const string DemoFolder = "Assets/AibtFirstAgentDemo";
        private const string AssetPath = DemoFolder + "/FirstAgent.runtime.asset";
        private const string ScenePath = DemoFolder + "/FirstAgent.unity";

        [MenuItem("AIBT/Samples/First Agent/Create Demo")]
        public static void CreateDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Create();
        }

        /// <summary>Prepares the runtime asset, creates and opens the demo scene; returns the scene path.</summary>
        public static string Create()
        {
            var sources = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)
                && path.EndsWith("/first-agent.aibt.json", StringComparison.Ordinal)).ToArray();
            if (sources.Length != 1) throw new InvalidOperationException("Import exactly one First Agent sample before creating the demo.");
            if (!AssetDatabase.IsValidFolder(DemoFolder)) AssetDatabase.CreateFolder("Assets", "AibtFirstAgentDemo");
            PrepareRuntimeAsset(sources[0]);
            AssetDatabase.SaveAssets();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var asset = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(AssetPath);
            if (asset == null) throw new InvalidOperationException("The prepared runtime asset could not be loaded.");
            var goal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            goal.name = "Goal";
            goal.transform.position = new Vector3(5f, 0f, 0f);
            goal.transform.localScale = Vector3.one * 0.3f;

            var agentObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            agentObject.name = "First Agent";
            agentObject.transform.position = new Vector3(-5f, 0f, 0f);
            var agent = agentObject.AddComponent<NativeTreeAgent>();
            var serializedAgent = new SerializedObject(agent);
            serializedAgent.FindProperty("_tree").objectReferenceValue = asset;
            serializedAgent.ApplyModifiedPropertiesWithoutUndo();
            if (agent.Tree == null) throw new InvalidOperationException("The demo agent did not retain its runtime tree reference.");
            var game = new SerializedObject(agentObject.AddComponent<FirstAgent>());
            game.FindProperty("_goal").objectReferenceValue = goal.transform;
            game.ApplyModifiedPropertiesWithoutUndo();

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 4f, -9f);
            camera.transform.LookAt(Vector3.zero);
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            return ScenePath;
        }

        // The asset records its source and catalog, so saving the tree later re-prepares it.
        private static ProductionTreeAsset PrepareRuntimeAsset(string source)
        {
            var parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(source), source);
            if (!parsed.Success) throw new InvalidOperationException(string.Join("\n", parsed.Diagnostics));
            var prepared = ProductionTreeAssetBaker.Create(parsed.Document, source, typeof(FirstAgentCatalog), out var diagnostics);
            if (prepared == null) throw new InvalidOperationException(string.Join("\n", diagnostics));
            var existing = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(AssetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(prepared, AssetPath);
                return prepared;
            }
            EditorUtility.CopySerialized(prepared, existing);
            existing.name = Path.GetFileNameWithoutExtension(AssetPath);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(prepared);
            return existing;
        }
    }
}
