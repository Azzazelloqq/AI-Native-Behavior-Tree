using AIBT.Runtime.Scheduling;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.Benchmarking
{
    /// <summary>
    /// P4-001: proves <see cref="SchedulingPolicyDriver"/> itself is correct (drives N agents to
    /// completion under each of the three accepted fixed policies without error, and introduces
    /// no extra managed allocation of its own) against a minimal hand-built two-node program --
    /// not a scenario-result assertion, per this card's own scope split with
    /// <c>Benchmarks~/Phase4/Scheduling/</c>.
    /// </summary>
    public sealed class SchedulingPolicyDriverTests
    {
        private const int AgentCount = 8;

        [Test]
        public void ImmediateDrivesEveryAgentToCompletionWithTheConfiguredLeafStatus()
        {
            var program = Fixture.CreateSingleLeafProgram();
            var kinds = Fixture.NodeKinds;
            var leafStatus = new[] { NodeStatus.Running, NodeStatus.Success };

            Assert.That(SchedulingPolicyDriver.TryCreateAgents(program, kinds, AgentCount, Allocator.Persistent, out var agents, out var createFailure), Is.True, createFailure.Code.ToString());
            try
            {
                Assert.That(SchedulingPolicyDriver.TryRunImmediate(agents, 1, leafStatus, out var steps, out var runFailure), Is.True, runFailure.Code.ToString());
                Assert.That(steps, Is.GreaterThan(0UL));
                foreach (var agent in agents)
                {
                    Assert.That(agent.TerminalResult, Is.EqualTo(NodeStatus.Success));
                }
            }
            finally
            {
                foreach (var agent in agents) agent.Dispose();
            }
        }

        [Test]
        public void BudgetedDrivesEveryAgentToCompletionAcrossMultipleSegments()
        {
            var program = Fixture.CreateSingleLeafProgram();
            var kinds = Fixture.NodeKinds;
            var leafStatus = new[] { NodeStatus.Running, NodeStatus.Success };

            Assert.That(SchedulingPolicyDriver.TryCreateAgents(program, kinds, AgentCount, Allocator.Persistent, out var agents, out var createFailure), Is.True, createFailure.Code.ToString());
            try
            {
                var budgetStates = new NativeBudgetStateV1[AgentCount];
                Assert.That(SchedulingPolicyDriver.TryRunBudgeted(agents, budgetStates, 1, stepLimit: 1, leafStatus, out var steps, out var runFailure), Is.True, runFailure.Code.ToString());
                Assert.That(steps, Is.GreaterThan(0UL));
                foreach (var agent in agents)
                {
                    Assert.That(agent.TerminalResult, Is.EqualTo(NodeStatus.Success));
                }
            }
            finally
            {
                foreach (var agent in agents) agent.Dispose();
            }
        }

        [Test]
        public void BatchedJobsSameFrameDrivesEveryAgentToCompletion()
        {
            var program = Fixture.CreateSingleLeafProgram();
            var kinds = Fixture.NodeKinds;
            var leafStatus = new[] { NodeStatus.Running, NodeStatus.Success };

            Assert.That(SchedulingPolicyDriver.TryCreateAgents(program, kinds, AgentCount, Allocator.Persistent, out var agents, out var createFailure), Is.True, createFailure.Code.ToString());
            try
            {
                Assert.That(SchedulingPolicyDriver.TryRunBatchedJobsSameFrame(agents, 1, batchSize: 4, leafStatus, Allocator.Persistent, out var steps, out var runFailure), Is.True, runFailure.Code.ToString());
                Assert.That(steps, Is.GreaterThan(0UL));
                foreach (var agent in agents)
                {
                    Assert.That(agent.TerminalResult, Is.EqualTo(NodeStatus.Success));
                }
            }
            finally
            {
                foreach (var agent in agents) agent.Dispose();
            }
        }

        [Test]
        public void ImmediateRunIntroducesNoManagedAllocationBeyondAgentConstruction()
        {
            var program = Fixture.CreateSingleLeafProgram();
            var kinds = Fixture.NodeKinds;
            var leafStatus = new[] { NodeStatus.Running, NodeStatus.Success };

            Assert.That(SchedulingPolicyDriver.TryCreateAgents(program, kinds, AgentCount, Allocator.Persistent, out var agents, out var createFailure), Is.True, createFailure.Code.ToString());
            try
            {
                var success = false;
                Assert.That(() =>
                {
                    success = SchedulingPolicyDriver.TryRunImmediate(agents, 1, leafStatus, out _, out _);
                }, GcAllocIs.Not.AllocatingGCMemory());
                Assert.That(success, Is.True);
            }
            finally
            {
                foreach (var agent in agents) agent.Dispose();
            }
        }

        private static class Fixture
        {
            internal static NativeLifecycleNodeKindV1[] NodeKinds => new[]
            {
                NativeLifecycleNodeKindV1.MemorySequence,
                NativeLifecycleNodeKindV1.GeneratedLeaf,
            };

            /// <summary>A two-node program: root memory-sequence with one generated-leaf child. No blackboard, no config.</summary>
            internal static CompiledProgram CreateSingleLeafProgram()
            {
                var nodes = new[]
                {
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1, 0, 4, 4, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 1), CompiledNodeFlags.BurstDomain, 0, default, default),
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("test.leaf"), 1, 0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                        default, CompiledNodeFlags.BurstDomain, 1, default, default),
                };
                var children = new uint[] { 1 };
                var debug = new[]
                {
                    new CompiledDebugMapEntry(0, new NodeId("root"), "/tree/root"),
                    new CompiledDebugMapEntry(1, new NodeId("leaf"), "/tree/leaf"),
                };

                var preliminary = BuildProgram(Hash('d'), nodes, children, debug);
                var contentHash = CompiledProgramContentHashV1.Compute(preliminary);
                return BuildProgram(contentHash, nodes, children, debug);
            }

            private static CompiledProgram BuildProgram(
                CompiledHash contentHash,
                CompiledNodeRecord[] nodes,
                uint[] children,
                CompiledDebugMapEntry[] debug)
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
