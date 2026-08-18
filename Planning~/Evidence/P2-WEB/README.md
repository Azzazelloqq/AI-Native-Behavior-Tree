# P2 Web conformance evidence

Observed 2026-08-17 using the repository-owned
`Tools~/Verification/P2/Web/Run-WebConformance.ps1` isolated harness.

- Unity 6000.5.8f1, non-Development WebGL, IL2CPP, Burst enabled.
- Build: 0 errors and 0 warnings; 30,516,787 artifact bytes across 18 files.
- Artifact file-set SHA-256:
  `573d64328c01b2182db600a682011cda79191ced84b0b03a2d0ee59936d19938`.
- Chrome 151.0.7922.138 and Firefox 153.0.4 both executed the generated
  `ExecuteImmediate` callback successfully.
- Both browsers reported IL2CPP, Burst enabled, usable catalog, zero managed-path
  sentinel, exact Success status, expected memory value 38, and unchanged zero
  `AssetId` sentinel/padding.
- Both browsers passed the exact four-step Immediate versus four one-step
  Budgeted lifecycle trace comparison.
- Each browser retained seven raw generated-dispatch samples of 1,024 dispatches.
  Chrome observed approximately 4.18–7.88 microseconds/dispatch and Firefox
  approximately 6.84–11.00 microseconds/dispatch on this one headless run.
- Both reported zero generation-0 collections during the measured window and a
  coarse managed-heap delta of 4,276,224 bytes. A separate observed 1 MiB managed
  allocation is the controlled canary. `GC.GetTotalMemory` is explicitly not used
  as a zero-allocation proof; the initialized zero-GC claim remains P2-021's
  Unity recorder result.
- Analyzer SHA-256:
  `a7e6765b530b112591d0a2302271b13bd2675f4f1246d07a3cf7730d72c96dbc`.
- Runtime file-set SHA-256:
  `ebb7383d0671c3b505acc0eec02d73e42bd8293e1a7825d06d38e7589189965d`.

Raw browser JSON, build logs, npm browser tooling, and the isolated build remain
machine-local and are excluded from version control. Current claims cover desktop
Chrome/Firefox generated-dispatch conformance only; Safari, mobile Web, worker
parallelism, and device-performance claims remain excluded.
