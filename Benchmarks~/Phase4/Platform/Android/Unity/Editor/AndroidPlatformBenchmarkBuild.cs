using System;
using System.IO;
using Unity.Burst;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Benchmarks.Phase4.Platform.Android.Editor
{
    /// <summary>
    /// Builds a real, non-development, IL2CPP, Burst-enabled, ARM64-only Android APK containing
    /// <see cref="AIBT.Benchmarks.Phase4.Platform.Android.AndroidPlatformSchedulingProbe"/>.
    /// Mirrors the Windows/Web build scripts' pattern.
    /// </summary>
    public static class AndroidPlatformBenchmarkBuild
    {
        private const string OutputArgument = "-aibtP4008PlayerOutput";
        private const string EvidenceArgument = "-aibtP4008BuildEvidence";
        private const string ScenePath = "Assets/AIBTP4008AndroidPlatformBenchmark.unity";
        private const string SuccessMarker = "AIBT_P4_008_ANDROID_BUILD_OK|";

        public static void Build()
        {
            var output = RequiredArgument(OutputArgument);
            var evidencePath = RequiredArgument(EvidenceArgument);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath));

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Android build support is not installed for this Unity editor.");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Unity could not activate Android.");

            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.applicationIdentifier = "com.aibt.p4008platformbenchmark";
            BurstCompiler.Options.EnableBurstCompilation = true;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);

            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.CleanBuildCache,
                });

                var summary = report.summary;
                var evidence = new BuildEvidence
                {
                    schema = "aibt-p4-008-android-build-v1",
                    unityVersion = Application.unityVersion,
                    target = summary.platform.ToString(),
                    architecture = PlayerSettings.Android.targetArchitectures.ToString(),
                    result = summary.result.ToString(),
                    totalErrors = summary.totalErrors,
                    totalWarnings = summary.totalWarnings,
                    outputBytes = checked((long)summary.totalSize),
                    scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android).ToString(),
                    burstEnabled = BurstCompiler.Options.EnableBurstCompilation,
                    developmentBuild = (summary.options & BuildOptions.Development) != 0,
                    applicationIdentifier = PlayerSettings.applicationIdentifier,
                    output = output,
                };
                File.WriteAllText(evidencePath, JsonUtility.ToJson(evidence, true));

                if (summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Android build failed: " + summary.result + ", errors=" + summary.totalErrors + ".");
                if (evidence.scriptingBackend != ScriptingImplementation.IL2CPP.ToString())
                    throw new InvalidOperationException("The Android build did not use IL2CPP.");
                if (!evidence.burstEnabled || evidence.developmentBuild)
                    throw new InvalidOperationException("The build did not retain the required Burst-enabled, non-development settings.");
                if (evidence.architecture != AndroidArchitecture.ARM64.ToString())
                    throw new InvalidOperationException("The Android build did not target ARM64 only.");

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
            public string applicationIdentifier;
            public string output;
        }
    }
}
