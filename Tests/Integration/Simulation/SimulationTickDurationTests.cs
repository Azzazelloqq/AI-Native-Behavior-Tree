using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Simulation;
using AIBT.Tests.Integration.NativeRuntime;
using NUnit.Framework;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// P12-001: durations in ticks in the simulation container. The container clock is
    /// <c>tick × TickMicroseconds</c> and every library duration is <c>deadline = now + d</c>, reached when
    /// <c>now &gt;= deadline</c>. So (a) a duration of <c>n × TickMicroseconds</c> ends after exactly n ticks,
    /// (b) with <c>TickMicroseconds = 1</c> every "microseconds" parameter is a tick count, and a duration
    /// that is not a multiple rounds up. Under <see cref="ThinkSchedule.Spread"/> a duration ends at the
    /// agent's first think at or after its deadline. <c>aibt.stdlib.wait</c> counts its own Tick calls
    /// (thinks), not ticks.
    /// Every probe tree emits <c>aibt.native.signal</c> 1 when its timer starts and 2 when it ends (the
    /// set-deadline and cooldown probes emit 7 each time they fire), then holds in <c>aibt.native.idle</c>.
    /// Each case runs in place and in the agent-frame job and must give the same ticks. Evidence:
    /// <c>Planning~/Evidence/P12-001/README.md</c>.
    /// </summary>
    public sealed class SimulationTickDurationTests
    {
        private const string S = "aibt.native.";
        private const uint Tick30Hz = 33333u;
        private const int Agents = 5;
        private const uint Ticks = 40;
        private static readonly CommandChannel<int> Signal = CommandChannel<int>.Create("aibt.native.signal");

        // ---- probe trees ------------------------------------------------------------------------------

        private static string Hold(string id, string timer, params string[] timerNodes)
            => Tree("tests.p12001." + id, "seq", new[] { Bool("flag"), Long("cd") },
                new[]
                {
                    Composite("seq", "aibt.core.memory-sequence", "s1", timer, "s2", "h"),
                    Leaf("s1", S + "signal", "\"code\": 1"),
                    Leaf("s2", S + "signal", "\"code\": 2"),
                    Leaf("h", S + "idle"),
                }.Concat(timerNodes).ToArray());

        private static string WaitMicroseconds(ulong d)
            => Hold("wait-us", "w", Leaf("w", S + "wait-microseconds", "\"duration-microseconds\": " + d));

        // The flag is never set: the wait fails at its timeout, the inverter turns that into Success.
        private static string WaitUntilTrueTimeout(ulong d)
            => Hold("wait-until-true", "inv",
                Composite("inv", "aibt.core.inverter", "w"),
                Leaf("w", S + "wait-until-true", "\"timeout-microseconds\": " + d, "\"key\": \"flag\""));

        private static string WaitRandom(ulong min, ulong max)
            => Hold("wait-random", "w", Leaf("w", S + "wait-random-microseconds", "\"min-microseconds\": " + min + ", \"max-microseconds\": " + max));

        private static string Timeout(ulong d)
            => Hold("timeout", "t",
                CompositeWith("t", "aibt.core.timeout", "\"durationMicroseconds\": " + d + ", \"terminalResult\": \"success\"", "w"),
                Leaf("w", S + "idle"));

        private static string StdlibWait(uint ticks)
            => Hold("stdlib-wait", "w", Leaf("w", "aibt.stdlib.wait", "\"ticks\": " + ticks));

        // Fires (set-deadline, signal 7) whenever the deadline in "cd" has passed; the v1 reactive
        // composites re-check deadline-passed at every think. Fires at its first think (cd = 0).
        private static string Deadline(ulong d)
            => Tree("tests.p12001.deadline", "root", new[] { Long("cd") },
                Composite("root", "aibt.core.reactive-selector", "fire", "rest"),
                Composite("fire", "aibt.core.reactive-sequence", "ready", "go"),
                Leaf("ready", S + "deadline-passed", null, "\"key\": \"cd\""),
                Composite("go", "aibt.core.memory-sequence", "start", "say", "hold"),
                Leaf("start", S + "set-deadline", "\"duration-microseconds\": " + d, "\"key\": \"cd\""),
                Leaf("say", S + "signal", "\"code\": 7"),
                Leaf("hold", S + "idle"),
                Leaf("rest", S + "idle"));

        // Fires (signal 7, then fail) whenever the cooldown lets the child in; the deadline starts on entry.
        private static string CooldownOnEnter(ulong d)
            => Tree("tests.p12001.cooldown-enter", "root", Array.Empty<string>(),
                Composite("root", "aibt.core.reactive-selector", "gate", "rest"),
                CompositeWith("gate", "aibt.core.cooldown", "\"durationMicroseconds\": " + d + ", \"blockedResult\": \"failure\", \"startPolicy\": \"on-enter\"", "fire"),
                Composite("fire", "aibt.core.memory-sequence", "say", "no"),
                Leaf("say", S + "signal", "\"code\": 7"),
                Leaf("no", S + "fail"),
                Leaf("rest", S + "idle"));

        // The same with the deadline started when the child succeeds; the inverter keeps the selector on "rest".
        private static string CooldownOnSuccessfulExit(ulong d)
            => Tree("tests.p12001.cooldown-exit", "root", Array.Empty<string>(),
                Composite("root", "aibt.core.reactive-selector", "gate", "rest"),
                Composite("gate", "aibt.core.inverter", "cd"),
                CompositeWith("cd", "aibt.core.cooldown", "\"durationMicroseconds\": " + d + ", \"blockedResult\": \"success\", \"startPolicy\": \"on-successful-exit\"", "fire"),
                Composite("fire", "aibt.core.memory-sequence", "say", "ok"),
                Leaf("say", S + "signal", "\"code\": 7"),
                Leaf("ok", S + "succeed"),
                Leaf("rest", S + "idle"));

        /// <summary>A time node or decorator and the code its start and end signals carry.</summary>
        public sealed class Probe
        {
            internal string Name;
            internal Func<ulong, string> Json;
            internal int StartCode = 1;
            internal int EndCode = 2;

            public override string ToString() => Name;
        }

        private static readonly Probe[] TimeProbes =
        {
            new Probe { Name = "wait-microseconds", Json = WaitMicroseconds },
            new Probe { Name = "wait-until-true timeout", Json = WaitUntilTrueTimeout },
            new Probe { Name = "set-deadline + deadline-passed", Json = Deadline, StartCode = 7, EndCode = 7 },
            new Probe { Name = "wait-random-microseconds (min = max)", Json = d => WaitRandom(d, d) },
            new Probe { Name = "aibt.core.timeout", Json = Timeout },
            new Probe { Name = "aibt.core.cooldown on-enter", Json = CooldownOnEnter, StartCode = 7, EndCode = 7 },
            new Probe { Name = "aibt.core.cooldown on-successful-exit", Json = CooldownOnSuccessfulExit, StartCode = 7, EndCode = 7 },
        };

        private static IEnumerable<Probe> Probes() => TimeProbes;

        // ---- runner ---------------------------------------------------------------------------------

        private sealed class Outcome
        {
            internal readonly List<(uint Tick, int Agent, int Code)> Signals = new List<(uint, int, int)>();
            internal readonly List<(uint Tick, int Agent)> Thinks = new List<(uint, int)>();
            internal int Faults;
            internal long JobBatches;
            internal AgentFault[] FaultList = Array.Empty<AgentFault>();
        }

        private static Outcome Run(NativeLibraryKit.Prepared prepared, uint tickMicroseconds, bool spread, bool useJobs,
            IEnumerable<uint> ticks, int agents = Agents, Action<uint, TreeGroup, AgentHandle[]> beforeStep = null, ulong seed = 0)
        {
            var outcome = new Outcome();
            var settings = TreeSimulationSettings.Default(tickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = 1;
            settings.RandomSeed = seed;
            using (var simulation = new TreeSimulation(settings))
            {
                Assert.That(simulation.TryAddGroup(prepared.Program, TreeGroupSettings.Default(agents), out var group), Is.EqualTo(SimulationResult.Success));
                var handles = new AgentHandle[agents];
                for (var agent = 0; agent < agents; agent++)
                    Assert.That(group.TryAdd(spread ? ThinkSchedule.Spread(5) : ThinkSchedule.Every(1, 0), out handles[agent]), Is.EqualTo(SimulationResult.Success));
                var faults = new List<AgentFault>();
                foreach (var tick in ticks)
                {
                    beforeStep?.Invoke(tick, group, handles);
                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    Assert.That(group.TryGetCommands(Signal, out var commands), Is.EqualTo(SimulationResult.Success));
                    for (var item = 0; item < commands.Length; item++)
                        outcome.Signals.Add((tick, commands[item].Agent.Index, commands[item].Value));
                    var stepFaults = group.GetFaults();
                    for (var item = 0; item < stepFaults.Length; item++) faults.Add(stepFaults[item]);
                    for (var agent = 0; agent < agents; agent++)
                    {
                        if (ThoughtOn(group, handles[agent], tick)) outcome.Thinks.Add((tick, agent));
                    }
                }
                outcome.Faults = faults.Count;
                outcome.FaultList = faults.ToArray();
                outcome.JobBatches = group.JobBatchesForTests;
            }
            return outcome;
        }

        private static readonly ActivePathNode[] PathBuffer = new ActivePathNode[64];

        /// <summary>True when the agent thought in the step of <paramref name="tick"/> (its debug state, read between steps).</summary>
        private static bool ThoughtOn(TreeGroup group, AgentHandle agent, uint tick)
        {
            Assert.That(group.TryGetActivePath(agent, PathBuffer, out _, out var state), Is.EqualTo(SimulationResult.Success));
            return state.HasThought && state.LastThinkTick == tick;
        }

        private static IEnumerable<uint> Range(uint first, uint last)
        {
            for (var tick = first; tick <= last; tick++) yield return tick;
        }

        private static uint FirstTick(Outcome outcome, int agent, int code)
            => outcome.Signals.First(s => s.Agent == agent && s.Code == code).Tick;

        /// <summary>The timer of <paramref name="agent"/>: the start signal's tick and the end signal's tick after it.</summary>
        private static (uint Start, uint End) Timer(Outcome outcome, Probe probe, int agent)
        {
            var mine = outcome.Signals.Where(s => s.Agent == agent).ToArray();
            var start = mine.First(s => s.Code == probe.StartCode);
            var end = mine.Where(s => s.Code == probe.EndCode && s.Tick > start.Tick).Select(s => s.Tick).DefaultIfEmpty(0u).First();
            return (start.Tick, end);
        }

        private static Outcome RunBothPaths(NativeLibraryKit.Prepared prepared, uint tickMicroseconds, bool spread, string label,
            IEnumerable<uint> ticks = null, Action<uint, TreeGroup, AgentHandle[]> beforeStep = null, int agents = Agents, ulong seed = 0)
        {
            var list = (ticks ?? Range(1, Ticks)).ToArray();
            var inPlace = Run(prepared, tickMicroseconds, spread, false, list, agents, beforeStep, seed);
            var jobs = Run(prepared, tickMicroseconds, spread, true, list, agents, beforeStep, seed);
            Assert.That(inPlace.Faults, Is.Zero, label + ": no fault in place");
            Assert.That(jobs.Faults, Is.Zero, label + ": no fault with jobs");
            Assert.That(inPlace.JobBatches, Is.Zero, label + ": UseJobs=false runs on the calling thread");
            Assert.That(jobs.JobBatches, Is.GreaterThan(0), label + ": UseJobs with MinimumJobBatch=1 ran the agent-frame job");
            Assert.That(jobs.Signals, Is.EqualTo(inPlace.Signals), label + ": the same signals on the same ticks in place and in jobs");
            Assert.That(jobs.Thinks, Is.EqualTo(inPlace.Thinks), label + ": the same thinks in place and in jobs");
            return inPlace;
        }

        // ---- (a) and (b): exact n ticks, every agent thinking every tick --------------------------------

        /// <summary>
        /// Recipe (a) at 30 Hz (d = n × 33333) and recipe (b) (TickMicroseconds = 1, d = n): the timer ends on
        /// the tick n after the tick it started, not n − 1 and not n + 1, for every time node and decorator.
        /// </summary>
        [Test]
        public void ExactMultipleOfTickMicroseconds_EndsAfterExactlyNTicks_EveryThink([ValueSource(nameof(Probes))] Probe probe,
            [Values(1u, 7u, 10u)] uint n, [Values(Tick30Hz, 1u)] uint tickMicroseconds)
        {
            using (var kit = new NativeLibraryKit())
            {
                var prepared = kit.Prepare(probe.Json((ulong)n * tickMicroseconds));
                var outcome = RunBothPaths(prepared, tickMicroseconds, spread: false, probe.Name + " n=" + n + " T=" + tickMicroseconds);
                for (var agent = 0; agent < Agents; agent++)
                {
                    var timer = Timer(outcome, probe, agent);
                    Assert.That(timer.Start, Is.EqualTo(1u), probe.Name + ": every agent thinks at tick 1 and starts its timer");
                    Assert.That(timer.End - timer.Start, Is.EqualTo(n), probe.Name + ": d = " + n + " × " + tickMicroseconds + " ends exactly " + n + " ticks later");
                }
            }
        }

        /// <summary>
        /// A duration that is not a multiple rounds up to the next tick: d = n·T − 1 still ends after n ticks
        /// (and does not end after n − 1), d = n·T + 1 ends after n + 1. At 30 Hz "one second" (1 000 000 µs)
        /// is 31 ticks, 30 ticks is 999 990 µs.
        /// </summary>
        [Test]
        public void NonMultiple_RoundsUpToTheNextTick([ValueSource(nameof(Probes))] Probe probe)
        {
            using (var kit = new NativeLibraryKit())
            {
                foreach (var (d, ticks) in new[]
                         {
                             (7ul * Tick30Hz - 1ul, 7u), (7ul * Tick30Hz + 1ul, 8u), (1_000_000ul, 31u), (999_990ul, 30u),
                         })
                {
                    var outcome = RunBothPaths(kit.Prepare(probe.Json(d)), Tick30Hz, spread: false, probe.Name + " d=" + d, Range(1, 45));
                    for (var agent = 0; agent < Agents; agent++)
                    {
                        var timer = Timer(outcome, probe, agent);
                        Assert.That(timer.End - timer.Start, Is.EqualTo(ticks), probe.Name + ": d = " + d + " µs at 33333 µs per tick ends after ceil(d / T) ticks");
                    }
                }
            }
        }

        // ---- Spread(5): the next think at or after the deadline ------------------------------------------

        /// <summary>
        /// Five agents with Spread(5) (phases 0 to 4, each thinking every 5 ticks): the timer ends at the
        /// agent's first think at or after start + n, never earlier and at most 4 ticks later (n = 1: 4 late,
        /// n = 7: 3 late, n = 10: on time). Both recipes.
        /// </summary>
        [Test]
        public void UnderSpread5_EndsAtTheFirstThinkAtOrAfterTheDeadline([ValueSource(nameof(Probes))] Probe probe,
            [Values(1u, 7u, 10u)] uint n, [Values(Tick30Hz, 1u)] uint tickMicroseconds)
        {
            using (var kit = new NativeLibraryKit())
            {
                var prepared = kit.Prepare(probe.Json((ulong)n * tickMicroseconds));
                var outcome = RunBothPaths(prepared, tickMicroseconds, spread: true, probe.Name + " n=" + n + " T=" + tickMicroseconds + " Spread(5)");
                var expected = 5u * ((n + 4u) / 5u);
                var starts = new HashSet<uint>();
                for (var agent = 0; agent < Agents; agent++)
                {
                    var timer = Timer(outcome, probe, agent);
                    starts.Add(timer.Start % 5u);
                    var thinks = outcome.Thinks.Where(t => t.Agent == agent).Select(t => t.Tick).ToArray();
                    Assert.That(thinks, Has.Member(timer.Start).And.Member(timer.End), probe.Name + ": the timer starts and ends on the agent's own thinks");
                    Assert.That(thinks.All(t => t % 5u == timer.Start % 5u), Is.True, probe.Name + ": no off-schedule think");
                    Assert.That(timer.End - timer.Start, Is.EqualTo(expected), probe.Name + ": n = " + n + " ends at the first think at or after start + n");
                    Assert.That(timer.End - timer.Start - n, Is.InRange(0u, 4u), probe.Name + ": never earlier, at most 4 ticks late");
                }
                Assert.That(starts.Count, Is.EqualTo(5), "Spread(5) gave the five agents five different phases");
            }
        }

        // ---- wait-random-microseconds with a range -------------------------------------------------------

        /// <summary>
        /// A random wait over [a·T, b·T] µs (recipe a) or [a, b] (recipe b) ends between a and b ticks after it
        /// started, for every agent; draws differ between agents.
        /// </summary>
        [Test]
        public void WaitRandomRange_EndsInsideTheRangeInTicks([Values(Tick30Hz, 1u)] uint tickMicroseconds)
        {
            const uint a = 3u, b = 12u;
            const int agents = 24;
            var probe = new Probe { Name = "wait-random-microseconds [a, b]" };
            using (var kit = new NativeLibraryKit())
            {
                var prepared = kit.Prepare(WaitRandom((ulong)a * tickMicroseconds, (ulong)b * tickMicroseconds));
                var outcome = RunBothPaths(prepared, tickMicroseconds, spread: false, probe.Name + " T=" + tickMicroseconds, agents: agents, seed: 0x12001UL);
                var lengths = Enumerable.Range(0, agents).Select(agent => Timer(outcome, probe, agent)).Select(t => t.End - t.Start).ToArray();
                Assert.That(lengths, Is.All.InRange(a, b), "Every drawn duration ends inside [a, b] ticks");
                Assert.That(lengths.Distinct().Count(), Is.GreaterThan(1), "The agents drew different durations");
            }
        }

        // ---- the largest durations ------------------------------------------------------------------------

        /// <summary>
        /// UInt32 microseconds (the native nodes): at 33333 µs per tick the largest exact duration is 128 850
        /// ticks (4 294 957 050 µs); with TickMicroseconds = 1 it is 4 294 967 295 ticks, the whole tick range.
        /// The ticks are skipped (Step accepts any increasing tick), so the cases run in four steps.
        /// </summary>
        [Test]
        public void LargestUInt32Duration_IsExactInBothRecipes([Values("wait-microseconds", "wait-until-true timeout", "set-deadline + deadline-passed", "wait-random-microseconds (min = max)")] string name)
        {
            var probe = TimeProbes.Single(p => p.Name == name);
            using (var kit = new NativeLibraryKit())
            {
                // Recipe (a): 128 850 × 33333 = 4 294 957 050 fits in UInt32; 128 851 × 33333 does not.
                const uint maxTicks30Hz = uint.MaxValue / Tick30Hz;
                Assert.That(maxTicks30Hz, Is.EqualTo(128850u));
                var outcome = RunBothPaths(kit.Prepare(probe.Json((ulong)maxTicks30Hz * Tick30Hz)), Tick30Hz, false, name + " max at 30 Hz",
                    new[] { 1u, maxTicks30Hz, maxTicks30Hz + 1u, maxTicks30Hz + 2u });
                for (var agent = 0; agent < Agents; agent++)
                    Assert.That(Timer(outcome, probe, agent), Is.EqualTo((1u, maxTicks30Hz + 1u)), name + ": ends exactly 128 850 ticks after tick 1");

                // Recipe (b): a full-range duration started at tick 0 ends on the last tick, uint.MaxValue.
                outcome = RunBothPaths(kit.Prepare(probe.Json(uint.MaxValue)), 1u, false, name + " max at T = 1",
                    new[] { 0u, uint.MaxValue - 1u, uint.MaxValue });
                for (var agent = 0; agent < Agents; agent++)
                    Assert.That(Timer(outcome, probe, agent), Is.EqualTo((0u, uint.MaxValue)), name + ": ends exactly 4 294 967 295 ticks after tick 0");
            }
        }

        /// <summary>
        /// A UInt32 parameter above 4 294 967 295 does not compile, so recipe (a) cannot ask for more than
        /// 128 850 ticks at 30 Hz from a native node.
        /// </summary>
        [Test]
        public void NativeDurationAboveUInt32_DoesNotCompile()
        {
            var result = NativeLibraryKit.CompileResult(WaitMicroseconds((ulong)uint.MaxValue + 1ul));
            Assert.That(result.Success, Is.False);
        }

        /// <summary>
        /// UInt64 microseconds (aibt.core.timeout and cooldown): any duration whose end tick is inside the
        /// tick range is exact in both recipes (here uint.MaxValue ticks at 30 Hz, and uint.MaxValue with
        /// TickMicroseconds = 1). A deadline beyond Int64 microseconds faults the agent at activation
        /// (the runtime accepts at most long.MaxValue − now).
        /// </summary>
        [Test]
        public void LargeUInt64Duration_IsExact_AndADeadlineBeyondInt64FaultsTheAgent([Values("aibt.core.timeout", "aibt.core.cooldown on-enter")] string name)
        {
            var probe = TimeProbes.Single(p => p.Name == name);
            using (var kit = new NativeLibraryKit())
            {
                var n = uint.MaxValue - 1u;
                var outcome = RunBothPaths(kit.Prepare(probe.Json((ulong)n * Tick30Hz)), Tick30Hz, false, name + " at 30 Hz",
                    new[] { 1u, uint.MaxValue - 1u, uint.MaxValue });
                for (var agent = 0; agent < Agents; agent++)
                    Assert.That(Timer(outcome, probe, agent), Is.EqualTo((1u, uint.MaxValue)), name + ": 4 294 967 294 ticks at 30 Hz");

                outcome = RunBothPaths(kit.Prepare(probe.Json(n)), 1u, false, name + " at T = 1", new[] { 1u, uint.MaxValue - 1u, uint.MaxValue });
                for (var agent = 0; agent < Agents; agent++)
                    Assert.That(Timer(outcome, probe, agent), Is.EqualTo((1u, uint.MaxValue)), name + ": 4 294 967 294 ticks at T = 1");

                var beyond = Run(kit.Prepare(probe.Json(long.MaxValue)), 1u, false, false, new[] { 1u, 2u }, agents: 1);
                Assert.That(beyond.Faults, Is.EqualTo(1), name + ": now + d overflows Int64 microseconds: the agent faults");
            }
        }

        // ---- aibt.stdlib.wait counts thinks ---------------------------------------------------------------

        /// <summary>
        /// aibt.stdlib.wait counts its own Tick calls, i.e. thinks, and the think that enters it is the first:
        /// "ticks: 6" ends 5 ticks after it started for an agent thinking every tick, 25 ticks after under
        /// Spread(5) (its 6th think), whatever TickMicroseconds is. aibt.native.wait-microseconds of 6 ticks
        /// ends 6 ticks after it started in both cases' tick terms (Spread rounds it to 10).
        /// </summary>
        [Test]
        public void StdlibWait_CountsThinks_NotTicks([Values(false, true)] bool spread, [Values(Tick30Hz, 1u)] uint tickMicroseconds)
        {
            var probe = new Probe { Name = "aibt.stdlib.wait" };
            using (var kit = new NativeLibraryKit())
            {
                var outcome = RunBothPaths(kit.Prepare(StdlibWait(6)), tickMicroseconds, spread, "stdlib.wait spread=" + spread);
                for (var agent = 0; agent < Agents; agent++)
                {
                    var timer = Timer(outcome, probe, agent);
                    var thinks = outcome.Thinks.Count(t => t.Agent == agent && t.Tick >= timer.Start && t.Tick <= timer.End);
                    Assert.That(thinks, Is.EqualTo(6), "ticks: 6 ends on its 6th think, the entering think included");
                    Assert.That(timer.End - timer.Start, Is.EqualTo(spread ? 25u : 5u), "ticks: 6 is 5 ticks every tick, 25 ticks under Spread(5)");
                }
            }
        }

        /// <summary>
        /// An extra think shortens aibt.stdlib.wait but not a microsecond duration: an agent thinking every
        /// 5 ticks (phase 0) starts both at tick 5; a TryWake before tick 7 adds a think there. "ticks: 3" then
        /// ends at tick 10 (thinks 5, 7, 10) instead of 15; wait-microseconds of 10 ticks still ends at 15.
        /// </summary>
        [Test]
        public void StdlibWait_IsShortenedByAnExtraWake_AMicrosecondWaitIsNot()
        {
            var probe = new Probe { Name = "wake" };
            Action<uint, TreeGroup, AgentHandle[]> wakeAt7 = (tick, group, handles) =>
            {
                if (tick == 7) Assert.That(group.TryWake(handles[0]), Is.EqualTo(SimulationResult.Success));
            };
            using (var kit = new NativeLibraryKit())
            {
                foreach (var (json, plain, woken) in new[]
                         {
                             (StdlibWait(3), 15u, 10u),
                             (WaitMicroseconds(10ul * Tick30Hz), 15u, 15u),
                         })
                {
                    var prepared = kit.Prepare(json);
                    foreach (var wake in new[] { false, true })
                    {
                        var inPlace = RunEvery5(prepared, false, wake ? wakeAt7 : null);
                        var jobs = RunEvery5(prepared, true, wake ? wakeAt7 : null);
                        Assert.That(jobs.Signals, Is.EqualTo(inPlace.Signals));
                        Assert.That(inPlace.Faults + jobs.Faults, Is.Zero);
                        Assert.That(Timer(inPlace, probe, 0), Is.EqualTo((5u, wake ? woken : plain)), json.Contains("stdlib") ? "stdlib.wait" : "wait-microseconds");
                        Assert.That(inPlace.Thinks.Select(t => t.Tick).Where(t => t <= 15u),
                            Is.EqualTo(wake ? new[] { 5u, 7u, 10u, 15u } : new[] { 5u, 10u, 15u }));
                    }
                }
            }
        }

        private static Outcome RunEvery5(NativeLibraryKit.Prepared prepared, bool useJobs, Action<uint, TreeGroup, AgentHandle[]> beforeStep)
        {
            var outcome = new Outcome();
            var settings = TreeSimulationSettings.Default(Tick30Hz);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = 1;
            using (var simulation = new TreeSimulation(settings))
            {
                Assert.That(simulation.TryAddGroup(prepared.Program, TreeGroupSettings.Default(1), out var group), Is.EqualTo(SimulationResult.Success));
                var handles = new AgentHandle[1];
                Assert.That(group.TryAdd(ThinkSchedule.Every(5, 0), out handles[0]), Is.EqualTo(SimulationResult.Success));
                for (uint tick = 1; tick <= 20; tick++)
                {
                    beforeStep?.Invoke(tick, group, handles);
                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    var commands = group.GetCommands(Signal);
                    for (var item = 0; item < commands.Length; item++) outcome.Signals.Add((tick, commands[item].Agent.Index, commands[item].Value));
                    outcome.Faults += group.GetFaults().Length;
                    if (ThoughtOn(group, handles[0], tick)) outcome.Thinks.Add((tick, 0));
                }
                outcome.JobBatches = group.JobBatchesForTests;
            }
            if (useJobs) Assert.That(outcome.JobBatches, Is.GreaterThan(0));
            return outcome;
        }
    }
}
