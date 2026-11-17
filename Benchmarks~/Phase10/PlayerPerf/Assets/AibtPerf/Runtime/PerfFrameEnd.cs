using System;
using System.Diagnostics;
using System.Collections.Generic;
using AIBT.Samples.SimpleAgent;
using UnityEngine;

namespace AIBT.Perf
{
    /// <summary>Last script in LateUpdate: closes the bracket and lets the runner act outside it.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class PerfFrameEnd : MonoBehaviour
    {
        public PerfRunner runner;

        private void LateUpdate()
        {
            PerfClock.EndTicks = PerfClock.Watch.ElapsedTicks;
            PerfClock.EndAlloc = GC.GetAllocatedBytesForCurrentThread();
            PerfClock.EndHeap = GC.GetTotalMemory(false);
            PerfClock.EndGc = GC.CollectionCount(0);
            if (runner != null) runner.AfterFrame();
        }
    }
}
