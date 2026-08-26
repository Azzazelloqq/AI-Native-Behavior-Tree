using System;
using System.IO;
using Unity.Burst;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Benchmarks.Phase4.Platform.Web.Editor
{
    /// <summary>
    /// Builds a real single-thread Unity Web (WebGL) Player containing
    /// <see cref="AIBT.Benchmarks.Phase4.Platform.Web.WebPlatformSchedulingProbe"/>. Mirrors
    /// `Benchmarks~/Phase4/Platform/Windows/Unity/Editor/WindowsPlatformBenchmarkBuild.cs`'s
    /// pattern, targeting `WebGL` instead of `StandaloneWindows64` and without an IL2CPP setting
    /// (WebGL always builds through IL2CPP -> Emscripten; there is no scripting-backend choice).
    /// </summary>
    public static class WebPlatformBenchmarkBuild
    {
        private const string OutputArgument = "-aibtP4008PlayerOutput";
        private const string EvidenceArgument = "-aibtP4008BuildEvidence";
        private const string ScenePath = "Assets/AIBTP4008WebPlatformBenchmark.unity";
        private const string SuccessMarker = "AIBT_P4_008_WEB_BUILD_OK|";

        public static void Build()
        {
            var output = RequiredArgument(OutputArgument);
            var evidencePath = RequiredArgument(EvidenceArgument);
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath));

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                throw new InvalidOperationException("WebGL build support is not installed for this Unity editor.");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                throw new InvalidOperationException("Unity could not activate WebGL.");

            BurstCompiler.Options.EnableBurstCompilation = true;
            PlayerSettings.WebGL.threadsSupport = false;
            // The static file server used to serve this build for local testing does not set the
            // "Content-Encoding: gzip" header real hosting (e.g. a CDN) would, so the browser
            // cannot auto-decompress Unity's default gzip-compressed build artifacts. Unity's own
            // decompression fallback (client-side JS decompression) is the documented fix for
            // hosts that cannot set that header.
            PlayerSettings.WebGL.decompressionFallback = true;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);

            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = BuildOptions.CleanBuildCache,
                });

                var summary = report.summary;
                var evidence = new BuildEvidence
                {
                    schema = "aibt-p4-008-web-build-v1",
                    unityVersion = Application.unityVersion,
                    target = summary.platform.ToString(),
                    result = summary.result.ToString(),
                    totalErrors = summary.totalErrors,
                    totalWarnings = summary.totalWarnings,
                    outputBytes = checked((long)summary.totalSize),
                    burstEnabled = BurstCompiler.Options.EnableBurstCompilation,
                    developmentBuild = (summary.options & BuildOptions.Development) != 0,
                    threadsSupport = PlayerSettings.WebGL.threadsSupport,
                    output = output,
                };
                File.WriteAllText(evidencePath, JsonUtility.ToJson(evidence, true));

                if (summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("WebGL Player build failed: " + summary.result + ", errors=" + summary.totalErrors + ".");
                if (evidence.threadsSupport)
                    throw new InvalidOperationException("The WebGL build enabled multi-threading; this card claims single-thread Web only.");

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
            public bool burstEnabled;
            public bool developmentBuild;
            public bool threadsSupport;
            public string output;
        }
    }
}
