# P2-024 — Web unmanaged backend re-spike and conformance

Status: `Done`

## Objective

Repeat the accepted Web spike using the real native packed executor and decide its internal unmanaged/Burst implementation without changing public single-thread policies.

## Depends on

- `P2-012`.
- `P2-018`.
- `P2-020`.
- `P2-021`.

## Required reading

- `Documentation~/specifications/platform-backends-v1.md`
- `Planning~/Evidence/P0-003/ADR-P0-003-web-entry-point.md`
- `Planning~/Evidence/P0-003/README.md`

## Allowed changes

- `Spikes~/WebBackend/P2/`
- `Benchmarks~/Platform/Web/P2/`
- `Tools~/Verification/P2/Web/`
- `Planning~/Evidence/P2-WEB/`
- One accepted P2 Web implementation ADR.

## Forbidden changes

- Public job/Burst-direct Web policy, worker parallelism, BatchedJobsSameFrame, PipelinedJobs, hidden latency changes, or Safari/mobile claims without evidence.

## Deliverables

- Non-Development Web IL2CPP build, Chrome/Firefox conformance, unmanaged direct versus feasible Burst entry measurements, and accepted internal implementation decision.

## Acceptance criteria

- Applicable P1/P2 cases pass through `SingleThreadImmediate` and `SingleThreadBudgeted` with full semantic equivalence across partitions.
- Build size, warm raw steps/s samples, yields/frame budget, managed allocation signal with controlled probe, native-memory evidence where measurable, exceptions, browser versions, and throttling caveats are recorded.
- Public policy names and latency semantics remain unchanged.
- Unsupported `IJob.Run`/Burst-direct variants are reported, not fabricated or exposed.

## Required verification

```text
non-Development Web IL2CPP build
Chrome and Firefox full applicable behavior matrix
Immediate/Budgeted full-observable equivalence
allocation-probe and artifact/source hash checks
```

## Handoff notes

- Safari and mobile Web remain unverified until hardware/access exists.
