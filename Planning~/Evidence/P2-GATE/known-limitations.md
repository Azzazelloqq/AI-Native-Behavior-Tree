# Known limitations after Phase 2

Prepared 2026-08-18 for the `P2-025` review.

## Blocking the gate

- All Phase 2 work is uncommitted by repository policy, so no suite has yet run
  from the clean committed snapshot that `P2-025` requires.
- The remote `P0-005` Unity CI job remains queued for the owner-managed
  self-hosted runner. This was explicitly waived for starting Phase 2 and must not
  be reported as passed.

## Scope limitations carried into Phase 3 and Phase 4

- Only three fixed execution policies exist: Immediate, Budgeted, and
  BatchedJobsSameFrame. `PipelinedJobs` and `Auto` are unimplemented.
- No calibrated scheduling defaults, work estimates, or regression thresholds
  exist. The only Phase 2 measurements are microbenchmarks on one workstation and
  one headless Web run.
- The zero-GC claim covers twelve measured initialized windows on the reference
  workstation. Initialization, compilation, managed and reference nodes, and host
  materialization are unmeasured.
- Android evidence proves build and AOT compatibility, not device execution.
- Web evidence covers desktop Chrome and Firefox only. Safari, mobile browsers,
  and worker parallelism are unverified, and `OQ-004` still needs macOS access.
- Native capacities are fixed at compile time. Exceeding a capacity is a rejection,
  not a growth path, and the ergonomics of choosing capacities are unaddressed.
- The visual editor is a stub: `Editor/` contains only its assembly definition. The
  graph framework decision `OQ-005` is unmade.
- MCP tooling is a placeholder README under `Tools~/McpServer/`.
- Hot reload, state migration, and persisted execution state do not exist.
- Public API and persisted formats remain experimental below `1.0.0`.
