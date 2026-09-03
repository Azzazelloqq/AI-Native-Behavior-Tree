using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace AibtMcpServer;

// Thin relay to AIBT.Mcp.Migration.McpMigrationToolDispatcher (P7-006), mirroring
// VerificationTools.cs/AuthoringTools.cs's exact shape. No logic of any kind lives here.
[McpServerToolType]
public static class MigrationTools
{
    [McpServerTool(Name = "aibt_migrate_document")]
    [Description("Persists to disk the same in-memory node-contract migration aibt_validate/aibt_compile already apply transparently on every call (ADR-P7-005): renamed/added-with-default fields only, for any node whose authored parameter version is behind the currently-registered manifest version. Never mutates anything if dryRun is true, or if no node in the document needed migrating. A version gap with no registered migration rule (e.g. a removed field) is left untouched here too -- it still fails aibt_validate/aibt_compile with the existing UnsupportedNodeVersion diagnostic, never silently guessed.")]
    public static string MigrateDocument(
        [Description("Target tree ID.")] string treeId,
        [Description("If true, reports what would change without writing the file.")] bool dryRun = false)
        => BridgeClient.SendRequest("migrate_document", new JsonObject { ["treeId"] = treeId, ["dryRun"] = dryRun });
}
