# P6-001 MCP transport/hosting/permission-model decision evidence

## Result

Resolved via `Documentation~/decisions/ADR-P6-001-mcp-transport-and-permission-model.md`
(`AIBT-024`, Accepted 2026-08-27). Full decision, rationale, and consequences are in the
ADR; this file records the evidence behind it.

## Provenance: research before deciding, not assumed

Before any candidate was chosen, real current evidence was gathered (web search + doc
fetch, all dated 2026-08-27):

- The official C# MCP SDK (`ModelContextProtocol` / `ModelContextProtocol.Core`, NuGet,
  latest `2.2.0`) targets .NET 8.0 and .NET Standard 2.0, confirmed from the NuGet package
  pages and the SDK's own repository.
- Unity ships a first-party `com.unity.ai.assistant` ("Unity MCP," `2.0.0-pre.1`) with an
  in-process Editor bridge and documented third-party tool registration -- confirmed from
  Unity's own docs, but not installed in this host project.
- This host project's own installed `com.coplaydev.unity-mcp` (`Packages/manifest.json`,
  the exact server behind the `unityMCP` tools used throughout this session) was checked
  directly: its own docs say "Install the Python server" -- its real MCP-protocol server is
  an external Python process, not code running inside Unity. This directly informed
  Candidate A' (below).
- This host project's `ProjectSettings.asset` sets `apiCompatibilityLevel: 6`, and
  `Assets/Plugins/Roslyn/*.dll` is an existing precedent for vendoring third-party managed
  DLLs directly into Unity -- both checked directly from project files, not assumed.
- `Library/EditorInstance.json` was read directly from this exact project
  (`{"process_id":40100,"version":"6000.5.8f1",...}`) as real precedent for how an
  external process discovers a running Unity Editor instance.

## Candidates considered

See `ADR-P6-001`'s Context section for full detail. Summary: Candidate A (vendor the SDK's
DLLs into Unity's Mono domain) was rejected before spiking, on the real assembly-conflict
risk against `Assets/Plugins/Roslyn/*.dll` plus the architectural finding that no existing
Unity MCP integration (first-party or community) actually does this. Candidate B (register
into Unity's own official preview-status Unity MCP) was recorded but not selected --
depends on an optional, preview-status first-party package. **Candidate A' (external
`dotnet` process on the official SDK, thin no-SDK-dependency Unity-side bridge) was
selected by the owner directly in conversation**, mirroring `com.coplaydev.unity-mcp`'s own
proven architecture in C# instead of Python.

Distribution (requires .NET SDK on the user's machine, no vendored binary) and setup UX
(one-button Editor command for config-writing and SDK detection, never silent SDK install)
were also owner decisions made directly in conversation, recorded in the task card's own
"Pre-decided by the owner" section and confirmed working by this spike, not re-derived.

## The spike: proves the model against real tooling, not simulated

`Spikes~/McpTransportModel/Server/` (a plain `dotnet new console` project, `dotnet add
package ModelContextProtocol 2.2.0` + `Microsoft.Extensions.Hosting`, entirely outside
Unity's Asset Database):

1. **Compiles.** `dotnet build`: 0 warnings, 0 errors, first attempt.
2. **Real external client round-trip**, using the official Anthropic
   `@modelcontextprotocol/inspector` CLI (`npx @modelcontextprotocol/inspector --cli dotnet
   run --project Spikes~/McpTransportModel/Server -- --method ...`), launching the server
   exactly the way an AI client's own MCP config would:
   - `tools/list` -> `aibt_spike_ping` with its declared JSON schema.
   - `tools/call aibt_spike_ping message="hello from P6-001 spike"` ->
     `"AIBT MCP spike pong: hello from P6-001 spike"`.
   - `resources/list` -> `aibt-spike://status`.
   - `resources/read aibt-spike://status` -> the exact static status text.
3. **Unity-side discovery-file bridge**, run live in the actually-open Unity `6000.5.8f1`
   Editor via Unity MCP `execute_code` (not a separate file, per this card's own disposable
   spike discipline mirroring `P5-001`'s pattern): a `TcpListener` bound to an ephemeral
   loopback port, port + process ID + project path + Unity version written to
   `Library/AibtMcpSpike.json`. The written `process_id` (`40100`) matched the real,
   independently-read `Library/EditorInstance.json`'s own `process_id` for the same running
   Editor session, confirming the discovery file genuinely identifies the live Editor
   instance. A real external Node process (outside Unity, outside the spike server) read
   the discovery file and connected to the reported port, receiving
   `"aibt-spike-bridge-echo: hello from external process"` back.
4. **One-button config-writing feasibility**, also run live via `execute_code`: `dotnet
   --version` invoked from inside the Editor process returned `10.0.300`, matching the
   workstation's real installed SDK (independently confirmed via a plain shell `dotnet
   --version` call outside Unity). A syntactically valid `.mcp.json`-shaped JSON fragment
   was written and independently validated with `JSON.parse`.

5. **Real Claude Code round-trip, after a genuine session restart.** The first attempt at
   this (mid-session `.mcp.json` edit, step 3 above) failed to connect -- disclosed
   honestly rather than silently substituted. The owner was told plainly and asked to
   restart their Claude Code session with `aibtMcpSpike` already registered in
   `.mcp.json`. After the real restart, the session listed
   `mcp__aibtMcpSpike__aibt_spike_ping` as an available tool, called it and received
   `"AIBT MCP spike pong: hello from real Claude Code client, P6-001"`, listed the
   `aibt-spike://status` resource via `ListMcpResourcesTool`, and read it via
   `ReadMcpResourceTool` -- identical results to the Inspector CLI. This is the actual
   target client (Claude Code), not a proxy for it, closing the one real gap step 3 left
   open.

No step failed by the end of this evidence cycle; the one step that did fail on its first
attempt (step 3's mid-session reconnect) was disclosed immediately rather than papered
over, and was independently re-verified with the real client once the owner restarted
their session. Candidates B/C were not spiked further, per the card's own decision
priority (`ADR-P6-001`).

All scratch artifacts (`Library/AibtMcpSpike*.json`, the temporary `.mcp.json`
`aibtMcpSpike` entry used to attempt the in-session-client path before switching to the
Inspector CLI) were removed/reverted after evidence capture; `Spikes~/McpTransportModel/`
itself is retained per this card's own Allowed changes (matching `Spikes~/WebBackend/`'s
precedent from `P0-003`).

## Decision

See `ADR-P6-001` in full. Summary: external `dotnet` process on the official C# MCP SDK,
stdio transport, thin no-SDK Unity-side bridge over a discovery file, .NET-SDK-required
distribution, honestly-scoped one-button setup UX, fixed permission-category taxonomy.

## Scope and limitations

- No production code ships from this card, per its own Forbidden changes. `P6-005` builds
  the real server, bridge, permission enforcement, and config-writing command.
- Concurrent clients, multiple simultaneously open Editor instances end-to-end, non-Windows
  hosting, and stale-discovery-file/crash recovery were not exercised -- stated explicitly
  in `ADR-P6-001`'s "Explicitly unverified" section, not generalized from the single-instance
  proof here.
- One workstation, one Unity version (`6000.5.8f1`), one .NET SDK version (`10.0.300`) --
  no cross-platform or cross-version claim is made.
- Adding the spike server to this repo's own `.mcp.json` mid-session does not
  hot-reconnect -- Claude Code only picks up a new MCP server entry from its own startup.
  This is a genuine limitation of live-session reconfiguration, not of the server; it was
  worked around, not silently ignored, by asking the owner to restart their session (step
  5 above), which then verified the real target client end-to-end.

See `verification-results.json` for exact commands and results.
