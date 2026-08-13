# P0-003 — Unity Web and Burst WASM spike

Status: `Draft`

## Objective

Select the production single-thread Web execution entry point using a real Player build and recorded measurements.

## Depends on

- `P0-001`
- `P0-002`

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
