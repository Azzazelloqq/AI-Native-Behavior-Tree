using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace AIBT.Spikes.EditorGraphFramework
{
    internal static class SpikeRunner
    {
        private const int NodeCount = 240;

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var outputIndex = Array.IndexOf(args, "-aibtSpikeOutput");
            var outputPath = args[outputIndex + 1];

            var findings = new List<string>();
            var success = true;

            try
            {
                RunFindings(findings);
            }
            catch (Exception exception)
            {
                success = false;
                findings.Add("FATAL|" + exception.GetType().FullName + "|" + exception.Message);
            }

            var text = new StringBuilder();
            text.AppendLine("{");
            text.AppendLine("  \"schema\": \"aibt-p3-001-spike-v1\",");
            text.AppendLine("  \"passed\": " + (success ? "true" : "false") + ",");
            text.AppendLine("  \"findings\": [");
            for (var i = 0; i < findings.Count; i++)
            {
                var comma = i < findings.Count - 1 ? "," : "";
                text.AppendLine("    " + EscapeJson(findings[i]) + comma);
            }
            text.AppendLine("  ]");
            text.AppendLine("}");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, text.ToString(), new UTF8Encoding(false));
            Console.WriteLine("AIBT_P3_001_SPIKE_OK|" + outputPath);
            EditorApplication.Exit(success ? 0 : 1);
        }

        private static void RunFindings(List<string> findings)
        {
            // --- Full public type inventory via reflection (not just what the XML docs happened to list) ---
            var assembly = typeof(Graph).Assembly;
            var publicTypes = assembly.GetTypes().Where(t => t.IsPublic).Select(t => t.FullName).OrderBy(s => s, StringComparer.Ordinal).ToList();
            findings.Add("PUBLIC_TYPE_COUNT|" + publicTypes.Count);
            foreach (var typeName in publicTypes) findings.Add("PUBLIC_TYPE|" + typeName);

            var groupLikeTypes = publicTypes.Where(n =>
                n.IndexOf("Group", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Comment", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Sticky", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Reroute", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("View", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            findings.Add("GROUP_COMMENT_STICKY_REROUTE_WINDOW_VIEW_TYPES_FOUND|" + groupLikeTypes.Count);
            foreach (var n in groupLikeTypes) findings.Add("MATCHED_TYPE|" + n);

            // --- Serialization control: in-memory Graph without any asset backing ---
            var memoryGraph = new BehaviorTreeGraph();
            findings.Add("GRAPH_IS_SCRIPTABLE_OBJECT|" + (memoryGraph is ScriptableObject));
            try
            {
                var path = GraphDatabase.GetGraphAssetPath(memoryGraph);
                findings.Add("IN_MEMORY_GRAPH_HAS_ASSET_PATH|" + !string.IsNullOrEmpty(path));
            }
            catch (Exception e)
            {
                findings.Add("IN_MEMORY_GET_ASSET_PATH_THREW|" + e.GetType().Name);
            }
            try
            {
                memoryGraph.AddNode(new RootNode());
                findings.Add("IN_MEMORY_ADD_NODE_SUCCEEDED|True");
            }
            catch (Exception e)
            {
                findings.Add("IN_MEMORY_ADD_NODE_THREW|" + e.GetType().Name + ": " + e.Message);
            }

            // --- Build a synthetic 240-node tree through the only valid construction
            // path this framework accepts: an asset-backed graph from GraphDatabase. ---
            var assetPath = "Assets/aibt-spike-graph.aibtspike";
            var stopwatch = Stopwatch.StartNew();
            var graph = GraphDatabase.CreateGraph<BehaviorTreeGraph>(assetPath);
            var root = new RootNode { Position = Vector2.zero };
            graph.AddNode(root);
            var frontier = new List<Node> { root };
            var built = 1;
            var rng = new System.Random(12345);
            while (built < NodeCount)
            {
                var parent = frontier[rng.Next(frontier.Count)];
                Node child = built % 4 == 0 ? new SequenceNode()
                    : built % 4 == 1 ? new SelectorNode()
                    : built % 4 == 2 ? new ConditionNode()
                    : new ActionNode();
                child.Position = new Vector2((built % 20) * 220, (built / 20) * 140);
                graph.AddNode(child);

                var parentOutput = parent.GetOutputPortByName("children");
                var childInput = child.GetInputPortByName("in");
                if (parentOutput == null || childInput == null)
                {
                    findings.Add("CONNECT_SKIPPED_NO_PORT|parent=" + parent.GetType().Name + "|child=" + child.GetType().Name);
                }
                else
                {
                    graph.Connect(parentOutput, childInput);
                }

                if (child is SequenceNode || child is SelectorNode) frontier.Add(child);
                built++;
            }
            findings.Add("SYNTHETIC_TREE_NODE_COUNT|" + graph.NodeCount);
            findings.Add("SYNTHETIC_TREE_BUILD_MS|" + stopwatch.Elapsed.TotalMilliseconds.ToString("F2"));

            // --- Persist and inspect: does GraphDatabase force a format we don't control? ---
            var saveStopwatch = Stopwatch.StartNew();
            GraphDatabase.SaveGraph(graph);
            saveStopwatch.Stop();
            findings.Add("ASSET_SAVE_240_NODES_MS|" + saveStopwatch.Elapsed.TotalMilliseconds.ToString("F2"));
            var fullAssetPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            if (File.Exists(fullAssetPath))
            {
                var bytes = File.ReadAllBytes(fullAssetPath);
                findings.Add("ASSET_FILE_BYTES|" + bytes.Length);
                var isProbablyText = bytes.Take(Math.Min(bytes.Length, 200)).All(b => b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127));
                findings.Add("ASSET_FILE_LOOKS_LIKE_TEXT|" + isProbablyText);
                var preview = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 400));
                findings.Add("ASSET_FILE_PREVIEW|" + preview.Replace("\r", " ").Replace("\n", " | "));
            }
            else
            {
                findings.Add("ASSET_FILE_NOT_FOUND_AT|" + fullAssetPath);
            }

            // --- Testability: headless assertions on graph/node/port state, no window ---
            var reloaded = GraphDatabase.LoadGraph<BehaviorTreeGraph>(assetPath);
            findings.Add("HEADLESS_RELOAD_NODE_COUNT|" + (reloaded != null ? reloaded.NodeCount.ToString() : "null"));

            AssetDatabase.DeleteAsset(assetPath);
        }

        private static string EscapeJson(string value)
        {
            var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "\"" + escaped + "\"";
        }
    }
}
