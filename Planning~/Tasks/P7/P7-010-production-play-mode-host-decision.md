# P7-010 — Production Play-mode host decision

Status: `Done`

## Objective

Decide, on paper, the design for a real production component that drives a compiled tree's
lifecycle (reference or native) during actual Unity Play mode — a `MonoBehaviour`/`ScriptableObject`-driven
host that owns a tree instance's per-frame update, exposes it to `P3-010`'s debugger attachment and
`P7-007`'s trace production the same way `SchedulingPolicyDriver` already does for benchmarks. This
is the single most-repeated disclosed gap across the whole project: `P3-009`, `P3-010`, `P3-011`,
`P6-008`, and `P6-012`'s own gate session each independently found "no production code anywhere
drives a native or reference lifecycle machine during Play mode" and worked around it with a
self-driven or benchmark-driven pattern instead. Phase 7's own "production hardening" cannot
honestly claim in-game debugging, trace inspection in a real game, or native hot reload mattering in
practice without this existing.

## Depends on

- `P2-019` (native scheduling; the host must drive one of the four accepted policies, not invent a
  fifth).
- `P3-010` (native debugger attachment; the host is the missing thing every prior card attached a
  debugger to a self-driven substitute for).
- `P5-010` (Phase 5 gate; hot reload's own disclosed native-backend gap is downstream of this
  decision, per `P7-011`'s own dependency on this card as informing context, not a hard blocker).

## Required reading

- `Planning~/Evidence/P3-010/README.md`'s own Decision section (the first place this gap was found
  and explicitly scoped around, by owner direction).
- `Planning~/Evidence/P6-008/README.md` and `Planning~/Evidence/P6-GATE/phase7-inputs.md` (every
  later restatement of the same gap).
- `Documentation~/architecture.md`'s assignment of runtime responsibilities (confirm what layer a
  Play-mode host belongs in — likely a new `Runtime/` or `Authoring/` component, not `Editor/`,
  since it must work in a real Player build, not just the Editor).
- `Runtime/Scheduling/SchedulingPolicyDriver.cs` (the closest existing analog — a real driver loop,
  just never wired to `MonoBehaviour.Update`/a scene component).

## Allowed changes

- `Spikes~/ProductionPlayModeHost/` (new, disposable) — proves the recommended host design against
  a real compiled tree ticking across real Play-mode frames (Editor Play mode is an acceptable
  proxy for the spike; a full Player-build proof is this card's own implementation follow-up's job,
  not the spike's).
- `Planning~/Evidence/P7-010/`.
- One proposed ADR.

## Forbidden changes

- Any production change to `Runtime/`, `Authoring/`, `Editor/` — this card decides on paper.
- Assuming the host must support every accepted scheduling policy on day one — the ADR may scope an
  initial version to `Immediate`/`Budgeted` only and disclose `BatchedJobsSameFrame`/`PipelinedJobs`
  as follow-up, if a real reason exists (state it, don't hide it).

## Deliverables

- A decision on the host's shape: a `MonoBehaviour` component authors attach to a `GameObject`, a
  `ScriptableObject`-driven singleton, or something else — argued from Unity's own lifecycle
  guarantees (domain reload, scene load/unload, `Update`/`FixedUpdate` ordering), not convenience.
- A decision on how the host exposes itself to `P3-010`'s debugger attachment and `P7-007`'s trace
  recorder — both were built to attach to "a caller-owned session"/"whatever already drives the
  machine," per their own accepted designs; this card confirms the host satisfies that shape rather
  than requiring changes to either.
- A disposable spike proving a real compiled tree ticks correctly across real Play-mode frames
  through the recommended host, with a debugger attached mid-session using `P3-010`'s own
  unmodified `AttachSession` API.
- A proposed ADR.

## Acceptance criteria

- The spike runs in real Play mode (not `-batchmode`), observed live via Unity MCP against the open
  Editor, matching every other decision card's own verification bar in this project.
- The ADR states plainly which scheduling policies the initial host design supports and which are
  explicit follow-up.
- `P3-010`/`P3-011`'s own already-accepted public APIs require zero changes to attach to the new
  host — if a change turns out to be genuinely necessary, it is escalated per
  `Planning~/DECISION_BOUNDARIES.md`, not made silently inside this card.

## Required verification

```text
Verify-Static.ps1
disposable spike: real compiled tree ticking across real Play-mode frames, live via Unity MCP
debugger-attachment proof using P3-010's own unmodified AttachSession API
```

## Handoff notes

- A future implementation card builds the host into production per this ADR.
- `P7-011` (native-backend hot reload decision) should read this ADR before deciding whether native
  fresh-instance construction needs host cooperation or can remain self-contained.

## Outcome

Done, accepted. `ADR-P7-010` (`AIBT-034`) decides the shape (one `MonoBehaviour` per tree instance,
never a `ScriptableObject` singleton), location (`Runtime/Integration/`, inside `AIBT.Runtime` —
`AIBT.Editor`/any `*.Tests` assembly structurally ruled out, see below), initial scheduling-policy
scope (`Immediate`/`Budgeted` only; `BatchedJobsSameFrame`/`PipelinedJobs` disclosed follow-up
needing a population-level coordinator), attach/trace shape (host owns its own
`NativeTraceChannelOwnerV1`, zero changes needed to `P3-010`'s debugger session or `ADR-P6-015`'s
recorder shape), update timing (`Update()`, not `FixedUpdate()`), and lifecycle
(`Awake()`→`OnDestroy()`, proven leak-free). A real, reproduced Unity restriction was found and is
load-bearing for the location decision: Unity refuses `AddComponent` for any script in an
`"includePlatforms": ["Editor"]` or `"optionalUnityReferences": ["TestAssemblies"]` assembly,
independent of each other — isolated across two asmdef revisions before the spike could even run. A
disposable spike (`Spikes~/ProductionPlayModeHost/`) ticked a real compiled tree in real Play mode
(via Unity MCP) to **32,295 real `Update()` calls** with zero errors, and proved `P3-010`'s own
unmodified `NativeExecutionDebuggerSession.Attach`/`TryReadTrace` correctly reads the host's live
trace channel mid-session with zero perturbation (`TotalUpdates` identical before/after), including
correctly reporting a real, expected capacity-driven fault. No production file under `Runtime/`,
`Authoring/`, or `Editor/` was touched — decision-only, per this card's own Forbidden-changes clause.
**No implementation card exists yet** — this remains a real, disclosed gap against `scope.md`'s
"Production-ready editor and debugger" 1.0 criterion (see `P7-016`'s own gate evidence). See
`Planning~/Evidence/P7-010/README.md`.

**Bookkeeping note (found and fixed during `P7-016`'s gate review):** this card's own `Status`/
`Outcome` were never updated after its real, accepted completion — evidence existed, work-items.json
already said `done`, but this file stayed `Draft` with no Outcome until now.
