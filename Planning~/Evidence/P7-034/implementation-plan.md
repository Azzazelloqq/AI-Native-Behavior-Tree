# P7-034 implementation plan

Status: approved by owner on 2026-09-07; implementation resumes after P7-041.

## Gameplay loop

One reactive priority tree is used by every agent:

```text
danger within range -> flee
otherwise beacon sensed -> attack in range with cooldown, otherwise pursue
otherwise -> patrol
```

The four Tree-scope facts are written only through P7-039 at a `ProductionTreeWorld` idle barrier:
`agent-position`, `beacon-position`, `danger-position`, `patrol-point`. None has a node writer.
The Unity presenter consumes P7-041 typed committed effects (`move-intent`, `state-intent`, `attack`)
and never evaluates or mirrors the tree's decisions.

## Sample node set

| Node | Contract | Reuse in tree |
| --- | --- | --- |
| `within-radius` | Observer condition, two `Float3` reads, configured `UInt32` millimetre radius, trivial | threat, sensor, attack range |
| `steer` | Action, two `Float3` reads, emits `Float3` move + `Int32` state effects, low | patrol, pursue, flee |
| `attack-with-cooldown` | Action, instance-lifetime `Int64` clock memory, emits typed attack/state only when eligible, low | attack |

The current public ABI accepts `Bool`, `UInt32`, `UInt64` and generated handles for configuration
fields, not `Float32`. Radius is therefore explicitly stored as `UInt32` millimetres; no private
ABI access, sample-only float wrapper or hidden C# decision is introduced.

## Delivery

1. Ship the importable `Samples~/SwarmArena/` package sample: source-generated shard/catalog,
   authored tree/layout, scene bootstrapper/presenter, scene and README.
2. Use `SchedulingProfile.Normal` unchanged for responsive agents and one explicit latency-tolerant
   PipelinedJobs sample profile; both register with the same world/scheduler budget.
3. Verify public kernel behavior, the scripted phase sequence and actual Unity Play Mode. Capture
   representative low/high population views, debugger attachment and clean teardown.

## Non-goals

No NavMesh, Entities, DOTS dependency, library-node promotion, generic compiled-tree asset pipeline,
benchmark conclusion or gameplay C# state machine.

## Showcase revision: readable population and scheduler telemetry

Approved by the owner on 2026-09-13 after inspecting the first runnable scene. The original 12-agent
capsule presentation establishes the public path but does not make scheduler behaviour or the package
boundary legible. This revision keeps the exact tree, facts, effects and gameplay rules while changing
only sample presentation and observability:

1. Agent hosts remain stable for a selected population throughout every scripted phase. Population
   changes only from an explicit control. The scene displays the fixed seed and created/destroyed host
   counts, so a Play-mode restart cannot be mistaken for phase-driven respawning. A requested live
   population change waits for every host's P7-039-safe idle boundary before joining/leaving the
   pipelined cohort; it never mutates an in-flight grouped wave.
2. Replace one Capsule Renderer per agent with a single sample-side instanced visual facade, grouped by
   the four committed effect states. Host GameObjects remain package integration owners; rendering no
   longer determines the showcased population limit.
3. Provide explicit `64`, `256` and `512` population controls. Do not silently claim any scale result:
   a release Player benchmark remains P7-035's responsibility.
4. Add a live overlay sourced from public scheduler/world/agent state only: state counts, profile
   cohort counts, committed attacks/logical updates over a rolling interval, registered host count,
   allocated/consumed scheduler budget, overrun flag and current-frame admitted/deferred dispositions.
   Include a clearly bounded 0.5-second Runtime FPS display average, visible in Editor and Player;
   label it live orientation, not supported-hardware or Player performance evidence.
5. Verify population identity is stable across phase changes, the live overlay matches scheduler frame
   entries, low/high/stress controls work, debugger attachment remains valid and scene teardown stays
   clean.
