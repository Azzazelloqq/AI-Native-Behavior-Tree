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
    public static class FirstAgentSetup
    {
        [MenuItem("AIBT/Samples/First Agent/Create Demo")]
        public static void CreateDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var sources = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)
                && path.EndsWith("/first-agent.aibt.json", StringComparison.Ordinal)).ToArray();
            if (sources.Length != 1) throw new InvalidOperationException("Import exactly one First Agent sample before creating the demo.");
            if (AssetDatabase.IsValidFolder("Assets/AibtFirstAgentDemo"))
                throw new InvalidOperationException("Assets/AibtFirstAgentDemo already exists. Open its scene, or choose another project for a fresh demo.");
            var parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(sources[0]), sources[0]);
            if (!parsed.Success) throw new InvalidOperationException(string.Join("\n", parsed.Diagnostics));
            var asset = ProductionTreeAssetBaker.Create(parsed.Document, sources[0], typeof(FirstAgentCatalog), out var diagnostics);
            if (asset == null) throw new InvalidOperationException(string.Join("\n", diagnostics));
            AssetDatabase.CreateFolder("Assets", "AibtFirstAgentDemo");
            AssetDatabase.CreateAsset(asset, "Assets/AibtFirstAgentDemo/FirstAgent.runtime.asset");
            AssetDatabase.SaveAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            asset = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>("Assets/AibtFirstAgentDemo/FirstAgent.runtime.asset");
            if (asset == null) throw new InvalidOperationException("The prepared runtime asset could not be loaded.");
            var agent = GameObject.CreatePrimitive(PrimitiveType.Cube);
            agent.name = "First Agent";
            var component = agent.AddComponent<FirstAgent>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty("_tree").objectReferenceValue = asset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (serialized.FindProperty("_tree").objectReferenceValue == null)
                throw new InvalidOperationException("The demo agent did not retain its runtime tree reference.");
            var goal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            goal.name = "Goal";
            goal.transform.position = new Vector3(5f, 0f, 0f);
            goal.transform.localScale = Vector3.one * 0.3f;
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(2.5f, 3f, -7f);
            camera.transform.LookAt(new Vector3(2.5f, 0f, 0f));
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/AibtFirstAgentDemo/FirstAgent.unity");
            AssetDatabase.SaveAssets();
        }
    }
}
