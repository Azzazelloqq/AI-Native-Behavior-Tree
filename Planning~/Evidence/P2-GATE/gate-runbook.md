# P2-025 gate runbook

Prepared 2026-08-18. This is the ordered self-verification procedure to run
once `P2-022` produces a Windows baseline and the owner authorizes the
integration commit. Running it does not itself accept the gate.

Placeholders used below:

- `<UNITY_EXE>` — the Unity `6000.5.8f1` executable;
- `<PROJECT_PATH>` — the host Unity project containing the package;
- `<CLEAN_CLONE>` — a fresh clone of the committed snapshot, outside the host
  project;
- `<HARNESS>` — an isolated harness project created by the step that needs one.

Every command runs from the package root unless stated otherwise. Close every
Editor process holding the project before any Unity step.

## Preconditions

1. `P2-022` is `done` with committed evidence in `Evidence/P2-WINDOWS/`.
2. The commit described in `commit-package.md` exists and the working tree is
   clean.
3. The reviewer authored none of the P2 contracts under review.
4. The reviewer records the candidate commit SHA before starting.

## Ordered verification

### 1. Clean snapshot

```powershell
git clone <REPOSITORY> <CLEAN_CLONE>
git -C <CLEAN_CLONE> status --porcelain
git -C <CLEAN_CLONE> diff --check
```

Both checks must produce no output. Every following step runs against
`<CLEAN_CLONE>`, installed as a UPM package in a detached harness.

### 2. Static and schema verification

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Verify-Static.ps1'
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Verify-Schemas.ps1'
```

Record the work-item and schema counts.

### 3. Unity compile

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Run-UnityCompile.ps1' `
  -UnityPath '<UNITY_EXE>' -ProjectPath '<HARNESS>'
```

### 4. Full detached EditMode regression

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/Run-UnityTests.ps1' `
  -UnityPath '<UNITY_EXE>' -ProjectPath '<HARNESS>' -Mode EditMode -Scope Full
```

This covers the Phase 1 regression and the native behavior-case equivalence
matrix. Record totals and the NUnit XML SHA-256.

### 5. Clean CodeGen and dispatch consumer gate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/P2/CodeGen/Build-And-Verify.ps1' `
  -UnityEditor '<UNITY_EXE>'
```

Confirms the analyzer digest, generated contracts, and the public
`Public Burst Nodes` sample compiling in a clean project.

### 6. Allocation and lifetime gate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/P2/Allocation/Run-AllocationGate.ps1' `
  -UnityPath '<UNITY_EXE>' -ProjectPath '<PROJECT_PATH>'
```

The controlled allocation must still register exactly one `GC.Alloc` event. A run
without that canary proves nothing.

### 7. Platform evidence

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/P2/Windows/Assert-WindowsToolchain.ps1'
powershell -NoProfile -ExecutionPolicy Bypass -File './Benchmarks~/Phase2/Dispatch/Player/Run-GeneratedDispatchPlayerAot.ps1' `
  -UnityPath '<UNITY_EXE>'
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/P2/Windows/Verify-WindowsBaselineEvidence.ps1' `
  -EvidencePath '<RAW_EVIDENCE>'
powershell -NoProfile -ExecutionPolicy Bypass -File './Benchmarks~/Phase2/Dispatch/Player/Run-GeneratedDispatchPlayerAot.ps1' `
  -ControlledInvalid
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/P2/Android/Run-AndroidAot.ps1' `
  -UnityPath '<UNITY_EXE>'
powershell -NoProfile -ExecutionPolicy Bypass -File './Tools~/Verification/P2/Web/Run-WebConformance.ps1' `
  -UnityPath '<UNITY_EXE>'
```

The `-ControlledInvalid` run must stop with
`AIBT_P2_022_CONTROLLED_INVALID_REJECTED`.

### 8. Artifact and boundary audit

- Record the public API listing and its SHA-256, as `Evidence/P1-GATE/public-api.txt`
  and `public-api.sha256` did for Phase 1.
- Record the assembly dependency report and confirm `Runtime` references no
  `UnityEditor`, MCP, LLM, or DOTS Entities assembly.
- Record the analyzer, Runtime file-set, and generated-artifact digests.
- Confirm the working tree is still clean and `git diff --check` still passes.

### 9. Claim audit

Read `claims-inventory.md` against the produced results and reject any statement in
the package, `README.md`, `CHANGELOG.md`, or documentation that is stronger than
its evidence. Confirm `PipelinedJobs`, `Auto`, performance defaults, device
performance, Safari and mobile Web, and managed fallback remain explicitly out of
scope.

## Result document

Write `verification-results.json` in this directory only after the commands above
actually ran. The shape mirrors `Evidence/P1-GATE/verification-results.json`:

```json
{
  "format": "aibt.phase2-gate",
  "formatVersion": 1,
  "observedOn": "<YYYY-MM-DD>",
  "sourceCommit": "<candidate commit sha>",
  "unityVersion": "6000.5.8f1",
  "static": { "schemas": 0, "workItems": 0, "result": "" },
  "compile": { "installation": "UPM package in detached clean harness", "result": "" },
  "editMode": { "total": 0, "passed": 0, "failed": 0, "skipped": 0 },
  "codeGenGate": { "assertions": 0, "result": "" },
  "allocationGate": { "windows": 0, "gcAllocEvents": 0, "canaryEvents": 0, "result": "" },
  "publicApiSha256": "",
  "analyzerSha256": "",
  "platformEvidence": {
    "windows": "Planning~/Evidence/P2-WINDOWS/",
    "android": "Planning~/Evidence/P2-ANDROID/",
    "web": "Planning~/Evidence/P2-WEB/"
  },
  "verifiedBy": "<agent session>",
  "implementationVerdict": "",
  "formalGateVerdict": ""
}
```

Leave a field empty rather than guessing, and state `not run` with a reason for any
command that could not execute. A skipped command is never reported as a pass.

## Artifacts that must not be committed

Unity logs, NUnit XML, Players, APKs, Web builds, isolated harness projects, Burst
debug directories, benchmark raw output, the toolchain preflight report, and any
machine-specific path. Only sanitized summaries and digests belong in this
directory.
