using AIBT.Burst;
using Unity.Collections;

namespace AIBT.Execution.Burst.Dispatch
{
    internal enum NativeBurstDispatchFieldEncodingV2 : byte
    {
        Boolean = 0,
        Int8 = 1,
        UInt8 = 2,
        Int16 = 3,
        UInt16 = 4,
        Int32 = 5,
        UInt32 = 6,
        Int64 = 7,
        UInt64 = 8,
        Float32 = 9,
        Float64 = 10,
        GeneratedHandle = 11
    }

    [System.Flags]
    internal enum NativeBurstDispatchPhaseMaskV2 : byte
    {
        None = 0,
        Enter = 1 << 0,
        Tick = 1 << 1,
        Abort = 1 << 2,
        Exit = 1 << 3,
        Observer = 1 << 4
    }

    internal readonly struct NativeBurstDispatchFieldV2
    {
        internal NativeBurstDispatchFieldV2(
            uint fieldOrdinal,
            uint byteOffset,
            uint elementCount,
            uint elementSize,
            NativeBurstDispatchFieldEncodingV2 encoding)
            : this(
                fieldOrdinal,
                0u,
                byteOffset,
                elementCount,
                elementSize,
                encoding,
                NativeBurstDispatchCanonicalRuleKindV2.None)
        {
        }

        internal NativeBurstDispatchFieldV2(
            uint fieldOrdinal,
            uint firstElementIndex,
            uint byteOffset,
            uint elementCount,
            uint elementSize,
            NativeBurstDispatchFieldEncodingV2 encoding)
            : this(
                fieldOrdinal,
                firstElementIndex,
                byteOffset,
                elementCount,
                elementSize,
                encoding,
                NativeBurstDispatchCanonicalRuleKindV2.None)
        {
        }

        internal NativeBurstDispatchFieldV2(
            uint fieldOrdinal,
            uint firstElementIndex,
            uint byteOffset,
            uint elementCount,
            uint elementSize,
            NativeBurstDispatchFieldEncodingV2 encoding,
            NativeBurstDispatchCanonicalRuleKindV2 canonicalRuleKind)
        {
            FieldOrdinal = fieldOrdinal;
            FirstElementIndex = firstElementIndex;
            ByteOffset = byteOffset;
            ElementCount = elementCount;
            ElementSize = elementSize;
            Encoding = encoding;
            CanonicalRuleKind = canonicalRuleKind;
        }

        internal uint FieldOrdinal { get; }
        internal uint FirstElementIndex { get; }
        internal uint ByteOffset { get; }
        internal uint ElementCount { get; }
        internal uint ElementSize { get; }
        internal NativeBurstDispatchFieldEncodingV2 Encoding { get; }
        internal NativeBurstDispatchCanonicalRuleKindV2 CanonicalRuleKind { get; }
    }

    internal readonly struct NativeBurstDispatchCaseV2
    {
        internal NativeBurstDispatchCaseV2(
            ulong typeNumericId,
            uint typeVersion,
            uint catalogCaseIndex,
            uint firstConfigurationField,
            uint configurationFieldCount,
            uint configurationSize,
            uint firstMemoryField,
            uint memoryFieldCount,
            uint memorySize,
            NativeBurstDispatchPhaseMaskV2 phases,
            BurstNodeStatusMask possibleStatuses,
            bool hasRandomStream)
            : this(
                typeNumericId,
                typeVersion,
                catalogCaseIndex,
                firstConfigurationField,
                configurationFieldCount,
                configurationSize,
                firstMemoryField,
                memoryFieldCount,
                memorySize,
                phases,
                possibleStatuses,
                hasRandomStream,
                0u,
                0u)
        {
        }

        internal NativeBurstDispatchCaseV2(
            ulong typeNumericId,
            uint typeVersion,
            uint catalogCaseIndex,
            uint firstConfigurationField,
            uint configurationFieldCount,
            uint configurationSize,
            uint firstMemoryField,
            uint memoryFieldCount,
            uint memorySize,
            NativeBurstDispatchPhaseMaskV2 phases,
            BurstNodeStatusMask possibleStatuses,
            bool hasRandomStream,
            uint firstBinding,
            uint bindingCount)
        {
            TypeNumericId = typeNumericId;
            TypeVersion = typeVersion;
            CatalogCaseIndex = catalogCaseIndex;
            FirstConfigurationField = firstConfigurationField;
            ConfigurationFieldCount = configurationFieldCount;
            ConfigurationSize = configurationSize;
            FirstMemoryField = firstMemoryField;
            MemoryFieldCount = memoryFieldCount;
            MemorySize = memorySize;
            Phases = phases;
            PossibleStatuses = possibleStatuses;
            HasRandomStream = hasRandomStream ? (byte)1 : (byte)0;
            FirstBinding = firstBinding;
            BindingCount = bindingCount;
        }

        internal ulong TypeNumericId { get; }
        internal uint TypeVersion { get; }
        internal uint CatalogCaseIndex { get; }
        internal uint FirstConfigurationField { get; }
        internal uint ConfigurationFieldCount { get; }
        internal uint ConfigurationSize { get; }
        internal uint FirstMemoryField { get; }
        internal uint MemoryFieldCount { get; }
        internal uint MemorySize { get; }
        internal NativeBurstDispatchPhaseMaskV2 Phases { get; }
        internal BurstNodeStatusMask PossibleStatuses { get; }
        internal byte HasRandomStream { get; }
        internal uint FirstBinding { get; }
        internal uint BindingCount { get; }
    }

    internal readonly struct NativeBurstDispatchRequestV2
    {
        internal NativeBurstDispatchRequestV2(
            uint instanceOrdinal,
            uint runtimeNodeIndex,
            ulong typeNumericId,
            uint typeVersion,
            uint catalogCaseIndex,
            BurstCallbackPhase phase,
            uint configurationOffset,
            uint memoryOffset,
            uint randomStateIndex,
            long timeMicroseconds,
            BurstNodeAbortReason abortReason = BurstNodeAbortReason.Explicit,
            BurstNodeExitReason exitReason = BurstNodeExitReason.Success)
            : this(
                instanceOrdinal,
                runtimeNodeIndex,
                typeNumericId,
                typeVersion,
                catalogCaseIndex,
                phase,
                configurationOffset,
                memoryOffset,
                randomStateIndex,
                timeMicroseconds,
                new AIBT.TreeInstanceId((ulong)instanceOrdinal + 1UL),
                1u,
                0u,
                0u,
                abortReason,
                exitReason)
        {
        }

        internal NativeBurstDispatchRequestV2(
            uint instanceOrdinal,
            uint runtimeNodeIndex,
            ulong typeNumericId,
            uint typeVersion,
            uint catalogCaseIndex,
            BurstCallbackPhase phase,
            uint configurationOffset,
            uint memoryOffset,
            uint randomStateIndex,
            long timeMicroseconds,
            AIBT.TreeInstanceId treeInstanceId,
            uint activationGeneration,
            uint firstResolvedBinding,
            uint resolvedBindingCount,
            BurstNodeAbortReason abortReason = BurstNodeAbortReason.Explicit,
            BurstNodeExitReason exitReason = BurstNodeExitReason.Success)
        {
            InstanceOrdinal = instanceOrdinal;
            RuntimeNodeIndex = runtimeNodeIndex;
            TypeNumericId = typeNumericId;
            TypeVersion = typeVersion;
            CatalogCaseIndex = catalogCaseIndex;
            Phase = phase;
            ConfigurationOffset = configurationOffset;
            MemoryOffset = memoryOffset;
            RandomStateIndex = randomStateIndex;
            TimeMicroseconds = timeMicroseconds;
            AbortReason = abortReason;
            ExitReason = exitReason;
            TreeInstanceId = treeInstanceId;
            ActivationGeneration = activationGeneration;
            FirstResolvedBinding = firstResolvedBinding;
            ResolvedBindingCount = resolvedBindingCount;
        }

        internal uint InstanceOrdinal { get; }
        internal uint RuntimeNodeIndex { get; }
        internal ulong TypeNumericId { get; }
        internal uint TypeVersion { get; }
        internal uint CatalogCaseIndex { get; }
        internal BurstCallbackPhase Phase { get; }
        internal uint ConfigurationOffset { get; }
        internal uint MemoryOffset { get; }
        internal uint RandomStateIndex { get; }
        internal long TimeMicroseconds { get; }
        internal BurstNodeAbortReason AbortReason { get; }
        internal BurstNodeExitReason ExitReason { get; }
        internal AIBT.TreeInstanceId TreeInstanceId { get; }
        internal uint ActivationGeneration { get; }
        internal uint FirstResolvedBinding { get; }
        internal uint ResolvedBindingCount { get; }
    }

    internal readonly struct NativeBurstDispatchCreateInputV2
    {
        internal NativeBurstDispatchCreateInputV2(
            BurstCatalogHandshake handshake,
            NativeArray<NativeBurstDispatchCaseV2>.ReadOnly cases,
            NativeArray<NativeBurstDispatchRequestV2>.ReadOnly requests,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly configurationFields,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly memoryFields,
            NativeArray<byte>.ReadOnly configurationBytes,
            NativeArray<byte>.ReadOnly memoryBytes,
            NativeArray<ulong>.ReadOnly randomStates,
            NativeArray<ulong>.ReadOnly randomIncrements)
            : this(
                handshake,
                cases,
                requests,
                configurationFields,
                memoryFields,
                configurationBytes,
                memoryBytes,
                randomStates,
                randomIncrements,
                default,
                default)
        {
        }

        internal NativeBurstDispatchCreateInputV2(
            BurstCatalogHandshake handshake,
            NativeArray<NativeBurstDispatchCaseV2>.ReadOnly cases,
            NativeArray<NativeBurstDispatchRequestV2>.ReadOnly requests,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly configurationFields,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly memoryFields,
            NativeArray<byte>.ReadOnly configurationBytes,
            NativeArray<byte>.ReadOnly memoryBytes,
            NativeArray<ulong>.ReadOnly randomStates,
            NativeArray<ulong>.ReadOnly randomIncrements,
            NativeBurstDispatchBindingInputV2 bindingInput)
            : this(
                handshake,
                cases,
                requests,
                configurationFields,
                memoryFields,
                configurationBytes,
                memoryBytes,
                randomStates,
                randomIncrements,
                bindingInput,
                bindingInput.CanonicalInput)
        {
        }

        internal NativeBurstDispatchCreateInputV2(
            BurstCatalogHandshake handshake,
            NativeArray<NativeBurstDispatchCaseV2>.ReadOnly cases,
            NativeArray<NativeBurstDispatchRequestV2>.ReadOnly requests,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly configurationFields,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly memoryFields,
            NativeArray<byte>.ReadOnly configurationBytes,
            NativeArray<byte>.ReadOnly memoryBytes,
            NativeArray<ulong>.ReadOnly randomStates,
            NativeArray<ulong>.ReadOnly randomIncrements,
            NativeBurstDispatchBindingInputV2 bindingInput,
            NativeBurstDispatchCanonicalInputV2 canonicalInput)
        {
            Handshake = handshake;
            Cases = cases;
            Requests = requests;
            ConfigurationFields = configurationFields;
            MemoryFields = memoryFields;
            ConfigurationBytes = configurationBytes;
            MemoryBytes = memoryBytes;
            RandomStates = randomStates;
            RandomIncrements = randomIncrements;
            BindingInput = bindingInput;
            CanonicalInput = canonicalInput;
        }

        internal BurstCatalogHandshake Handshake { get; }
        internal NativeArray<NativeBurstDispatchCaseV2>.ReadOnly Cases { get; }
        internal NativeArray<NativeBurstDispatchRequestV2>.ReadOnly Requests { get; }
        internal NativeArray<NativeBurstDispatchFieldV2>.ReadOnly ConfigurationFields { get; }
        internal NativeArray<NativeBurstDispatchFieldV2>.ReadOnly MemoryFields { get; }
        internal NativeArray<byte>.ReadOnly ConfigurationBytes { get; }
        internal NativeArray<byte>.ReadOnly MemoryBytes { get; }
        internal NativeArray<ulong>.ReadOnly RandomStates { get; }
        internal NativeArray<ulong>.ReadOnly RandomIncrements { get; }
        internal NativeBurstDispatchBindingInputV2 BindingInput { get; }
        internal NativeBurstDispatchCanonicalInputV2 CanonicalInput { get; }
    }

    internal enum NativeBurstDispatchStateV2 : byte
    {
        Ready = 0,
        Running = 1,
        Terminal = 2,
        Disposed = 3
    }

    internal enum NativeBurstBatchRoleV2 : byte
    {
        Host = 0,
        ScheduledHost = 1,
        Job = 2,
        CompletedHost = 3
    }

    internal struct NativeBurstDispatchControlV2
    {
        internal ulong OwnerId;
        internal uint Generation;
        internal NativeBurstDispatchStateV2 State;
        internal uint Cursor;
        internal uint ActiveFrameId;
        internal uint NextFrameId;
        internal byte MemoryCommitted;
        internal BurstContextResult FirstFailure;
        internal BurstExecutionCode ResultCode;
        internal ushort DiagnosticNumber;
        internal uint InstancesVisited;
        internal ulong SegmentSteps;
    }
}
