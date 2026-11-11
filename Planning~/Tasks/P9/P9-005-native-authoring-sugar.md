# P9-005 — Shorter native node declarations

Status: `Draft`

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
