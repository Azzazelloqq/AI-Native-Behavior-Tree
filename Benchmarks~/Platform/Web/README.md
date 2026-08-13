# Web platform pilot

This directory records the P0-003 one-run WebGL pilot. It is compatibility evidence and an initial measurement, not a stable performance baseline.

The non-development Unity `6000.5.8f1` IL2CPP Player ran 25 warm-up cycles followed by 250 measured cycles per policy. Each policy executed 6,250 semantic steps of the accepted representative parallel/decorator case. Chrome and Firefox used different headless graphics routes, so their values must not be compared as browser rankings.

`SingleThreadBudgeted(1)` intentionally crosses the JavaScript/Player update boundary many more times than immediate execution. The observed cost is therefore expected and does not establish a production budget threshold.

No generation-0 collection occurred inside the measured windows, but coarse managed-heap deltas were nonzero. The metric uses `GC.GetTotalMemory`; it neither locates allocations nor proves zero allocation. Native memory was not measured because this spike exercises the managed reference executor.

Exact results and limitations are in `pilot-results.json`.

