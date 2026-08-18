# P2-025 — Phase 2 integration gate

Status: `Done`

## Objective

Verify the complete native Burst runtime, generated public node path, deterministic fixed scheduling policies, evidence boundaries, and repository hygiene.

## Depends on

- `P2-020`.
- `P2-021`.
- `P2-022`.
- `P2-023`.
- `P2-024`.

## Required reading

- Every P2 card and accepted P2 ADR/specification.
- `Planning~/DEFINITION_OF_DONE.md`
- `Planning~/Evidence/P1-GATE/`

## Allowed changes

- `Planning~/Evidence/P2-GATE/`
- Integration-owned package metadata, asmdefs, changelog, README, planning status/index, and public API baselines after verification.

## Forbidden changes

- New semantics, relaxed tests, runtime fixes, benchmark threshold invention, or platform claims stronger than evidence.

## Deliverables

- Clean detached-package verification report, contract checklist, public API/generated artifact hashes, dependency report, claims inventory, known limitations, and Phase 3/4 inputs.

## Acceptance criteria

- Static/schema, compile, full P1 regression, all behavior-case equivalence, public custom-node golden, allocation/lifetime, Windows Player/baseline, Android build, and Web gates pass from a clean committed snapshot.
- Generated Burst dispatch executes representative trees without managed fallback.
- Initialized measured native paths satisfy zero-GC; no broader allocation claim is made.
- Runtime dependency direction remains clean and excludes Editor, MCP, LLM, DOTS Entities, reflection, and platform conditionals as public policy.
- PipelinedJobs, Auto/autotuning, performance defaults, device performance, Safari/mobile, and managed fallback remain explicit future/limited scope where not implemented.
- Every required verification command in this card passes, and results are recorded under `Planning~/Evidence/P2-GATE/`.

## Required verification

```text
clean detached UPM harness
all P1 and P2 focused/full suites
native behavior-case policy matrix
allocation/lifetime gate
Windows/Android/Web evidence audits
public API, generated artifact, dependency, cleanliness, and diff checks
```

## Handoff notes

- The earlier remote `P0-005` Unity runner remains an infrastructure limitation explicitly waived for starting P2 by current owner direction; do not misreport it as passed.

## Current gate result

Accepted 2026-08-18 against commit `a78d10a0fb2f964d64e253b284ad1cf19730f936`.
See [`Planning~/Evidence/P2-GATE/README.md`](../../Evidence/P2-GATE/README.md)
and `verification-results.json` in that directory.
