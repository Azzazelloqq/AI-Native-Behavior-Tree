# P9-005 — Shorter native node declarations

Status: `Review`

## Authorization and dependencies

Owner approved in chat on 2026-11-11. Depends on P9-004.

## Objective

Additive attribute overloads with defaults and field-type inference, and optional
`Enter`/`Abort`/`Exit`, so a simple Burst leaf is a few lines. ABI v2 output for existing
declarations must stay byte-identical. If the generator or ABI contract tests cannot accept an
additive form, stop and present options rather than changing the ABI.

Owned: `CodeGen~/`, `Analyzers/`, `Runtime/Nodes/Contracts/`, `Tests/Editor/CodeGen/`,
`Tools~/Verification/P2/CodeGen/`, `Documentation~/burst-node-authoring.md`, `CHANGELOG.md`,
`Planning~/Evidence/P9-005/`.

## Acceptance

A Condition and an Action declared with the short form compile, dispatch and pass the existing
generator matrix; the long form produces unchanged output.

## Decision

Overloads in `AIBT.Burst` would change the ABI v2 manifest; the owner chose option A: short
attributes in `AIBT.Native`, expanded by the generator (ADR-P9-005, AIBT-047).

## Verification

Roslyn matrix with `VerifyOptionalLifecycleCallbacks` and `VerifyShortForms`; byte comparison of
all 122 generator outputs of the `HEAD` matrix between the `HEAD` and the new generator; Unity
`ShortFormDeclarationTests` and `PublicBurstAbiContractTests`; full AIBT assemblies; public API
additivity; First Agent `MoveNode` in short form run in Play Mode.

## Handoff

Task ID: P9-005. Outcome: implemented and verified in the `Modules` host on Unity 6000.7.0b1;
status Review, not Done. Branch: feature/aibt-consumer-workflow; local commit only, no push.
Verification: Roslyn matrix 1452 assertions; 122/122 long-form outputs byte-identical to `HEAD`;
`ExpectedPublicAbiV2.txt` untouched and its tests green; `AIBT.Editor.Tests` 616/618 (two
pre-existing host-only failures). See ../../Evidence/P9-005/README.md.
Known limits: no short form for documentation, shards, snapshot/async/completion bindings; the
short Condition is proven by generation equivalence, not by a Play Mode run; the previous analyzer
binary is not reproducible with the installed SDK, and `Build-And-Verify.ps1` was not run end to end.
