using System;
using System.Diagnostics;
using System.Collections.Generic;
using AIBT.Samples.SimpleAgent;
using UnityEngine;

namespace AIBT.Perf
{
    /// <summary>Central "game code": runs early in Update and times itself.</summary>
    [DefaultExecutionOrder(-31000)]
    public sealed class PerfFeeder : MonoBehaviour
    {
        public FeederMode mode;
        public readonly List<BehaviorTreeAgent> Managed = new List<BehaviorTreeAgent>();
        public readonly List<NativeTreeAgent> Native = new List<NativeTreeAgent>();
        public int allocCount;
        private byte[][] _sink = new byte[0][];

        private void Update()
        {
            if (mode == FeederMode.None) return;
            var visible = (Time.frameCount / 120) % 2 == 0;
            var alloc = GC.GetAllocatedBytesForCurrentThread();
            var start = PerfClock.Watch.ElapsedTicks;
            switch (mode)
            {
                case FeederMode.ManagedBlackboardWrite:
                    for (var i = 0; i < Managed.Count; i++) Managed[i].Blackboard.Set("target-visible", visible);
                    break;
                case FeederMode.ManagedManualTick:
                    var dt = Time.deltaTime;
                    for (var i = 0; i < Managed.Count; i++) Managed[i].Tick(dt);
                    break;
                case FeederMode.NativeFactBool:
                    for (var i = 0; i < Native.Count; i++) Native[i].SetFact("target-visible", visible);
                    break;
                case FeederMode.NativeFactDistance:
                    for (var i = 0; i < Native.Count; i++) Native[i].SetFact("distance", 100f);
                    break;
                case FeederMode.AllocProbe:
                    if (_sink.Length != allocCount) _sink = new byte[allocCount][];
                    for (var i = 0; i < allocCount; i++) _sink[i] = new byte[40];
                    break;
            }
            PerfClock.FeederTicks = PerfClock.Watch.ElapsedTicks - start;
            PerfClock.FeederAlloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
        }
    }
}
