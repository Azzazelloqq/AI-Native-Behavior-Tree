// Copy into the disposable consumer's Editor verification assembly.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AIBT;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AgentPlayerBuild
{
    public static void Build()
    {
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var config = JObject.Parse(File.ReadAllText(Path.Combine(root, "Library/agent-case.json")));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var asset = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>((string)config["asset"]);
        if (asset == null) throw new InvalidOperationException("Prepared asset was not loaded after scene creation.");
        var actor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        actor.name = "First Agent";
        var integration = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Single(type => type.FullName == "AIBT.Samples.FirstAgent.FirstAgent");
        var component = actor.AddComponent(integration);
        var serialized = new SerializedObject(component);
        serialized.FindProperty("_tree").objectReferenceValue = asset;
        serialized.FindProperty("_targetX").floatValue = (float)config["target"];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (serialized.FindProperty("_tree").objectReferenceValue == null) throw new InvalidOperationException("Smoke scene lost its prepared runtime asset reference.");
        var smokeType = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Single(type => type.Name == "AgentPlayerSmoke");
        var smoke = new GameObject("Player outcome verification").AddComponent(smokeType);
        smokeType.GetField("TargetX").SetValue(smoke, (float)config["target"]);
        smokeType.GetField("Radius").SetValue(smoke, (float)config["stoppingRadius"]);
        smokeType.GetField("ExpectedProgramHash").SetValue(smoke, (string)config["expectedProgramHash"]);
        new GameObject("Camera").AddComponent<Camera>().transform.position = new Vector3(0, 0, -10);
        var scene = "Assets/AibtFirstAgentDemo/AgentPlayerSmoke.unity";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scene);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scene }, target = BuildTarget.StandaloneWindows64,
            locationPathName = Path.Combine(root, "AgentPlayer/Agent.exe"), options = BuildOptions.Development });
        var summary = new JObject { ["result"] = result.summary.result.ToString(), ["errors"] = result.summary.totalErrors,
            ["warnings"] = result.summary.totalWarnings, ["backend"] = "Windows IL2CPP Development" };
        File.WriteAllText(Path.Combine(root, "agent-player-build.json"), summary.ToString());
        if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(summary.ToString());
    }
}
