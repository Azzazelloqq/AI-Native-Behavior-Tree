# P0-004 Android ARM64 build evidence

Observed on 2026-08-14. Result: **pass**.

## Proven boundary

The isolated harness containing the final P1-018 AIBT snapshot produced an unsigned development APK with:

- Unity `6000.5.8f1`;
- Android Player target;
- IL2CPP scripting backend;
- ARM64 as the only native architecture;
- Burst compilation enabled;
- a generated Burst `IJob` that references the production `AIBT.Runtime` assembly.

APK inspection found `lib/arm64-v8a/libil2cpp.so` and `lib/arm64-v8a/lib_burst_generated.so`, with no native library under another ABI directory. The clean Unity build summary reported `Succeeded`, zero errors, and one warning. The finalized command was then repeated successfully with its built-in APK inspection. The sanitized excerpt records the relevant clean-build native-library and result lines; the machine-readable evidence records both APK digests because development APK packaging is not byte-deterministic across runs.

This is build-compatibility evidence only. The APK was not installed or run on a device, and this evidence makes no runtime, allocation, throughput, thermal, battery, signing, store, or performance claim.

## Repeatable command

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Android/Run-AndroidBuildSmoke.ps1' `
  -UnityPath '<UNITY_EDITOR>' `
  -ProjectPath '<ISOLATED_HARNESS>' `
  -OutputPath '<IGNORED_RESULT_DIRECTORY>'
```

The command fails before starting Unity when the bundled Android SDK, NDK, or OpenJDK is missing. It validates the resulting APK metadata and native entries, then removes its generated harness-only source and scene directory.

## Artifacts

- `environment.json`: sanitized toolchain, result, APK digest, and native-entry record.
- `build-summary.txt`: sanitized build-log excerpt and APK entry listing.

The APK and raw machine-local Unity log remain in the ignored verification result directory and are not committed.

## Device follow-up

When an approved Android ARM64 device class is available, perform a separately scoped install/run evidence task. Record device model/SoC, Android version, cold start, semantic-case result, allocations, throughput, thermals, and power conditions before making any device or performance claim.
