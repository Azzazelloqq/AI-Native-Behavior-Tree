# P7-034 — Swarm Arena gameplay and behavior-tree showcase

Status: `Done`

## Objective

Ship an importable demonstration scene with understandable gameplay and visible agent behavior. The
same scene must exercise the global scheduler, custom profiles, production tree execution and live
debugging through public package APIs. It is a product/UX probe as well as a sample, not a decorative
benchmark shell.

## Showcase concept

A player-controlled target or beacon moves through an arena populated by a configurable swarm.
Agents patrol, acquire a target, pursue it, avoid a danger zone, attack with cooldown and return to
patrol. Visual state must make each behavior legible without opening the tree.

The final minimal gameplay loop is fixed during this card's planning pass. Add only mechanics needed
to demonstrate meaningful tree transitions and scheduling pressure.

## Depends on

- `P7-033` — global scheduler and custom profile API.
- `P7-039` — external live Tree-scope inputs for agents, beacon and danger data.
- `P7-041` — consumer-facing world frame/effect integration; the sample must not read internal
  command buffers or duplicate tree decisions in presentation code.
- `P7-023` — readable semantic/layout sample conventions.
- `P7-027`/`P7-030` — production host, lifecycle dispatch and live debugger.
- `P7-028` — current production stdlib and custom Burst-node authoring pattern.
- `P7-037` — production generated-node dispatch used by the sample agents.

## Required reading

- `Samples~/README.md`, `Samples~/ShowcaseTrees/` and `Samples~/BurstNodes/`.
- `Documentation~/production-host.md`, execution/scheduling documentation and P7-033 decision.
- Existing sample packaging/asmdef conventions and the current production node registry.

## Scope

- New `Samples~/SwarmArena/` sample with scene, runtime scripts, authored trees/layouts, custom
  profiles and sample-specific custom nodes.
- A deterministic simulation seed and scripted workload phases shared with P7-035.
- Lightweight rendering that can represent a large population without requiring DOTS Entities or
  making one expensive visual component per agent.
- Minimal controls for movement/beacon interaction, population selection and observable scheduling
  state. Presentation must distinguish gameplay state from benchmark metrics.

Initial sample-node candidates are `HasTarget`, `AcquireTarget`, `MoveTo`, `IsThreatened`, `Flee`,
`CanAttack`, `Attack` and `Patrol`. The implementation plan must reduce or reshape this list against
the actual public extension ABI. These names are not approval to add them to `aibt.stdlib`.

## Approved gameplay direction and implementation plan

The minimal loop is a single repeating reactive priority tree: threat guard -> flee; otherwise
beacon-in-sensor-range -> pursue until attack range -> attack subject to cooldown; otherwise patrol.
The beacon and danger zone are ordinary Unity presentation objects, but each agent receives its
own current `agent-position`, `beacon-position`, `danger-position` and deterministic
`patrol-point` through P7-039 Tree-scope external writes. No node writes those slots.

The sample uses only three gameplay-specific generated Burst node concepts:

- `aibt.samples.swarm.within-radius`: observer condition over two externally-fed `Float3` values;
  deterministic, no side effect, trivial cost. Its configured radius expresses target acquisition,
  threat and attack-range predicates rather than creating three duplicate conditions.
- `aibt.samples.swarm.steer`: Action reading agent/goal positions and emitting a `Float3` move
  intent plus an `Int32` visible state intent. Its configuration selects toward/away behavior, so
  patrol, pursuit and flee do not become separate library nodes.
- `aibt.samples.swarm.attack-with-cooldown`: Action with instance memory for its next permitted
  logical time; it emits visible attack/state effects only when eligible and otherwise remains
  Running. It is deterministic for the injected clock/inputs and has explicit low cost.

The final sample consumes P7-041's public typed effects through its Unity presenter. It does not
call `GeneratedTreeDispatchAdapterV2`, examine native command bytes, use NavMesh/Entities, or run a
shadow C# behavior state machine. It supplies two visible scheduling profiles sharing one global
budget, keeps the zero-configuration normal profile usable, and records a fixed seed plus scripted
low/high population phases for P7-035. A pipelined agent deliberately reacts to the most recently
staged world facts at its next idle barrier; it never races a live Transform write into an in-flight
native wave.

## Forbidden changes

- No promotion of gameplay nodes into the built-in catalog in this card.
- No private/internal API shortcut that a package consumer could not use after importing the sample.
- No NavMesh, Entities, third-party art/package dependency or large framework unless separately
  justified and accepted.
- Do not tune behavior or population to make one scheduler policy look good.
- No benchmark conclusion from Editor FPS or from a visually smooth recording.

## Acceptance criteria

- Importing the sample yields one documented scene that enters a complete patrol/react/attack/return
  loop in Play mode without project-specific setup.
- At least two custom scheduling profiles visibly affect cadence/latency while sharing the same
  global budget; the default path still works without manual policy selection.
- Authored trees and layouts open cleanly in AIBT Graph with readable names and no diagnostics.
- Sample-specific nodes execute through the real native production path and declare their data,
  side effects, determinism and cost contracts.
- Population changes do not change gameplay rules or deterministic scripted inputs.
- A selected live agent can be inspected through the existing debugger.
- Scene teardown disposes all runtime/native ownership cleanly.
- README explains controls, trees, profiles, expected behavior and how P7-035 obtains Player data.

## Required verification

```text
Verify-Static.ps1
focused observable-behavior tests for sample nodes and scripted phases
Run-UnityTests.ps1 -Mode EditMode -Scope Full
Unity MCP Play-mode proof of every gameplay phase and live debugger attachment
visual screenshots at representative low/high populations
clean scene exit with no native leak diagnostics
git diff --check
```

Standalone performance measurement belongs to P7-035; this card requires only a Player smoke build
if needed to prove the sample packages correctly.

## Outcome

Implemented the importable `Samples~/SwarmArena/` scene, authored reactive tree/layout, generated
sample shard/catalog and three public-ABI-only nodes. `SwarmArenaAgent` stages only P7-039 Tree-scope
facts and presents only P7-041 committed effects; it does not mirror decisions in C#. The scene shares
one scheduler between `Normal` and explicitly opted-in `PipelinedJobs` agents, has deterministic
scripted Patrol/Pursue/Flee/Attack/Return phases, low/high population controls and a live-trace menu.

Focused P7-041 bridge tests passed 6/6; the P7-037 generated lifecycle proof passed 12/12; the editor
assembly passed 436/436. Unity MCP Play Mode observed all five scripted phases, attached the existing
trace timeline to a selected agent, captured low/high population views, and completed a clean
no-capture teardown with zero Console warnings/errors. `Verify-Static.ps1` and package `git diff
--check` pass. Full host-project EditMode still has the known environment failures recorded below in
the P7-041 outcome; no Player performance conclusion is claimed (P7-035 owns it).

2026-09-13 showcase revision: the sample now starts at 64 agents and offers 64/256/512 controls,
one instanced visual facade grouped by committed state, and a human-readable public scheduler/world
telemetry overlay. A requested cohort change waits for all hosts to reach the P7-039-safe idle barrier;
live 256 and 512 transitions completed with zero host failures. The overlay includes a 0.5-second
Runtime FPS display average in both Editor and Player for live orientation, without presenting it as a
performance result.
