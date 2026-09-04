using AIBT.Burst;

namespace AIBT.Runtime.Scheduling
{
    /// <summary>
    /// Production implementation of <c>ADR-P6-015</c> (<c>AIBT-027</c>): an external recorder --
    /// never a change inside <see cref="NativeLifecycleMachineV1"/> itself -- that translates
    /// whatever already drives it (<see cref="SchedulingPolicyDriver.TryRunImmediate"/>'s own
    /// <c>TryAdvance</c>/<c>TryCompleteDispatch</c> call sites today) into real
    /// <see cref="NativeTraceEventKindV1"/> records on a caller-owned <see cref="NativeTraceChannelOwnerV1"/>,
    /// per the ADR's own fixed mapping table. Mirrors <c>Spikes~/NativeTraceProductionWiring/</c>'s
    /// already-proven shape.
    /// <para>
    /// Never creates, disposes, or otherwise owns the channel -- purely a writer against a
    /// caller-supplied owner, exactly like <c>NativeExecutionDebuggerSession</c> never creates or
    /// disposes the channel it reads. Every write is best-effort: a failure to acquire, append, or
    /// release never surfaces as an error to the driver -- trace production must never affect
    /// scheduling correctness, matching this ADR's own "additive hook, not a control-flow change"
    /// constraint.
    /// </para>
    /// </summary>
    internal sealed class NativeTraceRecorderV1
    {
        private readonly NativeTraceChannelOwnerV1 _owner;
        private readonly NativeHash256V1 _semanticHash;
        private readonly ulong _treeInstanceId;
        private NativeTraceChannelLeaseV1 _lease;
        private bool _hasLease;
        private ulong _sequence;
        private bool _rootExitRecorded;

        internal NativeTraceRecorderV1(NativeTraceChannelOwnerV1 owner, NativeHash256V1 semanticHash, ulong treeInstanceId)
        {
            _owner = owner;
            _semanticHash = semanticHash;
            _treeInstanceId = treeInstanceId;
        }

        /// <summary>Acquires a writer lease and records the update boundary -- a recorder-level hook, not a step result, per the ADR's mapping table.</summary>
        internal void RecordUpdateStarted(ulong updateId)
        {
            _rootExitRecorded = false;
            _hasLease = _owner.TryAcquireWriter(out _lease, out _);
            if (_hasLease) Append(updateId, NativeTraceEventKindV1.UpdateStarted, CompiledIndex.Invalid);
        }

        /// <summary>
        /// Maps one real <see cref="NativeLifecycleStepResultV1"/> to zero or one trace record, per
        /// the ADR's mapping table. <c>DispatchRequired</c> itself only announces a callback (its
        /// result is recorded separately by <see cref="RecordDispatchCompletion"/> once the
        /// caller-supplied status is known); <c>ChildSelected</c>/<c>ChildAccepted</c>/
        /// <c>ReactiveReset</c>/<c>ParallelBranchSuspended</c>/<c>Waiting</c> are deliberately
        /// no-ops (pure internal bookkeeping); the root's own <c>CompositeExited</c> is deliberately
        /// deferred and folded into the following <c>Completed</c> step's own <c>NodeExited</c>
        /// record, the only point a root's exit status is actually available.
        /// </summary>
        internal void RecordStep(ulong updateId, NativeLifecycleStepResultV1 step)
        {
            if (!_hasLease) return;
            switch (step.Kind)
            {
                case NativeLifecycleStepKindV1.CompositeEntered:
                    AppendNode(updateId, NativeTraceEventKindV1.NodeEntered, step.NodeIndex, NodeStatus.Running, includeStatus: true);
                    break;
                case NativeLifecycleStepKindV1.CompositeExited:
                    if (step.NodeIndex != 0)
                    {
                        // Non-root composite: no status available from this step alone (ADR-P6-015's
                        // own disclosed gap) -- record the boundary without ExitReason rather than
                        // guessing one.
                        AppendNode(updateId, NativeTraceEventKindV1.NodeExited, step.NodeIndex, default, includeStatus: false);
                    }
                    break;
                case NativeLifecycleStepKindV1.CompositeAborted:
                    AppendNode(updateId, NativeTraceEventKindV1.NodeAbortStarted, step.NodeIndex, default, includeStatus: false);
                    break;
                case NativeLifecycleStepKindV1.Completed:
                    if (step.HasRootStatus && !_rootExitRecorded)
                    {
                        AppendExit(updateId, 0, step.RootStatus);
                    }
                    break;
                default:
                    // DispatchRequired, ChildSelected, ChildAccepted, ReactiveReset,
                    // ParallelBranchSuspended, Waiting -- no direct record, per the ADR's own table.
                    break;
            }
        }

        /// <summary>
        /// Records a leaf dispatch's actual outcome once the caller-supplied status is known --
        /// <see cref="RecordStep"/> alone cannot, since <c>DispatchRequired</c> only announces the
        /// callback.
        /// </summary>
        internal void RecordDispatchCompletion(ulong updateId, NativeLifecycleStepResultV1 dispatchStep, NodeStatus status)
        {
            if (!_hasLease) return;
            switch (dispatchStep.Phase)
            {
                case BurstCallbackPhase.Enter:
                    AppendNode(updateId, NativeTraceEventKindV1.NodeEntered, dispatchStep.NodeIndex, NodeStatus.Running, includeStatus: true);
                    break;
                case BurstCallbackPhase.Tick:
                    AppendNode(updateId, NativeTraceEventKindV1.NodeTicked, dispatchStep.NodeIndex, status, includeStatus: true);
                    break;
                case BurstCallbackPhase.Exit:
                    if (dispatchStep.NodeIndex == 0) _rootExitRecorded = true;
                    if (dispatchStep.HasDispatchReasons)
                        AppendExitReason(updateId, dispatchStep.NodeIndex, (NativeTraceNodeExitReasonV1)(byte)dispatchStep.ExitReason);
                    else AppendExit(updateId, dispatchStep.NodeIndex, status);
                    break;
                case BurstCallbackPhase.Abort:
                    var record = BaseRecord(updateId, NativeTraceEventKindV1.NodeAbortStarted, dispatchStep.NodeIndex);
                    if (dispatchStep.HasDispatchReasons)
                    {
                        record.OptionalFields |= NativeTraceOptionalFieldsV1.AbortReason;
                        record.AbortReason = (NativeTraceNodeAbortReasonV1)(byte)dispatchStep.AbortReason;
                    }
                    _lease.Writer.TryAppend(record);
                    break;
            }
        }

        /// <summary>Records the update boundary and releases the writer lease -- symmetric with <see cref="RecordUpdateStarted"/>.</summary>
        internal void RecordUpdateEnded(ulong updateId, bool hasRootStatus, NodeStatus rootStatus)
        {
            if (!_hasLease) return;
            AppendNode(updateId, NativeTraceEventKindV1.UpdateCompleted, CompiledIndex.Invalid, rootStatus, includeStatus: hasRootStatus);
            ReleaseWriter();
        }

        internal void RecordBudgetYielded(ulong updateId)
        {
            if (_hasLease) Append(updateId, NativeTraceEventKindV1.BudgetYielded, CompiledIndex.Invalid);
            ReleaseWriter();
        }

        internal void RecordExecutionResumed(ulong updateId)
        {
            _hasLease = _owner.TryAcquireWriter(out _lease, out _);
            if (_hasLease) Append(updateId, NativeTraceEventKindV1.ExecutionResumed, CompiledIndex.Invalid);
        }

        internal void ReleaseWriter()
        {
            if (!_hasLease) return;
            _owner.TryReleaseWriter(_lease, out _);
            _hasLease = false;
        }

        private void AppendExit(ulong updateId, uint nodeIndex, NodeStatus status)
            => AppendExitReason(updateId, nodeIndex, status == NodeStatus.Success ? NativeTraceNodeExitReasonV1.Success : NativeTraceNodeExitReasonV1.Failure);

        private void AppendExitReason(ulong updateId, uint nodeIndex, NativeTraceNodeExitReasonV1 reason)
        {
            var record = BaseRecord(updateId, NativeTraceEventKindV1.NodeExited, nodeIndex);
            record.OptionalFields |= NativeTraceOptionalFieldsV1.ExitReason;
            record.ExitReason = reason;
            _lease.Writer.TryAppend(record);
        }

        private void AppendNode(ulong updateId, NativeTraceEventKindV1 kind, uint nodeIndex, NodeStatus status, bool includeStatus)
        {
            var record = BaseRecord(updateId, kind, nodeIndex);
            if (includeStatus)
            {
                record.OptionalFields |= NativeTraceOptionalFieldsV1.Status;
                record.Status = status;
            }

            _lease.Writer.TryAppend(record);
        }

        private void Append(ulong updateId, NativeTraceEventKindV1 kind, uint nodeIndex)
            => _lease.Writer.TryAppend(BaseRecord(updateId, kind, nodeIndex));

        private NativeTraceRecordV1 BaseRecord(ulong updateId, NativeTraceEventKindV1 kind, uint runtimeNodeIndex)
        {
            _sequence++;
            var record = new NativeTraceRecordV1
            {
                TraceFormatVersion = NativeTraceRecordV1.FormatVersion,
                Phase = NativeUpdatePhaseV1.Execute,
                UpdateId = updateId,
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
    }
}
