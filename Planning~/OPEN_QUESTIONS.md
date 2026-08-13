# Open questions and evidence gates

These are the only known unresolved items relevant to the current frontier. They are not permission for an agent to choose an answer silently.

| ID | Question or evidence gate | Resolution owner | Blocking |
| --- | --- | --- | --- |
| OQ-001 | Install exact Unity `6000.5.2f1` or explicitly approve a version-baseline change. | User | P0-001 |
| OQ-002 | Select CI provider and approve a secure Unity license mechanism. | User | P0-005 |
| OQ-003 | Select the Unity Web execution entry point from the required WASM spike. | Accepted ADR after P0-003 | P0-006 |
| OQ-004 | Obtain macOS/Safari verification access and define supported browser versions. | User + platform review | Public Web support matrix |
| OQ-005 | Select editor graph framework from a dedicated spike. | Accepted ADR in P3 | Editor implementation |
| OQ-006 | Decide whether runtime autotuning beats calibrated fixed heuristics. | Benchmark evidence in P4 | Auto scheduler finalization |

The following are closed and MUST NOT be reopened incidentally:

- product short name `AIBT`, namespace `AIBT`, package `com.azzazello.aibt`;
- English canonical code, API, diagnostic, schema, and documentation language;
- no required DOTS Entities dependency;
- Windows x64, Android ARM64, and single-thread Unity Web are mandatory pre-1.0 validation targets;
- execution, blackboard, async, determinism, and compiled-program v1 specifications;
- one canonical semantic JSON model and separate layout model;
- MCP is optional and not a runtime dependency.
