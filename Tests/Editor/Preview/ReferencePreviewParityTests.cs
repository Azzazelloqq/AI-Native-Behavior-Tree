using System;
using System.Collections.Generic;
using System.IO;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Tests.Editor;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Preview
{
    /// <summary>
    /// P3-009 acceptance criterion: "Stepping the same tree through the in-editor preview and
    /// through the existing behavior-case runner (headless) produces the identical step sequence
    /// and terminal status." The "oracle" side below constructs a raw <c>ReferenceExecutionMachine</c>
    /// exactly the way the headless behavior-case runner does (same registries, same compiled
    /// program) -- see <c>Tests/Integration/SemanticSlice/ReferenceBehaviorCaseAdapter.cs</c> for the
    /// pattern this mirrors. The "preview" side drives the same tree through the public
    /// <see cref="ReferencePreviewDriver"/> facade that <c>Editor/Preview/</c> uses. Comparing their
    /// observed trace projections and terminal statuses proves the facade introduces no drift.
    /// </summary>
    public sealed class ReferencePreviewParityTests
    {
        private static readonly CompiledCompilerVersion CompilerVersion = new CompiledCompilerVersion(1, 0, 0, 1);
        private const string SourceId = "tests/editor/preview/success-then-running.aibt.json";

        [Test]
        public void PreviewAndHeadlessOracleProduceIdenticalStepSequenceAcrossTicks()
        {
            var document = ParseFixture(FixturePath());

            var oracle = CreateOracle(document, SourceId, out var oracleTrace);
            Assert.That(ReferencePreviewDriver.TryCreate(document, SourceId, out var preview, out var previewDiagnostics), Is.True, Messages(previewDiagnostics));

            for (var tick = 1; tick <= 2; tick++)
            {
                var oracleEnvelope = oracle.Update(
                    new ReferenceUpdateContext((ulong)tick, new Revision((ulong)tick), 0),
                    ReferenceStepBudget.Unlimited);
                var oracleEvents = oracleTrace.Take();

                var previewEnvelope = preview.RunTick(timeMicroseconds: 0);
                var previewEvents = Project(previewEnvelope.TraceEvents);

                Assert.That(previewEvents, Is.EqualTo(oracleEvents), $"Tick {tick}: step sequence diverged.");
                Assert.That(previewEnvelope.RootResult, Is.EqualTo(oracleEnvelope.RootResult), $"Tick {tick}: terminal status diverged.");
            }

            Assert.That(preview.TerminalResult, Is.EqualTo(oracle.TerminalResult));
        }

        /// <summary>
        /// Acceptance criterion: "Edits made via P3-006 are reflected in the next preview run
        /// without requiring an editor restart." <see cref="SemanticEditOperations"/> is P3-006's own
        /// edit surface; applying it and creating a fresh driver over the result (exactly what
        /// <c>BehaviorTreePreviewWindow.LoadDocument</c> does when called again on an already-open
        /// window) proves the edit is observed without any editor/process restart.
        /// </summary>
        [Test]
        public void EditViaSemanticEditOperationsIsReflectedInNextPreviewRunWithoutEditorRestart()
        {
            var document = ParseFixture(FixturePath());

            Assert.That(ReferencePreviewDriver.TryCreate(document, "preview/before.aibt.json", out var before, out var beforeDiagnostics), Is.True, Messages(beforeDiagnostics));
            var beforeEnvelope = before.RunTick();
            // A leaf returning Running ends the update as Waiting with no root result yet (matches
            // ReferenceExecutionMachine.Tick's Running branch) -- the tree has not reached a terminal status.
            Assert.That(beforeEnvelope.Progress, Is.EqualTo(ReferencePreviewProgress.Waiting), "Baseline fixture must still be running (not terminal) before the edit.");
            Assert.That(beforeEnvelope.RootResult, Is.Null);

            var edited = SemanticEditOperations.RemoveNode(document, new NodeId("b"));

            Assert.That(ReferencePreviewDriver.TryCreate(edited, "preview/after.aibt.json", out var after, out var afterDiagnostics), Is.True, Messages(afterDiagnostics));
            var afterEnvelope = after.RunTick();

            Assert.That(afterEnvelope.RootResult, Is.EqualTo(NodeStatus.Success), "Removing the always-Running leaf must be reflected in the next preview run.");
        }

        private static ReferenceExecutionMachine CreateOracle(TreeDocument document, string sourceId, out CollectingSink trace)
        {
            var registry = ReferencePreviewDriver.CreatePreviewNodeRegistry();
            var options = new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, CompilerVersion);
            var compilation = ReferenceCompiler.Compile(document, registry, options);
            Assert.That(compilation.Success, Is.True, Messages(compilation.Diagnostics));

            var nodeIdByRuntimeIndex = new Dictionary<uint, NodeId>();
            foreach (var entry in compilation.Program.DebugMap)
            {
                nodeIdByRuntimeIndex[entry.RuntimeNodeIndex] = entry.AuthoringNodeId;
            }

            trace = new CollectingSink(nodeIdByRuntimeIndex);
            return new ReferenceExecutionMachine(
                compilation.Program,
                new TreeInstanceId(1),
                ReferenceLeafRegistry.CreatePhase1Fixtures(),
                trace,
                ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                RegisteredBlackboardRegistry.Empty,
                ReferenceObserverConditionRegistry.Empty);
        }

        private static List<(ReferencePreviewTraceEventKind Kind, NodeId? Node, NodeStatus? Status, NodeId? SourceNode)> Project(
            IReadOnlyList<ReferencePreviewTraceEvent> events)
        {
            var result = new List<(ReferencePreviewTraceEventKind, NodeId?, NodeStatus?, NodeId?)>(events.Count);
            foreach (var e in events)
            {
                result.Add((e.Kind, e.Node, e.Status, e.SourceNode));
            }

            return result;
        }

        private static TreeDocument ParseFixture(string path)
        {
            var result = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
            Assert.That(result.Success, Is.True, Messages(result.Diagnostics));
            return result.Document;
        }

        private static string FixturePath()
            => EditorTestPackagePaths.Resolve("Tests", "Editor", "Preview", "Fixtures", "success-then-running.aibt.json");

        private static string Messages(DiagnosticCollection diagnostics)
        {
            if (diagnostics == null) return string.Empty;
            var parts = new List<string>();
            for (var index = 0; index < diagnostics.Count; index++)
            {
                parts.Add(diagnostics[index].Code.Value + ": " + diagnostics[index].Message);
            }

            return string.Join("; ", parts);
        }

        private sealed class CollectingSink : IReferenceTraceSink
        {
            private readonly List<ReferenceTraceRecord> _records = new List<ReferenceTraceRecord>();
            private readonly IReadOnlyDictionary<uint, NodeId> _nodeIdByRuntimeIndex;

            internal CollectingSink(IReadOnlyDictionary<uint, NodeId> nodeIdByRuntimeIndex)
            {
                _nodeIdByRuntimeIndex = nodeIdByRuntimeIndex;
            }

            public void Record(in ReferenceTraceRecord record) => _records.Add(record);

            internal List<(ReferencePreviewTraceEventKind, NodeId?, NodeStatus?, NodeId?)> Take()
            {
                var result = new List<(ReferencePreviewTraceEventKind, NodeId?, NodeStatus?, NodeId?)>(_records.Count);
                foreach (var record in _records)
                {
                    NodeId? node = null;
                    if (record.NodeIndex.IsValid && _nodeIdByRuntimeIndex.TryGetValue(record.NodeIndex.Value, out var mapped))
                    {
                        node = mapped;
                    }

                    NodeId? sourceNode = null;
                    if (record.SourceNodeIndex.IsValid && _nodeIdByRuntimeIndex.TryGetValue(record.SourceNodeIndex.Value, out var mappedSource))
                    {
                        sourceNode = mappedSource;
                    }

                    result.Add((MapKind(record.Kind), node, record.Status, sourceNode));
                }

                _records.Clear();
                return result;
            }

            private static ReferencePreviewTraceEventKind MapKind(ReferenceTraceEventKind value)
            {
                switch (value)
                {
                    case ReferenceTraceEventKind.UpdateStarted: return ReferencePreviewTraceEventKind.UpdateStarted;
                    case ReferenceTraceEventKind.UpdateCompleted: return ReferencePreviewTraceEventKind.UpdateCompleted;
                    case ReferenceTraceEventKind.NodeEntered: return ReferencePreviewTraceEventKind.NodeEntered;
                    case ReferenceTraceEventKind.NodeTicked: return ReferencePreviewTraceEventKind.NodeTicked;
                    case ReferenceTraceEventKind.NodeAbortStarted: return ReferencePreviewTraceEventKind.NodeAbortStarted;
                    case ReferenceTraceEventKind.NodeExited: return ReferencePreviewTraceEventKind.NodeExited;
                    case ReferenceTraceEventKind.CommandEmitted: return ReferencePreviewTraceEventKind.CommandEmitted;
                    case ReferenceTraceEventKind.CompletionConsumed: return ReferencePreviewTraceEventKind.CompletionConsumed;
                    case ReferenceTraceEventKind.CompletionDiscarded: return ReferencePreviewTraceEventKind.CompletionDiscarded;
                    case ReferenceTraceEventKind.BlackboardChanged: return ReferencePreviewTraceEventKind.BlackboardChanged;
                    case ReferenceTraceEventKind.ObserverQueued: return ReferencePreviewTraceEventKind.ObserverQueued;
                    case ReferenceTraceEventKind.ObserverEvaluated: return ReferencePreviewTraceEventKind.ObserverEvaluated;
                    case ReferenceTraceEventKind.DiagnosticRaised: return ReferencePreviewTraceEventKind.DiagnosticRaised;
                    case ReferenceTraceEventKind.BudgetYielded: return ReferencePreviewTraceEventKind.BudgetYielded;
                    case ReferenceTraceEventKind.ExecutionResumed: return ReferencePreviewTraceEventKind.ExecutionResumed;
                    default: throw new ArgumentOutOfRangeException(nameof(value));
                }
            }
        }
    }
}
