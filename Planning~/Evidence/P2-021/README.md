# P2 initialized allocation and lifetime evidence

Observed 2026-08-17 with Unity 6000.5.8f1 through
`Tools~/Verification/P2/Allocation/Run-AllocationGate.ps1`.

- Focused tests: 3/3 passed.
- Immediate: 4 initialized windows, 0 `GC.Alloc` events in every window.
- Budgeted/Resume: 4 initialized windows, 0 `GC.Alloc` events in every window.
- BatchedJobsSameFrame: 4 initialized windows, 0 `GC.Alloc` events in every window.
- Controlled 4096-byte allocation: 1 `GC.Alloc` event, proving recorder sensitivity.
- Success, abort, semantic fault, recreate/restart, capacity rejection, and final
  disposal all completed under Unity native leak detection.
- Native leak/error scan: clean.
- NUnit XML SHA-256:
  `8cd96888e901782da0be4d127c0c6da61fba6721adb08f7159b36baff14de0ca`.
- Unity log SHA-256:
  `87797a8ab9ef818f0293d5422cd93d52704a17a39ffc734a8bac080cd24df6d2`.

The claim is limited to the twelve measured initialized windows. Initialization,
compilation, managed/reference nodes, host materialization, and unmeasured platforms
are excluded. Raw XML/log/JSON remain ignored machine-local artifacts.
