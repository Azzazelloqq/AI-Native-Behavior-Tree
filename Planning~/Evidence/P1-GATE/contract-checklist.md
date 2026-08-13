# Phase 1 contract checklist

| Gate | Evidence |
| --- | --- |
| Canonical JSON round-trip and strict invalid-input diagnostics | Editor/BehaviorCases suites; schema verification; golden fixtures |
| Deterministic registry and compiler hashes/layout | compiler and registry tests; P1-018 deterministic compile projection |
| Lifecycle, abort, memory, composite, decorator, observer semantics | Runtime reference-executor suites |
| Blackboard canonical values, versions, reset, registered equality | Runtime blackboard suites |
| Async commands/completions and exactly-once cancellation | CommandsAndAsync suites |
| Step-budget partition equivalence | Budgeting suite and P1-018 golden slice |
| Backend-neutral behavior cases | BehaviorCases and Integration suites |
| Runtime layering | assembly dependency report and static forbidden-token scan |
| Clean UPM installation | detached clone installed under `Packages/com.azzazello.aibt`, 580/580 |
| Platform evidence without performance overclaim | P0-003 Web and P0-004 Android evidence |

No normative contract was relaxed to obtain these results.
