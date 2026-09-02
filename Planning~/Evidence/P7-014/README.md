# P7-014 generated C# API reference documentation evidence

## Result

Done. Decides and builds, in one pass (mirroring `P6-021`'s own precedent for a mechanical,
non-architectural decision made and applied together): generate a full, always-in-sync reference
for AIBT's public C# surface, rather than rely on XML-doc comments plus `architecture.md`. No public
member's own signature, name, or behavior was touched, per this card's own Forbidden-changes clause.

## The decision, and why it wasn't "rely on XML-doc comments"

Investigated directly before designing anything, per this card's own Required-reading note ("check
before assuming it is sufficient or insufficient"):

- **Real current XML-doc coverage is ~2.4-4.5% of public members** — a source-parse across all four
  assemblies found 46 of 1,949 regular members+enum values documented, 106 of 2,366 total public
  declarations including types. Cross-checked two ways: the regex sweep's own type count (417)
  matched the reflection dump's exactly, and independently counting every `<summary>` tag in the
  four folders and checking what it precedes confirmed the low number is real, not a parsing
  artifact (most `<summary>` tags precede `internal`/private members, not public ones).
- **`architecture.md` cannot substitute** — confirmed by reading it directly: a narrative, one-
  paragraph-per-layer document with no per-type or per-member entries at all.
- Hand-writing ~2,000 new doc comments to reach 100% coverage would be exactly the kind of bulk,
  low-signal documentation this project's own style avoids writing without real content behind it —
  not proportionate to what the card actually needs.
- **The card's own acceptance criterion has a cheaper, fully legitimate reading**: "100% of public
  members... have *either* a generated reference entry *or* an XML-doc comment." A generated
  reference entry does not itself require doc-comment prose — a signature-complete generator
  satisfies this exactly, honestly, without inventing text for members that have none today.

## What was built

**`MCP/Documentation/McpApiReferenceGenerator.cs`** (new), mirroring `P6-011`'s own established
generator pattern exactly (a static `Generate()` returning content, `\n`-only line endings via the
same `.Replace("\r\n", "\n")` normalization `P6-012` already found necessary for a clean drift-check
on Windows). Reflects `AIBT.Runtime`/`AIBT.Authoring`/`AIBT.Editor`/`AIBT.Mcp` live
(`AppDomain.CurrentDomain.GetAssemblies()`) — placed inside `MCP/Documentation/` rather than a
standalone script because `AIBT.Mcp`'s own assembly already transitively references the other three,
so its AppDomain naturally has all four loaded when the existing `AIBT/MCP/Regenerate Documentation`
menu command runs; no isolated-project harness needed (unlike `P7-001`'s own `Get-FullPublicApi.ps1`,
which needed isolation specifically to *prove standalone compilability* — this generator only
describes the *current* project's own already-loaded surface).

Signature formatting (`TypeDisplayName`/`MethodSignature`) mirrors `Tools~/Verification/P7/Audit/
PublicApiDump.cs.txt`'s own helpers exactly, so every member's signature line reads identically to
the already-established `public-api.txt` convention.

**A real, disclosed technical constraint confirmed before writing anything**: Unity's compiled-
assembly reflection cannot recover XML-doc comment *text* — no `.xml` documentation file is emitted
anywhere under `Library/ScriptAssemblies/` for any of the four assemblies, and no `.csproj` in this
repo sets `DocumentationFile`. Doc text can only come from parsing C# source directly. Implemented as
a best-effort, **type-level only** enhancement: a regex source-scan across the four folders
correlates each type's own exact `FullName` with an immediately-preceding `<summary>` block (60 of
417 types, 14.4%, have one). Member-level correlation (matching a reflected `MethodInfo`/overload
against a source-parsed signature) was investigated and found materially more fragile — not
attempted this pass, disclosed rather than attempted and possibly wrong. Every member still gets its
own full signature line regardless of whether prose exists for it.

**Output**: `Documentation~/generated/api-reference-{runtime,authoring,editor,mcp}.md` (4 files, not
one ~5,100-line file, for the same navigability reason the existing dumps are already
assembly-grouped). `McpDocumentationRegenerateCommand.cs`'s existing `Regenerate()` gained four more
`File.WriteAllText` calls alongside its existing five — one command still regenerates everything.

## Tests

`Tests/Editor/Documentation/McpDocumentationGeneratorsTests.cs`:
- `CommittedGeneratedFilesMatchAFreshRegeneration` extended with the four new files (same
  established `AssertFileMatches`/`FindGeneratedDocumentationDirectory` helpers, already proven
  correct across both this repo's embedded layout and a real UPM consumer).
- New: `GeneratedApiReferenceCoversEveryPublicMemberInAllFourAssemblies` — this card's own literal
  acceptance criterion, proven mechanically rather than sampled: reflects each assembly fresh,
  builds the exact expected signature-line set, parses the committed file's own emitted lines back
  out, and asserts zero missing members plus a `### \`FullName\`` heading for every public type.
  Uses an independent copy of the formatting helpers (not a shared `internal`) so the test proves
  the *committed file* matches what a correct reflection pass expects, not merely what the
  generator's own internal formatting happens to produce.

Both passed on the first live run (11/11 in the Documentation test group; full EditMode regression,
1616 tests, shows no new failures beyond the 3 already-pre-existing, unrelated ones).

## Verification

```text
Verify-Static.ps1 -- passed
Live: AIBT/MCP/Regenerate Documentation menu command run via Unity MCP -- 4 new files written,
  spot-checked (api-reference-mcp.md: 7 types, real <summary> text inlined for 2 of them, every
  member signature present)
Unity MCP run_tests (EditMode):
  - AIBT.Tests.Editor.Documentation.* -- 11/11 passing (drift-check + new coverage-check)
  - Full EditMode project regression -- 1616 total, 1613 passed, 3 failed, all 3 pre-existing and
    unrelated to this card (same CodeGen-test-assembly-path environment issue and unrelated
    LocalSaveSystem failure already disclosed in prior cards' own evidence this session)
```

## Scope and limitations

- Member-level doc-comment prose correlation was not attempted (see above) — member-level coverage
  in the generated reference is signatures only, 100% of the time, with zero prose. This is
  disclosed plainly in every generated file's own header, not silently implied to be complete
  documentation in the "explains what this does" sense.
- Type-level summary coverage stays at whatever the source currently has (14.4%) — this card adds no
  new doc comments to source, per its own Forbidden-changes clause; a future pass that actually
  writes real `<summary>` content for the remaining 85.6% of types (and eventually members) would
  raise this generator's own output quality for free, with no generator change needed.
- The type-level summary regex assumes block-scoped `namespace X.Y.Z { }` declarations (confirmed as
  the exclusive style used throughout this codebase) and does not attempt correct `FullName`
  resolution for nested types (`Outer+Inner`) — a nested type simply gets no summary line (falls
  back to signature-only, same as any other undocumented type), never a wrong one.
