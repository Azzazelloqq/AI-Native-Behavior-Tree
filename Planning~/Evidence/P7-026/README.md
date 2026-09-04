# P7-026 — measured Windows Player size scaling

Outcome: **Done**, 2026-09-04. Two real Windows x64 IL2CPP Players and both payload probes passed.
Increasing the authored tree population from 1 to 100 grew raw shipped files by **36,028 bytes**
(about 0.041%, 363.92 bytes per added tree). Code binaries and IL2CPP metadata were byte-identical.
The total Player is not independent of tree count: JSON and Unity's asset/index metadata grow.
No per-tree code growth was observed for this fixed catalog and measured population range.

## Environment and reproducibility

- Runtime, Authoring and packaged analyzer source: AIBT `8a874a4aac5cd4aa43a939919a09805b735eb529`.
  Harness: the files committed alongside this evidence. No production source changes.
- Unity 6000.5.8f1; Windows x64; IL2CPP; Burst 1.8.30; Collections 6.5.0; Newtonsoft JSON 3.2.2.
- MSVC 19.44.35228.0 / Build Tools 14.44.35207, Windows SDK 10.0.26100.0; toolchain preflight passed.
- Release Player, managed stripping Low, Burst compilation enabled. The sole BuildOptions flag
  is DetailedBuildReport; no LZ4/LZ4HC override or archive compression. Same project, scene, product,
  compiler sources and catalog for both builds. Second build reuses the first build's code cache.
- Canonical completed run: `Benchmarks~/Phase7/BuildSize/Results/20260904-170405/` (ignored local
  binaries/logs). Run `Benchmarks~/Phase7/BuildSize/Run-BuildSize.ps1` for fresh evidence. See the
  [harness guide](../../../Benchmarks~/Phase7/BuildSize/README.md).

Both inputs use only `aibt.core.memory-sequence` and `aibt.stdlib.wait` with ticks=2. Each tree has
two nodes, the same structure and parameters, and fixed-length unique ID/name. The existing
built-in registry/catalog stays fixed. Trees are included as Resources TextAssets; Editor and
Player both parse and compile every document and reject duplicate IDs. Player count, byte count
and SHA-256 must match build inputs; BuildReport must contain the expected distinct asset paths.
The two Player catalog fingerprints match as actual 256-bit values (Word0..Word7 hex encoding).

The established P4-008 Windows harness supplies the isolation and build/run precedent. Comparing
against its historical scheduling executable would confound tree count with workload/source
changes; the controlled baseline here is a fresh one-tree build of the same current harness.

## Results (bytes)

| Measurement | 1 tree | 100 trees | Delta |
|---|---:|---:|---:|
| Raw shipped files | 87,958,367 | 87,994,395 | +36,028 |
| Input JSON, also verified in Player | 265 | 26,500 | +26,235 |
| BuildReport packed tree objects | 292 | 29,200 | +28,908 |
| `resources.assets` | 1,752 | 34,212 | +32,460 |
| `globalgamemanagers` | 40,868 | 44,436 | +3,568 |
| `GameAssembly.dll` | 25,767,424 | 25,767,424 | 0, identical SHA-256 |
| `lib_burst_generated.dll` | 229,376 | 229,376 | 0, identical SHA-256 |
| `global-metadata.dat` | 5,365,484 | 5,365,484 | 0, identical SHA-256 |

Measurements are nested, not additive rows. [Comparison JSON](comparison.json) lists every
hash-changed shipping file. The two [build inventories](1-build.json) / [100-tree inventory](100-build.json)
contain all 26 shipped file sizes and SHA-256 values, and every packed tree path. All shipping
files except `resources.assets`, `globalgamemanagers` and `boot.config` are byte-identical.

`resources.assets` accounts for serialized tree objects and serialized-file overhead. Of its
32,460-byte growth, 28,908 bytes are attributed by BuildReport to the 99 added TextAssets; the
remaining 3,552 bytes are serialized-file metadata/alignment. Relative to raw JSON, tree-object
packing adds 2,673 bytes. Along with the 3,568-byte resource index growth, non-JSON overhead totals
9,793 bytes. No delta remains outside data/index files.

Unity's `binary2text` output ([1 tree](1-globalgamemanagers.txt),
[100 trees](100-globalgamemanagers.txt)) localizes `globalgamemanagers` growth to the object at
ClassID 147, the resource index. Its start stays at byte 39,888; the next object moves from 40,016
to 43,584, exactly 3,568 bytes. Later object offsets move by the same amount. Direct inspection
finds 1 versus 100 `trees/tree-NNNN.aibt` path strings in this file. The serialized files omit type
trees, so the committed dumps establish object boundaries rather than a decoded field schema.

`boot.config` stays 156 bytes: only `build-guid` changes, from `a097c2147e66410bbb89ee4c32e43183`
to `2221c9a5e1144cc695ea5c1853556ca5`. This is per-build identity, not extra tree code. BuildReport
summary totals (657,136,188 / 657,172,216) also include non-shipping build/debug material; the
shipping metric is the explicit file inventory excluding Unity's `DoNotShip`/`DontShip` folders.

The structural check agrees with the measured result: `CodeGen~/AIBT.CodeGen/BurstNodeGenerator.cs`
registers source output from CompilationProvider and discovers node/catalog type declarations.
It does not register authored JSON as generator inputs. This supports the fixed-catalog result;
catalog-size scaling itself was not varied or measured.

## Verification and limits

- Windows toolchain preflight: passed.
- Two canonical release IL2CPP builds: passed; both Editor build success markers recorded.
- Actual Players: 2/2 passed; [1-tree result](1-player.json), [100-tree result](100-player.json).
  All 101 document loads/compilations passed with matching input hashes and unique IDs per variant.
- `Tools~/Verification/Verify-Static.ps1`: passed, 7 schemas and 137 work items.
- `git diff --check`: passed. Full host EditMode suite was not rerun: no production code or
  Unity-host asset/configuration changed. Historical host test results are not evidence for this probe.

An initial run was discarded as canonical evidence because the probe used the fingerprint
struct's default ToString(), which returned a type name. The corrected harness encodes its eight
words and requires a 64-digit hash; the canonical evidence above comes from two fresh builds and
Player runs after that correction. No measurement was copied from the discarded run.

This is authored-JSON packaging with a fixed Authoring/compiler probe in Player, not a measurement
of a runtime-only precompiled-program distribution. It provides no Android/Web size, compression,
node-catalog scaling, or runtime performance claim. Absolute sizes can vary with Unity/toolchain
and asset metadata; no universal per-tree byte constant or acceptable-growth threshold is imposed.

## Handoff

- Task: P7-026. Deliverables: reproducible harness, raw inventories/Player evidence, root-caused
  size comparison and compatibility-matrix statement.
- Scope deviation: historical P4 build used as harness precedent, with a fresh controlled baseline
  as recorded in the approved next-steps plan. No normative contract changes or new node types.
- Final self-check: required builds/probes completed; delta fully located; no stronger platform or
  runtime-only claim introduced. No runtime repair is indicated by this measurement.
- Next planned task: P7-025, graph-viewer usability.
