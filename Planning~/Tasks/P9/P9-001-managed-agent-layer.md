# P9-001 — Managed agent layer

Status: `Review`

## Authorization and dependencies

Owner approved the P9 plan in chat on 2026-11-11 ("both layers": a simple managed layer plus
ergonomics for the native path). The same approval explicitly authorizes the additive
node-contract change below. Builds on baseline commit `2612389` (P8-009..P8-012 remain `Review`;
this card does not promote them). Dependencies P3-009, P7-008, P7-028 are Done.

## Objective

A consumer adds one component, assigns a tree and writes a leaf as an ordinary C# class. No
generated catalog, runtime asset, host/program/world wiring or effect hashing is required.

## Plan and owned paths

1. New assembly `Agents/` (`AIBT.Agents`; references `AIBT.Runtime`, `AIBT.Authoring`).
   `BehaviorTreeAgent` drives the existing `ReferenceExecutionMachine` on the main thread;
   composites, decorators and reactive semantics are unchanged.
2. `BtAction` / `BtCondition` base classes, `[BtNode]`, public fields as parameters, `BbKey<T>`
   blackboard-key parameters, named blackboard access from gameplay code.
3. Additive node-contract change (authorized above, recorded in `ADR-P9-001`): parameter types
   `int32`, `string`, `blackboard-key`, valid only for `MainThread` manifests and never packed
   into the configuration blob. Burst ABI v2, generator output and existing canonical bytes are
   unchanged.
4. Idle-boundary external blackboard read/write on the reference machine with observer
   notification, mirroring ADR-P7-039 semantics for the native host.
5. `.aibt` importer produces a Player-loadable `BehaviorTreeSource`; `.aibt.json` is accepted as
   a `TextAsset`.
6. Starter `aibt.unity.*` leaves and the `Samples~/SimpleAgent` sample.

Owned: `Agents/`, `Runtime/Execution/Reference/`, `Runtime/Blackboard/Storage/`,
`Runtime/AssemblyInfo.cs`, `Authoring/Model/Nodes/`, `Authoring/Validation/`,
`Authoring/Registry/`, `Authoring/Compilation/`, `Authoring/AssemblyInfo.cs`, `Schemas~/`,
`Editor/Graph/BehaviorTreeImporter.cs`, `Editor/Registry/`, `Tests/`, `Samples~/SimpleAgent/`,
`Documentation~/`, `CHANGELOG.md`, `Planning~/Evidence/P9-001/`.

Forbidden: scheduler defaults, native execution semantics, Burst ABI v2, package version.

## Acceptance

- A tree with managed leaves runs in Play Mode from one component; root completion can restart.
- Gameplay code reads and writes blackboard values by name; a write re-evaluates reactive parents.
- Existing reference/native equivalence and full EditMode suites stay green.
- Managed leaves appear in the graph editor catalog and validate without a generated catalog.

## Verification

Focused EditMode tests for the driver, parameter types and external writes; full AIBT EditMode
assemblies; live Play Mode run of the sample through the Unity CLI; `Verify-Static.ps1`.

Baseline in the `Modules` host project before this card (Unity 6000.7.0b1, 2026-11-11):
`AIBT.Runtime.Tests` 707/707, `AIBT.Editor.Tests` 497/499, `AIBT.Integration.Tests` 58/58,
`AIBT.BehaviorCases.Tests` 65/65. The two existing failures are
`McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` and
`McpVerificationToolDispatcherTests.CompileReturnsTheSameContentHashAsADirectReferenceCompilerCall`.

## Handoff

Task ID: P9-001. Outcome: implemented and verified in the `Modules` host on Unity 6000.7.0b1;
status Review, not Done. Branch: feature/aibt-consumer-workflow; local commits only, no push.
Verification: `AIBT.Runtime.Tests` 707/707, `AIBT.Editor.Tests` 549/551,
`AIBT.Integration.Tests` 58/58, `AIBT.BehaviorCases.Tests` 65/65; `Verify-Static.ps1` passed
(7 schemas). The two Editor failures are the pre-existing host-only MCP catalog/hash tests, now
shown to differ only by the imported Swarm Arena and Simple Agent sample nodes.
See ../../Evidence/P9-001/README.md.
Known limits: no Player build yet; live Play Mode run of the sample not repeated in the tail;
API reference does not cover `AIBT.Agents`; condition interrupts are managed-only; the reference
Preview window explains instead of running managed trees.
