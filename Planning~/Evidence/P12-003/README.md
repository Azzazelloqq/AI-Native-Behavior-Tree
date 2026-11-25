# P12-003 evidence — read-only game arrays in nodes

Settled 2026-11-25: not built. The card `Planning~/Tasks/P12/P12-003-read-only-game-arrays.md` holds the
outcome, the shape to build if reopened (design E1), the condition, the prototype plan and the code sites.

## Records (copied on 2026-11-25 from `C:\UnityProjects\_aibt_acceptance\SweetKingdom\`)

The texts are unchanged. Their line references point at the code of `8396202` (the 0.2.0 package).

- `readonly-arrays-design-read.md`: the first independent design read.
  - §2: the designs A-G.
  - §3: the semantics to fix.
  - §4: Burst and safety.
  - §5: the prototype plan.
  - §6: the recommendation.
  - §7: the questions for the consumer and the owner.
- `decision-readonly-arrays.md`: the orchestrator's decision. Its sentence "an ADR records the considered
  designs" is replaced by the card: no ADR, because nothing in the architecture changes (see the card,
  "Decision record").
- `question-readonly-arrays-2026-11-25.md`: the recipes and the questions sent to the consumer.
- `answer-readonly-arrays-2026-11-25.md`: the consumer's answer ("0.2.0 is enough"), with the orchestrator's
  note on the second read.
- `readonly-arrays-reopen-read.md`: the second independent read, finished after the answer arrived. Its
  corrections were taken; its proposal to prototype now was not.

## Tests and runs

- Written on 2026-11-25 and not yet run: the Modules Editor is closed.
  - `TreeSimulationSquadRowsTests`: 13 cases, with the fixture assembly `AIBT.CodeGen.P12003SquadRows.Tests`.
  - The verifier case `ShortForm.SameInputTwice`.
- The verifier case was run alone outside the package (generator and verifier sources copied to a scratch
  folder, the dotnet SDK of 6000.6.9f1, no Editor). It passed, and with two different ids it failed, as it
  should.
- The full P2 matrix fails at baseline before reaching it (the contract compilation lacks the agent-frame
  sources). That failure is recorded in the card and not fixed here.
- Results of the Editor run go here when it happens.
