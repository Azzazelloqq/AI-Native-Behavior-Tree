# Showcase trees

These examples use only production nodes registered by `NodeRegistryBuilder.CreateWithBuiltIns()`.
They carry separate semantic and presentation files so moving a box never changes behavior.

## Timed Sequence

`timed-sequence.aibt.json` runs three `aibt.stdlib.wait` leaves in a memory sequence. The configured
waits take 2, 1 and 3 ticks. The tree remains Running across updates and completes with Success after
the last wait. This demonstrates retained composite progress and tick-counted actions; ticks are
logical updates, not wall-clock duration.

## Parallel and Decorators

`parallel-decorators.aibt.json` uses `require-all-success`. One branch repeats a short wait twice,
one converts an expected Failer result to Success through Inverter, and one waits for three ticks.
Parallel visits branches in order during an update; it does not imply simultaneous execution or
worker-thread concurrency. The root succeeds after every branch succeeds.

## Open and inspect

1. Import **Showcase Trees** through Unity Package Manager, or use these source paths in an embedded package.
2. Open **AIBT > Graph Editor**.
3. Select an imported tree in the project tree browser, or choose **Open Tree** and select the file.
   The neighboring `.aibt.layout.json` is loaded automatically when valid.

The experimental graph window supports initial editing, saving incomplete drafts, and explicitly
saving node positions with **Save Layout**. Project node contracts are discovered automatically;
the preview and live debugging workflows are not yet integrated into this window.
Both examples target the reference compiler and the native production catalog. Runtime hosting and
scheduler selection are documented separately in `Documentation~/production-host.md`.
