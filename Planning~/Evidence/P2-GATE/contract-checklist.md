# Phase 2 contract checklist

Prepared 2026-08-18 for the `P2-025` review. This is the checklist the gate
verification pass checks, not an acceptance record. Rows in the second table have
no evidence yet and must not be reported as satisfied.

Work items `P2-004` through `P2-011` carry no dedicated evidence directory. Their
contracts are evidenced by the named test suites and by the summary in this
directory's `README.md`.

## Verified from existing Phase 2 evidence

| Gate | Evidence |
| --- | --- |
| Public Burst node ABI v2 is closed, unmanaged, and reflection-free | `Evidence/P2-001/`, `Evidence/P2-012/`, ADR-P2-001, ADR-P2-012 |
| Generated dispatch is deterministic and prebound from the frozen analyzer | clean CodeGen/Dispatch consumer gate 77/77; analyzer/generator 1411 assertions; analyzer SHA-256 `a7e6765b530b112591d0a2302271b13bd2675f4f1246d07a3cf7730d72c96dbc` |
| Native ownership, fixed capacity, and rejection on overflow | `Evidence/P2-002/`, ADR-P2-002; capacity rejection case in `Evidence/P2-021/` |
| Tree, Agent, and Shared blackboard storage with deterministic reduction | `Evidence/P2-003/`; Shared/Snapshot/Tree-Agent suite 76/76 |
| Immutable native snapshots and append-only command streams | Shared/Snapshot/Tree-Agent suite 76/76; Commands/Async suite 20/20 |
| Bounded native diagnostics and trace channels | Runtime suite 477/477; trace assertions in `Evidence/P2-020/` |
| Native lifecycle, memory, reactive, parallel, decorator, observer, async, budget semantics | `Evidence/P2-013/` through `Evidence/P2-018/`; Runtime suite 477/477 |
| Batched same-frame Jobs scheduling preserves semantics | `Evidence/P2-019/`; Integration suite 26/26 |
| Native executor reproduces every P1 golden case under all three fixed policies | `Evidence/P2-020/`: 5 cases across Immediate, one-step Budgeted, and BatchedJobsSameFrame, 15 policy executions, focused 7/7 |
| Zero GC allocations in measured initialized windows | `Evidence/P2-021/`: 12 initialized windows with 0 `GC.Alloc`; a controlled 4096-byte allocation produced exactly 1 event, proving recorder sensitivity |
| Native lifetime safety under success, abort, fault, restart, capacity rejection, disposal | `Evidence/P2-021/` under Unity native leak detection; scan clean |
| Windows x64 IL2CPP/Burst Player executes generated dispatch and the behavior matrix without managed fallback | `Evidence/P2-WINDOWS/`: non-Development build, 0 errors/warnings, 7-case behavior matrix passed, schema-valid and digest-verified acceptance evidence |
| Android ARM64 IL2CPP/Burst AOT builds the generated catalog | `Evidence/P2-ANDROID/`: non-Development build, `lib/arm64-v8a` only, `libil2cpp.so` and `lib_burst_generated.so` present |
| Single-thread Web backend executes generated dispatch in Chrome and Firefox | `Evidence/P2-WEB/`: exact Immediate versus four one-step Budgeted trace comparison passed in both browsers |
| Public sample compiles against the generated API only | `Evidence/P2-020/`: the `Public Burst Nodes` sample built in a clean Unity project without internal Runtime access |
| Full detached-package regression | 902/902 EditMode; XML SHA-256 `80eca3c4b37d55c48a59371042a95058f5403f0797fda812b5535a117b319a13` |
| Static, schema, and whitespace hygiene | static 50 work items, 6 schemas, `git diff --check` |

## Pending for the gate

| Gate | Blocking condition |
| --- | --- |
| Every suite rerun from a clean committed snapshot | owner authorization for the integration commit, see `commit-package.md` |
| Runtime dependency direction reconfirmed on the committed snapshot | rerun of the assembly dependency and forbidden-token scan after the commit |
| Public API and generated artifact hashes recorded for the committed snapshot | produced by the `gate-runbook.md` artifact steps |

No normative contract was relaxed to obtain the verified rows above.
