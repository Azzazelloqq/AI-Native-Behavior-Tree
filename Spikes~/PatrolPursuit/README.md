# Patrol / pursuit research

A small consumer scene with three real native AIBT hosts. This spike is application code,
not a new built-in node library or a navigation system. The live evidence/report is under
Planning~/Evidence/P8-012. User instructions in Russian: USER_GUIDE.ru.md.

## Open the prepared consumer

Open C:/UnityProjects/AIBT-PatrolResearch in Unity 6000.7.0b1. Open
Assets/PatrolResearch/PatrolResearch.unity and press Play. The scene runs continuously;
the headless evidence probe is opt-in and does not stop ordinary user play.

- Blue: patrols four waypoints, acquires the nearest visible orange/green target inside
  5.2 m / 130 degrees, locks that target, pursues at 3.2 m/s until separation exceeds 9 m,
  then returns at 2.6 m/s to its suspended patrol destination and resumes at 1.8 m/s.
- Orange: follows a fixed loop at 8 m/s, intentionally fast enough to demonstrate escape.
- Green: chooses random destinations after arrival, using seed 1227 for reproducible research,
  and walks at 1.4 m/s. Both orange and green can be pursued. A slower target may remain pursued
  indefinitely if it never escapes; there is no combat/capture timeout in this scenario.
- Yellow cone: current geometric visibility. Yellow !: an active locked pursuit, including
  pursuit outside the cone until the larger give-up distance is exceeded.

The map is empty: movement is direct, with no obstacles, NavMesh, collision avoidance or
occlusion. Restart Play to reset routes and the random seed.

## Tree and API responsibilities

Patrol priority is ReactiveSelector [Chase, Failer(Return sequence), continuous Move]. Chase
owns target acquisition/loss and an Instance-lifetime target ID. The return sequence reads the
tree-owned return-required flag, moves to the route destination, and clears the flag. Failer
lets the selector fall through to patrol when return completes, keeping the root Running.

AIBT's ReactiveSelector aborts/exits its previous running subtree on every update before
reevaluating. An inner MemorySequence does not preserve Activation memory against this abort.
Chase deliberately uses Instance memory and clears its lock on distance loss. These are the
existing library semantics, not a production behavior change introduced by the example.

ResearchActor samples transforms, FOV, candidate ID, offsets and delta time. It resolves typed
external write handles once, stages facts in SampleInputs, writes only in TryFlushInputs and
applies committed movement/mode effects in Receive. It does not choose patrol/chase/return.
ResearchScene creates one ProductionTreeWorld, initializes the three participants and displays
their native mode effects. Each actor owns its ProductionTreeProgram, stops/unregisters its
host before disposing that program, and uses the explicit generated PatrolCatalog.

## Rebuild from source

1. Create a fresh Unity consumer with a local package dependency on this AIBT checkout.
   Include Unity modules imageconversion and physics (1.0.0). The installed research Editor
   is 6000.7.0b1; an exact stable-version/Player acceptance claim is not made here.
2. Copy Nodes, Catalog, Runtime, Editor and Trees to Assets/PatrolResearch; allow compilation
   to finish. The source generator requires separate node and catalog assemblies.
3. Start the consumer Editor. Use Tools~/aibt.ps1 from the package, always with --project.
   Run `runtime catalogs`, then `tree inspect research.patrol` and `tree validate research.patrol`.
4. For patrol, runner and wander, run `tree prepare research.<name> --catalog
   AIBT.PatrolResearch.PatrolCatalog --output Assets/PatrolResearch/Trees/<name>.runtime.asset
   --expected-hash <contentHash> --dry-run`, with `--permissions Compilation`.
   Inspect the dry-run and repeat without --dry-run to create the asset. For an existing asset,
   add --overwrite --expected-asset-hash <existingAssetHash from the dry-run>. GUIDs are preserved.
   Mixed inspect/prepare scripts need --permissions Read,Compilation because an explicit grant
   replaces the default Read grant. The CLI starts the Editor bridge on demand.
5. Run AIBT > Research > Create Patrol Scene, open the saved scene and press Play.

Edit behavior parameters in Trees/*.aibt.json or the graph editor, save and prepare the affected
runtime asset before the next Play. If a native node/catalog contract changes, prepare all three
assets against the new catalog. Change positions, waypoints and sight parameters in the scene's
ResearchActor components. New scene generation resets that demo layout; it is not needed to play.

## Evidence probe

The owned headless fixture starts with -batchmode -executeMethod
AIBT.PatrolResearch.Editor.ResearchSetup.Open. Atomically publish Temp/research-create.request
or Temp/research-run.request (write a .pending file and rename it after closing). The run request
opens the saved scene and opts into 45 seconds of observation, captures actual scene-camera
renders to Temp/PatrolEvidence, writes report.json and exits Play. research-quit.request closes
only this fixture Editor. These fixture files are not required in ordinary user play.

During Play, CLI `runtime hosts` and `trace read <instanceId> --limit 512` expose real host/program
identities. The default trace is bounded and can overflow well before a complete gameplay cycle;
complete=false/faulted/droppedCount describe diagnostic history loss, independently of native
host failure. The scene observer verifies positions and transitions separately.
