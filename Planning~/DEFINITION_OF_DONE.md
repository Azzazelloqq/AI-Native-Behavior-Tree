# Definition of Done

A work item is done only when every applicable requirement below is satisfied.

## Scope and contracts

- All card acceptance criteria are met.
- Dependencies were merged before implementation.
- No unrequested feature or speculative abstraction was added.
- Normative specifications and accepted decisions remain satisfied.
- Public or persisted contract changes are versioned and explicitly authorized.

## Code quality

- Responsibilities and dependency direction follow `architecture.md`.
- Runtime code introduces no editor, MCP, LLM-provider, or DOTS Entities dependency.
- Burst paths contain no hidden managed fallback, reflection, virtual dispatch, or per-tick allocation.
- Errors are structured and stable where required.
- Names and comments express intent; comments do not compensate for unclear code.

## Tests

- Tests assert observable behavior and negative cases.
- New semantics have behavior cases or equivalent contract tests.
- Immediate and specialized executors share applicable expectations.
- Tests pass in the exact supported Unity toolchain.
- Platform claims are tested in Player builds on recorded environments.

## Performance

- Performance-sensitive changes include a relevant benchmark or a documented reason it is not yet measurable.
- Zero-GC claims are measured after warmup.
- Results record environment and raw samples.
- No threshold or platform default is introduced from a single workstation result.

## Documentation and repository hygiene

- Owned documentation, schemas, samples, and changelog entries match behavior.
- No broken local Markdown links or invalid JSON are introduced.
- No secrets, machine paths, caches, generated IDE files, or benchmark noise are committed.
- `git diff --check` passes and unrelated user changes are preserved.
- Handoff report is complete and independently reviewable.

## Final self-check

The agent explicitly asks:

1. Did I complete every required item?
2. Did I invent anything outside the requirement?
3. Is any claim unverified or stronger than the evidence?

Any discovered issue is corrected or reported before handoff.
