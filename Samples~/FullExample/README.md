# Full example -- hot reload workflow pass

Two real tree documents (`before.aibt.json`, `after-compatible-reorder.aibt.json`) that walk
through a complete hot-reload pass in the Editor -- every step below was actually run live against
the real window (Unity MCP `execute_code`), not merely described:

1. Open **`AIBT/Hot Reload Workflow`** (`P5-008`).
2. **Load...** `before.aibt.json` -- a sequence of a leaf that always succeeds and a leaf that stays
   `Running`.
3. **Reload From...** `after-compatible-reorder.aibt.json` -- the same two leaves, reordered, while
   the loaded instance is still idle (never ticked). Watch the outcome panel: `HotReloadPreviewDriver`
   classifies this as a **compatible migration** (both leaves keep their own identity across the
   reorder) -- real output: `Strategy: Compatible migration / Migrated: 3  Reset: 0  Dropped: 0`.
4. **Run Tick** -- the *reloaded* instance ticks normally (2 active nodes, settling on the `Running`
   leaf), proving the migrated instance is genuinely live and usable afterward, not just structurally
   valid.

**A real behavior worth knowing, found while verifying this sample live**: reloading is only a
*compatible migration* while the old instance is idle. Reload the same two documents again, but
**Run Tick** once on `before.aibt.json` *before* reloading (so a node is genuinely active first),
and the outcome panel instead reports `Strategy: Full restart (old instance was still active)` --
the reference executor's own migration path is idle-only by design (falls back to a full restart the
moment any node is active), a real, disclosed limitation distinct from the native backend's own hot
reload (`P7-012`), which explicitly does support migrating an active instance.

## A real, disclosed limitation this sample ran into

The original intent for this sample (per `P7-013`'s own card) was to combine a **custom** node, an
**explicit scheduling-policy choice**, and a hot-reload pass in one example. Neither of the first
two is achievable through public API today, confirmed directly while building this sample rather
than assumed:

- **No public production entry point exists to drive a compiled tree with an explicit scheduling
  policy at all.** `SchedulingPolicyDriver` and every native-backend execution type are `internal`
  (confirmed against `Planning~/Evidence/P7-001/public-api-current.txt`, this package's own
  verified-complete public-surface dump — zero matches). `P7-010`'s own accepted decision names what
  a future production host looks like, but no implementation card for it exists yet.
- **The one public tree-preview entry point cannot run a custom node either.** Both
  `AIBT.Authoring.ReferencePreviewDriver` and `AIBT.Authoring.HotReloadPreviewDriver` (the type this
  sample actually uses) compile exclusively against a fixed built-in/fixture registry
  (`ReferencePreviewFixtureEnvironment.CreateNodeRegistry()`) -- their own doc comments say so
  directly: "AIBT ships no production per-project leaf-behavior registration mechanism yet." That
  statement is now stale relative to `P7-008`'s own new public `IReferenceLeafBehavior` extension
  point, but neither preview driver has been updated to accept a project's own registered leaves, so
  there is still no way to preview a custom node live through either one.

This sample therefore demonstrates the hot-reload pass alone, on the fixed built-in/fixture node
set both preview drivers already support -- a real, complete, live-verified example of what `P5-008`
actually ships today, rather than a fabricated one pretending the custom-node/policy gaps don't
exist. Widening `ReferencePreviewDriver`/`HotReloadPreviewDriver` to accept a project's own
`IReferenceLeafBehavior` registrations is recommended as a real follow-up card, not attempted here
(out of `P7-013`'s own Allowed-changes, which does not include editing those production files).
