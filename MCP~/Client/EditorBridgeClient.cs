using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace AibtTransport;

internal sealed class EditorBridgeClient
{
    internal string ProjectPath { get; }
    private readonly string _discoveryPath;
    private readonly string _permissions;
    private readonly TimeSpan _timeout;

    internal EditorBridgeClient(string projectPath, string? permissions = null, TimeSpan? timeout = null, string? discoveryPath = null)
    {
        ProjectPath = Path.GetFullPath(projectPath);
        _discoveryPath = discoveryPath ?? Path.Combine(ProjectPath, "Library", "AibtMcp.json");
        _permissions = permissions ?? Environment.GetEnvironmentVariable("AIBT_MCP_PERMISSIONS") ?? "";
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
    }

    internal static EditorBridgeClient FromEnvironment()
    {
        var discovery = Environment.GetEnvironmentVariable("AIBT_MCP_DISCOVERY_FILE");
        var project = Environment.GetEnvironmentVariable("AIBT_PROJECT_PATH");
        if (string.IsNullOrWhiteSpace(project))
            project = discovery == null ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(discovery)))!;
        return new EditorBridgeClient(project, discoveryPath: discovery);
    }

    internal JsonObject Status()
    {
        var response = Exchange(ReadEndpoint(), "bridge_status", new JsonObject(), TimeSpan.FromSeconds(2));
        if (response["error"] != null) throw new IOException(response.ToJsonString());
        var status = response["result"]?.AsObject() ?? throw new IOException("Invalid bridge health response.");
        var reportedProject = status["projectPath"]?.GetValue<string>();
        if (status["formatVersion"]?.GetValue<int>() != 1 || reportedProject == null
            || !PathsEqual(Path.GetDirectoryName(reportedProject)!, ProjectPath))
            throw new IOException("Bridge belongs to a different project or uses an unsupported protocol.");
        return (JsonObject)status.DeepClone();
    }

    internal JsonObject Ensure()
    {
        try { return Status(); }
        catch (Exception exception) when (IsConnectionFailure(exception)) { }
        if (!File.Exists(Path.Combine(ProjectPath, "ProjectSettings", "ProjectVersion.txt")))
            throw new IOException("Select a Unity project with --project or AIBT_PROJECT_PATH.");
        var directory = Path.Combine(ProjectPath, "Library", "AIBT", "bridge-control");
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        var requestPath = Path.Combine(directory, id + ".request.json");
        var responsePath = Path.Combine(directory, id + ".response.json");
        var observedPort = 0;
        try { observedPort = ReadEndpoint(); }
        catch (Exception exception) when (IsConnectionFailure(exception)) { }
        var request = new JsonObject
        {
            ["formatVersion"] = 1, ["requestId"] = id, ["operation"] = "ensure",
            ["expiresUtcTicks"] = DateTime.UtcNow.Add(_timeout).Ticks, ["restart"] = true, ["observedPort"] = observedPort,
        };
        File.WriteAllText(requestPath + ".tmp", request.ToJsonString());
        File.Move(requestPath + ".tmp", requestPath);
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < _timeout)
            {
                if (File.Exists(responsePath))
                {
                    var response = JsonNode.Parse(File.ReadAllText(responsePath));
                    if (response?["formatVersion"]?.GetValue<int>() != 1 || response?["requestId"]?.GetValue<string>() != id)
                        throw new IOException("Invalid bridge startup acknowledgement.");
                    return Status();
                }
                Thread.Sleep(100);
            }
            throw new IOException("Editor did not acknowledge bridge startup. Open the selected project with AIBT installed; wait for compilation or a blocking dialog to finish, then retry. Unity was not launched or restarted.");
        }
        finally
        {
            File.Delete(requestPath);
            File.Delete(responsePath);
        }
    }

    internal JsonObject Call(string operation, JsonObject args)
    {
        Ensure();
        // Recovery is before dispatch only: a lost response never permits replaying a mutation.
        return Exchange(ReadEndpoint(), operation, args, _timeout);
    }

    private int ReadEndpoint()
    {
        var discovery = JsonNode.Parse(File.ReadAllText(_discoveryPath));
        var port = discovery?["port"]?.GetValue<int>() ?? 0;
        var assets = discovery?["project_path"]?.GetValue<string>();
        if (port < 1 || port > 65535 || assets == null || !PathsEqual(Path.GetDirectoryName(assets)!, ProjectPath))
            throw new IOException("Invalid discovery file or selected-project mismatch.");
        return port;
    }

    private JsonObject Exchange(int port, string operation, JsonObject args, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            using var client = new TcpClient();
            client.ConnectAsync("127.0.0.1", port, cancellation.Token).AsTask().GetAwaiter().GetResult();
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var granted = new JsonArray();
            foreach (var permission in _permissions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                granted.Add(permission);
            var request = new JsonObject { ["tool"] = operation, ["args"] = args.DeepClone(), ["grantedCategories"] = granted };
            writer.WriteLineAsync(request.ToJsonString().AsMemory(), cancellation.Token).GetAwaiter().GetResult();
            var line = reader.ReadLineAsync(cancellation.Token).AsTask().GetAwaiter().GetResult();
            return JsonNode.Parse(line ?? throw new IOException("Bridge closed without a response; operation outcome may be unknown. Do not blindly retry a mutation."))?.AsObject()
                ?? throw new IOException("Empty bridge response.");
        }
        catch (OperationCanceledException)
        {
            throw new IOException("Bridge timed out; operation outcome may be unknown. Inspect the project before retrying a mutation.");
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsConnectionFailure(Exception exception)
        => exception is IOException or SocketException or System.Text.Json.JsonException or InvalidOperationException;
}
