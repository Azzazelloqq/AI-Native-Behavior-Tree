# P1-019 — Phase 1 independent integration gate

Status: `Draft`

## Objective

Review and integrate the semantic vertical slice without introducing new behavior during conflict resolution.

## Depends on

- `P1-018`
- Every Phase 1 task accepted by independent review.

## Allowed changes

- Integration-owned assembly definitions, package metadata, changelog, task status index, and `Planning~/Evidence/P1-GATE/`.
- Mechanical conflict resolution across accepted task branches.

## Forbidden changes

- New semantics, relaxed tests, normative specification edits, performance claims, or Phase 2 implementation.

## Deliverables

- Integrated branch, full verification report, contract-compliance checklist, known limitations, and Phase 2 decomposition inputs.

## Acceptance criteria

- Clean checkout with initialized submodules passes compile, static validation, focused suites, and full Phase 1 suite.
- Golden behavior is independent of culture and step-budget partition.
- No Editor/MCP/DOTS dependency leaks into Runtime.
- No claimed Burst/jobs/Web/Android performance exists without corresponding evidence.
- All remaining open items are explicitly assigned to later work packages.
