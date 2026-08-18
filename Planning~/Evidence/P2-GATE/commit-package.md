# Phase 2 integration commit package

Prepared 2026-08-18. `P2-025` must verify a clean committed snapshot, but every
Phase 2 change is intentionally uncommitted because repository policy requires
owner approval before committing. This document defines the scope boundaries and
checks for that commit. It is not a snapshot of the working tree; the exact file
list is produced from the tree at commit time.

## 1. Produce the scope listing

```powershell
git status --porcelain
git diff --stat
git diff --check
git status --porcelain --ignored | Select-String '^!!'
```

The last command exists to confirm that logs, Players, APKs, Web builds, isolated
harnesses, `TestResults`, and benchmark raw output are ignored rather than merely
unstaged.

## 2. Confirm what belongs in the commit

| Area | Included |
| --- | --- |
| `Runtime/` | native program, state, blackboards, snapshots, commands, diagnostics, execution, scheduling, node contracts |
| `Authoring/` | compilation, registry, and generated authoring artifacts referenced by Phase 2 |
| `Analyzers/AIBT.CodeGen.dll` and `.sha256` | the frozen analyzer whose digest the harnesses assert |
| `CodeGen~/` | generator and analyzer sources |
| `Tests/` | native runtime, integration, behavior-case adapters, allocation, fixtures |
| `Samples~/BurstNodes/` | public generated-API sample |
| `Tools~/Verification/P2/` | allocation, CodeGen, Android, Web, Windows tooling |
| `Benchmarks~/Phase2/` | dispatch and Windows harnesses, schemas, retained sanitized results |
| `Documentation~/` | Phase 2 specifications, ADRs, burst node authoring |
| `Planning~/` | P2 task cards, evidence directories, work-item statuses |
| root metadata | `package.json`, `CHANGELOG.md`, `README.md`, assembly definitions |

## 3. Confirm what must not enter the commit

- Unity logs, NUnit XML, Burst debug directories, isolated harness projects.
- Windows Players, Android APKs, Web build output, and their raw JSON except the
  sanitized results already tracked under `Benchmarks~/Phase2/Dispatch/Results/`.
- The toolchain preflight report and anything else under
  `Tools~/Verification/TestResults/`.
- Generated IDE and solution files, `Library/`, `Temp/`, `obj/`, `bin/`.
- Machine-specific absolute paths inside any committed text file.
- Secrets, credentials, and local MCP cache state.

## 4. Confirm the claim surface before committing

`README.md`, `CHANGELOG.md`, and every `Planning~/Evidence/` document must already
match `claims-inventory.md`. In particular, at commit time:

- `P2-022` is `blocked` in `Planning~/work-items.json` unless the Windows Player
  actually ran, in which case its evidence is committed in the same change;
- `P2-025` remains `blocked` — the commit enables the gate, it does not pass it;
- `P0-005`, `P0-006`, and `P1-019` keep their existing honest states;
- no document claims a Windows Player result, a performance default, device
  performance, Safari or mobile Web support, or `PipelinedJobs` and `Auto`.

## 5. Commit shape

Use the repository convention from `Planning~/AGENT_WORKFLOW.md`, one imperative
outcome per area, ordered so the tree compiles at each step:

```text
runtime: add native Phase 2 execution, storage, and scheduling
codegen: add ABI v2 generator, analyzers, and frozen artifact digest
tests: add native equivalence, allocation, and public sample coverage
tools: add P2 verification and platform harnesses
docs: add Phase 2 specifications, decisions, and evidence
planning: record Phase 2 work-item states and evidence
```

A single squashed commit is acceptable if the owner prefers it; the gate needs a
clean snapshot, not a particular history shape.

## 6. After committing

```powershell
git status --porcelain
git diff --check
git log -1 --format='%H %s'
```

Record the resulting SHA as the `P2-025` candidate commit, then follow
`gate-runbook.md`. Do not start the review without the explicit owner
authorization named in `Planning~/USER_ACTIONS.md`.
