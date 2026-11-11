# P8-011 — Agent consumer completion

Status: Review, verified on the installed toolchain, 2026-11-07. No commit/push.

## Implemented

1. Discovery, authoring and validation share project node contracts. Authoring chooses a native
   validator for native-only leaves while reference compilation remains explicit. Full project
   policy is preserved across edit/compile/prepare, including scope and warning restrictions.
2. A graph opened before delayed catalog discovery rebinds its registry without discarding the
   draft, Undo history or layout. Equal-contract refreshes preserve the execution view. This
   reproduced/fixed the First Agent `Move -> Unresolved` startup snapshot problem.
3. Resources resolve the installed package root; live node-catalog resources reflect the project.
   Project documentation generation writes Assets/AIBT-Documentation, not a package cache.
4. Shared schemas describe 37 bridge operations (36 MCP tools plus the resource lookup). CLI
   exposes operations/describe, tree inspect/prepare, runtime catalogs/hosts and native trace.
   New operations reuse the existing Editor baker/trace snapshot reader on the Editor thread.
5. Native preparation checks source hash, catalog leaf coverage, project policy and destination
   identity. Dry-run, explicit overwrite/asset hash, GUID preservation, dirty target refusal,
   path/link checks and targeted SaveAssetIfDirty avoid unrelated writes. Logical asset paths
   are normalized independently of import state on Windows.
6. Generated case data replaces the Inconclusive scaffold and is consumed by test_node. Cases
   compare caller/template-contract expectations against real Enter/Tick dispatch, scalar inputs,
   configuration and optional exact command payloads. Negative expectations fail. Applied Burst
   node assemblies are runtime-capable; staging/case data stay Editor-only. Async/registered/
   fixed-byte and multi-update behavior are explicitly outside this checker.

## Verification

Environment: Windows, Unity 6000.7.0b1, .NET SDK 10.0.300.

- Full isolated EditMode: **1514/1514**, no failed/skipped tests. Includes project policy rejection,
  schema parity, file-package resource resolution, native catalog refresh with Undo preservation,
  precise native status/effect cases and counterexamples, built-in native/reference contract
  coexistence, missing catalog coverage, logical/escaping asset paths and dropped trace history.
  Normalized XML and original-output SHA-256 are recorded alongside verification.json.
- External CLI/transport contracts: **16/16**. CLI and MCP server build with no errors/warnings.
- Real CLI and MCP stdio consumer: live.json records the complete First Agent and new-node paths:
  inspect/author/validate, explicit reference refusal, native dry-run/prepare/hash/GUID overwrite,
  Play-mode expected position/root result, exact graph/host mapping, native trace identity and
  loss disclosure, MCP trace/resources, staged generation/compile/native cases, deliberately
  wrong expectation, apply, subsequent runtime-node discovery and authoring a tree with it.
- Windows **IL2CPP Development Player** built and ran the CLI-prepared First Agent asset.
  player.json records Success, no native failure, the expected program hash and final position
  within the controlled target's stopping radius. Build warnings are the two empty staging
  assembly folders left after a successful apply, not Player errors. No benchmark claim.
- Static verification: 7 schemas, 157 work items, local links/JSON and diff whitespace pass.
- The full-suite shutdown warning still reports the existing 112 Persistent allocations, as
  independently reproduced before P8-009/010. The Player smoke does not report that warning.

The live trace channel is bounded and retains early records. A long run can finish after terminal
events no longer fit; complete=false/droppedCount/activeNodesReliable disclose that condition.
The host's terminal outcome and independently observed game position are checked separately;
the verifier does not fabricate a complete terminal history from a partial channel.

## Reproduce

Build MCP~/Cli/AibtCli.csproj and MCP~/Server/AibtMcpServer.csproj, then run:

```powershell
dotnet run --project Tools~/Verification/P8/CliContracts/CliContracts.csproj -- "<AIBT>/MCP~/Cli/bin/Debug/net10.0/aibt.dll"
```

Use a disposable file-package consumer with the First Agent sample imported and its demo scene
created. Copy Fixtures/AgentConsumerSession.cs into Assets/CliProbe/Editor. Start its Editor with
`-batchmode -executeMethod AgentConsumerSession.Open` (no -quit), then:

```powershell
python Tools~/Verification/P8/Run-AgentConsumerLive.py --project "<DisposableProject>" --output "<Results>/live.json"
```

Only this owned fixture may be used: the verifier edits First Agent, creates unique assets/trees,
enters Play and exercises node generation in that project. For Player checks copy
Fixtures/AgentPlayerSmoke.cs into a Runtime-only assembly referencing AIBT.Runtime and
Fixtures/AgentPlayerBuild.cs into an Editor assembly referencing Runtime/Editor/Newtonsoft.Json.
Close the fixture Editor, run AgentPlayerBuild.Build in batch mode, then launch the built
AgentPlayer/Agent.exe with `-batchmode -nographics -aibtReport <result.json>`. The smoke scene is
separate from the demo; the prepared asset is reloaded after creating the scene before assignment.
This prevents Unity scene unloading from invalidating the fixture's local asset reference.

Full tests use Run-UnityTests.ps1 -Scope Full -EnableGraphics in a separate clean project with
the package in testables. The package's canonical generated docs are regenerated from the actual
generator; project-aware generation is separately exercised by the live fixture.

## Limits and scope

Unity 6000.5.8f1 is not installed, so that baseline acceptance is not claimed. Native behavior
cases are independent single-tick scalar checks, not general async/gameplay tests. Reference
simulation retains its documented Phase 1 backend limits. No remote Player trace attachment or
trace comparison was added. Manual first-time UI acceptance was not rerun; the graph was tested
through real Editor window APIs, not screenshots or a claim of human usability.

Runtime VM/scheduler/ABI, package version, AI-client settings and unrelated work are unchanged.
The Authoring policy exposes CreateValidationOptions to reuse the same rules across assemblies;
the generated API reference includes this additive member.
