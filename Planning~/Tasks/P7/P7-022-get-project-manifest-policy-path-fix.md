# P7-022 — `get_project_manifest` policy-path resolution fix

Status: `Done`

## Objective

Found live while generating a real example for `P7-019`'s aggregate-manifest schema: **`MCP/
McpToolDispatcher.cs`'s `GetProjectManifest(projectRoot)` never successfully finds `.aibt/
policy.json` in this repository's real, live deployment topology, and has never done so in any real
MCP session against this project.** It resolves the policy path as
`Path.Combine(ProjectRootParent(projectRoot), ".aibt", "policy.json")`, where `ProjectRootParent`
strips one trailing path segment from `projectRoot`. Every real production caller
(`MCP/McpBridgeWindow.cs`, `MCP/McpBridgeAutoRestart.cs`) constructs the listener with
`projectRoot = Application.dataPath` (i.e. `.../Modules/Assets`), so `ProjectRootParent` resolves to
`.../Modules` and the tool looks for `.../Modules/.aibt/policy.json` — but the real file lives at
`.../Modules/Assets/AIBT/.aibt/policy.json` (nested inside the AIBT package folder itself, since
this host project embeds AIBT directly under `Assets/AIBT` rather than as a registered `Packages/`
entry). **Confirmed live, twice**, via `execute_code` calling `McpToolDispatcher.Dispatch` with the
exact real production `projectRoot` value: both calls return the degraded error shape
(`{"format":..., "formatVersion":1, "error":"Project policy could not be read: ...", "skippedTreeFiles":[...]}`)
instead of the real capabilities/policy/tree data — see `Planning~/Evidence/P7-019/
example-project-manifest-policy-error.json` for the real captured response. This is the same class
of environment-path-assumption bug `P7-021` already fixed in a different file
(`McpApiReferenceGenerator.CollectTypeSummaries`) — a hardcoded relative-path assumption that never
matched this repository's actual host-embedded layout.

## Depends on

- `P6-005` (the MCP server host / `McpToolDispatcher` this card fixes).
- `Planning~/Evidence/P7-019/example-project-manifest-policy-error.json` (the real, live-captured
  evidence of the bug).

## Required reading

- `MCP/McpToolDispatcher.cs`'s `GetProjectManifest`/`ProjectRootParent` (the bug).
- `MCP/McpBridgeWindow.cs`/`MCP/McpBridgeAutoRestart.cs` (confirm the real, current production
  `projectRoot` value passed to the listener — `Application.dataPath`, at the time this card was
  written; re-verify, don't assume, since this is exactly the kind of assumption that caused the
  bug).
- `Tests/Editor/Mcp/Discovery/McpToolDispatcherTests.cs`'s own `_assetsDir`/`_projectRoot` fixture
  setup (models `.aibt/policy.json` as a sibling of a synthetic `Assets/`, at the fake project root —
  confirm whether this test fixture's own assumption also needs to change, or whether it already
  matches a *different*, valid deployment topology this fix must not break, e.g. a real `Packages/`-
  registered UPM consumer where `Application.dataPath`'s parent genuinely is the right place to look).
- Whatever other MCP call sites use `ProjectRootParent`/a similar path-derivation pattern — confirm
  whether this is an isolated bug or a repeated pattern (`AibtTreeDiscovery.Scan`'s own path handling
  works correctly with `Application.dataPath` today, confirmed live — this card's own bug is
  isolated to the policy-path derivation specifically, not tree discovery, but check other MCP tools
  reading `.aibt/policy.json` or similarly-located per-project files too).

## Allowed changes

- `MCP/McpToolDispatcher.cs`'s policy-path resolution (and any other call site found to share the
  same bug during investigation, disclosed if found rather than silently expanding scope).
- `Tests/Editor/Mcp/Discovery/` — new/updated tests proving the fix against both the host-embedded
  layout (this repository's own real topology) and whatever the existing fixture's topology
  represents, if that also needs to keep working.
- `Planning~/Evidence/P7-022/`.

## Forbidden changes

- Changing where `.aibt/policy.json` is expected to live from the project-author's own perspective
  (`ai-and-mcp.md`'s documented convention) — this card fixes the tool's own path *resolution* to
  correctly find the file where it already, legitimately lives, not moving the file or changing the
  convention.

## Deliverables

- `get_project_manifest`, called with the real production `projectRoot` value against this real,
  live, currently-open project, returns the real success shape (capabilities/nodeRegistryHash/
  nodeCount/policy/trees) instead of the degraded error shape.
- A regression test proving this specifically, run against a fixture (or the real project) that
  matches this repository's actual host-embedded topology, not only the existing fixture's own
  (apparently different) assumption.

## Acceptance criteria

- Live proof against the real, open project (not only a synthetic fixture): `McpToolDispatcher
  .Dispatch("get_project_manifest", ..., Application.dataPath)` returns a real, non-error response
  with the actual project's own real `nodeRegistryHash`/`policy`/`trees`.
- The existing `McpToolDispatcherTests` suite (and any other MCP test suite exercising this path)
  passes unchanged, or is updated with a disclosed, reasoned explanation if its own fixture
  assumption turns out to need correcting too.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
live get_project_manifest call against the real, open project with the real production projectRoot value
```

## Handoff notes

- Spun off from `P7-019`'s own session (2026-09-03) — found while generating a real schema example,
  not fixed inside that card per its own Allowed-changes fence (schema/`Verify-Schemas.ps1`/evidence
  only). This is a real, currently-live production defect affecting a core discovery tool in every
  real MCP session against this project — worth prioritizing, not just filed away.

## Outcome

**The card's own premise was wrong.** Live re-verification (2026-09-03) found
`MCP/McpToolDispatcher.cs`'s `GetProjectManifest`/`ProjectRootParent` path resolution is not a bug:

- `ProjectRootParent(Application.dataPath)` always resolves to the true Unity project root
  (`<ProjectRoot>/Assets` -> `<ProjectRoot>`), **regardless** of whether AIBT is embedded under
  `Assets/AIBT` or registered via `Packages/` -- unlike `P7-021`'s bug, this resolution is not
  topology-dependent, because `Application.dataPath` is always `<ProjectRoot>/Assets` in every Unity
  project no matter where the AIBT package itself lives.
- This is the **documented, intentional** convention, confirmed independently in two earlier,
  unrelated evidence records this card did not originally check: `Planning~/Evidence/P6-005/
  README.md` ("`.aibt/policy.json`, which is a per-consuming-project file expected at the project
  root") and `Planning~/Evidence/P6-007/README.md` ("`validate`'s project-policy support only reads
  `.aibt/policy.json` at the project root (sibling to `Assets/`), same resolution
  `get_project_manifest` already uses").
- `Tests/Editor/Mcp/Discovery/McpToolDispatcherTests.cs`'s own fixture already models exactly this
  convention (`.aibt/policy.json` as a sibling of a synthetic `Assets/`, at the fake project root) --
  it was not a mismatched/outdated assumption as the card speculated.
- `C:\UnityProjects\Modules\.aibt\policy.json` (the real project root of this host repository)
  simply did not exist. The file the card pointed to, `Assets/AIBT/.aibt/policy.json`, is AIBT's own
  internal self-hosting/dev policy (present since the AIBT repository's very first commit,
  `768636e`) -- a file belonging to the AIBT package's own repo, not a policy for the `Modules` host
  project that embeds it.

**Real fix applied, put to the owner and approved rather than assumed:** a real
`.aibt/policy.json` was added at `C:\UnityProjects\Modules\.aibt\policy.json` (the parent `Modules`
repository, outside the AIBT submodule entirely -- not a change owned or committed by this card/
submodule). No line of `MCP/McpToolDispatcher.cs` changed. `get_project_manifest`, called with the
real production `projectRoot` against the real, open project, now returns the real success shape
instead of the degraded error shape. See `Planning~/Evidence/P7-022/README.md` for the live proof.
