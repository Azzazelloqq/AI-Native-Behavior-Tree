using System;
using System.IO;
using AIBT.Benchmarks.Phase7.SwarmArena;
using Unity.Burst;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Benchmarks.Phase7.SwarmArena.Editor
{
    /// <summary>
    /// Builds a real, non-development, IL2CPP, Burst-enabled Windows x64 Standalone Player containing
    /// <see cref="SwarmArenaBenchmarkProbe"/>, wired against the copied-in-unchanged
    /// <c>swarm-arena.aibt.json</c> tree. Mirrors
    /// <c>Benchmarks~/Phase4/Platform/Windows/Unity/Editor/WindowsPlatformBenchmarkBuild.cs</c>'s
    /// build-settings and evidence pattern.
    /// </summary>
    public static class SwarmArenaBenchmarkBuild
    {
        private const string OutputArgument = "-aibtP7035PlayerOutput";
        private const string EvidenceArgument = "-aibtP7035BuildEvidence";
        private const string ScenePath = "Assets/AIBTP7035SwarmArenaBenchmark.unity";
        private const string SuccessMarker = "AIBT_P7_035_WINDOWS_BUILD_OK|";
        private const string TreeAssetPath = "Assets/AIBT/Samples/SwarmArena/Trees/swarm-arena.aibt.json";

        public static void Build()
        {
            var output = RequiredArgument(OutputArgument);
            var evidencePath = RequiredArgument(EvidenceArgument);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath));

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Windows Standalone build support is not installed for this Unity editor.");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Unity could not activate StandaloneWindows64.");

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            BurstCompiler.Options.EnableBurstCompilation = true;

            var tree = AssetDatabase.LoadAssetAtPath<TextAsset>(TreeAssetPath);
            if (tree == null) throw new InvalidOperationException("Swarm Arena tree asset not found: " + TreeAssetPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var beacon = new GameObject("Beacon").transform;
            var danger = new GameObject("Danger").transform;
            var probeObject = new GameObject("Probe");
            var probe = probeObject.AddComponent<SwarmArenaBenchmarkProbe>();
            probe.Tree = tree;
            probe.Beacon = beacon;
            probe.Danger = danger;
            EditorSceneManager.SaveScene(scene, ScenePath);

            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.CleanBuildCache,
                });

                var summary = report.summary;
                var evidence = new BuildEvidence
                {
                    schema = "aibt-p7-035-swarm-arena-windows-build-v1",
                    unityVersion = Application.unityVersion,
                    target = summary.platform.ToString(),
                    architecture = "x86_64",
                    result = summary.result.ToString(),
                    totalErrors = summary.totalErrors,
                    totalWarnings = summary.totalWarnings,
                    outputBytes = checked((long)summary.totalSize),
                    scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString(),
                    burstEnabled = BurstCompiler.Options.EnableBurstCompilation,
                    developmentBuild = (summary.options & BuildOptions.Development) != 0,
                    output = output,
                };
                File.WriteAllText(evidencePath, JsonUtility.ToJson(evidence, true));

                if (summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Swarm Arena benchmark Player build failed: " + summary.result + ", errors=" + summary.totalErrors + ".");
                if (evidence.scriptingBackend != ScriptingImplementation.IL2CPP.ToString())
                    throw new InvalidOperationException("The Player did not use IL2CPP.");
                if (!evidence.burstEnabled || evidence.developmentBuild)
                    throw new InvalidOperationException("The build did not retain the required Burst-enabled, non-development settings.");

                Debug.Log(SuccessMarker + JsonUtility.ToJson(evidence));
            }
            finally
            {
                AssetDatabase.DeleteAsset(ScenePath);
            }
        }

        private static string RequiredArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                    return Path.GetFullPath(arguments[index + 1]);
            throw new ArgumentException("Missing required command argument " + name + ".");
        }

        [Serializable]
        private sealed class BuildEvidence
        {
            public string schema;
            public string unityVersion;
            public string target;
            public string architecture;
            public string result;
            public int totalErrors;
            public int totalWarnings;
            public long outputBytes;
            public string scriptingBackend;
            public bool burstEnabled;
            public bool developmentBuild;
            public string output;
        }
    }
}
