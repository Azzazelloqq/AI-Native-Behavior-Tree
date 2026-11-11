# P8-012 — Patrol / pursuit consumer research

Status: Review, 2026-11-07. Working scene and bounded compiler fix verified on the installed
Unity 6000.7.0b1 / .NET 10.0.300 toolchain. No commit/push.

## Result

The persistent consumer is C:/UnityProjects/AIBT-PatrolResearch, using this local package.
Open Assets/PatrolResearch/PatrolResearch.unity and press Play. Ordinary play runs continuously
and does not record an unbounded observation history. Russian user instructions are in
Spikes~/PatrolPursuit/USER_GUIDE.ru.md and the consumer root README.ru.md. Reproducible source,
public CLI preparation steps, API responsibilities and the probe/checker are under
Spikes~/PatrolPursuit. The scene has three actual ProductionTreeHost instances and native trees:
blue patrol/pursuit/return, orange looping runner, green seeded random walker. Both other actors
can be acquired. Visibility is geometric (5.2 m, 130 degrees), give-up distance is 9 m. There are
no obstacles, NavMesh, occlusion, combat or capture semantics.

report.json is the real 45-second observation; verification.json is produced independently by
Verify-Observation.py. The first complete cycle detects the runner, loses it beyond 9 m and
returns to the suspended patrol waypoint. All recorded return transitions must exceed 9 m;
all acquisition transitions must be inside the sight range. Per-frame alarm consistency,
no terminal host results/failures, sampled map bounds and random-walker progress are checked.
The checker also rejects both earlier incomplete observations described below.

scene-1.png: initial patrol; scene-2.png: acquisition/!; scene-3.png: return without !;
scene-4.png: resumed patrol at the route. These are actual 1600 x 1000 scene-camera renders
captured in Editor Play, not desktop Editor screenshots or fabricated UI. All four were visually
inspected. This establishes scene presentation, not manual graph/newcomer authoring acceptance.

## Compiler defect and approved fix

before-fix-cli.json records that the original research.patrol JSON validated but native
preparation failed with AIBT3017: default-value alignment. The secondary trees prepared.
The canonical patrol keys began with candidate (4 bytes), dt (4), return-required (1), then
route-offset (12, alignment 4). Slot storage aligned the vector to 12; defaults placed it at 9.
Both generated-v2 defaults and the semantic projection appended bytes without alignment gaps.

The owner explicitly approved the production fix after this reproduction. The compiler now
pads each default to its existing declared alignment, using one private helper in the two
packing paths. No API, schema, supported platform, execution semantics or package version
changed. Reprepare affected assets; already valid aligned layouts retain their bytes.

red-tests.xml records 0/3 mixed-width cases passing before the fix, all failing with AIBT3017.
full-tests.xml records **1517/1517** passing afterward, zero failures/skips/inconclusive cases.
The three regressions assert explicit nonzero Bool/Float32/Float3/Int64 bytes, zero padding,
aligned native/semantic offsets and unchanged canonical bytes when declaration order reverses,
for Tree, Agent and Shared. Existing negative/compiler/native checks passed in the full suite.
The known full-suite shutdown warning still reports 112 Persistent allocations, also recorded
before this task in P8-011; it is not presented as a new scene regression or resolved here.

after-fix-cli.json proves that the exact original patrol source hash then prepared successfully.
final-cli.json records inspection, validation, dry-run and explicit hash-checked overwrite for
all final trees. Existing runtime asset GUIDs were preserved. Catalog contract changes require
preparing all its consumer assets; this was done after changing chase memory lifetime.

## Research findings

1. ReactiveSelector deliberately aborts/exits its running subtree on each update. An inner
   MemorySequence cannot protect its Activation memory against that abort. The first example
   therefore lost/switched targets early: initial-observation.json is a rejected attempt,
   despite superficially completing a cycle. The final Chase node uses Instance memory,
   acquires only when unlocked and clears its lock on actual distance loss. No core semantic
   change or MonoBehaviour behavior state machine was introduced.
2. With target locking corrected, a 5 m/s runner turned soon enough to stay within 9 m on this
   small map. target-lock-observation.json is another rejected attempt (no return). The final
   runner is 8 m/s to exercise the requested escape. Slower green targets can legitimately stay
   pursued indefinitely because they do not escape; no unsolicited timeout/combat was added.
3. Repeater is finite; continuous movement keeps Running instead. Failer on completed Return
   allows immediate fallback to patrol, avoiding a terminal root and implicit host restart.
4. Four custom native leaves plus shard/catalog assemblies and an explicit fact/effect adapter
   are needed here. The runtime/API works, but this is developer integration work, not a ready
   navigation kit. Scene graphics and the observer add separate demonstration code.
5. AIBT5006 requires direct callback-configuration binding fields. A selected local handle and a
   shared helper performing context access are rejected. Movement became one configurable node;
   only pure arithmetic is shared. This constraint deserves a visible authoring example.
6. Explicit CLI permission categories replace defaults. Combined inspect/prepare scripts need
   Read,Compilation; the CLI successfully starts the bridge without manual MCP activation.
7. Native trace is bounded: hosts-during-play.json and patrol-trace.json identify all real
   programs/hosts and disclose dropped history (complete=false, activeNodesReliable=false).
   Trace overflow/faulted is separate from host LastFailure=None. This trace is not claimed as
   a complete chase-cycle history; positions/modes are independently observed by the scene.
8. Generated manifest reads/writes are empty although behaviorCaseContract exposes bindings.
   Preparation uses generated binding metadata and works; manual graph discovery impact was
   not assessed. No incidental metadata/catalog fix was made.

A first headless request used a non-atomic file write and raced the fixture's deletion, logging
an IOException. Final fixture requests are published by closing a .pending file then renaming;
this is test-fixture transport, not an AIBT runtime failure. Final Play logs were checked.

## Reproduce verification

Run Tools~/Verification/Run-UnityTests.ps1 with -Scope Full -EnableGraphics against an isolated
file-package consumer with AIBT in testables. The owned regression project used here is recorded
in the task transcript; it is distinct from the scene consumer. Run
Tools~/Verification/Verify-Static.ps1 for schema, work-item, JSON, links and whitespace checks.
No performance benchmark is needed for this authoring-time alignment correction; no runtime
performance or zero-GC claim is made.

Follow Spikes~/PatrolPursuit/README.md to prepare and create a fresh scene. Publish a probe
request in its owned batch Editor; after 45 seconds run:

    python Spikes~/PatrolPursuit/Verify-Observation.py <ResearchProject>/Temp/PatrolEvidence/report.json

The exact stable 6000.5.8f1 Editor is not installed. No Player build, manual first-time graph UI
acceptance or comparison with third-party libraries was performed in this research. Conclusions
are limited to this working native consumer path and the observed integration friction.
