using System;
using System.Diagnostics;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Editor.Graph;
using AIBT.Editor.Layout;
using AIBT.Editor.Organization;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Editor.Performance
{
    /// <summary>
    /// P3-012: proves the editor's rendering (P3-003), manual-organization (P3-004/P3-005), and
    /// semantic-editing (P3-006) surfaces remain correct at synthetic scales matching and exceeding
    /// P3-001's 240-node measured scale, and logs interaction-latency/memory numbers via
    /// <see cref="TestContext.WriteLine(string)"/> for transcription into
    /// <c>Planning~/Evidence/P3-012/</c> and <c>Benchmarks~/Platform/Editor/</c>. This test asserts
    /// correctness only -- it defines no performance default, threshold, or "supported graph size"
    /// claim (that is Phase 4's ownership); the recorded numbers and this card's own usability read
    /// of them live in the evidence, not in code.
    /// </summary>
    public sealed class LargeGraphPerformanceTests
    {
        private const int BranchingFactor = 8;
        private static readonly int[] Scales = { 240, 500, 1000, 2000 };

        [Test]
        public void EveryScaleCompilesRendersAndSupportsEditOrganizeOperationsWithRecordedLatency()
        {
            var registry = ReferencePreviewDriver.CreatePreviewNodeRegistry();

            foreach (var scale in Scales)
            {
                var sourceId = "tests/editor/performance/large-graph-" + scale + ".aibt.json";
                var (document, shape) = LargeGraphFixtures.Build(scale, BranchingFactor, sourceId);
                Assert.That(document.Nodes.Count, Is.EqualTo(shape.NodeCount));

                var beforeMemory = GC.GetTotalMemory(true);

                var graphView = new BehaviorTreeGraphView();
                var renderMs = Time(() => graphView.Populate(document, registry));
                Assert.That(graphView.NodesById.Count, Is.EqualTo(shape.NodeCount));

                var afterMemory = GC.GetTotalMemory(false);

                var layoutMs = Time(() => DeterministicAutoLayoutService.Layout(document));

                var leafId = FindALeaf(document);
                var repositionMs = Time(() =>
                {
                    var layout = DeterministicAutoLayoutService.Layout(document);
                    LayoutOrganizationOperations.SetNodePosition(layout, leafId, new LayoutPoint(123f, 456f));
                });

                var addOptions = new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 1));
                TreeDocument afterAdd = null;
                var addMs = Time(() =>
                {
                    var newNode = new NodeDocument(
                        new NodeId("perf-added"), "aibt.test.success", 1,
                        parameters: SemanticObject.Empty, tags: TagSet.Empty);
                    var result = SemanticEditTransaction.Apply(
                        document, doc => SemanticEditOperations.AddNode(doc, newNode, document.Root), registry, addOptions);
                    Assert.That(result.Accepted, Is.True, Messages(result.Diagnostics));
                    afterAdd = result.Document;
                });

                var reRenderMs = Time(() => graphView.Populate(afterAdd, registry));

                var panZoomMs = Time(() =>
                {
                    graphView.viewTransform.position = new Vector3(200f, -150f, 0f);
                    graphView.viewTransform.scale = new Vector3(0.5f, 0.5f, 1f);
                });

                TestContext.WriteLine(
                    "P3-012 scale=" + shape.NodeCount
                    + " branching=" + shape.BranchingFactor
                    + " depth=" + shape.Depth
                    + " edges=" + shape.EdgeCount
                    + " leaves=" + shape.LeafCount
                    + " renderMs=" + renderMs.ToString("F3")
                    + " autoLayoutMs=" + layoutMs.ToString("F3")
                    + " repositionMs=" + repositionMs.ToString("F3")
                    + " addNodeMs=" + addMs.ToString("F3")
                    + " reRenderAfterEditMs=" + reRenderMs.ToString("F3")
                    + " panZoomMs=" + panZoomMs.ToString("F3")
                    + " managedHeapDeltaBytes=" + (afterMemory - beforeMemory));
            }
        }

        private static NodeId FindALeaf(TreeDocument document)
        {
            foreach (var node in document.Nodes)
            {
                if (node.Children.Count == 0)
                {
                    return node.Id;
                }
            }

            throw new InvalidOperationException("The generated fixture has no leaf node.");
        }

        private static double Time(Action action)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static string Messages(DiagnosticCollection diagnostics)
        {
            if (diagnostics == null) return string.Empty;
            var parts = new System.Collections.Generic.List<string>();
            for (var index = 0; index < diagnostics.Count; index++)
            {
                parts.Add(diagnostics[index].Code.Value + ": " + diagnostics[index].Message);
            }

            return string.Join("; ", parts);
        }
    }
}
