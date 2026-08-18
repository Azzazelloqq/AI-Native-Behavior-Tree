# P2-001 — Public Burst node ABI and Roslyn feasibility

Status: `Done`

## Objective

Accept the exact versioned public Burst node ABI and prove that its analyzer/source-generator approach works in the pinned Unity toolchain before any production custom-node implementation.

## Depends on

- `P1-018`.

## Required reading

- `Documentation~/specifications/node-contract-v1.md`
- `Documentation~/specifications/execution-semantics-v1.md`
- `Documentation~/specifications/blackboard-v1.md`
- `Documentation~/specifications/update-phases-v1.md`
- `Documentation~/specifications/time-and-random-v1.md`
- `Planning~/Evidence/P1-GATE/phase2-inputs.md`

## Allowed changes

- `Documentation~/specifications/burst-node-abi-v1.md`
- Focused completion of `Documentation~/specifications/time-and-random-v1.md` for the public context seed combiner and published vectors
- Focused allocation of the analyzer diagnostic range in `Documentation~/specifications/diagnostics-v1.md`
- A focused P2-001 ADR linked from `Documentation~/decisions.md`
- `Spikes~/BurstNodeAbi/`
- `Planning~/Evidence/P2-001/`

## Forbidden changes

- Production Runtime, Authoring, compiler, code-generator, or executor implementation.
- Reflection, delegate/interface hot-path dispatch, managed fallback, or public composite/decorator ABI without an accepted child-transition contract.
- Silent reinterpretation of existing manifest read/write declarations or persisted formats.

## Deliverables

- Exact attributes, supported node kinds, unmanaged config/memory constraints, callback signatures, abort/exit reasons, typed blackboard/snapshot/command/random accessors, ABI versioning, analyzer diagnostics, and generated artifact contract.
- A decision on static generated callbacks versus alternatives; custom Condition/Action-only v1 is the default proposal.
- A disposable Unity/Roslyn/Burst harness proving analyzer and incremental generator discovery in a clean UPM consumer.

## Acceptance criteria

- Valid public declarations generate deterministic byte-identical output and Burst-compile in Unity `6000.5.8f1`.
- Invalid managed fields, signatures, kinds, accessibility, type collisions, undeclared/wrong-type access, and forbidden Unity APIs produce exact diagnostics and no usable binding.
- Generated feasibility dispatch contains no reflection, boxing, delegates, interface/virtual dispatch, arbitrary pointers, strings, tasks, coroutines, or `UnityEngine.Object` access.
- ABI version and node type version are distinct, and every breaking rule has an explicit compatibility outcome.
- Deterministic PCG stream ownership and root-seed/tree-hash/instance combiner have accepted published vectors; abort and budget suspension consume no additional random value.
- Independent review accepts the normative contract and evidence.

## Required verification

```text
Verify-Static.ps1
Verify-Schemas.ps1
isolated analyzer/generator tests
two clean generation runs with SHA-256 comparison
Unity clean-harness compile and focused Burst compile
```

## Handoff notes

- Explicit owner direction on 2026-08-14 authorizes this contract task without waiting for the remote `P0-005` runner; the infrastructure item remains unpassed.
- Stop if typed key binding requires an unapproved format change, Unity does not load the generator deterministically, or Burst rejects every compliant callback design.
- Record generator/import time and generated code size as feasibility observations, not product thresholds.
