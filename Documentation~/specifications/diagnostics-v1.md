# Structured diagnostics v1

## Layering

Backend-neutral diagnostic primitives live in `AIBT.Runtime` under `Runtime/Core/Diagnostics/`. They contain severity, code, identity/location values, deterministic comparison, and an immutable collection. They do not depend on JSON or Authoring.

Authoring diagnostic serialization, JSON paths, related document locations, and machine-applicable authoring operations live under `Authoring/Diagnostics/` and may depend on the Runtime primitives.

## Record

A diagnostic contains:

- stable code and severity (`Info`, `Warning`, or `Error`);
- stable primary identity when known: tree ID, node ID, or tree instance ID;
- optional RFC 6901 JSON Pointer and one-based line/column;
- culture-invariant English message intended for humans but not parsing;
- zero or more related locations;
- optional safe suggested operation with stable operation ID and typed payload.

Unknown optional values are absent, never synthesized. A diagnostic record is immutable.

## Codes

Codes use `AIBT` plus four decimal digits. Phase 1 reserves:

- `AIBT0001`-`0999`: core/runtime and identity;
- `AIBT1000`-`1999`: syntax, schema, and canonical serialization (`1000`-`1099`: `.aibt.json`; `1100`-`1199`: `*.aibt.layout.json`, see `editor-layout-v1.md`);
- `AIBT2000`-`2999`: semantic validation and policy;
- `AIBT3000`-`3999`: node registry and compiler;
- `AIBT4000`-`4999`: execution, blackboard, command, and async runtime;
- `AIBT5000`-`5999`: public node ABI, analyzers, generated catalogs, and binding/layout handshakes;
- `AIBT9000`-`9999`: tooling and test-case input.

Adding a code requires one catalog entry with default severity and field contract. Consumers must not infer subsystem from message text.

## Ordering and duplicates

Collections sort by severity (`Error`, `Warning`, `Info`), code ordinal, document identity, JSON Pointer, line, column, node identity, and message ordinal. Missing values sort before present values. Related locations are sorted by the same location key.

Exact duplicates after normalization are retained once. Diagnostics with the same code/location but different structured fields or messages are distinct. Insertion order never affects output.

## Authoring JSON

Diagnostic JSON uses canonical JSON v1. Required properties are `code`, `severity`, and `message`; other properties are emitted only when known. Severity text is `error`, `warning`, or `info`. Suggested operations are data only and are never automatically applied.

Properties are emitted in this exact order when present: `code`, `severity`, `message`, `treeId`, `nodeId`, `treeInstanceId`, `documentId`, `jsonPointer`, `line`, `column`, `relatedLocations`, `suggestedOperation`. A suggested operation emits `operationId`, `payloadType`, then `payload`. Location objects use the same location-field order. Operation payload maps use ordinal UTF-8 key order.

Managed exceptions are converted at the owning boundary to a stable diagnostic code. Stack traces and machine paths may be attached to local debug logs but are not canonical diagnostic fields.
