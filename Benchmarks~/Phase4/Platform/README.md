# P4-008 platform benchmark evidence

Mirrors `Benchmarks~/Phase2/`'s per-platform structure: one subdirectory per mandatory pre-1.0
target (Windows x64, Android ARM64, single-thread Unity Web), each running `P4-001`'s scenario
matrix on real (non-Editor) execution for that platform.

- [`Windows/`](Windows/README.md) -- done. Real IL2CPP/Burst Windows x64 Standalone Player results.
- [`Web/`](Web/README.md) -- done. Real single-thread WebGL Player results, run in a browser via
  the Browser pane (`Immediate`/`Budgeted` only, per this backend's own accepted policy scope).
- `Android/` -- not measured. Only an x86_64 emulator image was available locally (no genuine
  ARM64 device/emulator); a disclosed gap, per `Planning~/Evidence/P4-008/README.md`.

Results from one platform are never presented as establishing support for another, per
`Documentation~/benchmarks.md`'s own rule.
