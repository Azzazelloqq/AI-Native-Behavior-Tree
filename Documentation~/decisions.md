# Architectural decisions

This file records accepted direction. Detailed ADR files may be introduced when implementation begins.

| ID | Decision | Status |
| --- | --- | --- |
| AIBT-001 | Use one repository and one UPM package with independently compiled layers. | Accepted |
| AIBT-002 | Do not require DOTS Entities; use Burst, Jobs, and Unity Collections. | Accepted |
| AIBT-003 | Guarantee zero GC allocations per tick only for the initialized Burst path. | Accepted |
| AIBT-004 | Separate canonical semantics, shared visual layout, and compiled runtime data. | Accepted |
| AIBT-005 | Use strict versioned JSON as the canonical interchange format. | Accepted |
| AIBT-006 | Let Unity Job System own threads; AIBT selects policy, batching, and budgets. | Accepted |
| AIBT-007 | Support generated unmanaged nodes and an explicit managed fallback. | Accepted |
| AIBT-008 | Include hot reload; persistent save/load of live execution state is excluded. | Accepted |
| AIBT-009 | Make MCP optional, model-neutral, transactional, and schema-driven. | Accepted |
| AIBT-010 | Treat graph readability, layout, comments, groups, and reroutes as product features. | Accepted |
| AIBT-011 | Base initial development on Unity 6; add compatibility claims only after verification. | Accepted |
| AIBT-012 | Use [`UnityEditor.Experimental.GraphView`](decisions/ADR-P3-014-editor-graph-framework.md) as the editor graph framework; Unity Graph Toolkit was rejected ([ADR P3-001](decisions/ADR-P3-001-editor-graph-framework.md)). | Accepted |
| AIBT-013 | Add runtime autotuning only when benchmarks show value over calibrated heuristics; tested and rejected — see [ADR P4-007](decisions/ADR-P4-007-runtime-autotuning-resolution.md). | Accepted |
| AIBT-014 | Fix public statuses to Success, Failure, and Running; lifecycle and composite behavior follow Execution Semantics v1. | Accepted |
| AIBT-015 | Execute one tree instance sequentially; `Parallel` is semantic, while CPU parallelism is across instances and batches. | Accepted |
| AIBT-016 | Use command/completion records instead of Task, threads, or coroutines in the runtime async contract. | Accepted |
| AIBT-017 | Support Windows x64, Android ARM64, and single-threaded Unity Web as mandatory pre-1.0 validation targets. | Accepted |
| AIBT-018 | Keep the product short name and namespace `AIBT` and package ID `com.azzazello.aibt`. | Accepted |
| AIBT-019 | Use English for canonical code, APIs, identifiers, diagnostics, schemas, and documentation; translations may be supplemental. | Accepted |
| AIBT-020 | Use the versioned generated public Burst node ABI, per-assembly metadata shards, and explicit consumer-owned catalog-set facade defined by [ADR P2-001](decisions/ADR-P2-001-public-burst-node-abi.md). | Accepted |
| AIBT-021 | Use explicit native owners, immutable preflight capacity plans, staged atomic publication, and dependency-complete disposal defined by [ADR P2-002](decisions/ADR-P2-002-native-ownership-and-capacity.md). | Accepted |
| AIBT-022 | Use Burst node ABI v2 with source-stable public signatures, Runtime-private job-safe carrier backing, and an implicit metadata-only Runtime built-in authority as defined by [ADR P2-012](decisions/ADR-P2-012-burst-node-abi-v2.md). | Accepted |
| AIBT-023 | Hot reload always constructs a fresh instance and selectively copies surviving state keyed by stable authoring node ID, never by compiled index or in-place array mutation; full restart, subtree restart, and compatible migration are the same mechanism with a different exclusion set, defined by [ADR P5-001](decisions/ADR-P5-001-hot-reload-compatibility-model.md). | Accepted |
| AIBT-024 | AIBT's MCP server is an external `dotnet` process on the official C# MCP SDK (stdio transport), bridged to the Unity Editor by a thin, no-SDK Editor-only listener over a discovery file; requires the .NET SDK on the user's machine (no vendored binary); permission model is a fixed category taxonomy every tool declares against, defined by [ADR P6-001](decisions/ADR-P6-001-mcp-transport-and-permission-model.md). | Accepted |
| AIBT-025 | Domain patches are two kinds, never unified: a semantic patch (`TreeDocument`, checked against `Revision`) and a layout patch (`LayoutDocument`, checked against a computed content hash, no new persisted field); dry-run is calling the transaction without persisting; diffs are purpose-built node/field-level comparisons, not a generic deep-diff, defined by [ADR P6-002](decisions/ADR-P6-002-domain-patch-revision-and-diff-model.md). | Accepted |
| AIBT-026 | Widen `ReferencePreviewDriver`'s facade to surface completions injection, resume-with-step-budget, abort (via the `Abort(update, reason, index)` overload, not `RequestAbort`), and a caller-supplied `TreeInstanceId`; `rootSeed` and behavior-case-style external events remain explicitly out of scope as genuine missing engine capability, not facade gaps, defined by [ADR P6-013](decisions/ADR-P6-013-reference-preview-driver-simulation-capability.md). | Accepted |
| AIBT-027 | Native trace production wiring is an external recorder co-located with whatever already drives `NativeLifecycleMachineV1` (never a change inside it), mapping lifecycle steps to `NativeTraceEventKindV1` per a fixed table; `SnapshotRevision`/`TreeSemanticHash`/`TreeInstanceId` are caller-supplied and abort reason/in-flight state are driver-tracked, never new machine accessors; a real, disclosed gap remains where `CompositeExited` for a non-root composite carries no exit status, defined by [ADR P6-015](decisions/ADR-P6-015-native-trace-production-wiring.md). | Accepted |
| AIBT-028 | `HotReloadPreviewDriver.TryReload` gains a purely additive `internal` overload accepting an optional `IReferenceTraceSink`, never a public facade change (the sink type is itself `internal`, so no public-parameter option was ever real); a future benchmark-owning assembly needs matching `InternalsVisibleTo` grants from both `AIBT.Runtime` and `AIBT.Authoring`, mirroring `P4-001`'s own technique; the sink attaches to the migrated/restarted instance's own future ticks, not the reload procedure itself, defined by [ADR P6-020](decisions/ADR-P6-020-hot-reload-trace-injection.md). | Accepted |

Normative details are in `Documentation~/specifications/`. An implementation may change them only through an accepted decision and corresponding specification update.
