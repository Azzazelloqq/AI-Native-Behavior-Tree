# P7-042 — Tree assets and browser

Status: `In Progress`

## Authorization and scope

Owner approved `.aibt` JSON sources, typed Unity imports, searchable tree browsing,
double-click graph opening and backwards-compatible `.aibt.json`/layout/MCP support.
Dependencies: P7-025, P7-023 (Done).

## Plan and acceptance

1. Centralize both source suffixes and their unchanged `.aibt.layout.json` sidecar mapping.
2. Add an Editor-only imported tree asset; source JSON remains authoritative, not duplicated authoring data.
3. Add graph toolbar, project browser, name/ID/path search, tag/status filters and explicit errors.
4. Refresh on AssetDatabase project changes; preserve legacy files and support explicit opening.
5. Update MCP discovery/create and migration scanning without changing compiler/execution semantics.
6. Test import, both extensions, sidecars, filtering, invalid input and live Swarm Arena rendering.

No mass rename, new runtime node, scheduler change, semantic visual editing, commit or push.
Unknown project node contracts must not be reported as fully validated by the built-in registry.

## Verification

Pending focused tests, full package regression and live Editor verification.
