using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Unity.Burst;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBT.Benchmarks.BuildSize
{
    public static class BuildSizeBuild
    {
        public static void Build()
        {
            var output = Path.GetFullPath(BuildSizeProbe.Argument("-sizeOutput"));
            Directory.CreateDirectory(output);
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Windows target unavailable");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Low);
            PlayerSettings.productName = "AIBTSizeProbe";
            PlayerSettings.companyName = "AIBTVerification";
            BurstCompiler.Options.EnableBurstCompilation = true;
            const string scenePath = "Assets/SizeProbe.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, scenePath);
            Directory.CreateDirectory("Assets/Resources/Trees");

            foreach (var count in new[] { 1, 100 })
            {
                // The same project, scene, sources and catalog are used in both builds.
                for (var index = 0; index < count; index++)
                {
                    var id = index.ToString("D4");
                    var json = "{\"format\":\"aibt.tree\",\"formatVersion\":1,\"treeId\":\"tree.size-" + id
                        + "\",\"name\":\"Size " + id + "\",\"root\":\"root\",\"nodes\":{\"root\":{\"type\":\"aibt.core.memory-sequence\",\"typeVersion\":1,\"children\":[\"wait\"]},\"wait\":{\"type\":\"aibt.stdlib.wait\",\"typeVersion\":1,\"parameters\":{\"ticks\":2}}}}";
                    File.WriteAllText("Assets/Resources/Trees/tree-" + id + ".aibt.json", json, new UTF8Encoding(false));
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var assets = Directory.GetFiles("Assets/Resources/Trees", "*.json").OrderBy(p => p)
                    .Select(AssetDatabase.LoadAssetAtPath<TextAsset>).ToArray();
                if (assets.Length != count) throw new InvalidOperationException("Unexpected input population");
                var inputHash = BuildSizeProbe.Validate(assets);
                var variant = Path.Combine(output, count.ToString());
                Directory.CreateDirectory(variant);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath },
                    target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone,
                    locationPathName = Path.Combine(variant, "AIBTSizeProbe.exe"), options = BuildOptions.DetailedBuildReport });
                if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Build failed: " + report.summary.result);
                var files = Directory.GetFiles(variant, "*", SearchOption.AllDirectories)
                    .Where(p => !p.Contains("DoNotShip") && !p.Contains("DontShip"))
                    .OrderBy(p => p, StringComparer.Ordinal).Select(p => new FileEntry {
                        path = Path.GetRelativePath(variant, p).Replace('\\', '/'), bytes = new FileInfo(p).Length, sha256 = Hash(p) }).ToArray();
                var packed = report.packedAssets.SelectMany(a => a.contents).Where(c => c.sourceAssetPath.StartsWith("Assets/Resources/Trees/", StringComparison.Ordinal))
                    .Select(c => new PackedEntry { path = c.sourceAssetPath, bytes = (long)c.packedSize }).ToArray();
                var result = new Evidence { trees = count, unityVersion = Application.unityVersion,
                    backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString(),
                    target = report.summary.platform.ToString(), development = (report.summary.options & BuildOptions.Development) != 0,
                    burst = BurstCompiler.Options.EnableBurstCompilation, contentHash = inputHash,
                    sourceBytes = assets.Sum(a => (long)a.bytes.Length), summaryBytes = (long)report.summary.totalSize,
                    shippedBytes = files.Sum(f => f.bytes), packedTrees = packed, files = files };
                File.WriteAllText(Path.Combine(output, count + "-build.json"), JsonUtility.ToJson(result, true));
                Debug.Log("AIBT_BUILD_SIZE_BUILD_OK|" + count);
            }
        }

        private static string Hash(string path)
        {
            using var algorithm = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        [Serializable] private sealed class FileEntry { public string path, sha256; public long bytes; }
        [Serializable] private sealed class PackedEntry { public string path; public long bytes; }
        [Serializable] private sealed class Evidence
        {
            public int trees;
            public string unityVersion, backend, target, contentHash;
            public bool development, burst;
            public long sourceBytes, summaryBytes, shippedBytes;
            public FileEntry[] files;
            public PackedEntry[] packedTrees;
        }
    }
}
