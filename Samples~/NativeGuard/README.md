# Native Guard

A native (Burst) tree built only from package nodes: `aibt.core.*` composites and the
`aibt.native.*` library (`Documentation~/native-node-library.md`). No Burst code is written for it.

1. Import **Native Guard** from AIBT's Package Manager Samples tab.
2. Run **AIBT > Samples > Native Guard > Create Demo**, then press Play.
3. The intruder (pink sphere) walks back and forth past the guard tower. While it is within 8 m the
   tower fires a flare (yellow); within 3 m a zap (red) knocks it back and sends it away. Both share
   one 1.5 s weapon cooldown, so after a zap no flare comes until the cooldown has passed. While the weapon
   cools down the tree watches. When nobody is near it patrols: it asks the game to open the gate,
   waits until the gate is open, lights one of three spots A, B, C behind the tower (picked at
   random), sweeps (cyan) and closes it, whistles (green) 40 % of the time, then rests a random
   1 to 3 s. The third line of the overlay lists the spots picked so far (`+w` = a whistle).
4. Randomness is seeded: select the tower and change **Random Seed** on its Native Tree Agent, then
   press Play again. Another seed picks another sequence of spots. The same seed picks the same
   sequence again while the runs line up: every finished patrol ends the run, and the agent restarts
   it as a new tree instance (the id shown on screen) with a new stream. An intruder that interrupts
   a patrol makes it start over, with new draws, in the same run. The intruder moves with frame time,
   so an interruption can come a little earlier or later; from that point the runs, and so the later
   choices, can differ even with one seed.
   How many patrols you see depends on where the intruder walks, so compare the order, not the count.
5. Open `native-guard.aibt.json` in **AIBT > Graph Editor** and select the tower during Play: the
   graph highlights the running node live.

## How it is wired

- `Catalog/NativeGuardCatalog.cs` is the whole opt-in: the project's catalog set lists
  `typeof(StandardNativeShard)`.
- The tree (`native-guard.aibt.json`) uses:
  - a retaining reactive selector as the guard: `zap`, `flare` and `watch` are checked at every
    update and take over from the patrol when they succeed or run;
  - `aibt.native.bool-is-true` on the facts `intruder-close` and `intruder-seen`;
  - `aibt.native.set-deadline` / `aibt.native.deadline-passed` on the Int64 key `weapon-ready-at`,
    shared by the zap and flare branches;
  - `aibt.native.wait-until-true` on the fact `gate-open` (timeout 3 s);
  - `aibt.native.random-int` (`pick-spot`, 0 to 2, both included) on the Int32 key `sweep-spot`,
    read back by `aibt.native.int-equals`; `aibt.native.random-chance` (`whistle-chance`, 0.4);
    `aibt.native.wait-random-microseconds` (`rest`, 1 to 3 s);
  - `aibt.native.signal` codes 1 (zap), 2 (flare), 3 (open the gate), 4 (sweep and close), 5 to 7
    (light spot A, B or C) and 8 (whistle).
- `Runtime/NativeGuard.cs` is the game side. It writes the facts with `NativeTreeAgent.SetFact` and
  handles the signals with `NativeTreeAgent.OnEffect("aibt.native.signal", (int code) => ...)`.

Native hosts do not re-evaluate observers today. The guard works because the reactive composites
check their conditions again at every update.
