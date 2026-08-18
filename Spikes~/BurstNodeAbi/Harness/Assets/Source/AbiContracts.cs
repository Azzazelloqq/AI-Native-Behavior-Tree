using System;
using System.Runtime.InteropServices;
using AIBT.BurstAbi.Canary;
#if UNITY_6000_0_OR_NEWER
using Unity.Collections;
using Unity.Burst;
#endif

namespace AIBT.Burst
{
#pragma warning disable CS0649 // Opaque ABI state is populated only by the runtime bridge.
    public enum BurstNodeKind : byte { Condition = 0, Action = 1 }
    public enum BurstCancellationMode : byte { NotApplicable = 0, AbortOnly = 1, Command = 2 }
    public enum BurstNodeCost : byte { Trivial = 0, Low = 1, Medium = 2, High = 3, Variable = 4 }
    [Flags] public enum BurstNodeStatusMask : byte { None = 0, Success = 1, Failure = 2, Running = 4 }
    public enum ConditionResult : byte { Success = 0, Failure = 1 }
    public enum BurstNodeExitReason : byte { Success = 0, Failure = 1, Aborted = 2 }
    public enum BurstNodeAbortReason : byte { Explicit = 0, ObserverSelf = 1, ObserverLowerPriority = 2, TreeStopped = 3, HotReload = 4, Timeout = 5 }
    public enum BurstBlackboardAccess : byte { Read = 0, Write = 1, ReadWrite = 2 }
    public enum BurstContextResult : byte { Success = 0, InvalidHandle = 1, TypeMismatch = 2, PhaseViolation = 3, CapacityExceeded = 4, StaleCompletion = 5, Overflow = 6, InvalidEncoding = 7, IncompleteValue = 8, AlreadyCommitted = 9, InvalidStatus = 10 }
    public enum BurstCompletionOutcome : byte { Succeeded = 0, Failed = 1, Cancelled = 2 }
    public enum BurstCallbackPhase : byte { Enter = 0, Tick = 1, Abort = 2, Exit = 3, Observer = 4 }
    internal enum BurstDispatchResult : byte { Success = 0, InvalidFrame = 1, InvalidCase = 2, InvalidPhase = 3 }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 32)]
    public readonly struct BurstHash256
    {
        public BurstHash256(uint word0, uint word1, uint word2, uint word3, uint word4, uint word5, uint word6, uint word7)
        { Word0 = word0; Word1 = word1; Word2 = word2; Word3 = word3; Word4 = word4; Word5 = word5; Word6 = word6; Word7 = word7; }
        public uint Word0 { get; } public uint Word1 { get; } public uint Word2 { get; } public uint Word3 { get; }
        public uint Word4 { get; } public uint Word5 { get; } public uint Word6 { get; } public uint Word7 { get; }
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
        public BurstCatalogHandshake(uint abiVersion, BurstCatalogFingerprint catalog, BurstHash256 nodeRegistry,
            uint compiledFormatVersion, uint executionSemanticsVersion, BurstHash256 configurationLayout,
            BurstHash256 memoryLayout, BurstHash256 accessLayout)
        { AbiVersion = abiVersion; Catalog = catalog; NodeRegistry = nodeRegistry; CompiledFormatVersion = compiledFormatVersion; ExecutionSemanticsVersion = executionSemanticsVersion; ConfigurationLayout = configurationLayout; MemoryLayout = memoryLayout; AccessLayout = accessLayout; }
        public uint AbiVersion { get; } public BurstCatalogFingerprint Catalog { get; } public BurstHash256 NodeRegistry { get; }
        public uint CompiledFormatVersion { get; } public uint ExecutionSemanticsVersion { get; }
        public BurstHash256 ConfigurationLayout { get; } public BurstHash256 MemoryLayout { get; } public BurstHash256 AccessLayout { get; }
    }

    public enum BurstCatalogValidationCode : byte
    {
        Success = 0, AbiVersionMismatch = 1, CatalogMismatch = 2, RegistryMismatch = 3,
        CompiledFormatMismatch = 4, SemanticsMismatch = 5, ConfigurationLayoutMismatch = 6,
        MemoryLayoutMismatch = 7, AccessLayoutMismatch = 8
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2, Size = 4)]
    public readonly struct BurstCatalogValidationResult
    {
        private readonly ushort _codeWord;
        private readonly ushort _diagnosticNumber;
        public BurstCatalogValidationResult(BurstCatalogValidationCode code, ushort diagnosticNumber) { _codeWord = (ushort)(byte)code; _diagnosticNumber = diagnosticNumber; }
        internal BurstCatalogValidationResult(ushort codeWord, ushort diagnosticNumber) { _codeWord = codeWord; _diagnosticNumber = diagnosticNumber; }
        public BurstCatalogValidationCode Code => (BurstCatalogValidationCode)(byte)_codeWord;
        public ushort DiagnosticNumber => _diagnosticNumber;
        public bool Success => Code == BurstCatalogValidationCode.Success;
        internal bool HasCanonicalCodeWord => (_codeWord & 0xff00u) == 0 && (byte)Code <= 8;
    }

    public enum BurstExecutionCode : byte { Success = 0, ValidationFailed = 1, Faulted = 2 }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 16)]
    public readonly struct BurstExecutionResult
    {
        private readonly ushort _codeWord;
        private readonly ushort _diagnosticNumber;
        private readonly uint _instancesVisited;
        private readonly ulong _segmentSteps;
        public BurstExecutionResult(BurstExecutionCode code, ushort diagnosticNumber, uint instancesVisited, ulong segmentSteps)
        { _codeWord = (ushort)(byte)code; _diagnosticNumber = diagnosticNumber; _instancesVisited = instancesVisited; _segmentSteps = segmentSteps; }
        internal BurstExecutionResult(ushort codeWord, ushort diagnosticNumber, uint instancesVisited, ulong segmentSteps)
        { _codeWord = codeWord; _diagnosticNumber = diagnosticNumber; _instancesVisited = instancesVisited; _segmentSteps = segmentSteps; }
        public BurstExecutionCode Code => (BurstExecutionCode)(byte)_codeWord;
        public ushort DiagnosticNumber => _diagnosticNumber;
        public uint InstancesVisited => _instancesVisited;
        public ulong SegmentSteps => _segmentSteps;
        public bool Success => Code == BurstExecutionCode.Success;
        internal bool HasCanonicalCodeWord => (_codeWord & 0xff00u) == 0 && (byte)Code <= 2;
    }
    internal readonly struct BurstFixedInt2
    {
        public BurstFixedInt2(int x, int y) { X = x; Y = y; }
        public int X { get; }
        public int Y { get; }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtBurstNodeAttribute : Attribute
    {
        public AibtBurstNodeAttribute(string canonicalTypeId, uint nodeTypeVersion, BurstNodeKind kind,
            Type configurationType, Type memoryType, AIBT.NodeMemoryLifetime memoryLifetime,
            bool deterministic, BurstCancellationMode cancellation, BurstNodeCost cost,
            BurstNodeStatusMask possibleStatuses) { }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AibtNodeDocumentationAttribute : Attribute
    {
        public AibtNodeDocumentationAttribute(string summary, string category, string whenToUse,
            string whenNotToUse, params string[] exampleIds) { }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)] public sealed class AibtObserverConditionAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)] public sealed class AibtRandomStreamAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)] public sealed class AibtBurstValueAttribute : Attribute { public AibtBurstValueAttribute(string canonicalTypeId, uint valueTypeVersion, string canonicalSchemaId) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)] public sealed class AibtValueFieldAttribute : Attribute { public AibtValueFieldAttribute(string fieldId, string valueTypeId, uint valueTypeVersion) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)] public sealed class AibtConfigFieldAttribute : Attribute { public AibtConfigFieldAttribute(string fieldId, string valueTypeId, uint valueTypeVersion) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)] public sealed class AibtMemoryFieldAttribute : Attribute { public AibtMemoryFieldAttribute(string fieldId, string valueTypeId, uint valueTypeVersion) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)] public sealed class AibtBlackboardBindingAttribute : Attribute { public AibtBlackboardBindingAttribute(string bindingId, BurstBlackboardAccess access, AIBT.BlackboardScope scope, string valueTypeId, uint valueTypeVersion) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)] public sealed class AibtSnapshotBindingAttribute : Attribute { public AibtSnapshotBindingAttribute(string bindingId, string valueTypeId, uint valueTypeVersion) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)] public sealed class AibtCommandBindingAttribute : Attribute { public AibtCommandBindingAttribute(string bindingId, string payloadTypeId, uint payloadTypeVersion) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)] public sealed class AibtAsyncOperationBindingAttribute : Attribute { public AibtAsyncOperationBindingAttribute(string bindingId, string startPayloadTypeId, uint startPayloadTypeVersion, string cancelPayloadTypeId, uint cancelPayloadTypeVersion) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)] public sealed class AibtCompletionBindingAttribute : Attribute { public AibtCompletionBindingAttribute(string bindingId, string payloadTypeId, uint payloadTypeVersion) { } }
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)] public sealed class AibtCatalogShardAttribute : Attribute { public AibtCatalogShardAttribute(string shardId, uint shardVersion) { } }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)] public sealed class AibtCatalogSetAttribute : Attribute { public AibtCatalogSetAttribute(string catalogId, uint catalogVersion, params Type[] shardTypes) { } }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)] public readonly struct BlackboardReadHandle<T> where T : unmanaged { private readonly uint _ordinal; private readonly uint _accessToken; internal BlackboardReadHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } internal bool IsValid(ulong catalog) => _ordinal != uint.MaxValue && _accessToken == unchecked((uint)catalog); }
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)] public readonly struct BlackboardWriteHandle<T> where T : unmanaged { private readonly uint _ordinal; private readonly uint _accessToken; internal BlackboardWriteHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } internal bool IsValid(ulong catalog) => _ordinal != uint.MaxValue && _accessToken == unchecked((uint)catalog); }
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)] public readonly struct BlackboardReadWriteHandle<T> where T : unmanaged { private readonly uint _ordinal; private readonly uint _accessToken; internal BlackboardReadWriteHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } internal bool IsValid(ulong catalog) => _ordinal != uint.MaxValue && _accessToken == unchecked((uint)catalog); }
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)] public readonly struct SnapshotReadHandle<T> where T : unmanaged { private readonly uint _ordinal; private readonly uint _accessToken; internal SnapshotReadHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } internal bool IsValid(ulong catalog) => _ordinal != uint.MaxValue && _accessToken == unchecked((uint)catalog); }
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)] public readonly struct CommandHandle<T> where T : unmanaged { private readonly uint _ordinal; private readonly uint _accessToken; internal CommandHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } internal bool IsValid(ulong catalog) => _ordinal != uint.MaxValue && _accessToken == unchecked((uint)catalog); }
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)] public readonly struct AsyncOperationHandle<TStart, TCancel> where TStart : unmanaged where TCancel : unmanaged { private readonly uint _ordinal; private readonly uint _accessToken; internal AsyncOperationHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } internal bool IsValid(ulong catalog) => _ordinal != uint.MaxValue && _accessToken == unchecked((uint)catalog); }
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)] public readonly struct CompletionHandle<T> where T : unmanaged { private readonly uint _ordinal; private readonly uint _accessToken; internal CompletionHandle(uint ordinal, uint token) { _ordinal = ordinal; _accessToken = token; } internal bool IsValid(ulong catalog) => _ordinal != uint.MaxValue && _accessToken == unchecked((uint)catalog); }

    internal struct BurstContextState
    {
        internal ulong Catalog;
        internal ulong Tree;
        internal uint Node;
        internal uint Generation;
        internal ulong NextSequence;
        internal AIBT.OperationId ActiveOperation;
        internal bool HasCompletion;
        internal bool CompletionConsumed;
        internal bool Cancelled;
        internal long Time;

        internal BurstContextResult Start<TStart, TCancel>(AsyncOperationHandle<TStart, TCancel> handle, out AIBT.OperationId operationId) where TStart : unmanaged where TCancel : unmanaged
        {
            operationId = default;
            if (!handle.IsValid(Catalog)) return BurstContextResult.InvalidHandle;
            if (NextSequence == ulong.MaxValue) return BurstContextResult.Overflow;
            operationId = new AIBT.OperationId(new AIBT.TreeInstanceId(Tree), new AIBT.RuntimeNodeIndex(Node), Generation, ++NextSequence);
            ActiveOperation = operationId;
            HasCompletion = false;
            CompletionConsumed = false;
            Cancelled = false;
            return BurstContextResult.Success;
        }

        internal bool Owns(AIBT.OperationId operationId) => operationId == ActiveOperation && operationId.IsValid;
    }

    internal enum BurstValueKind : byte { Read = 1, BlackboardWrite = 2, Effect = 3, Start = 4, FaultCancel = 5, Cancel = 6, Consume = 7 }
    public struct BurstValueReader { internal ulong Token; internal ulong Word0; internal ulong Word1; internal uint ReadMask; internal BurstValueKind Kind; internal AIBT.OperationId Operation; }
    public struct BurstValueWriter { internal ulong Token; internal ulong Word0; internal ulong Word1; internal uint WriteMask; internal BurstValueKind Kind; internal AIBT.OperationId Operation; internal ulong Tree; internal uint Node; internal uint Generation; }

    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 24)]
    public struct BurstEnterContext
    {
        private readonly ulong _validationToken; private ulong _randomState; private readonly ulong _randomIncrement;
        internal BurstEnterContext(ulong token, ulong state, ulong increment) { _validationToken = token; _randomState = state; _randomIncrement = increment; }
        internal ulong ValidationToken => _validationToken; internal ulong RandomState => _randomState; internal ulong RandomIncrement => _randomIncrement;
        public BurstContextResult TryGetTimeMicroseconds(out long value) { value = 0; return _validationToken == 0 ? BurstContextResult.InvalidHandle : BurstContextResult.Success; }
        public BurstContextResult TryNextUInt32(out uint value) => BurstRandomContextOperations.TryNext(_validationToken, ref _randomState, _randomIncrement, out value);
        public BurstContextResult TryNextUInt32(uint boundExclusive, out uint value) => BurstRandomContextOperations.TryNextBounded(_validationToken, ref _randomState, _randomIncrement, boundExclusive, out value);
        public BurstContextResult TryNextFloat32(out float value) => BurstRandomContextOperations.TryNextFloat(_validationToken, ref _randomState, _randomIncrement, out value);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstContextOperations.BeginRead(_validationToken, handle.IsValid(_validationToken), BurstValueKind.Read, out reader);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadWriteHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstContextOperations.BeginRead(_validationToken, handle.IsValid(_validationToken), BurstValueKind.Read, out reader);
        public BurstContextResult TryBeginBlackboardWrite<T>(BlackboardWriteHandle<T> handle, out BurstValueWriter writer) where T : unmanaged => BurstContextOperations.BeginWrite(_validationToken, handle.IsValid(_validationToken), BurstValueKind.BlackboardWrite, out writer);
        public BurstContextResult TryBeginBlackboardWrite<T>(BlackboardReadWriteHandle<T> handle, out BurstValueWriter writer) where T : unmanaged => BurstContextOperations.BeginWrite(_validationToken, handle.IsValid(_validationToken), BurstValueKind.BlackboardWrite, out writer);
        public BurstContextResult TryBeginSnapshotRead<T>(SnapshotReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstContextOperations.BeginRead(_validationToken, handle.IsValid(_validationToken), BurstValueKind.Read, out reader);
        public BurstContextResult TryBeginConsume<T>(CompletionHandle<T> handle, AIBT.OperationId operationId, out BurstCompletionOutcome outcome, out BurstValueReader reader) where T : unmanaged { outcome = default; if (!operationId.IsValid || !BurstContextOperations.OwnsCompletion(_validationToken, operationId)) { reader = default; return BurstContextResult.StaleCompletion; } var result = BurstContextOperations.BeginRead(_validationToken, handle.IsValid(_validationToken), BurstValueKind.Consume, out reader); reader.Operation = operationId; return result; }
        public BurstContextResult TryBeginEffect<T>(CommandHandle<T> handle, out BurstValueWriter writer) where T : unmanaged => BurstContextOperations.BeginWrite(_validationToken, handle.IsValid(_validationToken), BurstValueKind.Effect, out writer);
        public BurstContextResult TryBeginStart<TStart, TCancel>(AsyncOperationHandle<TStart, TCancel> handle, out BurstValueWriter startWriter, out BurstValueWriter faultCancelWriter) where TStart : unmanaged where TCancel : unmanaged { var valid = handle.IsValid(_validationToken); var result = BurstContextOperations.BeginWrite(_validationToken, valid, BurstValueKind.Start, out startWriter); BurstContextOperations.BeginWrite(_validationToken, valid, BurstValueKind.FaultCancel, out faultCancelWriter); return result; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 24)]
    public struct BurstTickContext
    {
        private readonly ulong _validationToken; private ulong _randomState; private readonly ulong _randomIncrement;
        internal BurstTickContext(ulong token, ulong state, ulong increment) { _validationToken = token; _randomState = state; _randomIncrement = increment; }
        internal ulong ValidationToken => _validationToken; internal ulong RandomState => _randomState; internal ulong RandomIncrement => _randomIncrement;
        public BurstContextResult TryGetTimeMicroseconds(out long value) { value = 0; return _validationToken == 0 ? BurstContextResult.InvalidHandle : BurstContextResult.Success; }
        public BurstContextResult TryNextUInt32(out uint value) => BurstRandomContextOperations.TryNext(_validationToken, ref _randomState, _randomIncrement, out value);
        public BurstContextResult TryNextUInt32(uint boundExclusive, out uint value) => BurstRandomContextOperations.TryNextBounded(_validationToken, ref _randomState, _randomIncrement, boundExclusive, out value);
        public BurstContextResult TryNextFloat32(out float value) => BurstRandomContextOperations.TryNextFloat(_validationToken, ref _randomState, _randomIncrement, out value);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstContextOperations.BeginRead(_validationToken, handle.IsValid(_validationToken), BurstValueKind.Read, out reader);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadWriteHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstContextOperations.BeginRead(_validationToken, handle.IsValid(_validationToken), BurstValueKind.Read, out reader);
        public BurstContextResult TryBeginBlackboardWrite<T>(BlackboardWriteHandle<T> handle, out BurstValueWriter writer) where T : unmanaged => BurstContextOperations.BeginWrite(_validationToken, handle.IsValid(_validationToken), BurstValueKind.BlackboardWrite, out writer);
        public BurstContextResult TryBeginBlackboardWrite<T>(BlackboardReadWriteHandle<T> handle, out BurstValueWriter writer) where T : unmanaged => BurstContextOperations.BeginWrite(_validationToken, handle.IsValid(_validationToken), BurstValueKind.BlackboardWrite, out writer);
        public BurstContextResult TryBeginSnapshotRead<T>(SnapshotReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstContextOperations.BeginRead(_validationToken, handle.IsValid(_validationToken), BurstValueKind.Read, out reader);
        public BurstContextResult TryBeginConsume<T>(CompletionHandle<T> handle, AIBT.OperationId operationId, out BurstCompletionOutcome outcome, out BurstValueReader reader) where T : unmanaged { outcome = default; if (!operationId.IsValid || !BurstContextOperations.OwnsCompletion(_validationToken, operationId)) { reader = default; return BurstContextResult.StaleCompletion; } var result = BurstContextOperations.BeginRead(_validationToken, handle.IsValid(_validationToken), BurstValueKind.Consume, out reader); reader.Operation = operationId; return result; }
        public BurstContextResult TryBeginEffect<T>(CommandHandle<T> handle, out BurstValueWriter writer) where T : unmanaged => BurstContextOperations.BeginWrite(_validationToken, handle.IsValid(_validationToken), BurstValueKind.Effect, out writer);
        public BurstContextResult TryBeginStart<TStart, TCancel>(AsyncOperationHandle<TStart, TCancel> handle, out BurstValueWriter startWriter, out BurstValueWriter faultCancelWriter) where TStart : unmanaged where TCancel : unmanaged { var valid = handle.IsValid(_validationToken); var result = BurstContextOperations.BeginWrite(_validationToken, valid, BurstValueKind.Start, out startWriter); BurstContextOperations.BeginWrite(_validationToken, valid, BurstValueKind.FaultCancel, out faultCancelWriter); return result; }
    }

    internal static class BurstContextOperations
    {
        internal static BurstContextResult BeginRead(ulong token, bool valid, BurstValueKind kind, out BurstValueReader reader) { reader = new BurstValueReader { Token = valid ? token : 0, Kind = kind }; return valid ? BurstContextResult.Success : BurstContextResult.InvalidHandle; }
        internal static BurstContextResult BeginWrite(ulong token, bool valid, BurstValueKind kind, out BurstValueWriter writer) { writer = new BurstValueWriter { Token = valid ? token : 0, Kind = kind, Tree = 1, Node = 0, Generation = 1 }; return valid ? BurstContextResult.Success : BurstContextResult.InvalidHandle; }
        internal static ulong CompletionToken(ulong catalog, AIBT.OperationId operationId)
        {
            var hash = operationId.TreeInstanceId.Value ^ ((ulong)operationId.NodeIndex.Value << 17) ^ ((ulong)operationId.ActivationGeneration << 33) ^ operationId.Sequence;
            hash ^= hash >> 29; hash *= 0x9e3779b97f4a7c15UL; hash ^= hash >> 32;
            return (uint)catalog | ((hash & 0x7fffffffUL) << 32);
        }
        internal static bool OwnsCompletion(ulong token, AIBT.OperationId operationId) => (token & 0x7fffffffffffffffUL) == CompletionToken(token, operationId);
    }
    internal static class BurstRandomContextOperations
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong RandomCapability = 1UL << 63;
        internal static BurstContextResult TryNext(ulong token, ref ulong state, ulong increment, out uint value) { value = 0; var live = BurstContextRuntime.Check(token); if (live != BurstContextResult.Success) return live; if ((token & RandomCapability) == 0 || (increment & 1UL) == 0) return BurstContextRuntime.Latch(token, BurstContextResult.PhaseViolation); value = Advance(ref state, increment); return BurstContextResult.Success; }
        internal static BurstContextResult TryNextBounded(ulong token, ref ulong state, ulong increment, uint bound, out uint value) { value = 0; var live = BurstContextRuntime.Check(token); if (live != BurstContextResult.Success) return live; if ((token & RandomCapability) == 0 || (increment & 1UL) == 0) return BurstContextRuntime.Latch(token, BurstContextResult.PhaseViolation); if (bound == 0) return BurstContextRuntime.Latch(token, BurstContextResult.InvalidStatus); var threshold = unchecked(0u - bound) % bound; uint candidate; do { candidate = Advance(ref state, increment); } while (candidate < threshold); value = candidate % bound; return BurstContextResult.Success; }
        internal static BurstContextResult TryNextFloat(ulong token, ref ulong state, ulong increment, out float value) { var result = TryNext(token, ref state, increment, out var bits); value = result == BurstContextResult.Success ? (bits >> 8) / 16777216.0f : 0f; return result; }
        private static uint Advance(ref ulong state, ulong increment) { var old = state; state = unchecked(old * Multiplier + increment); var shifted = (uint)(((old >> 18) ^ old) >> 27); var rotation = (int)(old >> 59); return (shifted >> rotation) | (shifted << ((-rotation) & 31)); }
    }

    internal static class BurstContextRuntime
    {
        private struct RuntimeState { internal ulong Token; internal byte Failure; }
#if UNITY_6000_0_OR_NEWER
        private struct ContextKey { }
        private static readonly SharedStatic<RuntimeState> Shared = SharedStatic<RuntimeState>.GetOrCreate<ContextKey>();
        private static ref RuntimeState State => ref Shared.Data;
#else
        private static RuntimeState managedState;
        private static ref RuntimeState State => ref managedState;
#endif
        internal static void Activate(ulong token) { State.Token = token; State.Failure = 0; }
        internal static BurstContextResult Check(ulong token) => token == 0 || State.Token != token ? BurstContextResult.InvalidHandle : BurstContextResult.Success;
        internal static BurstContextResult Latch(ulong token, BurstContextResult failure) { if (Check(token) != BurstContextResult.Success) return BurstContextResult.InvalidHandle; if (State.Failure == 0) State.Failure = (byte)failure; return (BurstContextResult)State.Failure; }
        internal static BurstContextResult Completion(ulong token) { var live = Check(token); if (live != BurstContextResult.Success) return live; return State.Failure == 0 ? BurstContextResult.Success : (BurstContextResult)State.Failure; }
        internal static void Expire(ulong token) { if (State.Token == token) { State.Token = 0; State.Failure = 0; } }
    }

    internal static class BurstBatchLifetimeRuntime
    {
#if UNITY_6000_0_OR_NEWER
        private struct BatchKey { }
        private static readonly SharedStatic<ulong> Shared = SharedStatic<ulong>.GetOrCreate<BatchKey>();
        private static ref ulong ActiveIdentity => ref Shared.Data;
#else
        private static ulong activeIdentity;
        private static ref ulong ActiveIdentity => ref activeIdentity;
        private static ulong recordIdentity;
        private static byte recordAlive;
        private static byte recordTerminal;
        private static byte recordScheduleState;
        private static uint recordRemainingWork;
        private static uint recordInstanceOrdinal;
        private static uint recordRuntimeNodeIndex;
        private static uint recordCatalogCaseIndex;
        private static BurstCallbackPhase recordPhase;
        private static uint recordInstancesVisited;
        private static ulong recordSegmentSteps;
        private static BurstExecutionResult recordResult;
#endif
        internal static void Activate(ulong identity) { ActiveIdentity = identity; }
        internal static bool IsLive(ulong identity) => identity != 0 && ActiveIdentity == identity;
        internal static void Expire(ulong identity) { if (ActiveIdentity == identity) ActiveIdentity = 0; }
#if !UNITY_6000_0_OR_NEWER
        internal static void CreateRecord(ulong identity, uint caseIndex, BurstCallbackPhase phase)
        { recordIdentity = identity; recordAlive = 1; recordTerminal = 0; recordScheduleState = 0; recordRemainingWork = 1; recordInstanceOrdinal = 0; recordRuntimeNodeIndex = 0; recordCatalogCaseIndex = caseIndex; recordPhase = phase; recordInstancesVisited = 0; recordSegmentSteps = 0; recordResult = default; }
        internal static bool HasRecord(ulong identity) => recordAlive != 0 && recordIdentity == identity;
        internal static bool TryClaimSchedule(ulong identity)
        { if (!HasRecord(identity) || recordTerminal != 0 || recordRemainingWork == 0 || recordScheduleState != 0) return false; recordScheduleState = 1; return true; }
        internal static void ReadRecord(ulong identity, out byte alive, out byte terminal, out byte scheduleState, out uint remainingWork,
            out uint instanceOrdinal, out uint runtimeNodeIndex, out uint caseIndex, out BurstCallbackPhase phase,
            out uint instancesVisited, out ulong segmentSteps, out BurstExecutionResult result)
        { if (recordIdentity != identity) { alive = terminal = scheduleState = 0; remainingWork = instanceOrdinal = runtimeNodeIndex = caseIndex = instancesVisited = 0; phase = default; segmentSteps = 0; result = default; return; }
          alive = recordAlive; terminal = recordTerminal; scheduleState = recordScheduleState; remainingWork = recordRemainingWork; instanceOrdinal = recordInstanceOrdinal;
          runtimeNodeIndex = recordRuntimeNodeIndex; caseIndex = recordCatalogCaseIndex; phase = recordPhase; instancesVisited = recordInstancesVisited; segmentSteps = recordSegmentSteps; result = recordResult; }
        internal static void UpdateRecord(ulong identity, uint remainingWork, uint instanceOrdinal, uint runtimeNodeIndex, uint caseIndex,
            BurstCallbackPhase phase, uint instancesVisited, ulong segmentSteps)
        { if (!HasRecord(identity)) return; recordRemainingWork = remainingWork; recordInstanceOrdinal = instanceOrdinal; recordRuntimeNodeIndex = runtimeNodeIndex;
          recordCatalogCaseIndex = caseIndex; recordPhase = phase; recordInstancesVisited = instancesVisited; recordSegmentSteps = segmentSteps; }
        internal static void CompleteRecord(ulong identity, bool failure, uint instancesVisited, ulong segmentSteps, in BurstExecutionResult result)
        { if (!HasRecord(identity)) return; recordTerminal = 1; recordRemainingWork = 0; recordInstancesVisited = instancesVisited; recordSegmentSteps = segmentSteps; recordResult = result; }
        internal static void ReleaseRecord(ulong identity) { if (recordIdentity == identity) recordAlive = 0; }
#endif
    }

#if UNITY_6000_0_OR_NEWER
    internal struct BurstSharedRuntimeRecord
    {
        internal byte Alive;
        internal byte Terminal;
        internal byte TerminalFailure;
        internal byte ScheduleState;
        internal uint RemainingWork;
        internal uint InstanceOrdinal;
        internal uint RuntimeNodeIndex;
        internal uint CatalogCaseIndex;
        internal BurstCallbackPhase Phase;
        internal uint InstancesVisited;
        internal ulong SegmentSteps;
        internal BurstExecutionResult Result;
    }
#endif

    public readonly struct BurstAbortContext
    {
        private readonly BurstContextState state;
        internal BurstAbortContext(BurstContextState value) { state = value; }
        public BurstContextResult TryBeginCancel<TStart, TCancel>(AsyncOperationHandle<TStart, TCancel> handle, AIBT.OperationId operationId, out BurstValueWriter writer) where TStart : unmanaged where TCancel : unmanaged { var valid = handle.IsValid(state.Catalog) && state.Owns(operationId); writer = new BurstValueWriter { Token = valid ? state.Catalog : 0, Kind = BurstValueKind.Cancel, Operation = operationId }; return valid ? BurstContextResult.Success : BurstContextResult.StaleCompletion; }
    }
    public readonly struct BurstExitContext { }
    public readonly struct BurstObserverContext
    {
        private readonly BurstContextState state;
        internal BurstObserverContext(BurstContextState value) { state = value; }
        public BurstContextResult TryGetTimeMicroseconds(out long value) { value = state.Time; return state.Catalog == 0 ? BurstContextResult.InvalidHandle : BurstContextResult.Success; }
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstContextOperations.BeginRead(state.Catalog, handle.IsValid(state.Catalog), BurstValueKind.Read, out reader);
        public BurstContextResult TryBeginBlackboardRead<T>(BlackboardReadWriteHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstContextOperations.BeginRead(state.Catalog, handle.IsValid(state.Catalog), BurstValueKind.Read, out reader);
        public BurstContextResult TryBeginSnapshotRead<T>(SnapshotReadHandle<T> handle, out BurstValueReader reader) where T : unmanaged => BurstContextOperations.BeginRead(state.Catalog, handle.IsValid(state.Catalog), BurstValueKind.Read, out reader);
    }

    public struct BurstExecutionBatch
    {
        internal ulong Token;
        internal ulong RuntimeIdentity;
        internal ulong ExpectedCatalog;
        internal uint CaseCount;
        internal uint PreboundCase;
        internal ulong Config0;
        internal ulong Config1;
        internal ulong Config2;
        internal ulong Config3;
        internal ulong Memory0;
        internal ulong Memory1;
        internal bool PaddingIsZero;
        internal BurstNodeAbortReason AbortReason;
        internal BurstNodeExitReason ExitReason;
        internal AIBT.NodeStatus PublishedStatus;
        internal ConditionResult PublishedCondition;
        internal BurstNodeStatusMask AllowedStatuses;
        internal bool Completed;
        internal BurstCatalogHandshake Handshake;
        internal uint InstanceOrdinal;
        internal uint RuntimeNodeIndex;
        internal BurstCallbackPhase Phase;
        internal uint CallbackCount;
        internal bool HasWork;
        internal BurstExecutionResult ExecutionResult;
        internal bool RuntimeCreated;
        internal uint RemainingWork;
        internal uint InstancesVisited;
        internal ulong SegmentSteps;
        internal bool TerminalFailure;
        internal bool Scheduled;
        internal bool ScheduledOwner;
#if UNITY_6000_0_OR_NEWER
        internal NativeArray<BurstSharedRuntimeRecord> SharedRuntime;
        [ReadOnly]
        internal NativeList<int> SharedScheduleClaim;
#endif
        internal ulong RandomState;
        internal ulong RandomIncrement;
        internal bool RandomCapability;
        internal uint FrameGeneration;
    }

    public readonly struct BurstDispatchFrame
    {
        internal readonly ulong Token;
        internal readonly ulong Catalog;
        internal readonly uint CaseIndex;
        internal readonly BurstCallbackPhase Phase;
        internal readonly ulong Config0;
        internal readonly ulong Config1;
        internal readonly ulong Config2;
        internal readonly ulong Config3;
        internal readonly ulong Memory0;
        internal readonly ulong Memory1;
        internal readonly BurstNodeAbortReason AbortReason;
        internal readonly BurstNodeExitReason ExitReason;
        internal readonly ulong RandomState;
        internal readonly ulong RandomIncrement;
        internal readonly ulong ContextToken;
        internal readonly uint Generation;
        internal BurstDispatchFrame(ulong token, ulong catalog, uint caseIndex, BurstCallbackPhase phase,
            ulong config0, ulong config1, ulong config2, ulong config3, ulong memory0, ulong memory1,
            BurstNodeAbortReason abortReason, BurstNodeExitReason exitReason, ulong randomState, ulong randomIncrement, bool randomCapability, uint generation)
        { Token = token; Catalog = catalog; CaseIndex = caseIndex; Phase = phase; Config0 = config0; Config1 = config1; Config2 = config2; Config3 = config3; Memory0 = memory0; Memory1 = memory1; AbortReason = abortReason; ExitReason = exitReason; RandomState = randomState; RandomIncrement = randomIncrement; Generation = generation; ContextToken = ((catalog ^ ((ulong)generation << 32)) & 0x7fffffffffffffffUL) | (randomCapability ? 0x8000000000000000UL : 0UL); if (ContextToken == 0) ContextToken = 1; }
    }

    public readonly struct BurstConfigurationReader
    {
        internal readonly ulong Token;
        internal readonly ulong Word0;
        internal readonly ulong Word1;
        internal readonly ulong Word2;
        internal readonly ulong Word3;
        internal BurstConfigurationReader(in BurstDispatchFrame frame) { Token = frame.Token; Word0 = frame.Config0; Word1 = frame.Config1; Word2 = frame.Config2; Word3 = frame.Config3; }
    }

    public struct BurstMemoryAccessor
    {
        internal ulong Token;
        internal ulong Word0;
        internal ulong Word1;
        internal BurstMemoryAccessor(in BurstDispatchFrame frame) { Token = frame.Token; Word0 = frame.Memory0; Word1 = frame.Memory1; }
    }

    // Reserved for generated code. All entry points validate opaque tokens, bounds, phase and encoding.
    public static class BurstGeneratedRuntimeBridge
    {
        private const ulong BatchStamp = 0x4149425442415443UL;
        public static BurstContextResult TryGetCatalogHandshake(in BurstExecutionBatch batch, out BurstCatalogHandshake handshake)
        { handshake = default; if (!LiveBatch(in batch)) return BurstContextResult.InvalidHandle; handshake = batch.Handshake; return BurstContextResult.Success; }
        public static BurstContextResult TryRejectBatch(ref BurstExecutionBatch batch, in BurstCatalogValidationResult validationResult)
        {
            if (!LiveBatch(in batch)) return BurstContextResult.InvalidHandle;
            if (!validationResult.HasCanonicalCodeWord || validationResult.Success || validationResult.DiagnosticNumber != 5012) return BurstContextResult.InvalidStatus;
            batch.Completed = false; batch.HasWork = false; batch.TerminalFailure = true; batch.Token = 0;
            batch.ExecutionResult = new BurstExecutionResult(BurstExecutionCode.ValidationFailed, 5012, 0, 0);
            PublishShared(ref batch); return BurstContextResult.Success;
        }
        public static BurstContextResult TryGetExecutionRequest(in BurstExecutionBatch batch, out uint instanceOrdinal, out uint runtimeNodeIndex,
            out uint catalogCaseIndex, out BurstCallbackPhase phase, out bool hasWork)
        {
            instanceOrdinal = 0; runtimeNodeIndex = 0; catalogCaseIndex = 0; phase = default; hasWork = false;
            if (!RuntimeBatch(in batch)) return BurstContextResult.InvalidHandle;
#if !UNITY_6000_0_OR_NEWER
            if (BurstBatchLifetimeRuntime.HasRecord(batch.RuntimeIdentity))
            {
                BurstBatchLifetimeRuntime.ReadRecord(batch.RuntimeIdentity, out var alive, out var terminal, out var scheduleState,
                    out var remainingWork, out var sharedInstance, out var sharedNode, out var sharedCase, out var sharedPhase, out _, out _, out _);
                if (alive == 0 || terminal != 0) return BurstContextResult.InvalidHandle;
                if (scheduleState != 0 && (!batch.Scheduled || batch.ScheduledOwner)) return BurstContextResult.PhaseViolation;
                hasWork = remainingWork != 0;
                if (hasWork) { instanceOrdinal = sharedInstance; runtimeNodeIndex = sharedNode; catalogCaseIndex = sharedCase; phase = sharedPhase; }
                return BurstContextResult.Success;
            }
            if (batch.Scheduled && batch.ScheduledOwner) return BurstContextResult.PhaseViolation;
#endif
#if UNITY_6000_0_OR_NEWER
            if (batch.SharedScheduleClaim.IsCreated && batch.SharedScheduleClaim[0] != 0 && (!batch.Scheduled || batch.ScheduledOwner))
                return BurstContextResult.PhaseViolation;
            if (batch.SharedRuntime.IsCreated)
            {
                var shared = batch.SharedRuntime[0];
                if (shared.Alive == 0 || shared.Terminal != 0) return BurstContextResult.InvalidHandle;
                if (shared.ScheduleState != 0 && (!batch.Scheduled || batch.ScheduledOwner)) return BurstContextResult.PhaseViolation;
                hasWork = shared.RemainingWork != 0;
                if (hasWork) { instanceOrdinal = shared.InstanceOrdinal; runtimeNodeIndex = shared.RuntimeNodeIndex; catalogCaseIndex = shared.CatalogCaseIndex; phase = shared.Phase; }
                return BurstContextResult.Success;
            }
#endif
            if (batch.TerminalFailure) return BurstContextResult.PhaseViolation;
            if (batch.HasWork && !LiveBatch(in batch)) return BurstContextResult.InvalidHandle;
            hasWork = batch.HasWork;
            if (hasWork) { instanceOrdinal = batch.InstanceOrdinal; runtimeNodeIndex = batch.RuntimeNodeIndex; catalogCaseIndex = batch.PreboundCase; phase = batch.Phase; }
            return BurstContextResult.Success;
        }
        public static BurstContextResult TryGetExecutionResult(in BurstExecutionBatch batch, out BurstExecutionResult result)
        {
            result = default; if (!RuntimeBatch(in batch)) return BurstContextResult.InvalidHandle;
#if !UNITY_6000_0_OR_NEWER
            if (BurstBatchLifetimeRuntime.HasRecord(batch.RuntimeIdentity))
            {
                BurstBatchLifetimeRuntime.ReadRecord(batch.RuntimeIdentity, out var alive, out var terminal, out _, out _, out _, out _, out _, out _, out _, out _, out var sharedResult);
                if (alive == 0 || (batch.Scheduled && !batch.ScheduledOwner && terminal != 0)) return BurstContextResult.InvalidHandle;
                if (terminal == 0) return BurstContextResult.PhaseViolation;
                if (!sharedResult.HasCanonicalCodeWord) return BurstContextResult.InvalidStatus;
                result = sharedResult; return BurstContextResult.Success;
            }
            if (batch.Scheduled && !batch.ScheduledOwner) return batch.HasWork ? BurstContextResult.PhaseViolation : BurstContextResult.InvalidHandle;
#endif
#if UNITY_6000_0_OR_NEWER
            if (batch.SharedRuntime.IsCreated)
            {
                var shared = batch.SharedRuntime[0];
                if (shared.Alive == 0) return BurstContextResult.InvalidHandle;
                if (shared.Terminal == 0) return BurstContextResult.PhaseViolation;
                if (batch.Scheduled && !batch.ScheduledOwner) return BurstContextResult.InvalidHandle;
                if (!shared.Result.HasCanonicalCodeWord) return BurstContextResult.InvalidStatus;
                result = shared.Result; return BurstContextResult.Success;
            }
#endif
            if (batch.HasWork) return BurstContextResult.PhaseViolation; if (!batch.ExecutionResult.HasCanonicalCodeWord) return BurstContextResult.InvalidStatus; result = batch.ExecutionResult; return BurstContextResult.Success;
        }
        public static BurstContextResult TryPrepareSchedule(ref BurstExecutionBatch batch, out BurstExecutionBatch scheduledView)
        {
            scheduledView = default; if (!LiveBatch(in batch)) return BurstContextResult.InvalidHandle;
#if UNITY_6000_0_OR_NEWER
            if (!batch.SharedRuntime.IsCreated || !batch.SharedScheduleClaim.IsCreated) return BurstContextResult.InvalidHandle;
            if (batch.SharedScheduleClaim[0] != 0) return BurstContextResult.PhaseViolation;
            if (System.Threading.Interlocked.CompareExchange(ref batch.SharedScheduleClaim.ElementAt(0), 1, 0) != 0)
                return BurstContextResult.PhaseViolation;
            var shared = batch.SharedRuntime[0];
            if (shared.Alive == 0) { System.Threading.Interlocked.Exchange(ref batch.SharedScheduleClaim.ElementAt(0), 0); return BurstContextResult.InvalidHandle; }
            if (shared.Terminal != 0 || shared.ScheduleState != 0 || shared.RemainingWork == 0)
            { System.Threading.Interlocked.Exchange(ref batch.SharedScheduleClaim.ElementAt(0), 0); return BurstContextResult.PhaseViolation; }
            shared.ScheduleState = 1; batch.SharedRuntime[0] = shared;
#else
            if (BurstBatchLifetimeRuntime.HasRecord(batch.RuntimeIdentity))
            {
                if (!BurstBatchLifetimeRuntime.TryClaimSchedule(batch.RuntimeIdentity)) return BurstContextResult.PhaseViolation;
            }
            else if (batch.Scheduled || !batch.HasWork) return BurstContextResult.PhaseViolation;
#endif
            batch.Scheduled = true; batch.ScheduledOwner = true; scheduledView = batch; scheduledView.ScheduledOwner = false;
            return BurstContextResult.Success;
        }
        public static BurstContextResult TryAcquireDispatchFrame(ref BurstExecutionBatch batch, uint instanceOrdinal,
            uint runtimeNodeIndex, uint catalogCaseIndex, BurstCallbackPhase phase, out BurstDispatchFrame frame)
        {
            frame = default;
            if (!LiveBatch(in batch) || !batch.HasWork)
                return BurstContextResult.InvalidHandle;
#if !UNITY_6000_0_OR_NEWER
            if (BurstBatchLifetimeRuntime.HasRecord(batch.RuntimeIdentity))
            {
                BurstBatchLifetimeRuntime.ReadRecord(batch.RuntimeIdentity, out var alive, out var terminal, out var scheduleState,
                    out var remainingWork, out var sharedInstance, out var sharedNode, out var sharedCase, out var sharedPhase,
                    out var sharedVisited, out var sharedSteps, out _);
                if (alive == 0 || terminal != 0 || remainingWork == 0) return BurstContextResult.InvalidHandle;
                if (scheduleState != 0 && (!batch.Scheduled || batch.ScheduledOwner)) return BurstContextResult.PhaseViolation;
                batch.HasWork = true; batch.RemainingWork = remainingWork; batch.InstanceOrdinal = sharedInstance; batch.RuntimeNodeIndex = sharedNode;
                batch.PreboundCase = sharedCase; batch.Phase = sharedPhase; batch.InstancesVisited = sharedVisited; batch.SegmentSteps = sharedSteps;
            }
#endif
            if (batch.Scheduled && batch.ScheduledOwner) return BurstContextResult.PhaseViolation;
#if UNITY_6000_0_OR_NEWER
            if (batch.SharedScheduleClaim.IsCreated && batch.SharedScheduleClaim[0] != 0 && (!batch.Scheduled || batch.ScheduledOwner))
                return BurstContextResult.PhaseViolation;
            if (batch.Scheduled)
            {
                if (!batch.SharedRuntime.IsCreated) return BurstContextResult.InvalidHandle;
                var shared = batch.SharedRuntime[0];
                if (shared.Alive == 0 || shared.Terminal != 0 || shared.RemainingWork == 0) return BurstContextResult.InvalidHandle;
                batch.HasWork = true; batch.RemainingWork = shared.RemainingWork; batch.InstanceOrdinal = shared.InstanceOrdinal;
                batch.RuntimeNodeIndex = shared.RuntimeNodeIndex; batch.PreboundCase = shared.CatalogCaseIndex; batch.Phase = shared.Phase;
                batch.InstancesVisited = shared.InstancesVisited; batch.SegmentSteps = shared.SegmentSteps;
            }
#endif
            if (instanceOrdinal != batch.InstanceOrdinal || runtimeNodeIndex == uint.MaxValue || runtimeNodeIndex != batch.RuntimeNodeIndex
                || catalogCaseIndex >= batch.CaseCount || catalogCaseIndex != batch.PreboundCase || phase != batch.Phase)
            {
                batch.Completed = false; batch.HasWork = false; batch.TerminalFailure = true; batch.Token = 0;
                batch.ExecutionResult = new BurstExecutionResult(BurstExecutionCode.Faulted, 0, batch.InstancesVisited, batch.SegmentSteps);
                PublishShared(ref batch);
                return BurstContextResult.InvalidHandle;
            }
            batch.FrameGeneration++;
            frame = new BurstDispatchFrame(batch.Token, batch.ExpectedCatalog, catalogCaseIndex, phase,
                batch.Config0, batch.Config1, batch.Config2, batch.Config3, batch.Memory0, batch.Memory1, batch.AbortReason, batch.ExitReason,
                batch.RandomState, batch.RandomIncrement, batch.RandomCapability, batch.FrameGeneration);
            BurstContextRuntime.Activate(frame.ContextToken);
            return BurstContextResult.Success;
        }

        public static BurstContextResult TryCreateConfigurationReader(in BurstDispatchFrame frame, out BurstConfigurationReader reader)
        { reader = default; if (!Valid(frame)) return BurstContextResult.InvalidHandle; reader = new BurstConfigurationReader(in frame); return BurstContextResult.Success; }
        public static BurstContextResult TryCreateMemoryAccessor(in BurstDispatchFrame frame, out BurstMemoryAccessor accessor)
        { accessor = default; if (!Valid(frame)) return BurstContextResult.InvalidHandle; accessor = new BurstMemoryAccessor(in frame); return BurstContextResult.Success; }

        public static BurstContextResult TryReadBoolean(ref BurstConfigurationReader reader, uint fieldOrdinal, uint elementIndex, out bool value)
        { value = false; if (!Valid(reader.Token) || fieldOrdinal != 2 || elementIndex != 0 || (reader.Word2 & ~1UL) != 0) return BurstContextResult.InvalidHandle; value = reader.Word2 != 0; return BurstContextResult.Success; }
        public static BurstContextResult TryReadUInt32(ref BurstConfigurationReader reader, uint fieldOrdinal, uint elementIndex, out uint value)
        { value = 0; if (!TryConfigurationWord(ref reader, fieldOrdinal, elementIndex, out var word)) return BurstContextResult.InvalidHandle; value = unchecked((uint)word); return BurstContextResult.Success; }
        public static BurstContextResult TryReadUInt64(ref BurstConfigurationReader reader, uint fieldOrdinal, uint elementIndex, out ulong value)
        { value = 0; if (!TryConfigurationWord(ref reader, fieldOrdinal, elementIndex, out var word)) return BurstContextResult.InvalidHandle; value = word; return BurstContextResult.Success; }
        public static BurstContextResult TryReadBlackboardReadHandle<T>(ref BurstConfigurationReader reader, uint fieldOrdinal, ulong valueTypeNumericId, uint valueTypeVersion, out BlackboardReadHandle<T> value) where T : unmanaged { value = default; if (!TryHandleWords(ref reader, fieldOrdinal, valueTypeNumericId, valueTypeVersion, out var ordinal, out var token)) return BurstContextResult.TypeMismatch; value = new BlackboardReadHandle<T>(ordinal, token); return BurstContextResult.Success; }
        public static BurstContextResult TryReadBlackboardWriteHandle<T>(ref BurstConfigurationReader reader, uint fieldOrdinal, ulong valueTypeNumericId, uint valueTypeVersion, out BlackboardWriteHandle<T> value) where T : unmanaged { value = default; if (!TryHandleWords(ref reader, fieldOrdinal, valueTypeNumericId, valueTypeVersion, out var ordinal, out var token)) return BurstContextResult.TypeMismatch; value = new BlackboardWriteHandle<T>(ordinal, token); return BurstContextResult.Success; }
        public static BurstContextResult TryReadBlackboardReadWriteHandle<T>(ref BurstConfigurationReader reader, uint fieldOrdinal, ulong valueTypeNumericId, uint valueTypeVersion, out BlackboardReadWriteHandle<T> value) where T : unmanaged { value = default; if (!TryHandleWords(ref reader, fieldOrdinal, valueTypeNumericId, valueTypeVersion, out var ordinal, out var token)) return BurstContextResult.TypeMismatch; value = new BlackboardReadWriteHandle<T>(ordinal, token); return BurstContextResult.Success; }
        public static BurstContextResult TryReadSnapshotHandle<T>(ref BurstConfigurationReader reader, uint fieldOrdinal, ulong valueTypeNumericId, uint valueTypeVersion, out SnapshotReadHandle<T> value) where T : unmanaged { value = default; if (!TryHandleWords(ref reader, fieldOrdinal, valueTypeNumericId, valueTypeVersion, out var ordinal, out var token)) return BurstContextResult.TypeMismatch; value = new SnapshotReadHandle<T>(ordinal, token); return BurstContextResult.Success; }
        public static BurstContextResult TryReadCommandHandle<T>(ref BurstConfigurationReader reader, uint fieldOrdinal, ulong payloadTypeNumericId, uint payloadTypeVersion, out CommandHandle<T> value) where T : unmanaged { value = default; if (!TryHandleWords(ref reader, fieldOrdinal, payloadTypeNumericId, payloadTypeVersion, out var ordinal, out var token)) return BurstContextResult.TypeMismatch; value = new CommandHandle<T>(ordinal, token); return BurstContextResult.Success; }
        public static BurstContextResult TryReadAsyncOperationHandle<TStart, TCancel>(ref BurstConfigurationReader reader, uint fieldOrdinal, ulong startPayloadTypeNumericId, uint startPayloadTypeVersion, ulong cancelPayloadTypeNumericId, uint cancelPayloadTypeVersion, out AsyncOperationHandle<TStart, TCancel> value) where TStart : unmanaged where TCancel : unmanaged { value = default; if (cancelPayloadTypeNumericId == 0 || cancelPayloadTypeVersion == 0 || !TryHandleWords(ref reader, fieldOrdinal, startPayloadTypeNumericId, startPayloadTypeVersion, out var ordinal, out var token)) return BurstContextResult.TypeMismatch; value = new AsyncOperationHandle<TStart, TCancel>(ordinal, token); return BurstContextResult.Success; }
        public static BurstContextResult TryReadCompletionHandle<T>(ref BurstConfigurationReader reader, uint fieldOrdinal, ulong payloadTypeNumericId, uint payloadTypeVersion, out CompletionHandle<T> value) where T : unmanaged { value = default; if (!TryHandleWords(ref reader, fieldOrdinal, payloadTypeNumericId, payloadTypeVersion, out var ordinal, out var token)) return BurstContextResult.TypeMismatch; value = new CompletionHandle<T>(ordinal, token); return BurstContextResult.Success; }

        public static BurstContextResult TryReadMemoryBoolean(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out bool value) { value = false; return BurstContextResult.TypeMismatch; }
        public static BurstContextResult TryReadMemoryInt8(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out sbyte value) { var result = TryReadMemoryInt32(ref accessor, fieldOrdinal, elementIndex, out var wide); value = unchecked((sbyte)wide); return result; }
        public static BurstContextResult TryReadMemoryUInt8(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out byte value) { var result = TryReadMemoryUInt32(ref accessor, fieldOrdinal, elementIndex, out var wide); value = unchecked((byte)wide); return result; }
        public static BurstContextResult TryReadMemoryInt16(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out short value) { var result = TryReadMemoryInt32(ref accessor, fieldOrdinal, elementIndex, out var wide); value = unchecked((short)wide); return result; }
        public static BurstContextResult TryReadMemoryUInt16(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out ushort value) { var result = TryReadMemoryUInt32(ref accessor, fieldOrdinal, elementIndex, out var wide); value = unchecked((ushort)wide); return result; }
        public static BurstContextResult TryReadMemoryInt32(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out int value)
        { value = 0; if (!Valid(accessor.Token) || fieldOrdinal != 0 || elementIndex != 0) return BurstContextResult.InvalidHandle; value = unchecked((int)(uint)accessor.Word0); return BurstContextResult.Success; }
        public static BurstContextResult TryReadMemoryUInt32(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out uint value)
        { value = 0; if (!Valid(accessor.Token) || fieldOrdinal != 1 || elementIndex != 0) return BurstContextResult.InvalidHandle; value = unchecked((uint)(accessor.Word0 >> 32)); return BurstContextResult.Success; }
        public static BurstContextResult TryReadMemoryInt64(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out long value)
        { value = 0; if (!Valid(accessor.Token) || fieldOrdinal != 2 || elementIndex != 0) return BurstContextResult.InvalidHandle; value = unchecked((long)accessor.Word1); return BurstContextResult.Success; }
        public static BurstContextResult TryReadMemoryUInt64(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out ulong value)
        { var result = TryReadMemoryInt64(ref accessor, fieldOrdinal, elementIndex, out var signed); value = unchecked((ulong)signed); return result; }
        public static BurstContextResult TryReadMemoryFloat32(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out float value) { value = 0; return BurstContextResult.TypeMismatch; }
        public static BurstContextResult TryReadMemoryFloat64(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, out double value) { value = 0; return BurstContextResult.TypeMismatch; }
        public static BurstContextResult TryWriteMemoryBoolean(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, bool value) => BurstContextResult.TypeMismatch;
        public static BurstContextResult TryWriteMemoryInt8(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, sbyte value) => TryWriteMemoryInt32(ref accessor, fieldOrdinal, elementIndex, value);
        public static BurstContextResult TryWriteMemoryUInt8(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, byte value) => TryWriteMemoryUInt32(ref accessor, fieldOrdinal, elementIndex, value);
        public static BurstContextResult TryWriteMemoryInt16(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, short value) => TryWriteMemoryInt32(ref accessor, fieldOrdinal, elementIndex, value);
        public static BurstContextResult TryWriteMemoryUInt16(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, ushort value) => TryWriteMemoryUInt32(ref accessor, fieldOrdinal, elementIndex, value);
        public static BurstContextResult TryWriteMemoryInt32(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, int value)
        { if (!Valid(accessor.Token) || fieldOrdinal != 0 || elementIndex != 0) { accessor.Token = 0; return BurstContextResult.InvalidHandle; } accessor.Word0 = (accessor.Word0 & 0xffffffff00000000UL) | (uint)value; return BurstContextResult.Success; }
        public static BurstContextResult TryWriteMemoryUInt32(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, uint value)
        { if (!Valid(accessor.Token) || fieldOrdinal != 1 || elementIndex != 0) { accessor.Token = 0; return BurstContextResult.InvalidHandle; } accessor.Word0 = (accessor.Word0 & 0x00000000ffffffffUL) | ((ulong)value << 32); return BurstContextResult.Success; }
        public static BurstContextResult TryWriteMemoryInt64(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, long value)
        { if (!Valid(accessor.Token) || fieldOrdinal != 2 || elementIndex != 0) { accessor.Token = 0; return BurstContextResult.InvalidHandle; } accessor.Word1 = unchecked((ulong)value); return BurstContextResult.Success; }
        public static BurstContextResult TryWriteMemoryUInt64(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, ulong value) => TryWriteMemoryInt64(ref accessor, fieldOrdinal, elementIndex, unchecked((long)value));
        public static BurstContextResult TryWriteMemoryFloat32(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, float value) => BurstContextResult.TypeMismatch;
        public static BurstContextResult TryWriteMemoryFloat64(ref BurstMemoryAccessor accessor, uint fieldOrdinal, uint elementIndex, double value) => BurstContextResult.TypeMismatch;
        public static BurstContextResult TryCommitMemory(ref BurstMemoryAccessor accessor) { if (!Valid(accessor.Token)) return BurstContextResult.InvalidHandle; accessor.Token = 0; return BurstContextResult.Success; }

        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out bool value) { value = false; if (!ValueRead(ref reader, fieldOrdinal, elementIndex, out var word)) return BurstContextResult.InvalidHandle; if ((word & ~1UL) != 0) { reader.Token = 0; return BurstContextResult.TypeMismatch; } value = word != 0; return BurstContextResult.Success; }
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out sbyte value) { var result = TryReadValue(ref reader, fieldOrdinal, elementIndex, out int wide); value = unchecked((sbyte)wide); return result; }
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out byte value) { var result = TryReadValue(ref reader, fieldOrdinal, elementIndex, out uint wide); value = unchecked((byte)wide); return result; }
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out short value) { var result = TryReadValue(ref reader, fieldOrdinal, elementIndex, out int wide); value = unchecked((short)wide); return result; }
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out ushort value) { var result = TryReadValue(ref reader, fieldOrdinal, elementIndex, out uint wide); value = unchecked((ushort)wide); return result; }
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out int value) { value = 0; if (!ValueRead(ref reader, fieldOrdinal, elementIndex, out var word)) return BurstContextResult.InvalidHandle; value = unchecked((int)(uint)word); return BurstContextResult.Success; }
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out uint value) { var result = TryReadValue(ref reader, fieldOrdinal, elementIndex, out int signed); value = unchecked((uint)signed); return result; }
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out long value) { value = 0; if (!ValueRead(ref reader, fieldOrdinal, elementIndex, out var word)) return BurstContextResult.InvalidHandle; value = unchecked((long)word); return BurstContextResult.Success; }
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out ulong value) { var result = TryReadValue(ref reader, fieldOrdinal, elementIndex, out long signed); value = unchecked((ulong)signed); return result; }
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out float value) { value = 0; reader.Token = 0; return BurstContextResult.TypeMismatch; }
        public static BurstContextResult TryReadValue(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out double value) { value = 0; reader.Token = 0; return BurstContextResult.TypeMismatch; }
        public static BurstContextResult TryCompleteValueRead(ref BurstValueReader reader) { if (!Valid(reader.Token) || reader.ReadMask == 0) return BurstContextResult.InvalidHandle; reader.Token = 0; return BurstContextResult.Success; }

        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, bool value) => ValueWrite(ref writer, fieldOrdinal, elementIndex, value ? 1UL : 0UL);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, sbyte value) => ValueWrite(ref writer, fieldOrdinal, elementIndex, unchecked((ulong)value));
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, byte value) => ValueWrite(ref writer, fieldOrdinal, elementIndex, value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, short value) => ValueWrite(ref writer, fieldOrdinal, elementIndex, unchecked((ulong)value));
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, ushort value) => ValueWrite(ref writer, fieldOrdinal, elementIndex, value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, int value) => ValueWrite(ref writer, fieldOrdinal, elementIndex, unchecked((ulong)value));
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, uint value) => ValueWrite(ref writer, fieldOrdinal, elementIndex, value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, long value) => ValueWrite(ref writer, fieldOrdinal, elementIndex, unchecked((ulong)value));
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, ulong value) => ValueWrite(ref writer, fieldOrdinal, elementIndex, value);
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, float value) { writer.Token = 0; return BurstContextResult.TypeMismatch; }
        public static BurstContextResult TryWriteValue(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, double value) { writer.Token = 0; return BurstContextResult.TypeMismatch; }
        public static BurstContextResult TryCommitBlackboardWrite(ref BurstValueWriter writer) => Commit(ref writer, BurstValueKind.BlackboardWrite);
        public static BurstContextResult TryCommitEffect(ref BurstValueWriter writer) => Commit(ref writer, BurstValueKind.Effect);
        public static BurstContextResult TryCommitStart(ref BurstValueWriter startWriter, ref BurstValueWriter faultCancelWriter, out AIBT.OperationId operationId)
        { operationId = default; if (!Valid(startWriter.Token) || startWriter.Token != faultCancelWriter.Token || startWriter.Kind != BurstValueKind.Start || faultCancelWriter.Kind != BurstValueKind.FaultCancel || startWriter.WriteMask == 0 || faultCancelWriter.WriteMask == 0) { startWriter.Token = 0; faultCancelWriter.Token = 0; return BurstContextResult.InvalidHandle; } operationId = new AIBT.OperationId(new AIBT.TreeInstanceId(startWriter.Tree), new AIBT.RuntimeNodeIndex(startWriter.Node), startWriter.Generation, 1); startWriter.Token = 0; faultCancelWriter.Token = 0; return BurstContextResult.Success; }
        public static BurstContextResult TryCommitCancel(ref BurstValueWriter writer) => Commit(ref writer, BurstValueKind.Cancel);
        public static BurstContextResult TryCommitConsume(ref BurstValueReader reader) { if (reader.Kind != BurstValueKind.Consume || !reader.Operation.IsValid) { reader.Token = 0; return BurstContextResult.StaleCompletion; } return TryCompleteValueRead(ref reader); }
        public static BurstContextResult TryGetAbortReason(in BurstDispatchFrame frame, out BurstNodeAbortReason reason) { reason = frame.AbortReason; return Valid(frame) && frame.Phase == BurstCallbackPhase.Abort ? BurstContextResult.Success : BurstContextResult.PhaseViolation; }
        public static BurstContextResult TryGetExitReason(in BurstDispatchFrame frame, out BurstNodeExitReason reason) { reason = frame.ExitReason; return Valid(frame) && frame.Phase == BurstCallbackPhase.Exit ? BurstContextResult.Success : BurstContextResult.PhaseViolation; }
        public static BurstContextResult TryCompleteEnter(ref BurstExecutionBatch batch, in BurstDispatchFrame frame, ref BurstEnterContext context) { var validation = ValidContext(ref batch, in frame, context.ValidationToken, context.RandomState, context.RandomIncrement, BurstCallbackPhase.Enter); if (validation != BurstContextResult.Success) return validation; if (batch.RandomCapability) batch.RandomState = context.RandomState; batch.CallbackCount++; batch.FrameGeneration++; BurstContextRuntime.Expire(context.ValidationToken); Finish(ref batch, BurstExecutionCode.Success, 0); return BurstContextResult.Success; }
        public static BurstContextResult TryCompleteTick(ref BurstExecutionBatch batch, in BurstDispatchFrame frame, ref BurstTickContext context, AIBT.NodeStatus status) { var validation = ValidContext(ref batch, in frame, context.ValidationToken, context.RandomState, context.RandomIncrement, BurstCallbackPhase.Tick); if (validation != BurstContextResult.Success) return validation; if ((byte)status > 2 || (batch.AllowedStatuses & (BurstNodeStatusMask)(1 << (int)status)) == 0) return BurstContextRuntime.Latch(context.ValidationToken, BurstContextResult.PhaseViolation); if (batch.RandomCapability) batch.RandomState = context.RandomState; batch.PublishedStatus = status; batch.CallbackCount++; batch.FrameGeneration++; BurstContextRuntime.Expire(context.ValidationToken); Finish(ref batch, BurstExecutionCode.Success, 0); return BurstContextResult.Success; }
        public static BurstContextResult TryCompleteAbort(ref BurstExecutionBatch batch, in BurstDispatchFrame frame) => Complete(ref batch, in frame, BurstCallbackPhase.Abort);
        public static BurstContextResult TryCompleteExit(ref BurstExecutionBatch batch, in BurstDispatchFrame frame) => Complete(ref batch, in frame, BurstCallbackPhase.Exit);
        public static BurstContextResult TryCompleteObserver(ref BurstExecutionBatch batch, in BurstDispatchFrame frame, ConditionResult result) { if (!Valid(frame) || batch.Token != frame.Token || !batch.HasWork || frame.Phase != BurstCallbackPhase.Observer || (byte)result > 1) return BurstContextResult.PhaseViolation; batch.PublishedCondition = result; batch.CallbackCount++; Finish(ref batch, BurstExecutionCode.Success, 0); return BurstContextResult.Success; }
        public static BurstContextResult TryFailDispatch(ref BurstExecutionBatch batch, in BurstDispatchFrame frame, BurstContextResult failure) { if (!Valid(frame) || batch.Token != frame.Token || !batch.HasWork || batch.FrameGeneration != frame.Generation || failure == BurstContextResult.Success) return BurstContextResult.InvalidHandle; batch.Completed = false; batch.HasWork = false; batch.TerminalFailure = true; batch.FrameGeneration++; batch.Token = 0; batch.ExecutionResult = new BurstExecutionResult(BurstExecutionCode.Faulted, 0, batch.InstancesVisited, batch.SegmentSteps); PublishShared(ref batch); return failure; }

        private static bool ValueRead(ref BurstValueReader reader, uint fieldOrdinal, uint elementIndex, out ulong word) { word = 0; if (!Valid(reader.Token) || fieldOrdinal > 1 || elementIndex != 0 || (reader.ReadMask & (1u << (int)fieldOrdinal)) != 0) { reader.Token = 0; return false; } reader.ReadMask |= 1u << (int)fieldOrdinal; word = fieldOrdinal == 0 ? reader.Word0 : reader.Word1; return true; }
        private static bool TryConfigurationWord(ref BurstConfigurationReader reader, uint fieldOrdinal, uint elementIndex, out ulong word) { word = 0; if (!Valid(reader.Token) || fieldOrdinal > 3 || elementIndex != 0) return false; word = fieldOrdinal == 0 ? reader.Word0 : fieldOrdinal == 1 ? reader.Word1 : fieldOrdinal == 2 ? reader.Word2 : reader.Word3; return true; }
        private static bool TryHandleWords(ref BurstConfigurationReader reader, uint fieldOrdinal, ulong typeNumericId, uint typeVersion, out uint ordinal, out uint token) { ordinal = 0; token = 0; if (typeNumericId == 0 || typeVersion == 0 || !TryConfigurationWord(ref reader, fieldOrdinal, 0, out var word)) return false; ordinal = unchecked((uint)word); token = unchecked((uint)reader.Token); return token != 0; }
        private static BurstContextResult ValueWrite(ref BurstValueWriter writer, uint fieldOrdinal, uint elementIndex, ulong word) { if (!Valid(writer.Token) || fieldOrdinal > 1 || elementIndex != 0 || (writer.WriteMask & (1u << (int)fieldOrdinal)) != 0) { writer.Token = 0; return BurstContextResult.InvalidHandle; } writer.WriteMask |= 1u << (int)fieldOrdinal; if (fieldOrdinal == 0) writer.Word0 = word; else writer.Word1 = word; return BurstContextResult.Success; }
        private static BurstContextResult Commit(ref BurstValueWriter writer, BurstValueKind expected) { if (!Valid(writer.Token) || writer.Kind != expected || writer.WriteMask == 0) { writer.Token = 0; return BurstContextResult.InvalidHandle; } writer.Token = 0; return BurstContextResult.Success; }
        private static BurstContextResult Complete(ref BurstExecutionBatch batch, in BurstDispatchFrame frame, BurstCallbackPhase expected) { if (!Valid(frame) || batch.Token != frame.Token || !batch.HasWork || batch.FrameGeneration != frame.Generation || frame.Phase != expected) return BurstContextResult.PhaseViolation; batch.CallbackCount++; batch.FrameGeneration++; Finish(ref batch, BurstExecutionCode.Success, 0); return BurstContextResult.Success; }
        private static BurstContextResult ValidContext(ref BurstExecutionBatch batch, in BurstDispatchFrame frame, ulong token, ulong state, ulong increment, BurstCallbackPhase expected)
        { if (!Valid(frame) || batch.Token != frame.Token || !batch.HasWork || batch.FrameGeneration != frame.Generation || frame.Phase != expected || token != frame.ContextToken) return BurstContextResult.InvalidHandle; var runtime = BurstContextRuntime.Completion(token); if (runtime != BurstContextResult.Success) return runtime; return increment == frame.RandomIncrement && (increment & 1UL) == 1 && (batch.RandomCapability || state == 0) ? BurstContextResult.Success : BurstContextResult.PhaseViolation; }
        private static void Finish(ref BurstExecutionBatch batch, BurstExecutionCode code, ushort diagnosticNumber)
        {
            batch.Completed = code == BurstExecutionCode.Success; batch.InstancesVisited++; batch.SegmentSteps++;
            if (batch.RemainingWork > 0) batch.RemainingWork--;
            batch.HasWork = batch.RemainingWork != 0;
            if (batch.HasWork) batch.InstanceOrdinal++;
            if (!batch.HasWork) { batch.ExecutionResult = new BurstExecutionResult(code, diagnosticNumber, batch.InstancesVisited, batch.SegmentSteps); PublishShared(ref batch); }
#if UNITY_6000_0_OR_NEWER
            else if (batch.SharedRuntime.IsCreated)
            {
                var shared = batch.SharedRuntime[0]; shared.RemainingWork = batch.RemainingWork; shared.InstanceOrdinal = batch.InstanceOrdinal;
                shared.RuntimeNodeIndex = batch.RuntimeNodeIndex; shared.CatalogCaseIndex = batch.PreboundCase; shared.Phase = batch.Phase;
                shared.InstancesVisited = batch.InstancesVisited; shared.SegmentSteps = batch.SegmentSteps; batch.SharedRuntime[0] = shared;
            }
#else
            else if (BurstBatchLifetimeRuntime.HasRecord(batch.RuntimeIdentity))
                BurstBatchLifetimeRuntime.UpdateRecord(batch.RuntimeIdentity, batch.RemainingWork, batch.InstanceOrdinal,
                    batch.RuntimeNodeIndex, batch.PreboundCase, batch.Phase, batch.InstancesVisited, batch.SegmentSteps);
#endif
        }
        private static void PublishShared(ref BurstExecutionBatch batch)
        {
#if UNITY_6000_0_OR_NEWER
            if (batch.SharedRuntime.IsCreated) { var shared = batch.SharedRuntime[0]; shared.Terminal = 1; shared.TerminalFailure = (byte)(batch.TerminalFailure ? 1 : 0);
                shared.RemainingWork = 0; shared.InstancesVisited = batch.InstancesVisited; shared.SegmentSteps = batch.SegmentSteps;
                shared.Result = batch.ExecutionResult; batch.SharedRuntime[0] = shared; }
#else
            if (BurstBatchLifetimeRuntime.HasRecord(batch.RuntimeIdentity))
                BurstBatchLifetimeRuntime.CompleteRecord(batch.RuntimeIdentity, batch.TerminalFailure, batch.InstancesVisited,
                    batch.SegmentSteps, in batch.ExecutionResult);
#endif
        }

        public static BurstContextResult TryCreateEnterContext(in BurstDispatchFrame frame, out BurstEnterContext context) { context = default; if (!Valid(frame) || frame.Phase != BurstCallbackPhase.Enter) return BurstContextResult.PhaseViolation; context = new BurstEnterContext(frame.ContextToken, frame.RandomState, frame.RandomIncrement); return BurstContextResult.Success; }
        public static BurstContextResult TryCreateTickContext(in BurstDispatchFrame frame, out BurstTickContext context) { context = default; if (!Valid(frame) || frame.Phase != BurstCallbackPhase.Tick) return BurstContextResult.PhaseViolation; context = new BurstTickContext(frame.ContextToken, frame.RandomState, frame.RandomIncrement); return BurstContextResult.Success; }
        public static BurstContextResult TryCreateAbortContext(in BurstDispatchFrame frame, out BurstAbortContext context) { context = default; if (!Valid(frame) || frame.Phase != BurstCallbackPhase.Abort) return BurstContextResult.PhaseViolation; context = new BurstAbortContext(State(frame)); return BurstContextResult.Success; }
        public static BurstContextResult TryCreateExitContext(in BurstDispatchFrame frame, out BurstExitContext context) { context = default; return Valid(frame) && frame.Phase == BurstCallbackPhase.Exit ? BurstContextResult.Success : BurstContextResult.PhaseViolation; }
        public static BurstContextResult TryCreateObserverContext(in BurstDispatchFrame frame, out BurstObserverContext context) { context = default; if (!Valid(frame) || frame.Phase != BurstCallbackPhase.Observer) return BurstContextResult.PhaseViolation; context = new BurstObserverContext(State(frame)); return BurstContextResult.Success; }
        private static BurstContextState State(in BurstDispatchFrame frame) => new BurstContextState { Catalog = frame.Catalog, Tree = 1, Node = frame.CaseIndex, Generation = 1 };
        private static bool RuntimeBatch(in BurstExecutionBatch batch) => batch.RuntimeCreated && batch.ExpectedCatalog != 0 && batch.PaddingIsZero && BurstBatchLifetimeRuntime.IsLive(batch.RuntimeIdentity);
        private static bool LiveBatch(in BurstExecutionBatch batch) => RuntimeBatch(in batch) && batch.Token == (batch.ExpectedCatalog ^ BatchStamp);
        private static bool Valid(in BurstDispatchFrame frame) => frame.Catalog != 0 && frame.Token == (frame.Catalog ^ BatchStamp);
        private static bool Valid(ulong token) => token != 0;
    }

}

namespace AIBT.BurstAbi.Feasibility
{
    using AIBT.Burst;

    // Explicit test seam for handshake, ownership, padding and forged/default checks; not a production ABI surface.
    public static class BurstContractTestSeam
    {
        public static BurstExecutionBatch Batch(ulong catalog, uint caseCount, uint preboundCase, ulong config0,
            ulong config1, ulong config2, ulong config3, ulong memory0, ulong memory1, bool paddingIsZero)
            => CreateBatch(catalog, caseCount, preboundCase, config0, config1, config2, config3, memory0, memory1, paddingIsZero, false);
        public static BurstExecutionBatch RuntimeBatch(ulong catalog, uint caseCount, uint preboundCase, ulong config0,
            ulong config1, ulong config2, ulong config3, ulong memory0, ulong memory1, bool paddingIsZero)
            => CreateBatch(catalog, caseCount, preboundCase, config0, config1, config2, config3, memory0, memory1, paddingIsZero, true);
        private static BurstExecutionBatch CreateBatch(ulong catalog, uint caseCount, uint preboundCase, ulong config0,
            ulong config1, ulong config2, ulong config3, ulong memory0, ulong memory1, bool paddingIsZero, bool allocateRuntimeRecords)
        {
            var identity = catalog ^ 0x4149425442415443UL;
            var batch = new BurstExecutionBatch { Token = identity, RuntimeIdentity = identity, ExpectedCatalog = catalog,
                CaseCount = caseCount, PreboundCase = preboundCase, Config0 = config0, Config1 = config1,
                Config2 = config2, Config3 = config3, Memory0 = memory0, Memory1 = memory1, PaddingIsZero = paddingIsZero,
                AllowedStatuses = BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running, HasWork = true, RuntimeCreated = true, RemainingWork = 1, Phase = BurstCallbackPhase.Tick, RandomIncrement = 1 };
#if UNITY_6000_0_OR_NEWER
            if (allocateRuntimeRecords)
            {
                batch.SharedRuntime = new NativeArray<BurstSharedRuntimeRecord>(1, Allocator.TempJob);
                batch.SharedRuntime[0] = new BurstSharedRuntimeRecord { Alive = 1, RemainingWork = 1, CatalogCaseIndex = preboundCase, Phase = BurstCallbackPhase.Tick };
                batch.SharedScheduleClaim = new NativeList<int>(1, Allocator.TempJob); batch.SharedScheduleClaim.Add(0);
            }
#else
            if (allocateRuntimeRecords) BurstBatchLifetimeRuntime.CreateRecord(identity, preboundCase, BurstCallbackPhase.Tick);
#endif
            BurstBatchLifetimeRuntime.Activate(identity); return batch;
        }
        public static void SetHandshake(ref BurstExecutionBatch batch, in BurstCatalogHandshake handshake) { batch.Handshake = handshake; }
        public static void SetExecutionRequest(ref BurstExecutionBatch batch, uint instanceOrdinal, uint runtimeNodeIndex, BurstCallbackPhase phase)
        { batch.InstanceOrdinal = instanceOrdinal; batch.RuntimeNodeIndex = runtimeNodeIndex; batch.Phase = phase;
#if UNITY_6000_0_OR_NEWER
            if (batch.SharedRuntime.IsCreated) { var record = batch.SharedRuntime[0]; record.InstanceOrdinal = instanceOrdinal; record.RuntimeNodeIndex = runtimeNodeIndex; record.Phase = phase; batch.SharedRuntime[0] = record; }
#else
            if (BurstBatchLifetimeRuntime.HasRecord(batch.RuntimeIdentity))
                BurstBatchLifetimeRuntime.UpdateRecord(batch.RuntimeIdentity, batch.RemainingWork, instanceOrdinal, runtimeNodeIndex,
                    batch.PreboundCase, phase, batch.InstancesVisited, batch.SegmentSteps);
#endif
        }
        public static void SetWorkCount(ref BurstExecutionBatch batch, uint count) { batch.RemainingWork = count; batch.HasWork = count != 0;
#if UNITY_6000_0_OR_NEWER
            if (batch.SharedRuntime.IsCreated) { var record = batch.SharedRuntime[0]; record.RemainingWork = count; batch.SharedRuntime[0] = record; }
#else
            if (BurstBatchLifetimeRuntime.HasRecord(batch.RuntimeIdentity))
                BurstBatchLifetimeRuntime.UpdateRecord(batch.RuntimeIdentity, count, batch.InstanceOrdinal, batch.RuntimeNodeIndex,
                    batch.PreboundCase, batch.Phase, batch.InstancesVisited, batch.SegmentSteps);
#endif
        }
        public static uint CallbackCount(in BurstExecutionBatch batch) => batch.CallbackCount;
        public static ulong RandomState(in BurstExecutionBatch batch) => batch.RandomState;
        public static ulong RandomIncrement(in BurstExecutionBatch batch) => batch.RandomIncrement;
        public static void InvalidateToken(ref BurstExecutionBatch batch) { batch.Token ^= 1UL; }
        public static bool SetRandom(ref BurstExecutionBatch batch, ulong rootSeed, in DeterministicSemanticHashCanary semanticHash,
            ulong treeInstanceId, uint runtimeNodeIndex, bool capability)
        {
            if (!DeterministicRandomCanary.TryCreate(rootSeed, in semanticHash, treeInstanceId, runtimeNodeIndex, out var random)) return false;
            batch.RandomState = capability ? random.State : 0; batch.RandomIncrement = capability ? random.Increment : 1; batch.RandomCapability = capability; return true;
        }
        public static void SetReasons(ref BurstExecutionBatch batch, BurstNodeAbortReason abortReason, BurstNodeExitReason exitReason) { batch.AbortReason = abortReason; batch.ExitReason = exitReason; }
        public static AIBT.NodeStatus PublishedStatus(in BurstExecutionBatch batch) => batch.PublishedStatus;
        public static ConditionResult PublishedCondition(in BurstExecutionBatch batch) => batch.PublishedCondition;
        public static BurstCatalogValidationResult ForgeValidationResult(ushort codeWord, ushort diagnosticNumber) => new BurstCatalogValidationResult(codeWord, diagnosticNumber);
        public static void SetTerminalExecutionResult(ref BurstExecutionBatch batch, ushort codeWord, ushort diagnosticNumber, uint instancesVisited, ulong segmentSteps)
        { batch.HasWork = false; batch.ExecutionResult = new BurstExecutionResult(codeWord, diagnosticNumber, instancesVisited, segmentSteps);
#if UNITY_6000_0_OR_NEWER
            if (batch.SharedRuntime.IsCreated) { var record = batch.SharedRuntime[0]; record.Terminal = 1; record.RemainingWork = 0; record.InstancesVisited = instancesVisited; record.SegmentSteps = segmentSteps; record.Result = batch.ExecutionResult; batch.SharedRuntime[0] = record; }
#else
            if (BurstBatchLifetimeRuntime.HasRecord(batch.RuntimeIdentity))
                BurstBatchLifetimeRuntime.CompleteRecord(batch.RuntimeIdentity, true, instancesVisited, segmentSteps, in batch.ExecutionResult);
#endif
        }
        public static ulong MemoryWord0(in BurstMemoryAccessor accessor) => accessor.Word0;
        public static ulong MemoryWord1(in BurstMemoryAccessor accessor) => accessor.Word1;
        public static AsyncOperationHandle<TStart, TCancel> AsyncOperation<TStart, TCancel>(uint ordinal, ulong catalog) where TStart : unmanaged where TCancel : unmanaged => new AsyncOperationHandle<TStart, TCancel>(ordinal, unchecked((uint)catalog));
        public static CompletionHandle<T> Completion<T>(uint ordinal, ulong catalog) where T : unmanaged => new CompletionHandle<T>(ordinal, unchecked((uint)catalog));
        public static BurstTickContext Tick(ulong catalog, ulong tree, uint node, uint generation) => new BurstTickContext(catalog, 0, 1);
        public static BurstTickContext TickWithCompletion(ulong catalog, AIBT.OperationId operationId) => new BurstTickContext(BurstContextOperations.CompletionToken(catalog, operationId), 0, 1);
        public static BurstTickContext ForgeTick(in BurstDispatchFrame frame, ulong state, ulong increment) => new BurstTickContext(frame.ContextToken, state, increment);
        public static BurstEnterContext ForgeEnter(in BurstDispatchFrame frame, ulong state, ulong increment) => new BurstEnterContext(frame.ContextToken, state, increment);
        public static BurstAbortContext Abort(ulong catalog, AIBT.OperationId operationId) => new BurstAbortContext(new BurstContextState { Catalog = catalog, ActiveOperation = operationId });
        public static void Release(ref BurstExecutionBatch batch)
        {
#if UNITY_6000_0_OR_NEWER
            if (batch.SharedRuntime.IsCreated) { var record = batch.SharedRuntime[0]; record.Alive = 0; batch.SharedRuntime[0] = record; }
#else
            BurstBatchLifetimeRuntime.ReleaseRecord(batch.RuntimeIdentity);
#endif
            BurstBatchLifetimeRuntime.Expire(batch.RuntimeIdentity);
#if UNITY_6000_0_OR_NEWER
            if (batch.SharedRuntime.IsCreated) batch.SharedRuntime.Dispose();
            if (batch.SharedScheduleClaim.IsCreated) batch.SharedScheduleClaim.Dispose();
#endif
            batch.RuntimeCreated = false; batch.RuntimeIdentity = 0; batch.Token = 0; batch.HasWork = false;
        }
    }
#pragma warning restore CS0649
}
