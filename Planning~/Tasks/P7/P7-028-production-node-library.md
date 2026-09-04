# P7-028 — Production-ready built-in node library

Status: `Done`

## Objective

Owner request: a real, usable base library of common nodes for production, not just structural
composites/decorators. Confirmed by reading `Authoring/Model/Nodes/BuiltInNodeManifests.cs`: the
entire built-in catalog is **11 structural nodes only** — `MemorySequence`, `ReactiveSequence`,
`MemorySelector`, `ReactiveSelector`, `Parallel`, `Inverter`, `Succeeder`, `Failer`, `Repeater`,
`Timeout`, `Cooldown` — plus 3 `internal`, test-only leaves (`aibt.test.success/failure/running`)
that exist purely for assertions, not for real trees. There is no built-in condition node (e.g.
blackboard comparison), no built-in wait/delay leaf, no random-selection composite, no distance/range
check, nothing a real game author can drop into a tree without writing a custom node first. Every
consumer currently has to build their own base vocabulary from scratch.

## Depends on

- `Documentation~/specifications/burst-node-abi-v2.md` (the current node ABI every new built-in must
  target — confirm live which ABI version production built-ins compile against today, do not assume
  it matches the spec's own version number without checking).
- `AGENTS.md`'s node rules (unmanaged data, declared blackboard reads/writes/side effects/execution
  domain/determinism/cost category; a node has one responsibility and defined status semantics).

## Required reading

- `Authoring/Model/Nodes/BuiltInNodeManifests.cs` and the real implementation of at least one
  existing built-in decorator/composite (e.g. `Repeater`/`Cooldown`) end-to-end — manifest, Burst
  execution struct, and how it's wired into codegen — as the pattern every new node must follow
  exactly, not a reinvented shape.
- `Samples~/BurstNodes/` (an existing, real custom-node example — confirms the authoring pattern a
  production node also needs, and whether any of its nodes are themselves good candidates for
  promotion to built-in rather than duplicated).
- `Documentation~/data-formats.md`'s node-manifest section (the contract every new manifest must
  satisfy — `whenToUse`/`whenNotToUse`/examples/parameters, all currently required and validated by
  `NodeManifest`'s own constructor).

## Allowed changes

- New built-in node types in the same location/pattern as the existing 11 (manifest + Burst
  implementation + codegen wiring) — condition/utility/action leaves, e.g.: a blackboard-value
  comparison condition, a fixed-duration wait leaf, a random-child selector, a
  blackboard-boolean-gate decorator. Exact list is this card's own scoping decision, not fixed here —
  propose the concrete list during planning, grounded in what real trees commonly need (patrol/react
  patterns already exist as golden fixtures — mine them for real recurring needs rather than
  guessing).
- `Documentation~/generated/` (regenerated API reference/node catalog docs, mechanical, per the
  existing generator's own drift-check discipline).
- `Planning~/Evidence/P7-028/`.

## Forbidden changes

- Do not deprecate or change any of the existing 11 built-ins' behavior or contracts — purely
  additive, matching this project's own established public-API-stability discipline (`P7-001`/
  `P7-020`).
- Do not invent project-specific gameplay nodes (e.g. "move to target," "play animation") — those are
  legitimately project-specific and belong in a consuming project's own extensions
  (`IReferenceLeafBehaviorProvider`, `P7-008`), not the generic built-in catalog. Keep the new nodes
  domain-agnostic, matching the existing 11's own generality.

## Deliverables

- A real, documented, non-trivial expansion of the built-in node catalog covering at least the
  condition/utility gap identified above.
- Every new node ships with a manifest satisfying the existing validation contract (examples,
  when-to-use/when-not-to-use, parameters) — no placeholder text.

## Acceptance criteria

- The new nodes are usable end-to-end in a real tree, opened and validated through the normal
  authoring/compile pipeline (not just unit-tested in isolation).
- Public-API diff (`P7-020`'s own CI-style check, run locally) confirms the change is purely
  additive.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
Get-FullPublicApi.ps1 -BaselinePath Tools~/Verification/P7/Audit/Baseline/public-api-baseline.txt
```

## Handoff notes

- Owner request this session (2026-09-03) — confirmed in scope for `1.0`. The concrete node list is
  deliberately left open for the implementing session's own planning pass, grounded in real recurring
  patterns (existing golden/sample fixtures) rather than picked here.

## Outcome

Investigation before planning found the codebase actually has **three** parallel node-authoring
mechanisms, not the one this card's own "same location/pattern as the existing 11" framing implied:
the manual `NodeManifest` builder the 11 composites use (execution hardcoded into
`NativeLifecycleMachineV1`), `IReferenceLeafBehaviorProvider` (managed, reference-executor-only,
P7-008), and `[AibtBurstNode]` attribute + codegen (real native Burst execution, `BurstNodeKind` is
leaf-only — no Decorator/Composite value exists). Owner chose both mechanisms at once for the new
nodes, which scoped this card to leaf nodes only (Condition/Action).

Two genuinely new architectural facts surfaced mid-implementation, each resolved with the owner:

1. A node that reads the blackboard from a native `[AibtBurstNode]` struct can only do so through a
   `GeneratedHandle` config field + `[AibtBlackboardBinding]`, which the reference compiler
   (`ReferenceCompiler.BuildBlackboardSlots`) has no support for at all — it only resolves blackboard
   access through literal `NodeManifest.Reads` key names. A manifest shaped to satisfy the native side
   would have empty `Reads`, meaning the reference behavior could never actually observe the value.
   Owner dropped the planned `aibt.core.blackboard-bool-condition` node from scope rather than either
   building the reference-compiler feature this would require or making the node reference-only.
2. `AIBT.CodeGen`'s `BurstNodeGenerator` permanently freezes the `aibt.core.` namespace against any
   live `[AibtCatalogSet]` shard (AIBT5012) — its authority-merge step treats a shard's own declared
   identity as a duplicate the moment it also appears in `RuntimeBuiltInCatalogAuthority` (the frozen
   11-composite snapshot), and as unauthorized the moment it doesn't. There is no way to add a new
   natively-executed `aibt.core.*` node through this pathway; that namespace is permanently reserved
   for the original 11 hardcoded structural composites/decorators. Owner chose a new always-on
   namespace, `aibt.stdlib.*`, for built-in leaves that do carry a real native declaration.

Delivered: two production nodes, each shipping both a real native Burst execution path and a real
reference-executor path, byte-identity-checked against each other where the compile-time ABI
enforcement requires it:

- `aibt.stdlib.wait` (Action) — runs for a configured `ticks` count, then succeeds.
- `aibt.stdlib.random-condition` (Condition) — succeeds with a configured `success-chance-percent`
  probability, drawn from the native side's real per-instance deterministic Burst random stream on
  native, and a `System.Random` instance on reference (disclosed, not bit-identical between the two
  backends — the manifest's own `whenNotToUse` text says so).

`NodeRegistryBuilder.CreateWithBuiltIns()` now also folds these in via a new `AddBuiltInLeaf` (source
`BuiltIn`, a real reference handler binding, distinct from `AddProjectExtension`'s `UserExtension`
source since `aibt.core.`/`aibt.stdlib.` are reserved, project-extension namespaces are not).
`RuntimeBuiltInCatalogAuthorityVerifier`'s rebuild was narrowed to the `aibt.core.` subset of the
registry only, matching what the frozen authority actually represents now that "built-in" and
"aibt.core." are no longer the same set.
