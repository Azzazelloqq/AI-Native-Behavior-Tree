# Phase 2 integration gate status

Checkpoint: 2026-08-17, Unity 6000.5.8f1. Gate package prepared 2026-08-18.
Gate executed and accepted 2026-08-18 against commit `a78d10a0fb2f964d64e253b284ad1cf19730f936`.

## Verdict: Accepted

Every step in `gate-runbook.md` ran against a clean local clone of the
committed snapshot and passed. Full machine-readable results are in
`verification-results.json`. Summary:

- P2-001 through P2-024 complete, including Windows x64 IL2CPP/Burst Player
  conformance and raw baseline (P2-022, `Evidence/P2-WINDOWS/`), Android
  ARM64 IL2CPP/Burst AOT (P2-023), and Web IL2CPP conformance in Chrome and
  Firefox (P2-024);
- clean detached-UPM-harness compile;
- full detached-package EditMode: **902/902**, 0 failed, 0 skipped (XML
  SHA-256 `8bfac5c0f667d2e90849272ddeef8258b0e929f6bb1f26e97b763b3ec6133afd`),
  reconfirmed against the final commit after the CodeGen reproducibility fix
  regenerated the analyzer;
- CodeGen/dispatch consumer gate: reproducibility, Roslyn matrix (1411
  assertions), clean contract tests (77/77), public sample goldens (3/3);
- allocation/lifetime gate: 12 measured windows, 0 `GC.Alloc` events, 1
  canary event;
- public API surface recorded (340 types, 1826 members,
  `public-api.txt`/`.sha256`);
- assembly dependency audit: `AIBT.Runtime`/`AIBT.Authoring` reference
  neither UnityEditor, MCP, an LLM provider, nor `Unity.Entities`;
- claim audit: two stale claims found and corrected (README.md and
  `claims-inventory.md` still described the Windows baseline as blocked/
  uncommitted after P2-022 had already landed);
- `git diff --check` and a clean working tree at every checkpoint.

Three real defects were found and fixed while exercising this gate for the
first time (not weakened, worked around): `.gitignore` silently excluded the
analyzer's own `.csproj` from version control, the reproducibility check
built outside the csproj's `PathMap`-covered directory, and the sample-golden
fixture was never copied into its verification project and then collided by
name once it was. See the `fix(p2-012)` and `fix(tools)` commits in this
series for details. Two Windows-PowerShell-5.1 native-process interop bugs
(`2>&1` promotion, `tar` misparsing a drive letter as a remote host) were
also found and fixed in the Android gate.

## Gate package

These documents were prepared in advance so the review would be mechanical
once the gate actually ran; `contract-checklist.md`, `claims-inventory.md`,
and `assembly-dependencies.json` were updated in place with the results
below rather than left as separate historical records.

| Document | Purpose |
| --- | --- |
| `contract-checklist.md` | Each Phase 2 contract mapped to its existing evidence |
| `claims-inventory.md` | Exactly what Phase 2 claims and what it deliberately does not |
| `known-limitations.md` | Scope limits carried into Phase 3 and Phase 4 |
| `commit-package.md` | Scope boundaries, exclusions, and hygiene checks used for the integration commit |
| `gate-runbook.md` | Ordered verification commands actually executed, and the `verification-results.json` shape |
| `assembly-dependencies.json` | Per-asmdef reference audit against the forbidden-dependency list |
| `public-api.txt` / `.sha256` | Reflected public surface of `AIBT.Runtime` + `AIBT.Authoring` at the accepted commit |
| `verification-results.json` | Machine-readable result of every gate-runbook step |
| `phase3-inputs.md` | What the editor phase inherits and must resolve first |
| `phase4-inputs.md` | What the benchmark and scheduler phase inherits, and the claim rules it is bound by |
