# `P7-001`'s stability proposal — the owner's actual decision

`Planning~/Evidence/P7-001/stability-review-proposal.md` was explicitly "a proposal for the owner to
decide, not a decision itself," with 5 open questions never answered — unlike `P7-002`'s own parallel
proposal, which the owner explicitly approved as-is on 2026-09-02. `P7-016`'s own acceptance criteria
requires "independent confirmation that `P7-001`'s... proposal... has a recorded owner decision — not
merely a proposal awaiting one." This file is that record, gathered live during the `P7-016` gate
session (2026-09-03) via `AskUserQuestion`, plus one item the owner asked to actually be *verified*
rather than merely labeled.

`P7-001`'s core recommendation — **`AIBT.Runtime` (254 types) and `AIBT.Authoring` (109 types)
stable for 1.0** — was not contested and stands as recommended, on the same additive-only-since-
`P2-GATE`/`P3-GATE` evidence `P7-001` already established.

## 1. Is `AIBT.Mcp`'s external contract stable for 1.0?

**Owner's instruction: verify it empirically within this gate, not just label it.** Real check
performed: `git diff 97e3501e71534f8de2e063cf74cdf52a36a43d04..HEAD -- MCP/McpToolDispatcher.cs`
(the `P6-GATE` candidate commit to the current `P7-016` candidate) shows exactly one `case` line
changed, an addition (`migrate_document`, from `P7-006`) — **zero tool names ever renamed or
removed**. But a deeper check (diffing the two existing tool-dispatcher files Phase 7 actually
touched, `McpNodeDevelopmentToolDispatcher.cs` and `McpVerificationToolDispatcher.cs`) found a real,
undisclosed regression in the tracking discipline itself: **`test-node`'s response shape lost its
`scopeNote` field** (present in every response before `P7-009`, absent after) when `P7-009` widened
the tool to actually prove dispatch (`dispatchProven`/`dispatchReason`/`enteredSuccessfully`/
`tickStatus`/`tickCallbackFailure` added instead). This is exactly the kind of change
`Documentation~/generated/migrations.md`'s "MCP surface migrations" section exists to record ("an
output field's meaning changed") — but that file still reads "None yet... has had no breaking change
since its first release," because entries are added by hand and this one was never logged.

**Decision: `AIBT.Mcp` is *not* provably stable — stays explicitly experimental for 1.0.** The tool-
name surface is genuinely additive-only, but at least one real output-shape change already slipped
through the manual migrations log undetected, which is itself evidence that this surface's stability
cannot yet be claimed with confidence. Retroactively logged in `migrations.md` as part of this gate's
own documentation-consistency pass (see `gate-runbook.md`).

## 2. Is `AIBT.Editor`'s 47-type surface stable for 1.0?

Owner: "hard to decide myself — either discuss separately, or make sure it's definitely not counted
as Stable." **Decision: experimental**, the safe default requiring no further discussion — matches
`P7-001`'s own original recommendation. No sample or documented extension point targets
`AIBT.Editor` directly the way `AIBT.Runtime`'s leaf-behavior contract or `AIBT.Mcp`'s custom-tool-
provider contract do.

## 3. Does the tree format's v1-writer/v1-and-2-reader coexistence need resolving before freeze?

Owner: **v2 should become the real default — "v2 is better and we should transition to it."** This
is real production work (enabling `ReferenceCompilationPolicy.Phase1`'s currently-disabled Agent/
Shared capability flags in production, the same gap `P6-014` already found and deferred) — not
buildable inside `P7-016` itself, per its own Forbidden-changes clause (no implementation inside the
gate). **Decision: required before a 1.0 tree-format stability claim is meaningful; spun off as a new
follow-up card (`P7-018`, see below) rather than left as prose.** The gate's own verdict discloses
this as an open item, not a silent gap.

## 4. Should the aggregate `get_project_manifest` response get its own JSON Schema before 1.0?

Owner: **yes, before 1.0.** Real, separate deliverable (new `Schemas~/` file + wiring into
`Verify-Schemas.ps1` + a real example validated) — not buildable inside `P7-016` itself. **Decision:
required before 1.0; spun off as a new follow-up card (`P7-019`).**

## 5. Should `Get-FullPublicApi.ps1` become a CI-enforced check before 1.0?

Owner: **yes, spin off a follow-up card rather than leave it manual.** **Decision: required before
1.0; spun off as a new follow-up card (`P7-020`).**

## Summary for the gate verdict

| # | Question | Decision |
|---|---|---|
| 0 | `AIBT.Runtime`/`AIBT.Authoring` stable for 1.0? | **Yes** (uncontested) |
| 1 | `AIBT.Mcp` stable for 1.0? | **No — experimental**, confirmed by a real found-but-undocumented output-shape change (`test-node`'s `scopeNote`) |
| 2 | `AIBT.Editor` stable for 1.0? | **No — experimental** |
| 3 | Tree format v1/v2 coexistence — resolve before freeze? | **Yes, required** — new follow-up `P7-018` |
| 4 | Aggregate manifest schema needed before 1.0? | **Yes, required** — new follow-up `P7-019` |
| 5 | `Get-FullPublicApi.ps1` CI-enforced? | **Yes, required** — new follow-up `P7-020` |

Three items (3, 4, 5) are now real, tracked, required-before-1.0 work — not open questions sitting
in a proposal document. This is what `P7-016`'s own acceptance criterion asked for: a recorded
decision, not a proposal awaiting one.
