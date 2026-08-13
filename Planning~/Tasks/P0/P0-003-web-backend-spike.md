# P0-003 — Unity Web and Burst WASM spike

Status: `Done`

## Objective

Select the production single-thread Web execution entry point using a real Player build and recorded measurements.

The representative workload is the accepted P1-018 semantic slice. The spike MUST NOT invent a throwaway substitute executor before the reference executor exists.

## Depends on

- `P0-001`
- `P0-002`
- `P1-018`

## Required reading

- `specifications/platform-backends-v1.md`
- `specifications/determinism-v1.md`
- `Documentation~/benchmarks.md`

## Allowed changes

- `Spikes~/WebBackend/`
- `Benchmarks~/Platform/Web/`
- `Planning~/Evidence/P0-003/`
- One proposed ADR; integration owner applies accepted decision updates.

## Forbidden changes

- Production runtime API or an unsupported multithreading claim.

## Deliverables

- Minimal representative executor in unmanaged immediate, `IJob.Run`, and direct Burst-compatible variants where buildable.
- Chrome and Firefox results; Safari marked unverified until macOS access exists.
- Build compatibility, GC, native memory, throughput, frame budgeting, build size, and limitation report.
- Recommended backend entry point with evidence.

## Acceptance criteria

- A Web Player runs the same behavior assertions as the Editor reference.
- Unsupported variants are reported with build/runtime evidence.
- No result is generalized beyond tested browser, machine, and versions.
- Recommendation does not change semantic contracts.

## Required verification

- Production-like Web Player build.
- Browser execution of deterministic behavior cases and allocation/throughput scenario.

## Outcome

- Non-development IL2CPP WebGL build: **pass**.
- Chrome `151.0.7922.137`: **pass**, 5/5 behavior cases plus full normalized equivalence for reactive blackboard/abort and async-command scenarios.
- Firefox `153.0.4`: **pass**, 5/5 behavior cases plus full normalized equivalence for reactive blackboard/abort and async-command scenarios.
- Recommended policies: `SingleThreadImmediate` and `SingleThreadBudgeted` over identical semantic contracts.
- `IJob.Run` and Burst-direct execution of the managed reference executor: **unsupported**; repeat with the native packed executor.
- Safari and mobile: **unverified**.

Evidence: `Planning~/Evidence/P0-003/`. Pilot measurements: `Benchmarks~/Platform/Web/`.
