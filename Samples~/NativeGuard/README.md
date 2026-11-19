# Native Guard

A native (Burst) tree built only from package nodes: `aibt.core.*` composites and the
`aibt.native.*` library (`Documentation~/native-node-library.md`). No Burst code is written for it.

1. Import **Native Guard** from AIBT's Package Manager Samples tab.
2. Run **AIBT > Samples > Native Guard > Create Demo**, then press Play.
3. The intruder (pink sphere) walks back and forth past the guard tower. While it is within 8 m the
   tower fires a flare (yellow); within 3 m a zap (red) knocks it back and sends it away. Both share
   one 1.5 s weapon cooldown, so after a zap no flare comes until the cooldown has passed. While the weapon
   cools down the tree watches. When nobody is near it patrols: it asks the game to open the gate,
   waits until the gate is open, sweeps (cyan) and closes it, then rests 2 s.
4. Open `native-guard.aibt.json` in **AIBT > Graph Editor** and select the tower during Play: the
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
  - `aibt.native.signal` codes 1 (zap), 2 (flare), 3 (open the gate) and 4 (sweep and close).
- `Runtime/NativeGuard.cs` is the game side. It writes the facts with `NativeTreeAgent.SetFact` and
  handles the signals with `NativeTreeAgent.OnEffect("aibt.native.signal", (int code) => ...)`.

Native hosts do not re-evaluate observers today. The guard works because the reactive composites
check their conditions again at every update.
