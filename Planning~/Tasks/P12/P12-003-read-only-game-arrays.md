# P12-003 — Read-only game arrays in nodes: not built, recipes documented

Status: `Review` — settled 2026-11-25: **not built**. The recipes that serve the consumer with 0.2.0 are
documented. The two tests the consumer asked for are written and not committed or run: the Modules Editor is
closed. Acceptance is the orchestrator's.

## Objective

Decide whether a Burst node may read a game-owned array (a grid, a zone map, a threat list) by an index, and
in what shape. Settled: not now. What 0.2.0 already offers is documented, with a test behind every recipe.

## Outcome

- **Not built.** No handle, context call, generator, analyzer or specification change. The node ABI stays
  as it is.
- **The consumer's answer of 2026-11-25** (`Planning~/Evidence/P12-003/answer-readonly-arrays-2026-11-25.md`):
  "0.2.0 is enough; do not build the array read now." They have no real trees yet, and their examples
  (walkability grid, zone map, threat list) were anticipated needs. The game builds senses in one batch per
  tick before behaviour. The other squad is assigned by the game, not chosen by a node. No planned node
  scans. One tick of latency is acceptable for rare lookups. Recipes 1-5 cover them. They will come back
  with a named node and its numbers if a real tree needs an array read.
- **Delivered:**
  - the section "Game data that is not per agent" in `Documentation~/simulation-container.md`;
  - the test of one rows array behind two channels;
  - the behaviour of a second read of one input in one callback, established, tested and documented
    (see "What a second read does" below).

## Decision record

- Orchestrator, 2026-11-24, under the owner's delegation of 2026-11-23:
  `C:\UnityProjects\_aibt_acceptance\SweetKingdom\plan-2026-11-24.md`, decision 2. Random access to a bound
  array needs a new handle and context call in the node ABI, the generator, the analyzer and a specification
  section, all protected. ADR-P10-008 rejected a raw-pointer read. So a design task came first, no code.
- Orchestrator, 2026-11-25: `Planning~/Evidence/P12-003/decision-readonly-arrays.md`, after the independent
  read `readonly-arrays-design-read.md`. Not built now; the recipes are documented; the shape and the
  condition below are recorded.
- The consumer's answer of 2026-11-25 confirmed it (above). The second independent read,
  `readonly-arrays-reopen-read.md`, agreed on "no ABI change today". The orchestrator did not take its
  proposal to prototype now: the consumer has no node to measure for. Its corrections were taken: the
  recipes say that the refusal of a step needs collection safety checks; the limit "a node reads its own
  squad's row only" was too narrow; and the four code sites below.
- **No ADR.** The decision file says "an ADR records the considered designs". The planning system records
  a decision that changes no architecture as a decision record in the card's evidence, not as an ADR:
  P12-002 kept its decision in `Planning~/Evidence/P12-002/decision.md`, with "an amendment to ADR-P10-008
  comes with the code". `Planning~/DECISION_BOUNDARIES.md` says a separate accepted decision converts
  evidence into architecture; nothing is converted here. This card and the copied decision file are the
  record. If the read is built, its ADR comes with the build. No specification is amended.

## The shape to build if this is reopened: design E1

From `readonly-arrays-design-read.md` §2 E and the decision:

- a new read-only handle in the node configuration (working names: `[AibtArray("id")]`, an array read
  handle) with `TryReadElement(index, out T)` and `TryGetLength`;
- the game binds a `NativeArray<T>` at container level; it is copied once per step at entry, and the step is
  refused while a game job still writes it (`InputMissing`, collection safety checks on);
- every element read gets the canonical check an input row gets;
- an index out of range faults the agent with its own result code;
- no wake on change, no trace record per read;
- added to ABI v2 without a version bump, so catalogs and compiled programs that do not use it keep their
  bytes. The addition itself then stays forever.

Rejected for good:

- a raw pointer, `ref readonly T` or `NativeArray` in node code (ADR-P10-008, alternative 4);
- E2, reading game memory during the step without a copy (it goes around the job system's dependency
  tracking to save a copy of a few microseconds).

## The condition for reopening

The consumer names a node whose index is chosen in the think (from tree state the game cannot know before
the step), or a node that must scan several elements in one think, and one tick of latency through an
external action does not serve it. Per-agent sampling, an indexed channel and a second channel do not either.

## If reopened: the plan

1. The prototype of `readonly-arrays-design-read.md` §5:
   - a test-only harness in the container (no public API, no generator change);
   - variants: A (the game samples per agent into a row, then `Step`) against E1, and E2 only to rule it out;
   - workloads: W1 "is the cell ahead walkable" (3 cells of a 256×256 byte grid per think), W2 "nearest of
     up to 32 threats" (a scan), W3 an array-size sweep (64 KB, 1 MB, 4 MB);
   - numbers: `Step` median and p99 per variant and path, the copy time against size, ns per element read,
     game job plus `Step` for A, GC allocation per step, identical command digests in place and in jobs, the
     safety cases as tests, one Player build with checks off;
   - rule-outs as written there (E1 is out as the default if a realistic copy is above 5% of a jobs step;
     the feature is unjustified if A's total is within noise of E1 for W1 and W2);
   - the owner's measurement rules for this machine (interleaved pairs, repeats, load logs).
2. Then the decision, then an ADR with the shape, then the build.

### Code sites a build would touch that the first read missed

Found by `readonly-arrays-reopen-read.md` §7, item 7; each re-checked against the code on 2026-11-25:

- `Tests/Editor/CodeGen/Contracts/PublicBurstAbiContractTests.cs:28-72`: the public surface test hard-codes
  357 lines (`:36`) and rebuilds v2 from v1 plus the three past additions. A protected file.
- `CodeGen~/AIBT.CodeGen/BurstNodeUsageAnalyzer.cs:581-592`: `BindingAttribute` returns `null` for an unknown
  `BurstAccess` operation, and its callers then skip the binding check. A new `TryReadElement` would escape
  AIBT5006 and the phase check unless the analyzer learns it.
- `CodeGen~/AIBT.CodeGen/GeneratedMetadataEmitter.cs:134-135`: the final `else` of `ReadBindings` classifies
  any other binding attribute as kind 6 (Completion). A new binding that forgets its own branch is
  misclassified silently.
- `Runtime/Execution/Burst/Dispatch/NativeBurstDispatchBindingValidationV2.cs:432`: the catalog validation
  refuses any binding kind above Completion (6). Related, also from the reopen read:
  `Runtime/Simulation/SimulationProgramMapV1.cs:127-221` ignores unknown kinds, so a new kind would bind
  nothing unless it gets a case.

The rest of what a build touches (handle and binding recognition in the generator, `IsBinding` in the
emitter, the runtime bridge, the backing, the specification sections) is in the reopen read's §4.

## What a second read does (the consumer's second request)

Established from the code before the test was written:

- Every `TryReadSnapshot` call opens a new value session
  (`Runtime/Nodes/Contracts/BurstBindingBridgeCoreV2.cs:391-432` → `TryAllocateSession`, `:1807`, whose
  capacity check is `:1826-1832`).
  Sessions are reset only at the start of a callback's transaction (`:1106`, `:1278`, `:1291`); a completed
  read is not reused.
- The catalog sizes one callback's sessions and staging bytes for its largest node
  (`Runtime/Integration/GeneratedDispatch/GeneratedBurstCatalogV2.cs:191-239`): one session per binding, two
  for a `ReadWrite` binding and for an external action. The container's workspace (in place and in the
  agent-frame job) uses that capacity (`NativeBurstDispatchWorkspaceOwnerV2.cs:240-254`,
  `GeneratedTreeDispatchAdapterV2.cs:1267`).
- So a second read of one input in one callback succeeds, with the same row, when another node of the
  catalog leaves room. Otherwise it returns `CapacityExceeded`. `Latch` records it as the frame's first
  failure (`BurstBindingBridgeCoreV2.cs:1673-1692`), the callback is rejected, its commands are not published
  (`AgentFrameCoreV1.cs:377-395`, `GeneratedTreeDispatchAdapterV2.cs:1413-1419`), and the agent faults with
  `AgentFault.Cause` = `CapacityExceeded`. The step and other agents go on.
- Two fields of one node with the same input id: refused at compile time. `[AibtInput]` normalizes to a
  snapshot binding (`CodeGen~/AIBT.CodeGen/ShortFormNormalization.cs:108-116`), and two bindings of one node
  with one id are AIBT5010 (`CodeGen~/AIBT.CodeGen/BurstNodeGenerator.cs:293-311`).
- This is the rule P11-014 (item 7) established and documented for blackboard bindings on a host
  (`Documentation~/burst-node-authoring.md`, "Read a binding once per callback";
  `MixedScopeNativeTreeTests.ValueSessionLimit_*`), now for inputs in the container. A refusal by design,
  not a new defect. Its weak point, already recorded by P11-014: whether a second read works depends on the
  other nodes of the catalog, so adding a node can hide or reveal the fault. Not changed here. A fix would
  touch `TryAllocateSession` (reuse a completed read session of the same binding in one callback) or the
  catalog sizing in `GeneratedBurstCatalogV2.CreateWorkspaceCapacity`. Either is a change of execution
  semantics and needs a decision first.

## Changes

Planning and documentation (committed):

- `Documentation~/simulation-container.md`:
  - the section "Game data that is not per agent" (five recipes, each with its test, the collection safety
    checks caveat, and what the recipes cannot do);
  - the bullet "Read each input once per callback" in "Inputs and commands";
  - the corrected limit in "Group data: an indexed input channel".
- This card, `Planning~/work-items.json`, `Planning~/Evidence/P12-003/`.

Tests (written, not committed until the Editor is back):

- `Tests/Editor/CodeGen/GenerationP12003/`: a new fixture assembly `AIBT.CodeGen.P12003SquadRows.Tests` with
  the shard `aibt.tests.p12003.squad-rows`, which holds three nodes:
  - `compare-squads`: inputs "own" and "target" of one row type, one command;
  - `read-own-twice`;
  - `read-own-thrice`.
- `Tests/Integration/AIBT.Integration.Tests.asmdef`: the reference to the new assembly.
- `Tests/Integration/Simulation/TreeSimulationSquadRowsTests.cs` (`SquadRowsCatalog`, that shard only):
  - `OneRowsArray_TwoChannels_EachAgentReadsItsOwnAndItsTargetRow` (in place and jobs);
  - `TwoChannels_LogIsTheSameInPlaceAndInJobs` (30 agents, 40 ticks, rows and targets changing, wake on);
  - `WakeOnChange_OnOneChannel_WakesOnlyTheReadersThroughThatChannel`;
  - `RebindingOneChannel_LeavesTheOtherAsItWas`;
  - `OutOfRangeIndexInTheSecondChannel_RefusesTheStep_NothingRuns`;
  - `ReadingOneInputTwice_WithRoomInTheCatalog_ReadsTheSameRowTwice`;
  - `ReadingBeyondTheCatalogsValueSessions_FaultsTheAgentWithCapacityExceeded`.
- `Tools~/Verification/P2/CodeGen/Verifier/Program.cs`: the case `ShortForm.SameInputTwice` (two
  `[AibtInput]` fields with one id are refused with AIBT5010 and leave the shard unusable).
  - Run on 2026-11-25 outside the package: the generator and verifier sources were copied to a scratch
    folder, and only this case was run with the dotnet SDK of 6000.6.9f1. No Editor was started. It
    passed, and with two different ids it failed as it should.
  - The full matrix fails at baseline, before this case. Its contract compilation does not include the
    agent-frame sources, so a valid catalog does not compile (`CS0234: AIBT.AgentFrame`), and the run
    stops at `VerifyValidAndDeterministicGeneration`. Not fixed here. `Build-And-Verify.ps1` also starts a
    Unity Editor, so it was not run.

## Acceptance

- After the Editor is back: the new assembly compiles, and `TreeSimulationSquadRowsTests` passes (13 cases),
  with no new Console errors. `TreeSimulationIndexedInputTests` and `TreeSimulationIoTests` still pass.
- Every test named in the documentation section exists under that name.
- `Tools~/Verification/Verify-Static.ps1` passes.

## Forbidden changes

`CodeGen~`, `ExpectedPublicAbiV2.txt`, the compiled program format, `Documentation~/specifications`,
`package.json`, `Tests/Fixtures`, existing public signatures, execution semantics. Reopening the read needs
the condition above and then an ADR before any of these change.

## Evidence

`Planning~/Evidence/P12-003/README.md` and the five records next to it.
