# P7-019 aggregate project-manifest JSON Schema evidence

## Result

Done. `Schemas~/project-manifest.schema.json` (new) governs `get_project_manifest`'s real,
already-shipped response shape — described from the actual production source
(`Authoring/Discovery/ProjectManifestQuery.Build()` plus `MCP/McpToolDispatcher.GetProjectManifest`'s
own wrapping), not assumed. `Tools~/Verification/Verify-Schemas.ps1` gained two new real-document
validation pairs against the new schema.

## Fresh finding: `Verify-Schemas.ps1` did not actually validate 6 schemas before this card

`P7-019`'s own card text claims the script "currently validates 6 schemas against a real example
each." Re-read live, not assumed: it runs a metaschema-validity check (`--check-metaschema`) against
all schema *files* (the source of the "Schemas: N" count in its own output), but before this card it
only validated **2** schemas against a real committed *document*:
`work-item-index.schema.json`/`Planning~/work-items.json` and `policy.schema.json`/
`.aibt/policy.json`. `node-manifest.schema.json` and the other 3 pre-existing schemas have never
been wired into a real-document check — `P7-001`'s own "validated live" claim for those four was a
one-time manual check during that card's own session, never made permanent. This card adds
`project-manifest.schema.json` as the **3rd and 4th** permanently-wired real-document pairs (see
below — two pairs, not one), not "the 7th of 6 already-wired." Disclosed here rather than repeated.

## The real response shape has two forms, both discovered by reading the actual production code

`ProjectManifestQuery.Build()` alone does not describe the whole real response.
`MCP/McpToolDispatcher.GetProjectManifest(projectRoot)` (the true top-level response builder) always
injects a `skippedTreeFiles` array (from `AibtTreeDiscovery.Scan`) that `ProjectManifestQuery` itself
never emits, and — more significantly — returns a **completely different, minimal shape** when
`.aibt/policy.json` cannot be read: `{format, formatVersion, error, skippedTreeFiles}`, omitting
`capabilities`/`nodeRegistryHash`/`nodeCount`/`policy`/`trees` entirely. The schema is a `oneOf`
covering both real shapes (`$defs/success`, `$defs/policyError`), each validated against a real
captured example.

## Real, previously-undiscovered bug found while generating the example — disclosed, not fixed here

Generating a real example the intended way (calling `McpToolDispatcher.Dispatch("get_project_manifest",
...)` with the real production `projectRoot` value, `Application.dataPath`) returned the **error**
shape, not success — live, twice, reproducibly. Root cause: `GetProjectManifest`'s
`Path.Combine(ProjectRootParent(projectRoot), ".aibt", "policy.json")` resolves to
`C:\UnityProjects\Modules\.aibt\policy.json` for the real production `projectRoot`
(`Application.dataPath`), but the real file lives at `C:\UnityProjects\Modules\Assets\AIBT\.aibt\
policy.json` — this repository embeds AIBT directly under `Assets/AIBT`, and the tool's path
derivation was never adjusted for that layout. **`get_project_manifest` has never successfully
returned its real success shape in any actual MCP session against this project.** The same class of
bug `P7-021` already fixed elsewhere (`McpApiReferenceGenerator`'s hardcoded path assumption), found
independently here. Out of this card's own Allowed-changes fence (`MCP/McpToolDispatcher.cs` isn't
listed) — disclosed and spun off as `P7-022`, not fixed inline. The real captured error response is
committed as `example-project-manifest-policy-error.json` — genuine evidence of the bug, and one of
this schema's own two validated example documents.

## Producing the real success example despite the live bug

Called `ProjectManifestQuery.Build()` directly (unmodified real production type) plus
`AibtTreeDiscovery.Scan` and `ProjectPolicySnapshot.TryReadFile` against the real, correct policy
path (`Assets/AIBT/.aibt/policy.json`) — bypassing only the buggy path-derivation step, using
otherwise-untouched real production code and real project data (real registry hash, real policy
file, real tree documents). The full live capture returned 68 real tree entries (the whole repo's
own fixture/sample trees); `example-project-manifest.json` commits a trimmed-but-real 4-entry
subset for readability — every remaining field and entry is a real captured value, not
hand-invented.

## Verification

- `Verify-Static.ps1` — passed.
- `Verify-Schemas.ps1` — passed: 7 schema files pass the metaschema check; 4 real-document pairs
  validate cleanly, including both new `project-manifest.schema.json` pairs (success example,
  policy-error example).
- Deliberately malformed copy (a third, uncommitted variant: `capabilities.burst` removed,
  `policy.unreachableNodes` set to an invalid value) run through `check-jsonschema` directly:
  **fails**, exit code 1, with the exact injected defects named in the error output
  (`policy.unreachableNodes: 'not-a-real-enum-value' is not one of ['error', 'warning', 'allow']`) —
  proving the schema is not a rubber stamp, per the card's own acceptance criterion. Not committed.

## Scope and limitations

- Does not fix the live `get_project_manifest` policy-path bug found during this card's own work —
  spun off as `P7-022`, a real, currently-live production defect worth prioritizing.
- Does not wire the other 4 still-unvalidated pre-existing schemas (`node-manifest.schema.json`,
  `tree.schema.json`, `layout.schema.json`, `behavior-case.schema.json`) into `Verify-Schemas.ps1` —
  out of this card's own scope; a pre-existing gap, disclosed, not silently fixed in passing.

See `verification-results.json` for exact commands and results.
