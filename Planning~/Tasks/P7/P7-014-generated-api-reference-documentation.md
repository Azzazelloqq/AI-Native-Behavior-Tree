# P7-014 — Generated C# API reference documentation

Status: `Done`

## Objective

Decide and build (one card, mirroring `P6-021`'s own precedent for a mechanical, non-architectural
decision made and applied together) whether AIBT's public C# surface (`AIBT.Runtime`,
`AIBT.Authoring`, `AIBT.Editor`, `AIBT.Mcp` — 405+ types, 2067+ members per `P6-GATE`'s own dump)
needs a generated reference-documentation site/document, or whether XML-doc comments plus
`Documentation~/architecture.md` already satisfy `Documentation~/scope.md`'s "complete... API
documentation" release criterion. `MCP/Documentation/`'s existing generators
(`P6-011`) already cover the MCP-facing node catalog/workflow guide; this card is specifically about
the general C# public API, which has no generated reference today.

## Depends on

- `P6-012` (Phase 6 integration gate; the public-API baseline this card documents).

## Required reading

- `Planning~/Evidence/P6-GATE/public-api.txt` (the current public surface this card must document
  completely, not selectively).
- `MCP/Documentation/` (`P6-011`'s own generator pattern — "generator, not hand-maintained prose" is
  this project's established discipline; a new reference-doc mechanism should follow it, not
  hand-write and let drift).
- Every public type's own current XML-doc-comment coverage (check before assuming it is sufficient
  or insufficient).

## Allowed changes

- A new documentation generator (expected alongside `MCP/Documentation/`'s own pattern, or a
  standalone `Tools~/Verification/P7/ApiDocs/` script if a C# public-API doc generator does not
  belong in the MCP-specific assembly — decide and justify).
- `Documentation~/generated/` (new API reference output, if the decision is to generate one).
- XML-doc-comment additions on any currently-undocumented public member (mechanical, no behavior
  change).
- `Planning~/Evidence/P7-014/`.

## Forbidden changes

- Any change to a public member's signature, name, or behavior — this card documents the existing
  surface, it does not clean it up (that is `P7-001`'s own stability-review scope, not this card's).
- Hand-writing a reference document that a future member addition would silently leave stale — if
  the decision is "generate," it must be provably regenerate-and-diff-clean, per `P6-011`'s own
  drift-check discipline.

## Deliverables

- A decision: generate a full API reference, or explicitly rely on XML-doc comments plus
  `architecture.md` — argued from actual current XML-doc coverage, not assumed.
- If generating: a real generator, its output committed, and a drift-check test proving the
  committed output matches a fresh regeneration byte-for-byte.
- If relying on XML-doc comments: every public member gets one where missing, verified by a
  coverage check (a member with no XML-doc comment fails the check), not spot-checked by hand.

## Acceptance criteria

- 100% of public members in all four assemblies have either a generated reference entry or an
  XML-doc comment, whichever the decision chose — proven by a real coverage check, not sampled.
- If a generator was built, its own test suite includes a determinism check and a drift check
  against the committed output.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
public-member documentation-coverage check across all four assemblies (100%, not sampled)
if generated: regenerate-and-diff-clean check
```

## Handoff notes

- `P7-016` (Phase 7 gate) cites this card's coverage proof as part of its own "complete API
  documentation" release-criterion check.

## Outcome

Done. Decided to generate rather than rely on XML-doc comments, grounded in real current coverage
(~2.4-4.5% of public members, confirmed by a source-parse cross-checked two ways, not assumed) and
`architecture.md`'s own narrative-only scope (no per-type/per-member entries at all). New
`MCP/Documentation/McpApiReferenceGenerator.cs` mirrors `P6-011`'s own generator pattern exactly,
reflecting all four public assemblies live and writing `Documentation~/generated/
api-reference-{runtime,authoring,editor,mcp}.md`. Every public member gets its own signature line
(satisfying the card's own "generated reference entry" acceptance bar for 100% of the 2,528
reflected declarations); a type's own XML-doc `<summary>` is inlined where one exists in source (60
of 417 types, 14.4%) via a best-effort source-parse — member-level correlation was investigated and
found too fragile to attempt this pass, disclosed rather than silently attempted.

`Tests/Editor/Documentation/McpDocumentationGeneratorsTests.cs`'s existing drift-check was extended
for the four new files, and a new `GeneratedApiReferenceCoversEveryPublicMemberInAllFourAssemblies`
test proves the card's own literal 100%-coverage acceptance criterion mechanically (fresh reflection
vs. committed file, not sampled). Both passed live on the first run; full EditMode regression (1616
tests) shows no new failures beyond the 3 already-pre-existing, unrelated ones. See
`Planning~/Evidence/P7-014/README.md`.
