# P6-002 domain-patch, revision, and diff model decision evidence

## Result

Resolved via `Documentation~/decisions/ADR-P6-002-domain-patch-revision-and-diff-model.md`
(`AIBT-025`, Accepted 2026-08-27). Full decision, rationale, and consequences are in the ADR;
this file records the evidence behind it.

## Provenance: this card was already corrected once, then corrected further before the spike

While implementing `P6-003`, research found `Runtime/Core/Identity/Revision.cs` and
`Editor/Editing/SemanticEditTransaction.cs` already implement most of what this card's
original prose assumed needed inventing -- recorded in `P6-003`'s own evidence and applied to
this card's task file at the time.

Before running the spike for this card specifically, further reading (not assumed) found one
more structural fact neither correction had stated: `Editor/Editing/
SemanticEditOperations.cs` takes/returns only `TreeDocument`; `Editor/Organization/
LayoutOrganizationOperations.cs` (`SetNodePosition`, `Pin`/`Unpin`, groups, notes, reroutes)
takes/returns only `LayoutDocument` -- confirmed by reading every public method signature in
both files, not assumed from either file's name. `LayoutDocument`
(`Editor/Layout/LayoutDocument.cs`) has no revision or content-hash field at all -- confirmed
directly, and confirmed absent from `Documentation~/specifications/editor-layout-v1.md` too.

## The spike: proves the model against real documents, not simulated

`Spikes~/DomainPatchModel/` (run live via Unity MCP `execute_code` against the actually-open
Unity `6000.5.8f1` Editor, mirroring `P5-001`'s own disposable-spike methodology -- no
external process needed here since everything is plain C# against `TreeDocument`/
`LayoutDocument`):

1. **Registry setup**: `NodeRegistryBuilder.CreateWithBuiltIns()` plus its own internal
   `AddTestFixtures()` (invoked via reflection from the spike snippet, since `execute_code`'s
   dynamically-compiled code has no `InternalsVisibleTo` grant -- the same registry shape
   every other Phase 1-5 test already uses, not a special one built for this spike) produced
   14 registered types including the three `aibt.test.*` fixtures
   (`success`/`failure`/`running`) needed for a compilable 3-level tree (Phase 1's built-ins
   are all composites/decorators requiring at least one child -- confirmed while building the
   fixture: there is no zero-child built-in leaf at all).
2. **Atomicity, invalid case**: disconnecting an inverter's only required child (a real
   `ChildPolicy(1,1)` violation) through `SemanticEditTransaction.Apply` was rejected
   (`invalidRejected=True`), returned the exact same document reference
   (`invalidUnchangedRef=True`), and carried real diagnostics (`invalidHasDiagnostics=True`).
3. **Atomicity, valid case**: a two-operation composition (add a `succeeder` decorator under
   root, then add a `failure` leaf under it) was accepted (`validAccepted=True`), grew the
   node count 3 -> 5, and a purpose-built diff correctly reported
   `[added:guard2,added:leaf2]`. (First attempt at this step had a genuine bug in the spike's
   own test construction -- pre-populating a node's children in its constructor *and* also
   connecting it via `AddNode` produced a real duplicate-child diagnostic, `AIBT2024`; fixed by
   constructing the node with no children and letting `AddNode` make the connection, then
   re-run.)
4. **Expected-revision precondition**: a thin wrapper rejected a stale expected revision
   without ever invoking the edit function (proven via a side-effecting counter staying at
   zero, `mismatchRejectedWithoutRunningEdit=True`) and ran it exactly once on a match
   (`matchRanEdit=True`).
5. **Revision-per-patch fact**: the two-operation valid patch above moved the document from
   revision 1 to revision 3, not 2 -- `SemanticEditOperations` increments per individual call,
   confirmed directly (`resultRevision=3`), not assumed. Recorded as an explicit caller-facing
   contract requirement in the ADR (always use the actual returned revision, never assume
   "+1").
6. **Layout content-hash revision check**: `AIBT.StableHash.Sha256Hex(CanonicalLayoutJsonWriter
   .Write(layoutDocument))` changed after a real `LayoutOrganizationOperations.Pin` call
   (`hashChanged=True`), was stable across repeated computation on the same unchanged document
   (`sameHashOnRepeatedCompute=True`), and correctly gated a mismatch/match precondition
   wrapper of the same shape as the semantic one (`layoutMismatchRejected=True`,
   `layoutMatchAccepted=True`).
7. **Layout diff**: a purpose-built field comparison correctly reported
   `[pin-changed:guard=True]` for the same `Pin` call.
8. **Layout/semantic isolation holds by construction**: not re-tested at runtime (would be
   redundant with `P3-007`'s own already-accepted proof) -- confirmed instead by reading every
   public `LayoutOrganizationOperations` method signature and finding none accepts or returns
   a `TreeDocument`; there is no code path for a layout patch to touch semantic revision or
   produce a non-empty semantic diff.

## Decision

See `ADR-P6-002` in full. Summary: two patch kinds (semantic/`TreeDocument`/`Revision`,
layout/`LayoutDocument`/content-hash), never unified; dry-run is free (no persistence step
exists to skip); revision/hash contract requires callers to always use the actual returned
value; diffs are purpose-built node/field-level comparisons.

## Scope and limitations

- No production code ships from this card, per its own Forbidden changes. `P6-004` builds the
  real transaction wrappers, diagnostic codes, and diff serialization.
- Concurrent callers, very large patches, and cross-tree patches were not exercised --
  explicitly disclosed in `ADR-P6-002`'s "Explicitly unverified" section.
- One workstation, one Unity version (`6000.5.8f1`) -- no cross-platform claim is made or
  needed; this is a data-structure/API-behavior fact, not a performance measurement.

See `verification-results.json` for exact commands and results.
