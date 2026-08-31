namespace AIBT.Mcp.Documentation
{
    /// <summary>
    /// Generates the migrations document stub. Nothing has broken the MCP surface since P6-005, so
    /// this is near-empty at 0.x per the card's own deliverable -- but the format itself (one
    /// versioned entry per MCP-surface-breaking change, newest first) is the real deliverable, so a
    /// later phase can append to it rather than inventing a new shape.
    /// </summary>
    internal static class McpMigrationsDocumentGenerator
    {
        internal static string Generate()
        {
            return
                "# AIBT MCP surface migrations (generated)\n\n" +
                "Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command (this section is " +
                "static; append real entries to the section below by hand as breaking changes ship, then " +
                "regenerate to refresh the rest of the generated documentation set).\n\n" +
                "## Format\n\n" +
                "One entry per MCP-surface-breaking change (a tool renamed/removed, a required argument added, " +
                "an output field's meaning changed), newest first:\n\n" +
                "```text\n" +
                "## <version> (<date, format YYYY-MM-DD>)\n\n" +
                "- **What changed:** <one sentence>\n" +
                "- **Why:** <one sentence>\n" +
                "- **Migration:** <the concrete steps an existing agent/integration must take>\n" +
                "```\n\n" +
                "## Entries\n\n" +
                "None yet -- the MCP surface (`P6-005` through `P6-010`) has had no breaking change since its " +
                "first release.\n";
        }
    }
}
