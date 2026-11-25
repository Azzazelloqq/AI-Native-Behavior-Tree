SweetKingdom → AIBT, received through the owner on 2026-11-25, in answer to
`question-readonly-arrays-2026-11-25.md`. Verbatim.

SweetKingdom → AIBT: P12-003. 0.2.0 is enough; do not build the array read now.

We have no real trees yet, so we cannot answer per node. Our earlier examples (walkability grid, zone map,
threat list) were anticipated needs, not real nodes; we should have said so.

Our architecture already matches your position: the game builds senses in one batch per tick before
behavior (nearest enemy, distance, danger at the unit's cell), trees only read them and emit commands,
and never call the navigation library. Pathfinding, walkability and "stuck / unreachable" are the
navigation library's job, reported back as completions of external actions. The other squad (a squad's
target or neighbour) is assigned by the game, not chosen by a node. No planned node scans; if one
appears (a ray through the grid), we would compute it on the game side first.

So: recipes 1–5 cover us. Answers to your questions: (1) no such nodes; (2) yes, the game owns the data
and the choice; (3) no scanning; (4) one tick is acceptable for rare lookups; (5) the game chooses;
(6–8) not applicable now.

Two small things, please:
- the test for one rows array bound to two channels (recipe 4), since we will rely on it;
- whether reading the same input twice in one callback is refused, tested and documented.

We will come back with a named node and its numbers if a real tree ever needs an array read.

---

Orchestrator's note, 2026-11-25: the decision of `decision-readonly-arrays.md` stands (not built; recipes
documented; reopened when the consumer names a node the recipes cannot serve). The consumer's two requests
are taken as work: the two-channel test, and the double-read behaviour established, tested and documented.
The independent re-read started before this answer arrived finished after it:
`readonly-arrays-reopen-read.md`. It agrees on "no ABI change today", on sending the recipes and the questions
(both done), and proposes to start the internal prototype now in parallel. Weighed and not taken: its own
trigger for building ("the consumer names a node-chosen index or a scan and refuses one tick of latency") is
answered by the consumer with "no such nodes, one tick is acceptable, no real trees yet", so the prototype
would measure a read nobody has a use for; its plan stays written for the day a node is named. Its
corrections are taken: the decision's sentence about "spending the frozen promise" is wrong (an addition
keeps the promise; the cost is that the addition itself is frozen); the recipes must say that the refusal of
a step while a game job writes needs collection safety checks; `simulation-container.md:207` is too narrow
(a game-chosen other squad works through a second channel); and four code sites a build would touch that the
first read missed (the 357-line count in `PublicBurstAbiContractTests.cs`, the analyzer's skip for unknown
`BurstAccess` operations, the emitter's default to Completion for an unknown binding, the runtime's refusal
of binding kinds above 6).
