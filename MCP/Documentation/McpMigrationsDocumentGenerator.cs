namespace AIBT.Mcp.Documentation
{
    /// <summary>
    /// Generates the migrations document. Two genuinely different concepts share this one file, per
    /// `P7-006`'s own decision not to invent a second document: MCP-surface migrations (a tool
    /// renamed/removed -- nothing has broken since `P6-005`, so that section stays near-empty) and,
    /// since `P7-006`, node-contract migrations (a node's own parameter contract changing version --
    /// see `ADR-P7-005`/`Authoring/Migration/`).
    /// </summary>
    internal static class McpMigrationsDocumentGenerator
    {
        internal static string Generate()
        {
            return
                "# AIBT migrations (generated)\n\n" +
                "Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command (the format/scope " +
                "text in this file is static; only the two dynamic parts below change as real migrations are " +
                "authored -- MCP-surface entries by hand, node-contract rules as they are registered in " +
                "`Authoring/Migration/`).\n\n" +
                "## MCP surface migrations\n\n" +
                "One entry per MCP-surface-breaking change (a tool renamed/removed, a required argument added, " +
                "an output field's meaning changed), newest first:\n\n" +
                "```text\n" +
                "## <version> (<date, format YYYY-MM-DD>)\n\n" +
                "- **What changed:** <one sentence>\n" +
                "- **Why:** <one sentence>\n" +
                "- **Migration:** <the concrete steps an existing agent/integration must take>\n" +
                "```\n\n" +
                "### Entries\n\n" +
                "## Unreleased (shipped in `P7-009`)\n\n" +
                "- **What changed:** `test-node`'s response dropped its always-present `scopeNote` field.\n" +
                "- **Why:** `P7-009` widened the tool to actually drive generated dispatch (in its own proven " +
                "scope) instead of only proving compiled metadata is structurally valid, replacing the static " +
                "`scopeNote` text with real per-call fields (`dispatchProven`, and on success " +
                "`enteredSuccessfully`/`tickStatus`/`tickCallbackFailure`, or on failure `dispatchReason`).\n" +
                "- **Migration:** an existing agent/integration must stop reading `scopeNote` and instead branch " +
                "on `dispatchProven` (a node outside the translator's proven scope -- a `Registered` field, or an " +
                "`AsyncOperation`/`Completion` binding -- reports `dispatchProven: false` with a `dispatchReason`, " +
                "never a false pass).\n\n" +
                "Found retroactively during `P7-016`'s gate review (a real change that was never logged here when " +
                "it shipped) -- no other MCP-surface-breaking change exists before or since.\n\n" +
                "## Node-contract migrations\n\n" +
                "When a node type's own authored-parameter contract changes (a field renamed, or added with a " +
                "default) and its manifest `Version` increments, `Authoring/Migration/DocumentMigrator` rewrites " +
                "an authored document's affected node **in memory only** the next time it is validated or " +
                "compiled (via `aibt_validate`/`aibt_compile`, or the Editor equivalents) -- the document keeps " +
                "working, and a structured, non-blocking `AIBT2042` (Info) diagnostic names exactly what changed. " +
                "The on-disk file is never rewritten as a side effect; persist the fix explicitly with " +
                "`aibt_migrate_document`, or via the `AIBT/Migration Notifications` Editor window. A version gap " +
                "with no registered rule (e.g. a removed field, or a type change) is left untouched and still " +
                "fails validation/compilation with the existing `UnsupportedNodeVersion` diagnostic -- migration " +
                "never guesses. See `Documentation~/decisions/ADR-P7-005-migration-tooling.md` for the full " +
                "decision and scope.\n\n" +
                "### Registered rules\n\n" +
                "None yet -- no node type in this project has ever had its contract version bumped.\n";
        }
    }
}
