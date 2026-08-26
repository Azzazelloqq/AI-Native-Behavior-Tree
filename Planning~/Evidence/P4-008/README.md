# P4-008 platform benchmark evidence

## Result

- `Benchmarks~/Phase4/Platform/Windows/` (new): built a real, non-development, IL2CPP,
  Burst-enabled Windows x64 Standalone Player containing `P4-001`'s exact scenario/policy sweep
  (`SchedulingScenarios.cs`/`SchedulingPolicyDriver.cs` copied in unchanged), and ran it. This is
  the first Phase 4 benchmark to run on real (non-Editor) execution -- every prior P4 card
  (`P4-001`, `P4-002`, `P4-006`) measured in Editor batchmode only.
- **Major finding**: the release Player is **~13-14x faster** than the same scenarios measured in
  the Editor, consistently across all 6 scenarios and both agent-count extremes (16 and 1024) --
  see the full comparison table in `Benchmarks~/Phase4/Platform/Windows/README.md`. This means
  every Editor-measured number in `P4-001`/`P4-002`/`P4-005`/`P4-006`/`P4-007`'s evidence
  understates real release performance by roughly an order of magnitude on this workstation. This
  is disclosed as a real, actionable finding for anyone reading those cards' numbers as a
  performance signal -- not a new threshold or claim this card is authorized to make
  (`Planning~/USER_ACTIONS.md` still requires owner approval before any threshold is adopted).
- `BatchedJobsSameFrame`'s fixed-batch-size scheduling overhead (the mechanism `P4-002`/`P4-006`
  traced) reproduces in the Player too (e.g. `deep-sequence-selector-traversal`/1024 agents:
  11.95x costlier than `Immediate`) -- confirming that mechanism is a genuine property of the
  scheduling code, not an Editor-only artifact.
- Build proof recorded and verified by the PowerShell driver before accepting any run:
  `target=StandaloneWindows64`, `architecture=x86_64`, `scriptingBackend=IL2CPP`,
  `burstEnabled=true`, `developmentBuild=false`, `result=Succeeded`, 0 build errors/warnings.

## Update: Web measured too; Android remains a genuine device gap

After the initial Windows-only pass (below), the user asked directly whether Android/Web builds
were actually feasible rather than assumed unavailable. Checking properly found real
infrastructure: `AndroidPlayer` and `WebGLSupport` Unity modules are both installed, and a Browser
pane is available that can genuinely load and run a WebGL build (not just build it). **Web was
measured** -- see `Benchmarks~/Phase4/Platform/Web/README.md`. **Android was not**: the only
locally available system image is `x86_64` (no `arm64-v8a` image is downloaded), and the one
pre-configured AVD (`Pixel_10_Pro`) is also `x86_64`. Building for that emulator would not satisfy
`Planning~/USER_ACTIONS.md`'s "identify an Android ARM64 device class" -- an x86_64 emulator is
not ARM64 evidence, and even downloading an arm64-v8a system image would only produce
QEMU-emulated ARM64-on-x86_64, not genuine hardware performance evidence. The user offered to run
a build on their own physical Android phone instead; that requires the phone connected and
`adb`-visible, which had not happened as of this evidence snapshot -- tracked as the remaining gap.

### Web result

Ran `P4-001`'s scenario sweep (`Immediate`/`Budgeted` only, per
`Documentation~/specifications/platform-backends-v1.md`'s accepted scope for this backend) inside
a real, non-development, single-thread WebGL Player, loaded and executed in an actual browser via
the Browser pane -- not merely built. Confirmed `applicationPlatform: "WebGLPlayer"`,
`burstEnabled: true`, `is64BitProcess: false` (WASM's 32-bit runtime, correctly reported).

A real build-hosting problem was found and fixed along the way: the plain static file server used
to serve the build locally (`npx serve`) does not set `Content-Encoding: gzip` for Unity's
gzip-compressed build artifacts, so the browser could not auto-decompress them (confirmed via a
real console error on the first attempt). Fixed by enabling
`PlayerSettings.WebGL.decompressionFallback` -- Unity's own documented fix for hosts that cannot
set that header, not a benchmark-specific workaround.

A real, disclosed measurement limitation was also found, not hidden: many cases report
`medianNsPerAgent: 0.000` because `Stopwatch`'s timing resolution in this browser is too coarse to
register elapsed time for very fast/small samples (a known browser timing-precision reduction, not
a bug). Only scenarios with enough absolute per-sample cost register nonzero values --
`deep-sequence-selector-traversal` consistently reports ~12,109-12,500 ns/agent across every
agent count, a genuinely usable data point. Full detail in
`Benchmarks~/Phase4/Platform/Web/README.md`.

## Scope: Windows x64 and Web measured; Android ARM64 not measured

This card now covers two of the three mandatory pre-1.0 targets. Android ARM64 remains a
genuine, disclosed gap (see above) -- not silently omitted or faked. Per
`Documentation~/benchmarks.md`'s own rule, no result here is presented as establishing support for
the unmeasured platform.

## Original scope note (Windows-only pass)

Escalated via `AskUserQuestion` before the first implementation pass: at that point, this session
had (apparently) no Android ARM64 device/emulator and no WebGL-capable browser access, so the
first pass measured Windows x64 only. The Update above corrects this: Web access did in fact
exist and was used once actually checked, rather than assumed unavailable.

## Decision

- **Windows and Web scope; Android deferred to real device access**, resolved by explicit user
  decisions before each implementation pass (see Scope/Update above) -- the only
  architectural/feasibility decisions this card required. Android specifically was not just
  "unavailable" -- an x86_64 emulator exists, but was correctly rejected as not satisfying
  `USER_ACTIONS.md`'s ARM64-device-class requirement, rather than run anyway and mislabeled.
- **A dedicated build+run pipeline was written per platform rather than reusing `Benchmarks~/Phase2/Dispatch/Player/`'s
  exact apparatus.** That pipeline's SHA-256 source-fingerprinting, frozen-analyzer-hash pinning,
  and generated-dispatch-catalog proofs exist to validate a *different* claim -- that a specific
  source generator's AOT-compiled output is correct and unmodified -- which has no equivalent here
  (this card has no source generator; it reuses `P4-001`'s already-proven scenario/driver code
  as-is). Both `Run-WindowsPlatformBenchmark.ps1` and `Build-WebPlatformBenchmark.ps1` mirror the
  *general* pattern (isolated project, `BuildPipeline.BuildPlayer` with the right platform/Burst/
  non-development settings, structured build evidence, a `[RuntimeInitializeOnLoadMethod]` Player
  probe, verified success markers) without replicating checks that would not actually verify
  anything relevant to this card's own claim.
- **Web results are read from the browser console, not a file.** A WebGL Player has no reliable
  arbitrary filesystem write; logging to console and reading it via the Browser pane's own tools
  is the correct mechanism for this platform, not a shortcut.

## Scope and limitations

- Android ARM64 is not measured -- a real, disclosed gap (see Update above), pending either a
  genuine ARM64 device/emulator or the user's own physical phone becoming `adb`-visible.
- Web coverage is `Immediate`/`Budgeted` only (matching this backend's own accepted policy scope),
  a reduced parameter matrix (3 agent counts, fewer samples), and does not specifically exercise
  Burst-compiled-to-WASM code (neither measured policy uses a `[BurstCompile]` job) -- see
  `Benchmarks~/Phase4/Platform/Web/README.md` for the full disclosure, including the browser
  timer-resolution limitation that produces several `0.000 ns/agent` entries.
- One run per platform on one workstation/browser; not generalized to other hardware or browsers
  (`Planning~/USER_ACTIONS.md` requires owner approval across multiple hardware classes, and of
  the supported browser/version policy, before any threshold or support claim is adopted).
- No regression threshold or "supported" performance claim is drawn from any number here, per this
  card's own forbidden-changes clause -- including the striking Windows Editor-vs-Player ratio,
  which is reported as a finding, not turned into a rule.
- This card measures the same 6 implemented `P4-001` scenarios already proven in the Editor; it
  does not add new scenarios, and `PipelinedJobs`/`Auto` are not exercised on any platform here
  either (matching `P4-001`'s own scope, which this card's harnesses reuse unchanged).

See `verification-results.json` for exact commands and results.
