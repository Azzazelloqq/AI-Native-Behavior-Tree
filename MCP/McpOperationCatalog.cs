using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Mcp.Authoring;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp
{
    internal static class McpOperationCatalog
    {
        private static readonly IReadOnlyDictionary<string, JObject> Operations = Build();

        internal static JObject List() => new JObject { ["formatVersion"] = 1, ["operations"] = new JArray(Operations.Values.Select(value => value.DeepClone())) };

        internal static JObject Describe(string operation)
        {
            if (operation != null && operation.StartsWith("aibt_", StringComparison.Ordinal)) operation = operation.Substring(5);
            if (operation == null || !Operations.TryGetValue(operation, out var schema))
                throw new McpToolException(McpDiagnostics.UnknownTool, "Unknown operation. Use list_operations.");
            return (JObject)schema.DeepClone();
        }

        private static IReadOnlyDictionary<string, JObject> Build()
        {
            var result = new SortedDictionary<string, JObject>(StringComparer.Ordinal);
            void Add(string name, string permission, string fields, string description, string sideEffect = "none")
            {
                var properties = new JObject();
                var required = new JArray();
                foreach (var specification in fields.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = specification.TrimStart('?').Split(':');
                    properties[parts[0]] = new JObject { ["type"] = parts[1] };
                    if (specification[0] != '?') required.Add(parts[0]);
                }
                result.Add(name, new JObject
                {
                    ["name"] = name, ["permission"] = permission, ["description"] = description, ["sideEffect"] = sideEffect,
                    ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = true },
                });
            }
            Add("list_operations", "Read", "", "List shared CLI/MCP operation contracts.");
            Add("get_operation_schema", "Read", "operation:string", "Read one bridge argument schema. Optional fields start absent, not null.");
            Add("get_project_manifest", "Read", "", "Project policy/capabilities and trees.");
            Add("search_nodes", "Read", "?keyword:string ?offset:integer ?count:integer", "Search current project node contracts.");
            Add("get_node_contract", "Read", "typeId:string", "Read one node's manifest.");
            Add("get_static_resource", "Read", "key:string", "Read allowlisted schemas/docs; node-catalog reflects current project contracts.");
            Add("inspect_tree", "Read", "treeId:string ?expanded:boolean", "Read authored tree, current semantic contentHash and reference availability. expanded=true adds the subtree expansion the compiler sees (ADR-P11-001).");
            Add("runtime_catalogs", "Read", "", "List generated Player catalog identities for native preparation.");
            Add("prepare_runtime", "Compilation", "treeId:string expectedHash:string catalog:string outputPath:string ?dryRun:boolean ?overwrite:boolean ?expectedAssetHash:string", "Prepare a native v2 runtime asset. outputPath starts Assets/. Dry-run reports existingAssetHash. Overwrite requires that hash; GUID is preserved.", "derived-runtime-asset");
            Add("runtime_hosts", "Read", "", "List scene hosts, instance/program identity and terminal state.");
            Add("read_trace", "Read", "instanceId:integer ?afterSequence:integer ?limit:integer", "Read native trace without completing jobs. Limit 1..512; busy returns available=false. complete=false indicates dropped/faulted history.");
            Add("runtime_simulations", "Read", "", "List simulation containers the game registered (TreeSimulationDebug.Register), their groups, agent counts and traced agents. Empty with a hint when none is registered.");
            Add("describe_agent", "Read", "simulation:string group:integer agent:integer ?generation:integer", "Why is a container agent doing what it does: state, active path with node names, root result, fault, last/next think and why, live actions, readable text. Busy during a step.");
            Add("read_agent_trace", "Read", "simulation:string group:integer agent:integer ?generation:integer ?afterSequence:integer ?limit:integer", "Read a traced container agent's trace: lifecycle records plus think reasons, inputs, commands, action events and faults, with ticks. Limit 1..512; complete=false when records were dropped. generation reads a removed agent's kept trace.");
            Add("create_tree", "SemanticEdit", "treeId:string name:string path:string rootNode:object ?blackboard:array ?description:string ?formatVersion:integer ?dryRun:boolean", "Create an atomic valid tree. Native roots imply v2; use formatVersion=2 for subsequent native authoring.", "semantic-source");
            result["create_tree"]["inputSchema"]["properties"]["formatVersion"]["enum"] = new JArray(1, 2);
            Add("add_node", "SemanticEdit", "treeId:string expectedHash:string parentId:string node:object ?insertIndex:integer ?dryRun:boolean ?connectSameNamedKeys:boolean", "Add and connect one node atomically. A node with subtree {treeId, interfaceVersion?, keys?} is a subtree reference (format 3); unmentioned interface keys connect to same-named parent keys unless connectSameNamedKeys=false.", "semantic-source");
            Add("remove_node", "SemanticEdit", "treeId:string expectedHash:string nodeId:string ?dryRun:boolean", "Remove a node branch.", "semantic-source");
            Add("move_node", "SemanticEdit", "treeId:string expectedHash:string nodeId:string newParentId:string ?insertIndex:integer ?dryRun:boolean", "Reparent a node.", "semantic-source");
            Add("replace_node", "SemanticEdit", "treeId:string expectedHash:string nodeId:string newTypeId:string ?newTypeVersion:integer ?newParameters:object ?dryRun:boolean", "Replace node definition.", "semantic-source");
            Add("configure_node", "SemanticEdit", "treeId:string expectedHash:string nodeId:string parameterName:string value:object ?dryRun:boolean", "Set one semantic JSON value; value accepts any JSON type, including scalar/null.", "semantic-source");
            result["configure_node"]["inputSchema"]["properties"]["value"] = new JObject();
            Add("set_blackboard_keys", "SemanticEdit", "treeId:string expectedHash:string keys:array ?dryRun:boolean", "Replace built-in-scalar blackboard declarations. Agent/Shared require v2, defaults/contracts and project policy.", "semantic-source");
            Add("extract_subtree", "SemanticEdit", "treeId:string expectedHash:string nodeId:string ?dryRun:boolean", "Extract a subtree and return its payload.", "semantic-source");
            Add("inline_subtree", "SemanticEdit", "treeId:string expectedHash:string parentId:string subtreeRootId:string nodes:array ?insertIndex:integer ?dryRun:boolean", "Inline subtree payload.", "semantic-source");
            Add("set_subtree_keys", "SemanticEdit", "treeId:string expectedHash:string nodeId:string keys:object ?interfaceVersion:integer ?connectSameNamedKeys:boolean ?dryRun:boolean", "Rebind interface keys of a subtree reference: each key maps to a parent Tree key ID, {value: constant} or null (not connected). Accepted only if the tree still expands and compiles.", "semantic-source");
            result["set_subtree_keys"]["inputSchema"]["properties"]["keys"]["additionalProperties"] = new JObject { ["type"] = new JArray("string", "object", "null") };
            Add("extract_to_subtree", "SemanticEdit", "treeId:string expectedHash:string nodeId:string newTreeId:string path:string ?name:string ?description:string ?dryRun:boolean", "Move a node and its descendants into a new tree file (path under Assets) and put a subtree reference in its place; one transaction, no file written on failure. Not extract_subtree.", "semantic-source");
            Add("unpack_subtree", "SemanticEdit", "treeId:string expectedHash:string nodeId:string ?dryRun:boolean", "Replace a subtree reference by a plain renamed copy of its expansion (nodes r:n, private keys r:key). Not inline_subtree.", "semantic-source");
            Add("find_subtree_usages", "Read", "treeId:string", "List the reference nodes in the project's trees that use a tree ID, and the files declaring it.");
            Add("apply_domain_patch", "SemanticEdit", "treeId:string expectedHash:string operations:array ?dryRun:boolean", "Atomic ordered semantic operations: add/remove/move/replace/configure/setBlackboard.", "semantic-source");
            Add("request_layout", "LayoutEdit", "treeId:string ?expectedHash:string ?dryRun:boolean", "Produce/preview layout independently; expectedHash required when layout already exists.", "layout-source");
            Add("validate", "Compilation", "treeId:string", "Validate project contracts and policy; not execution proof.");
            Add("compile", "Compilation", "treeId:string", "Reference compile; native-only nodes require prepare_runtime.");
            Add("simulate", "TestExecution", "treeId:string steps:array", "Reference simulation only. Native-only nodes are not substituted.");
            Add("explain_diagnostic", "Read", "diagnostic:object", "Explain a supplied diagnostic including its code and optional suggested operation.");
            Add("run_tests", "TestExecution", "casePath:string", "Run a reference behavior-case fixture.");
            Add("run_benchmark", "BenchmarkExecution", "scenario:string agentCount:integer policy:string ?stepBudget:integer ?batchSize:integer", "Run an approved scheduling benchmark.");
            Add("generate_node", "CodeGeneration", "kind:string typeId:string namespace:string summary:string category:string whenToUse:string whenNotToUse:string blackboardReadKey:string blackboardReadType:string ?version:integer ?shardId:string ?shardVersion:integer ?asObserverCondition:boolean ?exampleKey:string ?blackboardWriteKey:string ?blackboardWriteType:string ?commandKey:string ?commandType:string ?asyncOperationKey:string ?asyncStartType:string ?asyncCompletionType:string ?completionKey:string", "Generate quarantined source. Action requires write/command/async/completion fields; condition requires read fields only.", "staging-source");
            Add("preview_node_diff", "Read", "", "Read staged files and content hash.");
            Add("generate_node_tests_and_manifest", "CodeGeneration", "?cases:array", "Generate compiled behavior-case data consumed by test_node. Condition templates provide contract-based pass/fail cases; other nodes require caller-authored cases.", "staging-source");
            Add("analyze_and_compile_node", "Compilation", "mode:string ?attemptId:string", "Start/check explicit staged compilation; reuse returned attemptId.", "staging-compilation");
            Add("test_node", "TestExecution", "expectedContentHash:string ?cases:array", "Exercise native dispatch; optional caller-authored behavior cases compare expected outcomes.");
            Add("apply_node", "CodeGeneration", "expectedContentHash:string destinationPath:string", "Apply verified staging to a new Assets-relative directory.", "project-source");
            Add("list_custom_tools", "Read", "", "Discover registered project tools.");
            Add("call_custom_tool", "provider-defined", "toolName:string ?args:object ?dryRun:boolean", "Invoke a project tool with its declared permission.", "provider-defined");
            Add("migrate_document", "SemanticEdit", "treeId:string ?dryRun:boolean", "Persist registered deterministic migrations.", "semantic-source");
            var behaviorCase = new JObject
            {
                ["type"] = "object", ["required"] = new JArray("expectedStatus"),
                ["properties"] = new JObject
                {
                    ["name"] = new JObject { ["type"] = "string" },
                    ["configuration"] = new JObject { ["type"] = "object", ["description"] = "Non-handle field IDs/types from behaviorCaseContract.configuration." },
                    ["inputs"] = new JObject { ["type"] = "object", ["description"] = "Binding IDs/types from behaviorCaseContract.inputs." },
                    ["expectedStatus"] = new JObject { ["type"] = "string", ["enum"] = new JArray("Success", "Failure", "Running") },
                    ["expectedCallbackFailure"] = new JObject { ["type"] = "string", ["default"] = "Success" },
                    ["expectedEffects"] = new JObject { ["type"] = "object", ["description"] = "Optional exact effect binding IDs/scalar values; empty object asserts no effects." },
                },
            };
            result["test_node"]["inputSchema"]["properties"]["cases"]["items"] = behaviorCase;
            result["generate_node_tests_and_manifest"]["inputSchema"]["properties"]["cases"]["items"] = behaviorCase.DeepClone();
            return result;
        }

        internal static void ValidateIfGranted(string operation, JObject args, ISet<McpPermissionCategory> granted)
        {
            if (!Operations.TryGetValue(operation ?? "", out var description)
                || !Enum.TryParse((string)description["permission"], out McpPermissionCategory permission) || !granted.Contains(permission)) return;
            var schema = (JObject)description["inputSchema"];
            foreach (var key in (JArray)schema["required"])
                if (args[(string)key] == null) throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, "Missing required property '" + key + "'. See get_operation_schema.");
            foreach (var property in ((JObject)schema["properties"]).Properties())
            {
                var value = args[property.Name];
                var type = (string)property.Value["type"];
                if (value == null || type == null) continue;
                var valid = type == "string" && value.Type == JTokenType.String || type == "object" && value.Type == JTokenType.Object
                    || type == "array" && value.Type == JTokenType.Array || type == "integer" && value.Type == JTokenType.Integer
                    || type == "boolean" && value.Type == JTokenType.Boolean;
                if (!valid) throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, "Property '" + property.Name + "' must be " + type + ".");
            }
        }
    }
}
