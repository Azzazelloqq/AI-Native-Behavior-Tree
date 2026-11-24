# P11-016 — Memory per agent and build weight

Status: `Review` — accepted by the orchestrator on `8b4ff3f` (full run of ten assemblies green, two
independent reads) with what was delivered: stage 0 (measurements), stage 1 (memory per agent) with its
follow-up, and items 4 and 5 of stage 2 (no Newtonsoft reference in `AIBT.Runtime`; link.xml rows of
`AIBT.Agents` only when the Player references it). On 2026-11-24 the owner postponed the rest; it is
P11-017: item 5a, the size table and the `performance.md` rewrite, the second round of memory, stage 3
(prepared-asset envelope 2).

## Objective

Bring down what AIBT costs a consumer, as the owner asked after the size measurement of P11-015 item 8.
The work follows the orchestrator's brief of 2026-11-24 (`C:\UnityProjects\_aibt_acceptance\P11-016\brief.md`),
which weighed two independent reads (`attribution-read.md`, `decision-read.md` in the same folder):

- **Stage 0, measurements only.**
  - 0a: whose the one-time block of about 74 MB with `NativeTreeAgent` is. It is Unity's job-worker
    allocator, not AIBT's.
  - 0b: a true native-only build.
  - 0c: what the generated link.xml keeps.
- **Stage 1, memory per agent:**
  1. leaf dispatch workspaces borrow the catalog's metadata arrays;
  2. no trace channel by default in a Player (ADR-P7-010 amendment 2026-11-24);
  3. a leaf-only agent-frame slot table with the catalog-wide fields held once per agent;
  4. then a report of what is left per leaf.
- **Stage 2, build weight:**
  - `AIBT.Runtime` without Newtonsoft;
  - the `AIBT.Agents` link.xml rows with `ignoreIfUnreferenced` (ADR-P9-004 amendment);
  - attribution of the rest of the weight.
- **Stage 3, the prepared asset:** envelope version 2 (ADR-P8-009 amendment).
- **Review of P11-015:** the Swarm Arena items and the "Size and memory" rewrite.

## Depends on

- `P11-015` (the size harness, the measurements and the packaging this task builds on).

## Scope

- **Stage 1:**
  - `Runtime/Execution/Burst/Dispatch/`;
  - `Runtime/Integration/GeneratedDispatch/`;
  - `Runtime/Integration/AgentFrame/AgentFrameCoreV1.cs`;
  - `Runtime/Integration/ProductionTreeAgentCore.cs`, `ProductionTreeHost.cs`, `ProductionTreeProgram.cs`,
    `NativeTreeAgent.cs`;
  - `Runtime/Trace/Native/NativeTraceContractsV1.cs`;
  - the new and changed tests;
  - the documents listed in the ADR-P7-010 amendment and the generated runtime API reference.
- **Later stages:** as the brief lists them.
- **Outside the submodule:** `Planning~/` and the harness under `C:\UnityProjects\_aibt_f14\size\`.

## Forbidden changes

Each of these needs a recorded decision:

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`;
- the compiled program format and the bytes of programs that compile today;
- `CodeGen~` and `package.json`;
- existing files under `Tests/Fixtures`;
- normative specifications and execution semantics;
- existing public signatures (only additions).

## Acceptance criteria

- Test first where behaviour changes, one commit per change; planning and evidence come last.
- After each stage 1 change: one rebuild of the memory Player and the P11-015 probe (`hosts`, `hosts100`,
  `first`, `native100`; N = 100 and 1000; managed and native separately), with the table before and after.
- Every Player runs with the probe's in-process deadline.
- Only the touched tests run here; the orchestrator makes the full run.
- `Tools~/Verification/Verify-Static.ps1` passes.
- Evidence in `Planning~/Evidence/P11-016/README.md`. Raw files go in `C:\UnityProjects\_aibt_f14\size\`.

## Evidence

`Planning~/Evidence/P11-016/README.md`.
