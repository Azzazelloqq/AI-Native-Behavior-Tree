using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace AibtMcpServer;

// Thin relays to AIBT.Mcp.NodeDevelopment.McpNodeDevelopmentToolDispatcher (P6-009) over the
// bridge TCP connection, mirroring TestingTools.cs/VerificationTools.cs's exact shape. No logic
// of any kind lives here.
[McpServerToolType]
public static class NodeDevelopmentTools
{
    [McpServerTool(Name = "aibt_generate_node")]
    [Description("Scaffolds a new Burst node from one of the two maintained templates (condition: typed blackboard read, optional observer; action: typed read/write, command emission, async start/completion, cancellation), writing it into a single reserved staging slot (overwriting any prior pending generation). Never touches the real project until aibt_apply_node.")]
    public static string GenerateNode(
        [Description("'condition' or 'action'.")] string kind,
        [Description("Canonical node type ID, e.g. 'aibt.myproject.my-condition'.")] string typeId,
        [Description("C# namespace for the generated source.")] string ns,
        [Description("Human summary for AibtNodeDocumentation.")] string summary,
        [Description("Documentation category, e.g. 'MyProject/Conditions'.")] string category,
        [Description("When to use this node.")] string whenToUse,
        [Description("When not to use this node.")] string whenNotToUse,
        [Description("Blackboard key to read (both kinds).")] string blackboardReadKey,
        [Description("Blackboard read value type: Bool, Int32, UInt32, Float32, or Float64.")] string blackboardReadType,
        [Description("Node version (default 1).")] int? version = null,
        [Description("Observer-condition support (condition only, default false).")] bool? asObserverCondition = null,
        [Description("Blackboard key to write (action only).")] string? blackboardWriteKey = null,
        [Description("Blackboard write value type (action only).")] string? blackboardWriteType = null,
        [Description("Command key to emit (action only).")] string? commandKey = null,
        [Description("Command payload type (action only).")] string? commandType = null,
        [Description("Async operation key (action only).")] string? asyncOperationKey = null,
        [Description("Async start payload type (action only).")] string? asyncStartType = null,
        [Description("Async completion payload type (action only).")] string? asyncCompletionType = null,
        [Description("Completion key (action only).")] string? completionKey = null,
        [Description("Shard ID (default '<typeId>-shard').")] string? shardId = null)
    {
        var args = new JsonObject
        {
            ["kind"] = kind,
            ["typeId"] = typeId,
            ["namespace"] = ns,
            ["summary"] = summary,
            ["category"] = category,
            ["whenToUse"] = whenToUse,
            ["whenNotToUse"] = whenNotToUse,
            ["blackboardReadKey"] = blackboardReadKey,
            ["blackboardReadType"] = blackboardReadType,
        };
        if (version.HasValue) args["version"] = version.Value;
        if (asObserverCondition.HasValue) args["asObserverCondition"] = asObserverCondition.Value;
        if (blackboardWriteKey != null) args["blackboardWriteKey"] = blackboardWriteKey;
        if (blackboardWriteType != null) args["blackboardWriteType"] = blackboardWriteType;
        if (commandKey != null) args["commandKey"] = commandKey;
        if (commandType != null) args["commandType"] = commandType;
        if (asyncOperationKey != null) args["asyncOperationKey"] = asyncOperationKey;
        if (asyncStartType != null) args["asyncStartType"] = asyncStartType;
        if (asyncCompletionType != null) args["asyncCompletionType"] = asyncCompletionType;
        if (completionKey != null) args["completionKey"] = completionKey;
        if (shardId != null) args["shardId"] = shardId;
        return BridgeClient.SendRequest("generate_node", args);
    }

    [McpServerTool(Name = "aibt_preview_node_diff")]
    [Description("Returns the exact staged file content the pending generate-node call produced -- never mutates anything, never compiles anything.")]
    public static string PreviewNodeDiff()
        => BridgeClient.SendRequest("preview_node_diff", new JsonObject());

    [McpServerTool(Name = "aibt_generate_node_tests_and_manifest")]
    [Description("Generates a paired test scaffold for the currently-staged node into the same staging slot. Manifest data needs no separate artifact -- it is inherent in generate-node's own attributes. The scaffold is an honest placeholder pending P6-022's generic native-dispatch test harness, not a fake working test.")]
    public static string GenerateNodeTestsAndManifest()
        => BridgeClient.SendRequest("generate_node_tests_and_manifest", new JsonObject());

    [McpServerTool(Name = "aibt_analyze_and_compile_node")]
    [Description("Non-blocking compilation: mode='start' captures staged content and requests a new compilation, returning attemptId. Poll mode='check' with that attemptId while status is 'pending' or 'still-compiling'. The attempt survives domain reload; 'compiled' returns contentHash for test/apply, and 'failed' returns diagnostics. Changed staging, superseded attempts or an Editor restart require a fresh start. Log offsets are not accepted as compilation proof.")]
    public static string AnalyzeAndCompileNode(
        [Description("'start' or 'check'.")] string mode,
        [Description("The attemptId returned by start (required for check).")] string? attemptId = null)
    {
        var args = new JsonObject { ["mode"] = mode };
        if (attemptId != null) args["attemptId"] = attemptId;
        return BridgeClient.SendRequest("analyze_and_compile_node", args);
    }

    [McpServerTool(Name = "aibt_test_node")]
    [Description("Checks the staged node's compiled metadata and registry materialization. dispatchProven reports native dispatch verification for supported binding types; unsupported bindings return a reason. Requires the contentHash from a successful analyze-and-compile check; changed staged content is rejected.")]
    public static string TestNode(
        [Description("contentHash from the last successful aibt_analyze_and_compile_node check.")] string expectedContentHash)
        => BridgeClient.SendRequest("test_node", new JsonObject { ["expectedContentHash"] = expectedContentHash });

    [McpServerTool(Name = "aibt_apply_node")]
    [Description("The only step that persists a generated node into the real project. Re-verifies the staged content's hash and re-runs the compile-clean/registry-valid check itself immediately before moving the files -- never trusts a prior claim or session state. Rejected with a structured diagnostic if the recheck fails.")]
    public static string ApplyNode(
        [Description("contentHash from the last successful aibt_analyze_and_compile_node check.")] string expectedContentHash,
        [Description("Destination folder relative to Assets, e.g. 'MyProject/GeneratedNodes/MyCondition'. Must not already exist. Rooted paths, escapes outside Assets and link/reparse ancestry are rejected.")] string destinationPath)
        => BridgeClient.SendRequest("apply_node", new JsonObject { ["expectedContentHash"] = expectedContentHash, ["destinationPath"] = destinationPath });
}
