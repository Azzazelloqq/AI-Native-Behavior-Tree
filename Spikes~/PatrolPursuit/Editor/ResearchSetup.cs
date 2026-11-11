using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.PatrolResearch.Editor
{
    [InitializeOnLoad]
    public static class ResearchSetup
    {
        private const string Folder = "Assets/PatrolResearch";
        private const string ScenePath = Folder + "/PatrolResearch.unity";
        static ResearchSetup() { EditorApplication.update += Pump; }
        public static void Open() { Debug.Log("PATROL_EDITOR_READY"); }
        private static void Pump()
        {
            var root = Path.GetFullPath(Application.dataPath + "/../Temp/");
            if (File.Exists(root + "research-create.request")) { File.Delete(root + "research-create.request"); Create(); }
            if (File.Exists(root + "research-run.request"))
            {
                File.Delete(root + "research-run.request");
                EditorSceneManager.OpenScene(ScenePath);
                File.WriteAllText(root + "research-probe.request", "45 seconds");
                EditorApplication.isPlaying = true;
            }
            if (File.Exists(root + "research-quit.request")) { File.Delete(root + "research-quit.request"); EditorApplication.Exit(0); }
        }

        [MenuItem("AIBT/Research/Create Patrol Scene")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder(Folder + "/Materials")) AssetDatabase.CreateFolder(Folder, "Materials");
            var background = new Color(0.035f, 0.055f, 0.085f);
            var camera = new GameObject("Overview Camera").AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 11.6f;
            camera.transform.position = new Vector3(0, 26, -16); camera.transform.LookAt(new Vector3(0, 0, 1));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = background;
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(60, -25, 0); light.intensity = 1.2f;
            RenderSettings.ambientLight = new Color(0.65f, 0.7f, 0.8f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Flat map / direct locomotion";
            floor.transform.position = new Vector3(0, -0.2f, 0); floor.transform.localScale = new Vector3(25, 0.3f, 17);
            floor.GetComponent<Renderer>().sharedMaterial = Material(new Color(0.11f, 0.16f, 0.22f));
            for (var x = -12; x <= 12; x += 2) Line("Grid", new[] {new Vector3(x,0,-8),new Vector3(x,0,8)}, new Color(0.19f,0.24f,0.3f), 0.025f);
            for (var z = -8; z <= 8; z += 2) Line("Grid", new[] {new Vector3(-12,0,z),new Vector3(12,0,z)}, new Color(0.19f,0.24f,0.3f), 0.025f);
            var blue = new Color(0.23f, 0.63f, 1f); var orange = new Color(1f, 0.52f, 0.18f); var green = new Color(0.35f, 0.9f, 0.55f);
            var patrolRoute = new[] { Point(-5,-3), Point(-5,3), Point(0,3), Point(0,-3) };
            var patrol = Actor(ResearchKind.Patroller, Point(-5,-3), blue, patrolRoute, "patrol");
            var runner = Actor(ResearchKind.Runner, Point(-11,6), orange, new[] {Point(-5,-1),Point(10,-1),Point(10,6),Point(-10,6),Point(-10,-6),Point(-5,-6)}, "runner");
            var wander = Actor(ResearchKind.Wanderer, Point(7,-5), green, new[] {Point(7,5)}, "wander");
            patrol.TargetA = runner; patrol.TargetB = wander;
            patrol.Alert = Text("Detection !", "!", new Vector3(0, 3.5f, 0), 0.38f, Color.yellow, patrol.transform);
            patrol.Alert.gameObject.SetActive(false);
            for (var i = 0; i < patrolRoute.Length; i++)
            {
                var p = patrolRoute[i]; p.y = 0.04f; var next = patrolRoute[(i+1)%patrolRoute.Length]; next.y = p.y;
                Line("Patrol route", new[]{p,next}, blue * 0.7f, 0.08f);
                Text("Waypoint", (i+1).ToString(), p + Vector3.up * 0.15f, 0.08f, blue).transform.rotation = camera.transform.rotation;
            }
            var scene = new GameObject("AIBT World").AddComponent<ResearchScene>();
            scene.Actors = new[] {patrol,runner,wander}; scene.View = camera;
            scene.Sight = Line("Sight cone (5.2m / 130deg)", new[]{Vector3.zero,Vector3.forward}, new Color(1f,0.83f,0.28f), 0.045f);
            scene.Status = Text("World HUD", "AIBT / PATROL RESEARCH", new Vector3(-11,0,12.5f), 0.11f, Color.white);
            scene.Status.anchor = TextAnchor.UpperLeft; scene.Status.alignment = TextAlignment.Left;
            scene.Status.transform.rotation = camera.transform.rotation;
            EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene(ScenePath, true)};
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets(); Debug.Log("PATROL_SCENE_CREATED");
        }

        private static Vector3 Point(float x, float z) => new Vector3(x, 0.6f, z);
        private static ResearchActor Actor(ResearchKind kind, Vector3 position, Color color, Vector3[] route, string tree)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); body.name = kind.ToString(); body.transform.position = position;
            body.transform.localScale = new Vector3(0.65f,0.6f,0.65f); body.GetComponent<Renderer>().sharedMaterial = Material(color);
            var actor = body.AddComponent<ResearchActor>(); actor.Kind = kind; actor.Route = route;
            actor.Tree = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(Folder + "/Trees/" + tree + ".runtime.asset");
            if (actor.Tree == null) throw new InvalidOperationException("Prepare " + tree + " runtime asset through CLI first.");
            actor.Label = Text(kind + " label", kind.ToString(), new Vector3(0, 1.6f, 0), 0.1f, color, body.transform);
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube); nose.name = "Heading"; nose.transform.SetParent(body.transform, false);
            nose.transform.localPosition = new Vector3(0,0.5f,0.6f); nose.transform.localScale = new Vector3(0.2f,0.2f,0.5f);
            nose.GetComponent<Renderer>().sharedMaterial = Material(Color.white);
            return actor;
        }
        private static Material Material(Color color)
        {
            var path = Folder + "/Materials/" + ColorUtility.ToHtmlStringRGBA(color) + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Standard")); material.color = color;
            AssetDatabase.CreateAsset(material, path); return material;
        }
        private static TextMesh Text(string name, string text, Vector3 position, float size, Color color, Transform parent = null)
        {
            var label = new GameObject(name).AddComponent<TextMesh>(); label.text = text; label.fontSize = 64;
            label.characterSize = size; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = color;
            if (parent != null) label.transform.SetParent(parent, false);
            label.transform.localPosition = position; return label;
        }
        private static LineRenderer Line(string name, Vector3[] points, Color color, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>(); line.useWorldSpace = true; line.positionCount = points.Length;
            line.SetPositions(points); line.startWidth = line.endWidth = width;
            var path = Folder + "/Materials/Line.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Sprites/Default")); AssetDatabase.CreateAsset(material, path); }
            line.sharedMaterial = material; line.startColor = line.endColor = color;
            return line;
        }
    }
}
