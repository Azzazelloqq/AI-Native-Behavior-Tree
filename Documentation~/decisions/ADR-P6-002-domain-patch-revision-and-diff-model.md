# ADR P6-002: domain-patch, revision, and diff model

- Status: Accepted 2026-08-27
- Date: 2026-08-27
- Decision ID: AIBT-025

## Context

`Documentation~/ai-and-mcp.md`'s "Safe mutation protocol" requires domain-patch transactions
to accept an expected revision, support dry-run, and return semantic and layout diffs. No
dedicated spec file states how, and this card's own task file originally assumed a
transaction mechanism needed to be invented from scratch.

Research while implementing `P6-003` (and confirmed further before this card's own spike)
found that assumption wrong: `Runtime/Core/Identity/Revision.cs` and `Editor/Editing/
SemanticEditTransaction.cs` already implement most of this for semantic edits. This card's
real job, and the disposable spike (`Spikes~/DomainPatchModel/`, run live via Unity MCP
`execute_code` against the real, currently-open Editor -- same methodology `P5-001` used) it
is based on, confirmed exactly what already works, decided the two genuinely open pieces
(the expected-revision precondition, and the diff format), and found one further structural
fact not previously stated anywhere: **semantic and layout edits are separate operation
families on separate document types, enforced at the type level.**
`Editor/Editing/SemanticEditOperations.cs` only ever takes/returns `TreeDocument`.
`Editor/Organization/LayoutOrganizationOperations.cs` only ever takes/returns
`LayoutDocument` -- confirmed by reading every public method signature in both files.
`Planning~/Evidence/P3-007/README.md` already proves this holds at runtime (no layout
operation changes the compiled content hash) and states it is enforced at the type level too.

## Spike evidence (2026-08-27, this workstation, live against Unity `6000.5.8f1`)

All cases run against real `TreeDocument`/`LayoutDocument` instances, the real
`ReferenceCompiler`/`TreeValidator` (via `SemanticEditTransaction.Apply`), and the real
`aibt.test.*` fixture registry (`NodeRegistryBuilder.CreateWithBuiltIns()` plus its own
`AddTestFixtures()`, the same registry every other Phase 1-5 test uses):

- **Atomicity, invalid case**: a multi-step edit ending in a real `ChildPolicy` violation
  (disconnecting an inverter's only required child) was rejected; the returned document was
  the exact same object reference as the input (`ReferenceEquals` true) with real compiler
  diagnostics attached.
- **Atomicity, valid case**: a two-operation composition (add a decorator node, then add its
  child) was accepted; the result reflected both operations (node count 3 -> 5); a
  purpose-built node-level diff correctly reported `added:guard2, added:leaf2`.
- **Dry-run is free**: `SemanticEditTransaction.Apply` has no persistence step of any kind --
  calling it and discarding the result is already a complete, correct dry-run. No new
  production code path is needed for this.
- **Expected-revision precondition**: a thin wrapper checking `document.Revision.Value`
  against a caller-supplied expected value before even invoking the edit function was spiked
  and confirmed correct: a mismatch was rejected without the edit function ever running
  (proven by a side-effecting counter that stayed at zero); a match ran it exactly once.
- **Revision counts individual edits, not patches.** The valid two-operation patch above
  moved the document from revision 1 to revision 3 (one increment per
  `SemanticEditOperations` call inside the composed patch), not revision 2. This is existing,
  correct, unmodified behavior (`SemanticEditOperations` itself increments), not something
  this card changes -- but it is a real fact any caller-facing contract must state explicitly:
  a caller must always use the actual returned revision after a patch as the expected revision
  for its next patch, never assume "+1 per patch."
- **Layout content-hash revision check**: `AIBT.StableHash.Sha256Hex(CanonicalLayoutJsonWriter
  .Write(layoutDocument))` (both already-existing production types, no new hashing scheme)
  changed after a real `LayoutOrganizationOperations.Pin` call, was stable across repeated
  computation on the same document, and correctly gated a mismatch/match precondition wrapper
  the same shape as the semantic one.
- **Layout diff**: a purpose-built field comparison over `LayoutDocument.Nodes` correctly
  reported `pin-changed:guard=True` for the same `Pin` call.
- **Layout/semantic isolation holds by construction, not by a runtime check.** Every public
  method on `LayoutOrganizationOperations` takes and returns only `LayoutDocument`; there is
  no code path by which a layout patch could observe or mutate a `TreeDocument`'s `Revision`
  or produce a non-empty semantic diff. This was confirmed by reading the full method list,
  not by writing a redundant runtime test for something the type system already prevents --
  the same reasoning `P3-007`'s own evidence already documents for the compiled-content-hash
  case.

Full raw output: `Planning~/Evidence/P6-002/README.md` / `verification-results.json`.

## Decision

1. **Two patch kinds, never unified into one operation type.** A "semantic patch" operates on
   `TreeDocument` via `SemanticEditOperations`/`SemanticEditTransaction`, checked against
   `Revision`. A "layout patch" operates on `LayoutDocument` via
   `LayoutOrganizationOperations`, checked against a computed canonical-JSON content hash (no
   persisted-format change to `.aibt.layout.json` -- `LayoutDocument` gets no new field). A
   single domain-patch request from a caller is one kind or the other, never both; this
   matches the codebase's own existing type-level separation rather than inventing a new
   structure that would have to bridge it.
2. **Expected-revision precondition**: for a semantic patch, reject before running any edit
   operation if `document.Revision.Value != expectedRevision`. For a layout patch, reject
   before running any edit operation if the document's canonical-JSON content hash does not
   match the caller-supplied expected hash string. Both precondition failures are structured
   diagnostics (exact codes are `P6-004`'s to allocate at implementation time -- this ADR does
   not reserve production diagnostic codes, per its own Forbidden changes).
3. **Dry-run is "call the transaction, don't persist the result."** No separate dry-run code
   path exists or is needed for the semantic case (`SemanticEditTransaction.Apply` already has
   no persistence step). The layout case is the same shape by construction once `P6-004`
   builds an equivalent wrapper.
4. **Revision contract for callers**: after any accepted patch (semantic or layout), the
   caller must use the actual resulting revision/hash from the response as the expected value
   for its next patch -- never assume a fixed increment, since a multi-operation patch may
   advance the semantic revision by more than one.
5. **Diff formats**: a semantic diff is a node-level list keyed by stable `NodeId`
   (added/removed/changed-parameter), built by direct `TreeDocument.Nodes` comparison. A
   layout diff is a field-level list per `NodeId`/group/note/reroute key (moved/pinned/
   grouped/rerouted), built by direct `LayoutDocument` field comparison. Neither uses a
   generic deep-diff library, consistent with this codebase's existing preference for
   purpose-built comparisons (`P3-007`'s own content-hash check being the precedent).

## Consequences

- `P6-004` implements both transaction wrappers as real production code (`Authoring/
  Patching/` per its own task card), allocates real diagnostic codes for the two
  precondition-failure cases, and builds the two diff types as real, tested serializable
  output (this ADR only proves each piece is constructible, not their final JSON shape).
- `P6-006`/`P6-007` (MCP authoring/verification tools) expose semantic and layout patches as
  two distinct tool categories (or one tool with a required `kind` discriminator) rather than
  one generic "apply patch" operation that tries to accept mixed operations.
- No change to `.aibt.json`/`.aibt.layout.json`'s persisted format. No new field on
  `LayoutDocument`.

## Explicitly unverified (stated, not generalized)

- Concurrent patches from two callers against the same document (this ADR defines the
  precondition check, not a locking/queueing mechanism -- `P6-004`/`P6-005` decide whether one
  is needed).
- Very large patches or cross-tree patches (out of this spike's scope; nothing observed here
  suggests either is unsafe, but neither was measured).
- The exact diagnostic codes and final JSON diff schema -- `P6-004`'s job, this ADR decided
  the model, not the wire format.
