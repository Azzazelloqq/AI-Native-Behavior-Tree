# P4-008 single-thread Unity Web (WebGL) platform benchmark evidence

Runs `P4-001`'s scenario sweep inside a real, non-development, single-thread WebGL Player, loaded
and executed in an actual browser (not just built) -- restricted to `Immediate`/`Budgeted`, since
`Documentation~/specifications/platform-backends-v1.md` does not claim `BatchedJobsSameFrame` or
`PipelinedJobs` as supported Web policies ("unavailable unless a future verified Unity capability
changes this decision"). This card does not test a policy the project's own accepted architecture
excludes for this backend.

## Build and run

`Build-WebPlatformBenchmark.ps1` builds an isolated project (copying `Runtime/`, `Authoring/`,
`Tests/Runtime/Benchmarking/SchedulingPolicyDriver.cs`, and
`Benchmarks~/Phase4/Scheduling/Unity/SchedulingScenarios.cs` unchanged, alongside this card's own
`Unity/` folder) and builds a release, single-thread, Burst-enabled WebGL Player from it.
`PlayerSettings.WebGL.decompressionFallback` is enabled: the plain static file server used to
serve the build locally (`npx serve`) does not set the `Content-Encoding: gzip` header a real CDN
host would, and without the fallback the browser cannot auto-decompress Unity's gzip build
artifacts -- this is Unity's own documented fix for exactly that hosting gap, not a workaround
specific to this benchmark.

A WebGL build has no reliable arbitrary filesystem write, so
`Unity/Runtime/WebPlatformSchedulingProbe.cs` logs its JSON result to the browser console
(`Debug.Log`, prefixed with a marker) instead of writing a file; the result was read directly from
the browser's console via the Browser pane and saved to
`Results/web-player-scheduling-20260821.json`.

```powershell
.\Build-WebPlatformBenchmark.ps1
# then serve the output directory over HTTP (WebGL requires HTTP, not file://) and open it in a browser
```

## Result

Confirmed real single-thread WebGL execution: `applicationPlatform: "WebGLPlayer"`,
`burstEnabled: true`, `is64BitProcess: false` (WASM is a 32-bit runtime, correctly reported, not a
bug). Build evidence separately confirms `target: WebGL`, `result: Succeeded`, 0 errors/warnings,
`threadsSupport: false`.

**A real, disclosed limitation, not a silently-passed result**: many measured cases show
`medianNsPerAgent: 0.000`. This is not a claim that those operations cost nothing -- it is
`System.Diagnostics.Stopwatch`'s timing resolution in this browser being too coarse to register
elapsed time for very fast/small samples (browsers commonly reduce high-resolution timer
precision for security reasons, e.g. Spectre-style timing-attack mitigations). Only scenarios with
enough absolute per-sample work register a nonzero measurement -- most clearly
`deep-sequence-selector-traversal` (the deepest, most expensive scenario), which reports a
consistent ~12,109-12,500 ns/agent across every measured agent count, a genuinely usable data
point. The smaller/cheaper scenarios' `0.000` entries are reported honestly as "below this
browser's measurable timer resolution," not converted into a false claim of zero cost.

## Scope and limitations

- Only `Immediate`/`Budgeted` are measured, per this backend's own accepted policy scope (see
  above) -- `BatchedJobsSameFrame` and `PipelinedJobs` are not exercised, matching
  `platform-backends-v1.md`'s decision, not an oversight.
- Reduced parameter matrix versus the Windows Player probe (3 agent counts: 16/64/256, not 1024;
  3 warmup + 7 measured samples, not 5+15) -- chosen given WebGL's slower single-threaded
  execution and to keep the single console-logged JSON line compact; this is a real, disclosed
  scope reduction for this platform, not a hidden one.
- Neither measured policy (`Immediate`, `Budgeted`) wraps its work in a `[BurstCompile]` job in
  this codebase -- only `BatchedJobsSameFrame`'s `AdvanceJob` does, and that policy is out of
  scope for Web. This benchmark therefore does not specifically exercise Burst-compiled-to-WASM
  code, only plain managed C# running under WebGL's IL2CPP/Emscripten toolchain. Burst WASM
  feasibility specifically remains unmeasured by this run; `burstEnabled: true` in the environment
  block reflects the Burst *compiler setting*, not proof that Burst-compiled code executed.
- One run in one browser on one workstation; not generalized to mobile browsers, Safari, or any
  other browser/device (`Planning~/USER_ACTIONS.md` requires owner approval of the supported
  browser/version policy before any public claim). `OQ-004` (macOS/Safari access) remains open.
- No regression threshold or "supported" performance claim is drawn from any number here.

## Recorded evidence

The canonical 2026-08-21 run is preserved as
[raw JSON](Results/web-player-scheduling-20260821.json) (captured from the browser console output)
and [build evidence](Results/web-build-20260821-build.raw.json). The WebGL build output itself and
the Unity build log are not committed (large generated artifacts / raw logs, per repository
policy).
