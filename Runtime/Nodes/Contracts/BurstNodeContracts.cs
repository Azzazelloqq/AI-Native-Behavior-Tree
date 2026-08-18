using System;
using System.Runtime.InteropServices;

namespace AIBT.Burst
{
    public enum BurstNodeKind : byte { Condition = 0, Action = 1 }
    public enum BurstCancellationMode : byte { NotApplicable = 0, AbortOnly = 1, Command = 2 }
    public enum BurstNodeCost : byte { Trivial = 0, Low = 1, Medium = 2, High = 3, Variable = 4 }

    [Flags]
    public enum BurstNodeStatusMask : byte
    {
        None = 0,
        Success = 1,
        Failure = 2,
        Running = 4
    }

    public enum ConditionResult : byte { Success = 0, Failure = 1 }
    public enum BurstNodeExitReason : byte { Success = 0, Failure = 1, Aborted = 2 }
    public enum BurstNodeAbortReason : byte
    {
        Explicit = 0,
        ObserverSelf = 1,
        ObserverLowerPriority = 2,
        TreeStopped = 3,
        HotReload = 4,
        Timeout = 5
    }

    public enum BurstBlackboardAccess : byte { Read = 0, Write = 1, ReadWrite = 2 }
    public enum BurstContextResult : byte
    {
        Success = 0,
        InvalidHandle = 1,
        TypeMismatch = 2,
        PhaseViolation = 3,
        CapacityExceeded = 4,
        StaleCompletion = 5,
        Overflow = 6,
        InvalidEncoding = 7,
        IncompleteValue = 8,
        AlreadyCommitted = 9,
        InvalidStatus = 10
    }

    public enum BurstCompletionOutcome : byte { Succeeded = 0, Failed = 1, Cancelled = 2 }
    public enum BurstCallbackPhase : byte { Enter = 0, Tick = 1, Abort = 2, Exit = 3, Observer = 4 }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 32)]
    public readonly struct BurstHash256
    {
        public BurstHash256(
            uint word0, uint word1, uint word2, uint word3,
            uint word4, uint word5, uint word6, uint word7)
        {
            Word0 = word0;
            Word1 = word1;
            Word2 = word2;
            Word3 = word3;
            Word4 = word4;
            Word5 = word5;
            Word6 = word6;
            Word7 = word7;
        }

        public uint Word0 { get; }
        public uint Word1 { get; }
        public uint Word2 { get; }
        public uint Word3 { get; }
        public uint Word4 { get; }
        public uint Word5 { get; }
        public uint Word6 { get; }
        public uint Word7 { get; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 32)]
    public readonly struct BurstCatalogFingerprint
    {
        public BurstCatalogFingerprint(BurstHash256 value) { Value = value; }
        public BurstHash256 Value { get; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 172)]
    public readonly struct BurstCatalogHandshake
    {
        public BurstCatalogHandshake(
            uint abiVersion,
            BurstCatalogFingerprint catalog,
            BurstHash256 nodeRegistry,
            uint compiledFormatVersion,
            uint executionSemanticsVersion,
            BurstHash256 configurationLayout,
            BurstHash256 memoryLayout,
            BurstHash256 accessLayout)
        {
            AbiVersion = abiVersion;
            Catalog = catalog;
            NodeRegistry = nodeRegistry;
            CompiledFormatVersion = compiledFormatVersion;
            ExecutionSemanticsVersion = executionSemanticsVersion;
            ConfigurationLayout = configurationLayout;
            MemoryLayout = memoryLayout;
            AccessLayout = accessLayout;
        }

        public uint AbiVersion { get; }
        public BurstCatalogFingerprint Catalog { get; }
        public BurstHash256 NodeRegistry { get; }
        public uint CompiledFormatVersion { get; }
        public uint ExecutionSemanticsVersion { get; }
        public BurstHash256 ConfigurationLayout { get; }
        public BurstHash256 MemoryLayout { get; }
        public BurstHash256 AccessLayout { get; }
    }

    public enum BurstCatalogValidationCode : byte
    {
        Success = 0,
        AbiVersionMismatch = 1,
        CatalogMismatch = 2,
        RegistryMismatch = 3,
        CompiledFormatMismatch = 4,
        SemanticsMismatch = 5,
        ConfigurationLayoutMismatch = 6,
        MemoryLayoutMismatch = 7,
        AccessLayoutMismatch = 8
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2, Size = 4)]
    public readonly struct BurstCatalogValidationResult
    {
        private readonly ushort _codeWord;
        private readonly ushort _diagnosticNumber;

        public BurstCatalogValidationResult(BurstCatalogValidationCode code, ushort diagnosticNumber)
        {
            _codeWord = (ushort)(byte)code;
            _diagnosticNumber = diagnosticNumber;
        }

        public BurstCatalogValidationCode Code => (BurstCatalogValidationCode)(byte)_codeWord;
        internal ushort CodeWord => _codeWord;
        public ushort DiagnosticNumber => _diagnosticNumber;
        public bool Success => Code == BurstCatalogValidationCode.Success;
    }

    public enum BurstExecutionCode : byte { Success = 0, ValidationFailed = 1, Faulted = 2 }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 16)]
    public readonly struct BurstExecutionResult
    {
        private readonly ushort _codeWord;
        private readonly ushort _diagnosticNumber;
        private readonly uint _instancesVisited;
        private readonly ulong _segmentSteps;

        public BurstExecutionResult(
            BurstExecutionCode code,
            ushort diagnosticNumber,
            uint instancesVisited,
            ulong segmentSteps)
        {
            _codeWord = (ushort)(byte)code;
            _diagnosticNumber = diagnosticNumber;
            _instancesVisited = instancesVisited;
            _segmentSteps = segmentSteps;
        }

        public BurstExecutionCode Code => (BurstExecutionCode)(byte)_codeWord;
        public ushort DiagnosticNumber => _diagnosticNumber;
        public uint InstancesVisited => _instancesVisited;
        public ulong SegmentSteps => _segmentSteps;
        public bool Success => Code == BurstExecutionCode.Success;
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtBurstNodeAttribute : Attribute
    {
        public AibtBurstNodeAttribute(
            string canonicalTypeId,
            uint nodeTypeVersion,
            BurstNodeKind kind,
            Type configurationType,
            Type memoryType,
            AIBT.NodeMemoryLifetime memoryLifetime,
            bool deterministic,
            BurstCancellationMode cancellation,
            BurstNodeCost cost,
            BurstNodeStatusMask possibleStatuses) { }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtNodeDocumentationAttribute : Attribute
    {
        public AibtNodeDocumentationAttribute(
            string summary,
            string category,
            string whenToUse,
            string whenNotToUse,
            params string[] exampleIds) { }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtObserverConditionAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtRandomStreamAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtConfigFieldAttribute : Attribute
    {
        public AibtConfigFieldAttribute(string fieldId, string valueTypeId, uint valueTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtMemoryFieldAttribute : Attribute
    {
        public AibtMemoryFieldAttribute(string fieldId, string valueTypeId, uint valueTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtBurstValueAttribute : Attribute
    {
        public AibtBurstValueAttribute(string canonicalTypeId, uint valueTypeVersion, string canonicalSchemaId) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtValueFieldAttribute : Attribute
    {
        public AibtValueFieldAttribute(string fieldId, string valueTypeId, uint valueTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtBlackboardBindingAttribute : Attribute
    {
        public AibtBlackboardBindingAttribute(
            string bindingId,
            BurstBlackboardAccess access,
            AIBT.BlackboardScope scope,
            string valueTypeId,
            uint valueTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtSnapshotBindingAttribute : Attribute
    {
        public AibtSnapshotBindingAttribute(string bindingId, string valueTypeId, uint valueTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtCommandBindingAttribute : Attribute
    {
        public AibtCommandBindingAttribute(string bindingId, string payloadTypeId, uint payloadTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtAsyncOperationBindingAttribute : Attribute
    {
        public AibtAsyncOperationBindingAttribute(
            string bindingId,
            string startPayloadTypeId,
            uint startPayloadTypeVersion,
            string cancelPayloadTypeId,
            uint cancelPayloadTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class AibtCompletionBindingAttribute : Attribute
    {
        public AibtCompletionBindingAttribute(string bindingId, string payloadTypeId, uint payloadTypeVersion) { }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtCatalogShardAttribute : Attribute
    {
        public AibtCatalogShardAttribute(string shardId, uint shardVersion) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AibtCatalogSetAttribute : Attribute
    {
        public AibtCatalogSetAttribute(string catalogId, uint catalogVersion, params Type[] shardTypes) { }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct BlackboardReadHandle<T> where T : unmanaged
    {
        private readonly uint _ordinal;
        private readonly uint _accessToken;
        internal BlackboardReadHandle(uint ordinal, uint accessToken) { _ordinal = ordinal; _accessToken = accessToken; }
        internal uint Ordinal => _ordinal;
        internal uint AccessToken => _accessToken;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct BlackboardWriteHandle<T> where T : unmanaged
    {
        private readonly uint _ordinal;
        private readonly uint _accessToken;
        internal BlackboardWriteHandle(uint ordinal, uint accessToken) { _ordinal = ordinal; _accessToken = accessToken; }
        internal uint Ordinal => _ordinal;
        internal uint AccessToken => _accessToken;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct BlackboardReadWriteHandle<T> where T : unmanaged
    {
        private readonly uint _ordinal;
        private readonly uint _accessToken;
        internal BlackboardReadWriteHandle(uint ordinal, uint accessToken) { _ordinal = ordinal; _accessToken = accessToken; }
        internal uint Ordinal => _ordinal;
        internal uint AccessToken => _accessToken;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct SnapshotReadHandle<T> where T : unmanaged
    {
        private readonly uint _ordinal;
        private readonly uint _accessToken;
        internal SnapshotReadHandle(uint ordinal, uint accessToken) { _ordinal = ordinal; _accessToken = accessToken; }
        internal uint Ordinal => _ordinal;
        internal uint AccessToken => _accessToken;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct CommandHandle<T> where T : unmanaged
    {
        private readonly uint _ordinal;
        private readonly uint _accessToken;
        internal CommandHandle(uint ordinal, uint accessToken) { _ordinal = ordinal; _accessToken = accessToken; }
        internal uint Ordinal => _ordinal;
        internal uint AccessToken => _accessToken;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct AsyncOperationHandle<TStart, TCancel>
        where TStart : unmanaged
        where TCancel : unmanaged
    {
        private readonly uint _ordinal;
        private readonly uint _accessToken;
        internal AsyncOperationHandle(uint ordinal, uint accessToken) { _ordinal = ordinal; _accessToken = accessToken; }
        internal uint Ordinal => _ordinal;
        internal uint AccessToken => _accessToken;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
    public readonly struct CompletionHandle<T> where T : unmanaged
    {
        private readonly uint _ordinal;
        private readonly uint _accessToken;
        internal CompletionHandle(uint ordinal, uint accessToken) { _ordinal = ordinal; _accessToken = accessToken; }
        internal uint Ordinal => _ordinal;
        internal uint AccessToken => _accessToken;
    }

    public struct BurstValueReader
    {
        private ulong _validationToken;
        private BurstDispatchBackingV2 _runtime;
        private uint _frameId;
        private uint _bindingOrdinal;
        private uint _state;
        private AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 _role;

        internal BurstValueReader(
            ulong validationToken,
            BurstDispatchBackingV2 runtime,
            uint frameId,
            uint bindingOrdinal,
            uint sessionOrdinal,
            AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 role)
        {
            _validationToken = validationToken;
            _runtime = runtime;
            _frameId = frameId;
            _bindingOrdinal = bindingOrdinal;
            _state = sessionOrdinal + 1u;
            _role = role;
        }

        internal ulong ValidationToken => _validationToken;
        internal BurstDispatchBackingV2 Runtime => _runtime;
        internal uint FrameId => _frameId;
        internal uint BindingOrdinal => _bindingOrdinal;
        internal AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 Role => _role;
        internal bool TryGetSessionOrdinal(out uint value)
        {
            value = _state - 1u;
            return _state != 0;
        }
    }

    public struct BurstValueWriter
    {
        private ulong _validationToken;
        private BurstDispatchBackingV2 _runtime;
        private uint _frameId;
        private uint _bindingOrdinal;
        private uint _state;
        private AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 _role;

        internal BurstValueWriter(
            ulong validationToken,
            BurstDispatchBackingV2 runtime,
            uint frameId,
            uint bindingOrdinal,
            uint sessionOrdinal,
            AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 role)
        {
            _validationToken = validationToken;
            _runtime = runtime;
            _frameId = frameId;
            _bindingOrdinal = bindingOrdinal;
            _state = sessionOrdinal + 1u;
            _role = role;
        }

        internal ulong ValidationToken => _validationToken;
        internal BurstDispatchBackingV2 Runtime => _runtime;
        internal uint FrameId => _frameId;
        internal uint BindingOrdinal => _bindingOrdinal;
        internal AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 Role => _role;
        internal bool TryGetSessionOrdinal(out uint value)
        {
            value = _state - 1u;
            return _state != 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct BurstEnterContext
    {
        private readonly ulong _validationToken;
        private ulong _randomState;
        private readonly ulong _randomIncrement;
        private BurstDispatchBackingV2 _runtime;
        private uint _frameId;
        private AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 _role;

        internal BurstEnterContext(ulong validationToken, ulong randomState, ulong randomIncrement, BurstDispatchBackingV2 runtime, uint frameId, AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 role)
        {
            _validationToken = validationToken;
            _randomState = randomState;
            _randomIncrement = randomIncrement;
            _runtime = runtime;
            _frameId = frameId;
            _role = role;
        }

        internal ulong ValidationToken => _validationToken;
        internal ulong RandomState => _randomState;
        internal ulong RandomIncrement => _randomIncrement;
        internal uint FrameId => _frameId;
        internal BurstDispatchBackingV2 Runtime => _runtime;
        internal AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 Role => _role;

        public BurstContextResult TryGetTimeMicroseconds(out long value) => BurstBindingBridgeCoreV2.TryGetTime(_runtime, _frameId, _validationToken, _role, out value);
        public BurstContextResult TryNextUInt32(out uint value) => BurstBindingBridgeCoreV2.TryNextUInt32(_runtime, _frameId, _validationToken, _role, _randomIncrement, ref _randomState, out value);
        public BurstContextResult TryNextUInt32(uint boundExclusive, out uint value) => BurstBindingBridgeCoreV2.TryNextUInt32(_runtime, _frameId, _validationToken, _role, _randomIncrement, ref _randomState, boundExclusive, out value);
        public BurstContextResult TryNextFloat32(out float value) => BurstBindingBridgeCoreV2.TryNextFloat32(_runtime, _frameId, _validationToken, _role, _randomIncrement, ref _randomState, out value);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginBlackboardRead(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, AIBT.Execution.Burst.Dispatch.NativeBurstDispatchBindingKindV2.BlackboardRead, BurstCallbackPhase.Enter, out reader);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadWriteHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginBlackboardRead(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, AIBT.Execution.Burst.Dispatch.NativeBurstDispatchBindingKindV2.BlackboardReadWrite, BurstCallbackPhase.Enter, out reader);
        public BurstContextResult TryBeginBlackboardWrite<T>(BlackboardWriteHandle<T> handle, out BurstValueWriter writer) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginBlackboardWrite(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, AIBT.Execution.Burst.Dispatch.NativeBurstDispatchBindingKindV2.BlackboardWrite, BurstCallbackPhase.Enter, out writer);
        public BurstContextResult TryBeginBlackboardWrite<T>(BlackboardReadWriteHandle<T> handle, out BurstValueWriter writer) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginBlackboardWrite(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, AIBT.Execution.Burst.Dispatch.NativeBurstDispatchBindingKindV2.BlackboardReadWrite, BurstCallbackPhase.Enter, out writer);
        public BurstContextResult TryBeginSnapshotRead<T>(SnapshotReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginSnapshotRead(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, BurstCallbackPhase.Enter, out reader);
        public BurstContextResult TryBeginConsume<T>(CompletionHandle<T> handle, AIBT.OperationId operationId, out BurstCompletionOutcome outcome, out BurstValueReader reader) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginConsume(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, BurstCallbackPhase.Enter, operationId, out outcome, out reader);
        public BurstContextResult TryBeginEffect<T>(CommandHandle<T> handle, out BurstValueWriter writer) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginEffect(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, BurstCallbackPhase.Enter, out writer);
        public BurstContextResult TryBeginStart<TStart, TCancel>(AsyncOperationHandle<TStart, TCancel> handle, out BurstValueWriter startWriter, out BurstValueWriter faultCancelWriter) where TStart : unmanaged where TCancel : unmanaged => BurstBindingBridgeCoreV2.TryBeginStart(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, BurstCallbackPhase.Enter, out startWriter, out faultCancelWriter);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct BurstTickContext
    {
        private readonly ulong _validationToken;
        private ulong _randomState;
        private readonly ulong _randomIncrement;
        private BurstDispatchBackingV2 _runtime;
        private uint _frameId;
        private AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 _role;

        internal BurstTickContext(ulong validationToken, ulong randomState, ulong randomIncrement, BurstDispatchBackingV2 runtime, uint frameId, AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 role)
        {
            _validationToken = validationToken;
            _randomState = randomState;
            _randomIncrement = randomIncrement;
            _runtime = runtime;
            _frameId = frameId;
            _role = role;
        }

        internal ulong ValidationToken => _validationToken;
        internal ulong RandomState => _randomState;
        internal ulong RandomIncrement => _randomIncrement;
        internal uint FrameId => _frameId;
        internal BurstDispatchBackingV2 Runtime => _runtime;
        internal AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 Role => _role;

        public BurstContextResult TryGetTimeMicroseconds(out long value) => BurstBindingBridgeCoreV2.TryGetTime(_runtime, _frameId, _validationToken, _role, out value);
        public BurstContextResult TryNextUInt32(out uint value) => BurstBindingBridgeCoreV2.TryNextUInt32(_runtime, _frameId, _validationToken, _role, _randomIncrement, ref _randomState, out value);
        public BurstContextResult TryNextUInt32(uint boundExclusive, out uint value) => BurstBindingBridgeCoreV2.TryNextUInt32(_runtime, _frameId, _validationToken, _role, _randomIncrement, ref _randomState, boundExclusive, out value);
        public BurstContextResult TryNextFloat32(out float value) => BurstBindingBridgeCoreV2.TryNextFloat32(_runtime, _frameId, _validationToken, _role, _randomIncrement, ref _randomState, out value);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginBlackboardRead(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, AIBT.Execution.Burst.Dispatch.NativeBurstDispatchBindingKindV2.BlackboardRead, BurstCallbackPhase.Tick, out reader);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadWriteHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginBlackboardRead(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, AIBT.Execution.Burst.Dispatch.NativeBurstDispatchBindingKindV2.BlackboardReadWrite, BurstCallbackPhase.Tick, out reader);
        public BurstContextResult TryBeginBlackboardWrite<T>(BlackboardWriteHandle<T> handle, out BurstValueWriter writer) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginBlackboardWrite(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, AIBT.Execution.Burst.Dispatch.NativeBurstDispatchBindingKindV2.BlackboardWrite, BurstCallbackPhase.Tick, out writer);
        public BurstContextResult TryBeginBlackboardWrite<T>(BlackboardReadWriteHandle<T> handle, out BurstValueWriter writer) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginBlackboardWrite(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, AIBT.Execution.Burst.Dispatch.NativeBurstDispatchBindingKindV2.BlackboardReadWrite, BurstCallbackPhase.Tick, out writer);
        public BurstContextResult TryBeginSnapshotRead<T>(SnapshotReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginSnapshotRead(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, BurstCallbackPhase.Tick, out reader);
        public BurstContextResult TryBeginConsume<T>(CompletionHandle<T> handle, AIBT.OperationId operationId, out BurstCompletionOutcome outcome, out BurstValueReader reader) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginConsume(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, BurstCallbackPhase.Tick, operationId, out outcome, out reader);
        public BurstContextResult TryBeginEffect<T>(CommandHandle<T> handle, out BurstValueWriter writer) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginEffect(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, BurstCallbackPhase.Tick, out writer);
        public BurstContextResult TryBeginStart<TStart, TCancel>(AsyncOperationHandle<TStart, TCancel> handle, out BurstValueWriter startWriter, out BurstValueWriter faultCancelWriter) where TStart : unmanaged where TCancel : unmanaged => BurstBindingBridgeCoreV2.TryBeginStart(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, BurstCallbackPhase.Tick, out startWriter, out faultCancelWriter);
    }

    public readonly struct BurstAbortContext
    {
        private readonly ulong _validationToken;
        private readonly BurstDispatchBackingV2 _runtime;
        private readonly uint _frameId;
        private readonly AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 _role;

        internal BurstAbortContext(ulong validationToken, BurstDispatchBackingV2 runtime, uint frameId, AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 role)
        {
            _validationToken = validationToken;
            _runtime = runtime;
            _frameId = frameId;
            _role = role;
        }

        internal ulong ValidationToken => _validationToken;
        internal BurstDispatchBackingV2 Runtime => _runtime;
        internal uint FrameId => _frameId;
        internal AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 Role => _role;

        public BurstContextResult TryBeginCancel<TStart, TCancel>(AsyncOperationHandle<TStart, TCancel> handle, AIBT.OperationId operationId, out BurstValueWriter writer) where TStart : unmanaged where TCancel : unmanaged => BurstBindingBridgeCoreV2.TryBeginCancel(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, operationId, out writer);
    }

    public readonly struct BurstExitContext { }

    public readonly struct BurstObserverContext
    {
        private readonly ulong _validationToken;
        private readonly BurstDispatchBackingV2 _runtime;
        private readonly uint _frameId;
        private readonly AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 _role;

        internal BurstObserverContext(ulong validationToken, BurstDispatchBackingV2 runtime, uint frameId, AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 role)
        {
            _validationToken = validationToken;
            _runtime = runtime;
            _frameId = frameId;
            _role = role;
        }

        internal ulong ValidationToken => _validationToken;
        internal BurstDispatchBackingV2 Runtime => _runtime;
        internal uint FrameId => _frameId;
        internal AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 Role => _role;

        public BurstContextResult TryGetTimeMicroseconds(out long value) => BurstBindingBridgeCoreV2.TryGetTime(_runtime, _frameId, _validationToken, _role, out value);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginBlackboardRead(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, AIBT.Execution.Burst.Dispatch.NativeBurstDispatchBindingKindV2.BlackboardRead, BurstCallbackPhase.Observer, out reader);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadWriteHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginBlackboardRead(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, AIBT.Execution.Burst.Dispatch.NativeBurstDispatchBindingKindV2.BlackboardReadWrite, BurstCallbackPhase.Observer, out reader);
        public BurstContextResult TryBeginSnapshotRead<T>(SnapshotReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstBindingBridgeCoreV2.TryBeginSnapshotRead(_runtime, _frameId, _validationToken, _role, handle.Ordinal, handle.AccessToken, BurstCallbackPhase.Observer, out reader);
    }

    public readonly struct BurstDispatchFrame
    {
        private readonly ulong _validationToken;
        private readonly BurstDispatchBackingV2 _runtime;
        private readonly uint _frameId;
        private readonly uint _requestOrdinal;
        private readonly AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 _role;

        internal BurstDispatchFrame(ulong validationToken, BurstDispatchBackingV2 runtime, uint frameId, uint requestOrdinal, AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 role)
        {
            _validationToken = validationToken;
            _runtime = runtime;
            _frameId = frameId;
            _requestOrdinal = requestOrdinal;
            _role = role;
        }

        internal ulong ValidationToken => _validationToken;
        internal BurstDispatchBackingV2 Runtime => _runtime;
        internal uint FrameId => _frameId;
        internal uint RequestOrdinal => _requestOrdinal;
        internal AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 Role => _role;
    }

    public readonly struct BurstConfigurationReader
    {
        private readonly ulong _validationToken;
        private readonly BurstDispatchBackingV2 _runtime;
        private readonly uint _frameId;
        private readonly uint _requestOrdinal;
        private readonly AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 _role;

        internal BurstConfigurationReader(ulong validationToken, BurstDispatchBackingV2 runtime, uint frameId, uint requestOrdinal, AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 role)
        {
            _validationToken = validationToken;
            _runtime = runtime;
            _frameId = frameId;
            _requestOrdinal = requestOrdinal;
            _role = role;
        }

        internal BurstDispatchBackingV2 Runtime => _runtime;
        internal ulong ValidationToken => _validationToken;
        internal uint FrameId => _frameId;
        internal uint RequestOrdinal => _requestOrdinal;
        internal AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 Role => _role;
    }

    public struct BurstMemoryAccessor
    {
        private ulong _validationToken;
        private BurstDispatchBackingV2 _runtime;
        private uint _frameId;
        private uint _requestOrdinal;
        private AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 _role;

        internal BurstMemoryAccessor(ulong validationToken, BurstDispatchBackingV2 runtime, uint frameId, uint requestOrdinal, AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 role)
        {
            _validationToken = validationToken;
            _runtime = runtime;
            _frameId = frameId;
            _requestOrdinal = requestOrdinal;
            _role = role;
        }

        internal BurstDispatchBackingV2 Runtime => _runtime;
        internal ulong ValidationToken => _validationToken;
        internal uint FrameId => _frameId;
        internal uint RequestOrdinal => _requestOrdinal;
        internal AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 Role => _role;
    }

    public struct BurstExecutionBatch
    {
        private ulong _validationToken;
        private BurstDispatchBackingV2 _runtime;
        private AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 _role;

        internal BurstExecutionBatch(BurstDispatchBackingV2 runtime, AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 role)
        {
            _validationToken = runtime.OwnerId;
            _runtime = runtime;
            _role = role;
        }

        internal BurstDispatchBackingV2 Runtime => _runtime;
        internal AIBT.Execution.Burst.Dispatch.NativeBurstBatchRoleV2 Role { get => _role; set => _role = value; }
        internal bool MatchesOwner(Unity.Collections.NativeArray<AIBT.Execution.Burst.Dispatch.NativeBurstDispatchControlV2> control)
            => _validationToken != 0 && _runtime.Control.Equals(control);
    }

    internal static class BurstNodeContractCore
    {
        internal static BurstContextResult Invalid<T>(out T value)
        {
            value = default;
            return BurstContextResult.InvalidHandle;
        }

        internal static BurstContextResult Invalid<T1, T2>(out T1 value1, out T2 value2)
        {
            value1 = default;
            value2 = default;
            return BurstContextResult.InvalidHandle;
        }
    }
}
