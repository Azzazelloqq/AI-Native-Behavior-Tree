# P7-023 — showcase example behavior trees

Outcome: **Done**, 2026-09-04. Two new source-package examples use only production nodes from
`NodeRegistryBuilder.CreateWithBuiltIns()` and each has a separate committed layout.
The package manifest exposes the folder as the importable **Showcase Trees** sample.

## Delivered examples

- `Samples~/ShowcaseTrees/timed-sequence.aibt.json`: memory sequence with three Wait actions.
  Actual reference result by update: `Waiting`, `Waiting`, `Waiting`, `Completed/Success`.
- `Samples~/ShowcaseTrees/parallel-decorators.aibt.json`: require-all Parallel with a repeated Wait,
  Inverter/Failer branch, and longer Wait. Actual result: `Waiting`, `Waiting`, `Completed/Success`.

For both examples, canonical parse succeeded, `TreeValidator` returned zero errors, and
`ReferenceCompiler` succeeded with zero errors against the built-in registry. The neighboring
layouts loaded with `UsedDefault=false`, contained every semantic node (4 and 7), and the graph
reported zero diagnostics and zero pairwise node overlaps.

Execution used the real unmodified `ReferenceExecutionMachine`, its production composite/decorator/
parallel registries, and the production `aibt.stdlib.wait` behavior from
`NodeRegistryBuilder.TryGetProjectLeafBehavior`. The disposable Unity MCP probe injected that
behavior into the machine's internal leaf registry by reflection because this was live evidence,
not package code.

The public `ReferencePreviewDriver.TryCreate` validates and compiles both examples but then faults
on their first tick: its fixed `ReferenceLeafRegistry.CreatePhase1Fixtures()` does not install the
production stdlib handlers already present in its node registry. That pre-existing preview limitation
is documented rather than hidden. It does not affect compilation, the native production catalog, or
the direct reference-machine result above. P7-023 is content-only and does not widen preview APIs.

## Visual evidence

- [Timed Sequence](timed-sequence.png): 4 nodes, authored 360/320-pixel horizontal spacing.
- [Parallel and Decorators](parallel-decorators.png): 7 nodes across three branches and four depths.

Both screenshots are from the real focused Unity 6000.5.8f1 `AIBT Graph` window at 100% graph zoom.
Titles come from committed `displayName` fields, edges match semantic child order, and no fallback
position was used.
The window currently has no file picker; the live proof called its public `OpenFromPath` API via MCP.

## Verification

- `Tools~/Verification/Verify-Static.ps1`: passed, 7 schemas and 137 work items.
- Live canonical parse, validation, compilation, layout load and graph open: 2/2 passed.
- Live reference execution: 2/2 reached the documented Success result at updates 4 and 3.
- Unity console: zero errors after the verification.
- `git diff --check`: passed.

No production code, existing golden fixture, new node type, semantic format, runtime host, or
scheduler behavior changed. These examples demonstrate logical tick delays and traversal only;
Parallel explicitly does not claim simultaneous execution or worker-thread concurrency.
