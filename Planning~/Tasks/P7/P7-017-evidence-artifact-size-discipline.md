# P7-017 — Evidence-artifact size discipline

Status: `Done`

## Objective

`P7-003`'s committed real Profiler capture
(`Planning~/Evidence/P7-003/profiler-capture-deep-sequence-selector-traversal.data`, 31 frames) is
22 MB — larger than any other binary evidence artifact ever committed in this repository (the prior
largest, `P4-008`'s WebGL build under `Benchmarks~/Phase4/Platform/Web/Results/`, is 8.3 MB). The
owner flagged this size during `P7-003`'s own review as worth a dedicated look rather than deciding
unilaterally in the moment (`P7-003` committed the file as-is by explicit owner direction; this card
investigates whether that should become the standing approach or whether evidence conventions need a
size policy). This is a housekeeping/policy question, not a `P7-003` defect — nothing about the
capture itself was wrong.

## Depends on

- `P7-003` (done — the concrete artifact and its own evidence README are this card's starting
  point).

## Required reading

- `Planning~/Evidence/P7-003/README.md`'s "Real Profiler capture" and "Scope and limitations"
  sections — how the 22 MB file was produced (31 frames via
  `UnityEditorInternal.ProfilerDriver.SaveProfile`) and why it is already the *smaller* of two
  captures taken that session (a first, unbounded capture reached 1.46 GB before being discarded).
- Every other binary/large evidence artifact already committed under `Benchmarks~/Phase*/` and
  `Planning~/Evidence/*/` (sizes, formats, and whatever `.gitignore` rules already exclude logs) —
  establishes the actual current baseline this card should reason from, not an assumed one.
- Whether this repository (or the parent `C:\UnityProjects\Modules`) has, or should have, a
  `.gitattributes`/Git LFS configuration — none exists today, confirmed by this card's own
  predecessor session; deciding whether to introduce one is explicitly in scope here since it
  changes how *any* future large artifact should be committed, not just Profiler captures.

## Allowed changes

- A repository-level evidence/benchmark artifact size convention, written down where other
  standing conventions already live (candidates to evaluate, not a predetermined answer:
  `Documentation~/benchmarks.md`, `AGENTS.md`, or a new short doc under `Planning~/`).
- If the investigation concludes a smaller capture technique is preferable (e.g. fewer frames,
  `Deep Profile` truly off confirmed, trimming to only the relevant marker categories via
  `ProfilerDriver.SetMarkerFiltering`, or a different export format), a documented, reusable
  recipe — not a silent rewrite of `P7-003`'s own already-accepted evidence.
- Git LFS / `.gitattributes` configuration, only if the investigation concludes it is the right
  fix — this is a repository-wide decision, escalate before applying if so.

## Forbidden changes

- Deleting, replacing, or shrinking `P7-003`'s own already-committed evidence file as a side
  effect of this card — that file was committed by explicit owner direction; if this card's own
  conclusion is that it should be smaller, that is itself a decision to bring back to the owner
  before touching it, not something to apply retroactively.
- Introducing a hard repository size limit or CI gate without owner approval — this card may
  propose a threshold; it does not get to adopt one unilaterally, per this project's standing
  `USER_ACTIONS.md`/`DECISION_BOUNDARIES.md` discipline for exactly this class of decision.

## Deliverables

- A short written recommendation: keep current practice (evidence artifacts committed as-is,
  case-by-case owner awareness at commit time — what `P7-003` already did) vs. adopt a documented
  convention (a size guideline, a trimming recipe, Git LFS, or some combination).
- If a trimming recipe is recommended, a real demonstration that it still preserves genuinely
  useful evidence (marker hierarchy and per-call cost still inspectable), not just a smaller file.

## Acceptance criteria

- The recommendation is grounded in the actual current state of committed evidence across the
  repository (sizes, growth trend across phases), not assumed from `P7-003`'s one data point.
- Any proposed size threshold or convention is put to the owner as a decision, not adopted
  silently.

## Required verification

```text
Verify-Static.ps1
If a trimming recipe is produced: a real re-capture demonstrating it, compared against P7-003's
  own existing evidence for what is gained/lost.
```

## Handoff notes

- Not required for the Phase 7 integration gate (`P7-016`) — a cross-cutting housekeeping question
  discovered during `P7-003`'s own review, mirroring `P6-013`-`P6-021`'s own pattern of deferring
  cross-phase debt to its own card rather than deciding it mid-session.

## Outcome

Done. Recommendation, grounded in real evidence rather than the card's own (found-wrong) framing:
**no hard size threshold, no CI gate, no Git LFS adoption.** The card's own cited baseline
("`P4-008`'s WebGL build, 8.3 MB committed") does not exist — `P4-008`'s real build binaries (21 MB
APK, 6.1 MB WASM) are excluded from git entirely via pre-existing, targeted `.gitignore` files
already living in their own `Results/` folders, a real precedent that predates this card. A fresh
repository-wide sweep found `P7-003`'s 22 MB capture is the largest tracked file by ~18x (next
largest: 1.2 MB), a one-off outlier with no growth trend — and that its real, compressed cost inside
git's pack is only 3.25 MB (6.8x compression), a materially smaller practical concern than the raw
file size suggests. A short addition to `AGENTS.md`'s "Quality gates" section now states the
principle this project already followed implicitly: regeneratable build intermediates are
`.gitignore`d, genuine evidence artifacts are committed even when large, and anything approaching
double-digit MB gets flagged to the owner at commit time. `P7-003`'s own file was not touched. See
`Planning~/Evidence/P7-017/README.md`.
