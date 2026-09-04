# P7-031 implementation proposal

Status: Accepted by owner on 2026-09-04; implementation and verification in progress.
Prepared 2026-09-04. Existing uncommitted P7-029/P7-030 changes are preserved.

## Preparation findings

- All three findings remain present: MoveTo performs no containment check, the watcher guesses
  project/Logs/Editor.log, and start records a baseline after writes without requesting compilation.
- Check treats any later ScriptCompilation marker plus isCompiling=false as success, then hashes
  the current staged files. This does not prove those files were the input to that compilation.
  Merely replacing the log path or capturing an earlier offset is insufficient for the acceptance
  criterion excluding stale/unrelated success evidence.
- P6-009/P7-009 are Done in work-items.json. P6-009's evidence explicitly records narrowed
  completion and verification; its stale Draft header has been reconciled with that evidence.
- The bridge calls McpToolDispatcher from its background TCP thread. A compilation request and
  Editor event handling must run on the main thread; do not call Unity APIs directly from start.
- The current Assets and AIBT-Generated directories are ordinary directories on this workstation.
  Destination containment still needs explicit link/reparse rejection, tested in disposable fixtures.

## Recommended compile contract

Retain non-blocking start/check, but identify a specific attempt rather than a raw log offset:

1. start captures the full current staged-content hash and creates one attemptId. It immediately
   returns pending plus attemptId. The one pending attempt belongs to the existing one staging
   slot, not to a general job queue.
2. A main-thread Editor hook imports the staged writes and explicitly requests compilation after
   any already-running compilation finishes. Even if Auto Refresh already compiled the files,
   start requests an observable new attempt; it never waits for an unrelated future compile.
3. Persist the attempt identity/hash/progress/result in a small project-local Library record,
   so the TCP request and domain reload need not stay alive. Do not persist authoring data or
   create a new tree/schema format. Serialize access between the bridge and Editor callbacks.
4. Track the requested compilation using Unity compilation events. Read Application.consoleLogPath
   for supporting diagnostics only; a guessed path or unrelated log marker cannot establish success.
   An unavailable/rotated log is reported explicitly; it is never silently replaced by another log.
5. check takes attemptId and returns pending/still-compiling, compiled with the captured contentHash,
   or failed diagnostics. Repeated checks after completion return the same result. Verify that the
   current staged hash still matches the attempt before claiming success. Modified staging,
   superseded/unknown attempts, lost state or an Editor restart require a fresh start with a clear
   structured error, rather than indefinite polling or fabricated success.
6. Keep the existing test/apply hash and registry checks. Their success does not become a
   client-supplied boolean. No synchronous compilation wait inside a request.

The tool names and generate/preview/tests/start/check/test/apply sequence stay unchanged. Add
attemptId to the server check arguments and responses. A legacy check carrying only
logPositionBefore is insufficient evidence and must receive a structured instruction to start
again/use attemptId; do not silently accept it as proof. Update server descriptions, recipe source
and generated output together. Existing diagnostic categories should be reused where applicable.

Alternative considered: retain raw log offsets and request compilation after start. This repairs
the missed-event race but still needs a trustworthy association with the staged content and the
specific completed attempt. The single-attempt record makes that association explicit.

## Destination behavior

- Treat destinationPath as Assets-relative. Reject absolute/rooted/drive-relative paths,
  whitespace-only values, escape traversal and sibling-prefix tricks before mutation.
- Normalize both separator forms, derive the absolute path, and compare with the Assets prefix
  including a directory separator using the filesystem's casing rules. Do not allow Assets itself.
- Reject existing links/reparse points on the destination ancestry and the staged source path;
  do not resolve them as permission to move outside Assets. Retain existing-destination rejection.
- Validate before directory creation or moves; rejected requests must leave source/destination
  unchanged. Preserve the existing asmdef and registry workflow.
- This is validation for the supported single-client workflow, not a claim of protection against
  another OS process maliciously replacing directories between validation and File.Move.

## Implementation and verification plan after agreement

1. Add behavior tests for valid nested destinations and all rejected path variants, including a
   junction/symlink fixture where supported. Assert no source or destination mutation on rejection.
2. Implement normalized containment and explicit reparse rejection in the existing staging path;
   map rejected arguments to structured diagnostics before applying files.
3. Add deterministic compile-attempt tests for fast completion before check, compilation starting
   after start, compilation already active at start, errors, changed staging, domain reload,
   repeated check, superseded attempts, and non-default/unavailable log paths.
4. Implement only the one-attempt tracker and main-thread Editor hook required by this workflow.
   Keep any narrow helper in MCP/NodeDevelopment; no transport-wide refactor or generalized jobs.
5. Update server arguments/descriptions and recipe generator, regenerate documentation, build
   the MCP server, then run focused and full Unity EditMode tests plus static verification.
6. Run the whole recipe in a disposable fixture through real compilation/domain reload and apply.
   Preserve any pre-existing staged files; verify the applied path, content-hash rejection and
   registry rejection. Record exact test counts and live evidence. No commit/push.

## Decision requested

Implementation observation (2026-09-04): Unity 6000.5 reports already-current assemblies through
assemblyCompilationNotRequired, including after CleanBuildCache. The implementation accepts that
explicit compiler event for the requested attempt and still requires both staging assemblies.
Any rebuilt assembly requires reload before exposing success. A normal explicit compilation
request suffices; no global cache purge or synthetic source change is needed.

Approve attemptId-bound start/check with an explicit compile request and one Library-backed
attempt record, plus its narrow helper/server/documentation scope. This resolves P7-031's explicit
protocol gate and Planning~/DECISION_BOUNDARIES.md's API/persistence boundary.
