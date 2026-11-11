# P8-011 — Complete the agent consumer workflow

Status: `Review`

## Authorization and plan

Owner approved the six-point proposal on 2026-11-06: consistent project catalogs and explicit
backends, UPM documentation/resources, First Agent discovery, native preparation commands,
self-describing operation arguments, meaningful node behavior checks and live trace access.
Reuse the owner's authorized P8-009/010 working implementations without promoting their Review
acceptance states. Completed foundations: P6-005/006/007/011, P7-008/009/031/039/041.

1. Reproduce First Agent discovery in the disposable consumer. Use one project contract snapshot
   for discovery, editing and validation; retain explicit reference/native execution boundaries.
2. Resolve the package root independently of embedded installation; publish workflow, recipes,
   integration guidance and a project-aware catalog without writing into registry package caches.
3. Expose native catalog selection/preparation, operation argument schemas and read-only live
   host/trace snapshots through the shared dispatcher and both external adapters.
4. Replace inconclusive node test generation with behavior checks that exercise generated native
   dispatch using explicit inputs and expected outcomes; never infer expected outcomes from runs.
5. Verify both CLI/MCP in a clean file-package consumer using First Agent: find, edit, validate,
   prepare, run, assert terminal behavior and inspect the matching trace. Run regressions/static
   checks and record unsupported environments separately.

Owned: Editor, MCP/MCP~, relevant Authoring integration, Tests/Editor, Samples~/FirstAgent,
Tools~/Verification/P8, Documentation~, README/CHANGELOG, planning/evidence. Preserve existing
changes. No runtime VM/scheduler/ABI/format changes, package version changes or publishing.

## Acceptance

- First Agent's native node is visible consistently and never silently substituted with a
  reference behavior. Reference-only requests explain unsupported native leaves.
- Native tree edits remain atomic/hash-checked; contracts are shared with the graph.
- UPM resources and generated project documentation work outside Assets/AIBT.
- Native preparation has explicit catalog/output, source preconditions and safe overwrite rules.
- Command schemas describe actual handler arguments, permissions and side effects.
- Native behavior checks distinguish expected status/callback/effects from dispatch proof.
- Trace snapshots identify a specific live host/program and expose dropped/incomplete history;
  reading never stalls jobs or changes execution.
- Full isolated regression and both adapter walkthroughs pass on the installed Editor.

## Handoff

Task ID: P8-011. Outcome: implemented and verified on Unity 6000.7.0b1.
Branch: feature/aibt-consumer-workflow; no commit/push.
Changed: common catalog/validation and graph rebind, package-aware docs/resources, operation
contracts and native prepare/trace adapters, native expected-outcome case generation/execution,
runtime-capable applied nodes, regression/live/Player fixtures, instructions and evidence.
Verification: full isolated EditMode 1514/1514; external transport 16/16; both .NET builds clean;
real CLI/MCP First Agent plus custom-node lifecycle; Windows IL2CPP Player Success with the
expected compiled program hash/position; static 7 schemas/157 work items and diff whitespace.
See ../../Evidence/P8-011/README.md and its machine-readable results.
Deviations: none; explicit native/reference and bounded-trace limits are preserved. A new public
Authoring policy projection avoids duplicating scope/warning validation across assemblies.
Normative decision: AIBT-044. Runtime formats/lifecycle/scheduling were not changed.
Known limits: exact Unity 6000.5.8f1 is unavailable; manual newcomer UI acceptance remains separate.
Expected-outcome node checks do not cover async, registered/fixed-byte or multi-update behavior.
Recommended next acceptance: supported baseline Editor and a human first-time authoring walkthrough.
