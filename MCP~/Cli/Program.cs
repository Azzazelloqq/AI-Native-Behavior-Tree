using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AibtTransport;

try
{
    var arguments = args.ToList();
    string? TakeOption(string name)
    {
        var index = arguments.IndexOf(name);
        if (index < 0) return null;
        if (index + 1 >= arguments.Count) throw new ArgumentException("Missing value for " + name);
        var value = arguments[index + 1];
        arguments.RemoveRange(index, 2);
        return value;
    }
    var project = TakeOption("--project") ?? Environment.GetEnvironmentVariable("AIBT_PROJECT_PATH") ?? Directory.GetCurrentDirectory();
    var permissions = TakeOption("--permissions") ?? Environment.GetEnvironmentVariable("AIBT_MCP_PERMISSIONS") ?? "Read";
    var timeoutText = TakeOption("--timeout");
    var timeoutSeconds = timeoutText == null ? 15 : int.Parse(timeoutText, System.Globalization.CultureInfo.InvariantCulture);
    if (timeoutSeconds < 1 || timeoutSeconds > 300) throw new ArgumentException("--timeout must be 1..300 seconds.");
    var argumentsPath = TakeOption("--args-file");
    var catalog = TakeOption("--catalog");
    var output = TakeOption("--output");
    var expectedHash = TakeOption("--expected-hash");
    var expectedAssetHash = TakeOption("--expected-asset-hash");
    var limitText = TakeOption("--limit");
    var afterText = TakeOption("--after-sequence");
    var dryRun = arguments.Remove("--dry-run");
    var overwrite = arguments.Remove("--overwrite");
    var json = arguments.Remove("--json");
    if (arguments.Count == 0 || arguments.Contains("--help"))
    {
        Console.WriteLine("""
            AIBT CLI (running Editor backend)
            status                       Probe without starting the bridge
            operations                   List operation argument schemas and permissions
            describe <operation>         Describe one bridge operation
            mcp ensure                   Start/recover bridge in the selected open Editor
            mcp serve                    Ensure bridge, then serve MCP over stdio
            nodes search [keyword]       Search registered node contracts
            nodes contract <typeId>      Read one node contract
            project manifest             Read project capabilities and tree listing
            tree validate <treeId>       Validate a tree
            tree compile <treeId>        Compile through existing MCP reference backend
            tree inspect <treeId>        Read source and current contentHash
            tree prepare <treeId>        Prepare native runtime asset (Compilation)
            tree expand <treeId>         Source plus its subtree expansion (Read)
            tree usages <treeId>         Trees whose reference nodes use a tree ID (Read)
            tree extract <treeId> <nodeId> <newTreeId> <path>  Move a branch into a new subtree file (SemanticEdit)
            tree unpack <treeId> <nodeId>                     Replace a reference by a plain copy (SemanticEdit)
            tree keys <treeId> <nodeId> <key>=<parentKey>|<key>:=<json>|<key>= ...  Rebind interface keys (SemanticEdit)
            runtime catalogs             List native generated catalogs
            runtime hosts                List live hosts and their program identities
            trace read <instanceId>      Read native trace (Read)
            runtime simulations          List registered simulation containers (TreeSimulationDebug)
            agent describe <sim> <group> <slot>  Explain a container agent (Read)
            agent trace <sim> <group> <slot>     Read a traced container agent's trace (Read)
            call <operation>             Invoke any existing bridge operation

            --project <UnityProject>     Explicit project root (or AIBT_PROJECT_PATH)
            --permissions <categories>   Comma-separated grants; default Read
            --args-file <file.json>      Structured operation arguments (same as bridge)
            --timeout <seconds>          Bounded wait, default 15
            --json                       Machine-readable result envelope
            --catalog <catalogId>        Native catalog from runtime catalogs
            --output <Assets/path.asset> Native asset destination
            --expected-hash <hash>       Source contentHash from tree inspect
            --dry-run                    Compile/preview without writing
            --overwrite --expected-asset-hash <hash>  Explicit existing-asset precondition
            --limit <1..512> --after-sequence <n>     Trace paging
            Exit: 0 success, 1 operation rejected/invalid, 2 usage, 3 connection failure
            All operations except status ensure the bridge before dispatch. No automatic
            replay after dispatch. Unity must already be open; no runtime dependency added.
            """);
        return 0;
    }
    var client = new EditorBridgeClient(project, permissions, TimeSpan.FromSeconds(timeoutSeconds));
    JsonObject result;
    if (arguments.SequenceEqual(new[] { "status" })) result = new JsonObject { ["result"] = client.Status() };
    else if (arguments.SequenceEqual(new[] { "mcp", "ensure" })) result = new JsonObject { ["result"] = client.Ensure() };
    else if (arguments.SequenceEqual(new[] { "mcp", "serve" }))
    {
        client.Ensure();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var serverDll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Server", "bin", configuration, "net10.0", "AibtMcpServer.dll"));
        if (!File.Exists(serverDll)) throw new ArgumentException("Build MCP~/Server/AibtMcpServer.csproj with the same configuration as CLI before mcp serve.");
        var host = Environment.ProcessPath;
        if (host == null || !string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase)) host = "dotnet";
        var start = new ProcessStartInfo(host) { UseShellExecute = false };
        start.ArgumentList.Add(serverDll);
        start.Environment["AIBT_PROJECT_PATH"] = client.ProjectPath;
        start.Environment["AIBT_MCP_DISCOVERY_FILE"] = Path.Combine(client.ProjectPath, "Library", "AibtMcp.json");
        start.Environment["AIBT_MCP_PERMISSIONS"] = permissions;
        using var server = Process.Start(start) ?? throw new IOException("Could not launch MCP server.");
        server.WaitForExit();
        return server.ExitCode;
    }
    else
    {
        var payload = argumentsPath == null ? new JsonObject() : JsonNode.Parse(File.ReadAllText(argumentsPath))?.AsObject() ?? throw new ArgumentException("Expected an argument object.");
        if (catalog != null) payload["catalog"] = catalog;
        if (output != null) payload["outputPath"] = output;
        if (expectedHash != null) payload["expectedHash"] = expectedHash;
        if (expectedAssetHash != null) payload["expectedAssetHash"] = expectedAssetHash;
        if (dryRun) payload["dryRun"] = true;
        if (overwrite) payload["overwrite"] = true;
        if (limitText != null) payload["limit"] = int.Parse(limitText, System.Globalization.CultureInfo.InvariantCulture);
        if (afterText != null) payload["afterSequence"] = ulong.Parse(afterText, System.Globalization.CultureInfo.InvariantCulture);
        string operation;
        if (arguments.SequenceEqual(new[] { "operations" })) operation = "list_operations";
        else if (arguments.Count == 2 && arguments[0] == "describe") { operation = "get_operation_schema"; payload["operation"] = arguments[1]; }
        else if (arguments.Count == 2 && arguments[0] == "runtime" && arguments[1] is "catalogs" or "hosts" or "simulations") operation = "runtime_" + arguments[1];
        else if (arguments.Count == 5 && arguments[0] == "agent" && arguments[1] is "describe" or "trace")
        {
            operation = arguments[1] == "describe" ? "describe_agent" : "read_agent_trace";
            payload["simulation"] = arguments[2];
            payload["group"] = int.Parse(arguments[3], System.Globalization.CultureInfo.InvariantCulture);
            payload["agent"] = int.Parse(arguments[4], System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (arguments.Count == 3 && arguments[0] == "trace" && arguments[1] == "read")
        { operation = "read_trace"; payload["instanceId"] = ulong.Parse(arguments[2], System.Globalization.CultureInfo.InvariantCulture); }
        else if (arguments.Count is 2 or 3 && arguments[0] == "nodes" && arguments[1] == "search")
        { operation = "search_nodes"; if (arguments.Count == 3) payload["keyword"] = arguments[2]; }
        else if (arguments.Count == 3 && arguments[0] == "nodes" && arguments[1] == "contract")
        { operation = "get_node_contract"; payload["typeId"] = arguments[2]; }
        else if (arguments.SequenceEqual(new[] { "project", "manifest" })) operation = "get_project_manifest";
        else if (arguments.Count == 3 && arguments[0] == "tree" && arguments[1] is "validate" or "compile" or "inspect" or "prepare")
        { operation = arguments[1] == "inspect" ? "inspect_tree" : arguments[1] == "prepare" ? "prepare_runtime" : arguments[1]; payload["treeId"] = arguments[2]; }
        else if (arguments.Count == 3 && arguments[0] == "tree" && arguments[1] is "expand" or "usages")
        {
            operation = arguments[1] == "expand" ? "inspect_tree" : "find_subtree_usages";
            payload["treeId"] = arguments[2];
            if (arguments[1] == "expand") payload["expanded"] = true;
        }
        else if (arguments.Count == 6 && arguments[0] == "tree" && arguments[1] == "extract")
        {
            operation = "extract_to_subtree";
            payload["treeId"] = arguments[2];
            payload["nodeId"] = arguments[3];
            payload["newTreeId"] = arguments[4];
            payload["path"] = arguments[5];
        }
        else if (arguments.Count == 4 && arguments[0] == "tree" && arguments[1] == "unpack")
        { operation = "unpack_subtree"; payload["treeId"] = arguments[2]; payload["nodeId"] = arguments[3]; }
        else if (arguments.Count >= 5 && arguments[0] == "tree" && arguments[1] == "keys")
        {
            // key=parentKey connects, key:=<json> binds a constant, key= disconnects.
            operation = "set_subtree_keys";
            payload["treeId"] = arguments[2];
            payload["nodeId"] = arguments[3];
            var keys = new JsonObject();
            foreach (var assignment in arguments.Skip(4))
            {
                var constant = assignment.IndexOf(":=", StringComparison.Ordinal);
                var equals = assignment.IndexOf('=');
                if (constant > 0) keys[assignment[..constant]] = new JsonObject { ["value"] = JsonNode.Parse(assignment[(constant + 2)..]) };
                else if (equals > 0) keys[assignment[..equals]] = equals == assignment.Length - 1 ? null : JsonValue.Create(assignment[(equals + 1)..]);
                else throw new ArgumentException("Expected key=parentKey, key:=<json constant> or key= (not connected): " + assignment);
            }
            payload["keys"] = keys;
        }
        else if (arguments.Count == 2 && arguments[0] == "call") operation = arguments[1].StartsWith("aibt_", StringComparison.Ordinal) ? arguments[1][5..] : arguments[1];
        else throw new ArgumentException("Unknown command. Use --help.");
        result = client.Call(operation, payload);
    }
    var failed = result["error"] != null || result["result"] is JsonObject body && (body["error"] != null || new[] { "success", "valid", "accepted", "found" }.Any(key => body[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && !flag));
    Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = !json }));
    return failed ? 1 : 0;
}
catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or System.Text.Json.JsonException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException)
{
    var usage = exception is ArgumentException or FormatException or OverflowException or System.Text.Json.JsonException or InvalidOperationException;
    var error = new JsonObject { ["error"] = new JsonObject { ["code"] = usage ? "CLI_USAGE" : "AIBT9014", ["message"] = exception.Message } }.ToJsonString();
    if (args.Contains("mcp") && args.Contains("serve")) Console.Error.WriteLine(error);
    else Console.WriteLine(error);
    return usage ? 2 : 3;
}
