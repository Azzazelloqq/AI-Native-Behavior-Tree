using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AIBT.Authoring;

namespace AIBT.Editor.Graph
{
    /// <summary>One-line text for a node card: the explicitly authored parameters, in contract order.</summary>
    internal static class NodeParameterSummary
    {
        internal const string Separator = " · ";

        internal static string Describe(NodeDocument node, NodeManifest manifest)
        {
            var parts = new List<string>();
            var named = new HashSet<string>();
            if (manifest != null)
            {
                foreach (var parameter in manifest.Parameters)
                {
                    named.Add(parameter.Name);
                    if (node.Parameters.TryGetValue(parameter.Name, out var value)) parts.Add(parameter.Name + ": " + Format(value));
                }
            }

            foreach (var property in node.Parameters.Properties.Where(property => !named.Contains(property.Name)))
                parts.Add(property.Name + ": " + Format(property.Value));
            return string.Join(Separator, parts);
        }

        internal static string Format(SemanticValue value)
        {
            if (value == null) return "null";
            if (value.TryGetBoolean(out var flag)) return flag ? "true" : "false";
            if (value.TryGetInt64(out var signed)) return signed.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetUInt64(out var unsigned)) return unsigned.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetNumber(out var number)) return number.ToString("0.###", CultureInfo.InvariantCulture);
            if (value.TryGetString(out var text)) return text;
            if (value.TryGetArray(out var items)) return "[" + items.Count + "]";
            if (value.TryGetObject(out _)) return "{…}";
            return "null";
        }
    }
}
