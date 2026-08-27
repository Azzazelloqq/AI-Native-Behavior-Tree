# P6-001 — MCP transport, hosting, and permission-model decision

Status: `Done`

## Objective

Decide, on real evidence rather than assumption, how AIBT exposes an MCP
server from this Unity C# package: SDK/library choice, process/hosting
model, transport, the new assembly's exact position in the dependency
graph, and the conceptual shape of the permission model every later MCP
tool card declares against. This card decides the model; it does not ship
a working server (`P6-005` does).

**Pre-decided by the owner during this card's own scoping (2026-08-27,
in-conversation, not to be silently re-opened by the spike):**

- **Process model: an external .NET process, not code hosted inside the
  Unity Editor's Mono domain.** The MCP protocol itself is spoken entirely
  by a standalone `dotnet` console process built on the official C# SDK; the
  Unity Editor side is a small bridge only (no MCP SDK dependency loaded
  into Unity at all). This mirrors the architecture the already-installed
  `com.coplaydev.unity-mcp` community package actually uses in this host
  project (confirmed by its own docs: "Install the Python server" — its
  real MCP-protocol server is an external Python process, not code running
  inside Unity; AIBT's own version of this is the same shape in C# instead
  of Python), and avoids the real assembly-version-conflict risk of
  vendoring the SDK's ~9 transitive NuGet dependencies into Unity's Mono
  domain (e.g. against the already-vendored `Assets/Plugins/Roslyn/
  System.Collections.Immutable.dll`).
- **Distribution: requires the .NET SDK installed on the user's machine.**
  The external server is launched by the AI client's own MCP config via
  `dotnet run --project ...` (or `dotnet <server>.dll`), not a vendored
  self-contained per-platform executable shipped inside the UPM package.
  This is an explicit, deliberate scope/UX tradeoff the owner chose over
  the (heavier, zero-dependency) vendored-binary alternative -- must be
  documented as a plain prerequisite for AIBT's MCP features specifically,
  never a silent requirement, and never implying the core `Runtime`/
  `Authoring`/`Editor` package depends on it.
- **One-button setup UX, scoped honestly.** An Editor menu command (e.g.
  `AIBT > MCP > Configure Claude Code`) should detect `dotnet --version`,
  report plainly if missing, write/update the AI client's own MCP config
  entry automatically (real precedent: `com.coplaydev.unity-mcp`'s own
  "Auto-configuration for popular MCP clients"), and may run one `dotnet
  build`. It must not attempt to silently install the .NET SDK itself (an
  OS-level installer action outside what an Editor command should do
  unprompted) and must not claim the AI client picks up the new server
  without possibly needing its own restart/reconnect.

## Depends on

- `P5-010` (Phase 5 integration gate; Phase 6 entry per `MASTER_PLAN.md`).

## Required reading

- `Documentation~/ai-and-mcp.md` (entire document — the normative contract
  this decision must satisfy).
- `Documentation~/architecture.md`'s "Layers" (`### MCP server`), "Core data
  ownership," and "Dependency direction" sections — note the existing
  diagram already places `MCP` as a sibling of `CodeGen`/`Editor`, not a
  child of `Runtime`, and states MCP "is not required in player builds."
- `Planning~/OPEN_QUESTIONS.md`'s closed-and-must-not-reopen list: "MCP is
  optional and not a runtime dependency."
- `Planning~/Evidence/P5-GATE/phase6-inputs.md` — the concrete shape
  (`HotReloadPreviewDriver`) later MCP tools will wrap, and the
  reference-executor-only disclosure obligation this decision's assembly
  boundary must not make harder to honor.
- `Planning~/DECISION_BOUNDARIES.md` — "public or cross-assembly API shape"
  and "new package dependency, assembly reference... platform conditional"
  are both "must escalate" categories this card exists to resolve properly.
- The real, installed `com.coplaydev.unity-mcp` (`Packages/manifest.json`)
  and its bridge architecture (external process + thin Unity-side C#
  listener) -- direct architectural precedent for the pre-decided process
  model above.
- `Library/EditorInstance.json` (Unity's own first-party discovery file:
  `process_id`, `version`, `app_path`, `app_contents_path`, confirmed
  present in this host project) -- precedent for how an external process
  discovers a running Unity Editor instance; AIBT's own bridge needs an
  analogous file carrying its own listener port.
- The C# SDK's proven console-app pattern (`dotnet new console`, `dotnet
  add package ModelContextProtocol`, `Microsoft.Extensions.Hosting`,
  `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`) is
  the shape to spike against directly, not `ModelContextProtocol.Core`
  alone (which lacks the hosting/DI builder extensions used here).

## Allowed changes

- `Spikes~/McpTransportModel/` (new, disposable) -- including a standalone
  `Spikes~/McpTransportModel/Server/` `.csproj` external to Unity's Asset
  Database (a normal `dotnet` console project, not a Unity assembly).
- `Planning~/Evidence/P6-001/`.
- One proposed ADR; integration owner applies accepted decision updates to
  `Documentation~/decisions.md` and `Documentation~/ai-and-mcp.md`.

## Forbidden changes

- Production `MCP/`, `Runtime/`, `Authoring/`, or `Editor/` implementation
  of any server, transport, or tool — this card decides the model on paper,
  backed by a disposable spike proving the chosen transport is at least
  constructible end-to-end (one resource + one tool call, real client, real
  response), it does not ship a server.
- Introducing a package dependency (e.g. an MCP C# SDK) into any
  `package.json`/`.asmdef` outside the disposable spike without the
  decision being accepted first.
- Weakening "MCP is optional and not a runtime dependency" or "no lower
  layer may reference a higher layer" to make the transport simpler.

## Deliverables

- Confirmation (not re-decision) that the external-.NET-process model,
  `dotnet`-SDK-required distribution, and one-button client-config UX
  (all pre-decided by the owner, above) actually work end-to-end -- this
  card's real job is proving the pre-decided shape is constructible, plus
  deciding the remaining open details below.
- A decided SDK package set: `ModelContextProtocol` +
  `Microsoft.Extensions.Hosting` (the proven hosting/DI builder pattern),
  backed by a real `dotnet add package`/`dotnet run` check on this
  workstation -- not a web search alone, per this project's own spike
  discipline.
- A decided transport (stdio, confirmed as the natural fit for a
  client-launched external process) and exactly which real MCP client this
  was checked against.
- A decided Unity-side bridge shape: the discovery-file contract (path,
  contents -- port plus a project-identity token, mirroring
  `Library/EditorInstance.json`'s own field shape) and the listener's
  startup trigger (recommend explicit Editor menu/window start, mirroring
  `P3-009`/`P5-008`'s own-window opt-in pattern, consistent with "must not
  be required in player builds" -- confirm or correct this recommendation
  with the spike's own findings).
- A decided new-assembly shape for the Unity-side bridge: name, which
  existing assemblies it may reference (`AIBT.Authoring`, `AIBT.Editor` --
  confirm or correct `architecture.md`'s existing diagram rather than
  silently assuming it), and confirmation the reverse never holds. The
  external server project itself is outside Unity's assembly graph
  entirely (a plain `.csproj`), so this only concerns the bridge.
- A decided permission-model taxonomy (read, semantic edit, layout edit,
  code generation, compilation, test execution, benchmark execution,
  arbitrary project integration, per `ai-and-mcp.md`'s "Safe mutation
  protocol") as a concrete type every later tool card's Deliverables must
  declare against — not implemented enforcement yet (`P6-005` implements
  enforcement), just the shape.
- A disposable spike proving the chosen model end-to-end: the external
  server compiles and runs via `dotnet run`, a real MCP client connects to
  it, lists at least one resource, calls at least one no-op tool, and
  receives a structured response.
- A disposable proof (may be a hand-tested manual run, not necessarily a
  polished feature) of the one-button config-writing step: an Editor menu
  command detects `dotnet --version` and writes a syntactically valid
  `.mcp.json`-shaped entry -- feasibility only, `P6-005` builds the real
  version.
- A proposed ADR recording the decision and its rationale.

## Acceptance criteria

- The decision states explicitly how a player build remains unaffected
  (no new player-facing assembly reference, no new player-facing
  dependency) — checked, not assumed.
- The decision states explicitly what happens when no MCP client is
  connected (the Editor and any Player must work identically to today).
- The spike used a real MCP client (not a hand-rolled fake) to exercise
  the transport at least once.
- The ADR states exactly what remains unverified (e.g., concurrent
  clients, authentication/remote transport, cross-platform hosting)
  rather than generalizing.

## Required verification

```text
Verify-Static.ps1
disposable spike: real MCP client connects, lists a resource, calls a tool
```

## Handoff notes

- `P6-003` through `P6-012` are blocked on this card's ADR being accepted,
  not merely on this card being `Done` — mirrors how `P3-001` and `P5-001`
  gated every later card in their phases.
- If the natural transport choice cannot actually run inside/alongside the
  installed Unity Editor on this workstation, iterate rather than shipping
  a known-broken decision — the same discipline `P3-001`'s spike applied
  when it rejected Unity Graph Toolkit on real evidence.

## Outcome

Accepted 2026-08-27: `Documentation~/decisions/ADR-P6-001-mcp-transport-and-permission-model.md`
(`AIBT-024`). Candidate A' (external `dotnet` process on the official C# MCP SDK, stdio
transport, no-SDK-dependency Unity-side bridge over a discovery file) selected, mirroring
this project's own already-installed `com.coplaydev.unity-mcp`'s architecture (external
process + thin Editor listener; confirmed via its own docs -- "Install the Python server")
in C# instead of Python. Distribution (requires .NET SDK on the user's machine) and setup
UX (one-button Editor config-writing command, never silent SDK install) were owner
decisions made directly in conversation before the spike ran, then confirmed working, not
re-derived. All four spike checks passed against real tooling: the external server compiled
clean on the first attempt; the official Anthropic `@modelcontextprotocol/inspector` CLI
(a genuine external MCP client, not a hand-rolled fake) round-tripped `tools/list`,
`tools/call`, `resources/list`, and `resources/read` successfully; a live-Editor
`TcpListener` + `Library/AibtMcpSpike.json` discovery file (mirroring Unity's own
`Library/EditorInstance.json` shape) was read and connected to by a genuinely external
process; and `dotnet --version` detection plus valid-JSON config writing were both proven
from inside the Editor process. One planned verification path failed on its first attempt and was disclosed immediately
rather than hidden: adding the spike server to this repo's own `.mcp.json` mid-session did
not let this Claude Code session's own MCP-client tooling see it, because Claude Code does
not dynamically reconnect servers from a mid-session `.mcp.json` edit. Rather than
substituting a proxy client and stopping there, the owner was asked directly ("То есть mcp
не тестился?") and agreed to restart their Claude Code session with the spike server
already registered. After the real restart, the actual target client — not the Inspector
CLI, Claude Code itself — listed `mcp__aibtMcpSpike__aibt_spike_ping`, called it
successfully, and listed/read the `aibt-spike://status` resource, matching the Inspector
CLI's earlier results exactly. Full detail in `Planning~/Evidence/P6-001/`.
