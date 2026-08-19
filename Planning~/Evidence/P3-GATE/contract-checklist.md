# Phase 3 contract checklist

Prepared 2026-08-19 for the `P3-013` review, against candidate commit
`4700b22e4a17de5d8c118c5d22dfb271a04177fc`. This is the checklist the gate
verification pass checks, not a separate acceptance record from what each P3
card's own evidence already established.

## Phase 2 gate's five "required before implementation" items (`P2-GATE/phase3-inputs.md`)

| # | Requirement | Resolution |
| --- | --- | --- |
| 1 | Resolve `OQ-005` with a dedicated spike and an accepted ADR | `P3-001` (rejects Unity Graph Toolkit on measured evidence) + `P3-014` (recommends `UnityEditor.Experimental.GraphView`); accepted as `AIBT-012` on 2026-08-18 (`ADR-P3-001`, `ADR-P3-014`) |
| 2 | Specify `*.aibt.layout.json` before any editing surface exists | `P3-002`: `Documentation~/specifications/editor-layout-v1.md` |
| 3 | Prove layout-only edits produce no compiled-program change, as an automated test | `P3-007`: `Tests/Editor/Layout/LayoutSemanticIsolationTests.cs`, re-run clean in this gate's detached harness (953/953, see `verification-results.json`) |
| 4 | Define native debugger attachment and read boundaries | `P3-010`: `Editor/Debugger/NativeExecutionDebuggerSession.cs`, strictly read-only, scoped to self-driven channels (no production Play-mode host exists yet -- disclosed, not silently assumed) |
| 5 | Decide how editor previews reuse the reference oracle | `P3-009`: `Authoring/Execution/ReferencePreviewDriver.cs` drives the accepted Phase 1 `ReferenceExecutionMachine` as-is |

All five are closed.

## Verified from existing Phase 3 evidence

| Gate | Evidence |
| --- | --- |
| Graph-framework decision made on measured evidence, not assumption | `Evidence/P3-001/`, `Evidence/P3-014/`, `ADR-P3-001`, `ADR-P3-014` |
| Layout model is fully AIBT-owned (no framework persistence to lean on) | `Evidence/P3-002/`; `editor-layout-v1.md` |
| Read-only graph adapter never mutates the semantic document on open | `Evidence/P3-003/`: byte-identical-on-disk and `Revision`-unchanged-in-memory tests |
| Deterministic auto-layout is a pure, order-stable function | `Evidence/P3-004/`: golden-byte tests + determinism-on-rerun tests |
| Manual organization (pin/group/note/reroute) and persistence round-trip losslessly | `Evidence/P3-005/`: 14 tests, including save-then-load byte-exact round trip |
| Semantic edits are gated by the real compiler/validator, no weaker in-editor path | `Evidence/P3-006/`: `SemanticEditTransaction` diagnostics asserted equal to an independent `TreeValidator.Validate` call |
| Layout/semantic isolation holds, and the test can actually detect a violation | `Evidence/P3-007/`: byte-identical-after-organization + byte-different-after-a-real-semantic-edit |
| Every diagnostic resolves to a stable Document/Node/Field graph location | `Evidence/P3-008/`: `Editor/Validation/` |
| Editor preview cannot drift from the accepted reference oracle | `Evidence/P3-009/`: step-sequence parity proof against a raw `ReferenceExecutionMachine` |
| Native debugger attachment is strictly read-only and non-blocking by construction | `Evidence/P3-010/`: allocation-neutral proof, byte-for-byte detach-is-unaffected proof |
| Trace scrubbing reproduces the actual per-step graph state, verified against raw channel data | `Evidence/P3-011/`: independently hand-replayed oracle comparison at every step |
| Large-graph measurements are recorded, not converted into a threshold | `Evidence/P3-012/`; `Benchmarks~/Platform/Editor/` |
| Full detached-package regression | 953/953 EditMode, 0 failed, 0 skipped, this gate's harness; XML SHA-256 `9855e2c158a78650b4b2d5b65f75ce4d6fb6888650047ae1ce2b4b3f0f44b415` |
| Clean detached-UPM-harness compile | this gate: exit code 0, see `verification-results.json` |
| Static, schema, and diff hygiene | static 64 work items, 6 schemas, `git diff --check` clean at the candidate commit |
| Runtime dependency direction: `Editor` depends on `Authoring`/`Runtime` only, never the reverse | `assembly-dependencies.json` |
| Public API surface recorded | `public-api.txt`/`.sha256`: 3 assemblies, 382 types, 1994 members |

## Explicitly disclosed, not silently claimed

| Item | Where disclosed |
| --- | --- |
| `Editor/Graph/`'s live `BehaviorTreeGraphView`/`BehaviorTreeNode` is never wired to `P3-004`/`P3-005`/`P3-006`/`P3-009`/`P3-010`/`P3-011`'s API/UI layers -- each hosts its own private view instance instead | Every `P3-004` through `P3-012` evidence README's "Scope and limitations" |
| No production Play-mode host exists to attach a debugger to a real running game | `Evidence/P3-010/README.md`'s Decision section; `AskUserQuestion` escalation on 2026-08-19 |
| Preview/debugger/trace-view node behavior is limited to the Phase 1 fixture/built-in set; AIBT has no production per-project leaf-registration mechanism yet | `Evidence/P3-009/README.md` |
| Large-graph render/re-render/load is explicitly reported as degraded at 1000/2000 nodes, not silently passed | `Evidence/P3-012/README.md` |

No normative contract was relaxed to obtain the verified rows above.
