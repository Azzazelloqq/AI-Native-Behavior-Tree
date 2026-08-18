# P2-012 generated Burst dispatch evidence

Accepted candidate observed 2026-08-17 with Unity 6000.5.8f1.

- Deterministic analyzer rebuild: SHA-256
  `4ac885faf44806162dc0d5f71d46ee4a30bba8b2beb92783398c9ef48ce9dea5`.
- Roslyn analyzer/generator matrix: 1411 assertions, AIBT5001–AIBT5012.
- Clean Unity CodeGen/Dispatch gate: 77/77, including generated immediate and
  scheduled Burst entry points plus the imported public package sample.
- Runtime dispatch suite is included in that clean gate; the broader Runtime
  suite passed 477/477.
- Actual Android ARM64 IL2CPP/Burst AOT build passed and retained
  `GeneratedDispatchCanaryCatalog.ExecuteImmediate` and
  `lib_burst_generated.so`.
- The 1/16/128 generated-switch benchmark retains 45 raw samples and a positive
  allocation canary under `Benchmarks~/Phase2/Dispatch/Results/`.

No reflection, managed fallback, pointer registry, `SharedStatic` context, or
dynamic dispatch path is accepted by the verifier.
