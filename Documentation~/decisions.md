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
| AIBT-012 | Select the graph UI framework only after an editor technology spike. | Pending spike |
| AIBT-013 | Add runtime autotuning only when benchmarks show value over calibrated heuristics. | Pending research |

