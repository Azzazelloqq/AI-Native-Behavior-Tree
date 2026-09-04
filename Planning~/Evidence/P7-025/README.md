# P7-025 — graph editor interactivity and readability

Outcome: **Done**, 2026-09-04. `AIBT Graph` remains a read-only semantic viewer, but now supports
normal GraphView navigation and selection, loads existing layout files, and renders readable titles.

## Observable behavior

- Standard zoom, pan, click/drag selection and rectangle selection manipulators are installed using
  the live Unity 6000.5.8f1 API. Nodes and edges are not deletable or copiable; disabled ports prevent
  creating or reconnecting edges.
- `OpenFromPath` uses `LayoutPersistenceController`. A valid neighboring layout supplies positions
  and auto-completes newly added nodes. A missing layout uses the existing grid without creating a
  file. An invalid layout clears stale content and exposes diagnostics instead of silently falling back.
- Explicit `NodeDocument.DisplayName` remains the title. Otherwise the Editor derives a readable
  last TypeId segment (`aibt.core.memory-sequence` -> `Memory Sequence`); the canonical TypeId remains
  in the tooltip. No semantic-format or manifest contract changed.
- Dragged positions are transient. Reopening reloads disk layout; the viewer writes neither file.

The committed [tree](interaction.aibt.json) and [layout](interaction.aibt.layout.json) are live-test
evidence rather than a package sample. They parsed and validated with zero errors, compiled through
the reference compiler, and loaded all four stored positions without fallback. P7-023 remains
responsible for production examples.

## Live Unity evidence

Unity MCP opened a 1000x650 `AIBT Graph` and sent real `EditorWindow.SendEvent` mouse input:

- wheel zoom changed scale `1.00` -> `0.87`;
- middle drag changed translation `(65.33, 42.00)` -> `(-14.67, -8.00)`, exactly `(-80, -50)`;
- left click selected exactly `root`; rectangle drag selected all four nodes and three edges;
- node drag moved root `(400, 60)` -> `(515.33, 117.33)` under zoom; reopen restored stored style
  position `(400, 60)`, with both source files SHA-256-identical.

[Screenshot after box selection](graph-box-selection.png) shows the focused Unity window, authored
positions, readable names, connections and blue selection outlines.

## Verification

- Focused graph adapter tests: **9/9 passed**. UI position cases wait for rendered frames and allow
  UI Toolkit's one-physical-pixel rounding on the 150% display.
- Final full host EditMode run via Unity MCP: **1732/1735 passed**, job
  `08e4c2dca8784389b0f335d2cbd106a1`. Two failures are the established embedded-package CodeGen
  `PackageInfo.FindForAssembly` baseline; the third is unrelated LocalSaveSystem autosave
  (`Expected: 9`, `But was: 0`). Graph and generated-documentation tests passed.
- Console: zero compilation errors. Editor API docs were regenerated with the repository command.
- `Tools~/Verification/Verify-Static.ps1`: passed, 7 schemas and 137 work items.
- `git diff --check`: passed.

`Run-UnityTests.ps1` was not started as a second Unity process because the project was already open
and locked. Unity MCP ran Unity Test Framework's full EditMode scope in that same project and
returned exact per-test results.

No semantic authoring, layout saving, groups/notes/reroutes rendering, automatic Frame All, runtime
change, new node field or migration was added. The next planned scope is P7-023.
