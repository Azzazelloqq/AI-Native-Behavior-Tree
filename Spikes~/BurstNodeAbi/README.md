# Burst node ABI feasibility spike

This disposable spike validates the public ABI choices made by `P2-001`. It is
not production Runtime, Authoring, or CodeGen code.

## Decision matrix

| Question | Option | Result | Reason |
| --- | --- | --- | --- |
| Callback dispatch | Generated static calls | Selected | Closed calls are reviewable and do not require reflection, delegates, interface dispatch, or boxing. |
| Callback dispatch | Constrained instance interface | Rejected for v1 | Unity's pinned compiler cannot express a static-abstract contract, while an instance contract adds an unnecessary dispatch hazard. |
| Callback dispatch | Function-pointer registry | Rejected for v1 | It adds AOT/WASM and lifetime surface without improving the required closed registry. |
| Public custom kinds | `Condition` and `Action` | Selected | They require only the accepted lifecycle. |
| Public custom kinds | `Composite` and `Decorator` | Deferred | No accepted child-transition ABI exists; inventing one here would change execution semantics. |
| Blackboard binding | Typed generated handles | Selected | The compiler can bind declared accesses to numeric ordinals without runtime strings. |
| Blackboard binding | Reinterpret existing manifest `reads`/`writes` | Rejected | It would silently change an accepted Phase 1 contract. |
| Seed derivation | Domain-separated SplitMix64 fold | Selected | It is small, portable, allocation-free, and has exact published vectors. |

## Layout

- `Generator/` contains the disposable incremental generator and diagnostic
  analyzer.
- `Runner/` executes isolated positive and negative Roslyn cases and writes
  generated source for deterministic comparison.
- `Harness/` is a clean Unity consumer project with two node asmdefs and a
  separate catalog-set asmdef. It references the local AIBT UPM package, loads
  the analyzer through explicit asmdef analyzer GUIDs, and runs synchronous
  Burst-compiled `IJob` cases for action and memoryless-observer dispatch.
- `Build-And-Verify.ps1` discovers Roslyn from the selected Unity Editor,
  builds the disposable tools, compares every artifact from two clean
  generations (including reverse input order under `tr-TR`), and runs the Unity
  EditMode/Burst harness. It requires Unity 6000.5.8f1, Roslyn 4.10, and the
  actually resolved Burst package 1.8.29.

Run with Windows PowerShell 5.1 or newer:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-And-Verify.ps1
```

Generated DLLs, Unity `Library/`, logs, and test results are intentionally
ignored. Evidence records hashes and sanitized observations instead of
committing machine output.
