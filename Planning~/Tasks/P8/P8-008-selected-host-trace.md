# P8-008 — Attach to a selected production host trace

Status: `In Progress`

## Objective

Make the existing read-only native trace available for any selected production host without sample-specific tooling.

## Dependencies

- P7-010 and P7-034 are complete; reuse the existing host channel and trace window.

## Scope

- Add an Editor command to attach to a selected live `ProductionTreeHost`.
- Preserve caller ownership and the existing safe trace snapshot protocol.
- Do not infer graph identity or claim source highlighting without a verified program mapping.

## Acceptance

- No selected host or uninitialized channel returns an actionable explanation.
- A live host attaches the existing trace session without changing execution.
- Focused Editor behavior tests and static verification pass.

## Current verification

- Unity 6000.7.0b1: 1481/1481 EditMode tests passed in an isolated consumer project.
- Focused tests verify a bootstrapped host attaches, unavailable channels are explained,
  and reattachment clears the previous graph mapping without disposing the host channel.
- Interactive Play-mode acceptance and the development baseline 6000.5.8f1 remain unverified.
