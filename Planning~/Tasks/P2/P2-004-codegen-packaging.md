# P2-004 — Production analyzer and generator packaging

Status: `Done`

## Objective

Ship the minimal public ABI surface plus reproducible Roslyn analyzer/incremental-generator packaging, without runtime dispatch.

## Depends on

- `P2-001`.

## Required reading

- `Documentation~/specifications/burst-node-abi-v1.md`
- P2-001 ADR and evidence.

## Allowed changes

- `Runtime/Nodes/Contracts/`
- `CodeGen~/`
- The exact analyzer package location accepted by P2-001
- `Tests/Editor/CodeGen/Contracts/`
- `Tools~/Verification/P2/CodeGen/`

## Forbidden changes

- Executor, scheduler, native layouts, emitted runtime dispatch, package upgrades, reflection, or assembly scanning.

## Deliverables

- Public attributes/contracts and stable analyzer diagnostic catalog.
- Reproducible analyzer/generator build and package verification.

## Acceptance criteria

- Every P2-001 valid/invalid declaration case is enforced with exact severity and location.
- Analysis is opt-in to AIBT declarations and does not pollute unrelated assemblies.
- Checked-in analyzer artifacts, if required by Unity, are reproducible from checked-in source and hash-verified.
- Generated text contains no machine path, timestamp, locale output, or nondeterministic enumeration.
- Public API snapshot matches the accepted ABI exactly.

## Required verification

```text
Roslyn analyzer/generator unit matrix
clean Unity import and compile
0/10/100/1000 declaration import-cost observation
public API and artifact hash verification
```

## Handoff notes

- Stop on an unapproved dependency or global analyzer side effect.

## Acceptance record

- Independently accepted on 2026-08-14 after a clean rerun: Roslyn matrix 300 assertions, Unity EditMode 3/3, exact 352-record ABI, and byte-identical analyzer SHA-256 `809075d687cbf85159dacfc672998546a2258f46e15a38362025ac78687c96be`.
- Generated output is limited to shard/catalog usability metadata; dispatch, registry, manifest, codec, fingerprint, and job generation remain owned by later cards.
