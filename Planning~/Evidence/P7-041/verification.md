# P7-041 verification

`ProductionTreeWorldEffectTests` passed 6/6 in 0.888 s: exactly-once post-commit delivery, stable
effect descriptor/rejection, grouped attribution, immediate frame ordering, pipelined idle-barrier
ordering and warmed drain allocation. Unity MCP then proved a live world/host path and existing trace
attachment. `Verify-Static.ps1` passed.

The full host-project EditMode run reported 1,806 total / 1,801 passed / 5 failed. Two old generated
API documentation checks were repaired by regeneration and `AIBT.Editor.Tests` subsequently passed
436/436. The remaining failures are two `GeneratedArtifactContractTests` where the embedded
`Assets/AIBT` import context makes `PackageInfo.FindForAssembly` null, plus unrelated
`LocalSaveSystem.Tests.SaveStore_AutoSave_WritesToDisk` (expected 9, actual 0). They preclude a clean
host-project full-regression green result, are outside P7-041 ownership, and were not hidden.

After the final asset refresh, the MCP connection briefly disconnected while the repeat test job
continued. On reconnection it reported all 1,806 tests complete and exactly the same three failures
(not capped); no P7-041 failure appeared. The job omitted its pass/skip summary, so no derived pass
count is recorded.
