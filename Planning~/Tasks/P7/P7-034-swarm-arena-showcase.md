# P7-034 — Swarm Arena gameplay and behavior-tree showcase

Status: `Draft`

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
