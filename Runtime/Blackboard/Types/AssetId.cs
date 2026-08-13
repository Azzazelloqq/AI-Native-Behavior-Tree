using System;

namespace AIBT
{
    public readonly struct AssetId : IEquatable<AssetId>
    {
        public AssetId(ulong guidHigh, ulong guidLow, long localFileId = 0, bool hasLocalFileId = false)
        {
            GuidHigh = guidHigh;
            GuidLow = guidLow;
            LocalFileId = hasLocalFileId ? localFileId : 0;
            HasLocalFileId = hasLocalFileId;
        }

        public ulong GuidHigh { get; }

        public ulong GuidLow { get; }

        public long LocalFileId { get; }

        public bool HasLocalFileId { get; }

        public bool IsValid => GuidHigh != 0 || GuidLow != 0;

        public static AssetId Parse(string guid, long? localFileId = null)
        {
            if (!TryParse(guid, localFileId, out var result))
            {
                throw new FormatException("Asset GUIDs must contain exactly 32 lowercase hexadecimal characters.");
            }

            return result;
        }

        public static bool TryParse(string guid, long? localFileId, out AssetId result)
        {
            result = default;
            if (guid == null || guid.Length != 32
                || !TryParseHex(guid, 0, out var high)
                || !TryParseHex(guid, 16, out var low))
            {
                return false;
            }

            result = new AssetId(high, low, localFileId.GetValueOrDefault(), localFileId.HasValue);
            return true;
        }

        public string ToGuidString()
        {
            var characters = new char[32];
            WriteHex(GuidHigh, characters, 0);
            WriteHex(GuidLow, characters, 16);
            return new string(characters);
        }

        public bool Equals(AssetId other)
        {
            return GuidHigh == other.GuidHigh
                && GuidLow == other.GuidLow
                && LocalFileId == other.LocalFileId
                && HasLocalFileId == other.HasLocalFileId;
        }

        public override bool Equals(object obj) => obj is AssetId other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = GuidHigh.GetHashCode();
                hashCode = (hashCode * 397) ^ GuidLow.GetHashCode();
                hashCode = (hashCode * 397) ^ LocalFileId.GetHashCode();
                return (hashCode * 397) ^ HasLocalFileId.GetHashCode();
            }
        }

        public override string ToString() => ToGuidString();

        public static bool operator ==(AssetId left, AssetId right) => left.Equals(right);

        public static bool operator !=(AssetId left, AssetId right) => !left.Equals(right);

        private static bool TryParseHex(string value, int offset, out ulong result)
        {
            result = 0;
            for (var index = 0; index < 16; index++)
            {
                var character = value[offset + index];
                int digit;
                if (character >= '0' && character <= '9')
                {
                    digit = character - '0';
                }
                else if (character >= 'a' && character <= 'f')
                {
                    digit = character - 'a' + 10;
                }
                else
                {
                    return false;
                }

                result = (result << 4) | (uint)digit;
            }

            return true;
        }

        private static void WriteHex(ulong value, char[] destination, int offset)
        {
            for (var index = 15; index >= 0; index--)
            {
                var digit = (int)(value & 0xf);
                destination[offset + index] = (char)(digit < 10 ? '0' + digit : 'a' + digit - 10);
                value >>= 4;
            }
        }
    }
}
