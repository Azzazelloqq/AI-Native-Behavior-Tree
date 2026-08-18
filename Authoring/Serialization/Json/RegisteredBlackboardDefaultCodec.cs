using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    internal static class RegisteredBlackboardDefaultCodec
    {
        internal static byte[] Encode(
            RegisteredBlackboardTypeCatalogEntry entry,
            SemanticObject value,
            RegisteredBlackboardTypeCatalog catalog)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (value.Properties.Count != entry.Fields.Count)
                throw new ArgumentException("Registered default must contain exactly every canonical schema member.", nameof(value));
            var bytes = new byte[entry.Descriptor.Size];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < entry.Fields.Count; index++)
            {
                var field = entry.Fields[index];
                if (!value.TryGetValue(field.FieldId, out var member) || !seen.Add(field.FieldId))
                    throw new ArgumentException("Registered default member set differs from its canonical schema.", nameof(value));
                EncodeField(bytes, checked((int)field.Offset), field, member, catalog);
            }
            for (var index = 0; index < value.Properties.Count; index++)
                if (!seen.Contains(value.Properties[index].Name))
                    throw new ArgumentException("Registered default contains an unknown schema member.", nameof(value));
            return bytes;
        }

        private static void EncodeField(byte[] destination, int offset, GeneratedStorageField field, SemanticValue value, RegisteredBlackboardTypeCatalog catalog)
        {
            switch (field.Encoding)
            {
                case GeneratedFieldEncoding.Bool8:
                    if (!value.TryGetBoolean(out var boolean)) throw Type(field); destination[offset] = boolean ? (byte)1 : (byte)0; return;
                case GeneratedFieldEncoding.Int8: WriteSigned(destination, offset, Signed(value, sbyte.MinValue, sbyte.MaxValue, field), 1); return;
                case GeneratedFieldEncoding.UInt8: WriteUnsigned(destination, offset, Unsigned(value, byte.MaxValue, field), 1); return;
                case GeneratedFieldEncoding.Int16LE: WriteSigned(destination, offset, Signed(value, short.MinValue, short.MaxValue, field), 2); return;
                case GeneratedFieldEncoding.UInt16LE: WriteUnsigned(destination, offset, Unsigned(value, ushort.MaxValue, field), 2); return;
                case GeneratedFieldEncoding.Int32LE: WriteSigned(destination, offset, Signed(value, int.MinValue, int.MaxValue, field), 4); return;
                case GeneratedFieldEncoding.UInt32LE: WriteUnsigned(destination, offset, Unsigned(value, uint.MaxValue, field), 4); return;
                case GeneratedFieldEncoding.Int64LE: WriteSigned(destination, offset, Signed(value, long.MinValue, long.MaxValue, field), 8); return;
                case GeneratedFieldEncoding.UInt64LE: WriteUnsigned(destination, offset, Unsigned(value, ulong.MaxValue, field), 8); return;
                case GeneratedFieldEncoding.Float32BitsLE:
                    var single = checked((float)Number(value, field)); if (float.IsNaN(single) || float.IsInfinity(single)) throw Type(field);
                    WriteUnsigned(destination, offset, unchecked((uint)BitConverter.SingleToInt32Bits(single)), 4); return;
                case GeneratedFieldEncoding.Float64BitsLE:
                    var number = Number(value, field); if (double.IsNaN(number) || double.IsInfinity(number)) throw Type(field);
                    WriteUnsigned(destination, offset, unchecked((ulong)BitConverter.DoubleToInt64Bits(number)), 8); return;
                case GeneratedFieldEncoding.Registered:
                    if (!value.TryGetObject(out var nestedValue)
                        || !catalog.TryGet(field.ValueTypeId, field.ValueTypeVersion, out var nested)) throw Type(field);
                    var nestedBytes = Encode(nested, nestedValue, catalog);
                    Buffer.BlockCopy(nestedBytes, 0, destination, offset, nestedBytes.Length); return;
                case GeneratedFieldEncoding.FixedBytes:
                    EncodeFixed(destination, offset, field, value); return;
                default:
                    throw new ArgumentException("Registered defaults cannot contain generated handles or unknown encodings.");
            }
        }

        private static void EncodeFixed(byte[] destination, int offset, GeneratedStorageField field, SemanticValue value)
        {
            BlackboardDefaultValue parsed;
            switch (field.ValueTypeId)
            {
                case "Float2": var f2 = Vector(value, 2, field); parsed = BlackboardDefaultValue.Float2(f2[0], f2[1]); break;
                case "Float3": var f3 = Vector(value, 3, field); parsed = BlackboardDefaultValue.Float3(f3[0], f3[1], f3[2]); break;
                case "Quaternion": var q = Vector(value, 4, field); parsed = BlackboardDefaultValue.Quaternion(q[0], q[1], q[2], q[3]); break;
                case "AgentId": if (!value.TryGetString(out var agentText) || !AgentId.TryParse(agentText, out var agent)) throw Type(field); parsed = BlackboardDefaultValue.AgentId(agent); break;
                case "EntityId": if (!value.TryGetString(out var entityText) || !EntityId.TryParse(entityText, out var entity)) throw Type(field); parsed = BlackboardDefaultValue.EntityId(entity); break;
                case "OperationId": if (!value.TryGetString(out var operationText) || !OperationId.TryParse(operationText, out var operation)) throw Type(field); parsed = BlackboardDefaultValue.OperationId(operation); break;
                case "AssetId":
                    if (!value.TryGetObject(out var assetValue)
                        || (assetValue.Properties.Count != 1 && assetValue.Properties.Count != 2)
                        || !string.Equals(assetValue.Properties[0].Name, "guid", StringComparison.Ordinal)
                        || !assetValue.Properties[0].Value.TryGetString(out var guid)) throw Type(field);
                    long? localFileId = null;
                    if (assetValue.Properties.Count == 2)
                    {
                        if (!string.Equals(assetValue.Properties[1].Name, "localFileId", StringComparison.Ordinal)
                            || !assetValue.Properties[1].Value.TryGetInt64(out var local)) throw Type(field);
                        localFileId = local;
                    }
                    if (!AssetId.TryParse(guid, localFileId, out var asset)) throw Type(field);
                    parsed = BlackboardDefaultValue.AssetId(asset); break;
                case "FixedString32": if (!value.TryGetString(out var s32)) throw Type(field); parsed = BlackboardDefaultValue.FixedString32(s32); break;
                case "FixedString64": if (!value.TryGetString(out var s64)) throw Type(field); parsed = BlackboardDefaultValue.FixedString64(s64); break;
                case "FixedString128": if (!value.TryGetString(out var s128)) throw Type(field); parsed = BlackboardDefaultValue.FixedString128(s128); break;
                case "FixedString512": if (!value.TryGetString(out var s512)) throw Type(field); parsed = BlackboardDefaultValue.FixedString512(s512); break;
                default: throw Type(field);
            }
            if (!parsed.TryGetRuntimeValue(out var runtime)) throw Type(field);
            var bytes = CompiledBlackboardValueEncoder.Encode(runtime);
            if (bytes.Length != field.Size) throw Type(field);
            Buffer.BlockCopy(bytes, 0, destination, offset, bytes.Length);
        }

        private static float[] Vector(SemanticValue value, int count, GeneratedStorageField field)
        {
            if (!value.TryGetObject(out var source) || source.Properties.Count != count) throw Type(field);
            var result = new float[count]; var names = new[] { "x", "y", "z", "w" };
            for (var index = 0; index < count; index++)
            {
                if (!source.TryGetValue(names[index], out var component)) throw Type(field);
                result[index] = checked((float)Number(component, field));
                if (float.IsNaN(result[index]) || float.IsInfinity(result[index])) throw Type(field);
            }
            return result;
        }

        private static long Signed(SemanticValue value, long minimum, long maximum, GeneratedStorageField field)
        {
            if (!value.TryGetInt64(out var result) || result < minimum || result > maximum) throw Type(field);
            return result;
        }
        private static ulong Unsigned(SemanticValue value, ulong maximum, GeneratedStorageField field)
        {
            ulong result;
            if (value.TryGetUInt64(out result)) { }
            else if (value.TryGetInt64(out var signed) && signed >= 0) result = (ulong)signed;
            else throw Type(field);
            if (result > maximum) throw Type(field); return result;
        }
        private static double Number(SemanticValue value, GeneratedStorageField field)
        {
            if (value.TryGetNumber(out var result)) return result;
            if (value.TryGetInt64(out var signed)) return signed;
            if (value.TryGetUInt64(out var unsigned)) return unsigned;
            throw Type(field);
        }
        private static void WriteSigned(byte[] bytes, int offset, long value, int count) => WriteUnsigned(bytes, offset, unchecked((ulong)value), count);
        private static void WriteUnsigned(byte[] bytes, int offset, ulong value, int count)
        { for (var index = 0; index < count; index++) bytes[offset + index] = (byte)(value >> (index * 8)); }
        private static ArgumentException Type(GeneratedStorageField field) => new ArgumentException("Registered default member '" + field.FieldId + "' differs from its closed canonical field codec.");
    }
}
