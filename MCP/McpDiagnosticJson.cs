using AIBT.Authoring;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp
{
    /// <summary>
    /// The one diagnostic-collection-to-JSON writer every MCP tool group uses -- reuses the real
    /// canonical per-diagnostic serializer (<see cref="DiagnosticJson.Serialize"/>, named by
    /// diagnostics-v1.md itself), never a second hand-rolled shape. Extracted from
    /// <c>MCP/Verification/McpVerificationJson.cs</c> (P6-007) so <c>MCP/Authoring/</c> (P6-006)
    /// stops maintaining its own field-dropping copy, and so neither tool group's folder is the
    /// one every other card has to depend on for this.
    /// </summary>
    internal static class McpDiagnosticJson
    {
        /// <summary>
        /// Serializes a whole collection using the real canonical per-diagnostic writer. Each
        /// diagnostic is written once via the canonical writer, then re-parsed into a JObject so
        /// multiple diagnostics can be collected into one JSON array; the resulting bytes for each
        /// entry are exactly what the canonical writer alone would have produced.
        /// </summary>
        internal static JArray WriteDiagnostics(DiagnosticCollection diagnostics)
        {
            var array = new JArray();
            foreach (var diagnostic in diagnostics)
            {
                var json = DiagnosticJson.Serialize(new AuthoringDiagnostic(diagnostic));
                array.Add(JObject.Parse(json));
            }

            return array;
        }
    }
}
