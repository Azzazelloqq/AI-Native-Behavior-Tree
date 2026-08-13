# Proposed ADR: Web single-thread entry points

Status: `Accepted`

## Context

Unity Web runs the accepted P1-018 semantic slice through the managed reference executor on a single thread. The representative WebGL Player passed the same behavior assertions under immediate and one-semantic-step budgeting in Chrome and Firefox. The current managed executor is neither an unmanaged packed executor nor a Burst-compatible job payload.

## Decision

Expose `SingleThreadImmediate` and `SingleThreadBudgeted` as the Web execution policies, without changing behavior-tree semantics. Do not publish a job-scheduled or Burst-direct Web policy until a representative native packed executor exists and this spike is repeated.

Automatic policy selection, if added later, must be benchmark-derived and remains outside this decision.

## Consequences

- Web callers can choose completion-in-call or explicit frame-cooperative budgeting.
- Chrome and Firefox functional evidence is available for the tested versions and host only.
- Safari, mobile, native-memory behavior, and native Burst throughput remain open verification work.
