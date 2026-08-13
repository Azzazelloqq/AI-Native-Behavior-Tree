# Android ARM64 build smoke

Run against an isolated Unity harness containing the current AIBT package snapshot:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Android/Run-AndroidBuildSmoke.ps1' `
  -UnityPath '<UNITY_EDITOR>' `
  -ProjectPath '<ISOLATED_HARNESS>' `
  -OutputPath '<IGNORED_RESULT_DIRECTORY>'
```

The command builds an unsigned development APK using Android, IL2CPP, ARM64 only, and Burst enabled. It adds a temporary host scene and Burst `IJob` under a task-specific harness directory, then removes that generated source directory. Missing Android SDK, NDK, or OpenJDK modules fail before Unity starts.

Passing this command proves build compatibility only. It does not prove installation, device runtime behavior, performance, thermals, or production signing.
