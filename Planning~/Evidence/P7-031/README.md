# P7-031 verification checkpoint

Status: **InProgress**, 2026-09-04. No commit or push. Existing P7-029/P7-030 and unrelated
host-project changes are preserved. The compile/path fixes are verified; the complete TCP recipe
has two pre-existing integration blockers requiring the proposed scope extension below.

## Implemented

- Assets-relative canonical destination validation before mutation; rooted/drive-relative paths,
  escapes, ambiguous components, existing destinations and link/reparse ancestry are rejected.
- One Library-backed attemptId bound to staged relative paths and contents. The main-thread hook
  imports and explicitly requests compilation after any current compile. Both staging assemblies
  must be verified by compiler events. assemblyCompilationNotRequired is valid up-to-date evidence
  for that requested attempt; any rebuilt assembly requires reload before success is exposed.
- The actual Editor log supplies diagnostics only. Unknown/superseded/restarted or changed-content
  attempts cannot claim success. Test/apply retain hash and registry checks.
- Server check arguments, recipe source/generated output and contract documentation updated.

## Verification

- Unity compilation succeeds after fixing missing exception namespace imports. Initial errors
  were in this implementation; the cause of the owner's Editor crash was not established.
- Initial focused run: 40/41, one NUnit Count-constraint misuse; corrected without changing the
  expected count. After the fix: 41/41. Live up-to-date compiler events exposed a tracker omission;
  two regression cases were added and both event paths are now handled.
- Final focused EditMode: **43/43 passed**, zero failed/skipped, job
  fccf43ebd210466ebee499ae11c4175c.
- Full EditMode: **1709/1712 passed, 3 failed, 0 skipped**; AIBT: **1342/1344**.
  Fresh Unity TestResults.xml covers 14:10:22–14:11:45 UTC. MCP lost callback state on domain
  reload and reported initialization failure; the completed XML is the result authority.
  The same two CodeGen PackageInfo assertions and LocalSaveSystem autosave assertion failed
  as in P7-029. Exact names/messages and job identity are in verification-results.json.
- Server build: dotnet build Assets/AIBT/MCP~/Server --no-restore, 0 warnings/errors.
- Static verification: Verify-Static.ps1 passed, 7 schemas and 137 work items.
- Production C# path probe through PowerShell .NET: **15/15**, including actual Windows destination
  and staging junctions and unchanged source/target on refusal. See path-verification.json.
  This is additional filesystem proof, not part of the Unity test count.
- Full generated documentation regenerated in Unity with McpDocumentationRegenerateCommand.
- git diff --check passed.

## Live recipe

See live-verification.json. An initially absent staging slot was used for a disposable UInt32
condition. Generate, preview and scaffold calls went through the real background TCP bridge.

- start/check returned pending and then compiled, including when both assemblies were already
  compiled before start. The attempt persisted through import/domain reload and repeated checks.
- A deliberate #error returned failed with CS1029 diagnostics. Restoring the source and starting
  again recovered to compiled. Changed staging and a stale apply hash were refused with AIBT9032.
- Without a loaded staging assembly, apply refused with AIBT9035.
- TCP test_node failed with AIBT9013 because GenericNodeDispatchRunner uses Allocator.Temp on the
  background bridge thread. The same request on the main thread returned valid=true,
  dispatchProven=true, Enter success and Tick Success. This is not a passing TCP test_node.
- TCP apply moved the expected node/test/asmdef into Assets/P731-LiveVerification. The next compile
  failed AIBT5011/CS0246 because the staging-only companion catalog still referenced the moved
  shard. After removing the owned staging fixture, the applied assembly compiled and loaded
  both the generated node and shard. This does not make the unmodified full recipe pass.
- All created staging/applied source fixtures were removed. The Editor returned to compiling
  without errors before automated regression tests. No user staging files were overwritten.

## Remaining acceptance gate

The agreed compile/path implementation does not fix GenericNodeDispatchRunner's allocator or
the pre-existing staging companion cleanup. The current card's file/packaging scope was not
silently broadened. See [follow-up-proposal.md](follow-up-proposal.md) for a bounded extension:
background-compatible owned allocation, successful-apply-only companion cleanup, behavior
regressions and a fresh complete TCP/post-apply verification. P7-031 must stay InProgress until
that required recipe passes. P7-032 remains subsequent work.
