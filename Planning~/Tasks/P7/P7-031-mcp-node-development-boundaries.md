# P7-031 — Correct MCP node-development compile tracking and apply boundaries

Status: `Draft`

## Objective

Make the existing generate -> compile/check -> test -> apply workflow observe the intended
compilation and move staged node files only to the advertised Assets-relative destination.
This scope groups three findings in the same MCP node-development workflow.

## Revalidated findings

Reviewed 2026-09-04 against `66fa058`.

1. **P1 — apply_node accepts destinations outside Assets.** The public server tool describes
   `destinationPath` as relative to the project's Assets folder. `StagingSlot.MoveTo` only
   combines paths, checks whether the destination directory exists, then creates it and moves
   staged files. There is no normalized containment/rooted-path validation. Read-only path
   evaluation with `Assets` as root and `../ReviewOutsideAssets/Node` resolves outside Assets;
   rooted paths also bypass the intended base. No files were moved in the review. This is an
   application write-boundary defect, not proof of privilege escalation or arbitrary overwrite:
   existing destination directories are currently rejected and OS permissions still apply.
2. **P2 — Compile tracking reads an assumed log location.** `EditorLogCompileWatcher.EditorLogPath`
   constructs `<project>/Logs/Editor.log`. Fresh Unity inspection returned a different actual
   `Application.consoleLogPath` in the user's local Unity log directory. Custom log locations
   cannot be inferred from the project path either. Valid compilations can remain unobserved.
3. **P2 — The compile baseline can be captured after the relevant compilation.** GenerateNode
   writes scripts before AnalyzeAndCompileNode(start) records the log length; start does not
   request another compilation. If Auto Refresh finishes in between, check cannot see that
   completed compilation in the tail and can remain NotYetObserved until another compile event.
   This conditional race is proven by code ordering, not by a fresh file-writing reproduction.
   Fixing only the log location does not fix this ordering problem.

## Depends on

- P6-009 (node development tools).
- P7-009 (current staged catalog/test-node integration).

## Required reading

- `Documentation~/architecture.md`, `Documentation~/ai-and-mcp.md`.
- `Planning~/DECISION_BOUNDARIES.md`, P6-009 and P7-009 cards/evidence.
- `MCP/NodeDevelopment/StagingSlot.cs`, `EditorLogCompileWatcher.cs`,
  `McpNodeDevelopmentToolDispatcher.cs` in that directory.
- `MCP~/Server/NodeDevelopmentTools.cs`, `MCP/Documentation/McpRecipesDocumentGenerator.cs`.

## Scope

- The three MCP/NodeDevelopment implementation files above and their focused tests in
  `Tests/Editor/Mcp/NodeDevelopment/`.
- Server tool descriptions/signatures and generated recipe source/output only as required by
  the approved workflow contract; use the existing generators for generated documentation.
- `Planning~/Evidence/P7-031/`.

## Implementation plan and decision boundary

1. Establish a normalized Assets-contained destination before any directory creation or move.
   Use existing path validation conventions; inspect link/reparse behavior on the supported
   filesystem so lexical containment cannot silently authorize an external physical target.
2. Use the actual Editor log location if log tracking is retained. Agree how a compile request
   or observation baseline is tied to the current staged content before changing the protocol.
   Capturing before writes or explicitly requesting compilation after start are candidate
   approaches to assess, not permission to implement both or invent a generic job service.
3. Preserve non-blocking start/check behavior across domain reload. Update tool descriptions and
   recipes together with any approved argument/result change; test both timing orders.

## Forbidden changes

- No CodeGen rewrite, new node types, generalized task queue, broad transport refactor or
  synchronous wait loop inside one MCP request.
- No trust in a client-supplied success claim, removal of content-hash/registry checks, silent
  expansion of writable roots, or overwriting existing destination directories.
- No raw hand edits to generated documentation or unrelated staging packaging changes.

## Deliverables and acceptance criteria

- Valid nested destinations inside Assets succeed. Parent traversal escaping Assets, absolute
  paths, rooted/drive-relative variants and sibling-prefix tricks are rejected with a structured
  error before filesystem mutation. Exercise separators/casing appropriate to supported platforms.
  Rejected requests leave staged and destination files unchanged. Existing-destination rejection
  remains intact; cover links/reparse points where supported or document explicit rejection.
- Tests resolve/read a non-default Editor log, including unavailable-log handling, without silently
  substituting an unrelated project's log or claiming compilation success.
- Compilation completing before the client's next check, compilation beginning after start,
  failure and domain reload all have recoverable, observable outcomes for the intended staged
  content. Polling must not depend on an unrelated future compilation to notice an already
  completed relevant attempt; unrelated/stale log evidence must not yield false success.
- The documented generate/preview/tests/start/check/test/apply recipe works in a disposable Unity
  fixture. Confirm the applied path and compile result; preserve hash mismatch and registry rejection.

## Required verification

From the package root, with verification environment variables set:

```powershell
& './Tools~/Verification/Verify-Static.ps1'
& './Tools~/Verification/Run-UnityTests.ps1' -UnityPath $UnityPath -ProjectPath $ProjectPath -OutputPath $OutputPath -Mode EditMode -Scope Full
git diff --check
```

Run focused node-development tests first, including deterministic timing-order tests. Use Unity
MCP tests for an already-open project. Perform the live recipe in a disposable fixture/project;
do not demonstrate path escape by writing outside it. Record exact test counts, domain-reload
evidence and baseline failures separately. Build the MCP server if its code/signatures change.

## Handoff notes

This is a Draft implementation scope, not implementation authorization. Protocol/API changes
need approval under the existing decision boundary. No new end-to-end compilation run was
performed during this re-review; the current path mismatch and path resolution were checked live.
P6-009 is marked done in `work-items.json` but its card header still says Draft; reconcile that
existing dependency-status discrepancy before promoting this card to Ready.
