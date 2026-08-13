using System;

namespace AIBT
{
    public readonly struct Float2Value : IEquatable<Float2Value>
    {
        public Float2Value(float x, float y)
        {
            X = BlackboardNumber.Canonicalize(x, nameof(x));
            Y = BlackboardNumber.Canonicalize(y, nameof(y));
        }

        public float X { get; }

        public float Y { get; }

        public bool Equals(Float2Value other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is Float2Value other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public static bool operator ==(Float2Value left, Float2Value right) => left.Equals(right);

        public static bool operator !=(Float2Value left, Float2Value right) => !left.Equals(right);
    }

    public readonly struct Float3Value : IEquatable<Float3Value>
    {
        public Float3Value(float x, float y, float z)
        {
            X = BlackboardNumber.Canonicalize(x, nameof(x));
            Y = BlackboardNumber.Canonicalize(y, nameof(y));
            Z = BlackboardNumber.Canonicalize(z, nameof(z));
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public bool Equals(Float3Value other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is Float3Value other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = X.GetHashCode();
                hashCode = (hashCode * 397) ^ Y.GetHashCode();
                return (hashCode * 397) ^ Z.GetHashCode();
            }
        }

        public static bool operator ==(Float3Value left, Float3Value right) => left.Equals(right);

        public static bool operator !=(Float3Value left, Float3Value right) => !left.Equals(right);
    }

    public readonly struct QuaternionValue : IEquatable<QuaternionValue>
    {
        public QuaternionValue(float x, float y, float z, float w)
        {
            X = BlackboardNumber.Canonicalize(x, nameof(x));
            Y = BlackboardNumber.Canonicalize(y, nameof(y));
            Z = BlackboardNumber.Canonicalize(z, nameof(z));
            W = BlackboardNumber.Canonicalize(w, nameof(w));
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public float W { get; }

        public bool Equals(QuaternionValue other)
        {
            return X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        }

        public override bool Equals(object obj) => obj is QuaternionValue other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = X.GetHashCode();
                hashCode = (hashCode * 397) ^ Y.GetHashCode();
                hashCode = (hashCode * 397) ^ Z.GetHashCode();
                return (hashCode * 397) ^ W.GetHashCode();
            }
        }

        public static bool operator ==(QuaternionValue left, QuaternionValue right) => left.Equals(right);

        public static bool operator !=(QuaternionValue left, QuaternionValue right) => !left.Equals(right);
    }

    internal static class BlackboardNumber
    {
        public static float Canonicalize(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, "Non-finite numbers are not valid blackboard values.");
            }

            return value == 0f ? 0f : value;
        }

        public static double Canonicalize(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, "Non-finite numbers are not valid blackboard values.");
            }

            return value == 0d ? 0d : value;
        }
    }
}
