using System;
using System.Collections.Generic;

namespace AIBT
{
    internal sealed class ReferenceBlackboardStorage
    {
        private readonly CompiledProgram _program;
        private readonly TreeInstanceId _treeInstanceId;
        private readonly RegisteredBlackboardRegistry _registeredTypes;
        private readonly byte[] _arena;
        private readonly byte[] _defaults;
        private readonly ulong[] _slotVersions;

        private ReferenceBlackboardStorage(
            CompiledProgram program,
            TreeInstanceId treeInstanceId,
            RegisteredBlackboardRegistry registeredTypes,
            byte[] arena,
            byte[] defaults)
        {
            _program = program;
            _treeInstanceId = treeInstanceId;
            _registeredTypes = registeredTypes;
            _arena = arena;
            _defaults = defaults;
            _slotVersions = new ulong[program.BlackboardSlots.Count];
        }

        internal ulong Revision { get; private set; }
        internal int SlotCount => _slotVersions.Length;
        internal int ArenaSize => _arena.Length;

        internal static bool TryCreate(
            CompiledProgram program,
            TreeInstanceId treeInstanceId,
            RegisteredBlackboardRegistry registeredTypes,
            out ReferenceBlackboardStorage storage,
            out Diagnostic diagnostic,
            CompiledHash expectedRegisteredRegistryHash = default,
            IReadOnlyList<ReferenceBlackboardInitialValue> initialValues = null)
        {
            storage = null;
            diagnostic = null;
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (!treeInstanceId.IsValid) throw new ArgumentException("A tree instance ID is required.", nameof(treeInstanceId));
            registeredTypes = registeredTypes ?? RegisteredBlackboardRegistry.Empty;
            if ((expectedRegisteredRegistryHash.IsValid && registeredTypes.Hash != expectedRegisteredRegistryHash)
                || !registeredTypes.HashMatchesExpected)
            {
                diagnostic = Create(BlackboardStorageDiagnosticCodes.RegistryMismatch,
                    "The registered blackboard binding registry hash does not match the required contract.", treeInstanceId);
                return false;
            }

            ulong arenaSize = 0;
            for (var index = 0; index < program.BlackboardSlots.Count; index++)
            {
                var slot = program.BlackboardSlots[index];
                if (slot.Scope != BlackboardScope.Tree)
                {
                    diagnostic = Create(BlackboardStorageDiagnosticCodes.UnsupportedScope,
                        "Phase 1 reference storage supports only Tree-scope compiled slots.", treeInstanceId);
                    return false;
                }

                if (!TryResolveType(slot, registeredTypes, out _, out _))
                {
                    diagnostic = Create(BlackboardStorageDiagnosticCodes.MissingTypeBinding,
                        "A compiled blackboard slot has no matching built-in or registered runtime binding.", treeInstanceId);
                    return false;
                }

                arenaSize = Math.Max(arenaSize, (ulong)slot.Offset + slot.Size);
            }

            if (arenaSize > int.MaxValue)
            {
                diagnostic = Create(BlackboardStorageDiagnosticCodes.InvalidSlot,
                    "The reference blackboard arena exceeds managed array capacity.", treeInstanceId);
                return false;
            }

            var arena = new byte[(int)arenaSize];
            var defaults = new byte[(int)arenaSize];
            for (var index = 0; index < program.BlackboardSlots.Count; index++)
            {
                var slot = program.BlackboardSlots[index];
                Copy(program.DefaultValueBlob, slot.DefaultValueOffset, defaults, slot.Offset, slot.Size);
                Copy(program.DefaultValueBlob, slot.DefaultValueOffset, arena, slot.Offset, slot.Size);
                if (!ValidateStoredValue(slot, arena, registeredTypes))
                {
                    diagnostic = Create(BlackboardStorageDiagnosticCodes.InvalidValue,
                        "A compiled blackboard default is invalid for its declared runtime type.", treeInstanceId);
                    return false;
                }
            }

            if (!TryApplyInitialValues(
                program,
                treeInstanceId,
                registeredTypes,
                arena,
                initialValues,
                out diagnostic))
            {
                return false;
            }

            storage = new ReferenceBlackboardStorage(program, treeInstanceId, registeredTypes, arena, defaults);
            return true;
        }

        internal ulong GetSlotVersion(uint slotIndex)
        {
            if (slotIndex >= _slotVersions.Length) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            return _slotVersions[slotIndex];
        }

        internal ReferenceBlackboardSnapshot CaptureSnapshot()
        {
            var entries = new ReferenceBlackboardSnapshotEntry[_program.BlackboardSlots.Count];
            for (var index = 0; index < entries.Length; index++)
            {
                var slot = _program.BlackboardSlots[index];
                if (!TryResolveType(slot, _registeredTypes, out var type, out _))
                    throw new InvalidOperationException("A compiled blackboard slot lost its runtime type binding.");

                if (type.ValueType == BlackboardValueType.Registered)
                {
                    entries[index] = new ReferenceBlackboardSnapshotEntry(
                        (uint)index,
                        slot.StableKeyId,
                        type,
                        _slotVersions[index],
                        default,
                        Slice(_arena, slot.Offset, slot.Size).ToArray());
                    continue;
                }

                if (!BlackboardBuiltInCodec.TryDecode(
                    type,
                    Slice(_arena, slot.Offset, slot.Size),
                    out var value))
                {
                    throw new InvalidOperationException("A built-in blackboard slot contains invalid runtime bytes.");
                }

                entries[index] = new ReferenceBlackboardSnapshotEntry(
                    (uint)index,
                    slot.StableKeyId,
                    type,
                    _slotVersions[index],
                    value,
                    null);
            }

            return new ReferenceBlackboardSnapshot(Revision, entries);
        }

        internal BlackboardStorageResult TryRead(
            RuntimeNodeIndex nodeIndex,
            uint declaredReadOrdinal,
            out BlackboardValue value)
        {
            value = default;
            if (!TryResolveDeclaredSlot(nodeIndex, declaredReadOrdinal, false, out var slotIndex, out var slot, out var failure)) return failure;
            if (!TryResolveType(slot, _registeredTypes, out var builtIn, out _))
            {
                return Fail(BlackboardStorageDiagnosticCodes.MissingTypeBinding, "The slot type binding is unavailable.");
            }

            if (builtIn.ValueType == BlackboardValueType.Registered)
            {
                return Fail(BlackboardStorageDiagnosticCodes.TypeMismatch, "Registered slots must be read through the registered byte contract.");
            }

            if (!BlackboardBuiltInCodec.TryDecode(builtIn, Slice(_arena, slot.Offset, slot.Size), out value))
            {
                return Fail(BlackboardStorageDiagnosticCodes.InvalidValue, "The slot contains invalid bytes for its declared built-in type.");
            }

            if (builtIn.ValueType == BlackboardValueType.Enum32
                && (!value.TryGetEnum32(out var enumValue) || enumValue.ContractTypeId != slot.EnumContractId))
            {
                value = default;
                return Fail(BlackboardStorageDiagnosticCodes.InvalidValue, "The stored enum contract does not match the compiled slot contract.");
            }

            var version = _slotVersions[slotIndex];
            return BlackboardStorageResult.ForSlot(slotIndex, slot.StableKeyId, version, version);
        }

        internal BlackboardStorageResult TryWrite(
            RuntimeNodeIndex nodeIndex,
            uint declaredWriteOrdinal,
            BlackboardValue value)
        {
            if (!TryResolveDeclaredSlot(nodeIndex, declaredWriteOrdinal, true, out var slotIndex, out var slot, out var failure)) return failure;
            if (!TryResolveType(slot, _registeredTypes, out var builtIn, out _)
                || builtIn.ValueType == BlackboardValueType.Registered
                || value.Type != builtIn.ValueType)
            {
                return Fail(BlackboardStorageDiagnosticCodes.TypeMismatch, "The write value does not match the compiled slot type.");
            }

            if (builtIn.ValueType == BlackboardValueType.Enum32
                && (!value.TryGetEnum32(out var enumValue) || enumValue.ContractTypeId != slot.EnumContractId))
            {
                return Fail(BlackboardStorageDiagnosticCodes.TypeMismatch, "The enum write contract does not match the compiled slot contract.");
            }

            if (!BlackboardBuiltInCodec.TryEncode(value, builtIn, out var bytes))
            {
                return Fail(BlackboardStorageDiagnosticCodes.InvalidValue, "The write value is invalid for its declared built-in type.");
            }

            return WriteBytes(slotIndex, slot, bytes, null);
        }

        internal BlackboardStorageResult TryReadRegistered(
            RuntimeNodeIndex nodeIndex,
            uint declaredReadOrdinal,
            ulong typeId,
            uint version,
            out byte[] value)
        {
            value = null;
            if (!TryResolveDeclaredSlot(nodeIndex, declaredReadOrdinal, false, out var slotIndex, out var slot, out var failure)) return failure;
            if (slot.TypeId != typeId || slot.TypeVersion != version
                || !_registeredTypes.TryGet(typeId, version, out _))
            {
                return Fail(BlackboardStorageDiagnosticCodes.TypeMismatch, "The registered read contract does not match the compiled slot type.");
            }

            value = Slice(_arena, slot.Offset, slot.Size).ToArray();
            var slotVersion = _slotVersions[slotIndex];
            return BlackboardStorageResult.ForSlot(slotIndex, slot.StableKeyId, slotVersion, slotVersion);
        }

        internal BlackboardStorageResult TryWriteRegistered(
            RuntimeNodeIndex nodeIndex,
            uint declaredWriteOrdinal,
            ulong typeId,
            uint version,
            ReadOnlySpan<byte> value)
        {
            if (!TryResolveDeclaredSlot(nodeIndex, declaredWriteOrdinal, true, out var slotIndex, out var slot, out var failure)) return failure;
            if (slot.TypeId != typeId || slot.TypeVersion != version
                || !_registeredTypes.TryGet(typeId, version, out var binding)
                || value.Length != binding.Descriptor.Size)
            {
                return Fail(BlackboardStorageDiagnosticCodes.TypeMismatch, "The registered write contract does not match the compiled slot type and size.");
            }

            return WriteBytes(slotIndex, slot, value, binding.Equality);
        }

        internal BlackboardResetResult Reset()
        {
            var changedSlots = new List<uint>();
            for (var index = 0; index < _program.BlackboardSlots.Count; index++)
            {
                var slot = _program.BlackboardSlots[index];
                var current = Slice(_arena, slot.Offset, slot.Size);
                var expected = Slice(_defaults, slot.Offset, slot.Size);
                if (!TryAreEqual(slot, current, expected, out var equal))
                {
                    return ResetFail(
                        BlackboardStorageDiagnosticCodes.EqualityFault,
                        "The registered blackboard equality contract threw during reset.");
                }

                if (equal) continue;
                if (_slotVersions[index] == ulong.MaxValue)
                {
                    return ResetFail(BlackboardStorageDiagnosticCodes.VersionOverflow, "A blackboard slot version cannot advance without wrapping.");
                }

                changedSlots.Add((uint)index);
            }

            if (changedSlots.Count == 0) return BlackboardResetResult.Unchanged;

            if (Revision == ulong.MaxValue)
            {
                return ResetFail(BlackboardStorageDiagnosticCodes.VersionOverflow, "The tree blackboard revision cannot advance without wrapping.");
            }

            var changes = new BlackboardSlotChange[changedSlots.Count];
            for (var changedIndex = 0; changedIndex < changedSlots.Count; changedIndex++)
            {
                var slotIndex = changedSlots[changedIndex];
                var slot = _program.BlackboardSlots[(int)slotIndex];
                var expected = Slice(_defaults, slot.Offset, slot.Size);
                expected.CopyTo(Slice(_arena, slot.Offset, slot.Size));
                var oldVersion = _slotVersions[slotIndex];
                var newVersion = ++_slotVersions[slotIndex];
                changes[changedIndex] = new BlackboardSlotChange(
                    slotIndex, slot.StableKeyId, oldVersion, newVersion);
            }

            Revision++;
            return new BlackboardResetResult(true, changes, null);
        }

        private BlackboardStorageResult WriteBytes(
            uint slotIndex,
            CompiledBlackboardSlotRecord slot,
            ReadOnlySpan<byte> value,
            RegisteredBlackboardEquality equality)
        {
            var current = Slice(_arena, slot.Offset, slot.Size);
            bool equal;
            try
            {
                equal = equality == null ? current.SequenceEqual(value) : equality(current, value);
            }
            catch (Exception)
            {
                return Fail(
                    BlackboardStorageDiagnosticCodes.EqualityFault,
                    "The registered blackboard equality contract threw during write.");
            }

            var oldVersion = _slotVersions[slotIndex];
            if (equal) return BlackboardStorageResult.ForSlot(slotIndex, slot.StableKeyId, oldVersion, oldVersion);
            if (_slotVersions[slotIndex] == ulong.MaxValue || Revision == ulong.MaxValue)
            {
                return Fail(BlackboardStorageDiagnosticCodes.VersionOverflow, "A blackboard version cannot advance without wrapping.");
            }

            value.CopyTo(current);
            var newVersion = ++_slotVersions[slotIndex];
            Revision++;
            return BlackboardStorageResult.ForSlot(slotIndex, slot.StableKeyId, oldVersion, newVersion);
        }

        private bool TryResolveDeclaredSlot(
            RuntimeNodeIndex nodeIndex,
            uint declaredOrdinal,
            bool write,
            out uint slotIndex,
            out CompiledBlackboardSlotRecord slot,
            out BlackboardStorageResult failure)
        {
            slotIndex = CompiledIndex.Invalid;
            slot = default;
            failure = default;
            if (!nodeIndex.IsValid || nodeIndex.Value >= _program.Nodes.Count)
            {
                failure = Fail(BlackboardStorageDiagnosticCodes.InvalidSlot, "The node index is outside the compiled program.");
                return false;
            }

            var node = _program.Nodes[(int)nodeIndex.Value];
            var range = write ? node.WriteSlots : node.ReadSlots;
            var table = write ? _program.WriteSlotIndices : _program.ReadSlotIndices;
            if (declaredOrdinal >= range.Count)
            {
                failure = Fail(BlackboardStorageDiagnosticCodes.UndeclaredAccess, "The declared blackboard access ordinal is outside the node contract.");
                return false;
            }

            slotIndex = table[checked((int)(range.Offset + declaredOrdinal))];
            slot = _program.BlackboardSlots[(int)slotIndex];
            return true;
        }

        private bool TryAreEqual(
            CompiledBlackboardSlotRecord slot,
            ReadOnlySpan<byte> left,
            ReadOnlySpan<byte> right,
            out bool equal)
        {
            try
            {
                equal = _registeredTypes.TryGet(slot.TypeId, slot.TypeVersion, out var binding)
                    ? binding.Equality(left, right)
                    : left.SequenceEqual(right);
                return true;
            }
            catch (Exception)
            {
                equal = false;
                return false;
            }
        }

        private BlackboardStorageResult Fail(DiagnosticCode code, string message)
            => BlackboardStorageResult.Failed(Create(code, message, _treeInstanceId));

        private BlackboardResetResult ResetFail(DiagnosticCode code, string message)
            => BlackboardResetResult.Failed(Create(code, message, _treeInstanceId));

        private static Diagnostic Create(DiagnosticCode code, string message, TreeInstanceId treeInstanceId)
            => BlackboardStorageDiagnostics.Create(code, message, treeInstanceId);

        private static bool ValidateStoredValue(
            CompiledBlackboardSlotRecord slot,
            byte[] arena,
            RegisteredBlackboardRegistry registry)
        {
            if (!TryResolveType(slot, registry, out var builtIn, out _)) return false;
            if (builtIn.ValueType == BlackboardValueType.Registered) return true;
            if (!BlackboardBuiltInCodec.TryDecode(builtIn, Slice(arena, slot.Offset, slot.Size), out var value)) return false;
            if (builtIn.ValueType == BlackboardValueType.Enum32
                && (!value.TryGetEnum32(out var enumValue) || enumValue.ContractTypeId != slot.EnumContractId))
            {
                return false;
            }

            return BlackboardBuiltInCodec.TryEncode(value, builtIn, out var canonical)
                && Slice(arena, slot.Offset, slot.Size).SequenceEqual(canonical);
        }

        private static bool TryApplyInitialValues(
            CompiledProgram program,
            TreeInstanceId treeInstanceId,
            RegisteredBlackboardRegistry registry,
            byte[] arena,
            IReadOnlyList<ReferenceBlackboardInitialValue> initialValues,
            out Diagnostic diagnostic)
        {
            diagnostic = null;
            if (initialValues == null || initialValues.Count == 0) return true;

            var slotsByKey = new Dictionary<ulong, uint>();
            for (var index = 0; index < program.BlackboardSlots.Count; index++)
                slotsByKey.Add(program.BlackboardSlots[index].StableKeyId, (uint)index);

            var seen = new HashSet<ulong>();
            var writes = new List<InitialWrite>(initialValues.Count);
            for (var index = 0; index < initialValues.Count; index++)
            {
                var initial = initialValues[index];
                if (!initial.IsValid)
                {
                    diagnostic = Create(
                        BlackboardStorageDiagnosticCodes.InvalidValue,
                        "An initial blackboard value is invalid.",
                        treeInstanceId);
                    return false;
                }

                if (!seen.Add(initial.StableKeyId))
                {
                    diagnostic = Create(
                        BlackboardStorageDiagnosticCodes.InvalidSlot,
                        "Initial blackboard values must name each stable key at most once.",
                        treeInstanceId);
                    return false;
                }

                if (!slotsByKey.TryGetValue(initial.StableKeyId, out var slotIndex))
                {
                    diagnostic = Create(
                        BlackboardStorageDiagnosticCodes.InvalidSlot,
                        "An initial blackboard value names a key outside the compiled program.",
                        treeInstanceId);
                    return false;
                }

                var slot = program.BlackboardSlots[(int)slotIndex];
                if (!TryResolveType(slot, registry, out var type, out var registered))
                {
                    diagnostic = Create(
                        BlackboardStorageDiagnosticCodes.MissingTypeBinding,
                        "An initial blackboard value has no matching runtime type binding.",
                        treeInstanceId);
                    return false;
                }

                byte[] bytes;
                if (initial.IsRegistered)
                {
                    bytes = initial.CopyRegisteredBytes();
                    if (type.ValueType != BlackboardValueType.Registered
                        || initial.RegisteredTypeId != slot.TypeId
                        || initial.RegisteredTypeVersion != slot.TypeVersion
                        || registered.Descriptor.Size != bytes.Length)
                    {
                        diagnostic = Create(
                            BlackboardStorageDiagnosticCodes.TypeMismatch,
                            "An initial registered value does not match its compiled slot contract.",
                            treeInstanceId);
                        return false;
                    }
                }
                else
                {
                    var value = initial.BuiltInValue;
                    if (type.ValueType == BlackboardValueType.Registered || value.Type != type.ValueType)
                    {
                        diagnostic = Create(
                            BlackboardStorageDiagnosticCodes.TypeMismatch,
                            "An initial built-in value does not match its compiled slot type.",
                            treeInstanceId);
                        return false;
                    }

                    if (type.ValueType == BlackboardValueType.Enum32
                        && (!value.TryGetEnum32(out var enumValue)
                            || enumValue.ContractTypeId != slot.EnumContractId))
                    {
                        diagnostic = Create(
                            BlackboardStorageDiagnosticCodes.TypeMismatch,
                            "An initial enum value does not match its compiled enum contract.",
                            treeInstanceId);
                        return false;
                    }

                    if (!BlackboardBuiltInCodec.TryEncode(value, type, out bytes)
                        || bytes.Length != slot.Size)
                    {
                        diagnostic = Create(
                            BlackboardStorageDiagnosticCodes.InvalidValue,
                            "An initial built-in value cannot be encoded canonically for its compiled slot.",
                            treeInstanceId);
                        return false;
                    }
                }

                writes.Add(new InitialWrite(slot.Offset, slot.Size, bytes));
            }

            for (var index = 0; index < writes.Count; index++)
            {
                var write = writes[index];
                new ReadOnlySpan<byte>(write.Bytes).CopyTo(Slice(arena, write.Offset, write.Size));
            }

            return true;
        }

        private static bool TryResolveType(
            CompiledBlackboardSlotRecord slot,
            RegisteredBlackboardRegistry registry,
            out BlackboardTypeDescriptor descriptor,
            out RegisteredBlackboardBinding registered)
        {
            for (var raw = (int)BlackboardValueType.Bool; raw <= (int)BlackboardValueType.AssetId; raw++)
            {
                if (!BuiltInBlackboardTypes.TryGet((BlackboardValueType)raw, out var candidate)) continue;
                if (candidate.TypeId == slot.TypeId && candidate.Version == slot.TypeVersion
                    && candidate.Size == slot.Size && candidate.Alignment == slot.Alignment)
                {
                    descriptor = candidate;
                    registered = default;
                    return true;
                }
            }

            if (registry.TryGet(slot.TypeId, slot.TypeVersion, out registered)
                && registered.Descriptor.Size == slot.Size
                && registered.Descriptor.Alignment == slot.Alignment)
            {
                descriptor = BlackboardTypeDescriptor.FromRegistered(registered.Descriptor);
                return true;
            }

            descriptor = default;
            registered = default;
            return false;
        }

        private static Span<byte> Slice(byte[] bytes, uint offset, uint size)
            => new Span<byte>(bytes, checked((int)offset), checked((int)size));

        private static void Copy(IReadOnlyList<byte> source, uint sourceOffset, byte[] target, uint targetOffset, uint size)
        {
            for (uint index = 0; index < size; index++) target[(int)(targetOffset + index)] = source[(int)(sourceOffset + index)];
        }

        private readonly struct InitialWrite
        {
            internal InitialWrite(uint offset, uint size, byte[] bytes)
            {
                Offset = offset;
                Size = size;
                Bytes = bytes;
            }

            internal uint Offset { get; }
            internal uint Size { get; }
            internal byte[] Bytes { get; }
        }
    }
}
