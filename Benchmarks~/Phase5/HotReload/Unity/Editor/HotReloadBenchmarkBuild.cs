using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Benchmarks.Phase5.HotReload.Editor
{
    /// <summary>
    /// Builds a real, non-development Windows x64 Standalone Player containing
    /// <see cref="HotReloadBenchmarkRunner"/>'s <c>RunFromPlayer</c> entry point, so
    /// <c>P5-009</c>'s Editor-batchmode numbers can be checked against a real Player,
    /// mirroring <c>P4-008</c>'s general build-driver pattern. No Burst/IL2CPP claim is made
    /// here -- this benchmark exercises no Burst-compiled code, so the default scripting
    /// backend is used, matching <c>Documentation~/benchmarks.md</c>'s "don't replicate checks
    /// that verify nothing relevant to this card's claim" guidance.
    /// </summary>
    public static class HotReloadBenchmarkBuild
    {
        private const string OutputArgument = "-aibtP5009PlayerOutput";
        private const string EvidenceArgument = "-aibtP5009BuildEvidence";
        private const string ScenePath = "Assets/AIBTP5009HotReloadBenchmark.unity";
        private const string SuccessMarker = "AIBT_P5_009_WINDOWS_BUILD_OK|";

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

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
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
                    schema = "aibt-p5-009-windows-build-v1",
                    unityVersion = Application.unityVersion,
                    target = summary.platform.ToString(),
                    result = summary.result.ToString(),
                    totalErrors = summary.totalErrors,
                    totalWarnings = summary.totalWarnings,
                    outputBytes = checked((long)summary.totalSize),
                    scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString(),
                    developmentBuild = (summary.options & BuildOptions.Development) != 0,
                    output = output,
                };
                File.WriteAllText(evidencePath, JsonUtility.ToJson(evidence, true));

                if (summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Windows Player build failed: " + summary.result + ", errors=" + summary.totalErrors + ".");
                if (evidence.developmentBuild)
                    throw new InvalidOperationException("The build did not retain the required non-development setting.");

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
            public string result;
            public int totalErrors;
            public int totalWarnings;
            public long outputBytes;
            public string scriptingBackend;
            public bool developmentBuild;
            public string output;
        }
    }
}
