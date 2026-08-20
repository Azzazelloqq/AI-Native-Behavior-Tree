# P4-008 platform benchmark evidence

Mirrors `Benchmarks~/Phase2/`'s per-platform structure: one subdirectory per mandatory pre-1.0
target (Windows x64, Android ARM64, single-thread Unity Web), each running `P4-001`'s scenario
matrix on real (non-Editor) execution for that platform.

- [`Windows/`](Windows/README.md) -- done. Real IL2CPP/Burst Windows x64 Standalone Player results.
- `Android/` -- not measured. No Android ARM64 device or emulator was available in this session;
  a disclosed gap, per `Planning~/Evidence/P4-008/README.md`.
- `Web/` -- not measured. No WebGL-capable browser access was available in this session; a
  disclosed gap, per `Planning~/Evidence/P4-008/README.md`.

Results from one platform are never presented as establishing support for another, per
`Documentation~/benchmarks.md`'s own rule.
