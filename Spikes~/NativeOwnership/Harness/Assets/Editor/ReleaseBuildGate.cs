using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.NativeOwnership.Spike.Editor
{
    public static class ReleaseBuildGate
    {
        public static void Build()
        {
            var outputRoot = Environment.GetEnvironmentVariable("AIBT_NATIVE_OWNERSHIP_PLAYER_OUTPUT");
            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                throw new InvalidOperationException("AIBT_NATIVE_OWNERSHIP_PLAYER_OUTPUT is required.");
            }

            Directory.CreateDirectory(outputRoot);
            const string scenePath = "Assets/NativeOwnershipReleaseProbe.unity";
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, scenePath);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    locationPathName = Path.Combine(outputRoot, "NativeOwnershipProbe.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.CleanBuildCache
                };
                var report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException($"Release player build failed: {report.summary.result}, errors={report.summary.totalErrors}");
                }

                if ((report.summary.options & BuildOptions.Development) != 0)
                {
                    throw new InvalidOperationException("Release gate unexpectedly produced a development build.");
                }

                Debug.Log($"AIBT_NATIVE_OWNERSHIP_RELEASE_BUILD_OK bytes={report.summary.totalSize}");
            }
            finally
            {
                AssetDatabase.DeleteAsset(scenePath);
            }
        }
    }
}
