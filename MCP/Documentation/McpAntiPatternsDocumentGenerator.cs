using System.Text;

namespace AIBT.Mcp.Documentation
{
    /// <summary>
    /// Generates the anti-patterns document. Every entry is grounded in a real, already-disclosed
    /// limitation from a specific card's own evidence -- never a hypothetical -- so this document
    /// cannot silently drift into overclaiming or underclaiming a real capability.
    /// </summary>
    internal static class McpAntiPatternsDocumentGenerator
    {
        internal static string Generate()
        {
            var builder = new StringBuilder();
            builder.Append("# AIBT MCP anti-patterns (generated)\n\n");
            builder.Append("Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command.\n\n");

            Entry(
                builder,
                "Don't expect `aibt_simulate` to inject events, completions, or drive resume/abort/step-budget.",
                "The driver assigns `updateId`/`snapshotRevision` itself sequentially starting at 1 and exposes no injection API at all -- only plain `update` steps are supported. A step requesting anything else is rejected with a structured diagnostic (`P6-007`; widening this is `P6-013`, still `Draft`).");

            Entry(
                builder,
                "Don't expect a trace or compare-trace MCP tool.",
                "No production code anywhere wires a real running native tree's lifecycle into a trace channel yet -- `P6-008` found this and spun the question off into `P6-015` (still `Draft`) rather than build against a false premise.");

            Entry(
                builder,
                "Don't assume a fixed revision increment for a domain patch.",
                "`TreeDocument.Revision` is never persisted to `*.aibt.json`; every MCP call reloads a tree fresh from disk. Mutating calls are checked against a computed content hash instead (`ADR-P6-002`). Always use the `contentHash`/`expectedHash` the last *accepted* call actually returned, never a value you computed or assumed.");

            Entry(
                builder,
                "Don't request Agent or Shared blackboard scope over MCP.",
                "`aibt_set_blackboard_keys` only supports tree-scoped, built-in-scalar-typed keys today. The real project policy (`ReferenceCompilationPolicy.Phase1`) has `SupportsAgentScope`/`SupportsSharedScope` both `false` in production, so a request for either scope is rejected (`P6-006`'s disclosed finding; deciding whether to support this is `P6-014`, still `Draft`).");

            return builder.ToString();
        }

        private static void Entry(StringBuilder builder, string title, string body)
        {
            builder.Append("## ").Append(title).Append("\n\n").Append(body).Append("\n\n");
        }
    }
}
