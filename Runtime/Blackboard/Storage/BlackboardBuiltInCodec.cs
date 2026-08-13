using System;
using System.Text;

namespace AIBT
{
    internal static class BlackboardBuiltInCodec
    {
        internal static bool TryDecode(
            BlackboardTypeDescriptor descriptor,
            ReadOnlySpan<byte> source,
            out BlackboardValue value)
        {
            value = default;
            if (!descriptor.IsValid || source.Length != descriptor.Size) return false;
            try
            {
                switch (descriptor.ValueType)
                {
                    case BlackboardValueType.Bool:
                        if (source[0] > 1) return false;
                        value = BlackboardValue.FromBool(source[0] != 0); return true;
                    case BlackboardValueType.Int32:
                        value = BlackboardValue.FromInt32(unchecked((int)ReadUInt32(source, 0))); return true;
                    case BlackboardValueType.Int64:
                        value = BlackboardValue.FromInt64(unchecked((long)ReadUInt64(source, 0))); return true;
                    case BlackboardValueType.Float32:
                        value = BlackboardValue.FromFloat32(ReadFloat32(source, 0)); return true;
                    case BlackboardValueType.Float64:
                        value = BlackboardValue.FromFloat64(ReadFloat64(source, 0)); return true;
                    case BlackboardValueType.Float2:
                        value = BlackboardValue.FromFloat2(new Float2Value(ReadFloat32(source, 0), ReadFloat32(source, 4))); return true;
                    case BlackboardValueType.Float3:
                        value = BlackboardValue.FromFloat3(new Float3Value(ReadFloat32(source, 0), ReadFloat32(source, 4), ReadFloat32(source, 8))); return true;
                    case BlackboardValueType.Quaternion:
                        value = BlackboardValue.FromQuaternion(new QuaternionValue(
                            ReadFloat32(source, 0), ReadFloat32(source, 4), ReadFloat32(source, 8), ReadFloat32(source, 12))); return true;
                    case BlackboardValueType.Enum32:
                        value = BlackboardValue.FromEnum32(new Enum32Value(
                            ReadUInt64(source, 0),
                            unchecked((int)ReadUInt32(source, 8))));
                        return IsZero(source, 12);
                    case BlackboardValueType.FixedString32:
                    case BlackboardValueType.FixedString64:
                    case BlackboardValueType.FixedString128:
                    case BlackboardValueType.FixedString512:
                        return TryDecodeFixedString(descriptor.ValueType, source, out value);
                    case BlackboardValueType.AgentId:
                        value = BlackboardValue.FromAgentId(new AgentId(ReadUInt64(source, 0))); return true;
                    case BlackboardValueType.EntityId:
                        value = BlackboardValue.FromEntityId(new EntityId(ReadUInt64(source, 0))); return true;
                    case BlackboardValueType.OperationId:
                        value = BlackboardValue.FromOperationId(new OperationId(
                            new TreeInstanceId(ReadUInt64(source, 0)),
                            new RuntimeNodeIndex(ReadUInt32(source, 8)),
                            ReadUInt32(source, 12),
                            ReadUInt64(source, 16))); return true;
                    case BlackboardValueType.AssetId:
                        if (source[24] > 1 || !IsZero(source, 25)) return false;
                        value = BlackboardValue.FromAssetId(new AssetId(
                            ReadUInt64(source, 0),
                            ReadUInt64(source, 8),
                            unchecked((long)ReadUInt64(source, 16)),
                            source[24] != 0)); return true;
                    default:
                        return false;
                }
            }
            catch (ArgumentException)
            {
                value = default;
                return false;
            }
        }

        internal static bool TryEncode(BlackboardValue value, BlackboardTypeDescriptor descriptor, out byte[] bytes)
        {
            bytes = null;
            if (!value.IsValid || value.Type != descriptor.ValueType) return false;
            try
            {
                bytes = CompiledBlackboardValueEncoder.Encode(value);
                return bytes.Length == descriptor.Size;
            }
            catch (ArgumentException)
            {
                bytes = null;
                return false;
            }
        }

        private static bool TryDecodeFixedString(
            BlackboardValueType type,
            ReadOnlySpan<byte> source,
            out BlackboardValue value)
        {
            value = default;
            var length = source[0] | (source[1] << 8);
            if (length > source.Length - 2) return false;
            for (var index = 2 + length; index < source.Length; index++)
            {
                if (source[index] != 0) return false;
            }

            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(source.Slice(2, length).ToArray());
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            switch (type)
            {
                case BlackboardValueType.FixedString32: value = BlackboardValue.FromString32(text); return true;
                case BlackboardValueType.FixedString64: value = BlackboardValue.FromString64(text); return true;
                case BlackboardValueType.FixedString128: value = BlackboardValue.FromString128(text); return true;
                case BlackboardValueType.FixedString512: value = BlackboardValue.FromString512(text); return true;
                default: return false;
            }
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> source, int offset)
            => (uint)(source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16) | (source[offset + 3] << 24));

        private static ulong ReadUInt64(ReadOnlySpan<byte> source, int offset)
            => ReadUInt32(source, offset) | ((ulong)ReadUInt32(source, offset + 4) << 32);

        private static float ReadFloat32(ReadOnlySpan<byte> source, int offset)
        {
            var bytes = source.Slice(offset, 4).ToArray();
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }

        private static double ReadFloat64(ReadOnlySpan<byte> source, int offset)
        {
            var bytes = source.Slice(offset, 8).ToArray();
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToDouble(bytes, 0);
        }

        private static bool IsZero(ReadOnlySpan<byte> source, int start)
        {
            for (var index = start; index < source.Length; index++)
            {
                if (source[index] != 0) return false;
            }

            return true;
        }
    }
}
