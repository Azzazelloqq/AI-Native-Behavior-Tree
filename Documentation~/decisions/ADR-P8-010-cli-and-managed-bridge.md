# AIBT-043 — CLI and on-demand Editor bridge

Status: Accepted by owner's request, 2026-11-06.

CLI is the primary command interface for agents and scripts. MCP stays as a compatible
protocol adapter to the same Editor operation dispatcher, including client-facing resources
and custom-tool discovery. No operation is intrinsically MCP-only; do not fork business logic
or remove existing tools merely to create an artificial division.

An explicit CLI/MCP connection request may start or recover the local Editor bridge. This
supersedes ADR-P6-001's manual-window-only startup constraint for that request, not its
opt-in principle: merely opening Unity does not start a network listener. An Editor update
hook consumes a version-1 control request under Library/AIBT and acknowledges its identity.
The selected project's Editor must already be open and responsive. Do not restart Unity.

The external clients share endpoint discovery, health checks, bounded connection waits and
explicit permission categories. Connection recovery occurs before dispatch. A command with
an unknown outcome is never automatically replayed. Runtime dependencies stay unchanged.

`mcp ensure` prepares the Editor endpoint. `mcp serve` runs a stdio server for a client to
launch/connect; it cannot attach native MCP tools to an arbitrary AI client or rewrite its
configuration. Preserve existing MCP tools for compatibility. Offline authoring is outside
this task; CLI's first backend is the running Editor.
