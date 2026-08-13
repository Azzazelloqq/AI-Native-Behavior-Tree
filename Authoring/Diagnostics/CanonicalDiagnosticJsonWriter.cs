using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace AIBT.Authoring
{
    internal static class DiagnosticJsonContract
    {
        private static readonly ReadOnlyCollection<string> DiagnosticPropertiesValue = Array.AsReadOnly(new[]
        {
            "code",
            "severity",
            "message",
            "treeId",
            "nodeId",
            "treeInstanceId",
            "documentId",
            "jsonPointer",
            "line",
            "column",
            "relatedLocations",
            "suggestedOperation",
        });

        private static readonly ReadOnlyCollection<string> LocationPropertiesValue = Array.AsReadOnly(new[]
        {
            "treeId",
            "nodeId",
            "treeInstanceId",
            "documentId",
            "jsonPointer",
            "line",
            "column",
        });

        private static readonly ReadOnlyCollection<string> OperationPropertiesValue = Array.AsReadOnly(new[]
        {
            "operationId",
            "payloadType",
            "payload",
        });

        internal static IReadOnlyList<string> DiagnosticProperties => DiagnosticPropertiesValue;

        internal static IReadOnlyList<string> LocationProperties => LocationPropertiesValue;

        internal static IReadOnlyList<string> OperationProperties => OperationPropertiesValue;
    }

    internal static class CanonicalDiagnosticJsonWriter
    {
        internal static string Serialize(AuthoringDiagnostic diagnostic)
        {
            var builder = new StringBuilder(256);
            WriteDiagnostic(builder, diagnostic);
            builder.Append('\n');
            return builder.ToString();
        }

        internal static byte[] SerializeUtf8(AuthoringDiagnostic diagnostic) =>
            Encoding.UTF8.GetBytes(Serialize(diagnostic));

        internal static byte[] SerializePayloadUtf8(DiagnosticOperationPayload payload)
        {
            var builder = new StringBuilder(64);
            WritePayload(builder, payload, 0);
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        internal static byte[] SerializeOperationUtf8(SuggestedDiagnosticOperation operation)
        {
            var builder = new StringBuilder(128);
            WriteOperation(builder, operation, 0);
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        private static void WriteDiagnostic(StringBuilder builder, AuthoringDiagnostic authoringDiagnostic)
        {
            var diagnostic = authoringDiagnostic.Diagnostic;
            builder.Append('{');
            var hasProperty = false;
            for (var index = 0; index < DiagnosticJsonContract.DiagnosticProperties.Count; index++)
            {
                var property = DiagnosticJsonContract.DiagnosticProperties[index];
                switch (property)
                {
                    case "code":
                        WriteStringProperty(builder, property, diagnostic.Code.Value, 1, ref hasProperty);
                        break;
                    case "severity":
                        WriteStringProperty(builder, property, SeverityText(diagnostic.Severity), 1, ref hasProperty);
                        break;
                    case "message":
                        WriteStringProperty(builder, property, diagnostic.Message, 1, ref hasProperty);
                        break;
                    case "treeId":
                    case "nodeId":
                    case "treeInstanceId":
                    case "documentId":
                    case "jsonPointer":
                    case "line":
                    case "column":
                        WriteLocationProperty(builder, property, diagnostic.Location, 1, ref hasProperty);
                        break;
                    case "relatedLocations":
                        if (diagnostic.RelatedLocations.Count > 0)
                        {
                            BeginProperty(builder, property, 1, ref hasProperty);
                            WriteLocations(builder, diagnostic.RelatedLocations, 1);
                        }
                        break;
                    case "suggestedOperation":
                        if (authoringDiagnostic.SuggestedOperation != null)
                        {
                            BeginProperty(builder, property, 1, ref hasProperty);
                            WriteOperation(builder, authoringDiagnostic.SuggestedOperation, 1);
                        }
                        break;
                    default:
                        throw new InvalidOperationException("Unknown canonical diagnostic property.");
                }
            }

            EndObject(builder, 0, hasProperty);
        }

        private static void WriteLocations(StringBuilder builder, IReadOnlyList<DiagnosticLocation> locations, int indent)
        {
            builder.Append('[');
            for (var index = 0; index < locations.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append('\n');
                AppendIndent(builder, indent + 1);
                WriteLocation(builder, locations[index], indent + 1);
            }

            builder.Append('\n');
            AppendIndent(builder, indent);
            builder.Append(']');
        }

        private static void WriteLocation(StringBuilder builder, DiagnosticLocation location, int indent)
        {
            builder.Append('{');
            var hasProperty = false;
            for (var index = 0; index < DiagnosticJsonContract.LocationProperties.Count; index++)
            {
                WriteLocationProperty(builder, DiagnosticJsonContract.LocationProperties[index], location, indent + 1, ref hasProperty);
            }

            EndObject(builder, indent, hasProperty);
        }

        private static void WriteLocationProperty(
            StringBuilder builder,
            string property,
            DiagnosticLocation location,
            int indent,
            ref bool hasProperty)
        {
            switch (property)
            {
                case "treeId":
                    if (location.TreeId.IsValid)
                    {
                        WriteStringProperty(builder, property, location.TreeId.Value, indent, ref hasProperty);
                    }
                    break;
                case "nodeId":
                    if (location.NodeId.IsValid)
                    {
                        WriteStringProperty(builder, property, location.NodeId.Value, indent, ref hasProperty);
                    }
                    break;
                case "treeInstanceId":
                    if (location.TreeInstanceId.IsValid)
                    {
                        WriteStringProperty(builder, property, location.TreeInstanceId.ToString(), indent, ref hasProperty);
                    }
                    break;
                case "documentId":
                    if (location.HasDocumentId)
                    {
                        WriteStringProperty(builder, property, location.DocumentId, indent, ref hasProperty);
                    }
                    break;
                case "jsonPointer":
                    if (location.HasJsonPointer)
                    {
                        WriteStringProperty(builder, property, location.JsonPointer, indent, ref hasProperty);
                    }
                    break;
                case "line":
                    if (location.Line.HasValue)
                    {
                        WriteIntProperty(builder, property, location.Line.Value, indent, ref hasProperty);
                    }
                    break;
                case "column":
                    if (location.Column.HasValue)
                    {
                        WriteIntProperty(builder, property, location.Column.Value, indent, ref hasProperty);
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown canonical diagnostic location property.");
            }
        }

        private static void WriteOperation(StringBuilder builder, SuggestedDiagnosticOperation operation, int indent)
        {
            builder.Append('{');
            var hasProperty = false;
            for (var index = 0; index < DiagnosticJsonContract.OperationProperties.Count; index++)
            {
                var property = DiagnosticJsonContract.OperationProperties[index];
                switch (property)
                {
                    case "operationId":
                        WriteStringProperty(builder, property, operation.OperationId, indent + 1, ref hasProperty);
                        break;
                    case "payloadType":
                        WriteStringProperty(builder, property, operation.PayloadType, indent + 1, ref hasProperty);
                        break;
                    case "payload":
                        BeginProperty(builder, property, indent + 1, ref hasProperty);
                        WritePayload(builder, operation.Payload, indent + 1);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown canonical diagnostic operation property.");
                }
            }

            EndObject(builder, indent, hasProperty);
        }

        private static void WritePayload(StringBuilder builder, DiagnosticOperationPayload payload, int indent)
        {
            switch (payload.Kind)
            {
                case DiagnosticPayloadKind.Null:
                    builder.Append("null");
                    break;
                case DiagnosticPayloadKind.Boolean:
                    builder.Append(payload.BooleanValue ? "true" : "false");
                    break;
                case DiagnosticPayloadKind.Int32:
                    builder.Append(payload.Int32Value.ToString(CultureInfo.InvariantCulture));
                    break;
                case DiagnosticPayloadKind.Int64:
                    builder.Append(payload.Int64Value.ToString(CultureInfo.InvariantCulture));
                    break;
                case DiagnosticPayloadKind.Float32:
                    builder.Append(CanonicalJsonNumber.Format(payload.Float32Value));
                    break;
                case DiagnosticPayloadKind.Float64:
                    builder.Append(CanonicalJsonNumber.Format(payload.Float64Value));
                    break;
                case DiagnosticPayloadKind.String:
                    CanonicalJsonText.WriteString(builder, payload.StringValue);
                    break;
                case DiagnosticPayloadKind.Array:
                    WritePayloadArray(builder, payload.Items, indent);
                    break;
                case DiagnosticPayloadKind.Map:
                    WritePayloadMap(builder, payload.Members, indent);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(payload), payload.Kind, null);
            }
        }

        private static void WritePayloadArray(StringBuilder builder, IReadOnlyList<DiagnosticOperationPayload> items, int indent)
        {
            if (items.Count == 0)
            {
                builder.Append("[]");
                return;
            }

            builder.Append('[');
            for (var index = 0; index < items.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append('\n');
                AppendIndent(builder, indent + 1);
                WritePayload(builder, items[index], indent + 1);
            }

            builder.Append('\n');
            AppendIndent(builder, indent);
            builder.Append(']');
        }

        private static void WritePayloadMap(StringBuilder builder, IReadOnlyList<DiagnosticPayloadMember> members, int indent)
        {
            if (members.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            builder.Append('{');
            var hasProperty = false;
            for (var index = 0; index < members.Count; index++)
            {
                BeginProperty(builder, members[index].Name, indent + 1, ref hasProperty);
                WritePayload(builder, members[index].Value, indent + 1);
            }

            EndObject(builder, indent, hasProperty);
        }

        private static void WriteStringProperty(
            StringBuilder builder,
            string name,
            string value,
            int indent,
            ref bool hasProperty)
        {
            BeginProperty(builder, name, indent, ref hasProperty);
            CanonicalJsonText.WriteString(builder, value);
        }

        private static void WriteIntProperty(
            StringBuilder builder,
            string name,
            int value,
            int indent,
            ref bool hasProperty)
        {
            BeginProperty(builder, name, indent, ref hasProperty);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void BeginProperty(StringBuilder builder, string name, int indent, ref bool hasProperty)
        {
            if (hasProperty)
            {
                builder.Append(',');
            }

            builder.Append('\n');
            AppendIndent(builder, indent);
            CanonicalJsonText.WriteString(builder, name);
            builder.Append(": ");
            hasProperty = true;
        }

        private static void EndObject(StringBuilder builder, int indent, bool hasProperty)
        {
            if (hasProperty)
            {
                builder.Append('\n');
                AppendIndent(builder, indent);
            }

            builder.Append('}');
        }

        private static void AppendIndent(StringBuilder builder, int indent)
        {
            builder.Append(' ', indent * 2);
        }

        private static string SeverityText(DiagnosticSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticSeverity.Error:
                    return "error";
                case DiagnosticSeverity.Warning:
                    return "warning";
                case DiagnosticSeverity.Info:
                    return "info";
                default:
                    throw new ArgumentOutOfRangeException(nameof(severity), severity, null);
            }
        }
    }

    internal static class CanonicalJsonText
    {
        private const string Hex = "0123456789abcdef";

        internal static void ValidateUnicode(string value, string parameterName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsHighSurrogate(character))
                {
                    if (++index >= value.Length || !char.IsLowSurrogate(value[index]))
                    {
                        throw new ArgumentException("Strings must contain valid Unicode scalar sequences.", parameterName);
                    }
                }
                else if (char.IsLowSurrogate(character))
                {
                    throw new ArgumentException("Strings must contain valid Unicode scalar sequences.", parameterName);
                }
            }
        }

        internal static int CompareUtf8(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            return CanonicalBytes.Compare(leftBytes, rightBytes);
        }

        internal static void WriteString(StringBuilder builder, string value)
        {
            ValidateUnicode(value, nameof(value));
            builder.Append('"');
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u00");
                            builder.Append(Hex[(character >> 4) & 0xf]);
                            builder.Append(Hex[character & 0xf]);
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }

            builder.Append('"');
        }
    }

    internal static class CanonicalJsonNumber
    {
        internal static string Format(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return value == 0f ? "0" : NormalizeExponent(value.ToString("R", CultureInfo.InvariantCulture));
        }

        internal static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return value == 0d ? "0" : NormalizeExponent(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static string NormalizeExponent(string value)
        {
            var exponentIndex = value.IndexOf('E');
            if (exponentIndex < 0)
            {
                return value;
            }

            var exponentStart = exponentIndex + 1;
            var negative = exponentStart < value.Length && value[exponentStart] == '-';
            if (negative || exponentStart < value.Length && value[exponentStart] == '+')
            {
                exponentStart++;
            }

            while (exponentStart < value.Length - 1 && value[exponentStart] == '0')
            {
                exponentStart++;
            }

            return value.Substring(0, exponentIndex)
                + "e"
                + (negative ? "-" : string.Empty)
                + value.Substring(exponentStart);
        }
    }

    internal static class CanonicalBytes
    {
        internal static int Compare(byte[] left, byte[] right)
        {
            var length = Math.Min(left.Length, right.Length);
            for (var index = 0; index < length; index++)
            {
                var comparison = left[index].CompareTo(right[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return left.Length.CompareTo(right.Length);
        }

        internal static bool Equals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }

        internal static int GetDeterministicHashCode(byte[] bytes)
        {
            unchecked
            {
                var hash = (uint)2166136261;
                for (var index = 0; index < bytes.Length; index++)
                {
                    hash = (hash ^ bytes[index]) * 16777619;
                }

                return (int)hash;
            }
        }
    }
}
