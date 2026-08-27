# P6-002 — Domain-patch, revision, dry-run, and diff model decision

Status: `Draft`

## Objective

Decide, on real evidence against the actual `TreeDocument`/`Editor/Editing/`
code (not spec prose alone), the shape of a domain-patch transaction: what a
patch operation is, how an expected revision is checked and produced, what
dry-run returns, and what a semantic diff versus a layout diff contains.
This card decides the model; `P6-004` implements it.

## Depends on

- `P5-010` (Phase 5 integration gate; Phase 6 entry per `MASTER_PLAN.md`).

## Required reading

- `Documentation~/ai-and-mcp.md`'s "Authoring" and "Safe mutation protocol"
  sections (the only normative statement of this contract that exists
  today — no dedicated spec file, confirmed absent from
  `Documentation~/specifications/`).
- `Authoring/Editing/` production code (`P3-006`'s add/remove/connect/
  disconnect/set-parameter operations, gated by the real
  `ReferenceCompiler`/`TreeValidator`, with undo/redo) — the closest
  existing building block; this card decides how to wrap it in an atomic,
  revision-checked, dry-run-capable transaction, not whether to replace it.
- `Planning~/Evidence/P3-007/` — the layout/semantic isolation proof this
  model must not weaken (a layout-only patch must never produce a semantic
  diff or bump the semantic revision).
- `Documentation~/specifications/identity-and-hashing-v1.md` and
  `CompiledContentHash` usage across Phase 3/5 evidence — whatever
  "revision" means here must not compete with or duplicate this existing
  identity/hash concept without a stated reason.
- `Documentation~/specifications/diagnostics-v1.md` — a rejected/failed
  patch (revision mismatch, invalid partial tree) must surface as a
  structured diagnostic, not a bespoke error shape.

## Allowed changes

- `Spikes~/DomainPatchModel/` (new, disposable).
- `Planning~/Evidence/P6-002/`.
- One proposed ADR; integration owner applies accepted decision updates to
  `Documentation~/ai-and-mcp.md`.

## Forbidden changes

- Production `Authoring/` implementation of the transaction engine itself
  (`P6-004`'s job) — this card decides the model on paper, backed by a
  disposable spike proving it is constructible against a real
  `TreeDocument`.
- Reinventing `P3-006`'s individual operations; the model must compose them,
  not duplicate their validation/compilation gating.
- Any persisted-format change to `.aibt.json`/`.aibt.layout.json` without
  a separately authorized schema version bump.

## Deliverables

- A decided patch-operation set: whether it is exactly `P3-006`'s existing
  operation list expressed as data, or a stated, justified superset/subset.
- A decided revision model: what "expected revision" means concretely (a
  monotonic counter, a content hash, or both), how a mismatch is reported,
  and how the produced revision after a successful apply is derived.
- A decided atomicity guarantee: on any single operation in a multi-operation
  patch failing validation/compilation, the entire transaction persists
  nothing — stated as a proof obligation for `P6-004`'s tests, not just
  prose.
- A decided dry-run contract: exactly what a dry-run response contains
  (would-be diagnostics, would-be diff, would-be revision) and the
  guarantee that dry-run never persists.
- A decided diff contract: separate semantic-diff and layout-diff shapes,
  consistent with `P3-007`'s isolation proof (a layout-only patch produces
  an empty semantic diff and an unchanged semantic revision).
- A disposable spike proving the model against a real `TreeDocument`: one
  successful multi-operation patch, one revision-mismatch rejection, one
  mid-transaction validation failure that persists nothing, and one
  dry-run that changes nothing on disk/in memory.
- A proposed ADR recording the decision and its rationale.

## Acceptance criteria

- The spike demonstrates atomicity with a real invalid operation inside an
  otherwise-valid multi-operation patch, not a hypothetical description.
- The spike demonstrates the layout/semantic diff separation against a
  patch that only moves/pins/groups nodes.
- The ADR states exactly what remains unverified (e.g., concurrent patches
  from two callers, very large patches, cross-tree patches) rather than
  generalizing.

## Required verification

```text
Verify-Static.ps1
disposable spike: real TreeDocument, atomicity/dry-run/diff/revision proof
```

## Handoff notes

- `P6-004`, `P6-006`, `P6-007` are blocked on this card's ADR being
  accepted, not merely on this card being `Done`.
- Can proceed in parallel with `P6-001`; the two decisions are independent
  (transport does not constrain the patch model, and vice versa).
