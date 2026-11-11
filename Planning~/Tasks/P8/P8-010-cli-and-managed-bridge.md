# P8-010 — CLI and on-demand Editor bridge

Status: `Review`

## Authorization

Owner requested both CLI and MCP, with agents able to start/recover MCP through CLI when
not running (chat, 2026-11-06). This authorizes explicit command-triggered startup, not an
unconditional Editor startup listener or changes to AI-client configuration.
Dependencies P6-005, P6-006, P6-007, P7-031 are Done. P8-009's unmerged work is preserved.

## Plan and owned paths

1. Add an external .NET CLI and shared bridge client; reuse the existing operation dispatcher
   and permissions. Keep MCP tools backward compatible, including custom project tools.
2. Centralize Editor bridge ownership. Add a versioned, request-triggered Library handshake
   so CLI can start a stopped listener without needing that listener to already be running.
3. Probe health and recover stale endpoints with a bounded wait. Never retry an operation
   whose mutation outcome is unknown. Never kill/restart Unity or silently broaden permissions.
4. Provide status, discovery, tree verification, generic operation invocation and MCP stdio
   launch. Commands expose JSON results and nonzero failure exits. Unity remains required.
5. Update agent connection/workflow documentation and test transport, permissions, failures,
   repeated ensure, reload and real Editor calls. Record unavailable checks explicitly.

Allowed: MCP, MCP~, Tests/Editor/Mcp, Tools~/Verification/P8, Documentation~, README.md,
CHANGELOG.md, this card and its evidence/work-item entry. No Runtime changes, package version
bump, automatic project opening, AI-client configuration editing or removal of existing tools.

## Acceptance

- A fresh CLI process can start the bridge in an already-open Editor and issue a read call.
- Repeated ensure does not replace a healthy listener; domain reload restores one owner.
- Stale discovery and closed/busy Editor produce bounded, actionable errors.
- CLI and MCP share transport and dispatch; permissions remain explicit and fail closed.
- Mutations are dispatched once; transport timeout never implies safe automatic retry.
- Real MCP stdio client and CLI exercise the same open disposable project.

## Handoff

Task ID: P8-010. Outcome: implemented; installed-toolchain and live verification pass.
Branch: feature/aibt-consumer-workflow; no commit/push requested.
Changed: shared external connection client/CLI, Editor bridge ownership/control, transport tests,
agent documentation/generators, CLI live fixture/contracts and this planning/evidence entry.
Verification: both builds 0 warnings/errors; 16 transport/CLI contracts; isolated Unity full
1500/1500; real CLI and MCP stdio initialize/list/call plus recovery/reload and authoring checks;
7 schemas/156 work items and diff whitespace pass. See ../../Evidence/P8-010/README.md.
Normative choice: AIBT-043 records explicitly requested command-triggered startup.
Scope limits: no native Prepare Runtime/live trace command; existing backend/catalog limits
remain documented. No Player/performance claim. Unity 6000.5.8f1 acceptance was not run because
that Editor is not installed. No AI-client configuration was changed.
Recommended next task: unify native/project catalogs across authoring/verification, then add
native preparation/trace operations against that common backend; resolve First Agent editor
discovery before declaring the complete consumer workflow accepted.
