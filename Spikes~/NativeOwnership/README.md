# Native ownership feasibility harness

This isolated Unity project verifies the ownership, capacity, safety, Burst, and cleanup decisions in `Documentation~/specifications/native-runtime-v1.md`. It is test-only evidence; none of its owner types or containers are production runtime APIs.

Run from PowerShell:

```powershell
.\Build-And-Verify.ps1
```

The command runs focused EditMode behavior tests with collection safety checks and native leak detection, then builds a non-development Windows player so the Burst job is AOT-compiled in release mode. Generated Unity state and build/test artifacts are ignored.
