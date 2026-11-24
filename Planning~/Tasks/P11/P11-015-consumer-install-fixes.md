# P11-015 — Consumer-install fixes (findings of P11-011)

Status: `in_progress` — committed on `feature/aibt-consumer-workflow` (local commits only); waits for the
orchestrator's acceptance run.

## Objective

Fix in the package what the consumer install of P11-011 found, then prove the fixes on a fresh install,
as the orchestrator's briefs of 2026-11-24 list them
(`C:\UnityProjects\_aibt_acceptance\P11-015\brief-a-swarm-arena.md`, `brief-b-consumer-install.md`,
`brief-b-item-8-size.md`):

- **Part A.** Swarm Arena works in a consumer project (D2, D3, D4, D5, input). It was built and tested in
  the P11-011 temp projects, Built-in and URP.
- **Part B:**
  1. without `.aibt/policy.json`, MCP and the CLI use the Editor's default policy (D1);
  2. the test fixture assemblies stop compiling into a consumer's Editor (new finding);
  3. part A is ported into `Samples~/SwarmArena`;
  4. documentation: D5, D6, D7, D9 and the install routes;
  5. packaging: the development folders and the 370 MB raw report leave the package (D8);
  6. planning records for P11-011 and P11-015;
  7. proof on a fresh install from a new tarball;
  8. what AIBT weighs for a consumer: Player share, size per tree node, what a prepared asset holds and
     memory per agent (added on the owner's word).

## Depends on

- `P11-011` (the findings) and `P11-014` (the MCP verification code and the Known limitations page this
  task builds on).

## Scope

- `MCP/`, `Editor/Registry/ProjectNodeCatalog.cs`, `Editor/Graph/TreeCatalogEntry.cs`,
  `Authoring/Discovery/ProjectManifestQuery.cs`, `Schemas~/project-manifest.schema.json`;
- the three fixture asmdefs under `Tests/`, the new and changed tests;
- `Samples~/SwarmArena/`;
- `Agents/BehaviorTreeSource.cs` (comments);
- `Documentation~/`, `README.md`, `CHANGELOG.md`, `.gitattributes`, `.npmignore`;
- the removal of `Planning~/Evidence/P7-035/swarm-arena-player-20260917-090844.raw.json`;
- `Planning~/`;
- temp projects under `C:\UnityProjects\_aibt_f14\`.

## Forbidden changes

Each of these needs a recorded decision:

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`;
- the compiled program format;
- `CodeGen~` and `package.json`;
- files under `Tests/Fixtures`;
- normative specifications and execution semantics, including any scheduler change;
- existing public signatures (only additions).

No history rewrite and no other evidence file.

## Acceptance criteria

- Behaviour changes test first (failing before), one commit per item or small group of one kind.
- With a readable policy file present, MCP results do not change. (Narrowed at the review follow-up, as
  ADR-P9-001's amendment states: a file that exists but cannot be read is not applied; the tools use the
  Editor's defaults and report warning AIBT9008. Without a file, `compile` returns a different `contentHash`
  than before, because the program records the policy.)
- Only the touched tests are run; the Console is free of new errors; the submodule checkout is clean.
- `Tools~/Verification/Verify-Static.ps1` passes.
- The fresh install shows, with no `.aibt/policy.json`:
  - no fixture assembly loaded and no `aibt_custom_sample_write_marker` in the MCP tool list;
  - every sample tree valid with `policyApplied:false`, and the trees listed by `get_project_manifest`;
  - four demo scenes passing with exit code 0 in a windowed and a batch Player run.
- `Planning~/Evidence/P11-015/README.md`. Raw files go in `C:\UnityProjects\_aibt_acceptance\P11-015\` and
  `C:\UnityProjects\_aibt_f14\`.

## Evidence

`Planning~/Evidence/P11-015/README.md`.
