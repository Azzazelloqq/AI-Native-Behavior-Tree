# P11-017 — Postponed parts of the memory and build-weight task

Status: `Ready` — filed 2026-11-24 when the owner postponed the rest of P11-016 ("leave it for later, push
what there is, time to do features"). Not started; it starts on the owner's word.

## Objective

Finish what P11-016 left: the attribution of the remaining build weight, the second round of memory per
agent, and the prepared-asset envelope.

## Decision record

- Owner, 2026-11-24: postponed.
- Orchestrator, 2026-11-24, under the owner's delegation and after independent reads: the design of every
  part is in `C:\UnityProjects\_aibt_acceptance\P11-016\brief.md` (items 5a and stage 3) and
  `decision-second-round.md` in the same folder (one dispatch workspace per adapter, with its gates; what
  is not done and why). The reads behind them: `attribution-read.md`, `decision-read.md`,
  `second-round-read.md`.

## Depends on

- P11-016 (`8b4ff3f`).

## Required reading

- `Planning~/Evidence/P11-016/README.md`: "What is left per leaf", "Stage 2", "Postponed by the owner".
- The files named in the decision record.

## Scope

- **Item 5a** (report only): where the rest of AIBT's weight in `GameAssembly.dll` and `il2cpp_data` is, in
  bytes of the binary, generic instantiations included; the baseline without AIBT at High; the size table.
  If `AIBT.Runtime`'s own code dominates, the split by area goes to the owner as a question.
- **Documentation:** the rewrite of "Size and memory" in `Documentation~/performance.md`; the two
  corrections of the P11-015 evidence; the Swarm Arena notes of the P11-014/P11-015 review.
- **Second round of memory:** measurements first (simulation-container agents; a same-catalog pair of trees
  that separates per-node from per-leaf cost), then one dispatch workspace per adapter, tests first, with a
  prototype measurement as a gate.
- **Stage 3:** prepared-asset envelope version 2, with the ADR-P8-009 amendment first.

- **Findings of the independent read of `f8ad6b9..8b4ff3f`** (no blocker; full text
  `C:\UnityProjects\_aibt_acceptance\P11-016\acceptance\independent-review-followup-stage2.md`):
  - the link.xml override is read from Player Settings only (not a Build Profile's defines, not
    `extraScriptingDefines`), the glue `KeepManagedNodes(BuildReport)` has no test, the build does not log
    which mode it wrote, and `BuildTargetGroup.Unknown` falls to the stripping side;
  - the ADR-P9-004 amendment states what the linker counts as a reference more widely than was measured:
    measure a prefab-only and a reflection-only managed game, Low and Medium, Addressables, or narrow the text;
  - tests to strengthen: the container trace test does not assert that the job ran; "recorded from the
    job" is satisfied by a main-thread record; no equivalence run with tracing off (the Player default);
    the borrowed-metadata identity is vacuous for empty tables and the refusal test varies the cases only;
    the two-host test depends on the Jobs Debugger preference;
  - the producer of the canonical default text (`GeneratedBlackboardContractBytesV2`, Authoring) still
    uses Newtonsoft while the verifier uses `JsonStringLiteralV1`: make the producer call the same function;
  - the source of "376 B with First Agent's catalog" (CHANGELOG, a code comment) is not in the evidence.

## Forbidden changes

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, `CodeGen~`, `package.json`, `Tests/Fixtures`,
  normative specifications, execution semantics and existing public signatures, except as the outcome of a
  recorded decision (stage 3 changes the asset envelope under its ADR amendment).
- Timing and throughput work.

## Acceptance criteria

- Each part is accepted separately: diff read, protected paths, one full run of the ten assemblies, numbers
  recomputed from raw files, an independent read.

## Evidence

`Planning~/Evidence/P11-017/README.md` (to be written).
