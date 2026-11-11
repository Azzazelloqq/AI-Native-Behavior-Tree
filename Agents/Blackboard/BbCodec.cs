using System;
using UnityEngine;

namespace AIBT
{
    /// <summary>
    /// Allocation-free conversion between a C# value and the runtime's <see cref="BlackboardValue"/>
    /// for the types the managed layer exposes by name.
    /// </summary>
    internal static class BbCodec<T>
    {
        internal delegate bool Decoder(BlackboardValue value, out T result);

        // Assigned once by the static constructor through Assign<TValue>, which is why these are
        // not readonly: C# only allows a readonly static to be set directly inside its own type's
        // static constructor, not through the closed generic helper.
        internal static Func<T, BlackboardValueType, BlackboardValue> Encode;
        internal static Decoder Decode;
        internal static bool Supported;

        static BbCodec()
        {
            var type = typeof(T);
            if (type == typeof(bool))
            {
                Assign<bool>((value, _) => BlackboardValue.FromBool(value),
                    (BlackboardValue value, out bool result) => value.TryGetBool(out result));
            }
            else if (type == typeof(int))
            {
                Assign<int>((value, _) => BlackboardValue.FromInt32(value),
                    (BlackboardValue value, out int result) => value.TryGetInt32(out result));
            }
            else if (type == typeof(long))
            {
                Assign<long>((value, _) => BlackboardValue.FromInt64(value),
                    (BlackboardValue value, out long result) => value.TryGetInt64(out result));
            }
            else if (type == typeof(float))
            {
                Assign<float>((value, _) => BlackboardValue.FromFloat32(value),
                    (BlackboardValue value, out float result) => value.TryGetFloat32(out result));
            }
            else if (type == typeof(double))
            {
                Assign<double>((value, _) => BlackboardValue.FromFloat64(value),
                    (BlackboardValue value, out double result) => value.TryGetFloat64(out result));
            }
            else if (type == typeof(Vector2))
            {
                Assign<Vector2>((value, _) => BlackboardValue.FromFloat2(new Float2Value(value.x, value.y)),
                    (BlackboardValue value, out Vector2 result) =>
                    {
                        var ok = value.TryGetFloat2(out var raw);
                        result = new Vector2(raw.X, raw.Y);
                        return ok;
                    });
            }
            else if (type == typeof(Vector3))
            {
                Assign<Vector3>((value, _) => BlackboardValue.FromFloat3(new Float3Value(value.x, value.y, value.z)),
                    (BlackboardValue value, out Vector3 result) =>
                    {
                        var ok = value.TryGetFloat3(out var raw);
                        result = new Vector3(raw.X, raw.Y, raw.Z);
                        return ok;
                    });
            }
            else if (type == typeof(Quaternion))
            {
                Assign<Quaternion>((value, _) => BlackboardValue.FromQuaternion(new QuaternionValue(value.x, value.y, value.z, value.w)),
                    (BlackboardValue value, out Quaternion result) =>
                    {
                        var ok = value.TryGetQuaternion(out var raw);
                        result = new Quaternion(raw.X, raw.Y, raw.Z, raw.W);
                        return ok;
                    });
            }
            else if (type == typeof(string))
            {
                Assign<string>(EncodeString,
                    (BlackboardValue value, out string result) => value.TryGetFixedString(out result));
            }
        }

        private static void Assign<TValue>(
            Func<TValue, BlackboardValueType, BlackboardValue> encode,
            BbCodec<TValue>.Decoder decode)
        {
            BbCodec<TValue>.Encode = encode;
            BbCodec<TValue>.Decode = decode;
            BbCodec<TValue>.Supported = true;
        }

        private static BlackboardValue EncodeString(string value, BlackboardValueType slotType)
        {
            value = value ?? string.Empty;
            switch (slotType)
            {
                case BlackboardValueType.FixedString32: return BlackboardValue.FromString32(value);
                case BlackboardValueType.FixedString64: return BlackboardValue.FromString64(value);
                case BlackboardValueType.FixedString128: return BlackboardValue.FromString128(value);
                case BlackboardValueType.FixedString512: return BlackboardValue.FromString512(value);
                default: return default;
            }
        }
    }

    internal static class BbTypes
    {
        /// <summary>The single slot type a C# type maps to, or Invalid for string (any fixed-string slot).</summary>
        internal static bool TryGetSlotType(Type type, out BlackboardValueType slotType)
        {
            slotType = BlackboardValueType.Invalid;
            if (type == typeof(bool)) slotType = BlackboardValueType.Bool;
            else if (type == typeof(int)) slotType = BlackboardValueType.Int32;
            else if (type == typeof(long)) slotType = BlackboardValueType.Int64;
            else if (type == typeof(float)) slotType = BlackboardValueType.Float32;
            else if (type == typeof(double)) slotType = BlackboardValueType.Float64;
            else if (type == typeof(Vector2)) slotType = BlackboardValueType.Float2;
            else if (type == typeof(Vector3)) slotType = BlackboardValueType.Float3;
            else if (type == typeof(Quaternion)) slotType = BlackboardValueType.Quaternion;
            else return type == typeof(string);
            return true;
        }

        internal static bool Accepts(Type type, BlackboardValueType slotType)
        {
            if (!TryGetSlotType(type, out var expected)) return false;
            if (type == typeof(string))
            {
                return slotType == BlackboardValueType.FixedString32 || slotType == BlackboardValueType.FixedString64
                    || slotType == BlackboardValueType.FixedString128 || slotType == BlackboardValueType.FixedString512;
            }

            return slotType == expected;
        }

        internal static string Describe(Type type)
        {
            if (type == typeof(string)) return "a fixed string";
            return TryGetSlotType(type, out var slotType) ? slotType.ToString() : type.Name;
        }
    }
}
