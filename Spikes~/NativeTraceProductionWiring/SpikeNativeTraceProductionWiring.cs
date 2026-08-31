using System.Text;
using AIBT.Burst;
using AIBT.Editor.Debugger;
using AIBT.Editor.Trace;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Editor.NativeTraceProductionWiringSpike
{
    /// <summary>
    /// P6-015 disposable spike. Proves an external recorder -- co-located with whatever already
    /// drives <see cref="NativeLifecycleMachineV1.TryAdvance"/> (mirroring
    /// <c>SchedulingPolicyDriver.TryHandleStep</c>'s own step switch, never a change inside the
    /// machine itself) -- can translate a real compiled tree's real lifecycle steps into
    /// <see cref="NativeTraceRecordV1"/> writes on a real, unmodified <see cref="NativeTraceChannelOwnerV1"/>,
    /// readable back correctly through the real, unmodified <see cref="NativeExecutionDebuggerSession"/>/
    /// <see cref="TraceTimelineModel"/>. Archived to <c>Spikes~/NativeTraceProductionWiring/</c> once proven.
    /// </summary>
    public sealed class SpikeNativeTraceProductionWiring
    {
        [Test]
        public void SimpleSequence_RealExecutionProducesReadableTraceViaUnmodifiedReadSide()
        {
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.memory-sequence"), 0, 4, 0, 2),
                Node(StableHash.Fnv1A64("aibt.tests.first"), 4, 0, 0, 0),
                Node(StableHash.Fnv1A64("aibt.tests.second"), 4, 0, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1, 2 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.MemorySequence),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.GeneratedLeaf),
                new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(4, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(3, Allocator.Persistent);
            using var generations = new NativeArray<uint>(3, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);

            Assert.That(NativeLifecycleMachineV1.TryCreate(
                nodes, children, bindings, memory, frames, generations, control,
                out var machine, out var failure), Is.True, failure.Code.ToString());

            Assert.That(NativeTraceChannelOwnerV1.TryCreate(
                new NativeTraceChannelCapacityV1(32, 0, 0, 32),
                NativeTraceLevelV1.Detailed,
                new TreeInstanceId(777),
                workerOrdinal: 0,
                Allocator.Persistent,
                out var owner,
                out var channelFailure), Is.True, channelFailure.Code.ToString());

            try
            {
                var recorder = new SpikeTraceRecorder(owner, treeInstanceId: 777);

                Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());
                recorder.RecordUpdateStarted(1);

                DriveAndRecord(ref machine, recorder, 1, NativeLifecycleStepKindV1.CompositeEntered, 0);
                DriveAndRecord(ref machine, recorder, 1, NativeLifecycleStepKindV1.ChildSelected, 0);
                var enterFirst = DriveAndRecord(ref machine, recorder, 1, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Enter);
                CompleteAndRecord(ref machine, recorder, enterFirst, NodeStatus.Running);
                var tickFirst = DriveAndRecord(ref machine, recorder, 1, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Tick);
                CompleteAndRecord(ref machine, recorder, tickFirst, NodeStatus.Success);
                var exitFirst = DriveAndRecord(ref machine, recorder, 1, NativeLifecycleStepKindV1.DispatchRequired, 1, BurstCallbackPhase.Exit);
                CompleteAndRecord(ref machine, recorder, exitFirst, NodeStatus.Success);
                DriveAndRecord(ref machine, recorder, 1, NativeLifecycleStepKindV1.ChildAccepted, 0);
                DriveAndRecord(ref machine, recorder, 1, NativeLifecycleStepKindV1.ChildSelected, 0);
                var enterSecond = DriveAndRecord(ref machine, recorder, 1, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Enter);
                CompleteAndRecord(ref machine, recorder, enterSecond, NodeStatus.Running);
                var tickSecond = DriveAndRecord(ref machine, recorder, 1, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Tick);
                CompleteAndRecord(ref machine, recorder, tickSecond, NodeStatus.Running);
                DriveAndRecord(ref machine, recorder, 1, NativeLifecycleStepKindV1.Waiting, 2);
                recorder.RecordUpdateEnded(1, completed: false, hasRootStatus: false, rootStatus: default);

                Assert.That(machine.TryBeginUpdate(2, out failure), Is.True, failure.Code.ToString());
                recorder.RecordUpdateStarted(2);
                var resumedTick = DriveAndRecord(ref machine, recorder, 2, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Tick);
                CompleteAndRecord(ref machine, recorder, resumedTick, NodeStatus.Success);
                var exitSecond = DriveAndRecord(ref machine, recorder, 2, NativeLifecycleStepKindV1.DispatchRequired, 2, BurstCallbackPhase.Exit);
                CompleteAndRecord(ref machine, recorder, exitSecond, NodeStatus.Success);
                DriveAndRecord(ref machine, recorder, 2, NativeLifecycleStepKindV1.ChildAccepted, 0);
                // CompositeExited for the ROOT is deliberately not recorded directly here -- the
                // recorder defers it and folds it into the following Completed step, which is the
                // only place a root's own exit status (RootStatus) is actually available. See
                // RecordStep's handling of CompositeExited-at-root plus Completed below.
                DriveAndRecord(ref machine, recorder, 2, NativeLifecycleStepKindV1.CompositeExited, 0);
                var completedStep = DriveAndRecord(ref machine, recorder, 2, NativeLifecycleStepKindV1.Completed, 0);
                recorder.RecordUpdateEnded(2, completed: true, hasRootStatus: completedStep.HasRootStatus, rootStatus: completedStep.RootStatus);

                recorder.ReleaseWriter();

                var session = new NativeExecutionDebuggerSession();
                session.Attach(owner);
                Assert.That(session.TryReadTrace(out var view, out var readFailure), Is.True, readFailure.Code.ToString());
                Assert.That(view.IsFaulted, Is.False);
                Assert.That(view.DroppedCount, Is.EqualTo(0ul));
                Assert.That(view.ActiveNodeIndices, Is.Empty, "Tree fully completed -- no node should remain active.");

                var timeline = TraceTimelineModel.Build(view);
                Assert.That(timeline.IsFaulted, Is.False);
                Assert.That(timeline.HasDroppedEvents, Is.False);
                Assert.That(timeline.Steps.Count, Is.GreaterThan(0));

                var kinds = new System.Collections.Generic.List<NativeTraceEventKindV1>();
                foreach (var step in timeline.Steps) kinds.Add(step.Record.Kind);

                Assert.That(kinds[0], Is.EqualTo(NativeTraceEventKindV1.UpdateStarted));
                CollectionAssert.Contains(kinds, NativeTraceEventKindV1.NodeEntered);
                CollectionAssert.Contains(kinds, NativeTraceEventKindV1.NodeTicked);
                CollectionAssert.Contains(kinds, NativeTraceEventKindV1.NodeExited);
                Assert.That(kinds[kinds.Count - 1], Is.EqualTo(NativeTraceEventKindV1.UpdateCompleted));

                // Root's own NodeExited (deferred/folded from CompositeExited+Completed) must carry
                // a real ExitReason -- proving the fold-in design actually recovers the status that
                // a bare CompositeExited step result cannot supply for a non-leaf node.
                var rootExit = timeline.Steps[timeline.Steps.Count - 2].Record;
                Assert.That(rootExit.Kind, Is.EqualTo(NativeTraceEventKindV1.NodeExited));
                Assert.That(rootExit.RuntimeNodeIndex, Is.EqualTo(0u));
                Assert.That((rootExit.OptionalFields & NativeTraceOptionalFieldsV1.ExitReason) != 0, Is.True);
                Assert.That(rootExit.ExitReason, Is.EqualTo(NativeTraceNodeExitReasonV1.Success));

                // Active-node-set replay sanity: after leaf 1's NodeEntered the active set must
                // include it, and after its NodeExited it must not -- proves TraceTimelineModel's
                // unmodified replay logic reproduces the real execution's active-node history from
                // records this spike's recorder produced (not a hand-authored fixture).
                var enteredLeaf1Step = FindStep(timeline, NativeTraceEventKindV1.NodeEntered, 1u);
                CollectionAssert.Contains(timeline.ActiveRuntimeNodeIndicesAtStep(enteredLeaf1Step), 1u);
                var exitedLeaf1Step = FindStep(timeline, NativeTraceEventKindV1.NodeExited, 1u);
                CollectionAssert.DoesNotContain(timeline.ActiveRuntimeNodeIndicesAtStep(exitedLeaf1Step), 1u);
            }
            finally
            {
                owner.TryDispose(out _);
            }
        }

        [Test]
        public void NestedComposite_CompositeExitedStepAloneCannotSupplyExitStatus_RealGapNotAssumed()
        {
            // root MemorySequence -> child MemorySequence -> one leaf (always Success).
            using var nodes = new NativeArray<NativeCompiledNodeRecordV1>(new[]
            {
                Node(StableHash.Fnv1A64("aibt.core.memory-sequence"), 0, 4, 0, 1),
                Node(StableHash.Fnv1A64("aibt.core.memory-sequence"), 4, 4, 1, 1),
                Node(StableHash.Fnv1A64("aibt.tests.leaf"), 8, 0, 0, 0),
            }, Allocator.Persistent);
            using var children = new NativeArray<uint>(new uint[] { 1, 2 }, Allocator.Persistent);
            using var bindings = new NativeArray<NativeLifecycleNodeBindingV1>(new[]
            {
                new NativeLifecycleNodeBindingV1(0, NativeLifecycleNodeKindV1.MemorySequence),
                new NativeLifecycleNodeBindingV1(1, NativeLifecycleNodeKindV1.MemorySequence),
                new NativeLifecycleNodeBindingV1(2, NativeLifecycleNodeKindV1.GeneratedLeaf),
            }, Allocator.Persistent);
            using var memory = new NativeArray<byte>(8, Allocator.Persistent);
            using var frames = new NativeArray<NativeFrameStateV1>(3, Allocator.Persistent);
            using var generations = new NativeArray<uint>(3, Allocator.Persistent);
            using var control = new NativeArray<NativeLifecycleControlV1>(1, Allocator.Persistent);

            Assert.That(NativeLifecycleMachineV1.TryCreate(
                nodes, children, bindings, memory, frames, generations, control,
                out var machine, out var failure), Is.True, failure.Code.ToString());
            Assert.That(machine.TryBeginUpdate(1, out failure), Is.True, failure.Code.ToString());

            Assert.That(machine.TryAdvance(out var s0, out failure), Is.True); // root CompositeEntered
            Assert.That(s0.Kind, Is.EqualTo(NativeLifecycleStepKindV1.CompositeEntered));
            Assert.That(machine.TryAdvance(out var s1, out failure), Is.True); // root ChildSelected
            Assert.That(s1.Kind, Is.EqualTo(NativeLifecycleStepKindV1.ChildSelected));
            Assert.That(machine.TryAdvance(out var s2, out failure), Is.True); // child CompositeEntered
            Assert.That(s2.Kind, Is.EqualTo(NativeLifecycleStepKindV1.CompositeEntered));
            Assert.That(s2.NodeIndex, Is.EqualTo(1u));
            Assert.That(machine.TryAdvance(out var s3, out failure), Is.True); // child ChildSelected
            Assert.That(s3.Kind, Is.EqualTo(NativeLifecycleStepKindV1.ChildSelected));
            Assert.That(machine.TryAdvance(out var enter, out failure), Is.True);
            Assert.That(machine.TryCompleteDispatch(enter.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True);
            Assert.That(machine.TryAdvance(out var tick, out failure), Is.True);
            Assert.That(machine.TryCompleteDispatch(tick.DispatchToken, BurstContextResult.Success, NodeStatus.Success, out failure), Is.True);
            Assert.That(machine.TryAdvance(out var exit, out failure), Is.True);
            Assert.That(machine.TryCompleteDispatch(exit.DispatchToken, BurstContextResult.Success, NodeStatus.Success, out failure), Is.True);
            Assert.That(machine.TryAdvance(out var childAccepted, out failure), Is.True); // child's ChildAccepted (terminal, Sequence -> Exiting)
            Assert.That(childAccepted.Kind, Is.EqualTo(NativeLifecycleStepKindV1.ChildAccepted));

            // This is the step under test: the NESTED (non-root) composite's own CompositeExited.
            Assert.That(machine.TryAdvance(out var nestedExit, out failure), Is.True);
            Assert.That(nestedExit.Kind, Is.EqualTo(NativeLifecycleStepKindV1.CompositeExited));
            Assert.That(nestedExit.NodeIndex, Is.EqualTo(1u));

            // Real, disclosed finding: NativeLifecycleStepResultV1 carries no status field at all for
            // CompositeExited (unlike Completed's HasRootStatus/RootStatus). An external recorder
            // driven only by TryAdvance's return value has no way to populate NodeExited's
            // ExitReason/Status for this node from this step alone -- confirmed here by construction,
            // not assumed. A future implementation card closing this gap would need either a small,
            // additive widening of NativeLifecycleStepResultV1 (mirroring Completed's own optional
            // status fields) or the driver's own separately-tracked child-result state -- both left
            // to that future card per this one's own Forbidden-changes clause.
            var recordWithoutStatus = SpikeTraceRecorder.BuildNodeExitedRecordFromStepAlone(nestedExit, treeInstanceId: 1, updateId: 1);
            Assert.That((recordWithoutStatus.OptionalFields & NativeTraceOptionalFieldsV1.ExitReason) == 0, Is.True,
                "Confirms the gap: no ExitReason can be derived from CompositeExited alone for a non-root composite.");
        }

        private static int FindStep(TraceTimelineModel timeline, NativeTraceEventKindV1 kind, uint runtimeNodeIndex)
        {
            for (var index = 0; index < timeline.Steps.Count; index++)
            {
                var record = timeline.Steps[index].Record;
                if (record.Kind == kind && record.RuntimeNodeIndex == runtimeNodeIndex) return index;
            }

            Assert.Fail($"No {kind} step found for runtime node {runtimeNodeIndex}.");
            return -1;
        }

        private static NativeLifecycleStepResultV1 DriveAndRecord(
            ref NativeLifecycleMachineV1 machine,
            SpikeTraceRecorder recorder,
            ulong updateId,
            NativeLifecycleStepKindV1 expectedKind,
            uint expectedNodeIndex,
            BurstCallbackPhase expectedPhase = default)
        {
            Assert.That(machine.TryAdvance(out var step, out var failure), Is.True, failure.Code.ToString());
            Assert.That(step.Kind, Is.EqualTo(expectedKind));
            Assert.That(step.NodeIndex, Is.EqualTo(expectedNodeIndex));
            if (expectedKind == NativeLifecycleStepKindV1.DispatchRequired)
                Assert.That(step.Phase, Is.EqualTo(expectedPhase));
            recorder.RecordStep(updateId, step, pendingDispatchStatus: null);
            return step;
        }

        private static void CompleteAndRecord(
            ref NativeLifecycleMachineV1 machine,
            SpikeTraceRecorder recorder,
            NativeLifecycleStepResultV1 dispatchStep,
            NodeStatus status)
        {
            recorder.RecordDispatchCompletion(dispatchStep, status);
            Assert.That(machine.TryCompleteDispatch(dispatchStep.DispatchToken, BurstContextResult.Success, status, out var failure),
                Is.True, failure.Code.ToString());
        }

        private static NativeCompiledNodeRecordV1 Node(
            ulong typeId,
            uint memoryOffset,
            uint memorySize,
            uint childOffset,
            uint childCount)
            => new NativeCompiledNodeRecordV1(new CompiledNodeRecord(
                typeId, 1, 0, 0, 1,
                memorySize == 0 ? 0u : memoryOffset, memorySize, memorySize == 0 ? 1u : 4u,
                NodeMemoryLifetime.Activation,
                new CompiledRange(childOffset, childCount),
                CompiledNodeFlags.BurstDomain,
                0,
                new CompiledRange(0, 0),
                new CompiledRange(0, 0)));
    }

    /// <summary>
    /// Spike-only recorder: exactly the shape the ADR proposes production code adopt -- an external
    /// adapter, never a change inside <see cref="NativeLifecycleMachineV1"/>, driven by whoever already
    /// calls <see cref="NativeLifecycleMachineV1.TryAdvance"/> and <see cref="NativeLifecycleMachineV1.TryCompleteDispatch"/>.
    /// </summary>
    internal sealed class SpikeTraceRecorder
    {
        private readonly NativeTraceChannelOwnerV1 _owner;
        private readonly ulong _treeInstanceId;
        private readonly NativeHash256V1 _semanticHash;
        private NativeTraceChannelLeaseV1 _lease;
        private ulong _sequence;

        internal SpikeTraceRecorder(NativeTraceChannelOwnerV1 owner, ulong treeInstanceId)
        {
            _owner = owner;
            _treeInstanceId = treeInstanceId;
            _semanticHash = new NativeHash256V1(new CompiledHash(StableHash.Sha256Hex(Encoding.UTF8.GetBytes("spike-tree"))));
            Assert.That(owner.TryAcquireWriter(out _lease, out var failure), Is.True, failure.Code.ToString());
        }

        internal void ReleaseWriter()
        {
            Assert.That(_owner.TryReleaseWriter(_lease, out var failure), Is.True, failure.Code.ToString());
        }

        internal void RecordUpdateStarted(ulong updateId) => Append(updateId, NativeTraceEventKindV1.UpdateStarted, CompiledIndex.Invalid);

        internal void RecordUpdateEnded(ulong updateId, bool completed, bool hasRootStatus, NodeStatus rootStatus)
        {
            var record = BaseRecord(updateId, NativeTraceEventKindV1.UpdateCompleted, CompiledIndex.Invalid);
            if (completed && hasRootStatus)
            {
                record.OptionalFields |= NativeTraceOptionalFieldsV1.Status;
                record.Status = rootStatus;
            }

            Assert.That(_lease.Writer.TryAppend(record), Is.EqualTo(NativeTraceAppendResultV1.Written));
        }

        /// <summary>
        /// Maps one real <see cref="NativeLifecycleStepResultV1"/> to zero or one trace record, per
        /// the ADR's mapping table. <c>ChildSelected</c>/<c>ChildAccepted</c> are deliberately
        /// no-ops (pure internal bookkeeping, no new node-boundary information); the root's own
        /// <c>CompositeExited</c> is deliberately deferred and folded into the following
        /// <c>Completed</c> step's <c>NodeExited</c> record, the only point a root's exit status is
        /// actually available.
        /// </summary>
        internal void RecordStep(ulong updateId, NativeLifecycleStepResultV1 step, NodeStatus? pendingDispatchStatus)
        {
            switch (step.Kind)
            {
                case NativeLifecycleStepKindV1.CompositeEntered:
                    AppendNode(updateId, NativeTraceEventKindV1.NodeEntered, step.NodeIndex, NodeStatus.Running, includeStatus: true);
                    break;
                case NativeLifecycleStepKindV1.DispatchRequired:
                    // Leaf Enter/Tick/Exit/Abort dispatches are recorded by RecordDispatchCompletion,
                    // once the caller-supplied status is actually known -- DispatchRequired itself
                    // only announces the callback, it does not carry a result.
                    break;
                case NativeLifecycleStepKindV1.CompositeExited:
                    if (step.NodeIndex != 0)
                    {
                        // Non-root composite: no status available from this step alone (see the
                        // NestedComposite_* spike test) -- record the boundary without ExitReason
                        // rather than guessing one.
                        AppendNode(updateId, NativeTraceEventKindV1.NodeExited, step.NodeIndex, default, includeStatus: false);
                    }
                    // Root's own CompositeExited is folded into the Completed step below.
                    break;
                case NativeLifecycleStepKindV1.CompositeAborted:
                    AppendNode(updateId, NativeTraceEventKindV1.NodeAbortStarted, step.NodeIndex, default, includeStatus: false);
                    break;
                case NativeLifecycleStepKindV1.Completed:
                    if (step.HasRootStatus)
                    {
                        var record = BaseRecord(updateId, NativeTraceEventKindV1.NodeExited, 0);
                        record.OptionalFields |= NativeTraceOptionalFieldsV1.ExitReason;
                        record.ExitReason = step.RootStatus == NodeStatus.Success
                            ? NativeTraceNodeExitReasonV1.Success : NativeTraceNodeExitReasonV1.Failure;
                        Assert.That(_lease.Writer.TryAppend(record), Is.EqualTo(NativeTraceAppendResultV1.Written));
                    }
                    break;
                case NativeLifecycleStepKindV1.ChildSelected:
                case NativeLifecycleStepKindV1.ChildAccepted:
                case NativeLifecycleStepKindV1.ReactiveReset:
                case NativeLifecycleStepKindV1.ParallelBranchSuspended:
                case NativeLifecycleStepKindV1.Waiting:
                    // Deliberately no direct trace record -- see ADR-P6-015 mapping table.
                    break;
            }
        }

        internal void RecordDispatchCompletion(NativeLifecycleStepResultV1 dispatchStep, NodeStatus status)
        {
            switch (dispatchStep.Phase)
            {
                case BurstCallbackPhase.Enter:
                    AppendNode(0, NativeTraceEventKindV1.NodeEntered, dispatchStep.NodeIndex, NodeStatus.Running, includeStatus: true);
                    break;
                case BurstCallbackPhase.Tick:
                    AppendNode(0, NativeTraceEventKindV1.NodeTicked, dispatchStep.NodeIndex, status, includeStatus: true);
                    break;
                case BurstCallbackPhase.Exit:
                    var record = BaseRecord(0, NativeTraceEventKindV1.NodeExited, dispatchStep.NodeIndex);
                    record.OptionalFields |= NativeTraceOptionalFieldsV1.ExitReason;
                    record.ExitReason = status == NodeStatus.Success
                        ? NativeTraceNodeExitReasonV1.Success : NativeTraceNodeExitReasonV1.Failure;
                    Assert.That(_lease.Writer.TryAppend(record), Is.EqualTo(NativeTraceAppendResultV1.Written));
                    break;
                case BurstCallbackPhase.Abort:
                    AppendNode(0, NativeTraceEventKindV1.NodeAbortStarted, dispatchStep.NodeIndex, default, includeStatus: false);
                    break;
            }
        }

        private void AppendNode(ulong updateId, NativeTraceEventKindV1 kind, uint nodeIndex, NodeStatus status, bool includeStatus)
        {
            var record = BaseRecord(updateId, kind, nodeIndex);
            if (includeStatus)
            {
                record.OptionalFields |= NativeTraceOptionalFieldsV1.Status;
                record.Status = status;
            }

            Assert.That(_lease.Writer.TryAppend(record), Is.EqualTo(NativeTraceAppendResultV1.Written));
        }

        private void Append(ulong updateId, NativeTraceEventKindV1 kind, uint nodeIndex)
        {
            var record = BaseRecord(updateId, kind, nodeIndex);
            Assert.That(_lease.Writer.TryAppend(record), Is.EqualTo(NativeTraceAppendResultV1.Written));
        }

        private NativeTraceRecordV1 BaseRecord(ulong updateId, NativeTraceEventKindV1 kind, uint runtimeNodeIndex)
        {
            _sequence++;
            var record = new NativeTraceRecordV1
            {
                TraceFormatVersion = NativeTraceRecordV1.FormatVersion,
                Phase = NativeUpdatePhaseV1.Execute,
                UpdateId = updateId == 0 ? 1 : updateId,
                SnapshotRevision = 1,
                TreeSemanticHash = _semanticHash,
                TreeInstanceId = _treeInstanceId,
                Sequence = _sequence,
                WorkerOrdinal = 0,
                Kind = kind,
                RuntimeNodeIndex = CompiledIndex.Invalid,
                DebugIdentityIndex = CompiledIndex.Invalid,
                SourceNodeIndex = CompiledIndex.Invalid,
            };
            if (runtimeNodeIndex != CompiledIndex.Invalid)
            {
                record.OptionalFields |= NativeTraceOptionalFieldsV1.RuntimeNode;
                record.RuntimeNodeIndex = runtimeNodeIndex;
            }

            return record;
        }

        /// <summary>
        /// Used only by <c>NestedComposite_CompositeExitedStepAloneCannotSupplyExitStatus_RealGapNotAssumed</c>
        /// to build (and inspect) exactly the record an external recorder could produce from a bare
        /// <c>CompositeExited</c> step for a non-root node -- proving the missing-status gap by
        /// construction.
        /// </summary>
        internal static NativeTraceRecordV1 BuildNodeExitedRecordFromStepAlone(
            NativeLifecycleStepResultV1 step, ulong treeInstanceId, ulong updateId)
        {
            return new NativeTraceRecordV1
            {
                TraceFormatVersion = NativeTraceRecordV1.FormatVersion,
                Phase = NativeUpdatePhaseV1.Execute,
                UpdateId = updateId,
                SnapshotRevision = 1,
                TreeSemanticHash = new NativeHash256V1(new CompiledHash(StableHash.Sha256Hex(Encoding.UTF8.GetBytes("spike-tree")))),
                TreeInstanceId = treeInstanceId,
                Sequence = 1,
                WorkerOrdinal = 0,
                Kind = NativeTraceEventKindV1.NodeExited,
                OptionalFields = NativeTraceOptionalFieldsV1.RuntimeNode,
                RuntimeNodeIndex = step.NodeIndex,
                DebugIdentityIndex = CompiledIndex.Invalid,
                SourceNodeIndex = CompiledIndex.Invalid,
                // No ExitReason/Status set -- step alone supplies no status for a non-root CompositeExited.
            };
        }
    }
}
