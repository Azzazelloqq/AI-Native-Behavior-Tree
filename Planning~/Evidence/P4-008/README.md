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

## Scope: Windows x64 only (Android ARM64 and Unity Web not measured)

Escalated via `AskUserQuestion` before implementation: this session had no Android ARM64
device/emulator and no WebGL-capable browser access. Per the user's direction, this card measures
Windows x64 only; Android ARM64 and single-thread Unity Web are disclosed, not-yet-measured gaps
in `Benchmarks~/Phase4/Platform/README.md` and this card's own Outcome, not silently omitted or
faked. Per `Documentation~/benchmarks.md`'s own rule, no result here is presented as establishing
support for either unmeasured platform.

## Decision

- **Windows-only scope**, resolved by explicit user decision before implementation (see Scope
  above) -- the only architectural/feasibility decision this card required.
- **A dedicated build+run pipeline was written rather than reusing `Benchmarks~/Phase2/Dispatch/Player/`'s
  exact apparatus.** That pipeline's SHA-256 source-fingerprinting, frozen-analyzer-hash pinning,
  and generated-dispatch-catalog proofs exist to validate a *different* claim -- that a specific
  source generator's AOT-compiled output is correct and unmodified -- which has no equivalent here
  (this card has no source generator; it reuses `P4-001`'s already-proven scenario/driver code
  as-is). This card's build/run script (`Run-WindowsPlatformBenchmark.ps1`) mirrors the *general*
  pattern (isolated project, `BuildPipeline.BuildPlayer` with IL2CPP/Burst/non-development
  settings, structured build evidence, a `[RuntimeInitializeOnLoadMethod]` Player probe, verified
  success markers) without replicating checks that would not actually verify anything relevant to
  this card's own claim.

## Scope and limitations

- Android ARM64 and single-thread Unity Web are not measured (see Scope above) -- real gaps, to be
  closed by a follow-up run of this same Windows pipeline's pattern once device/browser access
  exists, not by this card retroactively.
- One run on one Windows x64 workstation; not generalized to other hardware
  (`Planning~/USER_ACTIONS.md` requires owner approval across multiple hardware classes before any
  threshold is adopted).
- No regression threshold or "supported" performance claim is drawn from any number here, per this
  card's own forbidden-changes clause -- including the striking Editor-vs-Player ratio, which is
  reported as a finding, not turned into a rule.
- This card measures the same 6 implemented `P4-001` scenarios and 3 fixed policies already
  proven in the Editor; it does not add new scenarios, and `PipelinedJobs`/`Auto` are not exercised
  here either (matching `P4-001`'s own scope, which this card's harness reuses unchanged).

See `verification-results.json` for exact commands and results.
