using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace AIBT.Burst
{
    internal struct BurstDispatchBackingV2
    {
        [NativeDisableContainerSafetyRestriction]
        internal NativeList<int> ExecutionClaim;
        [NativeDisableContainerSafetyRestriction]
        internal NativeList<long> FrameCompletionClaim;
        [NativeDisableParallelForRestriction]
        internal NativeArray<NativeBurstDispatchControlV2> Control;
        [ReadOnly] internal NativeArray<NativeBurstDispatchCaseV2> Cases;
        [ReadOnly] internal NativeArray<NativeBurstDispatchRequestV2> Requests;
        [ReadOnly] internal NativeArray<NativeBurstDispatchFieldV2> ConfigurationFields;
        [ReadOnly] internal NativeArray<NativeBurstDispatchFieldV2> MemoryFields;
        [ReadOnly] internal NativeArray<byte> ConfigurationBytes;
        [NativeDisableParallelForRestriction]
        internal NativeArray<byte> MemoryBytes;
        [NativeDisableParallelForRestriction]
        internal NativeArray<byte> MemoryStaging;
        [NativeDisableParallelForRestriction]
        internal NativeArray<byte> MemoryWritten;
        [NativeDisableParallelForRestriction]
        internal NativeArray<ulong> RandomStates;
        [ReadOnly] internal NativeArray<ulong> RandomIncrements;
        [NativeDisableParallelForRestriction]
        internal NativeArray<byte> RequestStatuses;
        [ReadOnly] internal NativeArray<NativeBurstDispatchBindingV2> Bindings;
        [ReadOnly] internal NativeArray<NativeBurstDispatchResolvedBindingV2> ResolvedBindings;
        [ReadOnly] internal NativeArray<NativeBurstDispatchFieldV2> ValueFields;
        [ReadOnly] internal NativeArray<NativeBurstDispatchCanonicalRangeV2> CaseCanonicalRanges;
        [ReadOnly] internal NativeArray<NativeBurstDispatchCanonicalRangeV2> BindingCanonicalRanges;
        [ReadOnly] internal NativeArray<NativeBurstDispatchCanonicalRuleV2> CanonicalRules;
        [NativeDisableParallelForRestriction] internal NativeArray<byte> BindingValueBytes;
        [NativeDisableParallelForRestriction] internal NativeArray<NativeBurstDispatchCompletionV2> Completions;
        [ReadOnly] internal NativeArray<byte> CompletionPayloadBytes;
        [NativeDisableParallelForRestriction] internal NativeArray<NativeBurstDispatchValueSessionV2> ValueSessions;
        [NativeDisableParallelForRestriction] internal NativeArray<byte> ValueStagingBytes;
        [NativeDisableParallelForRestriction] internal NativeArray<byte> ValueMarks;
        [NativeDisableParallelForRestriction] internal NativeArray<NativeBurstDispatchCommandV2> Commands;
        [NativeDisableParallelForRestriction] internal NativeArray<byte> CommandPayloadBytes;
        [NativeDisableParallelForRestriction] internal NativeArray<NativeBurstDispatchOperationV2> Operations;
        [NativeDisableParallelForRestriction] internal NativeArray<NativeBurstDispatchTransactionControlV2> TransactionControl;
        internal BurstCatalogHandshake Handshake;
        internal ulong OwnerId;
        internal uint Generation;

        internal BurstDispatchBackingV2(
            BurstCatalogHandshake handshake,
            NativeArray<NativeBurstDispatchControlV2> control,
            NativeList<int> executionClaim,
            NativeList<long> frameCompletionClaim,
            NativeArray<NativeBurstDispatchCaseV2> cases,
            NativeArray<NativeBurstDispatchRequestV2> requests,
            NativeArray<NativeBurstDispatchFieldV2> configurationFields,
            NativeArray<NativeBurstDispatchFieldV2> memoryFields,
            NativeArray<byte> configurationBytes,
            NativeArray<byte> memoryBytes,
            NativeArray<byte> memoryStaging,
            NativeArray<byte> memoryWritten,
            NativeArray<ulong> randomStates,
            NativeArray<ulong> randomIncrements,
            NativeArray<byte> requestStatuses,
            NativeArray<NativeBurstDispatchBindingV2> bindings,
            NativeArray<NativeBurstDispatchResolvedBindingV2> resolvedBindings,
            NativeArray<NativeBurstDispatchFieldV2> valueFields,
            NativeArray<NativeBurstDispatchCanonicalRangeV2> caseCanonicalRanges,
            NativeArray<NativeBurstDispatchCanonicalRangeV2> bindingCanonicalRanges,
            NativeArray<NativeBurstDispatchCanonicalRuleV2> canonicalRules,
            NativeArray<byte> bindingValueBytes,
            NativeArray<NativeBurstDispatchCompletionV2> completions,
            NativeArray<byte> completionPayloadBytes,
            NativeArray<NativeBurstDispatchValueSessionV2> valueSessions,
            NativeArray<byte> valueStagingBytes,
            NativeArray<byte> valueMarks,
            NativeArray<NativeBurstDispatchCommandV2> commands,
            NativeArray<byte> commandPayloadBytes,
            NativeArray<NativeBurstDispatchOperationV2> operations,
            NativeArray<NativeBurstDispatchTransactionControlV2> transactionControl)
        {
            Handshake = handshake;
            Control = control;
            ExecutionClaim = executionClaim;
            FrameCompletionClaim = frameCompletionClaim;
            Cases = cases;
            Requests = requests;
            ConfigurationFields = configurationFields;
            MemoryFields = memoryFields;
            ConfigurationBytes = configurationBytes;
            MemoryBytes = memoryBytes;
            MemoryStaging = memoryStaging;
            MemoryWritten = memoryWritten;
            RandomStates = randomStates;
            RandomIncrements = randomIncrements;
            RequestStatuses = requestStatuses;
            Bindings = bindings;
            ResolvedBindings = resolvedBindings;
            ValueFields = valueFields;
            CaseCanonicalRanges = caseCanonicalRanges;
            BindingCanonicalRanges = bindingCanonicalRanges;
            CanonicalRules = canonicalRules;
            BindingValueBytes = bindingValueBytes;
            Completions = completions;
            CompletionPayloadBytes = completionPayloadBytes;
            ValueSessions = valueSessions;
            ValueStagingBytes = valueStagingBytes;
            ValueMarks = valueMarks;
            Commands = commands;
            CommandPayloadBytes = commandPayloadBytes;
            Operations = operations;
            TransactionControl = transactionControl;
            var state = control[0];
            OwnerId = state.OwnerId;
            Generation = state.Generation;
        }

        internal bool IsCreated => Control.IsCreated;
    }
}
