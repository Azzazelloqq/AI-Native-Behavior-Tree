"""Exercise CLI and real MCP stdio against an explicitly prepared disposable Editor project."""
import argparse
import json
import pathlib
import queue
import socket
import subprocess
import tempfile
import threading
import time
import uuid

parser = argparse.ArgumentParser()
parser.add_argument("--project", required=True)
parser.add_argument("--output", required=True)
options = parser.parse_args()
package = pathlib.Path(__file__).resolve().parents[3]
project = pathlib.Path(options.project).resolve()
assert (project / "Assets/CliProbe/Editor/CliSessionProbe.cs").is_file(), "Requires the owned disposable Editor fixture"
cli = package / "MCP~/Cli/bin/Debug/net10.0/aibt.dll"
report = {}

def command(*arguments, permissions="Read", expected=0, payload=None):
    with tempfile.TemporaryDirectory(prefix="aibt-cli-request-") as temporary:
        args = ["dotnet", str(cli), "--project", str(project), "--permissions", permissions, "--json"]
        if payload is not None:
            file = pathlib.Path(temporary) / "arguments.json"
            file.write_text(json.dumps(payload), encoding="utf-8")
            args += ["--args-file", str(file)]
        result = subprocess.run(args + list(arguments), capture_output=True, text=True, timeout=40)
        assert result.returncode == expected, (arguments, result.returncode, result.stdout, result.stderr)
        return json.loads(result.stdout)

discovery = project / "Library/AibtMcp.json"
command("mcp", "ensure")
port = json.loads(discovery.read_text(encoding="utf-8-sig"))["port"]
command("mcp", "ensure")
assert json.loads(discovery.read_text(encoding="utf-8-sig"))["port"] == port
report["healthyEnsureReusesEndpoint"] = True
assert command("nodes", "contract", "aibt.core.cooldown")["result"]["found"]
command("tree", "validate", "tree.cli-verification", expected=1)
report["permissionDenial"] = True

token = uuid.uuid4().hex
tree_file = project / ("Assets/CliProbe/cli-tree-" + token + ".aibt.json")
tree = {"treeId": "tree.cli-verification-" + token, "name": "CLI Verification", "path": "CliProbe/" + tree_file.name,
        "rootNode": {"id": "root", "typeId": "aibt.core.memory-sequence", "typeVersion": 1}, "dryRun": True}
command("call", "create_tree", permissions="SemanticEdit", payload=tree)
assert not tree_file.exists()
report["dryRunDoesNotPersist"] = True
tree["dryRun"] = False
created = command("call", "create_tree", permissions="SemanticEdit", payload=tree)
assert created["result"]["accepted"]
original = tree_file.read_bytes()
command("call", "add_node", permissions="SemanticEdit", expected=1,
        payload={"treeId": tree["treeId"], "expectedHash": "0" * 64, "parentId": "root",
                 "node": {"id": "child", "typeId": "aibt.core.memory-sequence", "typeVersion": 1}})
assert tree_file.read_bytes() == original
report["staleHashDoesNotMutate"] = True
assert command("tree", "validate", tree["treeId"], permissions="Compilation")["result"]["valid"]
assert command("tree", "compile", tree["treeId"], permissions="Compilation")["result"]["success"]
report["cliAuthorValidateCompile"] = True

data = json.loads(discovery.read_text(encoding="utf-8-sig"))
data["port"] = 0
discovery.write_text(json.dumps(data), encoding="utf-8")
command("mcp", "ensure")
assert json.loads(discovery.read_text(encoding="utf-8-sig"))["port"] > 0
report["staleEndpointRecovery"] = True

blocked_port = json.loads(discovery.read_text(encoding="utf-8-sig"))["port"]
with socket.create_connection(("127.0.0.1", blocked_port), timeout=5):
    command("mcp", "ensure")
assert json.loads(discovery.read_text(encoding="utf-8-sig"))["port"] != blocked_port
report["unresponsiveEndpointRecovery"] = True

loaded = project / "Library/cli-probe-loaded.txt"
before = loaded.read_text()
reload_request = project / "Library/cli-probe-reload.request"
reload_request.with_suffix(".tmp").write_text("reload")
reload_request.with_suffix(".tmp").replace(reload_request)
deadline = time.monotonic() + 40
while time.monotonic() < deadline:
    if loaded.read_text() != before:
        break
    time.sleep(0.2)
else:
    raise AssertionError("Editor did not reload")
command("mcp", "ensure")
assert command("nodes", "contract", "aibt.core.cooldown")["result"]["found"]
report["domainReloadReconnect"] = True

messages = queue.Queue()
with tempfile.TemporaryFile(mode="w+", encoding="utf-8") as errors:
    process = subprocess.Popen(["dotnet", str(cli), "--project", str(project), "--permissions", "Read", "mcp", "serve"],
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=errors, text=True, encoding="utf-8")
    def read_messages():
        for line in process.stdout:
            try:
                messages.put(json.loads(line))
            except json.JSONDecodeError:
                messages.put({"invalidStdout": line})
    threading.Thread(target=read_messages, daemon=True).start()
    def send(value):
        process.stdin.write(json.dumps(value) + "\n")
        process.stdin.flush()
    def response(identifier):
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            value = messages.get(timeout=max(0.1, deadline - time.monotonic()))
            assert "invalidStdout" not in value, value
            if value.get("id") == identifier:
                assert "error" not in value, value
                return value["result"]
        raise AssertionError("MCP response timed out")
    try:
        send({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-03-26", "capabilities": {}, "clientInfo": {"name": "aibt-cli-verification", "version": "1"}}})
        response(1)
        send({"jsonrpc": "2.0", "method": "notifications/initialized"})
        send({"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {}})
        tools = response(2)["tools"]
        assert any(tool["name"] == "aibt_get_node_contract" for tool in tools)
        send({"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "aibt_get_node_contract", "arguments": {"typeId": "aibt.core.cooldown"}}})
        content = response(3)["content"]
        assert any(item.get("type") == "text" and json.loads(item["text"])["result"]["found"] for item in content)
        report["realMcpStdioInitializeListCall"] = True
        report["mcpToolCount"] = len(tools)
    finally:
        process.stdin.close()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.terminate()
            process.wait(timeout=5)
        errors.seek(0)
        report["mcpExit"] = process.returncode
        assert process.returncode == 0, errors.read()

pathlib.Path(options.output).parent.mkdir(parents=True, exist_ok=True)
pathlib.Path(options.output).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report))
