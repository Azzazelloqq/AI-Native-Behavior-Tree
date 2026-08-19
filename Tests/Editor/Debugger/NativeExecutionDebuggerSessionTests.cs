using System;
using AIBT.Editor.Debugger;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Editor.Debugger
{
    /// <summary>
    /// P3-010's card requires attaching to "a running native (Burst) executor," but no production
    /// Play-mode host exists anywhere in AIBT to run one against (see this card's evidence README).
    /// Per the accepted scope narrowing, these tests drive a real bounded
    /// <see cref="NativeTraceChannelOwnerV1"/> through a real Burst job (mirroring the existing
    /// production <c>NativeChannelsBurstProbeJobV1</c> compile-proof pattern) that this test file
    /// owns end-to-end, then attach <see cref="NativeExecutionDebuggerSession"/> to it exactly as a
    /// future caller would attach to a real running pass's channel.
    /// </summary>
    public sealed class NativeExecutionDebuggerSessionTests
    {
        [Test]
        public void AttachReadsStepHistoryAndDiagnosticsFromTheBoundedChannel()
        {
            using var scenario = new Scenario();
            scenario.RunPass(updateId: 1, includeExit: true);

            var session = new NativeExecutionDebuggerSession();
            session.Attach(scenario.Owner);

            Assert.That(session.TryReadTrace(out var view, out var failure), Is.True, failure.Code.ToString());
            Assert.That(view.StepHistory.Count, Is.EqualTo(6));
            Assert.That(view.DiagnosticEvents.Count, Is.EqualTo(1));
            Assert.That(view.ActiveNodeIndices, Is.Empty, "the node entered and exited within this same snapshot");
            Assert.That(view.IsFaulted, Is.False);
        }

        [Test]
        public void ANodeEnteredWithoutAMatchingExitIsReportedActive()
        {
            using var scenario = new Scenario();
            scenario.RunPass(updateId: 1, includeExit: false);

            var session = new NativeExecutionDebuggerSession();
            session.Attach(scenario.Owner);

            Assert.That(session.TryReadTrace(out var view, out _), Is.True);
            Assert.That(view.ActiveNodeIndices, Is.EquivalentTo(new uint[] { Scenario.NodeIndex }));
        }

        [Test]
        public void TryReadTraceFailsCleanlyWhenNotAttached()
        {
            var session = new NativeExecutionDebuggerSession();
            Assert.That(session.IsAttached, Is.False);
            Assert.That(session.TryReadTrace(out _, out _), Is.False);
        }

        /// <summary>
        /// Acceptance: "no measurable change in per-tick allocation... for Phase 2's initialized
        /// native paths." Record construction (managed, necessarily allocating) happens before the
        /// measured block; only the mechanical acquire/schedule/complete/release sequence -- the
        /// part an attached debugger's presence could plausibly affect -- is measured, matching how
        /// <c>NativeExecutionAllocationTests</c> isolates its own measured calls.
        /// </summary>
        [Test]
        public void AttachingAndReadingBetweenPassesAddsNoManagedAllocationToNativeExecution()
        {
            using var scenario = new Scenario();
            var session = new NativeExecutionDebuggerSession();
            session.Attach(scenario.Owner);

            // Warm-up: first job scheduling can allocate for job-system/Burst bookkeeping.
            using (var warmup = scenario.PrepareRecords(updateId: 1, includeExit: true))
            {
                Assert.That(scenario.RunPreparedPass(warmup), Is.True);
            }
            Assert.That(session.TryReadTrace(out _, out _), Is.True);
            Assert.That(scenario.Owner.TryReset(out var resetFailure), Is.True, resetFailure.Code.ToString());

            using var measured = scenario.PrepareRecords(updateId: 2, includeExit: true);
            var passSucceeded = false;
            Assert.That(() => { passSucceeded = scenario.RunPreparedPass(measured); }, GcAllocIs.Not.AllocatingGCMemory());
            Assert.That(passSucceeded, Is.True);

            Assert.That(session.TryReadTrace(out var view, out var failure), Is.True, failure.Code.ToString());
            Assert.That(view.StepHistory.Count, Is.EqualTo(6));
        }

        /// <summary>Acceptance: "detaching mid-run leaves the native executor in an unaffected state."</summary>
        [Test]
        public void DetachingMidRunLeavesNativeOutputIdenticalToRunningWithoutADebugger()
        {
            using var withoutDebugger = new Scenario();
            withoutDebugger.RunPass(updateId: 1, includeExit: true);
            Assert.That(withoutDebugger.Owner.TryGetSnapshot(out var expected, out _), Is.True);

            using var withDebugger = new Scenario();
            var session = new NativeExecutionDebuggerSession();
            session.Attach(withDebugger.Owner);
            withDebugger.RunPass(updateId: 1, includeExit: true);
            Assert.That(session.TryReadTrace(out _, out _), Is.True);
            session.Detach();

            Assert.That(withDebugger.Owner.TryGetSnapshot(out var actual, out _), Is.True);
            Assert.That(actual.RecordCount, Is.EqualTo(expected.RecordCount));
            for (var index = 0u; index < expected.RecordCount; index++)
            {
                Assert.That(actual.Records[(int)index].Kind, Is.EqualTo(expected.Records[(int)index].Kind), "record " + index);
                Assert.That(actual.Records[(int)index].Sequence, Is.EqualTo(expected.Records[(int)index].Sequence), "record " + index);
                Assert.That(actual.Records[(int)index].RuntimeNodeIndex, Is.EqualTo(expected.Records[(int)index].RuntimeNodeIndex), "record " + index);
            }
        }

        [BurstCompile]
        private struct AppendManyJob : IJob
        {
            public NativeTraceWriterV1 Writer;
            [ReadOnly] public NativeArray<NativeTraceRecordV1> Records;

            public void Execute()
            {
                for (var index = 0; index < Records.Length; index++)
                {
                    Writer.TryAppend(Records[index]);
                }
            }
        }

        /// <summary>Owns one real bounded native trace channel and a Burst job that writes real records into it.</summary>
        private sealed class Scenario : IDisposable
        {
            internal const uint NodeIndex = 1;
            private const ulong TreeInstanceIdValue = 42;
            private const uint WorkerOrdinal = 0;
            private static readonly NativeHash256V1 SemanticHash = new NativeHash256V1(
                new CompiledHash(StableHash.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("p3-010-debugger-probe"))));

            private ulong _nextSequence = 1;

            internal Scenario()
            {
                var capacity = new NativeTraceChannelCapacityV1(
                    recordCapacity: 32, payloadCapacity: 0, maximumPayloadBytes: 0, emissionCapacity: 32);
                Assert.That(NativeTraceChannelOwnerV1.TryCreate(
                    capacity, NativeTraceLevelV1.Detailed, new TreeInstanceId(TreeInstanceIdValue), WorkerOrdinal,
                    Allocator.Persistent, out var owner, out var failure), Is.True, failure.Code.ToString());
                Owner = owner;
            }

            internal NativeTraceChannelOwnerV1 Owner { get; }

            /// <summary>Builds the record set for one pass. Managed allocation; call before measuring.</summary>
            internal NativeArray<NativeTraceRecordV1> PrepareRecords(ulong updateId, bool includeExit)
            {
                var list = new System.Collections.Generic.List<NativeTraceRecordV1>
                {
                    Make(NativeTraceEventKindV1.UpdateStarted, updateId),
                    MakeForNode(NativeTraceEventKindV1.NodeEntered, updateId),
                    MakeTicked(updateId),
                    MakeDiagnostic(updateId),
                };
                if (includeExit)
                {
                    list.Add(MakeExit(updateId));
                }

                list.Add(Make(NativeTraceEventKindV1.UpdateCompleted, updateId));
                return new NativeArray<NativeTraceRecordV1>(list.ToArray(), Allocator.Persistent);
            }

            /// <summary>Acquire/schedule/complete/release only -- no managed allocation of its own.</summary>
            internal bool RunPreparedPass(NativeArray<NativeTraceRecordV1> records)
            {
                if (!Owner.TryAcquireWriter(out var lease, out _))
                {
                    return false;
                }

                var job = new AppendManyJob { Writer = lease.Writer, Records = records };
                var handle = job.Schedule();
                if (!Owner.TryRegisterDependency(lease, handle, out _))
                {
                    handle.Complete();
                    return false;
                }

                handle.Complete();
                return Owner.TryReleaseWriter(lease, out _);
            }

            internal void RunPass(ulong updateId, bool includeExit)
            {
                using var records = PrepareRecords(updateId, includeExit);
                Assert.That(RunPreparedPass(records), Is.True);
            }

            public void Dispose()
            {
                if (Owner.State != NativeOwnerStateV1.Disposed)
                {
                    Owner.TryDispose(out _);
                }
            }

            private NativeTraceRecordV1 Make(NativeTraceEventKindV1 kind, ulong updateId) => new NativeTraceRecordV1
            {
                TraceFormatVersion = NativeTraceRecordV1.FormatVersion,
                Phase = NativeUpdatePhaseV1.Execute,
                UpdateId = updateId,
                SnapshotRevision = updateId,
                TreeSemanticHash = SemanticHash,
                TreeInstanceId = TreeInstanceIdValue,
                Sequence = _nextSequence++,
                WorkerOrdinal = WorkerOrdinal,
                Kind = kind,
                RuntimeNodeIndex = CompiledIndex.Invalid,
                DebugIdentityIndex = CompiledIndex.Invalid,
                SourceNodeIndex = CompiledIndex.Invalid,
            };

            private NativeTraceRecordV1 MakeForNode(NativeTraceEventKindV1 kind, ulong updateId)
            {
                var record = Make(kind, updateId);
                record.OptionalFields |= NativeTraceOptionalFieldsV1.RuntimeNode;
                record.RuntimeNodeIndex = NodeIndex;
                return record;
            }

            private NativeTraceRecordV1 MakeTicked(ulong updateId)
            {
                var record = MakeForNode(NativeTraceEventKindV1.NodeTicked, updateId);
                record.OptionalFields |= NativeTraceOptionalFieldsV1.Status;
                record.Status = NodeStatus.Success;
                return record;
            }

            private NativeTraceRecordV1 MakeExit(ulong updateId)
            {
                var record = MakeForNode(NativeTraceEventKindV1.NodeExited, updateId);
                record.OptionalFields |= NativeTraceOptionalFieldsV1.ExitReason;
                record.ExitReason = NativeTraceNodeExitReasonV1.Success;
                return record;
            }

            private NativeTraceRecordV1 MakeDiagnostic(ulong updateId)
            {
                var record = Make(NativeTraceEventKindV1.DiagnosticRaised, updateId);
                record.OptionalFields |= NativeTraceOptionalFieldsV1.DiagnosticCode;
                record.DiagnosticCodeNumber = 1001;
                return record;
            }
        }
    }
}
