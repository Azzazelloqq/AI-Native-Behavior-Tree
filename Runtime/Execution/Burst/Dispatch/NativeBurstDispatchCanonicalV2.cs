using Unity.Collections;

namespace AIBT.Execution.Burst.Dispatch
{
    internal enum NativeBurstDispatchCanonicalStoragePolicyV2 : byte
    {
        Strict = 0,
        AllowZeroOpaqueSentinel = 1
    }

    internal static class NativeBurstDispatchCanonicalV2
    {
        internal static bool ValidateRuleLayout(
            NativeArray<NativeBurstDispatchCanonicalRuleV2>.ReadOnly rules,
            in NativeBurstDispatchCanonicalRangeV2 range,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly fields,
            uint firstField,
            uint fieldCount,
            uint storageSize)
        {
            if (!Range(range.FirstRule, range.RuleCount, rules.Length)
                || !Range(firstField, fieldCount, fields.Length))
            {
                return false;
            }

            ulong priorEnd = 0;
            uint annotationCursor = 0;
            for (uint fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                var field = fields[(int)(firstField + fieldIndex)];
                if ((byte)field.CanonicalRuleKind
                    > (byte)NativeBurstDispatchCanonicalRuleKindV2.FixedString512)
                {
                    return false;
                }

                if (field.CanonicalRuleKind == NativeBurstDispatchCanonicalRuleKindV2.None)
                {
                    continue;
                }

                if (annotationCursor >= range.RuleCount
                    || field.ElementCount != 1)
                {
                    return false;
                }

                var projected = rules[(int)(range.FirstRule + annotationCursor)];
                if (projected.Kind != field.CanonicalRuleKind
                    || projected.ByteOffset != field.ByteOffset)
                {
                    return false;
                }

                annotationCursor++;
            }

            if (annotationCursor != range.RuleCount)
            {
                return false;
            }

            for (uint ruleIndex = 0; ruleIndex < range.RuleCount; ruleIndex++)
            {
                var rule = rules[(int)(range.FirstRule + ruleIndex)];
                var size = RuleSize(rule.Kind);
                var end = (ulong)rule.ByteOffset + size;
                if (size == 0
                    || rule.ByteOffset < priorEnd
                    || end > storageSize
                    || !MatchesTransport(
                        in rule,
                        fields,
                        firstField,
                        fieldCount))
                {
                    return false;
                }

                priorEnd = end;
            }

            return true;
        }

        internal static bool ValidateTopLevelRule(
            ulong typeNumericId,
            uint typeVersion,
            uint valueSize,
            NativeArray<NativeBurstDispatchCanonicalRuleV2>.ReadOnly rules,
            in NativeBurstDispatchCanonicalRangeV2 range)
        {
            if (!TryBuiltInRule(typeNumericId, out var expectedKind, out var expectedSize))
            {
                return !TryPlainBuiltInSize(typeNumericId, out expectedSize)
                    || typeVersion == 1
                        && valueSize == expectedSize
                        && range.RuleCount == 0;
            }

            return typeVersion == 1
                && valueSize == expectedSize
                && range.RuleCount == 1
                && range.FirstRule < rules.Length
                && rules[(int)range.FirstRule].Kind == expectedKind
                && rules[(int)range.FirstRule].ByteOffset == 0;
        }

        internal static bool ValidateBytes(
            NativeArray<NativeBurstDispatchCanonicalRuleV2>.ReadOnly rules,
            in NativeBurstDispatchCanonicalRangeV2 range,
            NativeArray<byte>.ReadOnly bytes,
            uint baseOffset,
            uint storageSize)
            => ValidateBytes(
                rules,
                in range,
                bytes,
                baseOffset,
                storageSize,
                NativeBurstDispatchCanonicalStoragePolicyV2.Strict);

        internal static bool ValidateBytes(
            NativeArray<NativeBurstDispatchCanonicalRuleV2>.ReadOnly rules,
            in NativeBurstDispatchCanonicalRangeV2 range,
            NativeArray<byte>.ReadOnly bytes,
            uint baseOffset,
            uint storageSize,
            NativeBurstDispatchCanonicalStoragePolicyV2 policy)
        {
            if (!Range(range.FirstRule, range.RuleCount, rules.Length)
                || !Range(baseOffset, storageSize, bytes.Length)
                || (byte)policy > (byte)NativeBurstDispatchCanonicalStoragePolicyV2.AllowZeroOpaqueSentinel)
            {
                return false;
            }

            for (uint ruleIndex = 0; ruleIndex < range.RuleCount; ruleIndex++)
            {
                var rule = rules[(int)(range.FirstRule + ruleIndex)];
                var size = RuleSize(rule.Kind);
                var absoluteOffset = baseOffset + rule.ByteOffset;
                if (size == 0
                    || (ulong)rule.ByteOffset + size > storageSize
                    || !(policy == NativeBurstDispatchCanonicalStoragePolicyV2.AllowZeroOpaqueSentinel
                            && Zero(bytes, absoluteOffset, size)
                        || ValidateRuleBytes(in rule, bytes, absoluteOffset, size)))
                {
                    return false;
                }
            }

            return true;
        }

        internal static uint RuleSize(NativeBurstDispatchCanonicalRuleKindV2 kind)
        {
            switch (kind)
            {
                case NativeBurstDispatchCanonicalRuleKindV2.AgentId:
                case NativeBurstDispatchCanonicalRuleKindV2.EntityId:
                    return 8;
                case NativeBurstDispatchCanonicalRuleKindV2.OperationId:
                    return 24;
                case NativeBurstDispatchCanonicalRuleKindV2.AssetId:
                case NativeBurstDispatchCanonicalRuleKindV2.FixedString32:
                    return 32;
                case NativeBurstDispatchCanonicalRuleKindV2.FixedString64:
                    return 64;
                case NativeBurstDispatchCanonicalRuleKindV2.FixedString128:
                    return 128;
                case NativeBurstDispatchCanonicalRuleKindV2.FixedString512:
                    return 512;
                default:
                    return 0;
            }
        }

        private static bool MatchesTransport(
            in NativeBurstDispatchCanonicalRuleV2 rule,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly fields,
            uint firstField,
            uint fieldCount)
        {
            switch (rule.Kind)
            {
                case NativeBurstDispatchCanonicalRuleKindV2.AgentId:
                case NativeBurstDispatchCanonicalRuleKindV2.EntityId:
                    return HasLeaf(fields, firstField, fieldCount, rule.ByteOffset, NativeBurstDispatchFieldEncodingV2.UInt64);
                case NativeBurstDispatchCanonicalRuleKindV2.OperationId:
                    return HasLeaf(fields, firstField, fieldCount, rule.ByteOffset, NativeBurstDispatchFieldEncodingV2.UInt64)
                        && HasLeaf(fields, firstField, fieldCount, rule.ByteOffset + 8, NativeBurstDispatchFieldEncodingV2.UInt32)
                        && HasLeaf(fields, firstField, fieldCount, rule.ByteOffset + 12, NativeBurstDispatchFieldEncodingV2.UInt32)
                        && HasLeaf(fields, firstField, fieldCount, rule.ByteOffset + 16, NativeBurstDispatchFieldEncodingV2.UInt64);
                case NativeBurstDispatchCanonicalRuleKindV2.AssetId:
                    return HasLeaf(fields, firstField, fieldCount, rule.ByteOffset, NativeBurstDispatchFieldEncodingV2.UInt64)
                        && HasLeaf(fields, firstField, fieldCount, rule.ByteOffset + 8, NativeBurstDispatchFieldEncodingV2.UInt64)
                        && HasLeaf(fields, firstField, fieldCount, rule.ByteOffset + 16, NativeBurstDispatchFieldEncodingV2.Int64)
                        && HasLeaf(fields, firstField, fieldCount, rule.ByteOffset + 24, NativeBurstDispatchFieldEncodingV2.Boolean);
                case NativeBurstDispatchCanonicalRuleKindV2.FixedString32:
                case NativeBurstDispatchCanonicalRuleKindV2.FixedString64:
                case NativeBurstDispatchCanonicalRuleKindV2.FixedString128:
                case NativeBurstDispatchCanonicalRuleKindV2.FixedString512:
                {
                    var size = RuleSize(rule.Kind);
                    if (!HasLeaf(fields, firstField, fieldCount, rule.ByteOffset, NativeBurstDispatchFieldEncodingV2.UInt16))
                    {
                        return false;
                    }

                    for (uint byteIndex = 2; byteIndex < size; byteIndex++)
                    {
                        if (!HasLeaf(
                                fields,
                                firstField,
                                fieldCount,
                                rule.ByteOffset + byteIndex,
                                NativeBurstDispatchFieldEncodingV2.UInt8))
                        {
                            return false;
                        }
                    }

                    return true;
                }
                default:
                    return false;
            }
        }

        private static bool HasLeaf(
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly fields,
            uint firstField,
            uint fieldCount,
            uint byteOffset,
            NativeBurstDispatchFieldEncodingV2 encoding)
        {
            for (uint index = 0; index < fieldCount; index++)
            {
                var field = fields[(int)(firstField + index)];
                if (field.Encoding != encoding
                    || byteOffset < field.ByteOffset
                    || byteOffset - field.ByteOffset >= field.ElementCount * field.ElementSize
                    || (byteOffset - field.ByteOffset) % field.ElementSize != 0)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool ValidateRuleBytes(
            in NativeBurstDispatchCanonicalRuleV2 rule,
            NativeArray<byte>.ReadOnly bytes,
            uint offset,
            uint size)
        {
            switch (rule.Kind)
            {
                case NativeBurstDispatchCanonicalRuleKindV2.AgentId:
                case NativeBurstDispatchCanonicalRuleKindV2.EntityId:
                    return ReadUInt64(bytes, offset) != 0;
                case NativeBurstDispatchCanonicalRuleKindV2.OperationId:
                    return ReadUInt64(bytes, offset) != 0
                        && ReadUInt32(bytes, offset + 8) != uint.MaxValue;
                case NativeBurstDispatchCanonicalRuleKindV2.AssetId:
                {
                    var hasLocal = bytes[(int)(offset + 24)];
                    return hasLocal <= 1
                        && (hasLocal != 0 || ReadUInt64(bytes, offset + 16) == 0)
                        && (ReadUInt64(bytes, offset) != 0 || ReadUInt64(bytes, offset + 8) != 0)
                        && Zero(bytes, offset + 25, 7);
                }
                case NativeBurstDispatchCanonicalRuleKindV2.FixedString32:
                case NativeBurstDispatchCanonicalRuleKindV2.FixedString64:
                case NativeBurstDispatchCanonicalRuleKindV2.FixedString128:
                case NativeBurstDispatchCanonicalRuleKindV2.FixedString512:
                {
                    var length = ReadUInt16(bytes, offset);
                    return length <= size - 2
                        && ValidUtf8(bytes, offset + 2, length)
                        && Zero(bytes, offset + 2 + length, size - 2 - length);
                }
                default:
                    return false;
            }
        }

        private static bool TryBuiltInRule(
            ulong typeNumericId,
            out NativeBurstDispatchCanonicalRuleKindV2 kind,
            out uint size)
        {
            if (typeNumericId == AIBT.NativeBuiltInBlackboardTypeIdsV1.AgentId)
            {
                kind = NativeBurstDispatchCanonicalRuleKindV2.AgentId;
                size = 8;
                return true;
            }
            if (typeNumericId == AIBT.NativeBuiltInBlackboardTypeIdsV1.EntityId)
            {
                kind = NativeBurstDispatchCanonicalRuleKindV2.EntityId;
                size = 8;
                return true;
            }
            if (typeNumericId == AIBT.NativeBuiltInBlackboardTypeIdsV1.OperationId)
            {
                kind = NativeBurstDispatchCanonicalRuleKindV2.OperationId;
                size = 24;
                return true;
            }
            if (typeNumericId == AIBT.NativeBuiltInBlackboardTypeIdsV1.AssetId)
            {
                kind = NativeBurstDispatchCanonicalRuleKindV2.AssetId;
                size = 32;
                return true;
            }
            if (typeNumericId == AIBT.NativeBuiltInBlackboardTypeIdsV1.FixedString32)
            {
                kind = NativeBurstDispatchCanonicalRuleKindV2.FixedString32;
                size = 32;
                return true;
            }
            if (typeNumericId == AIBT.NativeBuiltInBlackboardTypeIdsV1.FixedString64)
            {
                kind = NativeBurstDispatchCanonicalRuleKindV2.FixedString64;
                size = 64;
                return true;
            }
            if (typeNumericId == AIBT.NativeBuiltInBlackboardTypeIdsV1.FixedString128)
            {
                kind = NativeBurstDispatchCanonicalRuleKindV2.FixedString128;
                size = 128;
                return true;
            }
            if (typeNumericId == AIBT.NativeBuiltInBlackboardTypeIdsV1.FixedString512)
            {
                kind = NativeBurstDispatchCanonicalRuleKindV2.FixedString512;
                size = 512;
                return true;
            }

            kind = default;
            size = 0;
            return false;
        }

        private static bool TryPlainBuiltInSize(ulong typeNumericId, out uint size)
        {
            switch (typeNumericId)
            {
                case AIBT.NativeBuiltInBlackboardTypeIdsV1.Bool:
                case 2320619146196309522UL: // Int8
                case 14568130492415350395UL: // UInt8
                    size = 1;
                    return true;
                case 13377999823495255249UL: // Int16
                case 9036528123728359218UL: // UInt16
                    size = 2;
                    return true;
                case AIBT.NativeBuiltInBlackboardTypeIdsV1.Int32:
                case 9038502846612247724UL: // UInt32
                case AIBT.NativeBuiltInBlackboardTypeIdsV1.Float32:
                    size = 4;
                    return true;
                case AIBT.NativeBuiltInBlackboardTypeIdsV1.Int64:
                case 9043283523170763027UL: // UInt64
                case AIBT.NativeBuiltInBlackboardTypeIdsV1.Float64:
                case AIBT.NativeBuiltInBlackboardTypeIdsV1.Float2:
                    size = 8;
                    return true;
                case AIBT.NativeBuiltInBlackboardTypeIdsV1.Float3:
                    size = 12;
                    return true;
                case AIBT.NativeBuiltInBlackboardTypeIdsV1.Quaternion:
                case AIBT.NativeBuiltInBlackboardTypeIdsV1.Enum32:
                    size = 16;
                    return true;
                default:
                    size = 0;
                    return false;
            }
        }

        private static ushort ReadUInt16(NativeArray<byte>.ReadOnly bytes, uint offset)
            => (ushort)(bytes[(int)offset] | bytes[(int)(offset + 1)] << 8);

        private static uint ReadUInt32(NativeArray<byte>.ReadOnly bytes, uint offset)
            => bytes[(int)offset]
                | (uint)bytes[(int)(offset + 1)] << 8
                | (uint)bytes[(int)(offset + 2)] << 16
                | (uint)bytes[(int)(offset + 3)] << 24;

        private static ulong ReadUInt64(NativeArray<byte>.ReadOnly bytes, uint offset)
            => ReadUInt32(bytes, offset) | (ulong)ReadUInt32(bytes, offset + 4) << 32;

        private static bool Zero(
            NativeArray<byte>.ReadOnly bytes,
            uint offset,
            uint count)
        {
            for (uint index = 0; index < count; index++)
            {
                if (bytes[(int)(offset + index)] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidUtf8(
            NativeArray<byte>.ReadOnly bytes,
            uint offset,
            uint count)
        {
            var end = (ulong)offset + count;
            for (var index = (ulong)offset; index < end; index++)
            {
                var value = bytes[(int)index];
                if (value < 0x80)
                {
                    continue;
                }

                int extra;
                uint code;
                if (value >= 0xc2 && value <= 0xdf)
                {
                    extra = 1;
                    code = (uint)(value & 0x1f);
                }
                else if (value >= 0xe0 && value <= 0xef)
                {
                    extra = 2;
                    code = (uint)(value & 0x0f);
                }
                else if (value >= 0xf0 && value <= 0xf4)
                {
                    extra = 3;
                    code = (uint)(value & 0x07);
                }
                else
                {
                    return false;
                }

                if (index + (ulong)extra >= end)
                {
                    return false;
                }

                for (var item = 0; item < extra; item++)
                {
                    var continuation = bytes[(int)++index];
                    if ((continuation & 0xc0) != 0x80)
                    {
                        return false;
                    }

                    code = (code << 6) | (uint)(continuation & 0x3f);
                }

                if (code > 0x10ffff
                    || code >= 0xd800 && code <= 0xdfff
                    || extra == 2 && code < 0x800
                    || extra == 3 && code < 0x10000)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Range(uint offset, uint count, int length)
            => (ulong)offset + count <= (ulong)length;
    }
}
