# P6-009 node development tools evidence

## Result

Done, narrowed. `MCP/NodeDevelopment/` implements all 6 tools this card owns -- `generate-node`,
`preview-node-diff`, `generate-node-tests-and-manifest`, `analyze-and-compile-node`, `test-node`,
`apply-node` -- the first time in the project's history a genuinely new custom node is generated,
compiled through the real packaged Roslyn analyzer, and registered end-to-end. Wired through
`McpToolDispatcher.cs` (6 new permission-tagged cases) and relayed by 6 new thin server methods in
`MCP~/Server/NodeDevelopmentTools.cs`.

## Scope correction: test-node narrowed, generic dispatch harness spun off to P6-022

Before implementation, reading the project's own golden test for the sample node
(`Tools~/Verification/P2/CodeGen/SampleGolden/PublicBurstNodeSampleGoldenTests.cs.txt`, 688 lines)
found this card's own premise -- that `test-node` could "run the generated node's own tests" and
prove it "actually executes through generated dispatch" -- assumed a capability that does not
exist. The golden test hand-computes every field offset/ordinal/binding-table entry for the one
specific, already-known sample node; there is no generic, reusable translator from a compiled
node's own descriptor metadata into the native `NativeBurstDispatchWorkspaceShapeV2` real
execution requires, and `burst-node-abi-v2.md`'s opaque-context rule rules out any reflection
shortcut (`BurstTickContext` and siblings are native-backed, Runtime-private carriers with no
public constructor). Building that translator generically is a substantial capability in its own
right. Per explicit owner decision (`AskUserQuestion`, this session, mirroring `P6-008`'s `P6-015`
split): spun off into `P6-022` (Draft, spike/decision), decided on paper first rather than built ad
hoc mid-card. `test-node` is narrowed to proving the compiled shard's metadata is structurally
valid and registry-materializable (`GeneratedShardMetadataMaterializer.MaterializeArtifact` +
`GeneratedNodeRegistry.Build`, both real, already-accepted, already-`public` production entry
points) -- real verification, just not runtime dispatch execution. `generate-node-tests-and-manifest`
generates an honest placeholder test scaffold (`Assert.Inconclusive`, citing `P6-022` directly)
rather than a fake pretend-working test. This card's own task card was corrected in place to match
(see its own "Scope correction" section), `work-items.json` updated, both committed together with
the rest of this session's work.

## Design (established before writing code, via `EnterPlanMode`, informed by live empirical tests)

**How does `analyze-and-compile-node` get a real Unity/Roslyn compile without new main-thread
bridge infrastructure?** `McpBridgeListener` runs entirely on a background TCP-accept thread with
no main-thread dispatch (the same root cause as `P6-008`'s `UnityEngine.Application` bug).
Empirically tested live against the real Editor: `EditorApplication.isCompiling` reads safely from
a background thread (confirmed); `AssetDatabase.Refresh()`/`EditorApplication.isUpdating` throw
off-main-thread (confirmed); **Unity's own Auto Refresh detects a plain background-thread file
write and starts compiling on its own** -- no explicit `Refresh()` call needed (confirmed live
multiple times: writing a probe `.cs` file from a background thread reliably triggered
`[ScriptCompilation] Requested script compilation because: AssetDatabase observed changes in
script compilation related files` in the project-relative, plain-file-readable `Logs/Editor.log`).
So the "write -> compile -> read diagnostics" cycle needs only thread-safe file I/O and polling.

**How does a generated node get registered without a second registry mechanism?**
`GeneratedShardMetadataMaterializer.MaterializeArtifact`/`GeneratedNodeRegistry.Build`
(`Authoring/Registry/Generated/`) are already `public` in `AIBT.Authoring` -- no accessibility gap,
unlike `P6-008`'s `BehaviorCaseRunner` situation. They consume exactly the `AibtGeneratedMetadata`
constants `GeneratedMetadataEmitter` already emits onto a compiled shard, readable via plain
reflection.

**A single request must never block across a domain reload.** Empirically confirmed this session:
started the bridge, wrote a `.cs` file that triggered a real compile, and after the resulting
domain reload the same TCP port stopped accepting connections -- the listener object was destroyed,
exactly what Unity does to all managed state on a domain reload by default. Every prior P6 tool
only ever wrote *data* files (`.aibt.json`/`.aibtcase.json`), which never trigger script
compilation, so this never surfaced before. Resolved two ways:
1. `analyze-and-compile-node` is a **two-call, instantaneous, non-blocking** design (`mode:
   "start"` records a log-position marker and returns immediately; `mode: "check"` takes a single,
   instantaneous snapshot -- never a polling loop inside one call -- and reports
   `not-yet-observed`/`still-compiling`/`compiled`/`failed`). The caller retries `check` across
   separate requests, exactly the "call again later" shape this project's own async job tools use
   elsewhere.
2. **`McpBridgeListener` now survives a domain reload.** New `MCP/McpBridgeAutoRestart.cs`
   (`[InitializeOnLoad]`) records "was running" in `UnityEditor.SessionState` (survives a domain
   reload within the same Editor session, unlike a plain field) and auto-restarts a fresh listener
   after every reload. This is shared, `P6-005`-owned infrastructure outside this card's original
   file list; touching it was an explicit owner decision (`AskUserQuestion`, this session) after
   finding the alternative (every MCP tool group silently breaking the first time anyone generates
   a node) unacceptable. **Verified empirically, twice, across real domain reloads**: the discovery
   file's port changed after each reload and the new port was independently confirmed reachable
   via a raw TCP test from outside Unity MCP entirely.

**`apply-node` never trusts a caller's claim or tracks server-side session state** (MCP calls in
this project reload fresh every time by design, per `ai-and-mcp.md`'s own domain-patch precondition
philosophy) -- it requires the exact content hash `analyze-and-compile-node`'s last `compiled`
check returned, re-verifies the *current* staged content still hashes to that value, and re-runs
the registry-materialization check itself immediately before persisting.

## Real bugs found live (all fixed, all re-verified)

1. **Generated node source lost implicit access to `NodeStatus`/`NodeMemoryLifetime`/
   `BlackboardScope`.** The sample's own namespace (`AIBT.Samples.BurstNodes`) starts with `AIBT.`,
   so C#'s enclosing-namespace lookup gives it implicit access to the bare `AIBT` namespace; a
   caller-chosen namespace not starting with `AIBT.` (used in this card's own live test) does not
   get that for free. Fixed by adding an explicit `using AIBT;` to both templates
   (`NodeTemplateGenerator.cs`). Found on the very first live `analyze-and-compile-node` check
   against a real Condition node (`CS0103`/`CS0246`), re-verified clean afterward.
2. **A second domain-reload-survival trap: Unity will not domain-reload at all while *any* compile
   error exists anywhere in the project**, even in an unrelated assembly. This meant my own fix to
   `NodeTemplateGenerator.cs` (item 1) did not actually take effect in the running Editor until the
   broken staged file was deleted -- the stale, pre-fix `AIBT.Mcp.dll` kept running and kept
   producing the old broken output on the next `generate-node` call, silently, with no error of its
   own. Not a code defect, but a real operational trap worth recording: **a broken staged
   generation blocks the whole project's next domain reload, including unrelated fixes**.
3. **Applying two different generated nodes without their own destination assembly collided.**
   `AIBT5011` ("a node assembly must declare exactly one shard") fired for real once a second
   applied node landed in the same default `Assembly-CSharp` as the first. Fixed by having
   `StagingSlot.MoveTo` write a fresh destination `.asmdef` (with the analyzer attached, mirroring
   the staging slot's own) whenever the destination path is not already inside an existing asmdef's
   folder -- found and fixed live, re-verified with both nodes applied simultaneously and compiling
   clean together, then confirmed searchable together in one real combined registry build (13
   entries: 11 built-ins + both new nodes).
4. **Unity's Auto Refresh detection latency is not reliably fast.** Most live writes were noticed
   within ~1.5s; one (the Action node, applied immediately after the Condition node's own
   asmdef-creating reload) was not noticed for over a minute even after repeated `check` calls,
   until an explicit `refresh_unity` (a capability only available to *this session's own* Unity MCP
   tooling, not to this card's own bridge) forced it. Disclosed as a real, inherent characteristic
   of relying on passive Auto Refresh rather than main-thread-dispatched `Refresh()`: a caller
   should retry `analyze-and-compile-node`'s `check` with real patience (tens of seconds, not a
   fixed short timeout), especially right after a heavy prior compile.

## Real gap found and disclosed, not silently worked around

**The official `@modelcontextprotocol/inspector` CLI's `tools/call` round trip against
`aibt_generate_node` timed out** (60s) even though the underlying `McpToolDispatcher.Dispatch` call
it should reach responds instantly (confirmed by calling the identical request directly via Unity
MCP `execute_code`, the same production entry point `McpBridgeListener.ServeClient` itself calls).
The `--tool-args-json` workaround `P6-006`/`P6-007`'s own evidence documented did not resolve it
either in this session's shell environment. Rather than spend further session time debugging a
third-party CLI's own transport behavior, live end-to-end verification for this card used direct
`execute_code`-driven calls to the real `McpToolDispatcher.Dispatch` entry point instead -- the
same technique `P6-001`'s own evidence already used when the Inspector CLI path proved awkward for
a different reason. `tools/list` via the CLI *did* succeed and confirmed all 6 new tools register
with real schemas, so the server/bridge/tool-registration stack itself is proven working; only the
specific `tools/call` round trip for this one tool was not exercised through the CLI.

## Unity EditMode tests (18, all real, run live against `6000.5.8f1`)

`Tests/Editor/Mcp/NodeDevelopment/McpNodeDevelopmentToolDispatcherTests.cs`, calling the real
`McpToolDispatcher.Dispatch` entry point. Scoped (disclosed) to what is reliably testable
synchronously: `generate-node` (both kinds, exact expected attribute usage, single-slot overwrite
behavior), `preview-node-diff` (before/after no-persistence proof), `generate-node-tests-and-manifest`
(scaffold + no-pending-generation refusal), `analyze-and-compile-node`'s `start`/malformed-mode
paths, `test-node`/`apply-node`'s stale-hash refusal paths, and the full 6-tool permission-negative
matrix. The full gate through a real compile (`check` reaching `compiled`, `test-node`/`apply-node`'s
happy path) triggers a genuine Unity domain reload that can outlive a single NUnit EditMode test
method -- no other P6 card's tools ever compiled anything, so none needed this; live verification
(below) exercises that path instead, matching this card's own "real MCP client" requirement.

## Regression

`AIBT.BehaviorCases.Tests` + `AIBT.Integration.Tests` + `AIBT.Runtime.Tests` + `AIBT.Editor.Tests`
(which now also holds `Tests/Editor/Mcp/NodeDevelopment/`): **1015/1015 passed, 0 failed, 0
skipped**, run twice (once after the dispatcher landed, once again after the `StagingSlot`
asmdef-per-destination fix) -- proves the shared bridge changes (`McpBridgeListener.cs`,
`McpBridgeAutoRestart.cs`) broke nothing else in the MCP surface.

## Live end-to-end verification (real running `6000.5.8f1` Editor, real domain reloads)

Both maintained templates exercised through the complete gate, each ending in a real applied,
compiled, registry-searchable node:

- **Condition** (`aibt.mcp-live-test.above-threshold`, typed `UInt32` blackboard read): generate ->
  preview (content matched, staging file count unchanged) -> analyze-and-compile (`start`/`check`
  cycle, real `AIBT50xx`/`CS0103`/`CS0246` diagnostics on the first attempt, clean `contentHash` on
  the second after the namespace fix) -> test (`valid: true`, real registry-materialization check)
  -> apply (moved to `Assets/AibtMcpLiveTest/GeneratedNodes/AboveThreshold/` with its own fresh
  asmdef) -> confirmed compiling clean at the real destination -> confirmed present
  (`aibt.mcp-live-test.above-threshold`) in a fresh registry built purely from the applied,
  reflected metadata.
- **Action** (`aibt.mcp-live-test.copy-and-notify`, typed read/write, command emission, async
  start/completion, cancellation): same full gate, same outcome -- applied to
  `Assets/AibtMcpLiveTest/GeneratedNodes/CopyAndNotify/`, confirmed compiling clean, confirmed
  registry-present.
- Both applied nodes confirmed compiling **together**, with separate asmdefs (the `AIBT5011` fix),
  and confirmed **together** in one combined real registry build (13 entries).
- All live-created artifacts (both applied node folders, the staging slot, the discovery file)
  cleaned up afterward; final compile confirmed clean with 0 console errors.

## Verification

```text
Unity EditMode: AIBT.Tests.Editor.Mcp.NodeDevelopment.McpNodeDevelopmentToolDispatcherTests --
  18/18 passed
Unity EditMode: AIBT.BehaviorCases.Tests + AIBT.Integration.Tests + AIBT.Runtime.Tests +
  AIBT.Editor.Tests -- 1015/1015 passed, no regressions (run twice)
dotnet build MCP~/Server -- 0 warnings, 0 errors
Live: real running Editor, real domain reloads, direct McpToolDispatcher.Dispatch calls (the same
  entry point McpBridgeListener.ServeClient itself calls) --
  full generate->preview->analyze->compile->test->apply gate, Condition node, ending in a real
    compiled, registry-searchable applied node
  full gate again, Action node, same outcome
  both applied nodes compiling together (post AIBT5011 fix) and registry-searchable together
  McpBridgeAutoRestart verified surviving two real domain reloads (new port each time, confirmed
    reachable via raw external TCP test)
  live-created artifacts cleaned up; final compile clean, 0 console errors
Live (partial): Inspector CLI tools/list confirmed all 6 new tools register with real schemas;
  tools/call round trip for aibt_generate_node timed out (CLI-side, not reproduced when calling
  the identical request directly) -- disclosed, not silently worked around
Tools~/Verification/Verify-Static.ps1 -- passed, 105 work items
```

## Scope and limitations

- `test-node` proves compile-clean + registry-materialization-valid, not dispatch execution --
  deferred to `P6-022`.
- `generate-node`/`generate-node-tests-and-manifest` support exactly the two maintained templates
  (Condition: typed blackboard read + optional observer; Action: typed read/write, command
  emission, async start/completion, cancellation), parametrized over 5 built-in scalar types
  (`Bool`/`Int32`/`UInt32`/`Float32`/`Float64`) -- not the full `burst-node-abi-v1.md` vocabulary
  (registered/opaque value types, Float2/Float3/Quaternion, AgentId/EntityId/AssetId are out of
  scope for this card's own two templates).
- `analyze-and-compile-node`'s Auto-Refresh-detection latency is not bounded or guaranteed fast;
  callers should retry `check` with real patience rather than a short fixed timeout.
- A destination path already inside an existing asmdef's own folder does not get a second asmdef
  written (detected by walking up from the destination to the project root); a destination outside
  any existing asmdef always gets a fresh one.
- Single client, single Unity instance at a time, same as every prior P6 card's own disclosed
  scope, unchanged here.
