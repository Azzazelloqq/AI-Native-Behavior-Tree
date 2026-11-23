# P11-010 evidence — a ReadWrite binding read and written in one tick can stop the host

Branch `feature/aibt-consumer-workflow`, base `1db104a`. Raw files: `C:\UnityProjects\_aibt_acceptance\P11-010\`.
Unity 6000.6.9f1, the Modules Editor (Pipeline port 7810). Follow-up 1 of `Planning~/Evidence/P11-009/README.md`
("Not verified", F9). Status: accepted by the orchestrator on `0c2b541` ("Acceptance run" at the end); the item is in `Review`.

## Commits

| Commit | Content |
| --- | --- |
| `1a5e1cd` | fix: the catalog sizes two value sessions and two values per ReadWrite binding; fixture shard, tests |
| this commit | task card, work item, MASTER_PLAN, CHANGELOG, this evidence |

The fix commit holds its test. "Before" runs were made with the final test in place and the fix absent (the
file restored from `HEAD`); "after" runs with both.

## The condition (reproduced)

- **Sessions per access.** Every `TryRead` and every `TryWrite` of a generated node opens its own value
  session with its own staging bytes for the rest of the frame (`BurstBindingBridgeCoreV2.TryBeginBlackboardRead`
  → `TryAllocateBlackboardReader` → `TryAllocateSession`; `TryBeginBlackboardWrite` → `TryAllocateSession`).
  `TryAllocateSession` returns `CapacityExceeded` when `SessionCount + 1` exceeds the session array or the
  staging bytes exceed the staging array; the result is latched on the frame and the host stops with
  `NativeLifetimeStateInvalid`. A ReadWrite binding read and then written in one tick (the shape of
  `aibt.native.increment-int`, `increment-float`, `toggle-bool`) therefore needs **two sessions and twice its
  value size**.
- **Capacity.** `GeneratedBurstCatalogV2.CreateWorkspaceCapacity` took, over all cases of the catalog (the
  catalog set: every shard it lists), the maximum of Σ sessions (1 per binding, 2 per AsyncOperation) and
  Σ staging (value size per binding, plus the secondary size for AsyncOperation). A ReadWrite binding counted
  one session and one value.
- **Exact condition.** A node fails when its case's demand in one tick (every binding touched once, +1 session
  and +1 value per ReadWrite binding read and written) exceeds the largest case's Σ, in the paths that run a
  node in its own frame with per-request room: `ProductionTreeHost`, and `NativeTreeAgent` under Interactive,
  Background, forced Immediate, Budgeted and JobsAgentFrame. Under BatchedJobsSameFrame and PipelinedJobs the
  group workspace is sized per request × participants (at least 2, `GeneratedDispatchGroupWorkspaceV2`,
  `GeneratedDispatchGroupExecutorV2`) and each request opens its own frame with the session count reset
  (`BurstDispatchBridgeCoreV2` → `BurstBindingBridgeCoreV2.PrepareFrame`), so a request has at least twice
  the room: the same node ran there (before: both policies pass, `before.txt`).
- **Which catalogs had room by accident** (`probe-capacity-before.txt`, every catalog set compiled in this
  Editor): every catalog that lists `StandardNativeShard` (NativeGuard sample, the P11-002 library catalog,
  JobsEquivalence, the simulation catalogs) has a larger case (3 sessions, 28 bytes), so increment-int,
  increment-float (2 / 8) and toggle-bool (2 / 2) fit; the P11-009 mixed-scope catalog has room 4 / 13 from
  `mixed-report`, so `agent-bump` (2 / 8) fits. The only catalogs where a ReadWrite case exceeds are the new
  fixture catalogs (1 / 4 against 2 / 8). No sample node (FirstAgent, NativeGuard, SwarmArena, BurstNodes)
  and no MCP template uses a ReadWrite binding. Who could hit it: a project catalog set whose largest node
  reads and writes a ReadWrite binding, e.g. a first custom counter node in its own catalog set (the MCP node
  template makes a one-shard set), or a library-style node with more ReadWrite bindings than the largest
  case has spare.
- **What P11-009 hit:** no raw file of P11-009 holds the original node (`P11-009\` has no
  `CapacityExceeded` output). By its own description — one node using one ReadWrite binding for a read and a
  write, in a catalog whose largest case had no spare binding — it is this condition; if that node also held
  the four bindings `mixed-report` has now, it needed 6 sessions against a room of 5 (inferred, not rerun).
- **Probe:** `P11010SessionCapacityProbe.cs` (a temporary test class, not committed; source in the raw folder as
  `P11010SessionCapacityProbe.cs.txt`) writes `probe-capacity-*.txt` (`-before`, `-before-final` with the hold
  catalog, `-after-attempt1`, `-after`; its "fits (spare room from a larger case)" label is the probe's wording, also
  printed after the fix where the case fits on its own); the committed test reproduces on both hosts.

## Fix

- `GeneratedBurstCatalogV2.CreateWorkspaceCapacity` (+7 −2): a `BlackboardReadWrite` binding counts two
  sessions and twice its value size, as an AsyncOperation already counts its start and cancel. Runtime only:
  the generator, the emitted layout blob, the ABI, the program format and the compiler are untouched.
- **Memory:** the room grows only where a ReadWrite case becomes the largest. `probe-capacity-before.txt` vs
  `probe-capacity-after.txt`: every catalog keeps its numbers except the two fixture catalogs (1 → 2 sessions,
  4 → 8 bytes); for the package and its samples nothing changes.

## Tests

`Tests/Integration/NativeRuntime/ReadWriteBindingSessionTests.cs` (11), fixture shard
`aibt.tests.p11010.read-write` (`Tests/Editor/CodeGen/GenerationP11010/`, node `aibt.tests.rw-increment`:
reads an Int32 Tree key and writes it + 1 through one ReadWrite binding; it is the only case of its catalog):

- `CatalogCapacity_CoversAReadAndAWriteOfEveryReadWriteBinding`: in the fixture catalog, the fixture with the
  built-in leaves and the standard library catalog every case's read-then-write demand fits; the fixture
  catalogs give exactly 2 sessions and 8 bytes.
- `ProductionTreeHost_ReadsAndWritesOneKeyThroughOneReadWriteBinding` ("single" → 6, "three" increments in a
  memory-sequence → 8: each node reads the previous node's write).
- `ProductionTreeHost_EveryUpdateReadsThePreviousWrite_AndAWarmedUpdateAllocatesNothing`: reactive-sequence
  [increment, `aibt.stdlib.wait`] keeps the run open; updates 1–3 give 6, 7, 8; a fourth, measured update
  (`UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory`) allocates nothing and gives 9.
- `NativeTreeAgent_…_InEverySchedulingProfile` (7: interactive, background, forced Immediate, Budgeted,
  BatchedJobsSameFrame, PipelinedJobs, JobsAgentFrame): three agents, every completed run `Success` with
  `counter=8`, no `LastError`, no world failure. (A fact cannot seed the key: `NativeTreeAgent` refuses facts
  on keys a node writes.)

**Before** (`before.txt`): 2/11 — the capacity test (`fixture case 0 sessions`, expected ≤ 1, was 2), both
host tests and the hold test (`NativeLifetimeStateInvalid`), and five agent profiles ("The native host
stopped: NativeLifetimeStateInvalid.") fail; BatchedJobsSameFrame and PipelinedJobs pass (room by accident,
above). **After** (`after-class.txt`): 11/11.

## Existing bytes and neighbours

- **Native Guard bytes unchanged:** `probe-bytes-after.txt` (probe `probe-bytes.cs`, the P11-009 F9 probe
  restricted to `tree-only`) is identical to the `tree-only` section of `P11-009\F9\probe-bytes-after.txt`
  after stripping CR, including the full byte stream (`native-guard-bytes-compare.txt`): content hash
  `1546cb72…f826`, length 5451, semantic hash `e2afaafe…0946`. The change is not in the compiler, so no
  program's bytes can move.
- **Pins:** `AIBT.CodeGen.GenerationTests` 14/14 with `generated-compiled-v2.json` and
  `minimal-compiled-v1.golden.json` untouched (`after-neighbours.txt`).
- `after-neighbours-classes.txt`: `ReadWriteBindingSessionTests` 10/10 (before the hold test was added),
  `MixedScopeNativeTreeTests` 11/11 (seven profiles), `NativeLibraryAliasedKeyTests` 14/14 (its policies).
- `after-neighbours.txt` (whole assemblies, fix and final tests in place, the temporary probe still present):
  `AIBT.Integration.Tests` 466/466 (465 without the probe), `AIBT.CodeGen.GenerationTests` 14/14,
  `AIBT.NativeBurstDispatch.Tests` 79/79 (the generated-catalog dispatch canary). After the probe was removed:
  `after-final-class.txt`, `ReadWriteBindingSessionTests` 11/11.
- Console after the runs: no entry naming the fixture, `rw-increment` or `CapacityExceeded` among the last
  entries the script returns; the errors there are other tests' expected ones (forced-policy refusals, value
  read refusals, the throwing effect handler). Not compared against a baseline run.
- `after-attempt1.txt`: the first after-run, with a harness mistake of mine (a fact on the written key),
  kept for the record; the capacity fix was the same.

## The P11-009 fixture

Kept as two nodes. The P11-009 card does not ask for one node, and the split adds a check a single node
would lose: the Agent value written by `agent-bump` is read by `mixed-report` in the same update (41 → 41009).
Only its comment changed: it said a node opens at most as many sessions as the largest node has bindings; it
now records why the nodes stay apart and that the limit is gone.

## For a user

A native node may read and then write a key through one `ReadWrite` binding in one tick in any catalog;
before, it stopped the host (`NativeLifetimeStateInvalid`) when that node was the largest case of its catalog
set, outside BatchedJobsSameFrame and PipelinedJobs. Projects whose catalog set lists the package library are
unchanged in memory and behaviour. Workaround for an older package: add a node with more bindings to the same
catalog set (for example `StandardNativeShard`), or read and write through separate Read and Write handles.

## Not verified

- The ten assemblies were not run (as the brief says).
- **Not covered by the fix (from code, not probed):** a node that opens more sessions than "each binding once,
  each ReadWrite binding read and written once" — e.g. reads one Read binding twice, writes one Write binding
  twice, or reads a ReadWrite binding again after writing it — still needs room it may not have when it is
  the largest case. The bridge does not reuse a finished session. Covering it needs either session reuse in
  the bridge (a runtime semantics change) or a per-node access count from the generator (`CodeGen~`); neither
  is in this brief. Validation cannot see it today: the access count is in the node's method body, which the
  analyzer does not count.
- Group dispatch in `ProductionTreeScheduler` waves outside `NativeTreeAgent` (direct scheduler registration)
  was not run separately; it uses the same group workspace as the two jobs policies.
- No Player build.

## Acceptance run (orchestrator, 2026-11-23)

Read by the orchestrator on `0c2b541` (clean working tree): this file, the session's report, the
whole diff `1db104a..0c2b541` outside `Planning~/` except the new test file, and
`CreateWorkspaceCapacity` in full.

- **Scope.** One production file, `Runtime/Integration/GeneratedDispatch/GeneratedBurstCatalogV2.cs`
  (+7 -2): a `BlackboardReadWrite` binding counts two sessions and twice its value size. `git diff
  --stat 1db104a..0c2b541` is empty for `Tests/Editor/CodeGen/Contracts`, `Tests/Fixtures`,
  `CodeGen~`, `package.json` and `Documentation~/specifications`.
- **Raw files.** `fix.diff` equals the committed change. `probe-capacity-before-final.txt` lists
  `aibt.native.increment-int`, `increment-float` and `toggle-bool` as cases that need 2 sessions and
  fit only by the spare room of a larger case. `native-guard-bytes-compare.txt`: Native Guard has
  the bytes of P11-009 (`1546cb72...f826`, 5451 bytes).
- **Full run of ten assemblies** on `0c2b541`, 2026-11-23 22:23-22:44; Editor-log counters: did not move (unknown-command 0, main-thread-timeout 1 before and after). Raw output:
  `C:\UnityProjects\_aibt_acceptance\P11-010\acceptance\full-run-0c2b541.txt`.

| Assembly | `3d16480` | `0c2b541` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | - |
| `AIBT.Integration.Tests` | 454/454 | 465/465 | `ReadWriteBindingSessionTests` 11 |
| `AIBT.Editor.Tests` | 857/857 | 857/857 | - |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | - |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | - |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | - |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | - |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | - |
| `AIBT.CodeGen.GenerationTests` | 14/14 | 14/14 | - |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | - |

Not done by the orchestrator: the new test file was not read line by line; no probe was run again;
the "before" run (2/11 with the fix removed) was not repeated.

To know (reported to the owner):
- The limit is lifted for one read and one write per ReadWrite binding. A node that reads one
  binding twice, or reads again after a write, can still exceed the room when it is the largest
  case of its catalog set. The session read this from the code and did not probe it. A full fix
  needs session reuse in the bridge or an access count from the generator; both are protected
  areas.
- Validation cannot warn about such a node today: the number of accesses is in the node's method
  body.
