# P1-006 — Canonical tree JSON reader and writer

Status: `Draft`

## Objective

Read and write semantic tree documents deterministically with structured syntax and schema diagnostics.

## Depends on

- `P1-002`
- `P1-003`
- `P1-005`

## Allowed changes

- `Authoring/Serialization/Json/`
- `Tests/Editor/Serialization/`
- `Tests/Fixtures/Trees/Serialization/`

## Forbidden changes

- Semantic graph validation, automatic repair, layout files, runtime compilation, or changing the schema as an implementation shortcut.

## Deliverables

- UTF-8 reader/writer for `.aibt.json`.
- Canonical ordering and numeric formatting rules.
- Valid, invalid, Unicode, locale, and unknown-field fixtures.

## Acceptance criteria

- Valid fixtures round-trip semantically and canonical output is byte-stable.
- Unknown fields fail because v1 schema is strict.
- Syntax errors include stable code and JSON location/path when available.
- Output contains no timestamps, machine paths, runtime indices, or layout data.

## Required verification

- Focused tests plus double-write byte equality across at least two process cultures.
