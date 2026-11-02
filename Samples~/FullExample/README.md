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

The notes below describe the state when this sample was built. A production host and scheduling
API were added later; see `Documentation~/production-host.md`. The separate hot-reload preview
still uses the built-in/fixture registry. The ordinary reference preview now accepts project
reference leaf providers.

The original intent for this sample (per `P7-013`'s own card) was to combine a **custom** node, an
**explicit scheduling-policy choice**, and a hot-reload pass in one example. It demonstrates only
the hot-reload pass with built-in/fixture nodes; the later production host and scheduler are shown
in Swarm Arena. The hot-reload preview still does not accept project reference leaves. Supporting
them there requires a defined migration contract for project-managed state.
