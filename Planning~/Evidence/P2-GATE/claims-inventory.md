# Phase 2 claims inventory

Prepared 2026-08-18 for the `P2-025` review, updated the same day once `P2-022`
landed. Every supported claim below already has committed evidence. The gate
itself is not accepted.

## Supported claims

- Non-Development Windows x64 IL2CPP with Burst enabled executes generated
  dispatch and the representative behavior matrix without managed fallback
  (`Evidence/P2-WINDOWS/`); no Windows performance default or threshold is
  claimed.
- The native executor reproduces all five Phase 1 golden behavior cases under
  Immediate, one-step Budgeted, and BatchedJobsSameFrame, observing native
  lifecycle, blackboard versions, observer replacement, async operations and
  commands, diagnostics, trace, active node, and step outputs.
- Burst node dispatch is generated, closed, and prebound from a frozen analyzer;
  no reflection, virtual dispatch, or managed payload participates in the measured
  native path.
- Twelve measured initialized execution windows across the three fixed policies
  recorded zero `GC.Alloc` events, with a controlled allocation proving the
  recorder was sensitive during the same run.
- Native program, instance, blackboard, snapshot, command, and diagnostic storage
  is fixed-capacity and rejects overflow rather than growing.
- Native lifetime is safe under success, abort, semantic fault, recreate/restart,
  capacity rejection, and final disposal with Unity native leak detection enabled.
- Unity `6000.5.8f1` compiles the package as a detached UPM installation and passes
  902 EditMode tests with no compiler, Burst, or native-leak failure marker.
- Android ARM64 IL2CPP with Burst enabled produces a non-Development build
  containing only `lib/arm64-v8a` with `libil2cpp.so` and `lib_burst_generated.so`.
- Non-Development Unity Web IL2CPP with Burst enabled executes generated dispatch
  in desktop Chrome `151.0.7922.138` and Firefox `153.0.4`, and both browsers
  reproduce the exact Immediate versus one-step Budgeted lifecycle trace.
- The Package Manager `Public Burst Nodes` sample compiles and runs in a clean
  Unity project using only the public generated API.

## Claims intentionally not made

- Any performance default, scheduling threshold, batch size, or crossover point.
  Phase 4 owns calibrated policies.
- Device performance on Android, or any battery, thermal, or store claim. Android
  evidence is build and AOT compatibility only.
- Safari, mobile browser, or Web worker parallelism support. Web evidence covers
  the two tested desktop browsers on one headless run.
- Zero allocation outside the twelve measured initialized windows. Initialization,
  compilation, managed and reference nodes, and host materialization are excluded.
- `PipelinedJobs`, `Auto`, and runtime autotuning. These are declared scope, not
  implemented behavior.
- A managed fallback path for Burst nodes. Managed nodes remain an explicit,
  separately declared execution boundary.
- Hot reload, the visual editor and debugger, and MCP integration.
- Linux, macOS, or console support.
- Stable public API compatibility beyond the recorded experimental `0.1.0`
  baseline.
