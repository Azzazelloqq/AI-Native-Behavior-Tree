using System;

namespace AIBT
{
    public enum NodeMemoryLifetime : byte
    {
        Activation = 0,
        Instance = 1,
    }

    public static class CompiledIndex
    {
        public const uint Invalid = uint.MaxValue;
    }

    public readonly struct CompiledRange : IEquatable<CompiledRange>
    {
        public CompiledRange(uint offset, uint count)
        {
            if (offset == CompiledIndex.Invalid)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if ((ulong)offset + count > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "The range exceeds the 32-bit address space.");
            }

            Offset = offset;
            Count = count;
        }

        public uint Offset { get; }

        public uint Count { get; }

        public ulong EndExclusive => (ulong)Offset + Count;

        public bool IsEmpty => Count == 0;

        public bool Equals(CompiledRange other) => Offset == other.Offset && Count == other.Count;

        public override bool Equals(object obj) => obj is CompiledRange other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Offset * 397) ^ (int)Count;
            }
        }

        public static bool operator ==(CompiledRange left, CompiledRange right) => left.Equals(right);

        public static bool operator !=(CompiledRange left, CompiledRange right) => !left.Equals(right);
    }

    public readonly struct CompiledHash : IEquatable<CompiledHash>
    {
        public const int HexLength = 64;

        public CompiledHash(string hexadecimalValue)
        {
            if (!IsCanonical(hexadecimalValue))
            {
                throw new ArgumentException("A compiled hash must contain exactly 64 lowercase hexadecimal characters.", nameof(hexadecimalValue));
            }

            HexadecimalValue = hexadecimalValue;
        }

        public string HexadecimalValue { get; }

        public bool IsValid => IsCanonical(HexadecimalValue);

        public bool Equals(CompiledHash other)
            => string.Equals(HexadecimalValue, other.HexadecimalValue, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is CompiledHash other && Equals(other);

        public override int GetHashCode() => HexadecimalValue == null ? 0 : HexadecimalValue.GetHashCode();

        public override string ToString() => HexadecimalValue ?? string.Empty;

        public static bool operator ==(CompiledHash left, CompiledHash right) => left.Equals(right);

        public static bool operator !=(CompiledHash left, CompiledHash right) => !left.Equals(right);

        private static bool IsCanonical(string value)
        {
            if (value == null || value.Length != HexLength)
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if ((character < '0' || character > '9') && (character < 'a' || character > 'f'))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public readonly struct CompiledCompilerVersion : IEquatable<CompiledCompilerVersion>
    {
        public CompiledCompilerVersion(ushort major, ushort minor, ushort patch, uint buildRevision)
        {
            if (major == 0 && minor == 0 && patch == 0 && buildRevision == 0)
            {
                throw new ArgumentException("The compiler version cannot be zero.");
            }

            Major = major;
            Minor = minor;
            Patch = patch;
            BuildRevision = buildRevision;
        }

        public ushort Major { get; }

        public ushort Minor { get; }

        public ushort Patch { get; }

        public uint BuildRevision { get; }

        public bool IsValid => Major != 0 || Minor != 0 || Patch != 0 || BuildRevision != 0;

        public bool Equals(CompiledCompilerVersion other)
            => Major == other.Major
                && Minor == other.Minor
                && Patch == other.Patch
                && BuildRevision == other.BuildRevision;

        public override bool Equals(object obj) => obj is CompiledCompilerVersion other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Major.GetHashCode();
                hashCode = (hashCode * 397) ^ Minor.GetHashCode();
                hashCode = (hashCode * 397) ^ Patch.GetHashCode();
                return (hashCode * 397) ^ (int)BuildRevision;
            }
        }

        public static bool operator ==(CompiledCompilerVersion left, CompiledCompilerVersion right) => left.Equals(right);

        public static bool operator !=(CompiledCompilerVersion left, CompiledCompilerVersion right) => !left.Equals(right);
    }
}
