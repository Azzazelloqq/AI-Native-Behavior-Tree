using System;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Perf.Editor
{
    /// <summary>Prepares the benchmark scene and native runtime assets, and builds the benchmark Player.</summary>
    public static class PerfBuild
    {
        private const string Root = "Assets/AibtPerf";
        private const string TreeFolder = Root + "/Trees";
        private const string RuntimeFolder = Root + "/Prepared";
        private const string ScenePath = Root + "/AibtPerf.unity";

        private static readonly string[] NativeTrees =
        {
            "shared-shape15", "shared-loop16", "shared-reactive2", "shared-reactive16", "shared-reactive64",
            "native-shape15", "native-move1", "native-wait1",
            // P10-010: native only (benchmark input and command nodes, PerfIoShard).
            "io-reactive16", "io-loop16",
            // P10-011: native only (benchmark external-action node, PerfIoShard).
            "act-reactive16", "act-loop16",
        };

        private static readonly string[] ManagedTrees =
        {
            "shared-shape15", "shared-loop16", "shared-reactive2", "shared-reactive16", "shared-reactive64",
            "unity-shape15-both", "unity-shape15-none",
        };

        [MenuItem("AIBT/Perf/Prepare")]
        public static void Prepare()
        {
            AssetDatabase.Refresh();
            if (!AssetDatabase.IsValidFolder(RuntimeFolder)) AssetDatabase.CreateFolder(Root, "Prepared");
            foreach (var name in NativeTrees) Bake(name);
            AssetDatabase.SaveAssets();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;
            var host = new GameObject("Perf");
            host.AddComponent<PerfFrameBegin>();
            var feeder = host.AddComponent<PerfFeeder>();
            var runner = host.AddComponent<PerfRunner>();
            host.AddComponent<PerfFrameEnd>().runner = runner;
            runner.feeder = feeder;
            foreach (var name in ManagedTrees)
            {
                var text = AssetDatabase.LoadAssetAtPath<TextAsset>(TreeFolder + "/" + name + ".aibt.json");
                if (text == null) throw new InvalidOperationException("Missing tree " + name);
                runner.managedTrees.Add(new NamedText { name = name, json = text });
            }
            foreach (var name in NativeTrees)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(RuntimeFolder + "/" + name + ".runtime.asset");
                if (asset == null) throw new InvalidOperationException("Missing prepared tree " + name);
                runner.nativeTrees.Add(new NamedNative { name = name, asset = asset });
            }
            runner.guardTree = FindSample("guard.aibt");
            runner.wanderTree = FindSample("wander.aibt");
            runner.scoutTree = FindSample("scout.aibt");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("AIBT_PERF_PREPARED|" + ScenePath);
        }

        private static BehaviorTreeSource FindSample(string file)
        {
            var path = AssetDatabase.GetAllAssetPaths().Single(p => p.StartsWith("Assets/Samples/", StringComparison.Ordinal) && p.EndsWith("/" + file, StringComparison.Ordinal));
            var source = AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(path);
            if (source == null) throw new InvalidOperationException("Not a BehaviorTreeSource: " + path);
            return source;
        }

        private static void Bake(string name)
        {
            var source = TreeFolder + "/" + name + ".aibt.json";
            var parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(source), source);
            if (!parsed.Success) throw new InvalidOperationException(name + ": " + string.Join("\n", parsed.Diagnostics.Select(d => d.Code + " " + d.Message)));
            var prepared = ProductionTreeAssetBaker.Create(parsed.Document, source, typeof(PerfCatalog), out var diagnostics);
            if (prepared == null) throw new InvalidOperationException(name + ": " + string.Join("\n", diagnostics.Select(d => d.Code + " " + d.Message)));
            var path = RuntimeFolder + "/" + name + ".runtime.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(path);
            if (existing == null) { AssetDatabase.CreateAsset(prepared, path); return; }
            EditorUtility.CopySerialized(prepared, existing);
            existing.name = name + ".runtime";
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(prepared);
        }

        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name, string fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
            var output = Arg("-perfPlayerOutput", "Build/Perf/AibtPerf.exe");
            var backend = Arg("-perfBackend", "il2cpp") == "mono" ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP;
            var stripping = (ManagedStrippingLevel)Enum.Parse(typeof(ManagedStrippingLevel), Arg("-perfStripping", "High"));
            var development = Arg("-perfDevelopment", "false") == "true";

            var target = NamedBuildTarget.Standalone;
            PlayerSettings.SetScriptingBackend(target, backend);
            PlayerSettings.SetManagedStrippingLevel(target, stripping);
            PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 640;
            PlayerSettings.defaultScreenHeight = 360;
            PlayerSettings.runInBackground = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.resizableWindow = true;

            // P10-002: extra defines, e.g. AIBT_SCHEDULER_FRAME_PROFILE for the per-stage frame profile.
            var defines = Arg("-perfDefines", string.Empty);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                extraScriptingDefines = string.IsNullOrEmpty(defines) ? null : defines.Split(';'),
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = !development ? BuildOptions.None
                    : Arg("-perfDeep", "false") == "true" ? BuildOptions.Development | BuildOptions.EnableDeepProfilingSupport
                    : BuildOptions.Development,
            });
            var summary = report.summary;
            var line = "AIBT_PERF_BUILD|result=" + summary.result + "|backend=" + backend + "|stripping=" + stripping
                + "|development=" + development + "|defines=" + defines + "|errors=" + summary.totalErrors + "|warnings=" + summary.totalWarnings
                + "|size=" + summary.totalSize + "|seconds=" + (int)summary.totalTime.TotalSeconds;
            Debug.Log(line);
            File.WriteAllText(Path.ChangeExtension(output, ".build.txt"), line + "\n");
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
