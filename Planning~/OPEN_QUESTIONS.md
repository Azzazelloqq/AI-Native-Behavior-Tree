# Open questions and evidence gates

These are the only known unresolved items relevant to the current frontier. They are not permission for an agent to choose an answer silently.

| ID | Question or evidence gate | Resolution owner | Blocking |
| --- | --- | --- | --- |
| OQ-001 | Unity `6000.5.8f1` baseline and Android/Web modules validated by P0-001 evidence. | Resolved | None |
| OQ-002 | GitHub Actions selected. Unity validation uses a pre-activated self-hosted Windows runner; workflows receive no license secrets. | Resolved | None |
| OQ-003 | Select the Unity Web execution entry point from the required WASM spike. | Accepted ADR after P0-003 | P0-006 |
| OQ-004 | Obtain macOS/Safari verification access and define supported browser versions. | User + platform review | Public Web support matrix |
| OQ-005 | Select editor graph framework from a dedicated spike. | Accepted ADR in P3 | Editor implementation |
| OQ-006 | Decide whether runtime autotuning beats calibrated fixed heuristics. | Resolved: rejected, see [ADR P4-007](../Documentation~/decisions/ADR-P4-007-runtime-autotuning-resolution.md) | None |
| OQ-007 | Define what "reload" means for a semantically changed tree with a live instance mid-execution (abort and restart? migrate in place? explicitly unsupported for a first cut?), and the compatibility-classification rule the reload strategy depends on. | Dedicated spike/decision in Phase 5 | `P5-002` onward |

The following are closed and MUST NOT be reopened incidentally:

- product short name `AIBT`, namespace `AIBT`, package `com.azzazello.aibt`;
- English canonical code, API, diagnostic, schema, and documentation language;
- no required DOTS Entities dependency;
- Windows x64, Android ARM64, and single-thread Unity Web are mandatory pre-1.0 validation targets;
- execution, blackboard, async, determinism, and compiled-program v1 specifications;
- one canonical semantic JSON model and separate layout model;
- MCP is optional and not a runtime dependency.
