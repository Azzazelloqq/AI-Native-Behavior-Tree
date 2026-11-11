using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Samples.SimpleAgent.Editor
{
    /// <summary>Builds the demo scene: a guard that patrols and chases, and a wanderer it chases.</summary>
    public static class SimpleAgentSetup
    {
        private const string DemoFolder = "Assets/AibtSimpleAgentDemo";
        private const string ScenePath = DemoFolder + "/SimpleAgent.unity";

        [MenuItem("AIBT/Samples/Simple Agent/Create Demo")]
        public static void CreateDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Create();
        }

        /// <summary>Creates and opens the demo scene without prompting; returns its asset path.</summary>
        public static string Create()
        {
            var guardTree = FindTree("guard.aibt");
            var wanderTree = FindTree("wander.aibt");
            if (!AssetDatabase.IsValidFolder(DemoFolder)) AssetDatabase.CreateFolder("Assets", "AibtSimpleAgentDemo");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2.4f, 1f, 2.4f);
            Tint(ground, new Color(0.23f, 0.26f, 0.3f));

            var wanderer = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wanderer.name = "Wanderer";
            wanderer.transform.position = new Vector3(5f, 0.5f, 4f);
            Tint(wanderer, new Color(0.95f, 0.6f, 0.2f));
            Assign(wanderer.AddComponent<BehaviorTreeAgent>(), wanderTree);

            var guard = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            guard.name = "Guard";
            guard.transform.position = new Vector3(-6f, 1f, -4f);
            Tint(guard, new Color(0.25f, 0.55f, 0.95f));
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Facing";
            UnityEngine.Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.transform.SetParent(guard.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.4f, 0.5f);
            nose.transform.localScale = new Vector3(0.25f, 0.25f, 0.5f);
            Tint(nose, Color.white);

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Alert";
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(guard.transform, false);
            marker.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            marker.transform.localScale = Vector3.one * 0.5f;
            Tint(marker, new Color(1f, 0.2f, 0.2f));
            marker.SetActive(false);

            var route = guard.AddComponent<PatrolRoute>();
            route.points = new[]
            {
                new Vector3(-6f, 1f, -4f), new Vector3(6f, 1f, -4f), new Vector3(6f, 1f, 4f), new Vector3(-6f, 1f, 4f),
            };
            guard.AddComponent<AlertView>().marker = marker;
            Assign(guard.AddComponent<BehaviorTreeAgent>(), guardTree);
            guard.AddComponent<GuardSenses>().target = wanderer.transform;

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 17f, -13f);
            camera.transform.LookAt(Vector3.zero);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.11f, 0.13f);
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            return ScenePath;
        }

        private static string FindTree(string fileName)
        {
            var paths = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)
                && path.EndsWith("/" + fileName, StringComparison.Ordinal)).ToArray();
            if (paths.Length != 1)
            {
                throw new InvalidOperationException("Import exactly one Simple Agent sample before creating the demo (found "
                    + paths.Length + " copies of " + fileName + ").");
            }

            return paths[0];
        }

        private static void Assign(BehaviorTreeAgent agent, string treePath)
        {
            // Load here, after the new scene exists: opening a scene unloads unreferenced assets.
            var tree = AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(treePath);
            if (tree == null) throw new InvalidOperationException(treePath + " did not import as a behavior tree.");
            agent.Tree = tree;
            EditorUtility.SetDirty(agent);
            if (agent.Tree == null) throw new InvalidOperationException("The demo agent did not keep its tree reference.");
        }

        private static void Tint(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;
            var material = new Material(renderer.sharedMaterial) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            var path = DemoFolder + "/" + target.name + ".mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);
            renderer.sharedMaterial = material;
        }
    }
}
