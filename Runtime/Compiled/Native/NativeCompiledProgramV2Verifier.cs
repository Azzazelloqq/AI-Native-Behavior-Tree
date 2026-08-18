using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace AIBT
{
    internal static class NativeCompiledProgramV2Verifier
    {
        internal const ulong CanonicalBytesEqualityContractId = 0x69e3a80e385e338eUL;
        private const uint AgentScopeCapability = 1u << 7;
        private const uint SharedScopeCapability = 1u << 8;

        internal static bool TryValidate(
            NativeProgramBlackboardBindingV2 binding,
            out NativeResourceKindV1 resource)
        {
            resource = NativeResourceKindV1.ProgramScopeDescriptors;
            if (binding == null || !ValidateScopeCoverage(binding)) return false;
            resource = NativeResourceKindV1.ProgramHash;
            if (!ValidateOuter(binding)) return false;
            resource = NativeResourceKindV1.ProgramRegisteredFields;
            if (!ValidateRegistered(binding)) return false;
            resource = NativeResourceKindV1.ProgramBlackboardSlots;
            for (var index = 0; index < binding.Slots.Count; index++)
                if (!CanonicalDefaultMatches(binding, binding.Slots[index], binding.SlotAuthorities[index])) return false;
            resource = NativeResourceKindV1.ProgramScopeDescriptors;
            for (var index = 0; index < binding.Scopes.Count; index++)
                if (!ValidateScope(binding, index)) return false;
            resource = default;
            return true;
        }

        private static bool ValidateScopeCoverage(NativeProgramBlackboardBindingV2 binding)
        {
            var covered = new bool[binding.Slots.Count];
            var hasAgentDescriptor = false; var hasSharedDescriptor = false;
            for (var descriptorIndex = 0; descriptorIndex < binding.Scopes.Count; descriptorIndex++)
            {
                var descriptor = binding.Scopes[descriptorIndex];
                if (descriptor.SlotCount == 0
                    || (ulong)descriptor.FirstSlot + descriptor.SlotCount > (uint)binding.Slots.Count)
                    return false;
                if (descriptor.Scope == BlackboardScope.Agent)
                { if (hasAgentDescriptor) return false; hasAgentDescriptor = true; }
                else if (descriptor.Scope == BlackboardScope.Shared)
                { if (hasSharedDescriptor) return false; hasSharedDescriptor = true; }
                else return false;
                for (var slotIndex = descriptor.FirstSlot; slotIndex < descriptor.FirstSlot + descriptor.SlotCount; slotIndex++)
                {
                    if (covered[slotIndex]) return false;
                    var slot = binding.Slots[(int)slotIndex];
                    if (slot.Scope != descriptor.Scope || slot.ScopeDescriptorIndex != (uint)descriptorIndex)
                        return false;
                    covered[slotIndex] = true;
                }
            }

            var hasAgentSlot = false; var hasSharedSlot = false;
            for (var slotIndex = 0; slotIndex < binding.Slots.Count; slotIndex++)
            {
                var slot = binding.Slots[slotIndex];
                if (slot.Scope == BlackboardScope.Tree)
                {
                    if (slot.ScopeDescriptorIndex != CompiledIndex.Invalid || covered[slotIndex]) return false;
                    continue;
                }
                if (slot.Scope == BlackboardScope.Agent) hasAgentSlot = true;
                else if (slot.Scope == BlackboardScope.Shared) hasSharedSlot = true;
                else return false;
                if (!covered[slotIndex] || slot.ScopeDescriptorIndex == CompiledIndex.Invalid) return false;
            }

            if (hasAgentDescriptor != hasAgentSlot || hasSharedDescriptor != hasSharedSlot) return false;
            var expected = (hasAgentSlot ? AgentScopeCapability : 0u)
                | (hasSharedSlot ? SharedScopeCapability : 0u);
            return (binding.SemanticProgram.Header.CapabilityFlags
                & (AgentScopeCapability | SharedScopeCapability)) == expected;
        }

        private static bool ValidateOuter(NativeProgramBlackboardBindingV2 binding)
        {
            var program = binding.SemanticProgram;
            if (binding.SlotAuthorities.Count != binding.Slots.Count) return false;
            if (binding.SemanticProgram.BlackboardSlots.Count != binding.Slots.Count) return false;
            for (var index = 0; index < binding.Slots.Count; index++)
            {
                var slot = binding.Slots[index];
                var semanticSlot = binding.SemanticProgram.BlackboardSlots[index];
                if (semanticSlot.StableKeyId != slot.StableKeyId || semanticSlot.TypeId != slot.TypeId
                    || semanticSlot.TypeVersion != slot.TypeVersion || semanticSlot.EnumContractId != slot.EnumContractId
                    || semanticSlot.Scope != slot.Scope || semanticSlot.Offset != slot.Offset
                    || semanticSlot.Size != slot.Size || semanticSlot.Alignment != slot.Alignment
                    || semanticSlot.DefaultValueOffset != slot.DefaultOffset || slot.DefaultSize != slot.Size
                    || semanticSlot.AccessFlags != slot.AccessFlags) return false;
            }
            if (binding.WatchedSlots.Count != binding.SemanticProgram.WatchedSlotIndices.Count) return false;
            for (var index = 0; index < binding.WatchedSlots.Count; index++)
            {
                var watched = binding.WatchedSlots[index];
                var found = CompiledIndex.Invalid;
                for (var slotIndex = 0; slotIndex < binding.Slots.Count; slotIndex++)
                    if (binding.Slots[slotIndex].Scope == watched.Scope
                        && binding.Slots[slotIndex].ScopeSlotIndex == watched.SlotIndex)
                    { if (found != CompiledIndex.Invalid) return false; found = (uint)slotIndex; }
                if (found == CompiledIndex.Invalid || binding.SemanticProgram.WatchedSlotIndices[index] != found) return false;
            }
            var header = program.Header;
            var reader = new Reader(binding.GetOuterPreimageCopy());
            if (!reader.U32(header.Magic) || !reader.U32(2) || !reader.U32(header.ExecutionSemanticsVersion)
                || !reader.U16(header.CompilerVersion.Major) || !reader.U16(header.CompilerVersion.Minor)
                || !reader.U16(header.CompilerVersion.Patch) || !reader.U32(header.CompilerVersion.BuildRevision)
                || !reader.Hash(header.CanonicalSemanticHash) || !reader.Hash(header.NodeRegistryHash)
                || !reader.Hash(header.CanonicalPolicyHash) || !reader.U32(header.PolicyFormatVersion)
                || !reader.U32(header.RootNodeIndex) || !reader.U32((uint)program.Nodes.Count)
                || !reader.U32((uint)program.ChildIndices.Count) || !reader.U32((uint)binding.Slots.Count)
                || !reader.U32((uint)program.DebugMap.Count) || !reader.U32((uint)program.ConfigBlob.Count)
                || !reader.U32(header.InstanceNodeMemorySize) || !reader.U32(header.RequiredMaximumAlignment))
                return false;
            var capabilities = header.CapabilityFlags;
            for (var index = 0; index < binding.Scopes.Count; index++)
                capabilities |= binding.Scopes[index].Scope == BlackboardScope.Agent ? 1u << 7 : 1u << 8;
            if (!reader.U32(capabilities) || !reader.U8(header.DeterministicModeCompatible ? (byte)1 : (byte)0)
                || !reader.U32((uint)binding.Scopes.Count)) return false;
            for (var index = 0; index < binding.Scopes.Count; index++)
            {
                var scope = binding.Scopes[index];
                if (!reader.U8(Scope(scope.Scope)) || !reader.String(scope.ContractId)
                    || !reader.U64(scope.ContractNumericId) || !reader.U32(scope.ContractVersion)
                    || !reader.Hash(scope.SchemaHash) || !reader.Hash(scope.LayoutHash)
                    || !reader.U32(scope.FirstSlot) || !reader.U32(scope.SlotCount)) return false;
            }
            for (var index = 0; index < program.Nodes.Count; index++)
            {
                var node = program.Nodes[index];
                AccessRanges(binding.Accesses, (uint)index, out var firstRead, out var readCount, out var firstWrite, out var writeCount);
                if (!reader.U64(node.NodeTypeId) || !reader.U32(node.NodeTypeVersion)
                    || !reader.U32(node.ConfigOffset) || !reader.U32(node.ConfigSize) || !reader.U32(node.ConfigAlignment)
                    || !reader.U32(node.InstanceMemoryOffset) || !reader.U32(node.InstanceMemorySize)
                    || !reader.U32(node.InstanceMemoryAlignment) || !reader.U8((byte)node.MemoryLifetime)
                    || !reader.U32(node.Children.Offset) || !reader.U32(node.Children.Count)
                    || !reader.U32((uint)node.Flags) || !reader.U32(node.DebugIdentityIndex)
                    || !reader.U32(firstRead) || !reader.U32(readCount)
                    || !reader.U32(firstWrite) || !reader.U32(writeCount)) return false;
            }
            for (var index = 0; index < program.ChildIndices.Count; index++)
                if (!reader.U32(program.ChildIndices[index])) return false;
            if (!reader.U32((uint)binding.Accesses.Count)) return false;
            for (var index = 0; index < binding.Accesses.Count; index++)
            {
                var access = binding.Accesses[index];
                if (!reader.U32(access.NodeIndex) || !reader.U32(access.AccessOrdinal)
                    || !reader.U8(Scope(access.Scope)) || !reader.U32(access.SlotIndex)
                    || !reader.U8((byte)access.Mode) || !reader.U8((byte)access.Reduction)) return false;
            }
            if (!reader.U32((uint)binding.Slots.Count)) return false;
            for (var index = 0; index < binding.Slots.Count; index++)
            {
                var slot = binding.Slots[index];
                var authority = binding.SlotAuthorities[index];
                if (!reader.String(authority.CanonicalKeyId) || !reader.U64(slot.StableKeyId)
                    || !reader.U64(slot.TypeId) || !reader.U32(slot.TypeVersion) || !reader.U64(slot.EnumContractId)
                    || !reader.U8(Scope(slot.Scope)) || !reader.U32(slot.ScopeSlotIndex)
                    || !reader.U32(slot.Offset) || !reader.U32(slot.Size) || !reader.U32(slot.Alignment)
                    || !reader.U32(slot.DefaultOffset) || !reader.U32(slot.DefaultSize)
                    || !reader.U8((byte)slot.AccessFlags) || !reader.U32(uint.MaxValue) || !reader.U32(0)) return false;
            }
            if (!reader.U32((uint)program.Observers.Count)) return false;
            for (var index = 0; index < program.Observers.Count; index++)
            {
                var observer = program.Observers[index];
                if (!reader.U32(observer.ObserverNodeIndex) || !reader.U32(observer.OwningReactiveCompositeIndex)
                    || !reader.U8((byte)observer.Mode) || !reader.U32(observer.WatchedSlots.Offset)
                    || !reader.U32(observer.WatchedSlots.Count)) return false;
            }
            if (!reader.U32((uint)binding.WatchedSlots.Count)) return false;
            for (var index = 0; index < binding.WatchedSlots.Count; index++)
                if (!reader.U8(Scope(binding.WatchedSlots[index].Scope))
                    || !reader.U32(binding.WatchedSlots[index].SlotIndex)) return false;
            if (!reader.Bytes(program.ConfigBlob) || !reader.Bytes(program.DefaultValueBlob)
                || !reader.U32((uint)binding.Scopes.Count)) return false;
            for (var index = 0; index < binding.Scopes.Count; index++)
                if (!reader.Bytes(binding.Scopes[index].GetRawLayoutCopy())) return false;
            if (!reader.U32((uint)program.DebugMap.Count)) return false;
            for (var index = 0; index < program.DebugMap.Count; index++)
            {
                var debug = program.DebugMap[index];
                if (!reader.U32(debug.RuntimeNodeIndex) || !reader.String(debug.AuthoringNodeId.Value)
                    || !reader.String(debug.SourcePath) || !reader.String(debug.DisplayName ?? string.Empty)) return false;
            }
            return reader.AtEnd;
        }

        private static bool ValidateScope(NativeProgramBlackboardBindingV2 binding, int scopeIndex)
        {
            var scope = binding.Scopes[scopeIndex];
            if ((ulong)scope.FirstSlot + scope.SlotCount > (uint)binding.Slots.Count) return false;
            var schema = new Reader(scope.GetSchemaBytesCopy());
            if (!schema.String("aibt.blackboard-scope") || !schema.U32(1) || !schema.U8(Scope(scope.Scope))
                || !schema.String(scope.ContractId) || !schema.U32(scope.ContractVersion)
                || !schema.U32(scope.SlotCount)) return false;
            for (var local = 0u; local < scope.SlotCount; local++)
            {
                var slot = binding.Slots[(int)(scope.FirstSlot + local)];
                var authority = binding.SlotAuthorities[(int)(scope.FirstSlot + local)];
                if (slot.Scope != scope.Scope || slot.ScopeDescriptorIndex != scopeIndex
                    || slot.ScopeSlotIndex != local || slot.StableKeyId != StableHash.Fnv1A64(authority.CanonicalKeyId)
                    || slot.TypeId != StableHash.Fnv1A64(authority.CanonicalTypeId)
                    || slot.EnumContractId != (authority.EnumContract.Length == 0 ? 0 : StableHash.Fnv1A64(authority.EnumContract))
                    || !schema.String(authority.CanonicalKeyId) || !schema.String(authority.CanonicalTypeId)
                    || !schema.U32(slot.TypeVersion) || !schema.String(authority.EnumContract)
                    || !CanonicalDefaultMatches(binding, slot, authority)
                    || !schema.Bytes(authority.GetCanonicalDefaultJsonCopy()) || !schema.U8((byte)slot.Reduction)) return false;
                if (local != 0 && CompareUtf8(
                    binding.SlotAuthorities[(int)(scope.FirstSlot + local - 1)].CanonicalKeyId,
                    authority.CanonicalKeyId) >= 0) return false;
            }
            if (!schema.AtEnd || new CompiledHash(StableHash.Sha256Hex(scope.GetSchemaBytesCopy())) != scope.SchemaHash)
                return false;

            var layout = new Reader(scope.GetRawLayoutCopy());
            if (!layout.String("aibt.blackboard-layout") || !layout.U32(1) || !layout.U8(Scope(scope.Scope))
                || !layout.String(scope.ContractId) || !layout.U32(scope.ContractVersion)
                || !layout.Hash(scope.SchemaHash) || !layout.U32(scope.SlotCount)) return false;
            for (var local = 0u; local < scope.SlotCount; local++)
            {
                var slot = binding.Slots[(int)(scope.FirstSlot + local)];
                var authority = binding.SlotAuthorities[(int)(scope.FirstSlot + local)];
                if (!layout.String(authority.CanonicalKeyId) || !layout.U32(slot.ScopeSlotIndex)
                    || !layout.U64(slot.TypeId) || !layout.U32(slot.TypeVersion) || !layout.U64(slot.EnumContractId)
                    || !layout.U32(slot.Offset) || !layout.U32(slot.Size) || !layout.U32(slot.Alignment)
                    || !layout.Bytes(binding.SemanticProgram.DefaultValueBlob, slot.DefaultOffset, slot.DefaultSize)
                    || !layout.U8((byte)slot.Reduction)) return false;
                for (var other = 0u; other < local; other++)
                {
                    var previous = binding.Slots[(int)(scope.FirstSlot + other)];
                    if ((ulong)slot.Offset < (ulong)previous.Offset + previous.Size
                        && (ulong)previous.Offset < (ulong)slot.Offset + slot.Size) return false;
                }
            }
            return layout.AtEnd
                && new CompiledHash(StableHash.Sha256Hex(scope.GetRawLayoutCopy())) == scope.LayoutHash;
        }

        private static bool ValidateRegistered(NativeProgramBlackboardBindingV2 binding)
        {
            var coveredFields = new bool[binding.RegisteredFields.Count];
            var canonicalTypeIdentities = new HashSet<string>(StringComparer.Ordinal);
            var numericTypeIdentities = new HashSet<string>(StringComparer.Ordinal);
            var canonicalSchemaIdentities = new HashSet<string>(StringComparer.Ordinal);
            var numericSchemaIdentities = new HashSet<ulong>();
            string previousTypeId = null; uint previousVersion = 0; uint expectedFirstField = 0;
            for (var typeIndex = 0; typeIndex < binding.RegisteredTypes.Count; typeIndex++)
            {
                var type = binding.RegisteredTypes[typeIndex];
                if (type.Descriptor.EqualityContractId != CanonicalBytesEqualityContractId
                    || (ulong)type.FirstField + type.FieldCount > (uint)binding.RegisteredFields.Count
                    || type.FirstField != expectedFirstField
                    || !ValidateRegisteredSchema(binding, typeIndex, out var canonicalTypeId, out var canonicalSchemaId)) return false;
                expectedFirstField += type.FieldCount;
                var comparison = previousTypeId == null ? 1 : CompareUtf8(canonicalTypeId, previousTypeId);
                if (previousTypeId != null && (comparison < 0 || comparison == 0 && type.Descriptor.Version <= previousVersion)) return false;
                var canonicalTypeIdentity = canonicalTypeId + "\0" + type.Descriptor.Version.ToString(CultureInfo.InvariantCulture);
                var numericTypeIdentity = type.Descriptor.TypeId.ToString(CultureInfo.InvariantCulture) + "\0" + type.Descriptor.Version.ToString(CultureInfo.InvariantCulture);
                if (!canonicalTypeIdentities.Add(canonicalTypeIdentity) || !numericTypeIdentities.Add(numericTypeIdentity)
                    || !canonicalSchemaIdentities.Add(canonicalSchemaId)
                    || !numericSchemaIdentities.Add(type.Descriptor.CanonicalSchemaId)) return false;
                previousTypeId = canonicalTypeId; previousVersion = type.Descriptor.Version;
                for (var fieldIndex = type.FirstField; fieldIndex < type.FirstField + type.FieldCount; fieldIndex++)
                {
                    if (coveredFields[fieldIndex]) return false;
                    coveredFields[fieldIndex] = true;
                    var field = binding.RegisteredFields[(int)fieldIndex];
                    if ((ulong)field.Offset + field.Size > (uint)type.Descriptor.Size) return false;
                    for (var previous = type.FirstField; previous < fieldIndex; previous++)
                    {
                        var other = binding.RegisteredFields[(int)previous];
                        if ((ulong)field.Offset < (ulong)other.Offset + other.Size
                            && (ulong)other.Offset < (ulong)field.Offset + field.Size) return false;
                    }
                    if (field.Encoding == NativeBlackboardFieldEncodingV2.Registered)
                    {
                        if (field.EqualityContractId != CanonicalBytesEqualityContractId) return false;
                        var found = false;
                        for (var nestedIndex = 0; nestedIndex < binding.RegisteredTypes.Count; nestedIndex++)
                        {
                            var nested = binding.RegisteredTypes[nestedIndex];
                            if (nested.Descriptor.TypeId == field.ValueTypeId
                                && nested.Descriptor.Version == field.ValueTypeVersion
                                && nested.Descriptor.CanonicalSchemaId == field.RegisteredSchemaId
                                && nested.SchemaHash == field.RegisteredSchemaHash
                                && nested.Descriptor.EqualityContractId == field.EqualityContractId
                                && nested.Descriptor.Size == field.Size && nested.Descriptor.Alignment == field.Alignment)
                            { found = true; break; }
                        }
                        if (!found) return false;
                    }
                    else if (!MatchesEncoding(field)) return false;
                }
            }
            if (expectedFirstField != (uint)binding.RegisteredFields.Count) return false;
            for (var index = 0; index < coveredFields.Length; index++) if (!coveredFields[index]) return false;
            return true;
        }

        private static bool ValidateRegisteredSchema(
            NativeProgramBlackboardBindingV2 binding,
            int typeIndex,
            out string canonicalTypeId,
            out string canonicalSchemaId)
        {
            canonicalTypeId = canonicalSchemaId = null;
            var type = binding.RegisteredTypes[typeIndex];
            var bytes = type.GetSchemaPreimageCopy();
            if (bytes.Length == 0 || new CompiledHash(StableHash.Sha256Hex(bytes)) != type.SchemaHash) return false;
            var reader = new Reader(bytes);
            if (!reader.Raw("AIBT-VALUE-SCHEMA-V1\0") || !reader.U32(1)
                || !reader.StringHash(type.Descriptor.TypeId, out canonicalTypeId) || !reader.U64(type.Descriptor.TypeId)
                || !reader.U32(type.Descriptor.Version)
                || !reader.StringHash(type.Descriptor.CanonicalSchemaId, out canonicalSchemaId) || !reader.U64(type.Descriptor.CanonicalSchemaId)
                || !reader.U32((uint)type.Descriptor.Size) || !reader.U8((byte)type.Descriptor.Alignment)
                || !reader.U32(type.FieldCount)) return false;
            string previousFieldId = null;
            uint cursor = 0; uint maximumAlignment = 1;
            for (var local = 0u; local < type.FieldCount; local++)
            {
                var field = binding.RegisteredFields[(int)(type.FirstField + local)];
                if (!reader.StringHash(field.FieldId, out var fieldId) || !reader.U64(field.FieldId)
                    || !reader.StringHash(field.ValueTypeId, out _) || !reader.U64(field.ValueTypeId)
                    || !reader.U32(field.ValueTypeVersion) || !reader.HashOrZero(field.RegisteredSchemaHash)
                    || !reader.U32(field.Offset) || !reader.U32(field.Size)
                    || !reader.U8((byte)field.Alignment) || !reader.U8((byte)field.Encoding)) return false;
                if (previousFieldId != null && CompareUtf8(previousFieldId, fieldId) >= 0
                    || field.Size == 0 || !PowerOfTwo(field.Alignment)) return false;
                var expectedOffset = Align(cursor, field.Alignment);
                if (expectedOffset != field.Offset || (ulong)field.Offset + field.Size > uint.MaxValue) return false;
                cursor = field.Offset + field.Size;
                if (field.Alignment > maximumAlignment) maximumAlignment = field.Alignment;
                previousFieldId = fieldId;
            }
            return reader.AtEnd && maximumAlignment == type.Descriptor.Alignment
                && Align(cursor, maximumAlignment) == type.Descriptor.Size;
        }

        private static uint Align(uint value, uint alignment)
            => checked((uint)(((ulong)value + alignment - 1) & ~((ulong)alignment - 1)));
        private static bool PowerOfTwo(uint value) => value != 0 && (value & (value - 1)) == 0;

        private static bool CanonicalDefaultMatches(
            NativeProgramBlackboardBindingV2 binding,
            NativeBlackboardSlotBindingV2 slot,
            NativeBlackboardSlotAuthorityV2 authority)
        {
            byte[] expected;
            if (slot.RegisteredTypeIndex != CompiledIndex.Invalid)
            {
                if (slot.RegisteredTypeIndex >= binding.RegisteredTypes.Count
                    || !IsCanonicalRegistered(binding, slot.RegisteredTypeIndex,
                        binding.SemanticProgram.DefaultValueBlob, slot.DefaultOffset, slot.DefaultSize, 0)) return false;
                expected = Array.Empty<byte>();
            }
            else
            {
                BlackboardValueType valueType = BlackboardValueType.Invalid;
                for (var raw = (int)BlackboardValueType.Bool; raw <= (int)BlackboardValueType.AssetId; raw++)
                    if (BuiltInBlackboardTypes.TryGet((BlackboardValueType)raw, out var descriptor)
                        && descriptor.TypeId == slot.TypeId) { valueType = (BlackboardValueType)raw; break; }
                if (valueType == BlackboardValueType.Invalid) return false;
                if (!TryCanonicalDefaultJson(valueType, authority.EnumContract,
                    binding.SemanticProgram.DefaultValueBlob, slot.DefaultOffset, slot.DefaultSize,
                    out expected)) return false;
            }
            var actual = authority.GetCanonicalDefaultJsonCopy();
            if (actual.Length != expected.Length) return false;
            for (var index = 0; index < actual.Length; index++) if (actual[index] != expected[index]) return false;
            return true;
        }

        internal static byte[] CanonicalDefaultJson(
            BlackboardValueType type,
            string enumContract,
            IReadOnlyList<byte> bytes,
            uint offset,
            uint size)
        {
            if (!TryCanonicalDefaultJson(type, enumContract, bytes, offset, size, out var result))
                throw new ArgumentException("The compiled default is not the canonical encoding for its exact type.", nameof(bytes));
            return result;
        }

        private static bool TryCanonicalDefaultJson(
            BlackboardValueType type,
            string enumContract,
            IReadOnlyList<byte> bytes,
            uint offset,
            uint size,
            out byte[] result)
        {
            result = null;
            if (bytes == null || !BuiltInBlackboardTypes.TryGet(type, out var descriptor)
                || size != descriptor.Size || (ulong)offset + size > (uint)bytes.Count
                || !IsCanonicalBuiltIn(type, enumContract, bytes, offset, size)) return false;
            string text;
            switch (type)
            {
                case BlackboardValueType.Bool: text = bytes[(int)offset] == 0 ? "false" : "true"; break;
                case BlackboardValueType.Int32: text = unchecked((int)ReadU32(bytes, offset)).ToString(CultureInfo.InvariantCulture); break;
                case BlackboardValueType.Int64: text = unchecked((long)ReadU64(bytes, offset)).ToString(CultureInfo.InvariantCulture); break;
                case BlackboardValueType.Float32: text = Format(ToSingle(ReadU32(bytes, offset))); break;
                case BlackboardValueType.Float64: text = Format(ToDouble(ReadU64(bytes, offset))); break;
                case BlackboardValueType.Float2: text = "{\"x\":" + Format(ToSingle(ReadU32(bytes, offset))) + ",\"y\":" + Format(ToSingle(ReadU32(bytes, offset + 4))) + "}"; break;
                case BlackboardValueType.Float3: text = "{\"x\":" + Format(ToSingle(ReadU32(bytes, offset))) + ",\"y\":" + Format(ToSingle(ReadU32(bytes, offset + 4))) + ",\"z\":" + Format(ToSingle(ReadU32(bytes, offset + 8))) + "}"; break;
                case BlackboardValueType.Quaternion: text = "{\"x\":" + Format(ToSingle(ReadU32(bytes, offset))) + ",\"y\":" + Format(ToSingle(ReadU32(bytes, offset + 4))) + ",\"z\":" + Format(ToSingle(ReadU32(bytes, offset + 8))) + ",\"w\":" + Format(ToSingle(ReadU32(bytes, offset + 12))) + "}"; break;
                case BlackboardValueType.Enum32: text = "{\"contract\":" + JsonConvert.ToString(enumContract) + ",\"value\":" + unchecked((int)ReadU32(bytes, offset + 8)).ToString(CultureInfo.InvariantCulture) + "}"; break;
                case BlackboardValueType.FixedString32:
                case BlackboardValueType.FixedString64:
                case BlackboardValueType.FixedString128:
                case BlackboardValueType.FixedString512:
                    var length = bytes[(int)offset] | bytes[(int)offset + 1] << 8;
                    var raw = new byte[length]; for (var index = 0; index < length; index++) raw[index] = bytes[(int)offset + 2 + index];
                    text = JsonConvert.ToString(Encoding.UTF8.GetString(raw)); break;
                case BlackboardValueType.AgentId:
                case BlackboardValueType.EntityId: text = JsonConvert.ToString(ReadU64(bytes, offset).ToString(CultureInfo.InvariantCulture)); break;
                case BlackboardValueType.OperationId:
                    text = JsonConvert.ToString(ReadU64(bytes, offset).ToString(CultureInfo.InvariantCulture) + ":"
                        + ReadU32(bytes, offset + 8).ToString(CultureInfo.InvariantCulture) + ":"
                        + ReadU32(bytes, offset + 12).ToString(CultureInfo.InvariantCulture) + ":"
                        + ReadU64(bytes, offset + 16).ToString(CultureInfo.InvariantCulture)); break;
                case BlackboardValueType.AssetId:
                    var asset = new AssetId(ReadU64(bytes, offset), ReadU64(bytes, offset + 8),
                        unchecked((long)ReadU64(bytes, offset + 16)), bytes[(int)offset + 24] != 0);
                    text = asset.HasLocalFileId ? "{\"guid\":" + JsonConvert.ToString(asset.ToGuidString())
                        + ",\"localFileId\":" + asset.LocalFileId.ToString(CultureInfo.InvariantCulture) + "}"
                        : JsonConvert.ToString(asset.ToGuidString()); break;
                default: return false;
            }
            result = Encoding.UTF8.GetBytes(text);
            return true;
        }

        private static uint ReadU32(IReadOnlyList<byte> bytes, uint offset)
            => (uint)(bytes[(int)offset] | bytes[(int)offset + 1] << 8 | bytes[(int)offset + 2] << 16 | bytes[(int)offset + 3] << 24);
        private static ulong ReadU64(IReadOnlyList<byte> bytes, uint offset)
            => ReadU32(bytes, offset) | (ulong)ReadU32(bytes, offset + 4) << 32;
        private static float ToSingle(uint bits) => BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        private static double ToDouble(ulong bits) => BitConverter.ToDouble(BitConverter.GetBytes(bits), 0);
        private static string Format(float value)
            => value == 0f ? "0" : NormalizeExponent(value.ToString("R", CultureInfo.InvariantCulture));
        private static string Format(double value)
            => value == 0d ? "0" : NormalizeExponent(value.ToString("R", CultureInfo.InvariantCulture));
        private static string NormalizeExponent(string value)
        {
            var exponentIndex = value.IndexOf('E');
            if (exponentIndex < 0) return value;
            var exponentStart = exponentIndex + 1;
            var negative = exponentStart < value.Length && value[exponentStart] == '-';
            if (negative || exponentStart < value.Length && value[exponentStart] == '+') exponentStart++;
            while (exponentStart < value.Length - 1 && value[exponentStart] == '0') exponentStart++;
            return value.Substring(0, exponentIndex) + "e" + (negative ? "-" : string.Empty) + value.Substring(exponentStart);
        }

        private static bool IsCanonicalRegistered(
            NativeProgramBlackboardBindingV2 binding,
            uint typeIndex,
            IReadOnlyList<byte> bytes,
            uint offset,
            uint size,
            int depth)
        {
            if (depth > binding.RegisteredTypes.Count || typeIndex >= binding.RegisteredTypes.Count) return false;
            var type = binding.RegisteredTypes[(int)typeIndex];
            if (size != type.Descriptor.Size || (ulong)offset + size > (uint)bytes.Count) return false;
            var covered = new bool[size];
            for (var fieldIndex = type.FirstField; fieldIndex < type.FirstField + type.FieldCount; fieldIndex++)
            {
                var field = binding.RegisteredFields[(int)fieldIndex];
                if ((ulong)field.Offset + field.Size > size) return false;
                for (var index = 0u; index < field.Size; index++)
                {
                    if (covered[field.Offset + index]) return false;
                    covered[field.Offset + index] = true;
                }
                var fieldOffset = offset + field.Offset;
                if (field.Encoding == NativeBlackboardFieldEncodingV2.Registered)
                {
                    var nestedIndex = CompiledIndex.Invalid;
                    for (var candidate = 0; candidate < binding.RegisteredTypes.Count; candidate++)
                    {
                        var nested = binding.RegisteredTypes[candidate];
                        if (nested.Descriptor.TypeId == field.ValueTypeId
                            && nested.Descriptor.Version == field.ValueTypeVersion
                            && nested.Descriptor.CanonicalSchemaId == field.RegisteredSchemaId
                            && nested.SchemaHash == field.RegisteredSchemaHash)
                        { if (nestedIndex != CompiledIndex.Invalid) return false; nestedIndex = (uint)candidate; }
                    }
                    if (nestedIndex == CompiledIndex.Invalid
                        || !IsCanonicalRegistered(binding, nestedIndex, bytes, fieldOffset, field.Size, depth + 1)) return false;
                }
                else if (!IsCanonicalField(field, bytes, fieldOffset)) return false;
            }
            for (var index = 0u; index < size; index++)
                if (!covered[index] && bytes[(int)(offset + index)] != 0) return false;
            return true;
        }

        private static bool IsCanonicalField(
            NativeRegisteredBlackboardFieldBindingV2 field,
            IReadOnlyList<byte> bytes,
            uint offset)
        {
            if (field.Encoding == NativeBlackboardFieldEncodingV2.Bool8)
                return bytes[(int)offset] <= 1;
            if (field.Encoding == NativeBlackboardFieldEncodingV2.Float32BitsLE)
                return Canonical32(ReadU32(bytes, offset));
            if (field.Encoding == NativeBlackboardFieldEncodingV2.Float64BitsLE)
                return Canonical64(ReadU64(bytes, offset));
            if (field.Encoding == NativeBlackboardFieldEncodingV2.FixedBytes)
            {
                for (var raw = (int)BlackboardValueType.Bool; raw <= (int)BlackboardValueType.AssetId; raw++)
                    if (BuiltInBlackboardTypes.TryGet((BlackboardValueType)raw, out var descriptor)
                        && descriptor.TypeId == field.ValueTypeId)
                        return IsCanonicalBuiltIn(descriptor.ValueType, string.Empty, bytes, offset, field.Size);
            }
            return true;
        }

        private static bool IsCanonicalBuiltIn(
            BlackboardValueType type,
            string enumContract,
            IReadOnlyList<byte> bytes,
            uint offset,
            uint size)
        {
            switch (type)
            {
                case BlackboardValueType.Bool: return bytes[(int)offset] <= 1;
                case BlackboardValueType.Float32: return Canonical32(ReadU32(bytes, offset));
                case BlackboardValueType.Float64: return Canonical64(ReadU64(bytes, offset));
                case BlackboardValueType.Float2: return Canonical32(ReadU32(bytes, offset)) && Canonical32(ReadU32(bytes, offset + 4));
                case BlackboardValueType.Float3: return Canonical32(ReadU32(bytes, offset)) && Canonical32(ReadU32(bytes, offset + 4)) && Canonical32(ReadU32(bytes, offset + 8));
                case BlackboardValueType.Quaternion: return Canonical32(ReadU32(bytes, offset)) && Canonical32(ReadU32(bytes, offset + 4)) && Canonical32(ReadU32(bytes, offset + 8)) && Canonical32(ReadU32(bytes, offset + 12));
                case BlackboardValueType.Enum32:
                    return !string.IsNullOrEmpty(enumContract)
                        && ReadU64(bytes, offset) == StableHash.Fnv1A64(enumContract)
                        && Zero(bytes, offset + 12, 4);
                case BlackboardValueType.AgentId:
                case BlackboardValueType.EntityId: return ReadU64(bytes, offset) != 0;
                case BlackboardValueType.OperationId:
                    return ReadU64(bytes, offset) != 0 && ReadU32(bytes, offset + 8) != uint.MaxValue;
                case BlackboardValueType.AssetId:
                    var hasLocal = bytes[(int)offset + 24];
                    return hasLocal <= 1 && (hasLocal != 0 || ReadU64(bytes, offset + 16) == 0)
                        && Zero(bytes, offset + 25, 7)
                        && (ReadU64(bytes, offset) != 0 || ReadU64(bytes, offset + 8) != 0);
                case BlackboardValueType.FixedString32:
                case BlackboardValueType.FixedString64:
                case BlackboardValueType.FixedString128:
                case BlackboardValueType.FixedString512:
                    var length = bytes[(int)offset] | bytes[(int)offset + 1] << 8;
                    return length <= size - 2 && Zero(bytes, offset + 2 + (uint)length, (int)size - 2 - length)
                        && ValidUtf8(bytes, offset + 2, length);
                default: return true;
            }
        }

        private static bool Finite32(uint bits) => (bits & 0x7f800000u) != 0x7f800000u;
        private static bool Finite64(ulong bits) => (bits & 0x7ff0000000000000UL) != 0x7ff0000000000000UL;
        private static bool Canonical32(uint bits) => Finite32(bits) && bits != 0x80000000u;
        private static bool Canonical64(ulong bits) => Finite64(bits) && bits != 0x8000000000000000UL;
        private static bool Zero(IReadOnlyList<byte> bytes, uint offset, int count)
        { for (var index = 0; index < count; index++) if (bytes[(int)offset + index] != 0) return false; return true; }
        private static bool ValidUtf8(IReadOnlyList<byte> bytes, uint offset, int count)
        {
            try
            {
                var raw = new byte[count];
                for (var index = 0; index < count; index++) raw[index] = bytes[(int)offset + index];
                new UTF8Encoding(false, true).GetString(raw);
                return true;
            }
            catch (DecoderFallbackException) { return false; }
        }

        private static bool MatchesEncoding(NativeRegisteredBlackboardFieldBindingV2 field)
        {
            for (var raw = (int)BlackboardValueType.Bool; raw <= (int)BlackboardValueType.AssetId; raw++)
            {
                if (!BuiltInBlackboardTypes.TryGet((BlackboardValueType)raw, out var type)
                    || type.TypeId != field.ValueTypeId) continue;
                var expected = FieldEncoding(type.ValueType);
                return field.ValueTypeVersion == type.Version && field.Size == type.Size
                    && field.Alignment == type.Alignment && field.Encoding == expected
                    && field.RegisteredSchemaId == 0 && !field.RegisteredSchemaHash.IsValid
                    && field.EqualityContractId == 0;
            }
            return Simple("Int8", 1, 1, NativeBlackboardFieldEncodingV2.Int8, field)
                || Simple("UInt8", 1, 1, NativeBlackboardFieldEncodingV2.UInt8, field)
                || Simple("Int16", 2, 2, NativeBlackboardFieldEncodingV2.Int16LE, field)
                || Simple("UInt16", 2, 2, NativeBlackboardFieldEncodingV2.UInt16LE, field)
                || Simple("UInt32", 4, 4, NativeBlackboardFieldEncodingV2.UInt32LE, field)
                || Simple("UInt64", 8, 8, NativeBlackboardFieldEncodingV2.UInt64LE, field);
        }

        private static bool Simple(
            string canonicalTypeId,
            uint size,
            uint alignment,
            NativeBlackboardFieldEncodingV2 encoding,
            NativeRegisteredBlackboardFieldBindingV2 field)
            => field.ValueTypeId == StableHash.Fnv1A64(canonicalTypeId)
                && field.ValueTypeVersion == 1 && field.Size == size && field.Alignment == alignment
                && field.Encoding == encoding && field.RegisteredSchemaId == 0
                && !field.RegisteredSchemaHash.IsValid && field.EqualityContractId == 0;

        private static NativeBlackboardFieldEncodingV2 FieldEncoding(BlackboardValueType type)
        {
            switch (type)
            {
                case BlackboardValueType.Bool: return NativeBlackboardFieldEncodingV2.Bool8;
                case BlackboardValueType.Int32: return NativeBlackboardFieldEncodingV2.Int32LE;
                case BlackboardValueType.Int64: return NativeBlackboardFieldEncodingV2.Int64LE;
                case BlackboardValueType.Float32: return NativeBlackboardFieldEncodingV2.Float32BitsLE;
                case BlackboardValueType.Float64: return NativeBlackboardFieldEncodingV2.Float64BitsLE;
                default: return NativeBlackboardFieldEncodingV2.FixedBytes;
            }
        }

        private static void AccessRanges(
            IReadOnlyList<NativeBlackboardAccessBindingV2> accesses,
            uint nodeIndex,
            out uint firstRead,
            out uint readCount,
            out uint firstWrite,
            out uint writeCount)
        {
            firstRead = readCount = firstWrite = writeCount = 0;
            var seenRead = false; var seenWrite = false;
            for (var index = 0; index < accesses.Count; index++)
            {
                var access = accesses[index];
                if (access.NodeIndex != nodeIndex) continue;
                if (access.Mode != NativeBlackboardAccessModeV2.Write)
                { if (!seenRead) { firstRead = (uint)index; seenRead = true; } readCount++; }
                if (access.Mode != NativeBlackboardAccessModeV2.Read)
                { if (!seenWrite) { firstWrite = (uint)index; seenWrite = true; } writeCount++; }
            }
        }

        private static byte Scope(BlackboardScope scope)
            => scope == BlackboardScope.Tree ? (byte)0 : scope == BlackboardScope.Agent ? (byte)1 : (byte)2;

        private static int CompareUtf8(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
            var rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
            var count = Math.Min(leftBytes.Length, rightBytes.Length);
            for (var index = 0; index < count; index++)
                if (leftBytes[index] != rightBytes[index]) return leftBytes[index] < rightBytes[index] ? -1 : 1;
            return leftBytes.Length.CompareTo(rightBytes.Length);
        }

        private sealed class Reader
        {
            private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
            private readonly byte[] _bytes;
            private int _offset;
            internal Reader(byte[] bytes) { _bytes = bytes ?? Array.Empty<byte>(); }
            internal bool AtEnd => _offset == _bytes.Length;
            internal bool U8(byte value) => Take(1, out var offset) && _bytes[offset] == value;
            internal bool U16(ushort value) => Take(2, out var offset) && ReadU16(offset) == value;
            internal bool U32(uint value) => Take(4, out var offset) && ReadU32(offset) == value;
            internal bool U64(ulong value) => Take(8, out var offset) && ReadU64(offset) == value;
            internal bool Raw(string value)
            {
                if (value == null) return false;
                var expected = Utf8.GetBytes(value);
                if (!Take(expected.Length, out var offset)) return false;
                for (var index = 0; index < expected.Length; index++)
                    if (_bytes[offset + index] != expected[index]) return false;
                return true;
            }
            internal bool StringHash(ulong expectedHash, out string value)
            {
                value = null;
                if (!Take(4, out var lengthOffset)) return false;
                var length = ReadU32(lengthOffset);
                if (length > int.MaxValue || !Take((int)length, out var offset)) return false;
                try
                {
                    value = Utf8.GetString(_bytes, offset, (int)length);
                    return value.Length != 0 && StableHash.Fnv1A64(value) == expectedHash;
                }
                catch (DecoderFallbackException) { return false; }
            }
            internal bool Hash(CompiledHash value)
            {
                if (!value.IsValid || !Take(32, out var offset)) return false;
                var hex = value.HexadecimalValue;
                for (var index = 0; index < 32; index++)
                    if (_bytes[offset + index] != (byte)((Nibble(hex[index * 2]) << 4) | Nibble(hex[index * 2 + 1]))) return false;
                return true;
            }
            internal bool HashOrZero(CompiledHash value)
            {
                if (value.IsValid) return Hash(value);
                if (!Take(32, out var offset)) return false;
                for (var index = 0; index < 32; index++) if (_bytes[offset + index] != 0) return false;
                return true;
            }
            internal bool String(string value)
            {
                if (value == null) return false;
                var expected = Utf8.GetBytes(value);
                return Bytes(expected);
            }
            internal bool Bytes(IReadOnlyList<byte> expected)
            {
                if (!U32((uint)expected.Count) || !Take(expected.Count, out var offset)) return false;
                for (var index = 0; index < expected.Count; index++)
                    if (_bytes[offset + index] != expected[index]) return false;
                return true;
            }
            internal bool Bytes(IReadOnlyList<byte> expected, uint sourceOffset, uint count)
            {
                if ((ulong)sourceOffset + count > (uint)expected.Count || !U32(count)
                    || !Take((int)count, out var offset)) return false;
                for (var index = 0u; index < count; index++)
                    if (_bytes[offset + index] != expected[(int)(sourceOffset + index)]) return false;
                return true;
            }
            private bool Take(int count, out int offset)
            {
                offset = _offset;
                if (count < 0 || _offset > _bytes.Length - count) return false;
                _offset += count;
                return true;
            }
            private ushort ReadU16(int offset) => (ushort)(_bytes[offset] | _bytes[offset + 1] << 8);
            private uint ReadU32(int offset) => (uint)(_bytes[offset] | _bytes[offset + 1] << 8 | _bytes[offset + 2] << 16 | _bytes[offset + 3] << 24);
            private ulong ReadU64(int offset) => ReadU32(offset) | (ulong)ReadU32(offset + 4) << 32;
            private static int Nibble(char value) => value <= '9' ? value - '0' : value - 'a' + 10;
        }
    }
}
