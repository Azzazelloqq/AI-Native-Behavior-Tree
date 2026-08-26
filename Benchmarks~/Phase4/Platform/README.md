# P4-008 platform benchmark evidence

Mirrors `Benchmarks~/Phase2/`'s per-platform structure: one subdirectory per mandatory pre-1.0
target (Windows x64, Android ARM64, single-thread Unity Web), each running `P4-001`'s scenario
matrix on real (non-Editor) execution for that platform.

- [`Windows/`](Windows/README.md) -- done. Real IL2CPP/Burst Windows x64 Standalone Player results.
- [`Web/`](Web/README.md) -- done. Real single-thread WebGL Player results, run in a browser via
  the Browser pane (`Immediate`/`Budgeted` only, per this backend's own accepted policy scope).
- [`Android/`](Android/README.md) -- done. Real IL2CPP/Burst Android ARM64 Player results, run on
  genuine ARM64 hardware (the user's own physical phone, over `adb`) after the only locally
  available emulator image proved to be x86_64-only.

All three mandatory pre-1.0 targets (`Documentation~/benchmarks.md`) are now measured.
Results from one platform are never presented as establishing support for another, per
`Documentation~/benchmarks.md`'s own rule.
