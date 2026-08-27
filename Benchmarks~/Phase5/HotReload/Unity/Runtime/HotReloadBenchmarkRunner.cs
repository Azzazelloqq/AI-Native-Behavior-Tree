using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using AIBT;
using AIBT.Authoring;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace AIBT.Benchmarks.Phase5.HotReload
{
    /// <summary>
    /// Measures the reference-executor hot-reload mechanisms (<c>P5-004</c>/<c>P5-005</c>/<c>P5-006</c>)
    /// via the public <see cref="HotReloadPreviewDriver"/> facade only -- no internals access, so
    /// this runs unmodified in Editor batchmode or copied into an isolated Player project, per
    /// <c>Documentation~/benchmarks.md</c>'s existing harness pattern. Measurement only: no
    /// default, threshold, or "acceptable reload cost" claim is drawn here.
    /// </summary>
    internal static class HotReloadBenchmarkRunner
    {
        private const int WarmupSamples = 5;
        private const int MeasuredSamples = 15;

        internal readonly struct TreeShape
        {
            internal TreeShape(string name, int nodeCount, Func<TreeDocument> build)
            {
                Name = name;
                NodeCount = nodeCount;
                Build = build;
            }

            internal string Name { get; }
            internal int NodeCount { get; }
            internal Func<TreeDocument> Build { get; }
        }

        internal static IReadOnlyList<TreeShape> Shapes { get; } = new[]
        {
            new TreeShape("single-leaf", 1, () => SingleLeafTree("aibt.test.success")),
            new TreeShape("shallow-sequence-5-leaves", 6, () => SequenceTree(5)),
            new TreeShape("deep-sequence-63-nodes", 63, () => DeepSequenceTree(depth: 6)),
        };

        private const string SuccessMarker = "AIBT_P5_009_HOTRELOAD_BENCHMARK_OK|";
        private const string FailureMarker = "AIBT_P5_009_HOTRELOAD_BENCHMARK_FAIL|";

#if UNITY_EDITOR
        /// <summary>Editor-batchmode entry point (<c>-executeMethod</c>).</summary>
        public static void RunFromEditor()
        {
            var exitCode = 0;
            try
            {
                RunAndWriteResults();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError(FailureMarker + exception);
                exitCode = 1;
            }

            EditorApplication.Exit(exitCode);
        }
#endif

        /// <summary>Real-Player entry point -- fires on load in a built Standalone Player, no Editor dependency.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RunFromPlayer()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-aibtRunHotReloadBenchmark") < 0) return; // opt-in, never runs in an ordinary Player launch

            try
            {
                RunAndWriteResults();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError(FailureMarker + exception);
            }

            Application.Quit(0);
        }

        private static void RunAndWriteResults()
        {
            var args = Environment.GetCommandLineArgs();
            var outputIndex = Array.IndexOf(args, "-aibtBenchmarkOutput");
            var outputPath = outputIndex >= 0 && outputIndex + 1 < args.Length
                ? args[outputIndex + 1]
                : Path.Combine(Application.persistentDataPath, "hot-reload-benchmark.json");

            var results = new List<string>();
            foreach (var shape in Shapes)
            {
                results.Add(MeasureShape(shape));
            }

            var json = "{\n"
                + "  \"schema\": \"aibt-p5-009-hot-reload-benchmark-v1\",\n"
                + "  \"environment\": " + EnvironmentJson() + ",\n"
                + "  \"configuration\": { \"warmupSamples\": " + WarmupSamples + ", \"measuredSamples\": " + MeasuredSamples + " },\n"
                + "  \"scenarios\": [\n" + string.Join(",\n", results) + "\n  ]\n"
                + "}\n";

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
            UnityEngine.Debug.Log(SuccessMarker + "path=" + outputPath + "|scenarios=" + Shapes.Count);
        }

        private static string MeasureShape(TreeShape shape)
        {
            var before = shape.Build();
            var afterCompatible = shape.Build(); // structurally identical -- isolates pure reload-mechanism cost with zero real change
            var afterIncompatible = WithFirstLeafTypeChanged(shape.Build());

            var compileMicros = MeasureMicros(() =>
            {
                HotReloadPreviewDriver.TryCreate(shape.Build(), "trees/bench.aibt.json", out _, out _);
            });

            var fullRestartMicros = MeasureMicros(() =>
            {
                HotReloadPreviewDriver.TryCreate(before, "trees/before.aibt.json", out var driver, out _);
                driver.TryReload(afterCompatible, "trees/after.aibt.json", out _, out _);
            });

            HotReloadPreviewDriver.TryCreate(before, "trees/before.aibt.json", out var idleDriver, out _);
            var migrationMicros = MeasureMicros(() =>
            {
                idleDriver.TryReload(afterCompatible, "trees/after.aibt.json", out _, out _);
            });

            HotReloadPreviewDriver.TryCreate(before, "trees/before.aibt.json", out var subtreeDriver, out _);
            var subtreeRestartMicros = MeasureMicros(() =>
            {
                subtreeDriver.TryReload(afterIncompatible, "trees/after-incompatible.aibt.json", out _, out _);
            });

            return "    {\n"
                + "      \"name\": \"" + shape.Name + "\",\n"
                + "      \"nodeCount\": " + shape.NodeCount + ",\n"
                + "      \"compileOnlyMicroseconds\": " + FormatSamples(compileMicros) + ",\n"
                + "      \"fullRestartTotalMicroseconds\": " + FormatSamples(fullRestartMicros) + ",\n"
                + "      \"compatibleMigrationTotalMicroseconds\": " + FormatSamples(migrationMicros) + ",\n"
                + "      \"subtreeRestartTotalMicroseconds\": " + FormatSamples(subtreeRestartMicros) + "\n"
                + "    }";
        }

        private static double[] MeasureMicros(Action action)
        {
            for (var i = 0; i < WarmupSamples; i++) action();

            var samples = new double[MeasuredSamples];
            var stopwatch = new Stopwatch();
            for (var i = 0; i < MeasuredSamples; i++)
            {
                stopwatch.Restart();
                action();
                stopwatch.Stop();
                samples[i] = stopwatch.Elapsed.TotalMilliseconds * 1000.0;
            }

            return samples;
        }

        private static string FormatSamples(double[] samples)
        {
            var sorted = (double[])samples.Clone();
            Array.Sort(sorted);
            var median = sorted[sorted.Length / 2];
            var min = sorted[0];
            var max = sorted[sorted.Length - 1];
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return "{ \"medianMicroseconds\": " + median.ToString("F3", culture) + ", \"minMicroseconds\": " + min.ToString("F3", culture)
                + ", \"maxMicroseconds\": " + max.ToString("F3", culture) + " }";
        }

        private static string EnvironmentJson()
        {
            return "{ \"unityVersion\": \"" + Application.unityVersion
                + "\", \"applicationPlatform\": \"" + Application.platform
                + "\", \"isEditor\": " + Application.isEditor.ToString().ToLowerInvariant()
                + ", \"operatingSystem\": \"" + SystemInfo.operatingSystem.Replace("\"", "'")
                + "\", \"processorType\": \"" + SystemInfo.processorType.Replace("\"", "'")
                + "\", \"processorCount\": " + SystemInfo.processorCount
                + ", \"systemMemoryMB\": " + SystemInfo.systemMemorySize + " }";
        }

        // --- tree shapes ---

        private static TreeDocument SingleLeafTree(string leafTypeId)
        {
            var leaf = Node("leaf", leafTypeId);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.hot-reload-bench"), "Bench", leaf.Id, new[] { leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument SequenceTree(int leafCount)
        {
            var childIds = Enumerable.Range(0, leafCount).Select(i => "leaf" + i).ToArray();
            var nodes = new List<NodeDocument> { Node("root", BuiltInNodeManifests.MemorySequenceTypeId, childIds) };
            nodes.AddRange(childIds.Select(id => Node(id, "aibt.test.success")));
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.hot-reload-bench"), "Bench", new NodeId("root"), nodes,
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        // A chain of nested single-child Sequences `depth` levels deep, each wrapping the next,
        // with a leaf at the bottom -- mirrors P4-002's "deep-sequence-selector-traversal" shape
        // (roughly 2^depth - 1 nodes) for a comparably-sized large-tree data point.
        private static TreeDocument DeepSequenceTree(int depth)
        {
            var nodes = new List<NodeDocument>();
            string BuildLevel(int level, int branch)
            {
                var id = "n" + level + "_" + branch;
                if (level >= depth)
                {
                    nodes.Add(Node(id, "aibt.test.success"));
                    return id;
                }

                var leftChild = BuildLevel(level + 1, branch * 2);
                var rightChild = BuildLevel(level + 1, branch * 2 + 1);
                nodes.Add(Node(id, BuiltInNodeManifests.MemorySequenceTypeId, leftChild, rightChild));
                return id;
            }

            var rootId = BuildLevel(0, 0);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.hot-reload-bench"), "Bench", new NodeId(rootId), nodes,
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument WithFirstLeafTypeChanged(TreeDocument document)
        {
            var index = -1;
            for (var i = 0; i < document.Nodes.Count; i++)
            {
                if (document.Nodes[i].TypeId == "aibt.test.success") { index = i; break; }
            }

            document.ReplaceNodeAt(index, document.Nodes[index].WithType("aibt.test.failure", 1));
            return document;
        }

        private static NodeDocument Node(string id, string typeId, params string[] children) =>
            new NodeDocument(
                new NodeId(id), typeId, 1, children.Select(c => new NodeId(c)),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
    }
}
