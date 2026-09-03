# P7-019 — Aggregate project-manifest JSON Schema

Status: `Draft`

## Objective

`P7-001`'s stability review found that `get_project_manifest`'s aggregate response document
(`{"format": "aibt-project-manifest", ...}`) has no JSON Schema of its own — only the *single-node*
manifest shape (`node-manifest.schema.json`) is schema-governed, and `Verify-Schemas.ps1` therefore
never validates it. Put to the owner directly during `P7-016`'s gate session (2026-09-03): the
owner's decision is to add this schema before `1.0`.

## Depends on

- `P7-001` (the stability review that surfaced this gap) and `Planning~/Evidence/P7-GATE/
  p7-001-stability-decision.md` (the recorded owner decision this card implements).
- `AIBT.Authoring.ProjectManifestQuery` (`P6-003` — the real production type that already builds
  this response shape; the schema must describe what it actually emits, not an assumed shape).

## Required reading

- `AIBT.Authoring.ProjectManifestQuery`'s own source (the authoritative shape to schematize).
- `Schemas~/node-manifest.schema.json` (the sibling schema to mirror in style/strictness).
- `Tools~/Verification/Verify-Schemas.ps1` (currently validates 6 schemas against a real example
  each; this card adds a 7th).

## Allowed changes

- `Schemas~/project-manifest.schema.json` (new).
- `Tools~/Verification/Verify-Schemas.ps1`, wired to validate the new schema against a real example
  response.
- `Planning~/Evidence/P7-019/`.

## Forbidden changes

- Changing `ProjectManifestQuery`'s own actual output shape to make it easier to schematize — the
  schema describes the real, already-shipped response; it does not drive a response-shape change.

## Deliverables

- `Schemas~/project-manifest.schema.json`, validated live against a real `get_project_manifest`
  response from the real project (mirroring `P7-001`'s own technique of generating a real example
  via a temporary script calling the public dispatcher entry point directly).
- `Verify-Schemas.ps1` updated to check it, confirmed passing.

## Acceptance criteria

- The schema validates a real response with zero errors, and rejects at least one deliberately
  malformed example (missing required field / wrong type), proving the schema is not a rubber
  stamp.

## Required verification

```text
Verify-Static.ps1
Verify-Schemas.ps1 (now 7 schemas)
a real get_project_manifest response validated live, plus one deliberate negative case
```

## Handoff notes

- Spun off from `P7-016`'s own gate session, per the owner's decision recorded in
  `Planning~/Evidence/P7-GATE/p7-001-stability-decision.md` — not required for `P7-016`'s own
  verdict, but required before the aggregate manifest response can honestly be called
  schema-governed for `1.0`.
