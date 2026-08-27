# ADR P6-001: MCP transport, hosting, and permission-model

- Status: Accepted 2026-08-27
- Date: 2026-08-27
- Decision ID: AIBT-024

## Context

Phase 6 needs a concrete answer to how AIBT exposes an MCP server from this
Unity C# package (`Documentation~/ai-and-mcp.md`, `AIBT-009`'s existing
"MCP optional, model-neutral, transactional, schema-driven" principle). No
MCP scaffolding exists anywhere in the codebase (confirmed by grep before
decomposition). Three real candidates were researched against actual,
current evidence (NuGet package pages, the official C# MCP SDK repository,
Unity's own official MCP package documentation, and this exact host
project's own installed `com.coplaydev.unity-mcp`), not assumed from
memory:

- **Candidate A (rejected): vendor the official SDK's DLLs into Unity's
  Mono Editor domain.** `ModelContextProtocol.Core` targets .NET 8.0/.NET
  Standard 2.0 and has 9 transitive NuGet dependencies at `≥10.0.10`. This
  project already vendors third-party DLLs directly
  (`Assets/Plugins/Roslyn/*.dll`, including `System.Collections.Immutable.dll`)
  — a real, non-hypothetical assembly-version-conflict risk against
  whatever version the SDK's own transitive dependency would bring.
  Rejected before being spiked, on this real risk plus the architecture
  finding below.
- **Candidate B (recorded, not selected): register into Unity's own
  official "Unity MCP"** (`com.unity.ai.assistant`, `2.0.0-pre.1`), which
  runs in-process in the Editor and documents third-party tool
  registration. Not installed in this host project, explicitly
  preview-status, and would make AIBT's core MCP contract depend on an
  optional first-party package most consuming projects will not have.
  Recorded as a possible future secondary adapter, not the foundation.
- **Candidate A' (selected): an external `dotnet` process running the
  official SDK, bridged to Unity over a thin, no-SDK-dependency Editor-side
  listener.** This is the same shape as the community `com.coplaydev.unity-mcp`
  package already installed and used throughout this project's own Phase
  3-5 evidence — confirmed directly from its own docs ("Install the Python
  server": its actual MCP-protocol server is an external Python process,
  not code running inside Unity). AIBT's version is the same shape in C#.
  No MCP SDK dependency is ever loaded into Unity's Asset Database, so
  Candidate A's assembly-conflict risk does not apply.

Distribution and setup UX were owner decisions made directly in
conversation (not derived by the spike):

- **Distribution: requires the .NET SDK installed on the user's machine**,
  launched via the AI client's own MCP config (`dotnet run --project ...`),
  over a vendored self-contained per-platform executable. A deliberate
  scope/UX tradeoff, not a default chosen by an implementing agent.
- **One-button setup UX, honestly scoped:** an Editor menu command may
  detect `dotnet --version` and auto-write/update the AI client's MCP
  config (real precedent: `com.coplaydev.unity-mcp`'s own
  "auto-configuration for popular MCP clients"), but must never attempt to
  silently install the SDK itself, and must not claim the client picks up
  the change without a possible restart.

## Spike evidence (`Spikes~/McpTransportModel/`, 2026-08-27, this workstation)

All four checks were run against real tooling, not simulated:

1. **External server compiles.** `Spikes~/McpTransportModel/Server/`
   (`dotnet new console`, `dotnet add package ModelContextProtocol 2.2.0`,
   `dotnet add package Microsoft.Extensions.Hosting`) built with `dotnet
   build`: 0 warnings, 0 errors, on the first attempt.
2. **Real external MCP client round-trip**, using the official Anthropic
   `@modelcontextprotocol/inspector` CLI (via `npx`, a genuine independent
   client, not a hand-rolled fake) launching the spike server exactly the
   way an AI client's own `.mcp.json` would (`dotnet run --project ...`):
   - `tools/list` → returned `aibt_spike_ping` with its declared schema.
   - `tools/call aibt_spike_ping message="hello from P6-001 spike"` →
     returned `"AIBT MCP spike pong: hello from P6-001 spike"`.
   - `resources/list` → returned the `aibt-spike://status` resource.
   - `resources/read aibt-spike://status` → returned the exact static text.
3. **Unity-side discovery-file bridge**, run live in the actually-open
   Unity `6000.5.8f1` Editor via Unity MCP `execute_code`: a `TcpListener`
   bound to an ephemeral loopback port, with the port + process ID +
   project path + Unity version written to `Library/AibtMcpSpike.json`
   (shape mirroring Unity's own first-party `Library/EditorInstance.json`,
   confirmed present in this project with real content
   `{"process_id":40100,...}` before this spike started — the written
   `process_id` in the spike's own discovery file matched exactly). A
   genuinely external process (Node, outside Unity and outside the spike
   server) read the discovery file and connected to the reported port
   successfully, receiving `"aibt-spike-bridge-echo: hello from external
   process"` back.
4. **One-button config-writing feasibility**, also run live via
   `execute_code`: `dotnet --version` invoked from inside the Editor
   process returned `10.0.300` (matching the workstation's real installed
   SDK), and a syntactically valid `.mcp.json`-shaped JSON fragment was
   written and independently validated (`JSON.parse` succeeded).

5. **Real Claude Code round-trip, after a full session restart** (the actual
   target client, not only the Inspector CLI proxy): with `aibtMcpSpike`
   registered in this repo's `.mcp.json` before session start, the restarted
   session listed `mcp__aibtMcpSpike__aibt_spike_ping` as an available tool,
   called it (`"AIBT MCP spike pong: hello from real Claude Code client,
   P6-001"`), listed the `aibt-spike://status` resource via
   `ListMcpResourcesTool`, and read it via `ReadMcpResourceTool` — all
   matching the Inspector CLI's own results exactly. This closes the one
   gap the mid-session attempt (item 2, first pass) could not: a live
   `.mcp.json` edit does not hot-reconnect mid-session, but a real client
   connecting from its own startup works exactly as expected.

No candidate failure was hit; Candidate B/C were not spiked further, per
the card's own decision priority.

Full raw output is in `Planning~/Evidence/P6-001/README.md` and
`verification-results.json`.

## Decision

1. **Process model.** AIBT's MCP server is a standalone `dotnet` console
   application (project name and exact location decided by `P6-005`, built
   on the pattern proven above: `ModelContextProtocol` +
   `Microsoft.Extensions.Hosting`, `AddMcpServer().WithStdioServerTransport()
   .WithToolsFromAssembly().WithResourcesFromAssembly()`), entirely outside
   Unity's Asset Database and assembly graph. It is launched by the AI
   client's own MCP configuration (`command: "dotnet"`, `args: ["run",
   "--project", "<path>"]`), the same way this session's own `.mcp.json`
   launches any stdio server.
2. **Transport.** stdio, for the client-to-server leg (the natural fit for
   a client-launched external process; no HTTP/SSE server needed for this
   leg).
3. **Unity-side bridge.** A new Editor-only assembly (working name `MCP/`,
   sibling to `Editor/`/`CodeGen~/` per `architecture.md`'s existing
   diagram, referencing `AIBT.Authoring` and `AIBT.Editor`, never
   referenced by `AIBT.Runtime`) hosts a small TCP listener with **no MCP
   SDK dependency**. On explicit start (an Editor menu/window command,
   mirroring `P3-009`/`P5-008`'s own-window opt-in pattern — never
   auto-started with the Editor, consistent with "must not be required in
   player builds"), it writes a discovery file under `Library/` (exact
   name/schema owned by `P6-005`; the spike's own
   `{"port", "process_id", "project_path", "unity_version"}` shape is
   proven constructible and is the recommended starting point) that the
   external `dotnet` server process reads to find and connect to the
   correct running Editor instance. Multiple concurrently open Editor
   instances are handled the same way `com.coplaydev.unity-mcp`'s own
   multi-instance routing already does in this project (one discovery
   file/port per instance; `P6-005` decides the exact routing/selection UX).
4. **Distribution.** Requires the .NET SDK on the user's machine; no
   vendored self-contained executable is shipped inside the AIBT UPM
   package. Documented as a plain, explicit prerequisite for AIBT's MCP
   features specifically — the core `Runtime`/`Authoring`/`Editor`
   assemblies gain no new dependency of any kind.
5. **Setup UX.** `P6-005` builds a real Editor menu command that detects
   `dotnet --version`, reports plainly (never silently) if missing, and
   writes/updates the AI client's MCP config entry automatically. It must
   not attempt an OS-level SDK install, and its documentation must state
   the AI client may need a restart/reconnect to pick up the change.
6. **Permission-model taxonomy** (shape only; `P6-005` implements
   enforcement). Every MCP tool/resource declares exactly one category from:
   `Read`, `SemanticEdit`, `LayoutEdit`, `CodeGeneration`, `Compilation`,
   `TestExecution`, `BenchmarkExecution`, `ArbitraryProjectIntegration`
   (verbatim from `ai-and-mcp.md`'s "Safe mutation protocol" list). A
   session is granted an explicit subset of categories; a call outside the
   granted set is rejected with a structured diagnostic, never silently
   downgraded or silently allowed. No category implies another.

## Consequences

- `P6-003` through `P6-012` are unblocked to proceed against this decision.
- `P6-005` builds the real server project, the real Unity-side bridge
  assembly, real discovery-file handling (including multi-instance
  routing and stale-file/crashed-Editor cleanup, not spiked here), the
  real permission-enforcement mechanism, and the real Editor
  config-writing command — all only proven feasible here, not shipped.
- Every later MCP tool card (`P6-006`-`P6-010`) declares its permission
  category from the taxonomy in item 6 above.
- AIBT's setup documentation must state the .NET SDK prerequisite plainly
  wherever MCP features are introduced.

## Explicitly unverified (stated, not generalized)

- Concurrent MCP clients against one server, or one server against
  multiple simultaneously open Unity Editor instances end-to-end (the
  discovery-file mechanism was proven for one instance only).
- Non-Windows hosting (this workstation is Windows x64 only).
- Authentication/remote transport (out of scope; stdio is local-only by
  construction).
- Stale-discovery-file handling when the Editor process that wrote it has
  since closed (the spike's listener was torn down cleanly after one
  connection; crash/unclean-shutdown recovery is real, disclosed follow-up
  work for `P6-005`).
- The exact new-assembly name/location is a working recommendation
  (`MCP/`), not yet a committed public API surface — `P6-005` may refine
  it, recorded as a card-level detail, not a re-opening of this ADR's
  process-model/transport/distribution decision.
