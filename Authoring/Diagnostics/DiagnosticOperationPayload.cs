using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public enum DiagnosticPayloadKind : byte
    {
        Null,
        Boolean,
        Int32,
        Int64,
        Float32,
        Float64,
        String,
        Array,
        Map,
    }

    public readonly struct DiagnosticPayloadMember
    {
        public DiagnosticPayloadMember(string name, DiagnosticOperationPayload value)
        {
            CanonicalJsonText.ValidateUnicode(name, nameof(name));
            if (name.Length == 0)
            {
                throw new ArgumentException("Payload member names cannot be empty.", nameof(name));
            }

            Name = name;
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Name { get; }

        public DiagnosticOperationPayload Value { get; }
    }

    public sealed class DiagnosticOperationPayload : IEquatable<DiagnosticOperationPayload>, IComparable<DiagnosticOperationPayload>
    {
        private static readonly ReadOnlyCollection<DiagnosticOperationPayload> EmptyItems =
            System.Array.AsReadOnly(System.Array.Empty<DiagnosticOperationPayload>());
        private static readonly ReadOnlyCollection<DiagnosticPayloadMember> EmptyMembers =
            System.Array.AsReadOnly(System.Array.Empty<DiagnosticPayloadMember>());
        private static readonly DiagnosticOperationPayload NullInstance = new DiagnosticOperationPayload(DiagnosticPayloadKind.Null, null);

        private readonly object _value;
        private readonly byte[] _canonicalBytes;

        private DiagnosticOperationPayload(DiagnosticPayloadKind kind, object value)
        {
            Kind = kind;
            _value = value;
            _canonicalBytes = CanonicalDiagnosticJsonWriter.SerializePayloadUtf8(this);
        }

        public DiagnosticPayloadKind Kind { get; }

        public static DiagnosticOperationPayload Null => NullInstance;

        public static DiagnosticOperationPayload From(bool value) =>
            new DiagnosticOperationPayload(DiagnosticPayloadKind.Boolean, value);

        public static DiagnosticOperationPayload From(int value) =>
            new DiagnosticOperationPayload(DiagnosticPayloadKind.Int32, value);

        public static DiagnosticOperationPayload From(long value) =>
            new DiagnosticOperationPayload(DiagnosticPayloadKind.Int64, value);

        public static DiagnosticOperationPayload From(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Diagnostic payload numbers must be finite.");
            }

            return new DiagnosticOperationPayload(DiagnosticPayloadKind.Float32, value);
        }

        public static DiagnosticOperationPayload From(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Diagnostic payload numbers must be finite.");
            }

            return new DiagnosticOperationPayload(DiagnosticPayloadKind.Float64, value);
        }

        public static DiagnosticOperationPayload From(string value)
        {
            CanonicalJsonText.ValidateUnicode(value, nameof(value));
            return new DiagnosticOperationPayload(DiagnosticPayloadKind.String, value);
        }

        public static DiagnosticOperationPayload Array(IEnumerable<DiagnosticOperationPayload> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var copy = new List<DiagnosticOperationPayload>();
            foreach (var item in items)
            {
                if (item == null)
                {
                    throw new ArgumentException("Payload arrays cannot contain null references; use DiagnosticOperationPayload.Null.", nameof(items));
                }

                copy.Add(item);
            }

            return new DiagnosticOperationPayload(DiagnosticPayloadKind.Array, copy.AsReadOnly());
        }

        public static DiagnosticOperationPayload Array(params DiagnosticOperationPayload[] items) =>
            Array((IEnumerable<DiagnosticOperationPayload>)items);

        public static DiagnosticOperationPayload Map(IEnumerable<DiagnosticPayloadMember> members)
        {
            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            var copy = new List<DiagnosticPayloadMember>(members);
            for (var index = 0; index < copy.Count; index++)
            {
                if (copy[index].Name == null || copy[index].Value == null)
                {
                    throw new ArgumentException("Payload maps must contain initialized members.", nameof(members));
                }
            }

            copy.Sort((left, right) => CanonicalJsonText.CompareUtf8(left.Name, right.Name));
            for (var index = 0; index < copy.Count; index++)
            {
                if (index > 0 && string.Equals(copy[index - 1].Name, copy[index].Name, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Payload map member names must be unique.", nameof(members));
                }
            }

            return new DiagnosticOperationPayload(DiagnosticPayloadKind.Map, copy.AsReadOnly());
        }

        public static DiagnosticOperationPayload Map(params DiagnosticPayloadMember[] members) =>
            Map((IEnumerable<DiagnosticPayloadMember>)members);

        public bool BooleanValue => GetValue<bool>(DiagnosticPayloadKind.Boolean);

        public int Int32Value => GetValue<int>(DiagnosticPayloadKind.Int32);

        public long Int64Value => GetValue<long>(DiagnosticPayloadKind.Int64);

        public float Float32Value => GetValue<float>(DiagnosticPayloadKind.Float32);

        public double Float64Value => GetValue<double>(DiagnosticPayloadKind.Float64);

        public string StringValue => GetValue<string>(DiagnosticPayloadKind.String);

        public IReadOnlyList<DiagnosticOperationPayload> Items =>
            Kind == DiagnosticPayloadKind.Array
                ? (IReadOnlyList<DiagnosticOperationPayload>)_value
                : EmptyItems;

        public IReadOnlyList<DiagnosticPayloadMember> Members =>
            Kind == DiagnosticPayloadKind.Map
                ? (IReadOnlyList<DiagnosticPayloadMember>)_value
                : EmptyMembers;

        public int CompareTo(DiagnosticOperationPayload other)
        {
            return other == null ? 1 : CanonicalBytes.Compare(_canonicalBytes, other._canonicalBytes);
        }

        public bool Equals(DiagnosticOperationPayload other)
        {
            return other != null && CanonicalBytes.Equals(_canonicalBytes, other._canonicalBytes);
        }

        public override bool Equals(object obj) => Equals(obj as DiagnosticOperationPayload);

        public override int GetHashCode() => CanonicalBytes.GetDeterministicHashCode(_canonicalBytes);

        private T GetValue<T>(DiagnosticPayloadKind expectedKind)
        {
            if (Kind != expectedKind)
            {
                throw new InvalidOperationException($"Payload kind is {Kind}, not {expectedKind}.");
            }

            return (T)_value;
        }
    }
}
