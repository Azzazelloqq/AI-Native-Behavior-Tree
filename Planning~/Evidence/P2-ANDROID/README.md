# P2 Android ARM64 AOT evidence

Observed 2026-08-17 with Unity 6000.5.8f1 using the repository-owned
`Tools~/Verification/P2/Android/Run-AndroidAot.ps1` isolated harness.

- Result: passed; non-Development Android ARM64 IL2CPP build, Burst enabled.
- Generated catalog: usable; retained entry point
  `GeneratedDispatchCanaryCatalog.ExecuteImmediate`.
- Build diagnostics: 0 errors, 0 warnings; CS/BC/fallback/native-leak scan clean.
- APK: 20,602,512 bytes; SHA-256
  `899d3f0d0b950330446ada26cdf27265f03d6b0956970a7a0bd1bd9eaf13bdef`.
- Native ABI: only `lib/arm64-v8a`; includes `libil2cpp.so` and
  `lib_burst_generated.so`.
- Analyzer SHA-256:
  `a7e6765b530b112591d0a2302271b13bd2675f4f1246d07a3cf7730d72c96dbc`.
- Runtime file-set SHA-256:
  `fd9f3098da62ed65d4779cc52c674c19f853e39da9826f545153375f637b58b6`.
- Build log SHA-256:
  `c6a3770f99edb415d621adee573b48929b048d35b40b7ba34ae990ad1a8c480a`.

The raw log, APK, Burst debug directory, isolated project, and machine-local paths
are intentionally excluded from version control. This evidence proves build/AOT
compatibility only; it makes no device-runtime, performance, battery, or store claim.
