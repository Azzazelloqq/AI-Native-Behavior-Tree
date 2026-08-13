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
| Behavior cases | `Tests/BehaviorCases/`, case runner | test asmdefs |
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
