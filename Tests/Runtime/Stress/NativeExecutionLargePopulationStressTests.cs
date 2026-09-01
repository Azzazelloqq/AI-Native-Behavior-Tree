using AIBT.Runtime.Scheduling;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.Stress
{
    /// <summary>
    /// P7-004's large-population stress deliverable: at least 10x `P4-002`'s largest measured
    /// population (1024 agents) with no crash, no silent corruption, and no determinism drift
    /// versus the same scenario at a normal scale. Reuses `SchedulingPolicyDriver.TryCreateAgents`/
    /// `TryRunBatchedJobsSameFrame` -- the exact API `Benchmarks~/Phase4/Scheduling/Unity/
    /// SchedulingBenchmarkRunner.cs` already drives at up to 128 agents -- scaled up, with `batchSize`
    /// matching `P4-002`'s own largest measured fixed batch size (128), the specific configuration
    /// that evidence found per-agent cost growing with population (chunk/`JobHandle.CombineDependencies`
    /// count scaling with agent count) -- a stress suite that avoided the one already-known-costly
    /// case would be dishonest.
    /// </summary>
    public sealed class NativeExecutionLargePopulationStressTests
    {
        private const int LargePopulation = 10_240; // 10x P4-002's largest measured population (1024).
        private const int ControlPopulation = 16; // P4-002's own smallest measured population.
        private const uint BatchSize = 128;

        [Test]
        public void LargePopulation_CompletesWithNoCrashAndTheSameDeterministicOutcomeAsASmallControlPopulation()
        {
            var program = ThreeLeafFixture.CreateProgram();
            var kinds = ThreeLeafFixture.NodeKinds;
            var leafStatus = ThreeLeafFixture.LeafStatus;

            RunAndAssertAllAgentsSucceed(program, kinds, leafStatus, ControlPopulation, "control (" + ControlPopulation + ")");
            RunAndAssertAllAgentsSucceed(program, kinds, leafStatus, LargePopulation, "large (" + LargePopulation + ")");
        }

        private static void RunAndAssertAllAgentsSucceed(
            CompiledProgram program, NativeLifecycleNodeKindV1[] kinds, NodeStatus[] leafStatus, int population, string label)
        {
            Assert.That(SchedulingPolicyDriver.TryCreateAgents(program, kinds, population, Allocator.Persistent, out var agents, out var createFailure),
                Is.True, label + ": " + createFailure.Code);
            try
            {
                Assert.That(SchedulingPolicyDriver.TryRunBatchedJobsSameFrame(agents, updateId: 1, BatchSize, leafStatus, Allocator.Persistent, out var totalSteps, out var runFailure),
                    Is.True, label + ": " + runFailure.Code);
                Assert.That(totalSteps, Is.GreaterThan(0UL), label);

                for (var index = 0; index < agents.Length; index++)
                {
                    Assert.That(agents[index].TerminalResult, Is.EqualTo(NodeStatus.Success),
                        label + ": agent " + index + " of " + population + " did not reach the identical deterministic outcome " +
                        "every other agent (and the same scenario at a different population) must reach.");
                }
            }
            finally
            {
                foreach (var agent in agents) agent.Dispose();
            }
        }

        /// <summary>A three-leaf sequence, deeper than `SchedulingPolicyDriverTests.Fixture`'s own
        /// single-leaf program so a batched run spans several rounds per agent (more opportunity for
        /// a cross-lane indexing bug to surface as a wrong per-agent outcome) while staying cheap
        /// enough to build 10,240 times over in one EditMode test.</summary>
        private static class ThreeLeafFixture
        {
            internal static NativeLifecycleNodeKindV1[] NodeKinds => new[]
            {
                NativeLifecycleNodeKindV1.MemorySequence,
                NativeLifecycleNodeKindV1.GeneratedLeaf,
                NativeLifecycleNodeKindV1.GeneratedLeaf,
                NativeLifecycleNodeKindV1.GeneratedLeaf,
            };

            internal static NodeStatus[] LeafStatus => new[]
            {
                NodeStatus.Running, NodeStatus.Success, NodeStatus.Success, NodeStatus.Success,
            };

            internal static CompiledProgram CreateProgram()
            {
                var nodes = new[]
                {
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1, 0, 4, 4, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 3), CompiledNodeFlags.BurstDomain, 0, default, default),
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.tests.stress-leaf-1"), 1, 0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                        default, CompiledNodeFlags.BurstDomain, 1, default, default),
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.tests.stress-leaf-2"), 1, 0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                        default, CompiledNodeFlags.BurstDomain, 2, default, default),
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.tests.stress-leaf-3"), 1, 0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                        default, CompiledNodeFlags.BurstDomain, 3, default, default),
                };
                var children = new uint[] { 1, 2, 3 };
                var debug = new[]
                {
                    new CompiledDebugMapEntry(0, new NodeId("root"), "/tree/root"),
                    new CompiledDebugMapEntry(1, new NodeId("leaf1"), "/tree/leaf1"),
                    new CompiledDebugMapEntry(2, new NodeId("leaf2"), "/tree/leaf2"),
                    new CompiledDebugMapEntry(3, new NodeId("leaf3"), "/tree/leaf3"),
                };

                var preliminary = Build(Hash('d'), nodes, children, debug);
                var contentHash = CompiledProgramContentHashV1.Compute(preliminary);
                return Build(contentHash, nodes, children, debug);
            }

            private static CompiledProgram Build(CompiledHash contentHash, CompiledNodeRecord[] nodes, uint[] children, CompiledDebugMapEntry[] debug)
            {
                var header = new CompiledProgramHeader(
                    1, 1, new CompiledCompilerVersion(1, 0, 0, 1),
                    Hash('a'), Hash('b'), Hash('c'), 1, contentHash,
                    0, (uint)nodes.Length, (uint)children.Length, 0, (uint)debug.Length,
                    0, 4, 4, 1, true);
                return new CompiledProgram(
                    header, nodes, children, System.Array.Empty<uint>(), System.Array.Empty<uint>(),
                    System.Array.Empty<CompiledBlackboardSlotRecord>(), System.Array.Empty<CompiledObserverRecord>(),
                    System.Array.Empty<uint>(), System.Array.Empty<byte>(), System.Array.Empty<byte>(), debug);
            }

            private static CompiledHash Hash(char value) => new CompiledHash(new string(value, 64));
        }
    }
}
