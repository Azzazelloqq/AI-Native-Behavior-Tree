using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using AibtTransport;

var root = Path.Combine(Path.GetTempPath(), "aibt-cli-contracts-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "Library"));
Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: contract-fixture");
var count = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
if (args.Length != 1) throw new ArgumentException("Pass the built CLI DLL path.");
var cliDll = Path.GetFullPath(args[0]);
(int exit, JsonObject result) RunCli(params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    start.ArgumentList.Add(cliDll);
    start.ArgumentList.Add("--project"); start.ArgumentList.Add(root); start.ArgumentList.Add("--json");
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, JsonNode.Parse(output)!.AsObject());
}
using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
void WriteDiscovery(string project) => File.WriteAllText(Path.Combine(root, "Library", "AibtMcp.json"), new JsonObject { ["port"] = port, ["project_path"] = Path.Combine(project, "Assets") }.ToJsonString());
var calls = new List<JsonObject>();
using var stopping = new CancellationTokenSource();
var server = Task.Run(async () =>
{
    try
    {
        while (!stopping.IsCancellationRequested)
        {
            using var socket = await listener.AcceptTcpClientAsync(stopping.Token);
            using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            var request = JsonNode.Parse((await reader.ReadLineAsync())!)!.AsObject();
            var tool = request["tool"]!.GetValue<string>();
            if (tool == "bridge_status")
                await writer.WriteLineAsync(new JsonObject { ["result"] = new JsonObject { ["formatVersion"] = 1, ["projectPath"] = Path.Combine(root, "Assets"), ["processId"] = Environment.ProcessId } }.ToJsonString());
            else
            {
                lock (calls) calls.Add(request);
                if (tool == "lose_response") continue;
                if (tool == "hang_response") { await Task.Delay(TimeSpan.FromSeconds(3), stopping.Token); continue; }
                await writer.WriteLineAsync("{\"result\":{\"accepted\":true}}");
            }
        }
    }
    catch (OperationCanceledException) { }
});
try
{
    WriteDiscovery(root);
    var statusCommand = RunCli("status");
    Check(statusCommand.exit == 0 && statusCommand.result["result"]?["formatVersion"]?.GetValue<int>() == 1, "CLI status must emit an attachable JSON envelope.");
    Check(RunCli("unknown-command").exit == 2, "Unknown command must return usage exit code.");
    Check(RunCli("--timeout", "999999999999999999999", "status").exit == 2, "Overflowing timeout must fail as usage, not crash.");
    var client = new EditorBridgeClient(root, "Read,SemanticEdit", TimeSpan.FromSeconds(1));
    Check(client.Ensure()["processId"]!.GetValue<int>() == Environment.ProcessId, "Probe must verify a running endpoint.");
    Check(!Directory.Exists(Path.Combine(root, "Library", "AIBT", "bridge-control")), "Healthy ensure must not issue a restart request.");
    var result = client.Call("add_node", new JsonObject { ["expectedHash"] = "fixture-hash", ["dryRun"] = true });
    Check(result["result"]?["accepted"]?.GetValue<bool>() == true, "Operation response lost.");
    Check(calls.Count == 1 && calls[0]["args"]?["expectedHash"]?.GetValue<string>() == "fixture-hash", "Arguments or operation count changed.");
    Check(calls[0]["grantedCategories"]!.AsArray().Count == 2, "Explicit grants not preserved.");
    try { client.Call("lose_response", new JsonObject()); throw new Exception("Expected lost-response error."); }
    catch (IOException exception) { Check(exception.Message.Contains("outcome", StringComparison.Ordinal), "Unknown mutation outcome must be explicit."); }
    Check(calls.Count(call => call["tool"]!.GetValue<string>() == "lose_response") == 1, "Mutation was automatically replayed.");
    try { client.Call("hang_response", new JsonObject()); throw new Exception("Expected command timeout."); }
    catch (IOException exception) { Check(exception.Message.Contains("outcome", StringComparison.Ordinal), "Timed-out mutation must report an unknown outcome."); }
    Check(calls.Count(call => call["tool"]!.GetValue<string>() == "hang_response") == 1, "Timed-out mutation was replayed.");
    WriteDiscovery(Path.Combine(root, "other"));
    try { client.Status(); throw new Exception("Accepted another project's endpoint."); }
    catch (IOException) { Check(true, "Project mismatch rejected."); }
    File.Delete(Path.Combine(root, "Library", "AibtMcp.json"));
    var timer = Stopwatch.StartNew();
    try { client.Ensure(); throw new Exception("Expected absent Editor error."); }
    catch (IOException exception) { Check(exception.Message.Contains("Open the selected project", StringComparison.Ordinal), "Closed Editor needs an actionable error."); }
    Check(timer.Elapsed < TimeSpan.FromSeconds(3), "Closed Editor wait was not bounded.");
    Check(!Directory.GetFiles(Path.Combine(root, "Library", "AIBT", "bridge-control")).Any(), "Timed-out startup left an actionable request behind.");
    Console.WriteLine(new JsonObject { ["passed"] = count, ["failed"] = 0 }.ToJsonString());
}
finally
{
    stopping.Cancel();
    listener.Stop();
    await server;
    Directory.Delete(root, true);
}
