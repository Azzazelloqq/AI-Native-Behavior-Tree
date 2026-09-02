# P7-013 samples expansion evidence

## Result

Done. Adds two new samples beyond the existing `Samples~/BurstNodes`/`Samples~/SemanticSlice`, per
`Documentation~/scope.md`'s "Samples, recipes, anti-patterns" release-criteria line and
`Planning~/Evidence/P6-GATE/phase7-inputs.md`'s own explicit suggestion. No production file was
touched, and neither existing sample's own content changed, per this card's own Forbidden-changes
clause.

## A real, structural finding that re-scoped the second sample (owner-approved mid-implementation)

Investigated directly while implementing, not assumed: the original "fuller example" deliverable
asked for a custom node, an explicit (non-`Auto`) scheduling-policy choice, and a hot-reload pass in
one sample. Two real blockers, confirmed against this session's own `P7-001` public-API dump and
the actual driver source:

1. **No public production API exists to drive a compiled tree with a chosen scheduling policy at
   all.** `SchedulingPolicyDriver` and every native-backend execution type are `internal`, confirmed
   by grepping `Planning~/Evidence/P7-001/public-api-current.txt` (this package's own
   verified-complete public-surface dump) for zero matches. `P7-010`'s own accepted decision names
   what a future production host looks like, but no implementation card exists for it yet.
2. **The one public tree-preview entry point cannot run a custom node either.** Both
   `AIBT.Authoring.ReferencePreviewDriver` and `AIBT.Authoring.HotReloadPreviewDriver` compile
   exclusively against a fixed built-in/fixture registry
   (`ReferencePreviewFixtureEnvironment.CreateNodeRegistry()`) -- their own doc comments still say
   "AIBT ships no production per-project leaf-behavior registration mechanism yet," which is now
   stale relative to `P7-008`'s own new public `IReferenceLeafBehavior` extension point (neither
   preview driver was updated to accept it).

Surfaced directly to the owner via `AskUserQuestion` (mid-implementation, after the plan had already
been approved but before this specific finding was known) with three options: trim the sample to
hot-reload-only, widen `ReferencePreviewDriver` to accept project extensions (a real production-file
change, its own escalation), or defer this card entirely. **The owner chose to trim the sample** to
demonstrate exactly what today's public API can actually do, disclosed plainly rather than faked.

## What was built

**`Samples~/CustomMcpToolProvider/`** (new): a real `ICustomMcpToolProvider` implementation
(`aibt_sample_greeting`), mirroring `Tests/Editor/Mcp/CustomTools/Fixtures/`'s own already-proven
shape (`P6-010`'s real fixture) with a fresh sample-namespaced implementation. Declares the full
9-member contract (name, description, input/output schemas, permission category, side effects,
cancellation support, dry-run support) with real values.

**`Samples~/FullExample/`** (new): two real `.aibt.json` documents walking through a complete
hot-reload pass via `HotReloadWorkflowWindow` (`P5-008`). No custom node or scheduling-policy
demonstration, per the re-scoping above -- the sample's own README discloses exactly why, in detail,
rather than silently downscoping without comment.

## Live verification

**Custom-tool-provider sample** (mirroring `P6-005`/`P6-010`'s own established pattern exactly):
- Sample temporarily copied to `Assets/_SampleVerificationTemp/` (outside `AIBT/`, matching this
  session's own established temp-verification pattern) to compile against the live Editor -- 0
  errors.
- Bridge started live in the real open Editor via `AIBT.Mcp.McpBridgeListener` (public API, called
  directly through Unity MCP `execute_code`) -- real `Library/AibtMcp.json` discovery file
  confirmed written.
- Official `@modelcontextprotocol/inspector --cli`, configured via a `.mcp.json`-shaped config file
  (`"env": {"AIBT_MCP_PERMISSIONS": "Read"}`, per `P6-005`'s own disclosed Windows env-passthrough
  workaround), against the real, permanent `MCP~/Server/` (`dotnet run --project
  Assets/AIBT/MCP~/Server`):
  - `tools/list` -- 33 real tools, `aibt_sample_greeting` present among them.
  - `tools/call aibt_sample_greeting {"name":"World"}` -- real response:
    `{"greeting":"Hello, World! (from a custom MCP tool provider)"}`.
- Bridge stopped cleanly, discovery file removed, temporary Inspector config and the temporary
  `Assets/_SampleVerificationTemp/` copy both deleted afterward.

**Full example's hot-reload pass** (Unity MCP `execute_code` against the real open Editor, reading
the window's own displayed state back via reflection into its private fields -- the same technique
`P5-008`'s own evidence used):
- Load `before.aibt.json`, reload (while still idle) to `after-compatible-reorder.aibt.json`: real
  output `Strategy: Compatible migration / Migrated: 3  Reset: 0  Dropped: 0`.
- A tick run on the migrated instance afterward: 2 active nodes, settled on the `Running` leaf --
  proves the migrated instance is genuinely live, not just structurally valid.
- **A real correction found live, not assumed**: the README's first draft claimed the `Running`
  leaf's own state "survives the reload." Running an actual tick *before* reloading (making the old
  instance genuinely active first) changed the real observed outcome to `Strategy: Full restart (old
  instance was still active)` -- the reference executor's own migration path is idle-only, falling
  back to full restart the moment any node is active (a real, disclosed, pre-existing limitation,
  distinct from `P7-012`'s own native-backend hot reload, which explicitly supports migrating an
  active instance). The README was corrected to describe both real, empirically-observed outcomes
  rather than the originally-assumed one.

## Verification

```text
Verify-Static.ps1 -- passed
Compile check: CustomMcpToolProvider sample compiled cleanly when temporarily present in the live
  Editor session (0 errors) -- a lighter-weight substitute for the full clone-based detached UPM
  harness (see Scope and limitations below)
Live: real MCP~/Server/ + @modelcontextprotocol/inspector --cli against the custom-tool-provider
  sample -- tools/list and tools/call both succeeded with real output
Live: FullExample's hot-reload pass via Unity MCP execute_code against the real open Editor -- both
  the idle (compatible migration) and active (full restart) outcomes verified
```

## Scope and limitations

- The full clone-based "detached UPM harness" (`Planning~/Evidence/P6-GATE/gate-runbook.md`'s own
  pattern -- fresh git clone, isolated project referencing it via a `file:` package path,
  `Run-UnityCompile.ps1`) was not run for these samples. Judged disproportionate: that harness's own
  real value is proving the *core package* resolves and compiles cleanly from a fresh checkout via
  its own UPM package reference, already proven repeatedly by `P4-008`/`P6-012`/`P7-001`'s own
  harness work this project. What's new here is one small, self-contained additional sample file
  compiling against the *already-proven* package surface -- the lighter compile-in-the-live-Editor
  check answers that exact question with the same compiler and references, at a fraction of the
  cost. Disclosed as a real scope choice, not silently substituted without comment.
- `Samples~/FullExample/` demonstrates neither a custom node nor an explicit scheduling-policy
  choice, per the owner-approved re-scoping above -- widening `ReferencePreviewDriver`/
  `HotReloadPreviewDriver` to accept a project's own `IReferenceLeafBehavior` registrations is
  recommended as a real, separate follow-up card, not attempted here (a production-file change,
  outside this card's own Allowed-changes).
- Neither existing sample (`BurstNodes`, `SemanticSlice`) was touched.
