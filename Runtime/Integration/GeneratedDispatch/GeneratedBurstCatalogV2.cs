using System;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;

namespace AIBT
{
    /// <summary>
    /// Explicit owner for one source-generated Burst catalog's immutable dispatch metadata.
    /// The generated layout blob is validated before the owner becomes usable.
    /// </summary>
    public sealed class GeneratedBurstCatalogV2 : IDisposable
    {
        private static readonly byte[] LayoutMagic =
        {
            0x41, 0x49, 0x42, 0x54, 0x2d, 0x47, 0x45, 0x4e,
            0x45, 0x52, 0x41, 0x54, 0x45, 0x44, 0x2d, 0x43,
            0x41, 0x54, 0x41, 0x4c, 0x4f, 0x47, 0x2d, 0x4c,
            0x41, 0x59, 0x4f, 0x55, 0x54, 0x2d, 0x56, 0x31,
            0x00,
        };

        private NativeArray<NativeBurstDispatchCaseV2> _cases;
        private NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
        private NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
        private NativeArray<NativeBurstDispatchBindingV2> _bindings;
        private NativeArray<NativeBurstDispatchFieldV2> _valueFields;
        private NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseRanges;
        private NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingRanges;
        private NativeArray<NativeBurstDispatchCanonicalRuleV2> _canonicalRules;
        private int _retainedUsers;
        private bool _disposed;

        private GeneratedBurstCatalogV2() { }

        public BurstCatalogFingerprint Fingerprint => Handshake.Catalog;
        public BurstHash256 NodeRegistryFingerprint => Handshake.NodeRegistry;
        public ulong OwnerId { get; private set; }
        public uint Generation { get; private set; }
        public bool IsCreated => !_disposed && OwnerId != 0 && Generation != 0;

        internal BurstCatalogHandshake Handshake { get; private set; }
        internal IGeneratedBurstCatalogExecutorV2 Executor { get; private set; }
        internal NativeArray<NativeBurstDispatchCaseV2>.ReadOnly Cases => _cases.AsReadOnly();
        internal NativeArray<NativeBurstDispatchFieldV2>.ReadOnly ConfigurationFields => _configurationFields.AsReadOnly();
        internal NativeArray<NativeBurstDispatchFieldV2>.ReadOnly MemoryFields => _memoryFields.AsReadOnly();
        internal NativeArray<NativeBurstDispatchBindingV2>.ReadOnly Bindings => _bindings.AsReadOnly();
        internal NativeArray<NativeBurstDispatchFieldV2>.ReadOnly ValueFields => _valueFields.AsReadOnly();
        internal NativeBurstDispatchCanonicalInputV2 CanonicalInput => new NativeBurstDispatchCanonicalInputV2(
            _caseRanges.AsReadOnly(), _bindingRanges.AsReadOnly(), _canonicalRules.AsReadOnly());

        public static bool TryCreate(
            IGeneratedBurstCatalogExecutorV2 executor,
            in BurstCatalogHandshake handshake,
            byte[] canonicalLayoutBlob,
            Allocator allocator,
            out GeneratedBurstCatalogV2 catalog,
            out BurstContextResult failure)
        {
            catalog = null;
            if (executor == null || canonicalLayoutBlob == null || allocator != Allocator.Persistent)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

            if (!TryDecode(canonicalLayoutBlob, out var decoded))
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }

            var created = new GeneratedBurstCatalogV2();
            try
            {
                created._cases = new NativeArray<NativeBurstDispatchCaseV2>(decoded.Cases, allocator);
                created._configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(decoded.ConfigurationFields, allocator);
                created._memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(decoded.MemoryFields, allocator);
                created._bindings = new NativeArray<NativeBurstDispatchBindingV2>(decoded.Bindings, allocator);
                created._valueFields = new NativeArray<NativeBurstDispatchFieldV2>(decoded.ValueFields, allocator);
                created._caseRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(decoded.CaseRanges, allocator);
                created._bindingRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(decoded.BindingRanges, allocator);
                created._canonicalRules = new NativeArray<NativeBurstDispatchCanonicalRuleV2>(decoded.CanonicalRules, allocator);
                created.Handshake = handshake;
                created.Executor = executor;

                if (!created.ValidateShape(out failure) || !NativeOwnerIdentityV1.TryNext(out var ownerId))
                {
                    created.DisposeArrays();
                    if (failure == BurstContextResult.Success) failure = BurstContextResult.Overflow;
                    return false;
                }

                created.OwnerId = ownerId;
                created.Generation = 1u;
                catalog = created;
                failure = BurstContextResult.Success;
                return true;
            }
            catch (Exception)
            {
                created.DisposeArrays();
                failure = BurstContextResult.CapacityExceeded;
                return false;
            }
        }

        public bool TryDispose(out BurstContextResult failure)
        {
            if (_disposed || _retainedUsers != 0)
            {
                failure = _disposed ? BurstContextResult.InvalidHandle : BurstContextResult.PhaseViolation;
                return false;
            }

            DisposeArrays();
            _disposed = true;
            if (Generation != uint.MaxValue) Generation++;
            failure = BurstContextResult.Success;
            return true;
        }

        public void Dispose() => TryDispose(out _);

        internal bool TryRetain()
        {
            if (!IsCreated || _retainedUsers == int.MaxValue) return false;
            _retainedUsers++;
            return true;
        }

        internal void Release()
        {
            if (_retainedUsers <= 0) throw new InvalidOperationException("Generated catalog retain count is invalid.");
            _retainedUsers--;
        }

        internal bool TryFindCase(ulong typeId, uint version, out uint caseIndex)
        {
            caseIndex = 0;
            if (!IsCreated) return false;
            for (var index = 0; index < _cases.Length; index++)
            {
                var candidate = _cases[index];
                if (candidate.TypeNumericId != typeId || candidate.TypeVersion != version) continue;
                caseIndex = (uint)index;
                return true;
            }
            return false;
        }

        internal NativeBurstDispatchWorkspaceShapeV2 CreateWorkspaceShape()
            => new NativeBurstDispatchWorkspaceShapeV2(
                Handshake,
                _cases.AsReadOnly(),
                _configurationFields.AsReadOnly(),
                _memoryFields.AsReadOnly(),
                _bindings.Length == 0 ? default : _bindings.AsReadOnly(),
                _bindings.Length == 0 ? default : _valueFields.AsReadOnly(),
                CanonicalInput);

        internal NativeBurstDispatchWorkspaceCapacityV2 CreateWorkspaceCapacity()
        {
            var maxMemory = 0u;
            var maxSessions = 0u;
            var maxStaging = 0u;
            var maxCommands = 0u;
            var maxCommandBytes = 0u;
            var maxOperations = 0u;
            for (var caseIndex = 0; caseIndex < _cases.Length; caseIndex++)
            {
                var dispatchCase = _cases[caseIndex];
                if (dispatchCase.MemorySize > maxMemory) maxMemory = dispatchCase.MemorySize;
                var sessions = 0u;
                var staging = 0u;
                var commands = 0u;
                var commandBytes = 0u;
                var operations = 0u;
                for (uint local = 0; local < dispatchCase.BindingCount; local++)
                {
                    var binding = _bindings[(int)(dispatchCase.FirstBinding + local)];
                    sessions = checked(sessions + (binding.Kind == NativeBurstDispatchBindingKindV2.AsyncOperation ? 2u : 1u));
                    var bytes = checked(binding.PrimaryValueSize
                        + (binding.Kind == NativeBurstDispatchBindingKindV2.AsyncOperation ? binding.SecondaryValueSize : 0u));
                    staging = checked(staging + bytes);
                    if (binding.Kind == NativeBurstDispatchBindingKindV2.EffectCommand
                        || binding.Kind == NativeBurstDispatchBindingKindV2.AsyncOperation)
                    {
                        commands++;
                        commandBytes = checked(commandBytes + bytes);
                    }
                    if (binding.Kind == NativeBurstDispatchBindingKindV2.AsyncOperation) operations++;
                }
                if (sessions > maxSessions) maxSessions = sessions;
                if (staging > maxStaging) maxStaging = staging;
                if (commands > maxCommands) maxCommands = commands;
                if (commandBytes > maxCommandBytes) maxCommandBytes = commandBytes;
                if (operations > maxOperations) maxOperations = operations;
            }
            return new NativeBurstDispatchWorkspaceCapacityV2(
                maxMemory,
                _bindings.Length == 0
                    ? default
                    : new NativeBurstDispatchBindingCapacityV2(
                        maxSessions, maxStaging, maxCommands, maxCommandBytes, maxOperations, 1u));
        }

        private bool ValidateShape(out BurstContextResult failure)
        {
            var shape = CreateWorkspaceShape();
            var capacity = CreateWorkspaceCapacity();
            if (!NativeBurstDispatchWorkspaceOwnerV2.TryCreate(
                    in shape, in capacity, Allocator.Temp, out var validator, out failure))
                return false;
            validator.TryDispose(out _);
            return true;
        }

        private static bool TryDecode(byte[] blob, out DecodedLayout decoded)
        {
            decoded = default;
            var reader = new LayoutReader(blob);
            if (!reader.Bytes(LayoutMagic) || !reader.U32(out var version) || version != 1u
                || !reader.Count(47, out var caseCount)) return false;

            var cases = new NativeBurstDispatchCaseV2[caseCount];
            for (var index = 0; index < cases.Length; index++)
            {
                if (!reader.U64(out var typeId) || !reader.U32(out var typeVersion)
                    || !reader.U32(out var caseIndex) || !reader.U32(out var firstConfigurationField)
                    || !reader.U32(out var configurationFieldCount) || !reader.U32(out var configurationSize)
                    || !reader.U32(out var firstMemoryField) || !reader.U32(out var memoryFieldCount)
                    || !reader.U32(out var memorySize) || !reader.U8(out var phases)
                    || !reader.U8(out var statuses) || !reader.U8(out var random)
                    || !reader.U32(out var firstBinding) || !reader.U32(out var bindingCount)) return false;
                cases[index] = new NativeBurstDispatchCaseV2(
                    typeId, typeVersion, caseIndex, firstConfigurationField, configurationFieldCount,
                    configurationSize, firstMemoryField, memoryFieldCount, memorySize,
                    (NativeBurstDispatchPhaseMaskV2)phases, (BurstNodeStatusMask)statuses,
                    random != 0, firstBinding, bindingCount);
            }

            if (!TryReadFields(ref reader, out var configurationFields)
                || !TryReadFields(ref reader, out var memoryFields)
                || !reader.Count(51, out var bindingCountValue)) return false;
            var bindings = new NativeBurstDispatchBindingV2[bindingCountValue];
            for (var index = 0; index < bindings.Length; index++)
            {
                if (!reader.U32(out var ordinal) || !reader.U32(out var configurationFieldOrdinal)
                    || !reader.U8(out var kind) || !reader.U8(out var scope) || !reader.U8(out var phaseMask)
                    || !reader.U64(out var primaryTypeId) || !reader.U32(out var primaryVersion)
                    || !reader.U32(out var firstPrimaryField) || !reader.U32(out var primaryFieldCount)
                    || !reader.U32(out var primarySize) || !reader.U64(out var secondaryTypeId)
                    || !reader.U32(out var secondaryVersion) || !reader.U32(out var firstSecondaryField)
                    || !reader.U32(out var secondaryFieldCount) || !reader.U32(out var secondarySize)) return false;
                bindings[index] = new NativeBurstDispatchBindingV2(
                    ordinal, configurationFieldOrdinal, (NativeBurstDispatchBindingKindV2)kind,
                    scope, (NativeBurstDispatchBindingPhaseMaskV2)phaseMask,
                    primaryTypeId, primaryVersion, firstPrimaryField, primaryFieldCount, primarySize,
                    secondaryTypeId, secondaryVersion, firstSecondaryField, secondaryFieldCount, secondarySize);
            }

            if (!TryReadFields(ref reader, out var valueFields)
                || !TryReadRanges(ref reader, out var caseRanges)
                || !TryReadRanges(ref reader, out var bindingRanges)
                || !reader.Count(5, out var ruleCount)) return false;
            var rules = new NativeBurstDispatchCanonicalRuleV2[ruleCount];
            for (var index = 0; index < rules.Length; index++)
            {
                if (!reader.U8(out var kind) || !reader.U32(out var offset)) return false;
                rules[index] = new NativeBurstDispatchCanonicalRuleV2(
                    (NativeBurstDispatchCanonicalRuleKindV2)kind, offset);
            }
            if (!reader.AtEnd) return false;
            decoded = new DecodedLayout(
                cases, configurationFields, memoryFields, bindings, valueFields,
                caseRanges, bindingRanges, rules);
            return true;
        }

        private static bool TryReadFields(ref LayoutReader reader, out NativeBurstDispatchFieldV2[] fields)
        {
            fields = null;
            if (!reader.Count(22, out var count)) return false;
            var result = new NativeBurstDispatchFieldV2[count];
            for (var index = 0; index < result.Length; index++)
            {
                if (!reader.U32(out var ordinal) || !reader.U32(out var firstElement)
                    || !reader.U32(out var offset) || !reader.U32(out var elementCount)
                    || !reader.U32(out var elementSize) || !reader.U8(out var encoding)
                    || !reader.U8(out var canonicalRule)) return false;
                result[index] = new NativeBurstDispatchFieldV2(
                    ordinal, firstElement, offset, elementCount, elementSize,
                    (NativeBurstDispatchFieldEncodingV2)encoding,
                    (NativeBurstDispatchCanonicalRuleKindV2)canonicalRule);
            }
            fields = result;
            return true;
        }

        private static bool TryReadRanges(ref LayoutReader reader, out NativeBurstDispatchCanonicalRangeV2[] ranges)
        {
            ranges = null;
            if (!reader.Count(8, out var count)) return false;
            var result = new NativeBurstDispatchCanonicalRangeV2[count];
            for (var index = 0; index < result.Length; index++)
            {
                if (!reader.U32(out var first) || !reader.U32(out var length)) return false;
                result[index] = new NativeBurstDispatchCanonicalRangeV2(first, length);
            }
            ranges = result;
            return true;
        }

        private void DisposeArrays()
        {
            Dispose(ref _canonicalRules);
            Dispose(ref _bindingRanges);
            Dispose(ref _caseRanges);
            Dispose(ref _valueFields);
            Dispose(ref _bindings);
            Dispose(ref _memoryFields);
            Dispose(ref _configurationFields);
            Dispose(ref _cases);
        }

        private static void Dispose<T>(ref NativeArray<T> array) where T : struct
        {
            if (!array.IsCreated) return;
            array.Dispose();
            array = default;
        }

        private readonly struct DecodedLayout
        {
            internal DecodedLayout(
                NativeBurstDispatchCaseV2[] cases,
                NativeBurstDispatchFieldV2[] configurationFields,
                NativeBurstDispatchFieldV2[] memoryFields,
                NativeBurstDispatchBindingV2[] bindings,
                NativeBurstDispatchFieldV2[] valueFields,
                NativeBurstDispatchCanonicalRangeV2[] caseRanges,
                NativeBurstDispatchCanonicalRangeV2[] bindingRanges,
                NativeBurstDispatchCanonicalRuleV2[] canonicalRules)
            {
                Cases = cases; ConfigurationFields = configurationFields; MemoryFields = memoryFields;
                Bindings = bindings; ValueFields = valueFields; CaseRanges = caseRanges;
                BindingRanges = bindingRanges; CanonicalRules = canonicalRules;
            }
            internal NativeBurstDispatchCaseV2[] Cases { get; }
            internal NativeBurstDispatchFieldV2[] ConfigurationFields { get; }
            internal NativeBurstDispatchFieldV2[] MemoryFields { get; }
            internal NativeBurstDispatchBindingV2[] Bindings { get; }
            internal NativeBurstDispatchFieldV2[] ValueFields { get; }
            internal NativeBurstDispatchCanonicalRangeV2[] CaseRanges { get; }
            internal NativeBurstDispatchCanonicalRangeV2[] BindingRanges { get; }
            internal NativeBurstDispatchCanonicalRuleV2[] CanonicalRules { get; }
        }

        private struct LayoutReader
        {
            private readonly byte[] _bytes;
            private int _offset;
            internal LayoutReader(byte[] bytes) { _bytes = bytes; _offset = 0; }
            internal bool AtEnd => _offset == _bytes.Length;
            internal bool Bytes(byte[] expected)
            {
                if (expected == null || _offset > _bytes.Length - expected.Length) return false;
                for (var index = 0; index < expected.Length; index++)
                    if (_bytes[_offset + index] != expected[index]) return false;
                _offset += expected.Length;
                return true;
            }
            internal bool U8(out byte value)
            {
                value = 0;
                if (_offset >= _bytes.Length) return false;
                value = _bytes[_offset++];
                return true;
            }
            internal bool U32(out uint value)
            {
                value = 0;
                if (_offset > _bytes.Length - 4) return false;
                value = (uint)(_bytes[_offset] | _bytes[_offset + 1] << 8
                    | _bytes[_offset + 2] << 16 | _bytes[_offset + 3] << 24);
                _offset += 4;
                return true;
            }
            internal bool U64(out ulong value)
            {
                value = 0;
                if (!U32(out var low) || !U32(out var high)) return false;
                value = low | (ulong)high << 32;
                return true;
            }
            internal bool Count(int minimumBytesPerItem, out int count)
            {
                count = 0;
                if (!U32(out var raw) || raw > int.MaxValue) return false;
                var remaining = _bytes.Length - _offset;
                if ((ulong)raw * (uint)minimumBytesPerItem > (ulong)remaining) return false;
                count = (int)raw;
                return true;
            }
        }
    }
}
