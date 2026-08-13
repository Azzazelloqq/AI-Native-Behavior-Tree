using System;

namespace AIBT
{
    public readonly struct DiagnosticCode : IEquatable<DiagnosticCode>, IComparable<DiagnosticCode>
    {
        private const int PrefixLength = 4;
        private const int DigitCount = 4;

        public DiagnosticCode(string value)
        {
            if (!IsValidValue(value))
            {
                throw new ArgumentException(
                    "Diagnostic codes must use 'AIBT' followed by four decimal digits.",
                    nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool IsValid => IsValidValue(Value);

        public static DiagnosticCode Parse(string value)
        {
            return new DiagnosticCode(value);
        }

        public static bool TryParse(string value, out DiagnosticCode result)
        {
            if (!IsValidValue(value))
            {
                result = default;
                return false;
            }

            result = new DiagnosticCode(value);
            return true;
        }

        public int CompareTo(DiagnosticCode other)
        {
            return string.Compare(Value, other.Value, StringComparison.Ordinal);
        }

        public bool Equals(DiagnosticCode other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is DiagnosticCode other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }

        public static bool operator ==(DiagnosticCode left, DiagnosticCode right) => left.Equals(right);

        public static bool operator !=(DiagnosticCode left, DiagnosticCode right) => !left.Equals(right);

        private static bool IsValidValue(string value)
        {
            if (value == null || value.Length != PrefixLength + DigitCount
                || value[0] != 'A' || value[1] != 'I' || value[2] != 'B' || value[3] != 'T')
            {
                return false;
            }

            for (var index = PrefixLength; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
