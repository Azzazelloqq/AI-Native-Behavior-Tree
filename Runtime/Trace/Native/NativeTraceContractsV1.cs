using System;
using Unity.Collections;

namespace AIBT
{
    public enum NativeTraceLevelV1 : byte
    {
        Off = 0,
        Errors = 1,
        Lifecycle = 2,
        Detailed = 3,
    }

    public enum NativeTraceEventKindV1 : byte
    {
        UpdateStarted = 0,
        UpdateCompleted = 1,
        NodeEntered = 2,
        NodeTicked = 3,
        NodeAbortStarted = 4,
        NodeExited = 5,
        BlackboardChanged = 6,
        ObserverQueued = 7,
        ObserverEvaluated = 8,
        CommandEmitted = 9,
        CompletionConsumed = 10,
        CompletionDiscarded = 11,
        BudgetYielded = 12,
        ExecutionResumed = 13,
        DiagnosticRaised = 14,
        SchedulerDecision = 15,
        TraceDroppedSummary = 16,
    }

    public enum NativeTraceNodeExitReasonV1 : byte
    {
        Success = 0,
        Failure = 1,
        Aborted = 2,
    }

    public enum NativeTraceNodeAbortReasonV1 : byte
    {
        Explicit = 0,
        ObserverSelf = 1,
        ObserverLowerPriority = 2,
        TreeStopped = 3,
        HotReload = 4,
        Timeout = 5,
    }

    [Flags]
    public enum NativeTraceOptionalFieldsV1 : byte
    {
        None = 0,
        RuntimeNode = 1 << 0,
        DebugIdentity = 1 << 1,
        Status = 1 << 2,
        ExitReason = 1 << 3,
        AbortReason = 1 << 4,
        SourceNode = 1 << 5,
        DiagnosticCode = 1 << 6,
        BlackboardVersions = 1 << 7,
    }

    public struct NativeTraceRecordV1
    {
        public const uint FormatVersion = 1;

        public uint TraceFormatVersion;
        public NativeUpdatePhaseV1 Phase;
        public ulong UpdateId;
        public ulong SnapshotRevision;
        public NativeHash256V1 TreeSemanticHash;
        public ulong TreeInstanceId;
        public ulong Sequence;
        public uint WorkerOrdinal;
        public NativeTraceEventKindV1 Kind;
        public NativeTraceOptionalFieldsV1 OptionalFields;
        public uint RuntimeNodeIndex;
        public uint DebugIdentityIndex;
        public NodeStatus Status;
        public NativeTraceNodeExitReasonV1 ExitReason;
        public NativeTraceNodeAbortReasonV1 AbortReason;
        public uint SourceNodeIndex;
        public ushort DiagnosticCodeNumber;
        public ulong StableBlackboardKeyId;
        public ulong OldBlackboardVersion;
        public ulong NewBlackboardVersion;
        public ulong OperationOrDecisionId;
        public uint PayloadOffset;
        public uint PayloadLength;
        public ulong DroppedCount;

        public bool HasValidHeader
        {
            get
            {
                const NativeTraceOptionalFieldsV1 known = NativeTraceOptionalFieldsV1.RuntimeNode
                    | NativeTraceOptionalFieldsV1.DebugIdentity
                    | NativeTraceOptionalFieldsV1.Status
                    | NativeTraceOptionalFieldsV1.ExitReason
                    | NativeTraceOptionalFieldsV1.AbortReason
                    | NativeTraceOptionalFieldsV1.SourceNode
                    | NativeTraceOptionalFieldsV1.DiagnosticCode
                    | NativeTraceOptionalFieldsV1.BlackboardVersions;
                return TraceFormatVersion == FormatVersion
                    && (byte)Phase >= (byte)NativeUpdatePhaseV1.CollectInput
                    && (byte)Phase <= (byte)NativeUpdatePhaseV1.PublishTraceAndMetrics
                    && UpdateId != 0
                    && SnapshotRevision != 0
                    && HasSemanticHash()
                    && TreeInstanceId != 0
                    && Sequence != 0
                    && (byte)Kind <= (byte)NativeTraceEventKindV1.TraceDroppedSummary
                    && (OptionalFields & ~known) == 0
                    && ((OptionalFields & NativeTraceOptionalFieldsV1.RuntimeNode) == 0) == (RuntimeNodeIndex == CompiledIndex.Invalid)
                    && ((OptionalFields & NativeTraceOptionalFieldsV1.DebugIdentity) == 0) == (DebugIdentityIndex == CompiledIndex.Invalid)
                    && ((OptionalFields & NativeTraceOptionalFieldsV1.SourceNode) == 0) == (SourceNodeIndex == CompiledIndex.Invalid)
                    && ((OptionalFields & NativeTraceOptionalFieldsV1.DebugIdentity) == 0
                        || (OptionalFields & NativeTraceOptionalFieldsV1.RuntimeNode) != 0)
                    && ((OptionalFields & NativeTraceOptionalFieldsV1.Status) == 0 || (byte)Status <= (byte)NodeStatus.Running)
                    && ((OptionalFields & NativeTraceOptionalFieldsV1.ExitReason) == 0
                        || (byte)ExitReason <= (byte)NativeTraceNodeExitReasonV1.Aborted)
                    && ((OptionalFields & NativeTraceOptionalFieldsV1.AbortReason) == 0
                        || (byte)AbortReason <= (byte)NativeTraceNodeAbortReasonV1.Timeout)
                    && ((OptionalFields & NativeTraceOptionalFieldsV1.DiagnosticCode) == 0
                        || DiagnosticCodeNumber >= 1 && DiagnosticCodeNumber <= 9999)
                    && (Kind == NativeTraceEventKindV1.TraceDroppedSummary ? DroppedCount != 0 : DroppedCount == 0);
            }
        }

        private bool HasSemanticHash()
        {
            for (var index = 0; index < 32; index++)
            {
                if (TreeSemanticHash.GetByte(index) != 0) return true;
            }

            return false;
        }
    }

    public readonly struct NativeTraceChannelCapacityV1
    {
        public NativeTraceChannelCapacityV1(
            uint recordCapacity,
            uint payloadCapacity,
            uint maximumPayloadBytes,
            uint emissionCapacity)
        {
            RecordCapacity = recordCapacity;
            PayloadCapacity = payloadCapacity;
            MaximumPayloadBytes = maximumPayloadBytes;
            EmissionCapacity = emissionCapacity;
        }

        public uint RecordCapacity { get; }
        public uint PayloadCapacity { get; }
        public uint MaximumPayloadBytes { get; }
        public uint EmissionCapacity { get; }
        public uint OrdinaryRecordCapacity => RecordCapacity == 0 ? 0 : RecordCapacity - 1;
    }

    public enum NativeTraceAppendResultV1 : byte
    {
        Written = 0,
        Filtered = 1,
        Dropped = 2,
        InvalidRecord = 3,
        DuplicateSequence = 4,
        ChannelFaulted = 5,
    }

    public readonly struct NativeTraceChannelFailureV1
    {
        public NativeTraceChannelFailureV1(
            NativeRuntimeDiagnosticCodeV1 code,
            NativeDiagnosticResourceKindV1 resourceKind = NativeDiagnosticResourceKindV1.None,
            ulong requested = 0,
            ulong capacity = 0,
            ulong ownerId = 0,
            uint generation = 0,
            ulong leaseId = 0)
        {
            Code = code;
            ResourceKind = resourceKind;
            Requested = requested;
            Capacity = capacity;
            OwnerId = ownerId;
            Generation = generation;
            LeaseId = leaseId;
        }

        public NativeRuntimeDiagnosticCodeV1 Code { get; }
        public NativeDiagnosticResourceKindV1 ResourceKind { get; }
        public ulong Requested { get; }
        public ulong Capacity { get; }
        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong LeaseId { get; }
        public bool IsSuccess => Code == NativeRuntimeDiagnosticCodeV1.None;
    }

    public readonly struct NativeTraceChannelSnapshotV1
    {
        internal NativeTraceChannelSnapshotV1(
            NativeArray<NativeTraceRecordV1>.ReadOnly records,
            NativeArray<byte>.ReadOnly payload,
            uint recordCount,
            ulong droppedCount,
            bool faulted)
        {
            Records = records;
            Payload = payload;
            RecordCount = recordCount;
            DroppedCount = droppedCount;
            IsFaulted = faulted;
        }

        public NativeArray<NativeTraceRecordV1>.ReadOnly Records { get; }
        public NativeArray<byte>.ReadOnly Payload { get; }
        public uint RecordCount { get; }
        public ulong DroppedCount { get; }
        public bool IsFaulted { get; }
    }

    internal static class NativeReferenceTraceProjectionV1
    {
        internal static bool TryProject(
            in ReferenceTraceRecord source,
            NativeUpdatePhaseV1 phase,
            uint workerOrdinal,
            uint debugIdentityIndex,
            out NativeTraceRecordV1 destination)
        {
            destination = new NativeTraceRecordV1
            {
                TraceFormatVersion = NativeTraceRecordV1.FormatVersion,
                Phase = phase,
                UpdateId = source.UpdateId,
                SnapshotRevision = source.SnapshotRevision.Value,
                TreeSemanticHash = new NativeHash256V1(source.TreeSemanticHash),
                TreeInstanceId = source.TreeInstanceId.Value,
                Sequence = source.Sequence,
                WorkerOrdinal = workerOrdinal,
                RuntimeNodeIndex = CompiledIndex.Invalid,
                DebugIdentityIndex = CompiledIndex.Invalid,
                SourceNodeIndex = CompiledIndex.Invalid,
            };

            if (!TryMapKind(source.Kind, out destination.Kind))
            {
                destination = default;
                return false;
            }

            if (source.NodeIndex.IsValid)
            {
                destination.OptionalFields |= NativeTraceOptionalFieldsV1.RuntimeNode;
                destination.RuntimeNodeIndex = source.NodeIndex.Value;
                if (debugIdentityIndex != CompiledIndex.Invalid)
                {
                    destination.OptionalFields |= NativeTraceOptionalFieldsV1.DebugIdentity;
                    destination.DebugIdentityIndex = debugIdentityIndex;
                }
            }

            if (source.Status.HasValue)
            {
                destination.OptionalFields |= NativeTraceOptionalFieldsV1.Status;
                destination.Status = source.Status.Value;
            }

            if (source.ExitReason.HasValue)
            {
                destination.OptionalFields |= NativeTraceOptionalFieldsV1.ExitReason;
                destination.ExitReason = (NativeTraceNodeExitReasonV1)(byte)source.ExitReason.Value;
            }

            if (source.AbortReason.HasValue)
            {
                destination.OptionalFields |= NativeTraceOptionalFieldsV1.AbortReason;
                destination.AbortReason = (NativeTraceNodeAbortReasonV1)(byte)source.AbortReason.Value;
            }

            if (source.SourceNodeIndex.IsValid)
            {
                destination.OptionalFields |= NativeTraceOptionalFieldsV1.SourceNode;
                destination.SourceNodeIndex = source.SourceNodeIndex.Value;
            }

            if (source.DiagnosticCode.IsValid)
            {
                destination.OptionalFields |= NativeTraceOptionalFieldsV1.DiagnosticCode;
                destination.DiagnosticCodeNumber = ParseCodeNumber(source.DiagnosticCode);
            }

            if (source.StableBlackboardKeyId != 0 || source.OldBlackboardVersion != 0 || source.NewBlackboardVersion != 0)
            {
                destination.OptionalFields |= NativeTraceOptionalFieldsV1.BlackboardVersions;
                destination.StableBlackboardKeyId = source.StableBlackboardKeyId;
                destination.OldBlackboardVersion = source.OldBlackboardVersion;
                destination.NewBlackboardVersion = source.NewBlackboardVersion;
            }

            return destination.HasValidHeader;
        }

        private static bool TryMapKind(ReferenceTraceEventKind source, out NativeTraceEventKindV1 destination)
        {
            switch (source)
            {
                case ReferenceTraceEventKind.UpdateStarted: destination = NativeTraceEventKindV1.UpdateStarted; return true;
                case ReferenceTraceEventKind.UpdateCompleted: destination = NativeTraceEventKindV1.UpdateCompleted; return true;
                case ReferenceTraceEventKind.NodeEntered: destination = NativeTraceEventKindV1.NodeEntered; return true;
                case ReferenceTraceEventKind.NodeTicked: destination = NativeTraceEventKindV1.NodeTicked; return true;
                case ReferenceTraceEventKind.NodeAbortStarted: destination = NativeTraceEventKindV1.NodeAbortStarted; return true;
                case ReferenceTraceEventKind.NodeExited: destination = NativeTraceEventKindV1.NodeExited; return true;
                case ReferenceTraceEventKind.BlackboardChanged: destination = NativeTraceEventKindV1.BlackboardChanged; return true;
                case ReferenceTraceEventKind.ObserverQueued: destination = NativeTraceEventKindV1.ObserverQueued; return true;
                case ReferenceTraceEventKind.ObserverEvaluated: destination = NativeTraceEventKindV1.ObserverEvaluated; return true;
                case ReferenceTraceEventKind.CommandEmitted: destination = NativeTraceEventKindV1.CommandEmitted; return true;
                case ReferenceTraceEventKind.CompletionConsumed: destination = NativeTraceEventKindV1.CompletionConsumed; return true;
                case ReferenceTraceEventKind.CompletionDiscarded: destination = NativeTraceEventKindV1.CompletionDiscarded; return true;
                case ReferenceTraceEventKind.BudgetYielded: destination = NativeTraceEventKindV1.BudgetYielded; return true;
                case ReferenceTraceEventKind.ExecutionResumed: destination = NativeTraceEventKindV1.ExecutionResumed; return true;
                case ReferenceTraceEventKind.DiagnosticRaised: destination = NativeTraceEventKindV1.DiagnosticRaised; return true;
                default: destination = default; return false;
            }
        }

        private static ushort ParseCodeNumber(DiagnosticCode code)
            => (ushort)(((code.Value[4] - '0') * 1000)
                + ((code.Value[5] - '0') * 100)
                + ((code.Value[6] - '0') * 10)
                + (code.Value[7] - '0'));
    }
}
