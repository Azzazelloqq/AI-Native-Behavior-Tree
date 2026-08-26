# P5-001 — Hot-reload compatibility model decision

Status: `Done`

## Objective

Resolve `OQ-007`: define what "reload" means for a semantically changed tree
with a live instance mid-execution, and the compatibility-classification
rule every later Phase 5 card depends on. This card decides the model; it
does not implement identity/versioning, restart, or migration itself.

## Depends on

- `P4-009` (Phase 4 integration gate; the scheduler contract this model must
  not weaken is accepted output).

## Required reading

- `Documentation~/hot-reload.md` (this phase's consolidated contract; read
  every section, especially "Compatibility classification," "Reload
  strategies," and "Open questions").
- `Documentation~/testing.md`'s "Hot-reload tests" section (the five required
  compatibility categories this model must classify).
- `Documentation~/specifications/compiled-program-v1.md`'s "Debug and
  hot-reload map" section.
- `Planning~/Evidence/P3-GATE/phase5-inputs.md` and
  `Planning~/Evidence/P4-GATE/phase5-inputs.md` (what is already proven and
  must not be re-litigated: `CompiledContentHash`'s layout/semantic
  isolation invariant, one shared `CompiledProgram` identity across both
  backends, scheduler-policy semantic equivalence).

## Allowed changes

- `Spikes~/HotReloadCompatibilityModel/` (new, disposable).
- `Planning~/Evidence/P5-001/`.
- One proposed ADR; integration owner applies accepted decision updates to
  `Documentation~/decisions.md` and `Documentation~/hot-reload.md`.

## Forbidden changes

- Production `Runtime/`, `Authoring/`, or `Editor/` implementation of any
  identity, versioning, restart, or migration mechanism -- this card decides
  the model on paper (backed by a disposable spike proving the model is at
  least constructible), it does not ship it.
- Weakening `P3-007`'s layout/semantic isolation invariant or any accepted
  Phase 4 policy's proven semantic equivalence to justify a simpler model.
- Assuming a production leaf-registration mechanism exists; the model must
  work against the same Phase 1 fixture/built-in node set every other Phase
  3 editor surface was built and tested against.

## Deliverables

- A decided definition of "reload" for a live instance mid-execution: does
  an incompatible change abort-and-restart the whole instance, abort-and-restart
  only the affected subtree, or is compatible-in-place migration attempted
  first with restart as fallback -- stated as a decision tree, not prose.
- A decided compatibility-classification rule mapping each of `testing.md`'s
  five change categories (parameter edit, insertion, removal, reordering,
  type-version change), alone and in combination, to compatible/incompatible,
  and to full-restart/subtree-restart/migration when compatible.
- A decided node-identity/program-version/state-layout-hash scheme sufficient
  for `P5-002` to implement directly, building on `CompiledContentHash`
  rather than introducing a second, competing identity.
- A disposable spike proving the decided model is constructible against a
  real (not synthetic-toy) compiled program shape, at minimum covering one
  case from each of the five change categories.
- A proposed ADR recording the decision and its rationale.

## Acceptance criteria

- Every one of `testing.md`'s five change categories has a stated, reasoned
  compatible/incompatible verdict -- no category is left ambiguous or
  deferred silently.
- The decision explicitly states what happens to an in-flight async
  operation and an uncommitted random stream on each reload strategy,
  consistent with `Documentation~/hot-reload.md`'s existing async/determinism
  sections (it may cite them, not contradict them).
- The spike demonstrates the model against real `CompiledProgram` data, not
  a hand-rolled toy structure unrelated to the actual compiled format.
- The ADR states exactly what remains unverified (e.g., very large trees,
  concurrent reloads, native-vs-managed backend parity) rather than
  generalizing.

## Required verification

```text
Verify-Static.ps1
disposable spike harness constructing and classifying real CompiledProgram pairs
```

## Handoff notes

- `P5-002` through `P5-009` are all blocked on this card's ADR being
  accepted, not merely on this card being `Done` -- mirrors how `P3-001`'s
  ADR blocked `P3-002` through `P3-013`, and `P4-007`'s `ADR-P4-007` closed
  `OQ-006` before later cards relied on the answer.
- If the spike finds the naively-expected model does not survive contact
  with the real compiled-program format, iterate the model rather than
  shipping a known-broken decision -- the same discipline `P3-001`'s spike
  applied when it rejected Unity Graph Toolkit on real evidence.

## Outcome

Accepted 2026-08-27: `Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md`
(`AIBT-023`). Real code (not just spec prose) was read first: `ReferenceCompiler.OrderNodes`/
`IndexNodes` assign compiled node index by a fresh pre-order DFS traversal on every compile (zero
stability across recompiles); every live-state array in both backends is flatly indexed by that
unstable index; the native layer already hard-rejects cross-generation execution by design
(`AIBT4311`); a Memory composite's running cursor is a positional `uint`, not a stable child ID.
**Decision: reload is never an in-place array mutation -- it is always construct-fresh-and-
selectively-copy, keyed by stable authoring node ID.** Full restart, subtree restart, and
compatible migration are the same mechanism with a different exclusion set (whole tree / localized
subtree / empty), not three independent implementations -- `P5-004`/`P5-005`/`P5-006` were
corrected accordingly before implementation starts. The disposable spike
(`Spikes~/HotReloadCompatibilityModel/`) proved the classifier against real `CompiledProgram`
pairs for all five `testing.md` categories, run live via Unity MCP: 5/5 passed. Full detail in
`Planning~/Evidence/P5-001/`.
