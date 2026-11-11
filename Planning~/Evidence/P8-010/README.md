# P8-010 — CLI and on-demand bridge

Status: Review. Implementation and the installed-toolchain checks pass. No commit/push.

## Outcome

- CLI and MCP share `MCP~/Client/EditorBridgeClient.cs` and the existing Editor operation
  dispatcher. CLI has status, ensure/serve, node search/contracts, manifest, tree validate/compile
  and generic JSON operation calls. Existing MCP tools remain compatible.
- A version-1 request under Library starts the selected already-open Editor's bridge. No listener
  starts merely because Unity opened. Health checks reuse a healthy listener, repair stale
  discovery and recover an endpoint blocked by an idle TCP client. Requests identify the
  observed endpoint so concurrent recovery does not blindly replace an already-recovered owner.
- Window and domain-reload recovery share one listener. Stop closes idle connections and the
  accept loop tolerates disconnected clients. Operation permissions remain explicit. Recovery
  precedes dispatch; a response loss/timeout never replays a mutation.
- Agent setup/workflow and outdated tracing/scope warnings are updated. Reference compilation,
  native preparation, simulation and live tracing are explicitly distinguished.

## Verification

Installed environment: Windows, Unity 6000.7.0b1, .NET SDK 10.0.300.

- CLI and MCP server builds: zero errors/warnings.
- External transport/CLI contracts: **16/16**, including project identity, healthy reuse,
  permission forwarding, valid status JSON, usage/overflow exits, lost/timed-out response exactly
  once and bounded closed-Editor failure with request cleanup.
- Full isolated Unity EditMode: **1500/1500**, zero failed/skipped. Includes explicit/expired
  bridge requests and idle connection shutdown. See `verification.json` and normalized XML.
  The first added shutdown test read the raw UTF-8 preamble as application data; it was corrected
  to use the protocol's text reader, retaining the expected EOF contract, then the full suite passed.
- Real CLI + real MCP stdio client: see `live.json`. Both exercise the same disposable Editor.
  Covers create/dry-run, stale-hash no mutation, validate/compile, permission denial, repeated
  ensure, stale discovery, hung endpoint recovery, domain reload and MCP initialize/list/call.
  The MCP process exits cleanly when its client closes stdin.
- Static/schema checks: 7 schemas and 156 work items pass. Diff whitespace check passes.
- The previously recorded full-suite shutdown warning (112 Persistent allocations) remains.
  This task makes no Player/performance/runtime-allocation claim. Unity 6000.5.8f1 is not installed.

## Reproduce

From the package root, build both external projects:

```powershell
dotnet build MCP~/Cli/AibtCli.csproj
dotnet build MCP~/Server/AibtMcpServer.csproj
dotnet run --project Tools~/Verification/P8/CliContracts/CliContracts.csproj -- "<AIBT>/MCP~/Cli/bin/Debug/net10.0/aibt.dll"
```

For live checks use a **disposable** Unity project with a local file dependency on this package.
Copy `Tools~/Verification/P8/Fixtures/CliSessionProbe.cs` to its `Assets/CliProbe/Editor/` directory.
Launch Unity with `-batchmode -projectPath <DisposableProject> -executeMethod CliSessionProbe.Open`
and no `-quit`. Do not use a user's active gameplay project. Then:

```powershell
python Tools~/Verification/P8/Run-CliLive.py --project "<DisposableProject>" --output "<Results>/live.json"
```

The verifier creates uniquely named test trees only in that fixture directory; it can be rerun.
Request fixture shutdown with an atomically published `Library/cli-probe-quit.request`, or close
that owned Editor normally. For full tests use `Run-UnityTests.ps1 -Scope Full -EnableGraphics`
with Unity closed and the package listed in the disposable project's testables.

## Limits

Unity must already be open/responsive. `mcp serve` needs the already-built server with matching
build configuration. CLI cannot register tools inside an arbitrary AI client; configure that
client to launch it. Existing native-only project-node/reference-backend and UPM documentation
resource-root limitations are not repaired by adding a transport. Native Prepare Runtime and
live native tracing still use the Editor. Do not present the CLI compile command as that path.

No Runtime assembly, scheduler semantics, package version, AI-client configuration or unrelated
working-tree changes were modified.
