AIBT → SweetKingdom: read-only game arrays (P12-003). Is 0.2.0 enough for your nodes? Please check against your real trees.

We have not built an array read. It is the one request that needs an addition to the node ABI (a new handle
type, the generator, the analyzer, the specification), and an ABI addition stays forever. Before we make it we
need to know whether your nodes need it or whether 0.2.0 already serves them.

Our position: all four of your examples can be served in 0.2.0 whenever the game knows, before `Step`, which
element the agent needs. An array read is needed only when the node itself chooses the index during the think,
or must scan several elements. Please check this against your code and tell us where it is wrong.

How each example is served in 0.2.0 (package at `8396202`, `Documentation~/simulation-container.md`)

1. Walkability grid (anything keyed by the agent's position).
   The game samples the grid per agent in its own Burst job before `Step` and writes the answers into the
   agent's row of a per-agent input channel, for example `SenseInput { WalkableAhead, WalkableLeft,
   WalkableRight, CellZone }`. The node reads it with `[AibtInput("sense")]` and `TryReadSnapshot`.
   Guarantees are those of inputs: the step sees the values at its entry, same in place and in jobs, no
   allocation, and a job that still writes the rows when `Step` starts refuses the step (`InputMissing`,
   collection safety checks on). No latency: sampled before `Step(t)`, read in `Step(t)`.

2. Zone map.
   One row per zone in an indexed channel: `group.TryBindInput(Zone, zoneRows, zoneOfSlot)`; the game writes
   `zoneOfSlot[slot]` when the agent changes cell. Every agent reads the row of its own zone. Wake on change
   works through the index if you want agents to react to a zone fact at once.

3. List of threats.
   The game picks per agent (or per squad) what the tree needs (nearest threat, the strongest, a count) and
   writes it into the per-agent row or the squad row. The node does not see the list, it sees the answer.

4. Another squad's row.
   When the game knows which other squad (the squad's current target, its neighbour, its leader squad): a
   second channel of the same row type, bound to the same rows array with another index array:
   `group.TryBindInput(TargetSquad, squadRows, targetSquadOfSlot)`. The node declares a second
   `[AibtInput("target-squad")]` field. Not yet pinned by a test of its own: binding one rows array to two
   channels follows from the design (each channel copies the rows at step start), and we will add the test.

5. A rare lookup whose index only the tree knows.
   An external action: the node starts it with the query as the start value (`TryStart`), the game answers
   between steps with `TryComplete<TResult>`, the node reads the answer in the next step (`TryConsume`).
   Latency: one tick (33 ms at 30 Hz); the completion wakes the agent whatever its schedule.

6. A change of the grid that matters to particular agents: `TryWakeMany`, or a trigger channel with
   `TrySetWakeOnChange`.

One caution, not established yet: read each input once per callback into a local. Whether a second read of
the same input field inside one callback is refused has not been tested.

What the recipes cannot do (here an array read is really needed)

- The index is computed inside the node from tree state in the same think (a blackboard value, the result of
  an earlier node of this think), and one tick of latency is not acceptable.
- The node must scan: a variable number of elements in one think (a ray march through the grid, a loop over
  all threats with a filter that only this node knows).
- So many different lookups per agent and think that a fixed row cannot hold the answers.

Questions. Please answer per real node of your trees, not in general.

1. List every node that would read an array. For each: which array, how the index is derived (the agent's
   position, its squad, a blackboard value the tree wrote, a loop), how many elements it reads per think,
   and how often it runs.
2. For each of them: can the game compute the index or the answer before `Step` from data it owns? If not,
   what exactly does only the tree know?
3. Does any node have to scan? What is the upper bound of elements per think?
4. Is one tick of latency acceptable for rare lookups (recipe 5)? For which nodes not?
5. "Another squad's row": who chooses the other squad, the game (assignment) or the node (for example it
   compares all squads)?
6. Array sizes (elements × bytes per element) and how often the game changes them. The design we would
   build copies each bound array once per step; that is what gives "same values in place and in jobs" and
   "the game may write it again after `Step` returns". A 256 × 256 byte grid (64 KB per step) is fine; a
   large map of structs is not. If an array is large: does it change rarely enough for the game to tell us
   when it changed?
7. Is your objection to sampling on the game side its cost per step (measured or estimated, at 500 agents),
   the amount of game code, or cases it cannot express? This decides what we build.
8. If the read is built: should an index out of range fault the agent (as a non-canonical input does) or
   return false with zeros? Do nodes need the length? Is wake on a change of an array needed? (We plan: fault,
   length available, no wake.)

The shape we would build, for your comments (names are not final): a new read-only handle in the node config,
`[AibtArray("id")]`, with `TryReadElement(index, out T)` and `TryGetLength`; the game binds a `NativeArray<T>`
to the container once; each step copies it at entry and refuses to start while a game job still writes it;
every element read is checked as an input row is; no wake on change; no trace record per read. It would be
added to ABI v2 without a version bump, so existing catalogs and compiled programs keep their bytes.

The answer we need: "0.2.0 is enough", "enough if you add X", or "not enough: node N needs …", with the node
named. If a node falls into the list "what the recipes cannot do", we build the read.
