# P2 Windows Player baseline — blocked evidence

Status: blocked by host toolchain on 2026-08-17.

The repository-owned generated Player harness reaches the Windows x64 IL2CPP C++
stage with Unity 6000.5.8f1, Burst enabled, a usable generated catalog, and zero
C#/AIBT/Burst/ILPP diagnostics. Unity then fails with
`Unity.IL2CPP.Bee.BuildLogic.ToolchainNotFoundException` because this host has no
MSVC x64 compiler and no Windows SDK.

Read-only detection confirmed:

- `vswhere.exe` absent;
- no `cl.exe` installation;
- no Windows 10/11 SDK registry key or Windows Kits 10 directory;
- the current process is not elevated, so the required toolchain cannot be
  installed unattended from this task.

Preserved raw evidence:

- build log:
  `Benchmarks~/Phase2/Dispatch/Results/windows-player-generated-dispatch-aot-6000.5.8f1-2026-08-16-rerun-2-build.log`;
- raw build report:
  `Benchmarks~/Phase2/Dispatch/Results/windows-player-generated-dispatch-aot-6000.5.8f1-2026-08-16-rerun-2-build.raw.json`.

The P2-022 repository harness is now complete:

- the non-Development x64 IL2CPP/Burst build overlays a Windows-only raw probe
  on the generated-dispatch conformance Player;
- the probe records 15 post-warmup repetitions for scheduling overhead, a cheap
  native lifecycle tree, command-heavy execution, and a mixed population;
- the existing generated user callback supplies the fifth, typed-blackboard
  read scenario;
- the acceptance report derives p50/p95/p99 frame contribution, throughput,
  scheduling/completion cost, fixed-arena native bytes, and the coarse Player GC
  signal without inferring thresholds or defaults;
- raw and acceptance JSON Schemas plus an independent digest-aware verifier are
  under `Benchmarks~/Phase2/Windows/` and `Tools~/Verification/P2/Windows/`;
- `-ControlledInvalid` is rejected before Unity launch with the stable
  `AIBT_P2_022_CONTROLLED_INVALID_REJECTED` marker.

The latest harness build again compiled the generated catalog and Windows probe,
ran Jobs/Burst IL post-processing, and reached the IL2CPP C++ stage with zero
C#/AIBT/Burst/ILPP warnings or errors. It stopped only at
`ToolchainNotFoundException`. The build log SHA-256 is
`91aaabd33b6b8e8b5dc1afe38eb25e7061bedab979517269d795089a0cbfc194` and the
raw build report SHA-256 is
`f9fdf9d5ec16e485c6de7c4a86f836e97c9a2576164929dbcd284eec68fb00e6`.

No Windows Player or performance claim is made. P2-022 remains blocked until
Visual Studio Build Tools with the x64/x86 C++ tools and a Windows SDK >=
10.0.19041 are installed; the actual Player/baseline must then be rerun.
