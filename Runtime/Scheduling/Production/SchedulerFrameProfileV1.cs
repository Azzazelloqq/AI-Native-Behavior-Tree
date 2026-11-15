using System.Diagnostics;

namespace AIBT
{
    /// <summary>
    /// P10-002 measurement aid: a lap timer that splits a <see cref="ProductionTreeScheduler"/> frame
    /// into stages (Immediate host work, and each step of a BatchedJobsSameFrame round). It exists only
    /// in builds that define <c>AIBT_SCHEDULER_FRAME_PROFILE</c>; without that define every call is
    /// removed by the compiler (<see cref="ConditionalAttribute"/>), so release code is unchanged.
    /// Each <see cref="Lap"/> charges the time since the previous lap to the stage it names, so the
    /// stages partition the instrumented time. Main thread only.
    /// </summary>
    internal static class SchedulerFrameProfileV1
    {
        internal enum Stage
        {
            FrameStart,
            WorldBridge,
            DueCollect,
            Bookkeeping,
            HostBegin,
            HostAdvance,
            HostRecord,
            HostDispatch,
            HostComplete,
            GroupSetup,
            RoundBegin,
            RoundAllocate,
            RoundSchedule,
            RoundComplete,
            RoundDispose,
            RoundHandle,
            RoundResolve,
            Count,
        }

        internal enum Counter
        {
            Frames,
            ImmediateHosts,
            BatchedGroups,
            Rounds,
            Lanes,
            DispatchMembers,
            Count,
        }

#if AIBT_SCHEDULER_FRAME_PROFILE
        private static readonly long[] s_ticks = new long[(int)Stage.Count];
        private static readonly long[] s_counts = new long[(int)Counter.Count];
        private static long s_last;
#endif

        [Conditional("AIBT_SCHEDULER_FRAME_PROFILE")]
        internal static void Start()
        {
#if AIBT_SCHEDULER_FRAME_PROFILE
            s_last = Stopwatch.GetTimestamp();
#endif
        }

        [Conditional("AIBT_SCHEDULER_FRAME_PROFILE")]
        internal static void Lap(Stage stage)
        {
#if AIBT_SCHEDULER_FRAME_PROFILE
            var now = Stopwatch.GetTimestamp();
            s_ticks[(int)stage] += now - s_last;
            s_last = now;
#endif
        }

        [Conditional("AIBT_SCHEDULER_FRAME_PROFILE")]
        internal static void Add(Counter counter, long value)
        {
#if AIBT_SCHEDULER_FRAME_PROFILE
            s_counts[(int)counter] += value;
#endif
        }

#if AIBT_SCHEDULER_FRAME_PROFILE
        /// <summary>Clears every stage and counter (read by the benchmark harness through reflection).</summary>
        [UnityEngine.Scripting.Preserve]
        internal static void Reset()
        {
            for (var index = 0; index < s_ticks.Length; index++) s_ticks[index] = 0;
            for (var index = 0; index < s_counts.Length; index++) s_counts[index] = 0;
        }

        /// <summary>Totals since <see cref="Reset"/> as a JSON object: stage microseconds and counters.</summary>
        [UnityEngine.Scripting.Preserve]
        internal static string Snapshot()
        {
            var text = new System.Text.StringBuilder(512);
            text.Append('{');
            for (var index = 0; index < s_ticks.Length; index++)
            {
                if (index > 0) text.Append(',');
                text.Append('"').Append(((Stage)index).ToString()).Append("Us\":")
                    .Append((s_ticks[index] * 1000000.0 / Stopwatch.Frequency).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            for (var index = 0; index < s_counts.Length; index++)
                text.Append(",\"").Append(((Counter)index).ToString()).Append("\":").Append(s_counts[index]);
            text.Append('}');
            return text.ToString();
        }
#endif
    }
}
