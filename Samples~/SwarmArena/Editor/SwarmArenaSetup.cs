using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Samples.SwarmArena.Editor
{
    /// <summary>
    /// Builds the Swarm Arena demo scene: an arena, a beacon, a danger zone and the object that owns the
    /// scheduler, the world and the scenario. Materials are copies of the active render pipeline's default
    /// material saved next to the scene; the four agent-state materials are saved with GPU instancing on,
    /// so a Player build keeps the instancing shader variants the presenter needs.
    /// </summary>
    public static class SwarmArenaSetup
    {
        private const string DemoFolder = "Assets/AibtSwarmArenaDemo";
        private const string ScenePath = DemoFolder + "/SwarmArena.unity";
        private static readonly string[] StateNames = { "Patrol", "Pursue", "Flee", "Attack" };

        [MenuItem("AIBT/Samples/Swarm Arena/Create Demo")]
        public static void CreateDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Create();
        }

        /// <summary>Creates and opens the demo scene without prompting; returns its asset path.</summary>
        public static string Create()
        {
            var sources = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)
                && path.EndsWith("/swarm-arena.aibt.json", StringComparison.Ordinal)).ToArray();
            if (sources.Length != 1)
            {
                throw new InvalidOperationException("Import exactly one Swarm Arena sample before creating the demo (found "
                    + sources.Length + " copies of swarm-arena.aibt.json).");
            }
            if (!AssetDatabase.IsValidFolder(DemoFolder)) AssetDatabase.CreateFolder("Assets", "AibtSwarmArenaDemo");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Load after the new scene exists: opening a scene unloads unreferenced assets.
            var tree = AssetDatabase.LoadAssetAtPath<TextAsset>(sources[0]);
            if (tree == null) throw new InvalidOperationException(sources[0] + " did not import as a text asset.");

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Arena Ground";
            ground.transform.localScale = new Vector3(3f, 1f, 3f);
            Tint(ground, "Arena-Ground", new Color(0.11f, 0.12f, 0.13f));

            var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            beacon.name = "Beacon";
            UnityEngine.Object.DestroyImmediate(beacon.GetComponent<Collider>());
            beacon.transform.position = new Vector3(28f, 1f, 0f);
            beacon.transform.localScale = Vector3.one * 1.35f;
            Tint(beacon, "Beacon", new Color(1f, 0.92f, 0.35f));

            var danger = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            danger.name = "Danger Zone";
            UnityEngine.Object.DestroyImmediate(danger.GetComponent<Collider>());
            danger.transform.position = new Vector3(-28f, 0.15f, 0f);
            danger.transform.localScale = new Vector3(2f, 0.15f, 2f);
            Tint(danger, "Danger-Zone", new Color(0.85f, 0.2f, 0.75f));

            var arena = new GameObject("AIBT Swarm Arena");
            arena.AddComponent<ProductionTreeScheduler>();
            var world = arena.AddComponent<ProductionTreeWorld>();
            var presenter = arena.AddComponent<SwarmArenaInstancedPresenter>();
            var scenario = arena.AddComponent<SwarmArenaScenario>();
            var serializedScenario = new SerializedObject(scenario);
            serializedScenario.FindProperty("_tree").objectReferenceValue = tree;
            serializedScenario.FindProperty("_scheduler").objectReferenceValue = arena.GetComponent<ProductionTreeScheduler>();
            serializedScenario.FindProperty("_world").objectReferenceValue = world;
            serializedScenario.FindProperty("_beacon").objectReferenceValue = beacon.transform;
            serializedScenario.FindProperty("_danger").objectReferenceValue = danger.transform;
            serializedScenario.ApplyModifiedPropertiesWithoutUndo();
            AssignAgentVisuals(presenter);

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 28f, -22f);
            camera.transform.rotation = Quaternion.Euler(51.8f, 0f, 0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.05f, 0.08f);
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            return ScenePath;
        }

        // The presenter draws a capsule per agent with one saved, instancing-enabled material per visible state.
        private static void AssignAgentVisuals(SwarmArenaInstancedPresenter presenter)
        {
            var prototype = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                var mesh = prototype.GetComponent<MeshFilter>().sharedMesh;
                var baseMaterial = prototype.GetComponent<Renderer>().sharedMaterial;
                var serialized = new SerializedObject(presenter);
                serialized.FindProperty("_mesh").objectReferenceValue = mesh;
                var materials = serialized.FindProperty("_stateMaterials");
                materials.arraySize = StateNames.Length;
                for (var index = 0; index < StateNames.Length; index++)
                {
                    var material = SaveMaterial(baseMaterial, "Agent-" + StateNames[index], SwarmArenaInstancedPresenter.StateColor(index));
                    material.enableInstancing = true;
                    EditorUtility.SetDirty(material);
                    materials.GetArrayElementAtIndex(index).objectReferenceValue = material;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prototype);
            }
        }

        private static void Tint(GameObject target, string fileName, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            renderer.sharedMaterial = SaveMaterial(renderer.sharedMaterial, fileName, color);
        }

        // A copy of the primitive's default material (the active pipeline's), saved next to the scene.
        private static Material SaveMaterial(Material source, string fileName, Color color)
        {
            var material = new Material(source) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            var path = DemoFolder + "/" + fileName + ".mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
