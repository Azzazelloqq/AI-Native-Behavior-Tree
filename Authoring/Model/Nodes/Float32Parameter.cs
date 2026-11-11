using System;

namespace AIBT.Authoring
{
    internal static class Float32Parameter
    {
        internal static bool TryRead(SemanticValue value, out float result)
        {
            result = 0f;
            if (value == null) return false;
            double number;
            if (value.TryGetNumber(out number)) { }
            else if (value.TryGetInt64(out var signed)) number = signed;
            else if (value.TryGetUInt64(out var unsigned)) number = unsigned;
            else return false;
            result = (float)number;
            if (float.IsNaN(result) || float.IsInfinity(result)) return false;
            if (result == 0f) result = 0f;
            return true;
        }

        internal static uint Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));
    }
}
