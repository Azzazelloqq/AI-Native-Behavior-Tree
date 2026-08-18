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
| AIBT-013 | Add runtime autotuning only when benchmarks show value over calibrated heuristics. | Pending research |
| AIBT-014 | Fix public statuses to Success, Failure, and Running; lifecycle and composite behavior follow Execution Semantics v1. | Accepted |
| AIBT-015 | Execute one tree instance sequentially; `Parallel` is semantic, while CPU parallelism is across instances and batches. | Accepted |
| AIBT-016 | Use command/completion records instead of Task, threads, or coroutines in the runtime async contract. | Accepted |
| AIBT-017 | Support Windows x64, Android ARM64, and single-threaded Unity Web as mandatory pre-1.0 validation targets. | Accepted |
| AIBT-018 | Keep the product short name and namespace `AIBT` and package ID `com.azzazello.aibt`. | Accepted |
| AIBT-019 | Use English for canonical code, APIs, identifiers, diagnostics, schemas, and documentation; translations may be supplemental. | Accepted |
| AIBT-020 | Use the versioned generated public Burst node ABI, per-assembly metadata shards, and explicit consumer-owned catalog-set facade defined by [ADR P2-001](decisions/ADR-P2-001-public-burst-node-abi.md). | Accepted |
| AIBT-021 | Use explicit native owners, immutable preflight capacity plans, staged atomic publication, and dependency-complete disposal defined by [ADR P2-002](decisions/ADR-P2-002-native-ownership-and-capacity.md). | Accepted |
| AIBT-022 | Use Burst node ABI v2 with source-stable public signatures, Runtime-private job-safe carrier backing, and an implicit metadata-only Runtime built-in authority as defined by [ADR P2-012](decisions/ADR-P2-012-burst-node-abi-v2.md). | Accepted |

Normative details are in `Documentation~/specifications/`. An implementation may change them only through an accepted decision and corresponding specification update.
