using System.Collections.Generic;
using System.Text;
using AIBT.Authoring;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.Documentation
{
    /// <summary>
    /// Generates the node-catalog document straight from P6-003's own query layer -- every node's
    /// section embeds the exact, unmodified NodeCatalogQuery.TryGetContract JObject verbatim, so
    /// "matches P6-003's output field for field" is true by construction, not by parallel
    /// maintenance of a second hand-written description.
    /// </summary>
    internal static class McpNodeCatalogDocumentGenerator
    {
        internal static string Generate(NodeRegistry registry)
        {
            var query = new NodeCatalogQuery(registry);
            var entries = query.Search(string.Empty);

            var builder = new StringBuilder();
            builder.Append("# AIBT node catalog (generated)\n\n");
            builder.Append("Source: the real AIBT node registry, via `AIBT.Authoring.NodeCatalogQuery` (P6-003). ");
            builder.Append("Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command. ");
            builder.Append("Do not hand-edit -- edits are overwritten on the next regeneration.\n\n");
            builder.Append(entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(" registered node type(s).\n");

            for (var index = 0; index < entries.Count; index++)
            {
                var manifest = entries[index].Manifest;
                query.TryGetContract(manifest.TypeId, out var contract);

                builder.Append("\n---\n\n");
                builder.Append("### `");
                builder.Append(manifest.TypeId);
                builder.Append("` (v");
                builder.Append(manifest.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
                builder.Append(")\n\n");
                AppendField(builder, "Summary", manifest.Summary);
                AppendField(builder, "Category", manifest.Category);
                AppendField(builder, "Kind", manifest.Kind.ToString());
                AppendField(builder, "When to use", manifest.WhenToUse);
                AppendField(builder, "When not to use", manifest.WhenNotToUse);
                AppendField(builder, "Execution domain", manifest.ExecutionDomain.ToString());
                AppendField(builder, "Deterministic", manifest.Deterministic.ToString());
                AppendField(builder, "Cancellation", manifest.Cancellation.ToString());
                AppendField(builder, "Cost hint", manifest.CostHint.ToString());

                builder.Append("\nFull contract (verbatim `get_node_contract` output):\n\n```json\n");
                builder.Append(FormatJson(contract));
                builder.Append("\n```\n");
            }

            return builder.ToString();
        }

        private static void AppendField(StringBuilder builder, string label, string value)
        {
            builder.Append("- **");
            builder.Append(label);
            builder.Append(":** ");
            builder.Append(value);
            builder.Append('\n');
        }

        private static string FormatJson(JObject value)
        {
            // Newtonsoft's Formatting.Indented embeds Environment.NewLine, which is platform-
            // dependent (\r\n on Windows) -- found live by P6-012's detached-harness gate: a fresh
            // Windows regeneration produced \r\n inside this block while the git-checked-out
            // committed file (normalized to \n by .gitattributes' eol=lf) did not, so the two never
            // matched. Normalized explicitly so this document's line endings are always \n,
            // independent of the host OS, matching every other generator in this file.
            return value.ToString(Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n");
        }
    }
}
