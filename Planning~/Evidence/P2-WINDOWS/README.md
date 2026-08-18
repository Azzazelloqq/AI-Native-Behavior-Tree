# P2 Windows Player baseline — evidence

Status: unblocked and passing on 2026-08-18. The non-Development Windows x64
IL2CPP/Burst Player executes the representative behavior matrix and the
generated user node without Burst fallback or errors, and the resulting
acceptance evidence is schema-valid and digest-verified.

## Toolchain preflight

`Assert-WindowsToolchain.ps1` reports:

```text
AIBT_P2_022_TOOLCHAIN_OK|msvc=19.44.35228.0|sdk=10.0.26100.0
```

- MSVC: `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64\cl.exe` (file version `19.44.35228.0`)
- Windows SDK: `C:\Program Files (x86)\Windows Kits\10\`, version `10.0.26100.0` (>= required `10.0.19041`)

`Install-WindowsToolchain.ps1` was not needed; both components were already
present on this host.

## Two defects found and fixed en route

Reaching a passing run first required diagnosing and fixing two independent
defects, both outside a plain toolchain-blocker explanation. Both fixes were
made with explicit owner authorization to edit outside P2-022's originally
assigned paths.

1. **Exception-masking bug in the Player probe.** `Scenario.Dispose()` in
   `Benchmarks~/Phase2/Dispatch/Player/Unity/Runtime/GeneratedDispatchPlayerAotProbe.cs`
   ran inside an implicit `using`-block finally. When a cycle failed mid-loop,
   the `Dispose()`-time `PhaseViolation` (the workspace owner wasn't in a
   resettable state) silently replaced the real originating exception, so the
   Player log only ever showed "Native dispatch owner disposal failed:
   PhaseViolation" with no way to see the real cause. Fixed by capturing the
   loop exception explicitly and surfacing both messages (with the original as
   `InnerException`) instead of letting the second exception clobber the
   first.
2. **Undersized value-session capacity for the canary node's read workload.**
   With the masking fixed, the real error was `executionCode=Faulted
   diagnosticNumber=4307` (`NativeOutputCapacityExceeded`) on the very first
   cycle. `GenerationNode.Tick` (`Benchmarks~/Phase2/Dispatch/Player/Unity/Runtime/Nodes/GeneratedDispatchCanaryNodeDeclarations.cs`)
   performs 32 sequential blackboard reads per Tick. Value sessions are an
   append-only per-frame ledger (`NativeBurstDispatchWorkspaceOwnerV2`/
   `BurstBindingBridgeCoreV2` bump-allocate `SessionCount`/`StagingByteCount`
   per read, never reusing a slot within a frame — confirmed by reading
   `TryAllocateSession`/`InitializeSession`), so 32 reads need 32 sessions and
   128 staging bytes. The probe's `Scenario` capacity declared only
   `MaxValueSessionsPerFrame=1, MaxValueStagingBytesPerFrame=4`. Fixed by
   raising the declared capacity to `32u, 128u` in
   `GeneratedDispatchPlayerAotProbe.cs` to match the fixture's actual demand.
   This is a benchmark-harness sizing fix, not a Runtime behavior change —
   `NativeBurstDispatchWorkspaceOwnerV2`/`BurstBindingBridgeCoreV2` were not
   modified.
3. **Digest format bug in the evidence verifier (found after the above two
   fixes).** `Get-TextSha256` in
   `Tools~/Verification/P2/Windows/Verify-WindowsBaselineEvidence.ps1` split
   `return (...).Replace('-', '').ToLowerInvariant()` across a line break
   before `.Replace`. In this host's Windows PowerShell 5.1, `return`
   terminated at the closing paren before the line break, so the function
   returned the raw dashed-uppercase `[BitConverter]::ToString()` output
   instead of the normalized lowercase hex used everywhere else, and every
   `environment.sha256`/file-set-fingerprint comparison failed even though the
   underlying hash bytes matched. Fixed by computing the hex string in a local
   variable before returning it, removing the line-break-before-`.Replace`
   pattern. (The harness script's own `Get-TextSha256` in
   `Run-GeneratedDispatchPlayerAot.ps1` keeps the same call on one line and
   was never affected.)

Both `Benchmarks~/Phase2/Dispatch/Player/` edits are scoped to the two changes
above (diagnostics + capacity constant); no dispatch semantics, lifecycle, or
public contract changed.

## Passing run

Full harness run `20260818-133534` (isolated project
`aibt-p2-012-player-aot-51860d5dcefb42588173d9626a9ea7f4`), rerun after the
P2-025 gate's CodeGen reproducibility fix regenerated `Analyzers/AIBT.CodeGen.dll`
(see `Planning~/Evidence/P2-GATE/`); the earlier `20260818-072054` run above
used the prior analyzer bytes and is superseded:

```text
AIBT P2-012 Windows Player/AOT acceptance passed. Evidence:
Benchmarks~/Phase2/Dispatch/Results/windows-player-generated-dispatch-aot-20260818-133534.json
```

Build: Unity `6000.5.8f1`, `StandaloneWindows64` x86_64, IL2CPP, Burst
enabled, non-Development, 0 errors/0 warnings. Packages: Burst `1.8.29`,
Collections `6.5.0`, Newtonsoft.Json `3.2.2`.

Behavior matrix (7 cases, both probes): `empty-sequence-immediate-success`,
`empty-selector-immediate-failure`, `command-effect-publication`,
`burst-job-schedule-complete`, `empty-sequence-immediate`,
`empty-sequence-budgeted`, `generated-user-node-scheduled`. All passed; no
Burst fallback (`managedPathSentinel=0` throughout), no ILPP/C#/Burst
diagnostics in either the build or Player log.

Five raw scenarios (p50/p95/p99 frame contribution, steps/s, commands/s,
native bytes, coarse GC signal) recorded verbatim in the artifacts below:
`scheduling-overhead`, `cheap-tree`, `command-heavy`, `mixed-population`,
`blackboard-heavy-generated-dispatch`. As always, no policy threshold,
scheduling default, or Android/Web/console conclusion is inferred from this
single-workstation result; Phase 4 owns that.

Both `Verify-WindowsBaselineEvidence.ps1 -EvidencePath <raw>` and
`-EvidencePath <acceptance>` pass against this run's artifacts.

### Artifact digests (SHA-256), run `20260818-133534`

- acceptance evidence: `dfedf118ebb5052e9c3df81bff8b8e3dfa6a1a9ff28d24637a598c74cb1daac6`
  (`Benchmarks~/Phase2/Dispatch/Results/windows-player-generated-dispatch-aot-20260818-133534.json`)
- build log: `5a27f9f4d6074232000cebf3ddf6e5d88f228051ae975663cac293f19073f92e`
- build raw evidence: `be426dc2bb6be8884ea92eba7052b0b154b994ab3890ec38f8158da92753a766`
- player log: `e0058613a4a25d8c4813229ee34fd4f90ca1a834e611d40f837ffc5f2fa257e5`
- player raw evidence: `d5fb1cce57a8bcafff560914f6a0d96a88f110ddcaf22a034713537152815540`
- windows-baseline raw evidence: `42f74433d1d692fd08f3a3745625e2e83ec4d90a02655f5c1f1b5eb099f9cd7b`
- Player executable: `922bca676cca2fb4fd06b9e5938dee6f665a1d9f8ba73491fb7b476fbee0260e`
- GameAssembly.dll: `c027c28969cead97d51ea8e4a44eb7002a5109d64515e0e815cf6b9b3a00b170`
- global-metadata.dat: `19631384105e3925273b5623724c2443ce1a2670da48ad2f2b93f0fa816ad788`
- lib_burst_generated.dll (331264 bytes): `6958e54b75a313b9c21418df67072a815ffe9dae95434d57ec3ca2b754999ed5`
- checked source generator (`Analyzers/AIBT.CodeGen.dll`): `4ac885faf44806162dc0d5f71d46ee4a30bba8b2beb92783398c9ef48ce9dea5`
- environment snapshot: `f82d9bf6c91a67c9a1f909ebaee4015b7cbbc83415ff76cc42fa3d499a9b9825`

Full digest set (including Runtime/harness file-set fingerprints, per-scenario
raw samples, and generated-code declaration hashes) is in the acceptance JSON
itself.

## Controlled-invalid negative case — passes

```text
powershell -File './Benchmarks~/Phase2/Dispatch/Player/Run-GeneratedDispatchPlayerAot.ps1' -ControlledInvalid
```

stops with `AIBT_P2_022_CONTROLLED_INVALID_REJECTED: malformed baseline
evidence was rejected by the production JSON Schema verifier.` before Unity
launches.

## Closure

P2-022 acceptance criteria are met on this workstation: non-Development
Windows x64 IL2CPP/Burst Player, clean behavior matrix, schema-valid and
digest-verified raw and acceptance evidence, and a passing controlled-invalid
negative case. No Android, Web, console, or scheduling-policy conclusion is
drawn from this result.
