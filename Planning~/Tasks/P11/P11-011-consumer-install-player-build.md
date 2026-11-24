# P11-011 — Consumer install and IL2CPP Player build (F14)

Status: `review` — run on 2026-11-23 against `0aa9a6a`; its findings D1 to D9 are fixed or recorded in
P11-015.

## Objective

Install the package the way a consumer does and build a Player with it:

- an immutable tarball of one commit (`git archive`), referenced from a fresh Unity project's
  `Packages/manifest.json` (Built-in render pipeline, no `.aibt/policy.json`);
- import all six samples through the Package Manager;
- follow each sample's documented user steps (the **Create Demo** menus);
- check the trees with the Graph window's tree list and with MCP `validate` and `compile`;
- build a Windows x64 IL2CPP release Player with the four demo scenes;
- run the Player windowed and in `-batchmode -nographics`, with a harness that reports each scene's
  agents and whether Burst ran;
- record every defect a consumer would meet, without changing the package.

## Depends on

- `P11-010` (the package state at `0c2b541`; `0aa9a6a` adds planning only).

## Scope

Temp projects and files under `C:\UnityProjects\_aibt_f14\` only. Nothing in the Modules project or the
package was written.

## Forbidden changes

- Any file of the package or the Modules project.
- Any Editor other than the run's own batch-mode instances; no process killed.

## Acceptance criteria

- The package installs from the tarball, and the run records whether AIBT writes into the installed copy.
- Every sample is imported, its user steps are run, and the result is recorded per sample.
- The Player builds, both runs end by themselves, and the exit codes and Player lines are recorded.
- Every defect has its evidence, its cause (or "not determined") and what a user sees.

## Evidence

`Planning~/Evidence/P11-011/README.md`. Raw files: `C:\UnityProjects\_aibt_f14\` (`REPORT.md`).
