# P7-026 — Player size versus authored tree count

Run from the AIBT package root on the established Windows IL2CPP toolchain:

```powershell
& './Tools~/Verification/P2/Windows/Assert-WindowsToolchain.ps1'
& './Benchmarks~/Phase7/BuildSize/Run-BuildSize.ps1'
```

The script creates a new temporary Unity project and a new ignored `Results/<UTC timestamp>`
directory. It leaves both available for inspection. It does not open or modify the host project's
scene, package configuration or Player settings. `-UnityPath`, `-OutputDirectory`,
`-BuildTimeoutSeconds` and `-PlayerTimeoutSeconds` can be supplied explicitly. An existing output
directory is rejected to avoid overwriting evidence.

## Controlled comparison

The Phase 4 Windows platform harness supplies the isolation/build/run pattern and toolchain
precedent. Its historical scheduling workload is not a comparable one-tree size baseline, so this
harness builds both population variants with the same current sources and probe. Runtime,
Authoring and the packaged analyzer are copied unchanged. Dependencies are pinned to Burst 1.8.30,
Collections 6.5.0 and Newtonsoft JSON 3.2.2. The host's ProjectVersion.txt selects the Editor version.

Both builds use Windows x64 IL2CPP, managed stripping Low, Burst enabled, a fixed product name and
the same empty scene. BuildOptions.DetailedBuildReport is the sole build flag: release Player,
no LZ4/LZ4HC override. Sizes are raw shipped files, not ZIP/download sizes. The second build reuses
the same project and compiled sources; only Resources tree assets change.

Population 1 contains one canonical JSON tree; population 100 contains it plus 99 trees. All reuse
the existing memory-sequence and wait node types and the same parameter values. Fixed-length unique
IDs and names distinguish documents without changing their structure or the node-type catalog.
Editor validation compiles every input. Each actual Player loads all Resources trees, requires the
expected count and unique IDs, compiles each document, and records its input SHA-256 and catalog
fingerprint. The driver compares these against the build inputs and checks BuildReport packed
asset paths. A missing/stripped tree therefore cannot masquerade as constant-size success.

## Output and interpretation

- `1/` and `100/`: runnable Players, plus Unity's non-shipping debug/backup folders.
- `1-build.json`, `100-build.json`: shipped-file size/SHA-256 inventory, BuildReport total and
  packed tree sizes, configuration and input identity. `DoNotShip`/`DontShip` folders are excluded
  from shipped-byte totals.
- `1-player.json`, `100-player.json`: measured Player payload counts, bytes and hashes.
- `comparison.json`: total delta and every added, removed or hash-changed shipping file.
- `*-globalgamemanagers.txt`: Unity binary2text object offsets, used to locate serialized
  resource-index growth independently of code binaries.
- `run.json`, `build.log`, `*-player.log`: source commit, isolated project location and execution logs.

Preserve compact JSON and a written interpretation under `Planning~/Evidence/P7-026/`; generated
Players, logs and caches remain ignored. Compare code binaries/IL2CPP metadata separately from
serialized Resources data. Rebuild a baseline only if unexplained build variability prevents
attribution. No arbitrary pass percentage is imposed.

This measures shipping authored JSON with a fixed Authoring/compiler probe present in the Player.
It does not measure the smaller runtime-only, precompiled-program deployment, vary catalog size,
establish Android/Web size behavior, or establish execution performance. A flat code size supports
absence of per-tree code growth in this measured range; total build size still includes growing
tree payload and asset metadata.

The catalog fingerprint is encoded as Word0 through Word7, each as eight hexadecimal digits.
