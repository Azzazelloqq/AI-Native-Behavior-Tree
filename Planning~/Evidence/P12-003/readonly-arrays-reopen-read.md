# Read-only game arrays (P12-003): independent read after the consumer's 0.2.0 message

Reader: independent read, 2026-11-25. Read-only: no file in the repository was changed, and no Unity, tests,
benchmarks or `unity` CLI were run.

Basis: `Assets/AIBT` working tree at `6db60b2` (clean). That is two commits after the 0.2.0 release `8396202`.
Between the two, only documentation comments changed in `Runtime/Simulation/SimulationTypes.cs` and
`TreeSimulation.cs`, plus tests and evidence. Line numbers below are those at `6db60b2`, and paths are relative
to `Assets/AIBT` unless they are absolute.

Labels: **[code]** means I read it at the cited line. **[unverified]** means I did not establish it by reading
code; it is an estimate, a judgement, or something only a run can show. Timing figures are **[unverified]**
unless they quote `Planning~/Evidence/P12-002/README.md`.

---

## 1. The four examples in 0.2.0 without a package change

### What every recipe rests on

- **What a node can read.** A node reads one value per `SnapshotReadHandle<T>` per read call. The row behind
  the value is chosen by the container, not by the node: `SelectRow` returns the slot, or `RowOf[slot]` for an
  indexed channel (`Runtime/Simulation/SimulationNativeV1.cs:175-185`). The value is staged into the node's
  binding region before every dispatch (`SimulationNativeV1.cs:194-226`, called from
  `Runtime/Integration/AgentFrame/AgentFrameCoreV1.cs:328-337`). **[code]**
- **No arrays in node code.** A node cannot hold a native container, a pointer or a static; the analyzer and the
  generator refuse them (`CodeGen~/AIBT.CodeGen/BurstNodeGenerator.cs:493-509`). So the node can never choose an
  index itself. **[code]**
- **When a step copies game data.** Every step copies each bound input before anything runs: slot rows up to
  `HighWaterIndex`, or the whole rows array plus the indices for an indexed channel
  (`Runtime/Simulation/TreeGroup.cs:1006-1018`). **[code]**
- **What is checked before the copy.** Validation runs first (`TreeGroup.cs:951-979`, called from
  `TreeSimulation.cs:160-166`). A game job that still writes the array is caught only inside
  `#if ENABLE_UNITY_COLLECTIONS_CHECKS` (`TreeGroup.cs:957-967`). **[code]**
- **Who thinks in a tick.** With `Spread(5)` the group assigns phases internally (`TreeGroup.cs:517-521`). The
  phase is not public, and `ThinkSchedule.Phase` is 0 for a spread schedule (`SimulationTypes.cs:228-229`). So
  the game cannot cheaply tell which 100 of its 500 agents think in a tick. It can if it schedules with
  `ThinkSchedule.Every(5, phase)` and picks the phases itself (`SimulationTypes.cs:224`). Wakes add thinkers on
  top of that (`TreeGroup.cs:605-611`). **[code]**
- **What one think costs.** The P12-002 job-path step costs about 715–739 µs for 100 thinks
  (`Planning~/Evidence/P12-002/README.md:124-127`), so about 7 µs per think including everything. **[code]**
  for the numbers; dividing them into a per-think cost is **[unverified]**.

### 1.1 Walkability grid

**How it works today.** The game owns the grid.
- Before each `Step` it runs its own Burst job over all live agents. The job reads the cells the nodes need
  (ahead, left and right of the agent's position and heading) and writes flags into a per-agent row type such as
  `Senses { bool AheadWalkable; ... }`. The game completes the job, then calls `Step`.
- The channel is bound once with `TryBindInput(channel, rows)` (`TreeGroup.cs:832-841`).
- The node reads the flags through `[AibtInput("senses")]`.

**What is lost:**
- **Latency.** None. The samples are written before `Step(t)` and read in `Step(t)`.
- **Cost per step.** The game job does about 500 × 3 cell reads per tick. That is 5 times more than needed,
  because only about 100 agents think, unless the game uses `Every(5, phase)`. In Burst it is probably a few µs
  **[unverified]**. AIBT copies 500 × `sizeof(Senses)` bytes per step (`TreeGroup.cs:1017`) and stages the row per
  dispatch. No measurable difference against a plain per-agent channel was found in P12-002
  (`README.md:131-136`).
- **Code the game must write.** A registered value struct, the job, the bind, and the completion of the job
  before `Step`. Every new kind of probe a node designer wants means a new field and new game code.
- **What cannot be expressed:**
  - a probe whose cell depends on node configuration, node memory or the branch the tree is in (for example
    "the cell towards my flee target", where the flee target lives in node memory or the blackboard);
  - a probe along a path;
  - a number of probes the node decides at run time;
  - a library node written against "a grid" rather than against this game's sampled row.

  All of these need the game to mirror the tree's state. The mirror can be fed through a command, with one tick
  of latency (see 1.4, variant c).

### 1.2 Zone map

**How it works today.** Use an indexed channel (`TreeGroup.cs:865-874`).
- `rows` is one row of zone facts per zone. `rowOfSlot[slot]` is the zone of the agent's current cell, and the
  game computes it each tick (500 grid lookups, in its own job, completed before `Step`;
  `Documentation~/simulation-container.md:176-178`).
- A negative index reads zeros (`SimulationNativeV1.cs:181-184`). An index past the end in a live slot refuses
  the step (`TreeGroup.cs:974-976`).

**What is lost:**
- **Latency.** None.
- **Cost.** The whole zone rows array is copied once per step per group (`TreeGroup.cs:1013`), plus 4 bytes per
  slot. Rows are staged per dispatch as for any channel.
- **What cannot be expressed:**
  - a node reading a neighbouring zone, or a zone it picks from its own state;
  - a node reading the cell-to-zone map at an arbitrary position.

  A fixed set of "other zones" can be offered as extra channels, one per lane (see 1.4).

### 1.3 List of threats

There are three variants today.

**(a) Sampled per agent.** The game job computes the nearest threat, the count within radius R, or the top-k
threats for each agent, into a slot-row channel. This is the same mechanism as 1.1.
- Cost: 500 × N distance tests per tick in the game's job, probably tens of µs at N = 32 **[unverified]**.
- Latency: none.

**(b) A fixed window in one row.** A registered struct with fields `T0…T(N-1)` and a `Count` field, bound as an
indexed channel. There is one row per squad or zone, or a single row with every `rowOfSlot` set to 0.
- Nested registered structs are allowed (`BurstNodeGenerator.cs:527-535`), but arrays are not. A fixed buffer is
  a pointer-typed field and probably refused by `:496` **[unverified]**.
- Each read must read every leaf of the value: `TryCompleteValueRead` latches `IncompleteValue` otherwise
  (`Runtime/Nodes/Contracts/BurstBindingBridgeCoreV2.cs:780-806`). The generated `TryReadSnapshot` does that for
  the node in one call. So a 32-entry `Float3` list costs about 97 leaf reads per think, whether the node needs
  one entry or all of them. That is probably a few µs, comparable with a whole think **[unverified]**.
- Element k is reached only by a hand-written `switch`.
- N is fixed when the node is compiled. The trace shows only the first 16 bytes
  (`simulation-container.md:197-199`).

**(c) An external action (query and answer).**
- One tick of latency, plus one extra think through the completion wake (`TreeGroup.cs:1243-1246`).
- One managed `TryComplete` call per query on the main thread. The first call per type allocates
  (`TreeGroup.cs:1259`).
- At most 2 live actions per agent by default (`SimulationTypes.cs:156-160`).

**What cannot be expressed:** a node that filters an unbounded or variable-length list by criteria of its own
(configuration, memory or branch), in one think.

### 1.4 Another squad's row

This is the example where the documents say less than the code allows. Today a node can read *a* squad row
other than its own, provided the **game** decides which one.

**Works, with zero latency.** Declare a second input id in the node, for example
`[AibtInput("targetSquad")] SnapshotReadHandle<SquadInput>`, next to `[AibtInput("squad")]`.
- Bind it with `TryBindInput(targetSquad, squadRows, targetSquadOf)`, that is, the **same** `squadRows` array
  and a **different** index array.
- Channels are separate states keyed by binding id (`Runtime/Simulation/SimulationProgramMapV1.cs:186-220`;
  `TreeGroup.cs:837`, `:870`).
- `BindRows` keeps a reference to the array and claims no ownership (`TreeGroup.cs:876-898`). Validation only
  checks read access (`TreeGroup.cs:960-961`). So nothing stops two channels from sharing one rows array.
- Cost: one more copy of the rows array and of the indices per step (`TreeGroup.cs:1013-1014`). For about 50
  squads this is small.
- Not pinned by a test: I found no fixture node with two snapshot bindings of the same value type, and no test
  that binds one rows array to two channels. The code shows no barrier. **[unverified by test]**

**Works: "my target agent's row" (ADR-P10-008 Q3).** Bind the game's per-agent array, at least `Capacity` long,
a second time as an indexed channel with `rowOfSlot[i]` set to the target's slot. The cost is a copy of the whole
per-agent array per step.

**Works: K fixed lanes.** Declare K such channels ("ally", "enemy", "support") for up to K other squads per
agent.

**Works, with one tick of latency (variant c).**
1. The node emits a command "I want squad k".
2. After the step, the game reads `GetCommands`, which returns a `NativeArray.ReadOnly` that a Burst job can
   read (`TreeGroup.cs:1115`). It writes `targetSquadOf[slot] = k` and calls `TryWakeMany` for the requesters
   (`TreeGroup.cs:481-495`).
3. The node reads the row in the next step.

This is allocation-free and batched, so it is a better fallback than an external action for many small queries.
Its costs are one tick and one extra think per request **[unverified cost]**.

**Does not work:**
- the node picks k from its own state and reads the row in the same think;
- the node iterates over all squads (a search, or "the weakest ally");
- reading more distinct other rows in one think than the channels it declares.

`simulation-container.md:207` says "A node reads its own squad's row only". Per channel that is true, but as a
statement of what a node can do it is too narrow, and it probably led the consumer to count this example as
impossible.

### 1.5 What the recipes lose overall

1. **Coupling.** Every node-side query becomes game-side code, plus a channel and a value type. A node designer
   cannot add a query without a game programmer. Library nodes cannot be written against world data at all.
2. **Questions decided inside the tree.** Indices that depend on tree state, and scans with tree-side criteria,
   are possible only with one tick of latency and an extra think.
3. **Wasted sampling.** About 5 times redundant at `Spread(5)`.

The CPU cost of the recipes at the consumer's load is probably not the problem **[unverified]**.

---

## 2. The reopening condition

**Is it met?** Read literally, no. The decision (`decision-readonly-arrays.md:32-35`) asks the consumer to name
"a decision whose index depends on tree state the game cannot know before the step, or one that must scan several
elements in one think". Their message names *data* (four kinds) and *guarantees*. It does not say where the index
comes from, or whether a node scans.

"Another squad's row" comes closest. If a node chooses the squad, it meets the condition. If the game chooses,
section 1.4 serves it today. The message does not say which.

**Is the condition a fair test?** No. There are four reasons.
1. **The consumer was never told it.** The section 7 questions that would tell them what to state were not
   sent. The last thing the consumer heard was "not available, design task opened"
   (`reply-2026-11-24.md:27-33`). A condition the consumer does not know cannot be met except by accident, and
   "still the main gap" is the answer one would expect.
2. **"The game cannot know before the step" is almost never strictly true.** Any tree state can be mirrored to
   the game through a command with one tick of latency (1.4, variant c). Read strictly, the condition can always
   be argued unmet. It needs a threshold instead: one tick of latency is unacceptable for this decision, or the
   mirroring would duplicate tree logic in game code.
3. **"An external action does not serve it" is the same trap.** An action always serves, with one tick of
   latency and a managed main-thread call per query. The condition gives no quantity: how many queries per think
   or per tick make that unacceptable.
4. **It asks for an implementation-level distinction** (where the index comes from) that a consumer describes
   naturally as "the node needs the grid". The consumer can answer if asked with examples. They cannot be
   expected to volunteer it.

A fair test asks concrete questions (section 6) and states, before the answers arrive, what result triggers
building it.

---

## 3. The guarantees asked for, and which design gives each

The consumer asks for:
1. a read-only `NativeArray`, not indexed by slot, bound to a group or the container;
2. readable from Burst nodes during `Step`;
3. no allocation;
4. the same values in place and in jobs;
5. the game may write it again after `Step` returns;
6. the step is refused if a game job still writes it.

Letters refer to the earlier read's designs; "Recipes" means A, B and C together.

| Guarantee | Recipes (A/B/C) | D (row k of a channel) | E1 (handle, copy per step) | E2 (handle, view of game memory) |
|---|---|---|---|---|
| 1 array, not slot-indexed | no: per agent, or one row per channel | yes: any array bound as an indexed channel's rows, which may have any length (`TreeGroup.cs:846-848`) | yes | yes |
| 2 read in Burst during Step | yes, but only rows the game chose | yes | yes | yes |
| 3 no allocation | yes; B allocates on the first `TryComplete` per type (`TreeGroup.cs:1259`) | yes, after the session-capacity change | yes, if the copy buffer is allocated at bind | yes |
| 4 same values in place and in jobs | yes: one staging routine for both paths (`SimulationNativeV1.cs:163-167`) | yes, if the read sits in the bridge | yes, the same way **[unverified until built]** | yes, the same way |
| 5 game writes again after Step | yes: copy at `BeginStep` | yes: reads staged copies | yes: copy at `BeginStep` | yes: all jobs complete before `Step` returns (`TreeGroup.cs:722-723`; `TreeSimulation.cs:11-14`) |
| 6 refused while a game job writes | only with collection checks (`TreeGroup.cs:957-967`) | same | same | same, but if the check is off the race lasts the whole step, not only the copy |

**Copy per step.** The recipes, D and E1 copy (the recipes and D through the existing input copy). E2 does not.

**What no design gives.** Guarantee 6 holds in no design in a build without
`ENABLE_UNITY_COLLECTIONS_CHECKS`. Without the checks, nothing detects a writer that is still running, for
inputs today or for any array design. The consumer asked for "the same guarantees as inputs", so E1 meets the
letter of the request. They should still be told plainly that in a Player with checks off the guarantee is
theirs to keep.

Only an additive `Step(tick, JobHandle)`, or a writer handle supplied at bind time, would make this hold in every
build. It would **wait** for the writer, not refuse. None of the earlier designs includes it; the earlier read
raised it only as an owner question (`readonly-arrays-design-read.md:387-388`).

Implied, but not asked for: wake when a cell changes, element reads in the trace, and library nodes reading the
arrays. The request does not need any of these.

---

## 4. What building E1 touches, checked against the code

**`Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`**
- New lines would be:
  - `T` lines for `ArrayReadHandle<T>` and `AibtArrayBindingAttribute` (with its `C` line);
  - `M` lines for `TryReadArrayHandle` on `BurstGeneratedRuntimeBridge`, next to `TryReadSnapshotHandle`;
  - `TryReadElement` and `TryGetLength`, either on three contexts or as bridge statics;
  - possibly an `OutOfRange` value: one `E` and one `F` line in `BurstContextResult` (`:117-141`).
- The short form `[AibtArray]` would not be in this file: `AibtInputAttribute` is not in it either
  (`Runtime/Nodes/Contracts/NativeNodeShortForms.cs:106`).
- Additive: existing lines stay byte-identical as long as no field is added to a context struct. Context sizes
  are pinned (`Tests/Editor/CodeGen/Contracts/PublicBurstAbiContractTests.cs:40-43`, `:77-85`).
- A second protected file must change too, which the earlier read did not list.
  `PublicSurfaceV2_ChangesOnlyAcceptedAdditiveMembersAndContextLayouts` hard-codes 357 lines and rebuilds v2
  from v1 plus the three past additions, AIBT-038, -041 and -042 (`PublicBurstAbiContractTests.cs:28-72`).
- E1 would be by far the largest addition the frozen surface has had. The past additions were one interface
  with three members, one lookup method, and one configuration reader. **[code]**
- Confidence that existing lines remain identical: high.

**Generator (`CodeGen~/AIBT.CodeGen/BurstNodeGenerator.cs`)**
- Changes needed:
  - recognise the new handle (`IsGeneratedHandle`, `:577-581`) and the binding attribute (`IsBindingAttribute`,
    `:594-600`; `HandleBindingMatches`, `:602-623`; `BindingMismatchLocation`, `:546-566`);
  - emit the configuration decode (`:1672` shows the snapshot case);
  - emit `BurstAccess` helpers.
- `BurstAccess` helpers are emitted per handle type that a catalog actually uses (`:1083-1127`). So catalogs that
  do not use the new handle get the same generated source.
- Additive for unused catalogs: confidence high.
- This is **not** true of D. A row-k overload emitted for every `SnapshotReadHandle<T>` (`:1113-1117`) would
  change the generated source of every catalog that has inputs, unless an attribute gates it. Fingerprints would
  still not change.

**Metadata emitter (`CodeGen~/AIBT.CodeGen/GeneratedMetadataEmitter.cs`)**
- `IsBinding` (`:349`) needs the new attribute. `ReadBindings` needs an explicit branch: today its final `else`
  classifies any other binding as kind 6, Completion (`:134-135`). An addition that forgets this would
  misclassify the binding silently.
- Binding kinds enter the descriptor and `AccessHash` only for nodes that have such a binding (`:202-210`,
  `:278-287`).
- The registry manifest, whose hash goes into compiled programs, contains no bindings at all. It writes empty
  `reads`/`writes` (`:153-189`, `:171`).
- Additive: confidence high.

**Node analyzer (`CodeGen~/AIBT.CodeGen/BurstNodeUsageAnalyzer.cs`)**
- `BindingAttribute(operation)` returns `null` for an unknown operation name (`:581-592`). Callers then skip the
  declared-binding check (`:59-63`). So a new `BurstAccess.TryReadElement` would **escape** AIBT5006 and the phase
  check (`PhaseAllows`, `:552-561`) unless both are extended.
- `BindingKindAllows` falls through to "snapshot or consume" (`:578`).
- Adding the rules is additive: existing code gets the same diagnostics. **[code; not run]**

**Compiled program format (`Documentation~/specifications/compiled-program-v1.md`)**
- No change. The "access tables" in the content hash (`:62`) are the blackboard read-slot and write-slot ranges
  derived from the manifest (`:42`). For generated Burst nodes the manifest emits these as empty
  (`GeneratedMetadataEmitter.cs:171`). The header carries the node-registry hash (`:20`), and the registry JSON has
  no bindings.
- This settles the earlier read's open point (`readonly-arrays-design-read.md:287-289`): binding kinds do not
  enter compiled programs.
- Confidence: medium-high. I did not trace the hot-reload paths, the Editor registry harness
  (`Editor/Registry/GeneratedNodeReflectionHarness.cs`) or the tree-asset import caches.

**Internal runtime (not protected, but needed)**
- The catalog validation refuses any kind above Completion
  (`Runtime/Execution/Burst/Dispatch/NativeBurstDispatchBindingValidationV2.cs:432`).
- The kind enum (`NativeBurstDispatchBindingContractsV2.cs:6-15`).
- Workspace capacity counts one session for each non-async binding (`GeneratedBurstCatalogV2.cs:213-219`). A
  direct read must not consume one.
- `SimulationProgramMapV1`'s switch ignores unknown kinds (`:127-221`). A new kind would bind nothing silently,
  so it needs a case.
- The dispatch backing has no view of game data (`Runtime/Execution/Burst/Dispatch/BurstDispatchBackingV2.cs:7-50`).
  A private read-only view is allowed by `burst-node-abi-v2.md:128-133`.
- The adapter and the agent-frame core both need to fill that view.
- Hosts must refuse programs with the new kind, as they refuse snapshots (`simulation-container.md:146-147`). I
  did not locate that refusal in the code. **[unverified]**

**Specifications (`Documentation~/specifications`)**
- `burst-node-abi-v2.md` gets a new section modelled on the AIBT-042 addendum (`:3-16`).
- The earlier read says the sentence "adds no … native-container accessor, pointer accessor" (`:40-42`) must be
  lifted. I disagree mildly. A typed element read through an opaque, validated handle exposes no container,
  just as `TryBeginSnapshotRead` exposes none. A clarifying sentence is enough.
- The new read must still follow "each operation validates owner generation, … phase, … bounds" (`:140-141`). A
  read without a session is a new kind of operation, so `burst-node-abi-v1.md`'s transaction section needs a
  sentence saying element reads open no session.
- `trace-v1.md` is unchanged if there is no per-read trace. `compiled-program-v1.md` is unchanged.

**What I could not establish by reading:**
- whether generated output for the existing fixtures (`Tests/Fixtures/P2/CodeGen`) really stays byte-identical;
  only a generator run shows it;
- whether a worker-thread read of a container-owned copy needs any safety attribute beyond those the backing
  already uses (`BurstDispatchBackingV2.cs:9-45`);
- Burst behaviour on the consumer's Unity 7000.0.0a8 with Burst 1.8.29. The package has no evidence there; G5 is
  postponed (`reply-2026-11-24.md:69-72`);
- the per-element read cost and the copy cost of a realistic array.

---

## 5. The cost of being wrong in each direction

### Building E1, and the shape turns out wrong

- **Permanent surface.** The handle, the attribute and the calls become ABI v2 lines pinned by the contract
  test. A better shape later means adding a second shape and keeping the first, or bumping `AbiVersion` to 3.
  Every catalog then fails with AIBT5012 until it is regenerated (`burst-node-abi-v2.md:29-34`).
- **Package effort.** Roughly the generator, analyzer, metadata, six internal sites, two contract files and a
  specification section **[unverified estimate]**.
- **What the consumer would redo:** rewrite the nodes that use the handle (configuration field, read sites) and
  the game's bind calls.
  - If a node's configuration layout changes, the node version must change and tree assets need migrating
    (`burst-node-abi-v2.md:14-16` states the rule for a parameter type change; that it applies to handle fields
    is **[unverified]**).
  - On Unity 7 they would be the first to run the new Burst path. A Burst or safety problem there lands on them
    first, and the package cannot reproduce it until G5.
- **Semantic mistakes** (fault against default for an index out of range, copy against view, group against
  container) can mostly be changed later without changing the surface.

### Leaving the consumer on recipes, and the array read turns out needed

- **What the consumer writes now:** sampling jobs, extra channels and value structs per query, and possibly
  fixed-window structs.
- **What they would redo when E1 arrives:** move those nodes to array reads and delete the sampling code. That
  redo is **optional**, because the recipes keep working.
- **The real cost is time and coupling.** The consumer called this "the main gap" twice. They cannot serve
  themselves: the analyzer forbids native containers, `SharedStatic` and pointers in node code
  (`BurstNodeUsageAnalyzer.cs:667-705`). Every week on recipes is game code written to mirror tree logic.

### Asymmetry

A wrong build costs the package forever, and costs the consumer only if the shape is removed. A wrong deferral
costs the consumer now, in work they will likely partly throw away. Neither cost is large compared with the
uncertainty that remains: sizes, where the index comes from, and scans. A short, explicit round of questions
removes that uncertainty, provided the questions are actually sent with a commitment attached.

---

## 6. What to do

### Recommendation: the decision stands with changes

Make no ABI change today, but replace the parts that leave the consumer waiting with no path forward.

**1. Send the consumer an answer now** (documentation only):
- the recipes of section 1, including the variants the decision and the earlier read missed:
  - a second channel over the same rows with its own index array ("another squad's row", chosen by the game);
  - the per-agent array bound again as an indexed channel ("my target's row");
  - K fixed lanes;
  - request and response through a command, an index array and `TryWakeMany`;
  - `Every(5, phase)` to sample only the agents that are due;
- correct `simulation-container.md:207`;
- qualify "the step refuses" with "collection checks on". The decision's recipe text omits this
  (`decision-readonly-arrays.md:16-17`).

**2. Ask five questions in the same message, with examples and with the trigger stated:**
1. For each of the four examples, who chooses the index or the row: the agent's position or squad (the game
   knows it), or node configuration, memory or branch? Name one node.
2. Does any node read several elements in one think (a scan or search)? How many?
3. Is one tick of latency plus one extra think acceptable for those queries?
4. What sizes and element types: the grid dimensions, the threat-list maximum, how many arrays, how many groups?
5. Out of range: a node bug that should fault the agent, or a legitimate "off the map" that should read a
   default?

State before the answers arrive: if questions 1 or 2 name at least one node-side index or scan, and question 3
says no, E1 is built, with no further round.

**3. Start the internal prototype now, in parallel.**
- Use the earlier read's section 5, reduced to A against E1 on W1 and W2, plus a copy-size sweep.
- It needs no public API and no protected change. The P12-003 card already had it in scope
  (`Planning~/Tasks/P12/P12-003-read-only-game-arrays.md:26`). The decision dropped it on the ground that it
  "cannot answer the question that matters" (`decision-readonly-arrays.md:47-50`).
- That is half right: it cannot answer whether such a case exists. It does answer the read cost, the copy cost,
  the job-safety question and the byte-identity question (run the generator on the existing fixtures). All four
  must be known before building anyway. Running it now means the build can start on the day the answer arrives.

### If it is built: the smallest E1

**Keep:**
- `ArrayReadHandle<T>` with `TryReadElement` and `TryGetLength` on Enter, Tick and Observer;
- a copy at `BeginStep`;
- the canonical check on each element read;
- the bind-time type check reused from inputs (`TreeGroup.cs:880-888`);
- the refusal with `InputMissing`, as for inputs;
- the short form `[AibtArray("id")]`, which is outside the contract file.

**Simplify: bind at group level first** (`TreeGroup.TryBindArray`). The consumer accepts either level. A group
bind reuses the validation and copy structure of `ValidateInputs` and `BeginStep` (`TreeGroup.cs:951-1019`) and
the per-group map. A container bind needs a new validation loop in `TreeSimulation.Step` (`:160-166`) and a map
across groups. Add a container overload only if question 4 shows many groups sharing large arrays.

**Decide from question 5:** a new `BurstContextResult.OutOfRange` against a non-faulting result.

**Drop, as E1 already does:** wake, the per-read trace, E2.

**Add:**
- the analyzer rules, without which the read bypasses AIBT5006 (section 4);
- the explicit metadata branch (`GeneratedMetadataEmitter.cs:134-135`);
- the contract-test amendment;
- a test pinning generator output for existing fixtures as byte-identical;
- a parity test, in place against jobs;
- a Burst proof test that the jobs actually ran Burst code (P12-002 added one; `git log 8e00e2d`).

**Consider:** an additive `Step(tick, JobHandle)` for inputs and arrays together, the only way guarantee 6
holds in a Player.

**Order:**
1. Owner question 1 of the earlier read: accept an additive ABI v2 change.
2. The prototype numbers.
3. The consumer's answers to questions 1–5.
4. An ADR that names the lines.
5. Code.
6. Evidence.

---

## 7. What is wrong or unsupported in the earlier documents

1. **The decision: "spend the promise 'the node ABI is frozen'"** (`decision-readonly-arrays.md:46-47`). The
   reply froze the *existing* handle types and the compiled program format (`reply-2026-11-24.md:75-76`). An
   additive handle keeps that promise. What an addition really costs is that the addition itself is frozen
   forever (section 5). The argument conflates the two.
2. **The decision: recipe 1 says "the step refuses to run while that job still writes the rows"**
   (`decision-readonly-arrays.md:16-17`). This holds only with collection checks (`TreeGroup.cs:957-967`).
3. **The decision: the reopening condition** is unannounced and close to unfalsifiable (section 2). The decision
   also never sent the questions (`:36`) that would make it testable.
4. **The decision: "a prototype cannot answer the question that matters"** (`:47-50`). It cannot answer that
   question, but it answers the other prerequisites, and P12-003's own scope included it. Deferring it makes the
   path serial.
5. **The earlier read, §0.1 and §6, and `simulation-container.md:207`.** Both miss that a second channel over the
   same rows with another index array already reads another squad's row, or a target agent's row, when the game
   chooses it. The read routes Q3 to design E (`readonly-arrays-design-read.md:351-353`) without noting this
   variant, which needs no ABI change.
6. **The earlier read, D: "Programs keep their bytes" and "catalog metadata … do not change."** True for
   metadata and fingerprints. But D as described, an overload for every snapshot handle, changes the generated
   source of every catalog with inputs (`BurstNodeGenerator.cs:1113-1117`).
7. **The earlier read, E's list of protected changes**, leaves out:
   - the contract test's hard-coded reconstruction (`PublicBurstAbiContractTests.cs:28-72`);
   - the analyzer gap: unknown `BurstAccess` operations escape the binding check (`BurstNodeUsageAnalyzer.cs:581-592`);
   - the emitter's default-to-Completion branch (`GeneratedMetadataEmitter.cs:134-135`);
   - the runtime kind ceiling (`NativeBurstDispatchBindingValidationV2.cs:432`).
8. **The earlier read, §4 "lift `burst-node-abi-v2.md:40-42`."** Arguable (section 4); a clarifying sentence
   should do.
9. **The earlier read's open point on compiled access tables** is settled in favour of "unaffected" (section 4).

Checked and found correct, as cited:
- `SelectRow` and `TryStage`;
- the input copy at `BeginStep`;
- `IncompleteValue`;
- the in-place against jobs numbers (`README.md:124-127`);
- ADR-P10-008 alternative 4 (`ADR-P10-008-simulation-container.md:590-595`) and Q3 (`:750-751`).

---

## Conclusion

The direction stands: no ABI change today, because all four examples are served in 0.2.0 when the game chooses
the index, and nothing yet shows a node-chosen index. But the decision as recorded must change in three ways:
- tell the consumer the recipes now, including the second-channel variant for "another squad's row";
- send five concrete questions with the build trigger stated in advance, because the current reopening condition
  is unannounced and nearly unfalsifiable;
- run the internal prototype now rather than after the answer.

If the answer names one node-chosen index or scan, build a group-level E1 with the additions listed in section 6.

**Confidence:** medium on the recommendation; high on the code facts in sections 1 and 4.

**Facts that would change this conclusion:**
1. The consumer names a node whose index comes from tree state, or a scan per think, and refuses one tick of
   latency. Then build E1 at once.
2. The prototype shows E1's element read is not clearly cheaper in total than game sampling at 500 agents, or
   shows a job-safety problem with the copy. Then recipes become the long-term answer, and the "build" trigger
   should require a case that cannot be expressed at all.
3. A generator run shows existing catalogs' output is not byte-identical under the addition. Then it is not
   additive, and it needs the owner's explicit acceptance as a format change.
