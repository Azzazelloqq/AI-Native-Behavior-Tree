# P12-006 — What of the simulation container is frozen and what may still change

Status: `Review` — filed and done 2026-11-25 from the SweetKingdom feedback on 0.2.0 (item C). Documentation only.
Acceptance is the orchestrator's.

## Objective

The consumer asked which members of the simulation container may still change before it is marked stable,
so they know what to wrap. Answer it in the package's documentation, from the code, without promising dates.

## Scope

- A section "API stability" in `Documentation~/simulation-container.md` with three lists, each built from
  the code of 0.2.0:
  - frozen, with the contract that freezes it;
  - experimental with no change planned;
  - may still change before stable, with the reason and what a consumer should wrap.
- A pointer to that section in the header note of the same page, and in the README's line for the page
  (the README states that public APIs are experimental).
- A `CHANGELOG.md` line under `[Unreleased]`.
- Not `Documentation~/specifications`.

## Sources the lists are built from

- The public surface: `Documentation~/generated/api-reference-runtime.md`, namespace `AIBT.Simulation`.
- The ABI contract: `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`. It lists the four binding
  attributes and the four handles of `AIBT.Burst`, and nothing of `AIBT.Simulation`.
- The experimental marks in the code ("Experimental (ADR-P10-008) until stage G5" on the settings, the
  channels, `SimulationResult`, `[AibtInput]` and the G4 debug types). The stage plan and decision D1 of
  ADR-P10-008.
- History: CHANGELOG (`MaximumStepsPerThink` 1 000 000 → 10 000, P10-011 F1), the 0.2.0 additions
  (`AgentTraceRecord.InputRow`, MCP `row`), and `SimulationResult` (values added per stage; `Unsupported`
  kept but not returned since G3).

## Acceptance

- Every member named in the section exists in `api-reference-runtime.md` (or in `AIBT.Native` /
  `AIBT.Burst` as named). No member of `AIBT.Simulation` is called frozen.
- The section promises no date and names no condition that the code or ADR-P10-008 does not name.
- `Tools~/Verification/Verify-Static.ps1` passes.

## Open questions (to the orchestrator)

1. The experimental mark is tied to stage G5 (Unity 7 and the 500-agent benchmark), which the owner has
   postponed. Should "stable" stay tied to G5, or be decided separately for 6000.6?
2. The `Step` overload with a job handle: no record of it was found in the package (planning, evidence,
   ADR). Where was it discussed, and would it be an addition beside `Step(uint)` or a replacement?
3. `SimulationResult.Unsupported` is kept but has not been returned since G3. Should it be kept when the
   container is marked stable, or removed before?
4. Should the G4 debugging surface be marked stable together with the core, or stay experimental after it?
5. The channel id (FNV-1a 64 of the name) is recorded in generated catalogs, but no specification names
   it. Should a specification name it before stable? That is a protected area: owner approval and an ADR.

## Evidence

The section itself; no test changes and no Editor run are needed. `Planning~/Evidence/P12-006/` is reserved for the orchestrator's acceptance record.
