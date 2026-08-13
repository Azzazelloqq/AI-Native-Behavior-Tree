using System;
using System.Text;

namespace AIBT
{
    internal static class CompiledBlackboardValueEncoder
    {
        public static byte[] Encode(BlackboardValue value)
        {
            if (!value.IsValid || !BuiltInBlackboardTypes.TryGet(value.Type, out var descriptor))
            {
                throw new ArgumentException("A valid built-in blackboard value is required.", nameof(value));
            }

            var bytes = new byte[descriptor.Size];
            switch (value.Type)
            {
                case BlackboardValueType.Bool:
                    value.TryGetBool(out var boolValue);
                    bytes[0] = boolValue ? (byte)1 : (byte)0;
                    break;
                case BlackboardValueType.Int32:
                    value.TryGetInt32(out var int32Value);
                    WriteUInt32(bytes, 0, unchecked((uint)int32Value));
                    break;
                case BlackboardValueType.Int64:
                    value.TryGetInt64(out var int64Value);
                    WriteUInt64(bytes, 0, unchecked((ulong)int64Value));
                    break;
                case BlackboardValueType.Float32:
                    value.TryGetFloat32(out var float32Value);
                    WriteBitConverterBytes(bytes, 0, BitConverter.GetBytes(float32Value));
                    break;
                case BlackboardValueType.Float64:
                    value.TryGetFloat64(out var float64Value);
                    WriteBitConverterBytes(bytes, 0, BitConverter.GetBytes(float64Value));
                    break;
                case BlackboardValueType.Float2:
                    value.TryGetFloat2(out var float2Value);
                    WriteFloat32(bytes, 0, float2Value.X);
                    WriteFloat32(bytes, 4, float2Value.Y);
                    break;
                case BlackboardValueType.Float3:
                    value.TryGetFloat3(out var float3Value);
                    WriteFloat32(bytes, 0, float3Value.X);
                    WriteFloat32(bytes, 4, float3Value.Y);
                    WriteFloat32(bytes, 8, float3Value.Z);
                    break;
                case BlackboardValueType.Quaternion:
                    value.TryGetQuaternion(out var quaternionValue);
                    WriteFloat32(bytes, 0, quaternionValue.X);
                    WriteFloat32(bytes, 4, quaternionValue.Y);
                    WriteFloat32(bytes, 8, quaternionValue.Z);
                    WriteFloat32(bytes, 12, quaternionValue.W);
                    break;
                case BlackboardValueType.Enum32:
                    value.TryGetEnum32(out var enumValue);
                    WriteUInt64(bytes, 0, enumValue.ContractTypeId);
                    WriteUInt32(bytes, 8, unchecked((uint)enumValue.Value));
                    break;
                case BlackboardValueType.FixedString32:
                case BlackboardValueType.FixedString64:
                case BlackboardValueType.FixedString128:
                case BlackboardValueType.FixedString512:
                    value.TryGetFixedString(out var stringValue);
                    WriteFixedString(bytes, stringValue, FixedStringCapacity(value.Type));
                    break;
                case BlackboardValueType.AgentId:
                    value.TryGetAgentId(out var agentId);
                    WriteUInt64(bytes, 0, agentId.Value);
                    break;
                case BlackboardValueType.EntityId:
                    value.TryGetEntityId(out var entityId);
                    WriteUInt64(bytes, 0, entityId.Value);
                    break;
                case BlackboardValueType.OperationId:
                    value.TryGetOperationId(out var operationId);
                    WriteUInt64(bytes, 0, operationId.TreeInstanceId.Value);
                    WriteUInt32(bytes, 8, operationId.NodeIndex.Value);
                    WriteUInt32(bytes, 12, operationId.ActivationGeneration);
                    WriteUInt64(bytes, 16, operationId.Sequence);
                    break;
                case BlackboardValueType.AssetId:
                    value.TryGetAssetId(out var assetId);
                    WriteUInt64(bytes, 0, assetId.GuidHigh);
                    WriteUInt64(bytes, 8, assetId.GuidLow);
                    WriteUInt64(bytes, 16, unchecked((ulong)assetId.LocalFileId));
                    bytes[24] = assetId.HasLocalFileId ? (byte)1 : (byte)0;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), "The value type has no Phase 1 built-in encoding.");
            }

            return bytes;
        }

        private static int FixedStringCapacity(BlackboardValueType type)
        {
            switch (type)
            {
                case BlackboardValueType.FixedString32: return BlackboardFixedStringCapacity.FixedString32;
                case BlackboardValueType.FixedString64: return BlackboardFixedStringCapacity.FixedString64;
                case BlackboardValueType.FixedString128: return BlackboardFixedStringCapacity.FixedString128;
                case BlackboardValueType.FixedString512: return BlackboardFixedStringCapacity.FixedString512;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static void WriteFixedString(byte[] destination, string value, int capacity)
        {
            var utf8 = Encoding.UTF8.GetBytes(value);
            if (utf8.Length > capacity || utf8.Length > ushort.MaxValue)
            {
                throw new ArgumentException("The fixed string exceeds its declared UTF-8 capacity.", nameof(value));
            }

            destination[0] = (byte)utf8.Length;
            destination[1] = (byte)(utf8.Length >> 8);
            Buffer.BlockCopy(utf8, 0, destination, 2, utf8.Length);
        }

        private static void WriteFloat32(byte[] destination, int offset, float value)
            => WriteBitConverterBytes(destination, offset, BitConverter.GetBytes(value));

        private static void WriteBitConverterBytes(byte[] destination, int offset, byte[] source)
        {
            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(source, 0, destination, offset, source.Length);
                return;
            }

            for (var index = 0; index < source.Length; index++)
            {
                destination[offset + index] = source[source.Length - index - 1];
            }
        }

        private static void WriteUInt32(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt64(byte[] destination, int offset, ulong value)
        {
            WriteUInt32(destination, offset, (uint)value);
            WriteUInt32(destination, offset + 4, (uint)(value >> 32));
        }
    }
}
