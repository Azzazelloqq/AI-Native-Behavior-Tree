# Supported claims

- Unity `6000.5.8f1` Editor on Windows x64 compiles and passes the complete Phase 1 EditMode suite.
- Android ARM64 IL2CPP with Burst enabled builds and contains the expected ARM64 IL2CPP/Burst libraries.
- Unity Web IL2CPP executes the tested semantic slice in Chrome and Firefox using single-thread immediate or explicit step budgeting.

# Claims intentionally not made

- Production Burst/jobs execution, zero-GC or zero-allocation execution.
- Android device behavior or performance.
- Safari, mobile browser, Linux, macOS, console, or Web worker support.
- Stable pre-1.0 public API compatibility beyond the recorded `0.1.0` baseline.
