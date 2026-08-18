using AIBT.Burst;
using Unity.Collections;

namespace AIBT.Execution.Burst.Dispatch
{
    internal enum NativeBurstDispatchBindingKindV2 : byte
    {
        BlackboardRead = 0,
        BlackboardWrite = 1,
        BlackboardReadWrite = 2,
        SnapshotRead = 3,
        EffectCommand = 4,
        AsyncOperation = 5,
        Completion = 6
    }

    [System.Flags]
    internal enum NativeBurstDispatchBindingPhaseMaskV2 : byte
    {
        None = 0,
        Execute = 1 << 0,
        Cancel = 1 << 1,
        Completion = 1 << 2
    }

    internal readonly struct NativeBurstDispatchBindingV2
    {
        internal const byte NoScope = 0xff;
        internal const uint NoOffset = uint.MaxValue;

        internal NativeBurstDispatchBindingV2(
            uint bindingOrdinal,
            uint configurationFieldOrdinal,
            NativeBurstDispatchBindingKindV2 kind,
            byte scope,
            NativeBurstDispatchBindingPhaseMaskV2 phaseMask,
            ulong primaryTypeNumericId,
            uint primaryTypeVersion,
            uint firstPrimaryValueField,
            uint primaryValueFieldCount,
            uint primaryValueSize,
            ulong secondaryTypeNumericId,
            uint secondaryTypeVersion,
            uint firstSecondaryValueField,
            uint secondaryValueFieldCount,
            uint secondaryValueSize)
        {
            BindingOrdinal = bindingOrdinal;
            ConfigurationFieldOrdinal = configurationFieldOrdinal;
            Kind = kind;
            Scope = scope;
            PhaseMask = phaseMask;
            PrimaryTypeNumericId = primaryTypeNumericId;
            PrimaryTypeVersion = primaryTypeVersion;
            FirstPrimaryValueField = firstPrimaryValueField;
            PrimaryValueFieldCount = primaryValueFieldCount;
            PrimaryValueSize = primaryValueSize;
            SecondaryTypeNumericId = secondaryTypeNumericId;
            SecondaryTypeVersion = secondaryTypeVersion;
            FirstSecondaryValueField = firstSecondaryValueField;
            SecondaryValueFieldCount = secondaryValueFieldCount;
            SecondaryValueSize = secondaryValueSize;
        }

        internal uint BindingOrdinal { get; }
        internal uint ConfigurationFieldOrdinal { get; }
        internal NativeBurstDispatchBindingKindV2 Kind { get; }
        internal byte Scope { get; }
        internal NativeBurstDispatchBindingPhaseMaskV2 PhaseMask { get; }
        internal ulong PrimaryTypeNumericId { get; }
        internal uint PrimaryTypeVersion { get; }
        internal uint FirstPrimaryValueField { get; }
        internal uint PrimaryValueFieldCount { get; }
        internal uint PrimaryValueSize { get; }
        internal ulong SecondaryTypeNumericId { get; }
        internal uint SecondaryTypeVersion { get; }
        internal uint FirstSecondaryValueField { get; }
        internal uint SecondaryValueFieldCount { get; }
        internal uint SecondaryValueSize { get; }
    }

    internal readonly struct NativeBurstDispatchResolvedBindingV2
    {
        internal NativeBurstDispatchResolvedBindingV2(
            uint bindingOrdinal,
            uint targetOrdinal,
            uint liveValueOffset)
        {
            BindingOrdinal = bindingOrdinal;
            TargetOrdinal = targetOrdinal;
            LiveValueOffset = liveValueOffset;
        }

        internal uint BindingOrdinal { get; }
        internal uint TargetOrdinal { get; }
        internal uint LiveValueOffset { get; }
    }

    internal enum NativeBurstDispatchCanonicalRuleKindV2 : byte
    {
        None = 0,
        AgentId = 1,
        EntityId = 2,
        OperationId = 3,
        AssetId = 4,
        FixedString32 = 5,
        FixedString64 = 6,
        FixedString128 = 7,
        FixedString512 = 8
    }

    internal readonly struct NativeBurstDispatchCanonicalRuleV2
    {
        internal NativeBurstDispatchCanonicalRuleV2(
            NativeBurstDispatchCanonicalRuleKindV2 kind,
            uint byteOffset)
        {
            Kind = kind;
            ByteOffset = byteOffset;
        }

        internal NativeBurstDispatchCanonicalRuleKindV2 Kind { get; }
        internal uint ByteOffset { get; }
    }

    internal readonly struct NativeBurstDispatchCanonicalRangeV2
    {
        internal NativeBurstDispatchCanonicalRangeV2(uint firstRule, uint ruleCount)
        {
            FirstRule = firstRule;
            RuleCount = ruleCount;
        }

        internal uint FirstRule { get; }
        internal uint RuleCount { get; }
    }

    internal readonly struct NativeBurstDispatchCanonicalInputV2
    {
        internal NativeBurstDispatchCanonicalInputV2(
            NativeArray<NativeBurstDispatchCanonicalRangeV2>.ReadOnly caseRanges,
            NativeArray<NativeBurstDispatchCanonicalRangeV2>.ReadOnly bindingRanges,
            NativeArray<NativeBurstDispatchCanonicalRuleV2>.ReadOnly rules)
        {
            CaseRanges = caseRanges;
            BindingRanges = bindingRanges;
            Rules = rules;
        }

        internal NativeArray<NativeBurstDispatchCanonicalRangeV2>.ReadOnly CaseRanges { get; }
        internal NativeArray<NativeBurstDispatchCanonicalRangeV2>.ReadOnly BindingRanges { get; }
        internal NativeArray<NativeBurstDispatchCanonicalRuleV2>.ReadOnly Rules { get; }
        internal bool IsCreated => CaseRanges.IsCreated;
    }

    internal enum NativeBurstDispatchCompletionStateV2 : byte
    {
        Available = 0,
        Consumed = 1
    }

    internal struct NativeBurstDispatchCompletionV2
    {
        internal NativeBurstDispatchCompletionV2(
            uint targetOrdinal,
            OperationId operationId,
            BurstCompletionOutcome outcome,
            uint payloadOffset)
        {
            TargetOrdinal = targetOrdinal;
            OperationId = operationId;
            Outcome = outcome;
            PayloadOffset = payloadOffset;
            State = NativeBurstDispatchCompletionStateV2.Available;
        }

        internal uint TargetOrdinal;
        internal OperationId OperationId;
        internal BurstCompletionOutcome Outcome;
        internal uint PayloadOffset;
        internal NativeBurstDispatchCompletionStateV2 State;
    }

    internal enum NativeBurstDispatchCommandKindV2 : byte
    {
        Effect = 0,
        Start = 1,
        Cancel = 2
    }

    internal readonly struct NativeBurstDispatchCommandV2
    {
        internal NativeBurstDispatchCommandV2(
            NativeBurstDispatchCommandKindV2 kind,
            uint targetOrdinal,
            OperationId operationId,
            uint payloadOffset,
            uint payloadSize)
        {
            Kind = kind;
            TargetOrdinal = targetOrdinal;
            OperationId = operationId;
            PayloadOffset = payloadOffset;
            PayloadSize = payloadSize;
        }

        internal NativeBurstDispatchCommandKindV2 Kind { get; }
        internal uint TargetOrdinal { get; }
        internal OperationId OperationId { get; }
        internal uint PayloadOffset { get; }
        internal uint PayloadSize { get; }
    }

    internal enum NativeBurstDispatchOperationStateV2 : byte
    {
        Active = 0,
        Tombstoned = 1
    }

    internal struct NativeBurstDispatchOperationV2
    {
        internal NativeBurstDispatchOperationV2(
            OperationId operationId,
            uint targetOrdinal,
            ulong startTypeNumericId,
            uint startTypeVersion,
            ulong cancelTypeNumericId,
            uint cancelTypeVersion,
            uint faultCancelPayloadOffset,
            uint faultCancelPayloadSize)
        {
            OperationId = operationId;
            TargetOrdinal = targetOrdinal;
            StartTypeNumericId = startTypeNumericId;
            StartTypeVersion = startTypeVersion;
            CancelTypeNumericId = cancelTypeNumericId;
            CancelTypeVersion = cancelTypeVersion;
            FaultCancelPayloadOffset = faultCancelPayloadOffset;
            FaultCancelPayloadSize = faultCancelPayloadSize;
            State = NativeBurstDispatchOperationStateV2.Active;
        }

        internal OperationId OperationId;
        internal uint TargetOrdinal;
        internal ulong StartTypeNumericId;
        internal uint StartTypeVersion;
        internal ulong CancelTypeNumericId;
        internal uint CancelTypeVersion;
        internal uint FaultCancelPayloadOffset;
        internal uint FaultCancelPayloadSize;
        internal NativeBurstDispatchOperationStateV2 State;
    }

    internal enum NativeBurstDispatchValueSessionKindV2 : byte
    {
        BlackboardRead = 0,
        BlackboardWrite = 1,
        SnapshotRead = 2,
        CompletionRead = 3,
        EffectWrite = 4,
        StartWrite = 5,
        FaultCancelWrite = 6,
        CancelWrite = 7
    }

    internal enum NativeBurstDispatchValueSessionStateV2 : byte
    {
        Active = 0,
        ReadComplete = 1,
        Sealed = 2,
        ConsumeSealed = 3
    }

    internal struct NativeBurstDispatchValueSessionV2
    {
        internal uint FrameId;
        internal uint BindingOrdinal;
        internal uint StagingOffset;
        internal uint ValueSize;
        internal uint CompanionSessionOrdinal;
        internal uint CompletionOrdinal;
        internal OperationId OperationId;
        internal NativeBurstDispatchValueSessionKindV2 Kind;
        internal NativeBurstDispatchValueSessionStateV2 State;
    }

    internal struct NativeBurstDispatchTransactionControlV2
    {
        internal ulong LedgerToken;
        internal TreeInstanceId TreeInstanceId;
        internal ulong MutationVersion;
        internal ulong IntegrityTag;
        internal uint ActiveFrameId;
        internal uint SessionCount;
        internal uint StagingByteCount;
        internal uint CommandCount;
        internal uint CommandPayloadByteCount;
        internal uint OperationCount;
        internal ulong NextOperationSequence;
    }

    internal static class NativeBurstDispatchTransactionLedgerV2
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        internal static void Initialize(ref NativeBurstDispatchTransactionControlV2 transaction)
        {
            transaction.MutationVersion = 1UL;
            Seal(ref transaction);
        }

        internal static void Seal(ref NativeBurstDispatchTransactionControlV2 transaction)
            => transaction.IntegrityTag = Compute(in transaction);

        internal static void Advance(ref NativeBurstDispatchTransactionControlV2 transaction)
        {
            if (transaction.MutationVersion != ulong.MaxValue)
            {
                transaction.MutationVersion++;
            }

            Seal(ref transaction);
        }

        internal static bool IsValid(in NativeBurstDispatchTransactionControlV2 transaction)
            => transaction.MutationVersion != 0
                && transaction.IntegrityTag == Compute(in transaction);

        private static ulong Compute(in NativeBurstDispatchTransactionControlV2 transaction)
        {
            var value = OffsetBasis;
            value = Mix(value, transaction.LedgerToken);
            value = Mix(value, transaction.TreeInstanceId.Value);
            value = Mix(value, transaction.MutationVersion);
            value = Mix(value, transaction.ActiveFrameId);
            value = Mix(value, transaction.SessionCount);
            value = Mix(value, transaction.StagingByteCount);
            value = Mix(value, transaction.CommandCount);
            value = Mix(value, transaction.CommandPayloadByteCount);
            value = Mix(value, transaction.OperationCount);
            value = Mix(value, transaction.NextOperationSequence);
            return value;
        }

        private static ulong Mix(ulong value, ulong next)
        {
            unchecked
            {
                value ^= next;
                return value * Prime;
            }
        }
    }

    internal readonly struct NativeBurstDispatchTransactionSnapshotV2
    {
        internal NativeBurstDispatchTransactionSnapshotV2(
            uint activeFrameId,
            uint sessionCount,
            uint stagingByteCount,
            uint commandCount,
            uint commandPayloadByteCount,
            uint operationCount,
            ulong nextOperationSequence)
        {
            ActiveFrameId = activeFrameId;
            SessionCount = sessionCount;
            StagingByteCount = stagingByteCount;
            CommandCount = commandCount;
            CommandPayloadByteCount = commandPayloadByteCount;
            OperationCount = operationCount;
            NextOperationSequence = nextOperationSequence;
        }

        internal uint ActiveFrameId { get; }
        internal uint SessionCount { get; }
        internal uint StagingByteCount { get; }
        internal uint CommandCount { get; }
        internal uint CommandPayloadByteCount { get; }
        internal uint OperationCount { get; }
        internal ulong NextOperationSequence { get; }
    }

    internal readonly struct NativeBurstDispatchBindingCapacityV2
    {
        internal NativeBurstDispatchBindingCapacityV2(
            uint maxValueSessionsPerFrame,
            uint maxValueStagingBytesPerFrame,
            uint maxCommands,
            uint maxCommandPayloadBytes,
            uint maxOperations,
            ulong firstOperationSequence = 1)
        {
            MaxValueSessionsPerFrame = maxValueSessionsPerFrame;
            MaxValueStagingBytesPerFrame = maxValueStagingBytesPerFrame;
            MaxCommands = maxCommands;
            MaxCommandPayloadBytes = maxCommandPayloadBytes;
            MaxOperations = maxOperations;
            FirstOperationSequence = firstOperationSequence;
        }

        internal uint MaxValueSessionsPerFrame { get; }
        internal uint MaxValueStagingBytesPerFrame { get; }
        internal uint MaxCommands { get; }
        internal uint MaxCommandPayloadBytes { get; }
        internal uint MaxOperations { get; }
        internal ulong FirstOperationSequence { get; }
    }

    internal readonly struct NativeBurstDispatchBindingInputV2
    {
        internal NativeBurstDispatchBindingInputV2(
            NativeArray<NativeBurstDispatchBindingV2>.ReadOnly bindings,
            NativeArray<NativeBurstDispatchResolvedBindingV2>.ReadOnly resolvedBindings,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly valueFields,
            NativeArray<byte>.ReadOnly liveValueBytes,
            NativeArray<NativeBurstDispatchCompletionV2>.ReadOnly completions,
            NativeArray<byte>.ReadOnly completionPayloadBytes,
            NativeBurstDispatchBindingCapacityV2 capacity)
            : this(
                bindings,
                resolvedBindings,
                valueFields,
                liveValueBytes,
                completions,
                completionPayloadBytes,
                capacity,
                default)
        {
        }

        internal NativeBurstDispatchBindingInputV2(
            NativeArray<NativeBurstDispatchBindingV2>.ReadOnly bindings,
            NativeArray<NativeBurstDispatchResolvedBindingV2>.ReadOnly resolvedBindings,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly valueFields,
            NativeArray<byte>.ReadOnly liveValueBytes,
            NativeArray<NativeBurstDispatchCompletionV2>.ReadOnly completions,
            NativeArray<byte>.ReadOnly completionPayloadBytes,
            NativeBurstDispatchBindingCapacityV2 capacity,
            NativeBurstDispatchCanonicalInputV2 canonicalInput)
        {
            Bindings = bindings;
            ResolvedBindings = resolvedBindings;
            ValueFields = valueFields;
            LiveValueBytes = liveValueBytes;
            Completions = completions;
            CompletionPayloadBytes = completionPayloadBytes;
            Capacity = capacity;
            CanonicalInput = canonicalInput;
        }

        internal NativeArray<NativeBurstDispatchBindingV2>.ReadOnly Bindings { get; }
        internal NativeArray<NativeBurstDispatchResolvedBindingV2>.ReadOnly ResolvedBindings { get; }
        internal NativeArray<NativeBurstDispatchFieldV2>.ReadOnly ValueFields { get; }
        internal NativeArray<byte>.ReadOnly LiveValueBytes { get; }
        internal NativeArray<NativeBurstDispatchCompletionV2>.ReadOnly Completions { get; }
        internal NativeArray<byte>.ReadOnly CompletionPayloadBytes { get; }
        internal NativeBurstDispatchBindingCapacityV2 Capacity { get; }
        internal NativeBurstDispatchCanonicalInputV2 CanonicalInput { get; }
        internal bool IsEnabled => Bindings.IsCreated;
    }
}
