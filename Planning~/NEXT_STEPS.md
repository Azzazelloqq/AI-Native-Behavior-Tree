# Next development plan

Prepared 2026-09-04 against AIBT commit 8a874a4. Planning only; no implementation starts as part
of this request. The owner has authorized subsequent development and local commits without
repeated confirmation. Release, external access and product claims retain their own boundaries.

## Current state

Update after step 1 (2026-09-04): P7-026 is Done. Two Windows IL2CPP builds and both Player
payload probes passed; 1 to 100 trees added 36,028 shipping bytes, with byte-identical code.
See [evidence](Evidence/P7-026/README.md). P7-025 is the next planned scope.

Update after step 2 (2026-09-04): P7-025 is Done. The graph viewer supports normal navigation and
selection, loads stored layouts, keeps movement transient, and presents readable titles. See
[evidence](Evidence/P7-025/README.md). P7-023 is the next planned scope.

- P7-028 and review scopes P7-029 through P7-032 are Done. The package working tree was clean.
- Latest completed full host EditMode run: 1726/1729 passed. Two CodeGen PackageInfo assertions
  and one LocalSaveSystem autosave test remain failed; do not describe this as an all-green suite.
- Remaining Phase 7 cards: P7-023, P7-024, P7-025, P7-026.
- P0-005 is Review; P0-006 and P1-019 are Blocked in the tracker. Existing evidence requires a
  successful remote Unity CI run. Runner availability must be rechecked live, not inferred from
  historical USER_ACTIONS.md entries.

## 1. P7-026: measure Player build-size scaling

Preserve the owner's functionality-before-polish priority. Reuse the existing Windows IL2CPP
Player harness and verify its toolchain before changing anything. Use an isolated fixture/output
directory so the dirty Modules host settings and other modules remain untouched.

1. Inspect how generated node catalogs and compiled tree payloads reach the Player. Confirm that
   the selected build actually includes the trees; excluded or deduplicated source files cannot
   establish a scaling result.
2. Build the same target/settings/toolchain/commit with a baseline tree count and a 100x population
   using the same node types. Keep payload identity/count observable, without adding node types.
3. Record total shipped bytes, code/metadata bytes, serialized tree payload bytes, compression
   settings and BuildReport attribution. Confirm the included tree count in the resulting artifact
   or Player. Explain the delta in absolute bytes and bytes per additional tree.
4. Rebuild a baseline only if unexplained build variability prevents attribution. Do not introduce
   an arbitrary acceptable-growth threshold or generalize Windows results to Android/Web.
5. Publish a reproducible harness, size table and compatibility-matrix evidence. Keep regeneratable
   binary builds ignored according to the existing evidence convention.

Exit: two real comparable builds with verified inclusion and an explained delta. If the architectural
claim fails, record the mechanism and a bounded repair task rather than changing the benchmark.

## 2. P7-025: make the existing graph viewer usable

This precedes final P7-023 acceptance because the current graph does not load the supplied layout.
Keep semantic authoring read-only: no node creation/deletion, edge editing or new tree format.

1. Verify the current GraphView interaction API; add standard pan, zoom, selection and box selection.
2. Pass the semantic document path to the existing layout loader and use its positions. Use the
   existing fallback only when layout loading actually reports a fallback condition.
3. Preserve explicit NodeDocument.DisplayName. When absent, derive a readable presentation label
   in the Editor and keep the canonical TypeId visible in details/tooltips. Do not add a public
   NodeManifest title contract merely to format a label.
4. Keep any viewer node movement transient. Reopening uses the authored layout; this scope does
   not silently introduce layout writes or interactive semantic authoring.
5. Test layout loading/fallback and title behavior. Verify actual mouse interaction, selection,
   reopening and readable positions in the Editor, with screenshots and full regression.

Exit: usable read-only navigation, authored positions retained on reopen, no semantic changes.

## 3. P7-023: add readable production-node examples

1. Inventory the actual production node registry and documented backend limitations. Choose two
   small examples: a basic timed sequence and a composite/decorator/parallel example using only
   supported existing nodes. Do not invent combat/perception functionality to justify a sample.
2. Add semantic trees, companion layouts, meaningful display names and descriptions of expected
   behavior under Samples/. Keep existing golden fixtures unchanged.
3. Validate and compile each tree against its intended registry; verify its described behavior
   through the available execution path and disclose backend-specific limitations.
4. Open both examples in AIBT Graph, verify zero diagnostics and non-overlapping readable layout,
   and add concise onboarding links.

Exit: examples a user can open and understand, with reproducible behavior and visual evidence.

## 4. P7-024: publish the scheduler comparison

1. Reuse accepted Phase 4 datasets and the compatibility matrix; audit their measured scope first.
2. Produce a report/table/chart comparing Immediate, SameFrame Jobs and Pipelined at measured
   populations. Distinguish throughput, elapsed cost, latency and platform constraints.
3. Link every plotted value to its source JSON and retain relevant workload/hardware settings.
4. Start with the report only. Do not build a second live benchmark application or rerun datasets
   for better-looking numbers. Record insufficient evidence explicitly if encountered.

Exit: reproducible figures with conclusions no stronger than the existing measurements.

## 5. Integration and release readiness

At the first clean package-checkout verification, determine whether the two AIBT CodeGen failures
are host-layout-only or also reproduce in the intended package installation. Fix a reproducible
package defect in a separate bounded scope; do not skip or weaken the assertions. Track the
LocalSaveSystem failure separately from AIBT work and preserve other-module changes.

Recheck Windows runner access and CI state, run the real workflow when the infrastructure is
available, and verify that failures propagate. Close P0-005, P0-006 and P1-019 only after their
recorded gates pass. Complete a clean package verification, current API/format review and accurate
supported-platform/limitations inventory. A public 1.0 release remains a separate owner decision.

## Execution discipline

For each card: inspect current code and dependencies, record the bounded implementation plan,
implement, run behavior-focused checks and required live/build evidence, inspect the diff, update
the card, and commit only owned files. Run broader tests for code changes or integration risk;
content-only reports follow their own lighter gates. Report exact test counts and known failures,
without transferring historical results to newly changed code. No speculative runtime framework,
new node library expansion or unrequested editor-authoring features.
