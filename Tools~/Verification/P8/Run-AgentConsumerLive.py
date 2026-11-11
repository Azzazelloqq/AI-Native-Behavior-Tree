"""Run only against the disposable First Agent project with AgentConsumerSession installed."""
import argparse
import json
import pathlib
import queue
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
assert (project / "Assets/CliProbe/Editor/AgentConsumerSession.cs").exists(), "Owned disposable fixture required"
cli = package / "MCP~/Cli/bin/Debug/net10.0/aibt.dll"
report = {}

def call(*arguments, payload=None, permissions="Read", expected=0):
    with tempfile.TemporaryDirectory(prefix="aibt-agent-call-") as directory:
        command = ["dotnet", str(cli), "--project", str(project), "--permissions", permissions, "--json"]
        if payload is not None:
            path = pathlib.Path(directory) / "args.json"
            path.write_text(json.dumps(payload), encoding="utf-8")
            command += ["--args-file", str(path)]
        result = subprocess.run(command + list(arguments), capture_output=True, text=True, timeout=40)
        assert result.returncode == expected, (arguments, result.returncode, result.stdout, result.stderr)
        return json.loads(result.stdout)

def publish(name, value):
    destination = project / "Library" / name
    temporary = destination.with_suffix(".tmp")
    temporary.write_text(value, encoding="utf-8")
    temporary.replace(destination)

def wait_json(name, predicate, seconds=40):
    deadline = time.monotonic() + seconds
    last = None
    while time.monotonic() < deadline:
        path = project / "Library" / name
        if path.exists():
            try:
                last = json.loads(path.read_text(encoding="utf-8-sig"))
                if predicate(last):
                    return last
            except (OSError, json.JSONDecodeError):
                pass
        time.sleep(0.2)
    raise AssertionError(("Fixture wait timed out", name, last))

preflight = wait_json("agent-preflight.json", lambda value: value["refreshedNativeGraphValid"])
if call("runtime", "hosts")["result"]["hosts"]:
    publish("agent-stop.request", "stop")
    deadline = time.monotonic() + 30
    while call("runtime", "hosts")["result"]["hosts"]:
        assert time.monotonic() < deadline, "Previous owned fixture did not stop"
        time.sleep(0.2)
assert not preflight["initialNativeGraphValid"] and preflight["projectDocsGenerated"]
report["lateGraphCatalogRebound"] = True
assert not (project / "Assets/AIBT").exists()
assert call("call", "get_static_resource", payload={"key": "workflow"})["result"]["found"]
catalog_document = call("call", "get_static_resource", payload={"key": "node-catalog"})["result"]["content"]
assert "aibt.samples.first-agent.move" in catalog_document
report["upmResourcesAndProjectDocumentation"] = True
schemas = call("operations")["result"]["operations"]
assert "expectedHash" in call("describe", "prepare_runtime")["result"]["inputSchema"]["required"]
report["operationSchemas"] = len(schemas)

tree_id = "aibt.samples.first-agent"
tree = call("tree", "inspect", tree_id)["result"]
assert not tree["referenceAvailable"]
for parameter, value in (("speed", 1.25), ("stop-distance", 0.125)):
    result = call("call", "configure_node", permissions="SemanticEdit", payload={"treeId": tree_id,
                  "expectedHash": tree["contentHash"], "nodeId": tree["tree"]["root"], "parameterName": parameter, "value": value})
    assert result["result"]["accepted"]
    tree = call("tree", "inspect", tree_id)["result"]
assert call("tree", "validate", tree_id, permissions="Compilation")["result"]["valid"]
assert not call("tree", "compile", tree_id, permissions="Compilation", expected=1)["result"]["success"]
report["nativeEditingValidationReferenceBoundary"] = True

catalog_name = "AIBT.Samples.FirstAgent.FirstAgentCatalog"
catalogs = call("runtime", "catalogs")["result"]["catalogs"]
assert any(item["typeName"] == catalog_name for item in catalogs)
asset = "Assets/AibtFirstAgentDemo/AgentCase-" + uuid.uuid4().hex + ".runtime.asset"
prepare = {"treeId": tree_id, "expectedHash": tree["contentHash"], "catalog": catalog_name, "outputPath": asset, "dryRun": True}
assert call("call", "prepare_runtime", payload=prepare, permissions="Compilation")["result"]["success"]
assert not (project / asset).exists()
prepare["dryRun"] = False
prepared = call("call", "prepare_runtime", payload=prepare, permissions="Compilation")["result"]
assert prepared["success"]
call("call", "prepare_runtime", payload=prepare, permissions="Compilation", expected=1)
prepare["dryRun"] = True
existing = call("call", "prepare_runtime", payload=prepare, permissions="Compilation")["result"]["existingAssetHash"]
prepare.update(dryRun=False, overwrite=True, expectedAssetHash=existing)
replaced = call("call", "prepare_runtime", payload=prepare, permissions="Compilation")["result"]
assert replaced["assetGuid"] == prepared["assetGuid"]
prepare["expectedHash"] = "0" * 64
call("call", "prepare_runtime", payload=prepare, permissions="Compilation", expected=1)
report["nativePrepareDryRunHashAndGuidOverwrite"] = True

case = {"runId": uuid.uuid4().hex, "asset": asset, "catalog": catalog_name, "target": 4.0, "stoppingRadius": 0.125}
publish("agent-case.json", json.dumps(case))
publish("agent-play.request", "play")
state = wait_json("agent-play-state.json", lambda value: value.get("runId") == case["runId"] and value.get("rootStatus") == "Success", seconds=60)
case = wait_json("agent-case.json", lambda value: "expectedProgramHash" in value)
assert abs(state["x"] - case["target"]) <= case["stoppingRadius"] + 0.0001, state
assert state["programHash"] == case["expectedProgramHash"] and state["graphAttached"], state
hosts = call("runtime", "hosts")["result"]["hosts"]
assert any(item["instanceId"] == state["instanceId"] and item["programHash"] == state["programHash"] for item in hosts)
trace = call("trace", "read", str(state["instanceId"]), "--limit", "512")["result"]
assert trace["available"] and trace["rootStatus"] == "Success" and trace["programHash"] == state["programHash"]
assert trace["complete"] == (trace["droppedCount"] == 0 and not trace["faulted"])
assert any(item["nodeId"] == tree["tree"]["root"] for item in trace["events"])
if trace["complete"]:
    assert any(item["nodeId"] == tree["tree"]["root"] and item["kind"] == "NodeExited" for item in trace["events"])
report["preparedGameplayExpectedPositionAndRootSuccess"] = True
report["sameGraphNativeHostAttachment"] = True
report["nativeTraceIdentityAndDropDisclosure"] = True

messages = queue.Queue()
with tempfile.TemporaryFile(mode="w+", encoding="utf-8") as errors:
    process = subprocess.Popen(["dotnet", str(cli), "--project", str(project), "--permissions", "Read", "mcp", "serve"],
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=errors, text=True, encoding="utf-8")
    def read_messages():
        for line in process.stdout:
            messages.put(json.loads(line))
    threading.Thread(target=read_messages, daemon=True).start()
    def request(identifier, method, params):
        process.stdin.write(json.dumps({"jsonrpc": "2.0", "id": identifier, "method": method, "params": params}) + "\n")
        process.stdin.flush()
        while True:
            message = messages.get(timeout=30)
            if message.get("id") == identifier:
                assert "error" not in message, message
                return message["result"]
    try:
        request(1, "initialize", {"protocolVersion": "2025-03-26", "capabilities": {}, "clientInfo": {"name": "aibt-agent-consumer", "version": "1"}})
        process.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n')
        process.stdin.flush()
        listed = request(2, "tools/list", {})["tools"]
        assert any(item["name"] == "aibt_read_trace" for item in listed)
        response = request(3, "tools/call", {"name": "aibt_read_trace", "arguments": {"instanceId": state["instanceId"], "limit": 512}})
        body = json.loads(next(item["text"] for item in response["content"] if item["type"] == "text"))["result"]
        assert body["programHash"] == state["programHash"] and body["available"]
        resource = request(4, "resources/read", {"uri": "aibt://resource/node-catalog"})
        assert "aibt.samples.first-agent.move" in resource["contents"][0]["text"]
        report["mcpNativeTraceAndProjectResources"] = True
        report["mcpToolCount"] = len(listed)
    finally:
        process.stdin.close()
        process.wait(timeout=10)
        assert process.returncode == 0

publish("agent-stop.request", "stop")
time.sleep(2)
token = uuid.uuid4().hex
type_id = "aibt.agentverification.threshold-" + token
generation = {"kind": "condition", "typeId": type_id, "namespace": "AgentVerification.Nodes",
              "summary": "Threshold behavior fixture", "category": "Verification", "whenToUse": "Disposable test",
              "whenNotToUse": "Gameplay", "blackboardReadKey": "current", "blackboardReadType": "Float32"}
call("call", "generate_node", payload=generation, permissions="CodeGeneration")
generated_cases = call("call", "generate_node_tests_and_manifest", permissions="CodeGeneration")["result"]
assert generated_cases["caseCount"] == 2 and "Assert.Inconclusive" not in generated_cases["source"]
attempt = call("call", "analyze_and_compile_node", payload={"mode": "start"}, permissions="Compilation")["result"]
deadline = time.monotonic() + 90
while time.monotonic() < deadline:
    status = call("call", "analyze_and_compile_node", payload={"mode": "check", "attemptId": attempt["attemptId"]}, permissions="Compilation")["result"]
    if status["status"] == "compiled":
        break
    assert status["status"] in ("pending", "still-compiling"), status
    time.sleep(1)
else:
    raise AssertionError("Staged compilation timed out")
hash_value = status["contentHash"]
checked = call("call", "test_node", payload={"expectedContentHash": hash_value}, permissions="TestExecution")["result"]
assert checked["valid"] and checked["dispatchProven"] and checked["behaviorProven"], checked
wrong = {"expectedContentHash": hash_value, "cases": [{"configuration": {"threshold": 1.0}, "inputs": {"current": 1.0}, "expectedStatus": "Failure"}]}
assert not call("call", "test_node", payload=wrong, permissions="TestExecution", expected=1)["result"]["valid"]
destination = "AgentVerification/Applied-" + token
call("call", "apply_node", payload={"expectedContentHash": hash_value, "destinationPath": destination}, permissions="CodeGeneration")
deadline = time.monotonic() + 60
while time.monotonic() < deadline:
    found = call("nodes", "search", type_id)["result"]["entries"]
    if any(item["typeId"] == type_id for item in found):
        break
    time.sleep(1)
else:
    raise AssertionError("Applied runtime node never appeared in shared catalog")
new_tree = {"treeId": "tree.agentverification-" + token, "name": "Agent Verification", "path": destination + "/tree.aibt.json", "formatVersion": 2,
            "rootNode": {"id": "condition", "typeId": type_id, "typeVersion": 1, "parameters": {"threshold": 1.0}, "bindings": {"current": "current"}},
            "blackboard": [{"id": "current", "valueType": "Float32", "default": 0.0}]}
created = call("call", "create_tree", payload=new_tree, permissions="SemanticEdit")["result"]
assert created["accepted"], created
assert call("tree", "validate", new_tree["treeId"], permissions="Compilation")["result"]["valid"]
report["generatedNativeBehaviorCasesAndCounterexample"] = True
report["appliedRuntimeNodeDiscoveryAndNativeAuthoring"] = True

pathlib.Path(options.output).parent.mkdir(parents=True, exist_ok=True)
pathlib.Path(options.output).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report))
