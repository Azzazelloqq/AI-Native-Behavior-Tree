# ADR P7-042: Unity tree sources and browsing

Status: Accepted by the owner on 2026-09-22.

`.aibt` is the preferred new filename suffix for the existing canonical JSON tree document.
`.aibt.json` remains fully supported. This is an additive source-container convention, not a
JSON schema/formatVersion or runtime ABI change. Renaming does not change JSON bytes or tree ID.
Both `name.aibt` and `name.aibt.json` map to `name.aibt.layout.json`.
Do not create both sources for the same tree; duplicate IDs are ambiguous and cannot be selected
implicitly by MCP mutation. No existing tree is automatically renamed.

A ScriptedImporter produces an Editor-only tree asset with metadata/parse diagnostics. It does
not produce a runtime compiled program, rewrite the source, or own a second editable document.
The browser indexes imported AssetDatabase paths (Assets and Packages), including legacy JSON.
Ignored `Samples~` payloads are available only after sample import or explicit file opening.
The browser refreshes on project changes and exposes source path, ID, tags and validation state.
Built-in-only validation cannot certify unresolved project/generated node contracts.

The graph remains a read-only semantic viewer. Navigation, selection and temporary movement do
not persist behavior/layout changes. Source files and existing authoring/MCP APIs remain the
editing boundary. This decision supersedes the filename-only `.aibt.json` wording of AGENTS,
architecture and canonical JSON conventions; all semantic and layout separation rules remain.
