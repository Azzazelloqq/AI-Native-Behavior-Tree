using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Spikes.WebBackend.Editor
{
    public static class WebBackendBuild
    {
        public static void Build()
        {
            var output = GetArgument("-aibtWebOutput");
            if (string.IsNullOrEmpty(output))
                throw new ArgumentException("-aibtWebOutput is required.");

            Directory.CreateDirectory("Assets/Generated");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var runner = new GameObject("AIBT Web Backend Spike");
            runner.AddComponent<AIBT.Spikes.WebBackend.WebBackendPlayer>();
            const string scenePath = "Assets/Generated/WebBackend.unity";
            EditorSceneManager.SaveScene(scene, scenePath);

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.dataCaching = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.productName = "AIBT Web Backend Spike";
            PlayerSettings.companyName = "AIBT";

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = Path.GetFullPath(output),
                target = BuildTarget.WebGL,
                options = BuildOptions.CleanBuildCache,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("WebGL build failed: " + report.summary.result);

            File.WriteAllText(
                Path.Combine(Path.GetFullPath(output), "aibt-build-summary.json"),
                "{\"result\":\"Succeeded\",\"totalSizeBytes\":" + report.summary.totalSize
                + ",\"totalTimeSeconds\":" + report.summary.totalTime.TotalSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + "}");
        }

        private static string GetArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < arguments.Length; index++)
                if (arguments[index] == name) return arguments[index + 1];
            return null;
        }
    }
}
