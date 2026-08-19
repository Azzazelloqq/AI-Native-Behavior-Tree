using System.Collections.Generic;
using AIBT.Editor.Debugger;
using AIBT.Editor.Trace;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Editor.Trace
{
    /// <summary>
    /// P3-011 acceptance criteria: scrubbing to a past step reproduces exactly the active-node
    /// state that step actually produced (verified independently against the raw channel records,
    /// not by re-trusting <see cref="TraceTimelineModel"/>'s own replay), and the view explicitly
    /// reports a degraded/dropped state rather than silently truncating.
    /// </summary>
    public sealed class TraceTimelineModelTests
    {
        [Test]
        public void ScrubbingEveryStepReproducesTheActiveSetTheRawChannelRecordsActuallyProduced()
        {
            using var scenario = new Scenario(recordCapacity: 64);
            // Enter(A), Enter(B), Exit(B), Enter(C), Exit(C), Exit(A) -- a nested-then-sequential shape.
            var records = new List<NativeTraceRecordV1>
            {
                scenario.Make(NativeTraceEventKindV1.NodeEntered, node: 10),
                scenario.Make(NativeTraceEventKindV1.NodeEntered, node: 11),
                scenario.MakeExit(node: 11),
                scenario.Make(NativeTraceEventKindV1.NodeEntered, node: 12),
                scenario.MakeExit(node: 12),
                scenario.MakeExit(node: 10),
            };
            scenario.Write(records);

            var session = new NativeExecutionDebuggerSession();
            session.Attach(scenario.Owner);
            Assert.That(session.TryReadTrace(out var view, out var failure), Is.True, failure.Code.ToString());

            var model = TraceTimelineModel.Build(view);
            Assert.That(model.Steps.Count, Is.EqualTo(records.Count));
            Assert.That(model.HasDroppedEvents, Is.False);

            // Independent oracle: replay the same raw records by hand and compare at every step.
            var expectedActive = new List<uint>();
            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];
                if (record.Kind == NativeTraceEventKindV1.NodeEntered) expectedActive.Add(record.RuntimeNodeIndex);
                else if (record.Kind == NativeTraceEventKindV1.NodeExited) expectedActive.Remove(record.RuntimeNodeIndex);

                var actual = model.ActiveRuntimeNodeIndicesAtStep(index);
                Assert.That(actual, Is.EquivalentTo(expectedActive), "step " + index);
            }
        }

        [Test]
        public void DiagnosticEventsAreCorrelatedToTheStepAndActiveNodesThatProducedThem()
        {
            using var scenario = new Scenario(recordCapacity: 64);
            var records = new List<NativeTraceRecordV1>
            {
                scenario.Make(NativeTraceEventKindV1.NodeEntered, node: 5),
                scenario.MakeDiagnostic(1001),
                scenario.MakeExit(node: 5),
            };
            scenario.Write(records);

            var session = new NativeExecutionDebuggerSession();
            session.Attach(scenario.Owner);
            Assert.That(session.TryReadTrace(out var view, out _), Is.True);

            var model = TraceTimelineModel.Build(view);
            Assert.That(model.Diagnostics.Count, Is.EqualTo(1));
            var diagnostic = model.Diagnostics[0];
            Assert.That(diagnostic.StepIndex, Is.EqualTo(1));
            Assert.That(diagnostic.Record.DiagnosticCodeNumber, Is.EqualTo(1001));
            Assert.That(diagnostic.ActiveRuntimeNodeIndicesAtStep, Is.EquivalentTo(new uint[] { 5 }));
        }

        /// <summary>Acceptance: "degrades explicitly... rather than silently showing a truncated trace as complete."</summary>
        [Test]
        public void OverflowingTheBoundedChannelIsReportedAsDroppedRatherThanATruncatedCompleteTrace()
        {
            // ordinaryCapacity = recordCapacity - 1 = 3; writing 6 records forces real drops.
            using var scenario = new Scenario(recordCapacity: 4);
            var records = new List<NativeTraceRecordV1>();
            for (uint node = 0; node < 6; node++)
            {
                records.Add(scenario.Make(NativeTraceEventKindV1.NodeEntered, node));
            }
            scenario.Write(records);

            var session = new NativeExecutionDebuggerSession();
            session.Attach(scenario.Owner);
            Assert.That(session.TryReadTrace(out var view, out var failure), Is.True, failure.Code.ToString());

            var model = TraceTimelineModel.Build(view);
            Assert.That(model.HasDroppedEvents, Is.True, "the channel should have dropped at least one record");
            Assert.That(model.DroppedCount, Is.GreaterThan(0UL));
            Assert.That(model.Steps.Count, Is.LessThan(records.Count), "the view must not silently claim a complete trace");
        }

        [Test]
        public void EmptyModelHasNoStepsAndNoDroppedEvents()
        {
            var model = TraceTimelineModel.Empty;
            Assert.That(model.Steps, Is.Empty);
            Assert.That(model.Diagnostics, Is.Empty);
            Assert.That(model.HasDroppedEvents, Is.False);
            Assert.That(model.ActiveRuntimeNodeIndicesAtStep(0), Is.Empty);
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

        private sealed class Scenario : System.IDisposable
        {
            private const ulong TreeInstanceIdValue = 99;
            private const uint WorkerOrdinal = 0;
            private static readonly NativeHash256V1 SemanticHash = new NativeHash256V1(
                new CompiledHash(StableHash.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("p3-011-trace-view-probe"))));

            private ulong _nextSequence = 1;

            internal Scenario(uint recordCapacity)
            {
                var capacity = new NativeTraceChannelCapacityV1(
                    recordCapacity: recordCapacity, payloadCapacity: 0, maximumPayloadBytes: 0, emissionCapacity: 64);
                Assert.That(NativeTraceChannelOwnerV1.TryCreate(
                    capacity, NativeTraceLevelV1.Detailed, new TreeInstanceId(TreeInstanceIdValue), WorkerOrdinal,
                    Allocator.Persistent, out var owner, out var failure), Is.True, failure.Code.ToString());
                Owner = owner;
            }

            internal NativeTraceChannelOwnerV1 Owner { get; }

            internal NativeTraceRecordV1 Make(NativeTraceEventKindV1 kind, uint node) => new NativeTraceRecordV1
            {
                TraceFormatVersion = NativeTraceRecordV1.FormatVersion,
                Phase = NativeUpdatePhaseV1.Execute,
                UpdateId = 1,
                SnapshotRevision = 1,
                TreeSemanticHash = SemanticHash,
                TreeInstanceId = TreeInstanceIdValue,
                Sequence = _nextSequence++,
                WorkerOrdinal = WorkerOrdinal,
                Kind = kind,
                OptionalFields = NativeTraceOptionalFieldsV1.RuntimeNode,
                RuntimeNodeIndex = node,
                DebugIdentityIndex = CompiledIndex.Invalid,
                SourceNodeIndex = CompiledIndex.Invalid,
            };

            internal NativeTraceRecordV1 MakeExit(uint node)
            {
                var record = Make(NativeTraceEventKindV1.NodeExited, node);
                record.OptionalFields |= NativeTraceOptionalFieldsV1.ExitReason;
                record.ExitReason = NativeTraceNodeExitReasonV1.Success;
                return record;
            }

            internal NativeTraceRecordV1 MakeDiagnostic(ushort code)
            {
                var record = new NativeTraceRecordV1
                {
                    TraceFormatVersion = NativeTraceRecordV1.FormatVersion,
                    Phase = NativeUpdatePhaseV1.Execute,
                    UpdateId = 1,
                    SnapshotRevision = 1,
                    TreeSemanticHash = SemanticHash,
                    TreeInstanceId = TreeInstanceIdValue,
                    Sequence = _nextSequence++,
                    WorkerOrdinal = WorkerOrdinal,
                    Kind = NativeTraceEventKindV1.DiagnosticRaised,
                    OptionalFields = NativeTraceOptionalFieldsV1.DiagnosticCode,
                    RuntimeNodeIndex = CompiledIndex.Invalid,
                    DebugIdentityIndex = CompiledIndex.Invalid,
                    SourceNodeIndex = CompiledIndex.Invalid,
                    DiagnosticCodeNumber = code,
                };
                return record;
            }

            internal void Write(List<NativeTraceRecordV1> records)
            {
                Assert.That(Owner.TryAcquireWriter(out var lease, out var acquireFailure), Is.True, acquireFailure.Code.ToString());
                using var recordArray = new NativeArray<NativeTraceRecordV1>(records.ToArray(), Allocator.TempJob);
                var job = new AppendManyJob { Writer = lease.Writer, Records = recordArray };
                var handle = job.Schedule();
                Assert.That(Owner.TryRegisterDependency(lease, handle, out var dependencyFailure), Is.True, dependencyFailure.Code.ToString());
                handle.Complete();
                Assert.That(Owner.TryReleaseWriter(lease, out var releaseFailure), Is.True, releaseFailure.Code.ToString());
            }

            public void Dispose()
            {
                if (Owner.State != NativeOwnerStateV1.Disposed)
                {
                    Owner.TryDispose(out _);
                }
            }
        }
    }
}
