# Phase 5 inputs

Prepared 2026-08-19 for the `P3-013` review. Phase 5 owns hot reload, state
migration, and persisted execution state. Phase 3 establishes the
editor/compiler revision-stability guarantees Phase 5's design depends on;
it does not implement hot reload itself.

## What Phase 5 inherits

- **A proven, automated layout/semantic isolation invariant** (`P3-007`):
  every manual-organization action and auto-layout run leaves
  `CompiledProgram.Header.CompiledContentHash` byte-identical, while a
  genuine semantic edit changes it. Hot reload can therefore use the
  compiled content hash as its own change-detection signal for "does this
  edit require a running tree instance to actually reload," without
  building a second detector -- a layout-only change is provably a no-op for
  any live instance; a semantic change provably is not.
- **A single canonical compiled artifact both execution backends agree on**
  (inherited from Phase 1/2, reconfirmed unchanged by Phase 3's own detached-
  harness regression): `CompiledProgram` and its content hash are the same
  object whether produced for the managed reference oracle or the native
  executor, so a future hot-reload mechanism has one artifact identity to
  track, not two.
- **A working example of driving the reference executor from outside its
  own assembly boundary** (`P3-009`'s `ReferencePreviewDriver`, a public
  `AIBT.Authoring` facade over the internal `ReferenceExecutionMachine`):
  the same pattern -- and the same escalation discipline (crossing an
  internals-visibility boundary is a decision, not a detail) -- applies if
  hot reload needs to swap a running instance's compiled program without
  restarting the process.
- **A working example of reading a bounded native channel from outside the
  native execution path without perturbing it** (`P3-010`'s
  `NativeExecutionDebuggerSession`): the same read-only, no-writer-lease-
  required pattern is directly relevant if hot reload needs to inspect a
  running native instance's state before deciding whether/how to migrate it.

## Required before any hot-reload claim

1. Decide what "reload" means for a *semantically* changed tree with a live
   instance mid-execution (abort and restart? migrate in-place? explicitly
   unsupported for a first cut?) -- Phase 3 deliberately made no such
   decision; layout-only reloads are the only case it proves is safe.
2. A production leaf-behavior registration mechanism (see
   `known-limitations.md`) likely needs to exist before hot reload is
   meaningful for real project content, since today only Phase 1 fixture/
   built-in node kinds are executable at all.
3. Define how a reloaded compiled program's content-hash change propagates
   to whichever execution backend (managed or native) currently owns a live
   instance -- this gate confirms the hash itself is a reliable signal, not
   how a consumer should act on it.

## Constraints Phase 5 must not violate

- Node coordinates, colors, groups, and comments still never influence
  semantics or reload decisions (the same `P3-007` invariant, used as a
  guarantee, not merely respected).
- A hot-reload path must not weaken `P3-006`'s "every semantic edit is gated
  by the real compiler/validator" contract to make reloading more
  convenient.
