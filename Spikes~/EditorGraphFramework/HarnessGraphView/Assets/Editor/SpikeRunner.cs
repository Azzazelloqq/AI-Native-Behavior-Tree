using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace AIBT.Spikes.EditorGraphFrameworkGraphView
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
            text.AppendLine("  \"schema\": \"aibt-p3-014-spike-v1\",");
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
            Console.WriteLine("AIBT_P3_014_SPIKE_OK|" + outputPath);
            EditorApplication.Exit(success ? 0 : 1);
        }

        private static void RunFindings(List<string> findings)
        {
            // --- Public type inventory, scoped to the GraphView namespace (the
            // assembly also carries the legacy Mecanim graph editor, so a raw
            // assembly-wide scan would be noisy/unfair versus P3-001's scan). ---
            var assembly = typeof(GraphView).Assembly;
            findings.Add("GRAPHVIEW_ASSEMBLY|" + assembly.GetName().Name);

            var namespaceTypes = assembly.GetTypes()
                .Where(t => t.IsPublic && t.Namespace == "UnityEditor.Experimental.GraphView")
                .Select(t => t)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();
            findings.Add("GRAPHVIEW_PUBLIC_TYPE_COUNT|" + namespaceTypes.Count);
            foreach (var t in namespaceTypes) findings.Add("GRAPHVIEW_PUBLIC_TYPE|" + t.FullName);

            var groupLikeTypes = namespaceTypes.Where(t =>
                t.FullName.IndexOf("Group", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.FullName.IndexOf("Comment", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.FullName.IndexOf("Sticky", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.FullName.IndexOf("Reroute", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            findings.Add("GROUP_COMMENT_STICKY_REROUTE_TYPES_FOUND|" + groupLikeTypes.Count);
            foreach (var t in groupLikeTypes) findings.Add("MATCHED_TYPE|" + t.FullName);

            // --- Support-risk: [Obsolete] on GraphView namespace types, checked
            // against the actually-installed 6000.5.8f1 Editor, not a doc scan. ---
            var obsoleteTypes = namespaceTypes.Where(t => t.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0).ToList();
            findings.Add("OBSOLETE_TYPE_COUNT|" + obsoleteTypes.Count);
            foreach (var t in obsoleteTypes) findings.Add("OBSOLETE_TYPE|" + t.FullName);
            findings.Add("GRAPHVIEW_TYPE_ITSELF_OBSOLETE|" + (typeof(GraphView).GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0));

            // --- Serialization control: construct a GraphView with no asset/
            // window backing at all. ---
            var view = new BehaviorTreeGraphView();
            findings.Add("GRAPHVIEW_CONSTRUCTED_STANDALONE|True");

            // --- Can it be hosted in an EditorWindow in -batchmode -nographics? ---
            try
            {
                var window = EditorWindow.CreateInstance<BehaviorTreeGraphWindow>();
                window.Populate(view);
                findings.Add("EDITORWINDOW_HOST_SUCCEEDED|True");
            }
            catch (Exception e)
            {
                findings.Add("EDITORWINDOW_HOST_THREW|" + e.GetType().Name + ": " + e.Message);
            }

            // --- Build the same 240-node synthetic tree shape P3-001 used. ---
            var stopwatch = Stopwatch.StartNew();
            var root = new BtNode("n0", BtNodeKind.Root);
            view.AddElement(root);
            root.SetPosition(new Rect(0, 0, 0, 0));
            var frontier = new List<BtNode> { root };
            var all = new List<BtNode> { root };
            var rng = new System.Random(12345);
            var built = 1;
            var connectFailures = 0;
            while (built < NodeCount)
            {
                var parent = frontier[rng.Next(frontier.Count)];
                var kind = built % 4 == 0 ? BtNodeKind.Sequence
                    : built % 4 == 1 ? BtNodeKind.Selector
                    : built % 4 == 2 ? BtNodeKind.Condition
                    : BtNodeKind.Action;
                var child = new BtNode("n" + built, kind);
                view.AddElement(child);
                child.SetPosition(new Rect((built % 20) * 220, (built / 20) * 140, 0, 0));

                if (parent.OutputPort != null && child.InputPort != null)
                {
                    var edge = new Edge { output = parent.OutputPort, input = child.InputPort };
                    parent.OutputPort.Connect(edge);
                    child.InputPort.Connect(edge);
                    view.AddElement(edge);
                }
                else
                {
                    connectFailures++;
                }

                if (kind == BtNodeKind.Sequence || kind == BtNodeKind.Selector) frontier.Add(child);
                all.Add(child);
                built++;
            }
            stopwatch.Stop();
            findings.Add("SYNTHETIC_TREE_NODE_COUNT|" + all.Count);
            findings.Add("SYNTHETIC_TREE_CONNECT_FAILURES|" + connectFailures);
            findings.Add("SYNTHETIC_TREE_BUILD_MS|" + stopwatch.Elapsed.TotalMilliseconds.ToString("F2"));
            findings.Add("GRAPHVIEW_NODE_COUNT_AFTER_BUILD|" + view.nodes.Count());
            findings.Add("GRAPHVIEW_EDGE_COUNT_AFTER_BUILD|" + view.edges.Count());

            // --- Persistence: GraphView has no built-in save/load. Confirm AIBT
            // would own 100% of the format by round-tripping a plain custom
            // snapshot, and measure its size against P3-001's ~2.85 KB/node. ---
            var snapshot = new GraphSnapshot { Nodes = new List<NodeSnapshot>() };
            foreach (var n in all)
            {
                var pos = n.GetPosition();
                snapshot.Nodes.Add(new NodeSnapshot
                {
                    Id = n.Id,
                    Kind = n.Kind.ToString(),
                    X = pos.x,
                    Y = pos.y
                });
            }
            var saveStopwatch = Stopwatch.StartNew();
            var json = JsonUtility.ToJson(snapshot);
            saveStopwatch.Stop();
            var jsonBytes = Encoding.UTF8.GetByteCount(json);
            findings.Add("CUSTOM_SNAPSHOT_SERIALIZE_MS|" + saveStopwatch.Elapsed.TotalMilliseconds.ToString("F2"));
            findings.Add("CUSTOM_SNAPSHOT_BYTES|" + jsonBytes);
            findings.Add("CUSTOM_SNAPSHOT_BYTES_PER_NODE|" + (jsonBytes / (double)all.Count).ToString("F2"));

            var reloaded = JsonUtility.FromJson<GraphSnapshot>(json);
            findings.Add("CUSTOM_SNAPSHOT_ROUNDTRIP_NODE_COUNT|" + (reloaded != null && reloaded.Nodes != null ? reloaded.Nodes.Count.ToString() : "null"));
            findings.Add("SERIALIZATION_IS_AIBT_OWNED|True");
        }

        [Serializable]
        private class NodeSnapshot
        {
            public string Id;
            public string Kind;
            public float X;
            public float Y;
        }

        [Serializable]
        private class GraphSnapshot
        {
            public List<NodeSnapshot> Nodes;
        }

        private static string EscapeJson(string value)
        {
            var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "\"" + escaped + "\"";
        }
    }
}
