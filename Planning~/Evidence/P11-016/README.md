# P11-016 evidence — memory per agent and build weight

Card: `Planning~/Tasks/P11/P11-016-memory-and-build-weight.md`. Brief:
`C:\UnityProjects\_aibt_acceptance\P11-016\brief.md`. Raw files are under `C:\UnityProjects\_aibt_f14\size\`
(written `size\` below). This file covers stages 0 and 1; stages 2 and 3 have not started.

Conditions of every Player number:

- **Build.** Unity 6000.6.9f1, Windows x64, IL2CPP. The Player is a release build of the P11-015 memory
  project D at Minimal stripping, run in batch mode.
- **Package.** Installed from a `git archive` tarball of the named commit, made by
  `size\scripts\Invoke-Stage1Memory.ps1` into `C:\UnityProjects\_aibt_f14\p16\<commit>\`.
- **Deadline.** Every Player ran with `-sizeDeadline 240`, a thread that ends the process with exit code 97.
  It never fired; every run exited with 0; nothing was stopped.
- **Workers.** Every Player ran with `-job-worker-count 1`. Stage 0a showed that with more workers Unity's
  job-worker allocator commits a block that varies between identical runs.
- **What a number is.**
  - "Native" is the change of `Profiler.GetTotalAllocatedMemoryLong`, "managed" the change of
    `GetMonoUsedSizeLong`, and "private" the change of the process's private bytes.
  - All are divided by N and taken after `GC.Collect`, between "before" and "after N agents have each run
    at least one update".
- **Repeats.** Two passes of every case. Native agrees to the byte in every row; managed and private agree
  within about 1%.
- **Timing.** The machine is loaded, so no timing claim is made anywhere.

## Stage 0 — measurements (accepted)

The full report is `size\P11-016-stage0-report.md`; it is also in the shared notes folder.

- **0a.** The one-time block of about 74 MB with `NativeTreeAgent` is Unity's job-worker allocator state:
  - a plain `IJobFor` with no AIBT code commits the same block;
  - it scales with the worker count and varies between identical runs;
  - it is committed inside reserved memory and never becomes resident.

  AIBT's own cost in that run is 7.3 MB for 100 First Agents. The documentation paragraph goes into the
  rewrite of "Size and memory" (not yet done).
- **0b.** A true native-only Player (`N0`) still ships `AIBT.Agents`, `AIBT.Authoring`, `Newtonsoft.Json`,
  `System.Xml` and `System.Data`.
- **0c.**
  - Without the `AIBT.Agents` link.xml rows, `AIBT.Agents` and `AIBT.Authoring` leave the native-only
    Player, at Minimal and at High. Newtonsoft stays, because `AIBT.Runtime` references it.
  - With the rows written `ignoreIfUnreferenced="1"`, a scene-only managed game runs at High with a PASS
    verdict, and the native-only Player is as small as without the rows.
  - The orchestrator chose that design for stage 2.

## Stage 1 — memory per agent

The probe's kinds:

| Kind | What each agent is | Tree |
| --- | --- | --- |
| `hosts` | a `ProductionTreeHost` bootstrapped from one shared `ProductionTreeProgram` | First Agent: 1 node, 1 leaf |
| `hosts100` | the same | size-native-100: 109 nodes, 75 generated leaves, 34 composites |
| `first` | a `NativeTreeAgent` in the shared world, run by the scheduler | First Agent |
| `native100` | the same | size-native-100 |

- The `hosts` kinds never run the `JobsAgentFrame` job: 0 batches.
- The `NativeTreeAgent` kinds do: 31 batches over 31 frames, and every agent updates.
- Catalogs: First Agent's own for `first` and `hosts`; the Native Guard sample's for the 100-node kinds.

The builds:

| Build | Commit | What it has | Player shipped B |
| --- | --- | --- | --- |
| `s1base` | `32c8bfa` | before stage 1 | 93,611,530 |
| `s1i1` | `51dfaed` | item 1 | 93,616,110 |
| `s1i2` | `a02d2f4` | item 2 (amendment `f30a80a`) | 93,620,194 |
| `s1i3` | `6afd082` | item 3, the table | 93,621,034 |
| `s1i3b` | `72db77b` | item 3, the table trimmed | 93,621,034 |

### Native bytes per agent (pass 1; pass 2 equal to the byte)

| Kind, N | before | item 1 | item 2 | item 3 (`6afd082`) | item 3 trimmed (`72db77b`) |
| --- | --- | --- | --- | --- | --- |
| `hosts`, 100 | 55,050 | 53,740 | 7,864 | 7,864 | 7,864 |
| `hosts`, 1000 | 51,642 | 51,249 | 3,211 | 3,211 | 3,211 |
| `hosts100`, 100 | 838,861 | 169,738 | 123,863 | 123,863 | 123,863 |
| `hosts100`, 1000 | 838,795 | 168,296 | 119,603 | 119,603 | 119,603 |
| `first`, 100 | 73,400 | 72,745 | 25,559 | 25,559 | 24,904 |
| `first`, 1000 | 59,834 | 59,376 | 11,272 | 11,207 | 11,272 |
| `native100`, 100 | 945,029 | 275,251 | 228,721 | 234,619 | 187,433 |
| `native100`, 1000 | 963,838 | 293,274 | 245,170 | 220,332 | 176,292 |

Over stage 1, native per agent at N = 1000:

| Kind | before | after | change |
| --- | --- | --- | --- |
| `hosts` | 51,642 | 3,211 | −94% |
| `hosts100` | 838,795 | 119,603 | −86% |
| `first` | 59,834 | 11,272 | −81% |
| `native100` | 963,838 | 176,292 | −82% |

### Managed bytes per agent, N = 1000 (pass 1)

| Kind | before | item 1 | item 2 | item 3 | item 3 trimmed |
| --- | --- | --- | --- | --- | --- |
| `hosts` | 3,846 | 3,858 | 3,355 | 3,793 | 3,797 |
| `hosts100` | 143,602 | 143,569 | 143,380 | 143,647 | 143,483 |
| `first` | 6,398 | 6,287 | 5,726 | 6,234 | 6,259 |
| `native100` | 143,553 | 143,745 | 142,950 | 143,512 | 143,778 |

Stage 1 leaves the managed memory of the 75-leaf kinds where it was (within 0.5 %). For the one-leaf kinds
the table shows a move that is larger than the passes' spread: `hosts` 3,858 -> 3,355 with item 2 and back to
3,793 with item 3 (`first` 6,287 -> 5,726 -> 6,234). A reading, not measured: item 2 removes the trace
owner and recorder objects, item 3 adds the shared struct and the map handle to every adapter.

### Private bytes per agent, N = 1000 (pass 1)

These include allocator overhead that the Profiler counter does not:

| Kind | before | item 1 | item 2 | item 3 | item 3 trimmed |
| --- | --- | --- | --- | --- | --- |
| `hosts` | 56,873 | 56,484 | 8,094 | 8,081 | 8,344 |
| `hosts100` | 990,933 | 324,342 | 275,542 | 275,546 | 275,546 |
| `first` | 72,061 | 71,291 | 22,069 | 21,651 | 22,200 |
| `native100` | 1,121,661 | 449,700 | 401,449 | 376,615 | 349,278 |

Raw files:

- **Runs:** `size\raw\stage1-<build>-runs.txt`.
- **Tables:** `size\raw\stage1-memory-<build>.txt`, made by `size\tools\memory_table16.py`. They also hold
  the working set and the agent-frame counters.
- **Per run:** `size\run16\D-Minimal-<build>-memory-<kind>-<N>-w1-p<pass>\{Player.log,size-lines.txt,deadline.txt}`.
- **Players:** `size\build16\D-Minimal-<build>\`.
- **Leaf reports:** `size\run16\leaf-report-s1i2\` and `leaf-report-s1i3\` (`leaf-report.txt`, `editor.log`),
  and `size\raw\stage1-leaf-report-s1i3-run.txt`.

### Item 1 — leaf workspaces borrow the catalog's metadata (`51dfaed`)

- **What changed.**
  - Every generated leaf's dispatch workspace held private copies of the catalog's eight metadata arrays:
    cases, configuration fields, memory fields, bindings, value fields, case and binding canonical
    ranges, and canonical rules.
  - With the Native Guard catalog that is 8,880 B per leaf (leaf report), the same for every leaf.
  - The tree adapter now creates the workspace through `NativeBurstDispatchWorkspaceOwnerV2.TryCreateBorrowing`,
    with the arrays the catalog lends (`GeneratedBurstCatalogV2.TryLendWorkspaceMetadata`). The workspace
    never disposes borrowed arrays.
  - The adapter already retained the catalog, so the catalog refuses disposal while a borrowing host lives.
  - The copying `TryCreate` is unchanged for every other caller (tests, the generic node dispatch runner,
    the catalog's validator), and so are the group workspaces.
- **Measured.**
  - `hosts100` −670,499 B per agent, `native100` −670,564 B: 75 leaves × 8,880 B = 666,000 B, plus
    allocator overhead.
  - The one-leaf kinds −393 to −458 B.
  - Managed is unchanged.
- **Differs from the brief.** `GeneratedBurstCatalogV2.TryCreate` already refuses every allocator but
  Persistent, so "a host from a non-persistent catalog" cannot be built. The copying fallback is tested
  through an internal seam, `DisableWorkspaceMetadataLendingForTests`, which makes a catalog refuse to lend.
- **Tests** (Modules Editor). Native leak detection with stack traces is on in the new class, and no leak
  line appeared in the Console.
  - `BorrowedCatalogMetadataTests` 4/4 (new):
    - every leaf reads the catalog's own arrays;
    - the catalog refuses disposal while a host lives and is usable after it;
    - a catalog that does not lend gives copying workspaces that run the same;
    - a shape the metadata does not match is refused.
  - NativeRuntime integration namespace 446/446.
  - `AIBT.Tests.Runtime.NativeExecution.Dispatch` 79/79.
  - `GenericNativeDispatchTranslatorV1Tests` 4/4.
- **Hot path.** The borrowing path still validates the shape for each leaf at creation. Nothing changes per
  update.

### Item 2 — no trace channel by default in a Player (`f30a80a` ADR amendment, `a02d2f4` change)

- **Decision** (ADR-P7-010, amendment 2026-11-24):
  - in the Editor the default stays 256 records and 512 emissions;
  - in any Player build a host has no channel unless the caller passes a capacity;
  - `NativeTraceChannelCapacityV1.None` (new, equal to `default`) means no channel everywhere;
  - `NativeTreeAgent` gets **Trace** (Auto / Off / On) and **Trace Records** (default 256; On gives
    records × 2 emissions). Auto is on in the Editor and off in a Player, and scenes saved earlier read as
    Auto;
  - the default is the pure function `NativeTraceDefaultsV1.Resolve(requested, isEditor)`, and the agent's
    choice is the pure function `NativeTreeAgent.ResolveTraceCapacity(mode, records, isEditor)`.
- **Documents updated.**
  - `production-host.md`: the owner sentence and the capacity paragraph.
  - `consumer-quickstart.md`: the trace capacity sentence and Attach Selected Host Trace.
  - `editor-and-layout.md`: the raw-host trace path.
  - `ai-and-mcp.md`: `read_trace` answers `available: false`.
  - `README.md`: the AI tools paragraph.
  - The generated runtime API reference: regenerated through the menu, additions only.
- **The specification.**
  - `specifications/native-runtime-v1.md:132` states a normative rule: a requested trace level with zero
    capacity fails preflight with AIBT4310. It does not state the default.
  - The rule still holds. A host with `None` requests no trace level, and a channel created at a level with
    zero capacity still fails.
  - One clarifying sentence was added after the rule, and the amendment records it. No stop was needed.
- **Measured.**
  - At N = 1000, −48,038 B per agent for `hosts` and −48,104 B for `first`: the channel's 43 KB plus its
    allocation overhead and the recorder.
  - The 100-node kinds lose about the same, since there is one channel per host whatever the tree.
- **Consumer change.**
  - A Player host bootstrapped without a capacity has a null `TraceChannelOwner`.
  - Code that passes a capacity, like the Swarm Arena sample and the benchmarks, is not affected.
  - The Editor, MCP and saved assets are unchanged.
  - An all-zero capacity used to fail bootstrap. It now bootstraps without a channel; the amendment records
    this.
- **Tests.**
  - `NativeTraceDefaultTests` 6/6 (new):
    - both branches of the pure function;
    - `None`, and an explicit capacity;
    - a bootstrap with `None` runs without a channel;
    - a bootstrap with a capacity owns one.
  - `NativeTreeAgentTests` 20/20, 4 of them new:
    - Auto, Off and On for the Editor and for a Player;
    - an agent saved before the field reads as Auto;
    - Auto in the Editor owns today's channel;
    - Off runs without a channel, and On takes the record capacity.
  - `McpRuntimeNativeAgentTests` 3/3; the new one checks that `read_trace` of an agent set to Off answers
    `available: false`.
  - `ProductionTreeHostTests` 20/20 and `AgentConsumerToolsTests` 14/14.
  - `JobsPolicyEquivalenceTests` 28/28; its `CaptureTrace` now asserts a channel.
  - `BurstExecutionPathTests` 3/3 and `McpDocumentationGeneratorsTests` 14/14.
- **Open findings** (outside this task; recorded in the amendment and in `known-limitations.md`):
  - the recorder keeps taking a lease and appending every update after its channel is faulted;
  - there is no public way to attach a trace channel to a live host.

### Item 3 — leaf-only agent-frame slot table (`6afd082`, `72db77b`)

- **What changed.**
  - `AgentFrameAgentV1` (internal) now holds three things:
    - `Slots`: one `AgentFrameSlotV1` per generated leaf, in node order;
    - `SlotOfNode`: −1 for a node without a slot;
    - `Catalog`: a `NativeBurstDispatchWorkspaceSharedArraysV1` with the handshake, the two flags and the
      eight metadata handles. Every leaf's workspace comes from the catalog's one shape, and with item 1
      they are the catalog's own arrays.
  - `NativeBurstDispatchWorkspaceArraysV1` keeps the per-slot part, `Capacity` included.
  - `AgentFrameSlotV1.Present` is gone.
  - The job looks a node up through the map. The two results for a dispatch without a slot are unchanged:
    - node beyond the map: `InvalidHandle`, or `CapacityExceeded` with an effect queue;
    - map entry −1: `TypeMismatch`, or `CapacityExceeded`.
  - `AgentFrameJobDataV1` (public) and the generator are not touched.
- **Second commit.** The first measurement found that `UnsafeList` rounds its capacity up to a power of two.
  The per-node table of 109 nodes and the leaf-only table of 75 leaves both held 128 slots (checked in the
  Editor: capacity 128 for 75 and for 109). `72db77b` trims the table and the map to their length, test
  first: the new assertion failed with capacity 8 for 5 leaves.
- **Measured**, `native100` per agent:
  - N = 1000: 245,170 → 220,332 → 176,292 B (−68,878 B over both commits);
  - N = 100: 228,721 → 234,619 → 187,433 B (−41,288 B).
  - The other kinds are unchanged within 655 B.
- **Slot size.** The Editor's leaf report gives `AgentFrameSlotV1` as 2,056 B before and 1,488 B after; the
  Editor's containers carry safety handles. The shared struct is 560 B in the Editor and exists once per
  agent. The Player sizes were not read directly.
- **Facts that differ.** The first commit raised `native100` at N = 100 by 5,898 B per agent, while N = 1000
  fell by 24,838 B; both passes repeat to the byte. The power-of-two capacity explains why the first commit
  saved little. Why N = 100 rose is not established; Unity's allocator granularity is the suspect, not
  verified.
- **Tests** (written first and run on the old layout, where all four failure cases passed, then ported to
  the new layout). `AgentFrameSlotTableTests` 5/5:
  - the four results for a dispatch the table has no slot for (beyond the table / without a slot; without
    and with an effect queue), each `CallbackRejected` at the callback site with no body run: `InvalidHandle`,
    `TypeMismatch`, `CapacityExceeded`, `CapacityExceeded`;
  - one slot per leaf, every leaf mapped to its own slot, the catalog fields the catalog's own arrays, and
    capacity equal to length.

  Also green:
  - NativeRuntime integration namespace 455/455 (on `6afd082`);
  - Simulation integration namespace 112/112;
  - `AIBT.Tests.Runtime.NativeExecution.Dispatch` 79/79.

  After `72db77b`:
  - `AgentFrameSlotTableTests` 5/5, `BurstExecutionPathTests` 3/3, `JobsPolicyEquivalenceTests` 28/28;
  - `NativeTreeAgentTests` 20/20, `BorrowedCatalogMetadataTests` 4/4.

  The Console showed no leak or exception line.
- **Hot path.** One more read per dispatch (the map entry). The catalog fields are read from the agent
  instead of the slot, the same number of reads. No timing claim.

## What is left per leaf

Measured on `72db77b` with the Native Guard catalog: Player Minimal, N = 1000, size-native-100 (75 leaves).

- **Per generated leaf, host side.**
  - Native: 1,573 B, from (`hosts100` − `hosts`) / 74 = (119,603 − 3,211) / 74.
  - Managed: 1,888 B, from (143,483 − 3,797) / 74.
- **Per generated leaf, agent-frame side** (`NativeTreeAgent` on the job path): about 657 B native, from
  ((`native100` − `hosts100`) − (`first` − `hosts`)) / 74 = (56,689 − 8,061) / 74. It holds:
  - the slot struct;
  - two more allocations (`Resolutions`, `EffectDescriptors`);
  - the map entry.
- **A composite node** costs 4 B (its map entry).
- **Share of the agent.** For `native100` the leaves are about 95% of the 176,292 B per agent: 75 × (1,573 +
  657) = 167 KB.

What the 1,573 B host-side native is (leaf report on `6afd082`, `size\raw\stage1-leaf-report-s1i3-run.txt`;
item 3 does not change the host side):

- **Allocation count.**
  - Each leaf makes 25 native allocations: 15 by its dispatch slot and 10 by its workspace.
  - Their payload is only 701 B on average (681–725). The other ~870 B per leaf is allocator overhead,
    about 35 B per allocation.
  - With this catalog, three allocations are always zero-length (completions, completion payload,
    operations), and up to six more are zero-length for leaves without memory, random stream,
    configuration or bindings.
  - The smallest arrays are padded:
    - each `NativeList` claim holds one value in 64 B (minimum capacity is a cache line);
    - the transaction control is 64 B.
- **Scratch sized by catalog-wide maxima.**
  - 425 B of the 701 B (61%) is sized by the catalog's largest case, not by the leaf.
  - The maxima here: memory 16 B; 3 value sessions; 28 B of staging; 1 command of 4 B; 0 operations.
  - The arrays: value sessions 168 B, request 80 B, command and published command 80 B, value staging and
    marks 56 B, memory staging and written 32 B, payloads 8 B, request status 1 B.
  - It grows with the catalog, not the tree. For example, a node with 1 KB of memory anywhere in the
    catalog would give every leaf of every tree 2 KB of staging.
  - First Agent's catalog: 297 of 597 B.
- **One workspace per leaf.**
  - 10 of the 25 allocations and 513 of the 701 B are the leaf's own workspace: control, two claims,
    request, memory staging and written, request status, value sessions, value staging and marks.
  - A workspace serves one request at a time, and one agent dispatches its leaves one after another.
    Sharing one workspace per agent (or per thread) would remove these allocations from every leaf.
  - Not checked: what in the workspace must survive between a leaf's requests (control generation, ledger
    binding).
- **Managed bytes per leaf: 1,888 B.** Per leaf, the adapter's `DispatchSlot` object and the workspace
  owner object (each with a field for every native array) and the managed `_resolutions` array.
  `hosts100`'s 143 KB per agent is almost all of this; the managed host without leaves is about 3.8 KB.

Not implemented, as the brief asks: the orchestrator decides on a second round from these numbers.

## Not verified

- The orchestrator's full run of the ten assemblies; only the touched tests ran here.
- A Player with a `NativeTreeAgent` set to On, or a host given an explicit capacity. The Player's default
  (no channel) shows in the memory drop, not in an assertion on `TraceChannelOwner`. The Editor tests
  cover both branches of the pure functions.
- A development build: not measured.
- Simulation-container agents (`TreeGroup`): their memory was not measured at any stage.
- The Player size of `AgentFrameSlotV1` and of the shared struct; only the Editor sizes were read.
- Why item 3's first commit raised `native100` at N = 100.
- Any timing.

## Deviations

- **Item 1.** The fallback for a non-persistent catalog is tested through an internal seam, because such a
  catalog cannot be created (see item 1).
- **Item 2.**
  - One clarifying sentence in `specifications/native-runtime-v1.md` (see item 2). The normative rule did
    not change, so there was no stop.
  - An all-zero capacity no longer fails bootstrap; it gives no channel. The amendment records it.
  - The MCP test is the one the decision read suggested; it is added.
  - The generated API reference was regenerated through the menu. The menu writes into the host project's
    `Assets/AIBT-Documentation`; the runtime file was copied into `Documentation~/generated/`, and the folder
    the menu created was removed again.
- **Item 3.** It has two commits. The trim was found by the first measurement; it completes the leaf-only
  table and was made test first.
- **Known limitations.** Two lines added: the trace channel keeps the first records only, and every
  generated leaf costs its own allocations.

## Stage 1 acceptance (orchestrator, 2026-11-24)

Stage 1 (`51dfaed..72db77b`, records `2f1c0d7`) is accepted. The item stays `in_progress`: stages 2 and 3
and a second round of memory follow.

- **Read by the orchestrator.** The session's report; the whole diff `32c8bfa..2f1c0d7` under `Runtime/`;
  the ADR-P7-010 amendment; the changed lines of the existing tests; this file.
- **Protected paths.** `git diff --name-status 32c8bfa..2f1c0d7` is empty for
  `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, `CodeGen~`, `package.json` and `Tests/Fixtures`.
  Under `Documentation~/specifications` one file, `native-runtime-v1.md`: one sentence added after the
  `AIBT4310` rule, covered by the amendment. The independent review finds the sentence wider than the code
  (see below).
- **Numbers.** The N = 1000 rows of the native, managed and private tables were recomputed from the raw
  `SIZE|MEM` and `SIZE|MEMORY` lines of the `s1base` and `s1i3b` runs: they match.
- **Job time, checked because the raw lines suggested a regression.** The stage's own runs show the
  agent-frame job counter of the last build (`s1i3b`) about 50 % above the build before it, in both passes
  (`native100`, N = 1000: 201 and 195 ms against 133 and 134 ms over 31 batches). Those runs were not
  interleaved. An interleaved comparison of the same three Players (4 rounds, rotating order, one worker):
  `s1i2` 133.6, 115.6, 117.7, 120.9 ms; `s1i3` 129.9, 115.6, 120.5, 118.2 ms; `s1i3b` 116.7, 119.0, 116.7,
  119.2 ms. No difference between the builds; the earlier figure was machine load. Raw:
  `C:\UnityProjects\_aibt_acceptance\P11-016\acceptance\ab-jobtime-stage1.txt`. This is not a timing claim beyond "no regression seen".
- **Independent review** of `32c8bfa..2f1c0d7` (a reader given the code and neutral questions): no blocker.
  Checked and found correct: the lifetime of the borrowed arrays on every dispose path, the absence of
  writers and of job-safety conflicts (all eight arrays are `[ReadOnly]` in the backing), every caller of
  the changed shapes, the equivalence of the slot-table control flow, null checks on every use of the trace
  channel, additions only on the public surface. Four should-fix findings, all in documents and records,
  given back to the session: the `ProductionTreeHost.TraceChannelOwner` summary still says "available until
  destruction"; the specification sentence says "by default in a Player" although only
  `ProductionTreeProgram` and `NativeTreeAgent` choose that default; "stage 1 does not change managed
  memory" against this file's own table for `hosts`; three different per-leaf figures for item 1. Notes
  (thin spots of the new tests, the unbounded `TraceRecordCapacity`, the trace menu's message for a host
  with Trace Off, and others) are in the full text: `C:\UnityProjects\_aibt_acceptance\P11-016\acceptance\independent-review-stage1.md`.
- **Full run of ten assemblies on `2f1c0d7`** (20:33-20:52, Editor started at 17:02), all green. Editor-log
  counters did not move during it (unknown-command 0, main-thread-timeout 13 before and after). Raw:
  `C:\UnityProjects\_aibt_acceptance\P11-016\acceptance\full-run-2f1c0d7.txt`.

| Assembly | `3862964` | `2f1c0d7` |
|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 821/821 |
| `AIBT.Integration.Tests` | 588/588 | 601/601 |
| `AIBT.Editor.Tests` | 892/892 | 893/893 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 |
| `AIBT.CodeGen.GenerationTests` | 14/14 | 14/14 |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 |

- **Decision on a second round of memory** (orchestrator, under the owner's delegation, after an independent
  options read: `C:\UnityProjects\_aibt_acceptance\P11-016\second-round-read.md`; the decision with its reasons: `C:\UnityProjects\_aibt_acceptance\P11-016\decision-second-round.md`).
  Done next, after stage 2: one dispatch workspace per adapter instead of one per leaf, with the state that
  must survive between a leaf's requests kept per leaf, tests first for what today's tests would not catch,
  and a prototype measurement as a gate. Before it, the measurements this file lacks: simulation-container
  agents, and a same-catalog pair of trees that separates per-node from per-leaf cost. Not now: packing a
  leaf's arrays into fewer allocations (decided again from the numbers after the first change). Not done:
  sizing scratch by the case (it reverses a pinned behaviour for 7 % or less).

Not done by the orchestrator: the new test files were read by the independent reviewer, not by the
orchestrator; no Player was run with a trace channel on; nothing was measured in a development build.

## Stage 1 follow-up (`26c803e..cbe4a3f`, 2026-11-24)

The findings of the independent review of stage 1, given back by the orchestrator as A1-A7. This section and
the three after it were written by the orchestrator from the session's report, its hand-over note and the raw
files: the session stopped at its usage limit before writing records.

- **A1** (`26c803e`). The public summary of `ProductionTreeHost.TraceChannelOwner` says when it is null.
- **A2** (`ec9bd3f`). The sentence added to `specifications/native-runtime-v1.md` names the hosts that have
  no channel by default (`ProductionTreeProgram.TryBootstrap` without a capacity and `NativeTreeAgent` on
  Auto, in a Player) instead of "by default in a Player". Same amendment of ADR-P7-010.
- **A3.** The managed-memory sentence and the missing "not measured" line, corrected above (orchestrator).
- **A4** (`0aadd7e`). One measured per-leaf figure for item 1, with its catalog: 8,880 B with the Native
  Guard sample catalog, 376 B with First Agent's.
- **A5** (`d6f6d66`), tests only. `BorrowedCatalogMetadataTests` 4/4: identity of all eight arrays; a
  mismatch from another catalog and from a cases set one short. `JobsPolicyEquivalenceTests`: a new test
  runs two hosts of one catalog in one parallel agent-frame job with the job debugger on.
- **A6** (`d1b6d1a`), behaviour change, test first. The per-agent agent-frame trace list is created only
  while a recorder records. Native bytes per agent at N = 1000 (Player `D-Minimals1f`, two passes equal to
  the byte; checked by the orchestrator in `C:\UnityProjects\_aibt_f14\size\run16\stage1-followup-a6-table.txt`):

  | Kind | `72db77b` | `d1b6d1a` |
  | --- | --- | --- |
  | `first` | 11,272 | 7,209 |
  | `native100` | 176,292 | 172,229 |
  | `hosts`, `hosts100` (never run the job) | 3,211 / 119,603 | unchanged |

  Also: the trace menu's message for a live host without a channel; the cost of a trace record (168 B) in
  the tooltip, the XML documentation and the quickstart. Tests as reported by the session:
  `JobsPolicyEquivalenceTests` 30/30, `TreeSimulationDebugTests` 16/16, `TreeSimulationActionTests` 46/46,
  `AgentFrameSlotTableTests` 5/5, `TraceTimelineWindowTests` 5/5, `McpDocumentationGeneratorsTests` 14/14.
- **A7** (`cbe4a3f`). `performance.md` and the PlayerPerf README say that runs before and after `a02d2f4`
  are not comparable (the Player has no trace channel by default now).

## Stage 2, items 4 and 5 (`24de026`, `9fe4529`, `8b4ff3f`, 2026-11-24)

- **Item 4** (`24de026`). `JsonStringLiteralV1.Quote` replaces the six `JsonConvert.ToString` calls of
  `NativeCompiledProgramV2Verifier`; `AIBT.Runtime` no longer references Newtonsoft.Json.
  `JsonStringLiteralParityTests` 5/5 (all 65,536 code units in three positions, null and empty, every
  surrogate, 20,000 seeded strings, every value type the verifier writes, and "no Newtonsoft reference"
  with a source scan); `NativeBlackboardProgramBindingTests` 11/11.
- **Item 5** (`9fe4529` ADR-P9-004 amendment, `8b4ff3f`). The generated link.xml writes the `AIBT.Agents`
  element with `ignoreIfUnreferenced="1"`; the scripting define `AIBT_LINKXML_KEEP_MANAGED_NODES` restores
  the previous output. `AibtLinkXmlProcessorTests` 21/21.
- **Players built from `8b4ff3f`** (`C:\UnityProjects\_aibt_f14\size\run16\stage2-builds.log`; every build and run exited 0, nothing
  was stopped). Windows x64, IL2CPP, release:

  | Player | Before (`N0`, stage 0) | After (`-s2`) | Verdict of the run |
  | --- | --- | --- | --- |
  | Native only, Minimal: shipped | 92,065,500 | 76,564,733 | PASS |
  | Native only, Minimal: `GameAssembly.dll` | 26,074,624 | 13,916,160 | |
  | Native only, High: shipped | 84,706,476 | 70,153,845 | PASS |
  | Native only, High: `GameAssembly.dll` | 20,640,256 | 9,011,712 | |
  | Scene-only managed game (`S0`), High: shipped | | 84,489,839 | PASS (the agent turned 131.7 degrees) |
  | Managed Simple Agent game (`C`), Minimal / High: shipped | 91,959,937 (P11-015) | 91,997,665 / 84,608,561 | PASS / PASS (3 agents) |

  The same project without AIBT at Minimal ships 69,491,211 B with a `GameAssembly.dll` of 8,764,416 B
  (P11-015 build A), so a native-only game now pays 7.07 MB at Minimal (5.15 MB of it in `GameAssembly.dll`)
  instead of 22.6 MB. The baseline without AIBT at High was not built, so the High row has no like-for-like
  difference. The stripped native-only Player at Minimal no longer contains `AIBT.Agents`, `AIBT.Authoring`,
  `Newtonsoft.Json` or `System.Data`; `System.Xml` is 6,656 B of IL. A managed game is as large as before.
- **The override.** With the define, project D's generated link.xml has the md5 of the file before item 5
  (`e0496da5f6598989c229ee4e13ee73ee`); without it the file differs by the one attribute
  (`C:\UnityProjects\_aibt_f14\size\out16\D\Minimal-s2keep-link.xml.txt`, `Minimal-s2-link.xml.txt`; md5 recomputed by the orchestrator).

## Postponed by the owner (2026-11-24)

The owner stopped the task here: "leave it for later, push what there is, time to do features". Not done, and
moved to work item P11-017:

- Item 5a: where the rest of AIBT's weight in `GameAssembly.dll` and `il2cpp_data` is. A tool was started
  (`C:\UnityProjects\_aibt_f14\size\tools\pdb_weight.py`: section contributions from `GameAssembly.pdb`, 19.03 of 20.64 MB attributed
  for `N0` at High; the modules are not yet mapped to assemblies).
- The baseline without AIBT at High, and the full size table.
- The rewrite of "Size and memory" in `Documentation~/performance.md` (a note at its top gives the new
  figures), the two corrections of the P11-015 evidence (the preimage is not verified at load; its build B
  was not native-only), the Swarm Arena notes of the P11-014/P11-015 review.
- The second round of memory: one dispatch workspace per adapter instead of per leaf. Decided by the
  orchestrator after an independent options read, not started (`C:\UnityProjects\_aibt_acceptance\P11-016\decision-second-round.md`,
  `C:\UnityProjects\_aibt_acceptance\P11-016\second-round-read.md`). It starts with measurements: simulation-container agents and a same-catalog pair
  of trees.
- Stage 3: the prepared-asset envelope version 2.

## Acceptance of the follow-up and of items 4 and 5 (orchestrator, 2026-11-24)

- **Read by the orchestrator.** The session's report and hand-over note; the diff `cbe4a3f..8b4ff3f` under
  `Runtime/` and `Editor/` (items 4 and 5); the build log, the list of assemblies left in the native-only
  Player and the two link.xml files; the A6 table. The diffs of A1-A7 were read by the independent reader,
  not by the orchestrator.
- **Protected paths.** `git diff --name-status f8ad6b9..8b4ff3f` is empty for
  `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, `CodeGen~`, `package.json` and `Tests/Fixtures`.
  Under `Documentation~/specifications`: `native-runtime-v1.md`, the sentence of A2.
- **Independent read** of `f8ad6b9..8b4ff3f`. A reader given the code and neutral questions; its
  session ended at a usage limit after it had written its full report, which the orchestrator read:
  `C:\UnityProjects\_aibt_acceptance\P11-016\acceptance\independent-review-followup-stage2.md`. No blocker.
  Checked and found correct: no read or write of a non-created trace list on any path and its disposal;
  `JsonStringLiteralV1.Quote` against Newtonsoft's per-code-unit rules (no differing input found) and that
  the parity test covers what it claims; the unconditional link.xml rows are unchanged; no public line
  changed; the specification sentence matches the code. Four should-fix findings, none in executed runtime
  code: the link.xml override is read from Player Settings only and its glue is untested; the consumer
  documents did not mention the change (corrected in this commit: `README.md`, `consumer-quickstart.md`,
  `known-limitations.md`, CHANGELOG); the ADR-P9-004 amendment speaks of what the linker counts as a
  reference more widely than was measured; three new tests prove less than their names say. The review
  could not see the Player results (they were not in the repository yet): they are in "Stage 2" above. The
  open findings are carried to P11-017.
- **Full run of ten assemblies on `8b4ff3f`** (22:34-22:53, Editor started at 17:02), all green; Editor-log counters: unknown-command 0, main-thread-timeout 13 before and after.
  Raw: `C:\UnityProjects\_aibt_acceptance\P11-016\acceptance\full-run-8b4ff3f.txt`.

| Assembly | `2f1c0d7` | `8b4ff3f` |
|---|---|---|
| `AIBT.Runtime.Tests` | 821/821 | 821/821 |
| `AIBT.Integration.Tests` | 601/601 | 604/604 |
| `AIBT.Editor.Tests` | 893/893 | 917/917 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 |
| `AIBT.CodeGen.GenerationTests` | 14/14 | 14/14 |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 |

- **Decision.** P11-016 goes to `Review` with what was delivered: stages 0 and 1, the follow-up, items 4
  and 5. The rest is P11-017, `Ready`, not started, waiting for the owner's word.

Not done by the orchestrator: no Player was built or run again; the override was checked by md5 only; a
development build and a Player with tracing on were measured by nobody.
