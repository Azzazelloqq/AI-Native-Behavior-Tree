using System;
using System.Diagnostics;
using System.Collections.Generic;
using AIBT.Samples.SimpleAgent;
using UnityEngine;

namespace AIBT.Perf
{
    /// <summary>First script in Update: opens the frame bracket.</summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class PerfFrameBegin : MonoBehaviour
    {
        private void Update()
        {
            PerfClock.FeederTicks = 0;
            PerfClock.FeederAlloc = 0;
            PerfClock.BeginGc = GC.CollectionCount(0);
            PerfClock.BeginHeap = GC.GetTotalMemory(false);
            PerfClock.BeginAlloc = GC.GetAllocatedBytesForCurrentThread();
            PerfClock.BeginTicks = PerfClock.Watch.ElapsedTicks;
        }
    }
}
