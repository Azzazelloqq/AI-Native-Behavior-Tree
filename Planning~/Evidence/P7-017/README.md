# P7-017 evidence-artifact size discipline evidence

## Result

Done. Recommendation: **no hard size threshold, no CI gate, no Git LFS adoption.** A short addition
to `AGENTS.md`'s existing "Quality gates" section instead writes down a principle this project
already follows implicitly but had never stated. `Planning~/Evidence/P7-003/`'s own committed
Profiler capture was not touched, per this card's own Forbidden-changes clause.

## The card's own "Required reading" claim was checked, not assumed — and found wrong

The card's own text cited "the prior largest, `P4-008`'s WebGL build under `Benchmarks~/Phase4/
Platform/Web/Results/`, is 8.3 MB" as the baseline `P7-003`'s 22 MB capture should be compared
against. A real `git ls-files`/`git ls-files -z | xargs du` sweep across the whole repository found
this baseline does not exist:

- `Benchmarks~/Phase4/Platform/Android/Results/android-build-calibration-v2-20260826.apk` (21 MB)
  and `Benchmarks~/Phase4/Platform/Web/Results/web-build-20260821/Build/
  web-build-20260821.wasm.unityweb` (6.1 MB) — the real, on-disk `P4-008` build outputs — are
  **not git-tracked at all**. Both are excluded by pre-existing, targeted `.gitignore` files already
  living in their own `Results/` folders (`.../Android/Results/.gitignore`: `*.apk`; `.../Web/
  Results/.gitignore`: `web-build-*/`). Only small derived summaries are committed:
  `android-build-calibration-v2-20260826.build.raw.json` (4 KB), `web-build-20260821-build.raw.json`
  (1 KB), and similar `.json`/`.log` result files, all under 10 KB each.
- This is a real, already-established, working precedent for exactly the problem this card was
  asked to investigate: **a regeneratable build binary never enters git; only the analysis output
  derived from it does.** It predates this card and was already in place when `P4-008` was
  committed — nobody invented it for this card.

## The real current state of committed evidence, swept fresh

A full `git ls-files -z | xargs -0 du -b | sort -rn` across the entire repository (not `P7-003`'s
one data point) found:

| Rank | File | Size |
|---|---|---|
| 1 | `Planning~/Evidence/P7-003/profiler-capture-deep-sequence-selector-traversal.data` | 22.2 MB (21.1 MiB) |
| 2 | `Benchmarks~/Phase4/CostCurves/Results/cost-curves-windows-editor-20260820.json` | 1.2 MB |
| 3 | `Benchmarks~/Phase4/Scheduling/Results/scheduling-windows-editor-20260819-165205.json` | 0.6 MB |
| 4 | `Planning~/Evidence/P0-001/batch-compile.log` | 0.3 MB |

**`P7-003`'s capture is the single largest tracked file in the entire repository by roughly 18x** —
not "somewhat larger than the prior largest" as the card's own framing suggested. It is a genuine,
isolated outlier. No growth trend exists across phases; every other evidence artifact, across every
phase from `P2` through `P7`, stays under 1.2 MB.

## `.gitattributes` / Git LFS — checked precisely, not assumed

A `.gitattributes` file **does exist** in `AIBT` (the card's "none exists today" claim is imprecise)
— but it is ordinary Unity line-ending/merge-driver configuration (`text=auto eol=lf`, `*.unity`/
`*.prefab`/`*.asset` merge drivers, a handful of `binary` markers for `.dll`/`.png`/`.jpg`). It
contains zero Git LFS tracking rules and zero rules for `.data`/`.apk`/`.wasm.unityweb` or any other
large-binary extension. No Git LFS is configured anywhere in this repository or the parent
`C:\UnityProjects\Modules`.

## The 22 MB figure overstates the artifact's real repository cost

`git verify-pack -v` on the AIBT submodule's own pack file shows this exact blob
(`9a6c6d56c5c5fbafc3a2c8788b31f0d8e362de59`) compresses to **3,252,690 bytes (3.25 MB) inside the
pack** — a 6.8x compression ratio, plausible for marker-name-repetitive binary Profiler trace data.
`git count-objects -v` reports the AIBT submodule's *entire* packed history is only ~6.19 MB — this
one blob accounts for roughly 52% of it, but the total remains small. The practical distinction:
**22 MB is the working-tree/disk-space cost on every clone or checkout; 3.25 MB is the actual
network/fetch cost.** Both are real, but the more alarming "22 MB" figure only applies to disk
space, not clone/fetch time — a materially less concerning picture than the raw file size alone
suggests.

## Recommendation, in full

No hard size threshold, CI gate, or Git LFS adoption. Instead, a short addition to `AGENTS.md`'s
"Quality gates" section (adjacent to its existing "Do not commit generated IDE files, benchmark
output..." line) states the principle explicitly:

1. A regeneratable build/benchmark intermediate is excluded from git via a local `.gitignore` in its
   own `Results/` folder — mirroring `Benchmarks~/Phase4/Platform/{Android,Web}/Results/.gitignore`'s
   own precedent, now written down rather than left implicit.
2. A genuine evidence artifact that is itself the analysis output (a Profiler `.data` capture, a
   benchmark JSON) is committed even when large — that is the entire point of `Planning~/Evidence/`/
   `Benchmarks~/*/Results/`.
3. Anything approaching double-digit MB gets flagged to the owner at commit time — exactly what
   `P7-003` already did — rather than committed silently.

`P7-003`'s own file is the accepted precedent for "a real Profiler capture is allowed to be this
size when it's the point of the evidence," not a mistake to walk back.

## Trimming recipe — not recommended as a mandatory practice, noted for future reference only

Given the real, compressed repository cost is modest (3.25 MB) and this is a one-off outlier with no
growth trend, a trimming recipe is **not** recommended as a standing requirement — no card acceptance
criterion demands one if none is being adopted as the fix, and no new capture was performed to
demonstrate one. For a future session that does need a smaller capture at a larger scale, two levers
are already visible in `P7-003`'s own evidence and worth knowing about: a bounded frame count (31
frames was already a deliberate choice — `P7-003`'s own first, unbounded attempt reached 1.46 GB
before being discarded) and `UnityEditorInternal.ProfilerDriver.SetMarkerFiltering`, available but
unused this time, to narrow a capture to only AIBT's own markers.

## Scope and limitations

- This is a policy/recommendation card; the recommendation above is proposed to the owner, not
  self-adopted — `AGENTS.md`'s own addition states an already-informally-followed principle rather
  than introducing a new enforced rule.
- No new Profiler capture was performed — not needed, since no trimming technique change is being
  recommended.

See `verification-results.json` for exact commands and results.
