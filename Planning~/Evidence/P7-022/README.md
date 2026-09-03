# P7-022 `get_project_manifest` policy-path resolution — evidence

## Result

Done — but not the fix the card described. Live re-verification of the card's own central claim
found it to be wrong: `MCP/McpToolDispatcher.cs`'s `GetProjectManifest`/`ProjectRootParent` path
resolution is not a topology-dependent bug the way `P7-021`'s was. `MCP/McpToolDispatcher.cs` was
not touched.

## What the card got wrong

The card claimed the tool's own path *resolution* needed fixing to find `.aibt/policy.json` "where
it already, legitimately lives" (`Assets/AIBT/.aibt/policy.json`, inside the embedded AIBT package).
Re-checking before implementing found:

1. **`ProjectRootParent(Application.dataPath)` is not topology-dependent.** `Application.dataPath`
   is always `<UnityProjectRoot>/Assets`, regardless of whether AIBT is embedded under `Assets/AIBT`
   or registered as a `Packages/` entry. `ProjectRootParent` strips one segment and always lands on
   the real Unity project root either way. This is a materially different situation from `P7-021`'s
   bug, where the *correct* answer genuinely differed by install topology.
2. **The project-root resolution is the documented, intentional convention**, confirmed
   independently in two evidence records that predate this card and were not checked before it was
   written:
   - `Planning~/Evidence/P6-005/README.md` (finding 1): "`.aibt/policy.json`, which is a
     per-consuming-project file expected at the project root" (i.e. sibling to `Assets/`, *not*
     inside the AIBT package folder).
   - `Planning~/Evidence/P6-007/README.md`: "`validate`'s project-policy support only reads
     `.aibt/policy.json` at the project root (sibling to `Assets/`), same resolution
     `get_project_manifest` already uses" — i.e. this exact resolution is already shared,
     consistent, deliberate behavior across at least two independent tools.
3. **`Tests/Editor/Mcp/Discovery/McpToolDispatcherTests.cs`'s own fixture already matches this
   convention exactly** (`.aibt/policy.json` as a sibling of a synthetic `Assets/`, at the fake
   project root) — it was not a stale/mismatched assumption, as the card speculated it might be.
4. **The real defect was a missing file, not a code bug.** `C:\UnityProjects\Modules\.aibt\
   policy.json` — the real project root of this host repository — did not exist.
   `Assets/AIBT/.aibt/policy.json` (the file the card pointed to) is AIBT's own internal
   self-hosting/dev policy, present since the AIBT repository's very first commit (`768636e`,
   "docs: define agent-ready implementation contracts") — a file that belongs to the AIBT package's
   own repo, not a policy authored by/for the `Modules` host project that embeds it as a submodule.

Put to the owner rather than resolved unilaterally: fix the code to also look inside the embedded
package (expanding the documented convention, forbidden by the card's own Forbidden-changes clause
and contradicted by the two independent evidence records above), or add the missing project-root
policy file. The owner chose the latter.

## Real fix applied

A real, schema-valid `.aibt/policy.json` was added at `C:\UnityProjects\Modules\.aibt\policy.json`
— the parent `Modules` repository, entirely outside the `Assets/AIBT` submodule. Content mirrors
AIBT's own dev policy (`Assets/AIBT/.aibt/policy.json`) as a reasonable starting default. This file
is not owned or committed by the AIBT submodule/this card's own `Planning~/Evidence/P7-022/` scope;
it is committed separately at the parent-repository level.

## Live verification

- **Before the fix**: `Planning~/Evidence/P7-019/example-project-manifest-policy-error.json`
  (captured during `P7-019`'s own session) already shows the exact computed policy path,
  `C:\UnityProjects\Modules\.aibt\policy.json`, confirming the resolved path was correct — the file
  at that path just did not exist yet.
- **After the fix**: live call via `mcp__unityMCP__execute_code`,
  `McpToolDispatcher.Dispatch("get_project_manifest", ...)` with the real production `projectRoot`
  (`Application.dataPath`) against this real, open project. See `verification-results.json` for the
  exact captured response.

## Scope and limitations

- No `MCP/McpToolDispatcher.cs` (or any other AIBT source) change. `Tests/Editor/Mcp/Discovery/
  McpToolDispatcherTests.cs` is unaffected and unchanged; its own fixture already modeled the
  correct, now-confirmed convention.
- `C:\UnityProjects\Modules\.aibt\policy.json` is owned by the parent `Modules` repository, not by
  the AIBT submodule this card's own `Allowed changes`/`owns` scope covers — committed separately,
  with its own confirmation, per this session's established two-step commit discipline.
