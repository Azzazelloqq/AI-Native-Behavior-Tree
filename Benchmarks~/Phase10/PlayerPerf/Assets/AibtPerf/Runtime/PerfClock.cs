using System;
using System.Diagnostics;
using System.Collections.Generic;
using AIBT.Samples.SimpleAgent;
using UnityEngine;

namespace AIBT.Perf
{
    /// <summary>Process-wide timing state shared by the bracket scripts, the feeder and the runner.</summary>
    public static class PerfClock
    {
        public static readonly Stopwatch Watch = Stopwatch.StartNew();
        public static long BeginTicks;
        public static long EndTicks;
        public static long BeginAlloc;
        public static long EndAlloc;
        public static long BeginHeap;
        public static long EndHeap;
        public static int BeginGc;
        public static int EndGc;
        public static long FeederTicks;
        public static long FeederAlloc;
        public static long EffectCount;
        public static long SpottedCount;
        public static object Sink;

        public static double TicksToMicroseconds(long ticks) => ticks * 1000000.0 / Stopwatch.Frequency;
    }

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

    public enum FeederMode
    {
        None,
        ManagedBlackboardWrite,
        ManagedManualTick,
        NativeFactBool,
        NativeFactDistance,
        AllocProbe,
    }

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

    public enum ApiRole { Guard, Wanderer, Scout }

    /// <summary>
    /// Approximate Unity API floor of the realistic scene without any tree: per frame the same kind
    /// of transform reads/writes the movement nodes perform, plus one raycast for a scout.
    /// </summary>
    public sealed class PerfApiOnly : MonoBehaviour
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        public ApiRole role;
        public Vector3 center;
        public Transform other;
        private Vector3 _goal;
        private uint _seed;

        private void Start()
        {
            _seed = (uint)(transform.position.GetHashCode()) * 2654435761u + 1u;
            _goal = NextGoal();
        }

        private Vector3 NextGoal()
        {
            _seed ^= _seed << 13; _seed ^= _seed >> 17; _seed ^= _seed << 5;
            var a = (_seed % 6283u) / 1000f;
            return center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 4f;
        }

        private void Update()
        {
            var body = transform;
            var position = body.position;
            if (role == ApiRole.Scout && other != null)
            {
                var eye = position + Vector3.up * 0.5f;
                var offset = other.position - eye;
                var distance = offset.magnitude;
                if (distance > 0.001f)
                    Physics.RaycastNonAlloc(eye, offset / distance, Hits, distance, -1, QueryTriggerInteraction.Ignore);
            }
            var next = Vector3.MoveTowards(position, _goal, 3f * Time.deltaTime);
            var delta = next - position;
            body.position = next;
            if (delta.sqrMagnitude > 1e-8f) body.rotation = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(delta), 720f * Time.deltaTime);
            if ((next - _goal).sqrMagnitude < 0.01f) _goal = NextGoal();
        }
    }
}
