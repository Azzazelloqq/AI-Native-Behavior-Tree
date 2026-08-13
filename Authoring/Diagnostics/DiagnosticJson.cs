using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AIBT.Authoring
{
    public static class DiagnosticJson
    {
        public static string Serialize(AuthoringDiagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            return CanonicalDiagnosticJsonWriter.Serialize(diagnostic);
        }

        public static byte[] SerializeUtf8(AuthoringDiagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            return CanonicalDiagnosticJsonWriter.SerializeUtf8(diagnostic);
        }

        // Compatibility for other authoring serializers. Diagnostic operation payloads never expose or accept JToken.
        internal static JToken CanonicalizePayload(JToken token)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (token is JObject sourceObject)
            {
                var properties = new List<JProperty>(sourceObject.Properties());
                properties.Sort((left, right) => CanonicalJsonText.CompareUtf8(left.Name, right.Name));
                var result = new JObject();
                for (var index = 0; index < properties.Count; index++)
                {
                    CanonicalJsonText.ValidateUnicode(properties[index].Name, nameof(token));
                    result.Add(properties[index].Name, CanonicalizePayload(properties[index].Value));
                }

                return result;
            }

            if (token is JArray sourceArray)
            {
                var result = new JArray();
                for (var index = 0; index < sourceArray.Count; index++)
                {
                    result.Add(CanonicalizePayload(sourceArray[index]));
                }

                return result;
            }

            if (!(token is JValue value))
            {
                throw new ArgumentException("Only canonical JSON value kinds are supported.", nameof(token));
            }

            switch (value.Type)
            {
                case JTokenType.Null:
                case JTokenType.Boolean:
                case JTokenType.Integer:
                    return value.DeepClone();
                case JTokenType.Float:
                    var number = Convert.ToDouble(value.Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (double.IsNaN(number) || double.IsInfinity(number))
                    {
                        throw new ArgumentException("JSON numbers must be finite.", nameof(token));
                    }
                    return value.DeepClone();
                case JTokenType.String:
                    CanonicalJsonText.ValidateUnicode((string)value.Value, nameof(token));
                    return value.DeepClone();
                default:
                    throw new ArgumentException("Only null, Boolean, integer, finite float, string, array, and object JSON values are supported.", nameof(token));
            }
        }
    }
}
