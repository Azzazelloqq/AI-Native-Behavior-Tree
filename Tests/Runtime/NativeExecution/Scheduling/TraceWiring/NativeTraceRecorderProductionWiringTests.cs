using AIBT.Runtime.Scheduling;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.NativeExecution.Scheduling.TraceWiring
{
    /// <summary>
    /// P7-007: proves <see cref="NativeTraceRecorderV1"/>, wired into
    /// <see cref="SchedulingPolicyDriver.TryRunImmediate"/> per <c>ADR-P6-015</c>, is (a) purely
    /// additive -- attaching it never changes scheduling decisions -- and (b) produces real,
    /// correctly ordered <see cref="NativeTraceRecordV1"/>s on a real <see cref="NativeTraceChannelOwnerV1"/>
    /// for a real, multi-update, multi-node compiled tree's real execution. Read-back through the
    /// unmodified <see cref="AIBT.Editor.Debugger.NativeExecutionDebuggerSession"/>/
    /// <see cref="AIBT.Editor.Trace.TraceTimelineModel"/> (this assembly has no reference to
    /// <c>AIBT.Editor</c>, so it cannot exercise them directly) was verified live via Unity MCP
    /// against the same channel these tests validate at the raw-snapshot level -- see
    /// <c>Planning~/Evidence/P7-007/README.md</c>.
    /// </summary>
    public sealed class NativeTraceRecorderProductionWiringTests
    {
        [Test]
        public void RecorderAttachment_ProducesBitIdenticalSchedulingResults()
        {
            var leafStatusWithout = new[] { NodeStatus.Success, NodeStatus.Success, NodeStatus.Success };
            var leafStatusWith = new[] { NodeStatus.Success, NodeStatus.Success, NodeStatus.Success };

            Assert.That(SchedulingPolicyDriver.TryCreateAgents(Fixture.CreateTwoLeafProgram(), Fixture.NodeKinds, 1, Allocator.Persistent, out var without, out var createFailureA), Is.True, createFailureA.Code.ToString());
            Assert.That(SchedulingPolicyDriver.TryCreateAgents(Fixture.CreateTwoLeafProgram(), Fixture.NodeKinds, 1, Allocator.Persistent, out var with, out var createFailureB), Is.True, createFailureB.Code.ToString());

            Assert.That(NativeTraceChannelOwnerV1.TryCreate(
                new NativeTraceChannelCapacityV1(64, 0, 0, 256), NativeTraceLevelV1.Detailed,
                new TreeInstanceId(1), workerOrdinal: 0, Allocator.Persistent, out var owner, out var channelFailure), Is.True, channelFailure.Code.ToString());

            try
            {
                var recorder = new NativeTraceRecorderV1(owner, Fixture.SemanticHash, treeInstanceId: 1);

                Assert.That(SchedulingPolicyDriver.TryRunImmediate(without, 1, leafStatusWithout, out var stepsWithout, out var runFailureA), Is.True, runFailureA.Code.ToString());
                Assert.That(SchedulingPolicyDriver.TryRunImmediate(with, 1, leafStatusWith, new[] { recorder }, out var stepsWith, out var runFailureB), Is.True, runFailureB.Code.ToString());

                Assert.That(stepsWith, Is.EqualTo(stepsWithout), "attaching a recorder must not change the number of scheduling steps.");
                Assert.That(with[0].TerminalResult, Is.EqualTo(without[0].TerminalResult));
            }
            finally
            {
                owner.TryDispose(out _);
                foreach (var agent in without) agent.Dispose();
                foreach (var agent in with) agent.Dispose();
            }
        }

        [Test]
        public void RecorderAttachment_ProducesRealReadableTraceRecordsAcrossTwoUpdates()
        {
            Assert.That(SchedulingPolicyDriver.TryCreateAgents(Fixture.CreateTwoLeafProgram(), Fixture.NodeKinds, 1, Allocator.Persistent, out var agents, out var createFailure), Is.True, createFailure.Code.ToString());
            Assert.That(NativeTraceChannelOwnerV1.TryCreate(
                new NativeTraceChannelCapacityV1(64, 0, 0, 256), NativeTraceLevelV1.Detailed,
                new TreeInstanceId(1), workerOrdinal: 0, Allocator.Persistent, out var owner, out var channelFailure), Is.True, channelFailure.Code.ToString());

            try
            {
                var recorder = new NativeTraceRecorderV1(owner, Fixture.SemanticHash, treeInstanceId: 1);

                // Update 1: leaf "a" (index 1) stays Running -> the tick ends Waiting, not Completed.
                var leafStatus = new[] { NodeStatus.Success, NodeStatus.Running, NodeStatus.Success };
                Assert.That(SchedulingPolicyDriver.TryRunImmediate(agents, 1, leafStatus, new[] { recorder }, out _, out var runFailure1), Is.True, runFailure1.Code.ToString());
                Assert.That(agents[0].TerminalResult, Is.Null, "leaf \"a\" is still Running -- the tree must not have completed yet.");

                // Update 2: leaf "a" now succeeds -> leaf "b" runs -> the root sequence completes.
                leafStatus[1] = NodeStatus.Success;
                Assert.That(SchedulingPolicyDriver.TryRunImmediate(agents, 2, leafStatus, new[] { recorder }, out _, out var runFailure2), Is.True, runFailure2.Code.ToString());
                Assert.That(agents[0].TerminalResult, Is.EqualTo(NodeStatus.Success));

                Assert.That(owner.TryGetSnapshot(out var snapshot, out var snapshotFailure), Is.True, snapshotFailure.Code.ToString());
                Assert.That(snapshot.IsFaulted, Is.False);
                Assert.That(snapshot.DroppedCount, Is.EqualTo(0ul));
                Assert.That(snapshot.RecordCount, Is.GreaterThan(0u));

                var records = new NativeTraceRecordV1[snapshot.RecordCount];
                for (var index = 0; index < snapshot.RecordCount; index++) records[index] = snapshot.Records[index];

                Assert.That(records[0].Kind, Is.EqualTo(NativeTraceEventKindV1.UpdateStarted));
                Assert.That(records[0].UpdateId, Is.EqualTo(1ul));
                Assert.That(records[records.Length - 1].Kind, Is.EqualTo(NativeTraceEventKindV1.UpdateCompleted));
                Assert.That(records[records.Length - 1].UpdateId, Is.EqualTo(2ul));

                Assert.That(HasNodeEvent(records, NativeTraceEventKindV1.NodeEntered, 1u), Is.True, "leaf \"a\" must have entered.");
                Assert.That(HasNodeEvent(records, NativeTraceEventKindV1.NodeExited, 1u), Is.True, "leaf \"a\" must have exited once it succeeded.");
                Assert.That(HasNodeEvent(records, NativeTraceEventKindV1.NodeEntered, 2u), Is.True, "leaf \"b\" must have entered.");
                Assert.That(HasNodeEvent(records, NativeTraceEventKindV1.NodeExited, 2u), Is.True, "leaf \"b\" must have exited.");

                var rootExit = FindLast(records, NativeTraceEventKindV1.NodeExited, 0u);
                Assert.That(rootExit.HasValue, Is.True, "the root sequence's own completion must produce a NodeExited(0) record, folded from Completed.");
                Assert.That((rootExit.Value.OptionalFields & NativeTraceOptionalFieldsV1.ExitReason) != 0, Is.True);
                Assert.That(rootExit.Value.ExitReason, Is.EqualTo(NativeTraceNodeExitReasonV1.Success));

                // NodeEntered for leaf "a" must appear exactly once (Enter dispatch, not repeated on resume) despite spanning two updates.
                var enteredCount = 0;
                foreach (var record in records)
                {
                    if (record.Kind == NativeTraceEventKindV1.NodeEntered && record.RuntimeNodeIndex == 1u) enteredCount++;
                }
                Assert.That(enteredCount, Is.EqualTo(1));
            }
            finally
            {
                owner.TryDispose(out _);
                foreach (var agent in agents) agent.Dispose();
            }
        }

        private static bool HasNodeEvent(NativeTraceRecordV1[] records, NativeTraceEventKindV1 kind, uint nodeIndex)
        {
            foreach (var record in records)
            {
                if (record.Kind == kind && record.RuntimeNodeIndex == nodeIndex) return true;
            }

            return false;
        }

        private static NativeTraceRecordV1? FindLast(NativeTraceRecordV1[] records, NativeTraceEventKindV1 kind, uint nodeIndex)
        {
            for (var index = records.Length - 1; index >= 0; index--)
            {
                if (records[index].Kind == kind && records[index].RuntimeNodeIndex == nodeIndex) return records[index];
            }

            return null;
        }

        private static class Fixture
        {
            internal static NativeHash256V1 SemanticHash => new NativeHash256V1(new CompiledHash(new string('e', 64)));

            internal static NativeLifecycleNodeKindV1[] NodeKinds => new[]
            {
                NativeLifecycleNodeKindV1.MemorySequence,
                NativeLifecycleNodeKindV1.GeneratedLeaf,
                NativeLifecycleNodeKindV1.GeneratedLeaf,
            };

            /// <summary>A three-node program: root memory-sequence with two generated-leaf children ("a", "b"). No blackboard, no config.</summary>
            internal static CompiledProgram CreateTwoLeafProgram()
            {
                var nodes = new[]
                {
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1, 0, 4, 4, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 2), CompiledNodeFlags.BurstDomain, 0, default, default),
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("test.leaf.a"), 1, 0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                        default, CompiledNodeFlags.BurstDomain, 1, default, default),
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("test.leaf.b"), 1, 0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                        default, CompiledNodeFlags.BurstDomain, 2, default, default),
                };
                var children = new uint[] { 1, 2 };
                var debug = new[]
                {
                    new CompiledDebugMapEntry(0, new NodeId("root"), "/tree/root"),
                    new CompiledDebugMapEntry(1, new NodeId("a"), "/tree/a"),
                    new CompiledDebugMapEntry(2, new NodeId("b"), "/tree/b"),
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
