# Decision: shape of the indexed input channel (P12-002) — orchestrator, 2026-11-24

Under the owner's delegation of 2026-11-23. Independent read first: `indexed-channel-design-read.md` in
this folder (a reader given the code, the consumer's words and neutral questions; it was told the route,
not a shape). I read its report in full and checked its reasoning against the fact check
(`requests-fact-check.md`); the code lines it cites that I had read before agree with it.

## Decision

Build shape A of the read: one additive overload on `TreeGroup`

    TryBindInput<T>(InputChannel<T> channel, NativeArray<T> rows, NativeArray<int> rowOfSlot)

`rowOfSlot[slot]` names the row that the agent in that slot reads. The two-argument method and its
"row = slot" meaning are unchanged.

Semantics:

1. `rows`: created, any length (zero allowed), not tied to `Capacity`. The whole array is copied at the
   start of the step into a buffer the group owns. The cost follows the array the game binds, not the agents.
2. `rowOfSlot`: created, length at least `Capacity` (so it is rebound after `TryReserve`, as rows are
   today). The first `HighWaterIndex` entries are copied at the start of the step. Jobs never touch game
   memory; the game may write both arrays again as soon as `Step` returns; a step sees the values at its entry.
3. A negative index means "no row": the node reads zeros (the same as an unbound channel today).
4. An index at or beyond `rows.Length` in the slot of a live agent refuses the whole step in validation with
   `InvalidArgument`; nothing runs. Slots without a live agent are not checked (their staging gives zeros).
5. Wake on change is the existing canonical value compare, now through the index: an agent wakes when the
   value it would read differs from the value at its last think, whether the row changed or the index did.
   No wake from an index change that leads to an equal value. No new per-row state.
6. One routine chooses the row for the job path, the in-place path, wake and the trace, so in place and
   jobs are identical by construction.
7. Binding the same channel again with the other overload switches the mode.
8. The trace record and the MCP `read_agent_trace` output say which row was read, if that is an additive
   change that moves no pinned format; if it is not, it is left out and reported.
9. With the code: an amendment to ADR-P10-008 (two routes for group data: this one built, the Shared-scope
   context still the provisional slot; correct the stale `InputMissing` sentence at :143 and the stale
   header status), the consumer documentation, CHANGELOG, and regeneration of
   `Documentation~/generated/api-reference-runtime.md`.

## Why A and not the per-agent setter (B)

- It is the consumer's own second wording ("an input channel indexed by squad, with a per-agent squad index")
  and the model they already use for rows: arrays by `handle.Index`, written between steps.
- Their load is data-oriented (500 agents, jobs). A lets membership be computed in the game's own jobs, and
  the existing read check already refuses a step while such a job still writes the array. B is a managed
  call per agent per channel.
- Several channels share one index array with no extra API (needed anyway: a target channel with wake and
  an anchor channel without wake, see below). B needs a call per channel or a single per-agent index, which
  cannot express two grouping levels.
- One public member. B stays possible later on the same internal core, if the consumer says membership
  comes from managed code and changes rarely.

What A costs, accepted knowingly: there is no "unset" state other than a negative index the game writes,
and a reused slot keeps the previous occupant's index until the game writes it. This is the hazard rows
already have today (a reused slot reads whatever is in row i), so it adds no new kind of mistake; the
documentation must say it next to `TryAdd`.

## Why refuse the step on a bad index

Weighed against faulting only that agent (would need a new precise fault cause; the path that exists reports
`InvalidHandle`, which misleads, and would fault teardown dispatches too), zeros (hides the game's bug) and
clamping (wrong data). A bad index is the same class as a short rows array, which refuses the step today.
It is checked before anything runs, so the world is not left half-stepped, and the game sees it at once.
Open to revisit if the consumer says one bad agent must not stop the world (question 3 of the read).

## Not decided here, stated to the consumer

- Library nodes and trees that read blackboard keys cannot see these rows; only the game's own nodes with
  `[AibtInput]` can. If they need more, that is the Shared-scope route and a separate decision.
- Nodes cannot write squad facts.
- Reading another squad's row needs a node-context call (protected ABI): part of P12-003's question.
- Documentation must warn: a channel with wake on whose row changes every tick (a moving anchor) wakes
  every member every tick; keep such data in a second channel without wake.
- Traces keep 16 bytes of a value; a larger squad struct is shown truncated.

## Order

After P12-001 is accepted (one writer in the checkout, one Editor). Tests first, from the list of 18 in §5
of the read; every item of "Semantics" above gets a test with that item's number in its summary.
