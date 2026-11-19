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

### Subtree codes (ADR-P11-001)

Single-document rules belong to the tree validator (`AIBT2xxx`); rules that need the subtree
resolver belong to the expander, in the compiler range (`AIBT30xx`). Every code is an error.

| Code | Name | Rule |
|---|---|---|
| `AIBT2047` | SubtreeReferenceShape | A node with `subtree` has no `type`, `typeVersion`, `parameters`, `bindings`, `observer` or `children`; its ID contains no `:`; `treeId` is a valid tree ID; `interfaceVersion` ≥ 1. (Schema-level shape errors still report `AIBT1004`.) |
| `AIBT2048` | SubtreeKeyTarget | A string entry in `subtree.keys` names a declared Tree-scope key of the parent. |
| `AIBT2049` | SubtreeReferenceRoot | The root is not a reference node. |
| `AIBT2050` | SubtreeInterface | Every `interface.keys` entry is a declared Tree-scope key; no duplicates; `version` ≥ 1. |
| `AIBT3020` | SubtreeNotFound | No document has the referenced tree ID, or no resolver was given and the tree has references. |
| `AIBT3021` | SubtreeAmbiguous | More than one document has the referenced tree ID (related locations list them). |
| `AIBT3022` | SubtreeCycle | The reference graph has a cycle (message lists it). |
| `AIBT3023` | SubtreeDepth | Nesting deeper than 8. |
| `AIBT3024` | SubtreeInterfaceVersion | `interfaceVersion` differs from the subtree's `interface.version`, or the subtree has no interface but the reference binds keys. |
| `AIBT3025` | SubtreeKeySet | Missing or unknown entries in `subtree.keys`. |
| `AIBT3026` | SubtreeKeyType | Type/version/enum mismatch of a binding or constant; a fixed-name manifest access that cannot be renamed. |
| `AIBT3027` | SubtreeQualifiedIdentity | An expanded node or key ID exceeds 128 characters or collides with another (including a parent ID that starts with `r:`). |
| `AIBT3028` | SubtreeScopeContract | Subtree's Agent/Shared scope contract differs from or is missing in the parent. |

Diagnostics raised inside a subtree carry the subtree's `documentId` and a related location at the
reference node.

## Ordering and duplicates

Collections sort by severity (`Error`, `Warning`, `Info`), code ordinal, document identity, JSON Pointer, line, column, node identity, and message ordinal. Missing values sort before present values. Related locations are sorted by the same location key.

Exact duplicates after normalization are retained once. Diagnostics with the same code/location but different structured fields or messages are distinct. Insertion order never affects output.

## Authoring JSON

Diagnostic JSON uses canonical JSON v1. Required properties are `code`, `severity`, and `message`; other properties are emitted only when known. Severity text is `error`, `warning`, or `info`. Suggested operations are data only and are never automatically applied.

Properties are emitted in this exact order when present: `code`, `severity`, `message`, `treeId`, `nodeId`, `treeInstanceId`, `documentId`, `jsonPointer`, `line`, `column`, `relatedLocations`, `suggestedOperation`. A suggested operation emits `operationId`, `payloadType`, then `payload`. Location objects use the same location-field order. Operation payload maps use ordinal UTF-8 key order.

Managed exceptions are converted at the owning boundary to a stable diagnostic code. Stack traces and machine paths may be attached to local debug logs but are not canonical diagnostic fields.
