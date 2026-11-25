# Decision: read-only game arrays in nodes (P12-003) — orchestrator, 2026-11-25

Under the owner's delegation of 2026-11-23. Independent read first: `readonly-arrays-design-read.md` in this
folder (a reader given the code at `8396202`, the consumer's words and neutral questions; nothing was decided
when it read). I read its findings and its sections 2, 6 and 7.

## Decision

**Not built now. The array read is deferred, with a recorded shape and a condition for reopening it; what is
delivered now is the pattern that serves the consumer's own examples with no change to the node ABI.**

1. No new handle, no context call, no generator or specification change now. The node ABI stays as the
   reply of 2026-11-25 told the consumer it is: frozen.
2. Delivered now (documents and tests only, no protected area): a section "Game data that is not per agent"
   in `Documentation~/simulation-container.md` with four recipes, each backed by a test:
   - the game samples per agent, in its own Burst job before `Step`, into an input row ("is the cell ahead
     walkable", "nearest threat"); the step refuses to run while that job still writes the rows;
   - zone-level facts through the indexed channel of 0.2.0 (`rowOfSlot` = the zone of the agent's cell);
   - a rare lookup whose index only the tree knows, through an external action answered by the game
     (one tick of latency);
   - `TryWakeMany` when a change of the grid matters to particular agents.
   Also establish by a test and write down what happens when one callback reads the same input binding more
   than once (the read found a possible `CapacityExceeded`; it is not confirmed).
3. An ADR records the considered designs, the shape to build if it is built, and the condition:
   - shape if built: design E1 of the read: a new array handle with `TryReadElement` and `TryGetLength`, the
     array bound at container level and copied once per step, a canonical check on each element read, an
     out-of-range index faulting the agent with its own result code, no wake on change, no per-read trace,
     added to ABI v2 without a version bump so that catalogs which do not use it keep their bytes;
   - rejected for good: a raw pointer or a `NativeArray` in node code (ADR-P10-008 alternative 4 stands) and
     reading game memory during the step (E2: it goes around the job system's dependency tracking to save a
     copy of a few microseconds);
   - reopened when the consumer names a decision whose index depends on tree state the game cannot know
     before the step, or one that must scan several elements in one think, and per-agent sampling or an
     external action does not serve it. Then: the prototype of the read's section 5 (A against E1, two
     workloads, an array-size sweep), then the decision.
4. The consumer gets the questions of section 7 of the read (the first three decide whether it is reopened).

## Why not build or prototype now

- Both examples the consumer gave (a walkability grid, a list of threats) ask for an answer that depends on
  the agent's position, which the game knows before the step. Per-agent sampling in the game's own Burst job
  serves them today, deterministically, with the guarantees inputs already have, and probably at the lowest
  total cost (one job over 500 agents instead of a read path inside every node dispatch).
- The array read is the one request of the seven that cannot be made without the protected areas: a new
  handle in the contract file, the generator and the analyzer, and a section of the normative
  specification. An ABI addition is kept forever. Making it before one concrete case needs it would spend
  the promise "the node ABI is frozen" on a guess.
- A prototype cannot answer the question that matters. It would say how many nanoseconds an element read
  costs against sampling; whether there is a decision that sampling cannot serve is the consumer's to say.
  The prototype is cheap to run later and its plan is written.
- Two facts are not settled from the code (whether compiled programs record binding kinds in a way a new
  kind disturbs; whether a worker-thread read of a game-owned array trips the safety checks). Both belong to
  the prototype, not to a decision made now.

## Noted for the owner, outside this item

- The in-place path of the container runs node bodies as managed code in the Editor: about 41 ms per step at
  500 agents against 0.7 ms in jobs (P12-002 measurement), above a 33 ms tick. A consumer must use the job
  path at that load in the Editor; a Player was not measured. This is the open item "Burst only in the
  agent-frame job", not started without the owner's word.
- The read's owner questions 1–4 (additive ABI change or version bump; copy per step as a principle; a
  `Step(tick, JobHandle)`; folding "another squad's row" into the same ADR) are answered by this decision only
  as far as "not now"; they are listed in the ADR as open.
