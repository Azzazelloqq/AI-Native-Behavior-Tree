using System;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.NodeDevelopment
{
    internal static class NativeBehaviorValues
    {
        internal static byte[] Encode(string type, JToken value)
        {
            if (value == null || type == "Bool" && value.Type != JTokenType.Boolean
                || type != "Bool" && value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
                throw new ArgumentException("Behavior values must use the declared JSON scalar type.");
            if ((type.StartsWith("Int", StringComparison.Ordinal) || type.StartsWith("UInt", StringComparison.Ordinal)) && value.Type != JTokenType.Integer)
                throw new ArgumentException("Integer behavior values must be JSON integers.");
            byte[] bytes;
            switch (type)
            {
                case "Bool": bytes = new[] { (byte)(value.Value<bool>() ? 1 : 0) }; break;
                case "Int32": bytes = BitConverter.GetBytes(value.Value<int>()); break;
                case "UInt32": bytes = BitConverter.GetBytes(value.Value<uint>()); break;
                case "Int64": bytes = BitConverter.GetBytes(value.Value<long>()); break;
                case "UInt64": bytes = BitConverter.GetBytes(value.Value<ulong>()); break;
                case "Float32":
                    var single = value.Value<float>();
                    if (float.IsNaN(single) || float.IsInfinity(single)) throw new ArgumentException("Float32 must be finite.");
                    bytes = BitConverter.GetBytes(single); break;
                case "Float64":
                    var number = value.Value<double>();
                    if (double.IsNaN(number) || double.IsInfinity(number)) throw new ArgumentException("Float64 must be finite.");
                    bytes = BitConverter.GetBytes(number); break;
                default: throw new NotSupportedException("Behavior values currently support Bool, Int32, UInt32, Int64, UInt64, Float32 and Float64; requested " + type + ".");
            }
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return bytes;
        }
    }
}
