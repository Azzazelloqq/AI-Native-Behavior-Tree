using System;
using System.IO;
using AIBT.Tests.Runtime.NativeExecution.Dispatch;
using Unity.Burst;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Benchmarks.Phase2.Dispatch.Player.Editor
{
    public static class GeneratedDispatchPlayerAotBuild
    {
        private const string OutputArgument = "-aibtP2PlayerOutput";
        private const string EvidenceArgument = "-aibtP2BuildEvidence";
        private const string ScenePath = "Assets/AIBTP2GeneratedDispatchPlayerAot.unity";
        private const string SuccessMarker = "AIBT_P2_012_PLAYER_AOT_BUILD_OK|";

        public static void Build()
        {
            var output = RequiredArgument(OutputArgument);
            var evidencePath = RequiredArgument(EvidenceArgument);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath));

            if (!BuildPipeline.IsBuildTargetSupported(
                BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64))
            {
                throw new InvalidOperationException(
                    "Windows Standalone build support is not installed for this Unity editor.");
            }

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64))
            {
                throw new InvalidOperationException(
                    "Unity could not activate StandaloneWindows64.");
            }

            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP);
            BurstCompiler.Options.EnableBurstCompilation = true;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);

            BuildReport report = null;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
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
                    schema = "aibt-p2-012-player-aot-build-v1",
                    unityVersion = Application.unityVersion,
                    target = summary.platform.ToString(),
                    architecture = "x86_64",
                    result = summary.result.ToString(),
                    totalErrors = summary.totalErrors,
                    totalWarnings = summary.totalWarnings,
                    outputBytes = checked((long)summary.totalSize),
                    scriptingBackend = PlayerSettings.GetScriptingBackend(
                        NamedBuildTarget.Standalone).ToString(),
                    burstEnabled = BurstCompiler.Options.EnableBurstCompilation,
                    developmentBuild = (summary.options & BuildOptions.Development) != 0,
                    catalogUsable = GeneratedDispatchCanaryCatalog.IsUsable,
                    generatedEntryPoint = "GeneratedDispatchCanaryCatalog.ExecuteImmediate",
                    output = output,
                };
                File.WriteAllText(evidencePath, JsonUtility.ToJson(evidence, true));

                if (summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Windows Player build failed: " + summary.result
                        + ", errors=" + summary.totalErrors + ".");
                }

                if (evidence.scriptingBackend != ScriptingImplementation.IL2CPP.ToString())
                {
                    throw new InvalidOperationException(
                        "The Windows Player did not use IL2CPP.");
                }

                if (!evidence.burstEnabled || evidence.developmentBuild
                    || !evidence.catalogUsable)
                {
                    throw new InvalidOperationException(
                        "The build did not retain the required Burst/release/generated-catalog settings.");
                }

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
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                {
                    return Path.GetFullPath(arguments[index + 1]);
                }
            }

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
            public bool catalogUsable;
            public string generatedEntryPoint;
            public string output;
        }
    }
}
