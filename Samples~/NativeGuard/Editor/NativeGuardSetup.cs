using System;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Samples.NativeGuard.Editor
{
    /// <summary>Builds the Native Guard demo: a tower whose native tree uses only package nodes, an intruder and a gate.</summary>
    public static class NativeGuardSetup
    {
        private const string DemoFolder = "Assets/AibtNativeGuardDemo";
        private const string AssetPath = DemoFolder + "/NativeGuard.runtime.asset";
        private const string ScenePath = DemoFolder + "/NativeGuard.unity";

        [MenuItem("AIBT/Samples/Native Guard/Create Demo")]
        public static void CreateDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Create();
        }

        /// <summary>Prepares the runtime asset, creates and opens the demo scene; returns the scene path.</summary>
        public static string Create()
        {
            var sources = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)
                && path.EndsWith("/native-guard.aibt.json", StringComparison.Ordinal)).ToArray();
            if (sources.Length != 1) throw new InvalidOperationException("Import exactly one Native Guard sample before creating the demo.");
            if (!AssetDatabase.IsValidFolder(DemoFolder)) AssetDatabase.CreateFolder("Assets", "AibtNativeGuardDemo");
            PrepareRuntimeAsset(sources[0]);
            AssetDatabase.SaveAssets();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var asset = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(AssetPath);
            if (asset == null) throw new InvalidOperationException("The prepared runtime asset could not be loaded.");

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3f, 1f, 1.2f);
            ground.GetComponent<Renderer>().sharedMaterial = Colored(ground, new Color(0.25f, 0.3f, 0.25f));

            var intruder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            intruder.name = "Intruder";
            intruder.transform.position = new Vector3(-11f, 0.5f, 2f);
            intruder.GetComponent<Renderer>().sharedMaterial = Colored(intruder, new Color(0.9f, 0.4f, 0.9f));

            var gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gate.name = "Gate";
            gate.transform.position = new Vector3(5f, 0f, -3f);
            gate.transform.localScale = new Vector3(3f, 1f, 0.3f);
            gate.GetComponent<Renderer>().sharedMaterial = Colored(gate, new Color(0.55f, 0.35f, 0.2f));

            var tower = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tower.name = "Guard Tower";
            tower.transform.position = new Vector3(0f, 1f, 0f);
            tower.transform.localScale = new Vector3(1f, 2f, 1f);
            var agent = tower.AddComponent<NativeTreeAgent>();
            var serializedAgent = new SerializedObject(agent);
            serializedAgent.FindProperty("_tree").objectReferenceValue = asset;
            // Repeatable draws: each run's streams depend on the seed, this id and the run index only.
            serializedAgent.FindProperty("_randomStreamId").ulongValue = 1UL;
            serializedAgent.ApplyModifiedPropertiesWithoutUndo();
            if (agent.Tree == null) throw new InvalidOperationException("The demo agent did not retain its runtime tree reference.");
            var game = new SerializedObject(tower.AddComponent<NativeGuard>());
            game.FindProperty("_intruder").objectReferenceValue = intruder.transform;
            game.FindProperty("_gate").objectReferenceValue = gate.transform;
            game.ApplyModifiedPropertiesWithoutUndo();

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 7.5f, -11f);
            camera.transform.LookAt(new Vector3(0f, 0f, 0.5f));
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            return ScenePath;
        }

        // A per-object copy of the primitive's default material, saved next to the scene.
        private static Material Colored(GameObject target, Color color)
        {
            var material = new Material(target.GetComponent<Renderer>().sharedMaterial) { color = color };
            AssetDatabase.CreateAsset(material, DemoFolder + "/" + target.name.Replace(' ', '-') + ".mat");
            return material;
        }

        // The asset records its source and catalog, so saving the tree later re-prepares it.
        private static void PrepareRuntimeAsset(string source)
        {
            var parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(source), source);
            if (!parsed.Success) throw new InvalidOperationException(string.Join("\n", parsed.Diagnostics));
            var prepared = ProductionTreeAssetBaker.Create(parsed.Document, source, typeof(NativeGuardCatalog), out var diagnostics);
            if (prepared == null) throw new InvalidOperationException(string.Join("\n", diagnostics));
            var existing = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(AssetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(prepared, AssetPath);
                return;
            }
            EditorUtility.CopySerialized(prepared, existing);
            existing.name = Path.GetFileNameWithoutExtension(AssetPath);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(prepared);
        }
    }
}
