using System;
using System.Text;
using AIBT.Authoring;

namespace AIBT.Tests.Editor.Performance
{
    /// <summary>
    /// Deterministic synthetic large-tree generator for P3-012. Produces a roughly balanced tree of
    /// <c>aibt.core.memory-sequence</c> internal nodes over <c>aibt.test.success</c> leaves (the same
    /// built-in/fixture node kinds <see cref="ReferencePreviewDriver.CreatePreviewNodeRegistry"/>
    /// compiles against), sized to a requested total node count.
    /// </summary>
    internal static class LargeGraphFixtures
    {
        internal readonly struct Shape
        {
            internal Shape(int nodeCount, int branchingFactor, int depth, int leafCount)
            {
                NodeCount = nodeCount;
                BranchingFactor = branchingFactor;
                Depth = depth;
                LeafCount = leafCount;
            }

            internal int NodeCount { get; }
            internal int BranchingFactor { get; }

            /// <summary>Depth from root (0) to the deepest leaf.</summary>
            internal int Depth { get; }
            internal int LeafCount { get; }

            /// <summary>A tree has exactly NodeCount - 1 edges; recorded explicitly, not measured.</summary>
            internal int EdgeCount => NodeCount - 1;
        }

        /// <summary>Builds a tree with approximately <paramref name="targetNodeCount"/> nodes and returns its parsed document plus the actual shape produced.</summary>
        internal static (TreeDocument Document, Shape Shape) Build(int targetNodeCount, int branchingFactor, string sourceId)
        {
            if (targetNodeCount < 2) throw new ArgumentOutOfRangeException(nameof(targetNodeCount));
            if (branchingFactor < 2) throw new ArgumentOutOfRangeException(nameof(branchingFactor));

            var json = new StringBuilder(targetNodeCount * 64);
            json.Append("{\"format\":\"aibt.tree\",\"formatVersion\":1,\"treeId\":\"tree.test.perf-large-graph\",\"name\":\"Large Graph Fixture\",\"root\":\"n0\",\"nodes\":{");

            var nextId = 1;
            var depth = 0;
            var leafCount = 0;
            var frontier = new System.Collections.Generic.List<int> { 0 };
            var first = true;

            // Breadth-first: for each parent in the current frontier, claim up to branchingFactor
            // fresh node IDs as its children. A parent becomes an internal memory-sequence node only
            // if it got a *full* set of children (nextId hadn't hit the target); otherwise -- including
            // the case where a parent gets zero children because the budget ran out earlier in this
            // same frontier pass -- its claimed children (if any) are emitted as leaves, and a
            // parent that claimed zero children is skipped (never emitted with an empty children array).
            while (frontier.Count > 0 && nextId < targetNodeCount)
            {
                var nextFrontier = new System.Collections.Generic.List<int>();
                foreach (var parentId in frontier)
                {
                    var childIds = new System.Collections.Generic.List<int>(branchingFactor);
                    for (var i = 0; i < branchingFactor && nextId < targetNodeCount; i++)
                    {
                        childIds.Add(nextId);
                        nextId++;
                    }

                    if (childIds.Count == 0)
                    {
                        AppendNode(json, ref first, parentId, "aibt.test.success", null);
                        leafCount++;
                        continue;
                    }

                    var isFullInternal = childIds.Count == branchingFactor;
                    AppendNode(json, ref first, parentId, "aibt.core.memory-sequence", childIds);
                    if (isFullInternal)
                    {
                        nextFrontier.AddRange(childIds);
                    }
                    else
                    {
                        foreach (var childId in childIds)
                        {
                            AppendNode(json, ref first, childId, "aibt.test.success", null);
                            leafCount++;
                        }
                    }
                }

                frontier = nextFrontier;
                depth++;
            }

            // Any parents left in the frontier (loop exited via nextId reaching the target while this
            // frontier was still pending expansion) are leaves: they already have no children claimed.
            foreach (var leftover in frontier)
            {
                AppendNode(json, ref first, leftover, "aibt.test.success", null);
                leafCount++;
            }

            json.Append("}}");

            var bytes = Encoding.UTF8.GetBytes(json.ToString());
            var result = CanonicalTreeJson.Parse(bytes, sourceId);
            if (!result.Success)
            {
                throw new InvalidOperationException("Generated large-graph fixture failed to parse: " + Describe(result.Diagnostics));
            }

            return (result.Document, new Shape(nextId, branchingFactor, depth, leafCount));
        }

        private static void AppendNode(StringBuilder json, ref bool first, int id, string typeId, System.Collections.Generic.List<int> childIds)
        {
            if (!first) json.Append(',');
            first = false;
            json.Append('"').Append('n').Append(id).Append("\":{\"type\":\"").Append(typeId).Append("\",\"typeVersion\":1");
            if (childIds != null && childIds.Count > 0)
            {
                json.Append(",\"children\":[");
                for (var i = 0; i < childIds.Count; i++)
                {
                    if (i > 0) json.Append(',');
                    json.Append('"').Append('n').Append(childIds[i]).Append('"');
                }
                json.Append(']');
            }

            json.Append('}');
        }

        private static string Describe(DiagnosticCollection diagnostics)
        {
            var builder = new StringBuilder();
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (index > 0) builder.Append("; ");
                builder.Append(diagnostics[index].Code.Value).Append(": ").Append(diagnostics[index].Message);
            }

            return builder.ToString();
        }
    }
}
