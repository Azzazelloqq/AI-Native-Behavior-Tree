# AIBT public API and persisted-format stability review — proposal

This is the concrete material for `Planning~/USER_ACTIONS.md`'s "Approve final public API and
persisted-format stability review" — a proposal for the owner to decide, not a decision itself.
Nothing here has been frozen; no `[Obsolete]` attribute or rename has been added anywhere.

Full raw data: `public-api-current.txt` (the live dump this proposal is built from),
`new-since-p6-gate.txt` (everything added since the last gate), `README.md` (methodology).

## 1. Public API — recommended stable

Every public member across all four assemblies has grown strictly additively since `P2-GATE`
(2025's first accepted gate) — confirmed by a full mechanical diff across all five prior gates, not
sampled or recalled (see `README.md`'s classification table). **Zero removals or breaking renames
anywhere in the project's history.** On that basis:

**Recommended stable for 1.0**: `AIBT.Runtime` (254 types) and `AIBT.Authoring` (109 types). Both
have been additive-only since `P2-GATE`/`P3-GATE` respectively, carry the project's core
tree/execution/scheduling/hot-reload/blackboard contracts, and are exercised by the overwhelming
majority of the test suite (1600+ tests).

**Recommended still-experimental**: `AIBT.Editor` (47 types) and `AIBT.Mcp` (7 types) — see open
questions 1–3 below. Neither is *unstable* (both are additive-only too), but both carry real,
disclosed external dependencies this review should not paper over.

## 2. Persisted formats

| Format | Current version | Ever changed post-acceptance | Migration story |
|---|---|---|---|
| `*.aibt.json` (tree) | writer default: **1**; readers accept **1 and 2** | No (v2 support landed before `P2-GATE`'s own acceptance) | None yet — `P7-005` undecomposed |
| `*.aibt.layout.json` | **1** | No | None yet |
| `*.aibtcase.json` (behavior case) | **1** | No | None yet |
| `.aibt/policy.json` | **1** | No | None yet |
| Node manifest (single-node) | **1** | No | None yet |
| Generated descriptor JSON | `abiVersion` **2** | Not checked (no gate ever captured a prior `abiVersion`; disclosed as unverified, not assumed unchanged) | None yet |

All six schema files were re-validated live against a real example document during this review (four
of them for the first time ever — `Verify-Schemas.ps1` itself only ever checked two). All six pass.
See `README.md` for the real, useful validation-shape finding this surfaced (the aggregate
`get_project_manifest` response has no schema of its own — open question 4).

## 3. Open questions for the owner

1. **Is `AIBT.Mcp`'s 7-type surface stable-for-1.0, or explicitly experimental past 1.0?** It depends
   on the external `dotnet` process model (`MCP~/Server/`) — a consuming project's own MCP client
   configuration and the external server process are outside this repository's own versioning. The
   *shape* of `AIBT.Mcp`'s in-process surface has been additive-only, but its *external contract*
   (the JSON-RPC-ish protocol `McpToolDispatcher.Dispatch` speaks) has never itself been reviewed for
   stability the way the C# surface has.
2. **Is `AIBT.Editor`'s 47-type surface stable-for-1.0?** It is Editor-only (`includePlatforms:
   ["Editor"]`) and additive-only, but has received far less external-consumer scrutiny than
   `Runtime`/`Authoring` (no sample or documented extension point targets it directly the way
   `AIBT.Runtime`'s leaf-behavior contract or `AIBT.Mcp`'s custom-tool-provider contract do).
3. **Does the tree format's version-1-writer / version-1-and-2-reader coexistence need resolving
   before freeze?** Every writer in the codebase still defaults to `formatVersion: 1`; version 2
   (extended blackboard bindings, Agent/Shared scope contracts) is reader-only-so-far and gated
   behind `ReferenceCompilationPolicy.Phase1` disabling the capability flags it needs in production
   (a separately-disclosed gap from the 2026-08-28/29 session, still open). Should 1.0 ship with v1
   as the permanent default and v2 as an indefinitely-experimental opt-in, or does v2 need to become
   the real default before a stability claim is meaningful?
4. **Should the aggregate `get_project_manifest` response document (`{"format":
   "aibt-project-manifest", ...}`) get its own schema?** It currently has none — only the *single-node*
   manifest shape (`node-manifest.schema.json`) is schema-governed. If external tooling is expected to
   consume the aggregate response directly (not just individual node contracts), a stability claim
   about "persisted formats" arguably should cover it too.
5. **Should `Get-FullPublicApi.ps1` become a real, CI-enforced check** (catching a future accidental
   public-surface change automatically) rather than a manually-run snapshot tool, before or as part of
   1.0? This is `P7-015`'s own possible scope, not decided here.

## 4. What this proposal does not do

- Does not freeze any type, member, or format version.
- Does not add `[Obsolete]` to anything speculatively "recommended still-experimental" above — that
  would itself be a public-surface change requiring the same owner approval this review exists to
  gather.
- Does not resolve any of the five open questions — each requires an owner decision this card cannot
  make on its own, per `Planning~/DECISION_BOUNDARIES.md`'s "public or cross-assembly API shape"
  escalation rule.
