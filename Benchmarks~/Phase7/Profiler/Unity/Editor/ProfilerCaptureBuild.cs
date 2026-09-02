using System;
using System.IO;
using Unity.Burst;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Benchmarks.Phase7.Profiler.Editor
{
    /// <summary>
    /// P7-003: builds a real, Development, Burst-enabled Windows x64 Standalone Player with
    /// <see cref="BuildOptions.ConnectWithProfiler"/> set, containing <c>ProfilerCaptureProbe</c>.
    /// Development is required here (unlike P4-008's own non-development Player) because a Unity
    /// Profiler capture genuinely cannot attach to a build with no profiler transport compiled in
    /// -- see this card's own evidence README for the disclosed reasoning. Mirrors
    /// `Benchmarks~/Phase4/Platform/Windows/Unity/Editor/WindowsPlatformBenchmarkBuild.cs`'s
    /// build-settings pattern otherwise.
    /// </summary>
    public static class ProfilerCaptureBuild
    {
        private const string OutputArgument = "-aibtP7003PlayerOutput";
        private const string EvidenceArgument = "-aibtP7003BuildEvidence";
        private const string ScenePath = "Assets/AIBTP7003ProfilerCapture.unity";
        private const string SuccessMarker = "AIBT_P7_003_PROFILER_BUILD_OK|";

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
                    options = BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ConnectWithProfiler,
                });

                var summary = report.summary;
                var evidence = new BuildEvidence
                {
                    schema = "aibt-p7-003-profiler-windows-build-v1",
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
                    connectWithProfiler = (summary.options & BuildOptions.ConnectWithProfiler) != 0,
                    output = output,
                };
                File.WriteAllText(evidencePath, JsonUtility.ToJson(evidence, true));

                if (summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Windows Player build failed: " + summary.result + ", errors=" + summary.totalErrors + ".");
                if (evidence.scriptingBackend != ScriptingImplementation.IL2CPP.ToString())
                    throw new InvalidOperationException("The Windows Player did not use IL2CPP.");
                if (!evidence.burstEnabled || !evidence.developmentBuild || !evidence.connectWithProfiler)
                    throw new InvalidOperationException("The build did not retain the required Burst-enabled, Development, ConnectWithProfiler settings.");

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
            public bool connectWithProfiler;
            public string output;
        }
    }
}
