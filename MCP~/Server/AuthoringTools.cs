using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace AibtMcpServer;

// Thin relays to AIBT.Mcp.Authoring.McpAuthoringToolDispatcher (P6-006) over the bridge TCP
// connection, mirroring DiscoveryTools.cs's exact shape. No logic of any kind lives here. Nested
// payloads (a node definition, a domain-patch operation list, an arbitrary parameter value) are
// accepted as raw JSON text and forwarded as structured JSON -- simpler and more inspectable via
// a real MCP client than relying on the SDK's own POCO-to-schema generation for this shape of
// deeply nested, tool-specific payload. insertIndex uses -1 to mean "append" (omitted from the
// forwarded args, matching how every insertIndex-accepting bridge handler already treats a
// missing value). Every mutating tool takes expectedHash, not a revision counter: TreeDocument's
// Revision field is never persisted to *.aibt.json (confirmed by reading CanonicalTreeJsonWriter
// and CanonicalTreeJson.ReadDocument directly, then proving it live -- every reload resets it to
// 1), so a stateless, reload-per-call MCP tool cannot use it for concurrency detection. The
// bridge instead checks a computed canonical content hash before ever calling into P6-004's
// transaction engine, the same fix ADR-P6-002 already made for LayoutDocument.
[McpServerToolType]
public static class AuthoringTools
{
    [McpServerTool(Name = "aibt_create_tree")]
    [Description("Creates a new AIBT tree file with a single root node. Fails without creating anything if the root node's type does not compile.")]
    public static string CreateTree(
        [Description("New tree's stable ID.")] string treeId,
        [Description("Human-readable tree name.")] string name,
        [Description("Relative path under the project's Assets folder, ending in '.aibt.json'. Must not already exist.")] string path,
        [Description("Root node definition JSON: {id, typeId, typeVersion, parameters?, displayName?, description?, tags?}.")] string rootNodeJson,
        [Description("Optional initial tree-scoped blackboard keys JSON array: [{id, valueType, description?}, ...].")] string blackboardJson = "",
        [Description("Optional tree description.")] string description = "",
        [Description("If true, validates and reports the result without creating the file.")] bool dryRun = false)
    {
        var args = new JsonObject
        {
            ["treeId"] = treeId,
            ["name"] = name,
            ["path"] = path,
            ["rootNode"] = JsonNode.Parse(rootNodeJson),
            ["dryRun"] = dryRun,
        };
        if (blackboardJson.Length > 0) args["blackboard"] = JsonNode.Parse(blackboardJson);
        if (description.Length > 0) args["description"] = description;
        return BridgeClient.SendRequest("create_tree", args);
    }

    [McpServerTool(Name = "aibt_add_node")]
    [Description("Adds a node under a parent (atomic add+connect). Rejected (nothing changed) if the resulting tree does not compile.")]
    public static string AddNode(
        [Description("Target tree ID.")] string treeId,
        [Description(ExpectedHashDescription)] string expectedHash,
        [Description("Existing parent node's ID.")] string parentId,
        [Description("New node definition JSON: {id, typeId, typeVersion, parameters?, displayName?, description?, tags?}.")] string nodeJson,
        [Description("Child insertion index; -1 appends.")] int insertIndex = -1,
        [Description("If true, validates and reports the result without persisting it.")] bool dryRun = false)
    {
        var args = BaseArgs(treeId, expectedHash, dryRun);
        args["parentId"] = parentId;
        args["node"] = JsonNode.Parse(nodeJson);
        if (insertIndex >= 0) args["insertIndex"] = insertIndex;
        return BridgeClient.SendRequest("add_node", args);
    }

    [McpServerTool(Name = "aibt_remove_node")]
    [Description("Removes a node and its entire subtree. Rejected (nothing changed) if the resulting tree does not compile.")]
    public static string RemoveNode(
        [Description("Target tree ID.")] string treeId,
        [Description(ExpectedHashDescription)] string expectedHash,
        [Description("Node to remove (its whole subtree is removed too; the root node cannot be removed).")] string nodeId,
        [Description("If true, validates and reports the result without persisting it.")] bool dryRun = false)
    {
        var args = BaseArgs(treeId, expectedHash, dryRun);
        args["nodeId"] = nodeId;
        return BridgeClient.SendRequest("remove_node", args);
    }

    [McpServerTool(Name = "aibt_move_node")]
    [Description("Moves a node (and its subtree) under a new parent, resolving its current parent automatically. Rejected (nothing changed) if the resulting tree does not compile.")]
    public static string MoveNode(
        [Description("Target tree ID.")] string treeId,
        [Description(ExpectedHashDescription)] string expectedHash,
        [Description("Node to move.")] string nodeId,
        [Description("New parent node's ID.")] string newParentId,
        [Description("Child insertion index under the new parent; -1 appends.")] int insertIndex = -1,
        [Description("If true, validates and reports the result without persisting it.")] bool dryRun = false)
    {
        var args = BaseArgs(treeId, expectedHash, dryRun);
        args["nodeId"] = nodeId;
        args["newParentId"] = newParentId;
        if (insertIndex >= 0) args["insertIndex"] = insertIndex;
        return BridgeClient.SendRequest("move_node", args);
    }

    [McpServerTool(Name = "aibt_replace_node")]
    [Description("Swaps a node's type/version/parameters in place, keeping its ID and children. Rejected (nothing changed) if the resulting tree does not compile.")]
    public static string ReplaceNode(
        [Description("Target tree ID.")] string treeId,
        [Description(ExpectedHashDescription)] string expectedHash,
        [Description("Node to replace.")] string nodeId,
        [Description("New node type ID.")] string newTypeId,
        [Description("New node type version.")] int newTypeVersion = 1,
        [Description("Optional new parameters JSON object.")] string newParametersJson = "",
        [Description("If true, validates and reports the result without persisting it.")] bool dryRun = false)
    {
        var args = BaseArgs(treeId, expectedHash, dryRun);
        args["nodeId"] = nodeId;
        args["newTypeId"] = newTypeId;
        args["newTypeVersion"] = newTypeVersion;
        if (newParametersJson.Length > 0) args["newParameters"] = JsonNode.Parse(newParametersJson);
        return BridgeClient.SendRequest("replace_node", args);
    }

    [McpServerTool(Name = "aibt_configure_node")]
    [Description("Sets (adds or overwrites) one parameter on a node. Rejected (nothing changed) if the resulting tree does not compile.")]
    public static string ConfigureNode(
        [Description("Target tree ID.")] string treeId,
        [Description(ExpectedHashDescription)] string expectedHash,
        [Description("Node to configure.")] string nodeId,
        [Description("Parameter name.")] string parameterName,
        [Description("Parameter value as raw JSON (string, number, boolean, null, array, or object).")] string valueJson,
        [Description("If true, validates and reports the result without persisting it.")] bool dryRun = false)
    {
        var args = BaseArgs(treeId, expectedHash, dryRun);
        args["nodeId"] = nodeId;
        args["parameterName"] = parameterName;
        args["value"] = JsonNode.Parse(valueJson);
        return BridgeClient.SendRequest("configure_node", args);
    }

    [McpServerTool(Name = "aibt_set_blackboard_keys")]
    [Description("Replaces the tree's full blackboard-key declaration list (tree-scoped built-in scalar types only). Rejected (nothing changed) if the resulting tree does not compile.")]
    public static string SetBlackboardKeys(
        [Description("Target tree ID.")] string treeId,
        [Description(ExpectedHashDescription)] string expectedHash,
        [Description("Full replacement blackboard-key list JSON array: [{id, valueType, description?}, ...].")] string keysJson,
        [Description("If true, validates and reports the result without persisting it.")] bool dryRun = false)
    {
        var args = BaseArgs(treeId, expectedHash, dryRun);
        args["keys"] = JsonNode.Parse(keysJson);
        return BridgeClient.SendRequest("set_blackboard_keys", args);
    }

    [McpServerTool(Name = "aibt_extract_subtree")]
    [Description("Removes a subtree from the tree and returns its node definitions plus its original attachment point, for a later aibt_inline_subtree call (on this or another tree). Rejected (nothing changed) if the resulting tree does not compile.")]
    public static string ExtractSubtree(
        [Description("Target tree ID.")] string treeId,
        [Description(ExpectedHashDescription)] string expectedHash,
        [Description("Subtree root node to extract.")] string nodeId,
        [Description("If true, validates and reports the result without persisting it.")] bool dryRun = false)
    {
        var args = BaseArgs(treeId, expectedHash, dryRun);
        args["nodeId"] = nodeId;
        return BridgeClient.SendRequest("extract_subtree", args);
    }

    [McpServerTool(Name = "aibt_inline_subtree")]
    [Description("Splices a previously extracted (or otherwise supplied) set of nodes back into a tree as a unit under a parent. Rejected (nothing changed) if the resulting tree does not compile.")]
    public static string InlineSubtree(
        [Description("Target tree ID.")] string treeId,
        [Description(ExpectedHashDescription)] string expectedHash,
        [Description("Node definitions JSON array, as returned by aibt_extract_subtree's extractedNodes.")] string nodesJson,
        [Description("The root node ID among nodesJson to attach under parentId.")] string subtreeRootId,
        [Description("Existing parent node's ID to attach under.")] string parentId,
        [Description("Child insertion index; -1 appends.")] int insertIndex = -1,
        [Description("If true, validates and reports the result without persisting it.")] bool dryRun = false)
    {
        var args = BaseArgs(treeId, expectedHash, dryRun);
        args["nodes"] = JsonNode.Parse(nodesJson);
        args["subtreeRootId"] = subtreeRootId;
        args["parentId"] = parentId;
        if (insertIndex >= 0) args["insertIndex"] = insertIndex;
        return BridgeClient.SendRequest("inline_subtree", args);
    }

    [McpServerTool(Name = "aibt_apply_domain_patch")]
    [Description("Applies an ordered list of authoring operations (add/remove/move/replace/configure/setBlackboard) as one atomic patch. Rejected (nothing changed) if any operation is invalid or the resulting tree does not compile.")]
    public static string ApplyDomainPatch(
        [Description("Target tree ID.")] string treeId,
        [Description(ExpectedHashDescription)] string expectedHash,
        [Description("Ordered operations JSON array. Each entry: {op: 'add'|'remove'|'move'|'replace'|'configure'|'setBlackboard', ...op-specific fields, matching the corresponding single-operation tool's arguments}.")] string operationsJson,
        [Description("If true, validates and reports the result without persisting it.")] bool dryRun = false)
    {
        var args = BaseArgs(treeId, expectedHash, dryRun);
        args["operations"] = JsonNode.Parse(operationsJson);
        return BridgeClient.SendRequest("apply_domain_patch", args);
    }

    [McpServerTool(Name = "aibt_request_layout")]
    [Description("Computes/persists a deterministic auto-layout for a tree, placing only nodes absent from the existing *.aibt.layout.json (the affected region) and leaving every already-placed node's position untouched.")]
    public static string RequestLayout(
        [Description("Target tree ID.")] string treeId,
        [Description("The current layout's content hash. Omit only when no *.aibt.layout.json exists yet for this tree.")] string expectedHash = "",
        [Description("If true, computes and reports the result without persisting it.")] bool dryRun = false)
    {
        var args = new JsonObject { ["treeId"] = treeId, ["dryRun"] = dryRun };
        if (expectedHash.Length > 0) args["expectedHash"] = expectedHash;
        return BridgeClient.SendRequest("request_layout", args);
    }

    private const string ExpectedHashDescription =
        "The document's current semantic content hash, from the last accepted call's own contentHash (aibt_create_tree's included) -- must match exactly or the call is rejected.";

    private static JsonObject BaseArgs(string treeId, string expectedHash, bool dryRun)
    {
        return new JsonObject { ["treeId"] = treeId, ["expectedHash"] = expectedHash, ["dryRun"] = dryRun };
    }
}
