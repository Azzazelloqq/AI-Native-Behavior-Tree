# Work ownership

Ownership is per assigned work item, not permanent personnel ownership.

| Area | Default exclusive paths | Integration-owned shared paths |
| --- | --- | --- |
| Runtime core | Task-specific subdirectories under `Runtime/Core/` and `Runtime/Execution/`, focused runtime tests | Runtime asmdef, package metadata |
| Blackboard | `Runtime/Blackboard/`, blackboard tests | Runtime asmdef |
| Authoring model | `Authoring/Model/`, focused authoring tests | Authoring asmdef |
| Formats | `Authoring/Serialization/`, format fixtures | schemas and package metadata |
| Validation | `Authoring/Validation/`, validation fixtures | diagnostic catalog index |
| Compiler | `Authoring/Compilation/`, compiler tests | compiled-format version registry |
| Behavior cases | `Tests/BehaviorCases/`, case runner, and its task-owned asmdef in P1-017 | other test asmdefs |
| Semantic integration | `Tests/Integration/SemanticSlice/`, fixtures, sample, and its task-owned asmdef in P1-018 | other test asmdefs |
| Node ABI and code generation | Task-specific paths under `Runtime/Nodes/Contracts/`, `CodeGen~/`, generated bridges, and focused tests | analyzer package placement, runtime/authoring asmdefs, persisted schema versions |
| Native runtime | Task-specific paths under `Runtime/Compiled/Native/`, `Runtime/State/Native/`, `Runtime/Execution/Native/`, and focused tests | Runtime asmdef, public ABI, shared diagnostic/version registries |
| Native integration and scheduling | Task-specific paths under `Runtime/Integration/`, `Runtime/Commands/Native/`, `Runtime/Scheduling/Native/`, and focused tests | Runtime asmdef and public host-facing contracts |
| Phase 2 verification | Task-specific paths under `Tools~/Verification/P2/`, `Benchmarks~/Phase2/`, P2 platform evidence, and focused fixtures | package metadata, test asmdefs, compatibility/claim summaries |
| Platform spikes | `Spikes~/`, `Benchmarks~/Platform/` | accepted decisions and package dependencies |
| Editor | `Editor/`, editor tests | editor asmdef and layout schema |
| MCP | `Tools~/McpServer/` | public schema registry |

The following files are coordinator/integration-owned unless a card explicitly assigns them:

- `package.json` and all `.asmdef` files;
- `CHANGELOG.md`, `README.md`, and `Documentation~/decisions.md`;
- normative files in `Documentation~/specifications/`;
- `Planning~/work-items.json`;
- schema version registries and release configuration.

An agent may read any file. Writing outside the card's allowed paths requires coordinator approval before the edit.
