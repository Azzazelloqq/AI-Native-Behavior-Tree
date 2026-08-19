using System;
using System.Collections.Generic;
using System.Text;
using AIBT.Authoring;

namespace AIBT.Benchmarks.Phase4.Scheduling
{
    /// <summary>
    /// The <c>Documentation~/benchmarks.md</c> scenario catalog. Each entry states what it
    /// isolates, per the catalog's own discipline. Scenarios marked <see cref="ScenarioDefinition.Implemented"/>
    /// false are real catalog entries with no implementation yet -- they need leaf semantics
    /// (blackboard access, command emission, async operations, managed nodes, cost tagging) that
    /// do not exist in a reusable form anywhere in AIBT today; building those is out of this
    /// card's scope, and this catalog does not fake them with structurally-similar trees that
    /// would not actually isolate what they claim to. This file lives only under
    /// <c>Benchmarks~/Phase4/</c> (copied into the isolated Player-benchmark project alongside
    /// <c>Runtime/</c> and <c>Authoring/</c>) because it needs <c>AIBT.Authoring</c>'s compiler,
    /// which <c>Tests/Runtime/Benchmarking/</c> deliberately does not reference.
    /// </summary>
    internal static class SchedulingScenarios
    {
        private static readonly CompiledCompilerVersion CompilerVersion = new CompiledCompilerVersion(1, 0, 0, 1);

        internal readonly struct ScenarioDefinition
        {
            internal ScenarioDefinition(string name, string isolates, bool implemented, Func<CompiledScenario> build)
            {
                Name = name;
                Isolates = isolates;
                Implemented = implemented;
                Build = build;
            }

            internal string Name { get; }
            internal string Isolates { get; }
            internal bool Implemented { get; }
            internal Func<CompiledScenario> Build { get; }
        }

        internal readonly struct CompiledScenario
        {
            internal CompiledScenario(CompiledProgram program, AIBT.NodeStatus[] leafStatusByRuntimeIndex, AIBT.NativeLifecycleNodeKindV1[] nodeKinds)
            {
                Program = program;
                LeafStatusByRuntimeIndex = leafStatusByRuntimeIndex;
                NodeKinds = nodeKinds;
            }

            internal CompiledProgram Program { get; }
            internal AIBT.NodeStatus[] LeafStatusByRuntimeIndex { get; }
            internal AIBT.NativeLifecycleNodeKindV1[] NodeKinds { get; }
        }

        internal static IReadOnlyList<ScenarioDefinition> Catalog { get; } = new[]
        {
            new ScenarioDefinition(
                "scheduling-baseline-empty-job",
                "The fixed per-tick overhead of beginning and completing an update on the smallest possible tree (one leaf), isolated from any node-execution cost.",
                true, () => BuildFixedShape(depth: 1, branching: 1, AIBT.NodeStatus.Success)),
            new ScenarioDefinition(
                "shallow-tree-cheap-conditions",
                "A small, flat sequence of cheap leaves -- the common case for simple agent behavior.",
                true, () => BuildFixedShape(depth: 2, branching: 4, AIBT.NodeStatus.Success)),
            new ScenarioDefinition(
                "deep-sequence-selector-traversal",
                "Traversal cost through many nested composite levels before reaching a leaf.",
                true, () => BuildFixedShape(depth: 6, branching: 2, AIBT.NodeStatus.Success)),
            new ScenarioDefinition(
                "wide-branching-frequent-failures",
                "A wide composite where most children fail, isolating early-exit/failure-path cost.",
                true, () => BuildFixedShape(depth: 2, branching: 16, AIBT.NodeStatus.Failure)),
            new ScenarioDefinition(
                "predominantly-running-actions",
                "Leaves that stay Running every tick, isolating the cost of resuming an already-entered tree without re-traversal.",
                true, () => BuildFixedShape(depth: 2, branching: 4, AIBT.NodeStatus.Running)),
            new ScenarioDefinition(
                "many-programs-small-populations",
                "Many distinct small compiled programs with few agents each, isolating per-program (not per-agent) scheduling overhead.",
                true, BuildManyProgramsPlaceholder),
            new ScenarioDefinition(
                "event-driven-sleeping-wakeup", "Agents that sleep until an external wakeup event.", false, null),
            new ScenarioDefinition(
                "intensive-typed-blackboard-access", "Leaves with heavy typed-blackboard read/write.", false, null),
            new ScenarioDefinition(
                "high-command-emission", "Leaves emitting many commands per tick.", false, null),
            new ScenarioDefinition(
                "computationally-expensive-burst-nodes", "Leaves with deliberately expensive Burst-compiled work.", false, null),
            new ScenarioDefinition(
                "mixed-cheap-and-expensive-agents", "A population mixing cheap and expensive agents/trees.", false, null),
            new ScenarioDefinition(
                "managed-node-boundary", "Trees crossing into a managed (non-Burst) node.", false, null),
            new ScenarioDefinition(
                "same-frame-pipelined-budgeted-execution", "Direct latency comparison across policies including PipelinedJobs (P4-003).", false, null),
            new ScenarioDefinition(
                "hot-reload-debug-instrumentation-overhead", "Overhead of debug instrumentation / hot reload while running.", false, null),
        };

        /// <summary>Builds a balanced tree of the given depth/branching factor with a fixed leaf status, mirroring `Tests/Editor/Performance/LargeGraphFixtures.cs`'s pattern.</summary>
        private static CompiledScenario BuildFixedShape(int depth, int branching, AIBT.NodeStatus leafStatus)
        {
            var json = new StringBuilder();
            json.Append("{\"format\":\"aibt.tree\",\"formatVersion\":1,\"treeId\":\"tree.benchmark.scheduling\",\"name\":\"Scheduling Scenario\",\"root\":\"n0\",\"nodes\":{");
            var nextId = 1;
            var first = true;
            var frontier = new List<int> { 0 };
            var leafTypeId = leafStatus switch
            {
                AIBT.NodeStatus.Success => "aibt.test.success",
                AIBT.NodeStatus.Failure => "aibt.test.failure",
                _ => "aibt.test.running",
            };

            void Append(int id, string typeId, List<int> children)
            {
                if (!first) json.Append(',');
                first = false;
                json.Append('"').Append('n').Append(id).Append("\":{\"type\":\"").Append(typeId).Append("\",\"typeVersion\":1");
                if (children != null && children.Count > 0)
                {
                    json.Append(",\"children\":[");
                    for (var i = 0; i < children.Count; i++)
                    {
                        if (i > 0) json.Append(',');
                        json.Append('"').Append('n').Append(children[i]).Append('"');
                    }
                    json.Append(']');
                }
                json.Append('}');
            }

            for (var level = 0; level < depth - 1 && frontier.Count > 0; level++)
            {
                var nextFrontier = new List<int>();
                foreach (var parentId in frontier)
                {
                    var childIds = new List<int>(branching);
                    for (var i = 0; i < branching; i++) { childIds.Add(nextId); nextId++; }
                    Append(parentId, "aibt.core.memory-sequence", childIds);
                    if (level < depth - 2) nextFrontier.AddRange(childIds);
                    else foreach (var c in childIds) Append(c, leafTypeId, null);
                }
                frontier = nextFrontier;
            }
            foreach (var leftover in frontier) Append(leftover, leafTypeId, null);

            json.Append("}}");
            return Compile(json.ToString(), leafStatus);
        }

        private static CompiledScenario BuildManyProgramsPlaceholder() => BuildFixedShape(depth: 1, branching: 1, AIBT.NodeStatus.Success);

        private static CompiledScenario Compile(string json, AIBT.NodeStatus leafStatus)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var parseResult = CanonicalTreeJson.Parse(bytes, "benchmarks/phase4/scheduling.aibt.json");
            if (!parseResult.Success) throw new InvalidOperationException("Scenario JSON failed to parse: " + Describe(parseResult.Diagnostics));

            var registry = BuildRegistry();
            var options = new ReferenceCompilerOptions("benchmarks/phase4/scheduling.aibt.json", ReferenceCompilationPolicy.Phase1, CompilerVersion);
            var compilation = ReferenceCompiler.Compile(parseResult.Document, registry, options);
            if (!compilation.Success) throw new InvalidOperationException("Scenario tree failed to compile: " + Describe(compilation.Diagnostics));

            var program = compilation.Program;
            var kinds = new AIBT.NativeLifecycleNodeKindV1[program.Nodes.Count];
            var leafStatusByIndex = new AIBT.NodeStatus[program.Nodes.Count];
            var memorySequenceTypeId = AIBT.StableHash.Fnv1A64("aibt.core.memory-sequence");
            for (var index = 0; index < program.Nodes.Count; index++)
            {
                var isComposite = program.Nodes[index].NodeTypeId == memorySequenceTypeId;
                kinds[index] = isComposite ? AIBT.NativeLifecycleNodeKindV1.MemorySequence : AIBT.NativeLifecycleNodeKindV1.GeneratedLeaf;
                leafStatusByIndex[index] = leafStatus;
            }

            return new CompiledScenario(program, leafStatusByIndex, kinds);
        }

        private static NodeRegistry BuildRegistry() => ReferencePreviewDriver.CreatePreviewNodeRegistry();

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
