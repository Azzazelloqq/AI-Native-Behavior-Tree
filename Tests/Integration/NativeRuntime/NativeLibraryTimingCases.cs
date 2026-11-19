using System;
using System.Collections.Generic;
using System.Linq;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-002 stage 2: one update of a timing case on a ProductionTreeHost driven by a controlled
    /// clock. Facts are written before the update; the root result (null = still running), the key
    /// values and the signals drained after the update are then compared.
    /// </summary>
    public sealed class TimingStep
    {
        internal long Clock;
        internal readonly List<(string Key, object Value)> Facts = new List<(string, object)>();
        internal NodeStatus? Root;
        internal readonly List<(string Key, object Value)> Values = new List<(string, object)>();
        internal int[] Signals = Array.Empty<int>();

        internal TimingStep Fact(string key, object value)
        {
            Facts.Add((key, value));
            return this;
        }

        internal TimingStep Expect(string key, object value)
        {
            Values.Add((key, value));
            return this;
        }

        internal TimingStep Emits(params int[] signals)
        {
            Signals = signals;
            return this;
        }
    }

    /// <summary>P11-002 stage 2: a behaviour case of a timing or signal node (ADR-P11-002 §5.1 group 4).</summary>
    public sealed class TimingCase
    {
        internal string Node;
        internal string Name;
        internal string Json;
        internal readonly List<TimingStep> Steps = new List<TimingStep>();

        public override string ToString() => Name;

        internal TimingCase At(long clock, NodeStatus? root, Func<TimingStep, TimingStep> configure = null)
        {
            var step = new TimingStep { Clock = clock, Root = root };
            Steps.Add(configure != null ? configure(step) : step);
            return this;
        }
    }

    internal static class TimingCases
    {
        internal const string Signal = "aibt.native.signal";
        private static int s_counter;

        internal static IReadOnlyList<TimingCase> All { get; } = Build();

        private static TimingCase Case(string node, string name, string rootId, IEnumerable<string> keys, params string[] nodes)
        {
            var id = "tests.p11002.stage2." + node.Substring("aibt.native.".Length) + "." + (++s_counter).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new TimingCase { Node = node, Name = node.Substring("aibt.native.".Length) + ": " + name, Json = Tree(id, rootId, keys, nodes) };
        }

        private static List<TimingCase> Build()
        {
            const string S = "aibt.native.";
            const string Wait = S + "wait-until-true";
            const string SetDeadline = S + "set-deadline";
            const string Passed = S + "deadline-passed";
            var cases = new List<TimingCase>
            {
                // ---- wait-until-true --------------------------------------------------------------------
                Case(Wait, "true at start succeeds at once", "w", new[] { Bool("flag", true) },
                        Leaf("w", Wait, "\"timeout-microseconds\": 0", "\"key\": \"flag\""))
                    .At(0, NodeStatus.Success),
                Case(Wait, "no timeout keeps waiting until a fact write", "w", new[] { Bool("flag") },
                        Leaf("w", Wait, "\"timeout-microseconds\": 0", "\"key\": \"flag\""))
                    .At(0, null).At(1000000000L, null).At(4000000000000L, null)
                    .At(4000000000001L, NodeStatus.Success, s => s.Fact("flag", true)),
                Case(Wait, "timeout: running before it, Failure exactly at it", "w", new[] { Bool("flag") },
                        Leaf("w", Wait, "\"timeout-microseconds\": 1000", "\"key\": \"flag\""))
                    .At(5000, null).At(5999, null).At(6000, NodeStatus.Failure),
                Case(Wait, "the key wins over a timeout reached in the same update", "w", new[] { Bool("flag") },
                        Leaf("w", Wait, "\"timeout-microseconds\": 1000", "\"key\": \"flag\""))
                    .At(0, null).At(1000, NodeStatus.Success, s => s.Fact("flag", true)),
                Case(Wait, "the maximum timeout 4294967295", "w", new[] { Bool("flag") },
                        Leaf("w", Wait, "\"timeout-microseconds\": 4294967295", "\"key\": \"flag\""))
                    .At(0, null).At(4294967294L, null).At(4294967295L, NodeStatus.Failure),
                // A node write: the writer runs after the waiter in the parallel, so the waiter sees the
                // flag at the next update.
                Case(Wait, "the key set by a node write", "both", new[] { Bool("flag"), Int("done") },
                        CompositeWith("both", "aibt.core.parallel", "\"policy\": \"require-all-success\"", "waiter", "writer"),
                        Composite("waiter", "aibt.core.memory-sequence", "wait", "mark"),
                        Leaf("wait", Wait, "\"timeout-microseconds\": 0", "\"key\": \"flag\""),
                        Leaf("mark", S + "set-int", "\"value\": 1", "\"key\": \"done\""),
                        Composite("writer", "aibt.core.memory-sequence", "pause", "raise"),
                        Leaf("pause", S + "wait-microseconds", "\"duration-microseconds\": 500"),
                        Leaf("raise", S + "set-bool", "\"value\": true", "\"key\": \"flag\""))
                    .At(0, null, s => s.Expect("flag", false).Expect("done", 0))
                    .At(499, null, s => s.Expect("flag", false))
                    .At(500, null, s => s.Expect("flag", true).Expect("done", 0))
                    .At(501, NodeStatus.Success, s => s.Expect("done", 1)),
                // Aborted while waiting by a reactive parent, then started again: the timeout counts
                // from the new start (800 + 1000), not from the first one (0 + 1000). The parent is a
                // retaining reactive selector, so the waiting child is kept between updates.
                Case(Wait, "aborted by a reactive parent, the timeout counts from the new start", "root", new[] { Bool("flag"), Bool("interrupt") },
                        Composite("root", "aibt.core.reactive-selector-retaining", "guard", "w"),
                        Composite("guard", "aibt.core.reactive-sequence", "interrupted", "hold"),
                        Leaf("interrupted", S + "bool-is-true", null, "\"key\": \"interrupt\""),
                        Leaf("hold", S + "idle"),
                        Leaf("w", Wait, "\"timeout-microseconds\": 1000", "\"key\": \"flag\""))
                    .At(0, null).At(600, null)
                    .At(700, null, s => s.Fact("interrupt", true))
                    .At(800, null, s => s.Fact("interrupt", false))
                    .At(1000, null).At(1799, null).At(1800, NodeStatus.Failure),
                // A v1 reactive selector aborts and re-enters its running child at every update
                // (reference-executor-machine-v1.md, "Reactive replacement"), so each update enters the
                // wait again and its timeout counts from that same update: it never times out. The key
                // still ends it.
                Case(Wait, "under a v1 reactive selector it restarts every update and never times out", "root", new[] { Bool("flag"), Bool("interrupt") },
                        Composite("root", "aibt.core.reactive-selector", "guard", "w"),
                        Leaf("guard", S + "bool-is-true", null, "\"key\": \"interrupt\""),
                        Leaf("w", Wait, "\"timeout-microseconds\": 1000", "\"key\": \"flag\""))
                    .At(0, null).At(999, null).At(1000, null).At(2000, null).At(1000000, null)
                    .At(1000001, NodeStatus.Success, s => s.Fact("flag", true)),

                // ---- set-deadline ---------------------------------------------------------------------------
                Case(SetDeadline, "writes now + duration", "a", new[] { Long("d", 7) },
                        Leaf("a", SetDeadline, "\"duration-microseconds\": 500", "\"key\": \"d\""))
                    .At(1000, NodeStatus.Success, s => s.Expect("d", 1500L)),
                Case(SetDeadline, "duration 0 writes now", "a", new[] { Long("d", 7) },
                        Leaf("a", SetDeadline, "\"duration-microseconds\": 0", "\"key\": \"d\""))
                    .At(123456, NodeStatus.Success, s => s.Expect("d", 123456L)),
                Case(SetDeadline, "beyond the Int32 range", "a", new[] { Long("d", 7) },
                        Leaf("a", SetDeadline, "\"duration-microseconds\": 4294967295", "\"key\": \"d\""))
                    .At(10000000000L, NodeStatus.Success, s => s.Expect("d", 14294967295L)),
                Case(SetDeadline, "a deadline of exactly long.MaxValue", "a", new[] { Long("d", 7) },
                        Leaf("a", SetDeadline, "\"duration-microseconds\": 10", "\"key\": \"d\""))
                    .At(long.MaxValue - 10, NodeStatus.Success, s => s.Expect("d", long.MaxValue)),
                Case(SetDeadline, "past long.MaxValue fails without writing", "a", new[] { Long("d", 7) },
                        Leaf("a", SetDeadline, "\"duration-microseconds\": 11", "\"key\": \"d\""))
                    .At(long.MaxValue - 10, NodeStatus.Failure, s => s.Expect("d", 7L)),

                // ---- deadline-passed -------------------------------------------------------------------------
                Case(Passed, "before the deadline fails", "c", new[] { Long("d", 1500) },
                        Leaf("c", Passed, null, "\"key\": \"d\""))
                    .At(1499, NodeStatus.Failure),
                Case(Passed, "exactly at the deadline succeeds", "c", new[] { Long("d", 1500) },
                        Leaf("c", Passed, null, "\"key\": \"d\""))
                    .At(1500, NodeStatus.Success),
                Case(Passed, "after the deadline succeeds", "c", new[] { Long("d", 1500) },
                        Leaf("c", Passed, null, "\"key\": \"d\""))
                    .At(1501, NodeStatus.Success),
                Case(Passed, "a key never set (0) has passed", "c", new[] { Long("d") },
                        Leaf("c", Passed, null, "\"key\": \"d\""))
                    .At(0, NodeStatus.Success),
                Case(Passed, "an Int64 deadline beyond the Int32 range, before", "c", new[] { Long("d", 5000000000L) },
                        Leaf("c", Passed, null, "\"key\": \"d\""))
                    .At(4999999999L, NodeStatus.Failure),
                Case(Passed, "an Int64 deadline beyond the Int32 range, at", "c", new[] { Long("d", 5000000000L) },
                        Leaf("c", Passed, null, "\"key\": \"d\""))
                    .At(5000000000L, NodeStatus.Success),
                Case(Passed, "a deadline written as a fact", "c", new[] { Long("d", long.MaxValue) },
                        Leaf("c", Passed, null, "\"key\": \"d\""))
                    .At(1000, NodeStatus.Success, s => s.Fact("d", 1000L)),
                Case(Passed, "under an inverter: still cooling down", "not", new[] { Long("d", 1500) },
                        Composite("not", "aibt.core.inverter", "c"),
                        Leaf("c", Passed, null, "\"key\": \"d\""))
                    .At(1000, NodeStatus.Success),

                // ---- a cooldown shared by two branches through one key ---------------------------------------
                SharedCooldown(S),

                // ---- signal -------------------------------------------------------------------------------------
                Case(Signal, "code 7 is emitted once", "a", Array.Empty<string>(), Leaf("a", Signal, "\"code\": 7"))
                    .At(0, NodeStatus.Success, s => s.Emits(7)),
                Case(Signal, "code 0", "a", Array.Empty<string>(), Leaf("a", Signal, "\"code\": 0"))
                    .At(0, NodeStatus.Success, s => s.Emits(0)),
                Case(Signal, "code 2147483647", "a", Array.Empty<string>(), Leaf("a", Signal, "\"code\": 2147483647"))
                    .At(0, NodeStatus.Success, s => s.Emits(int.MaxValue)),
                Case(Signal, "code 2147483648 fails and emits nothing", "a", Array.Empty<string>(), Leaf("a", Signal, "\"code\": 2147483648"))
                    .At(0, NodeStatus.Failure, s => s.Emits()),
                Case(Signal, "code 4294967295 fails and emits nothing", "a", Array.Empty<string>(), Leaf("a", Signal, "\"code\": 4294967295"))
                    .At(0, NodeStatus.Failure, s => s.Emits()),
                Case(Signal, "two signals in one update arrive once each, in order", "seq", Array.Empty<string>(),
                        Composite("seq", "aibt.core.memory-sequence", "one", "two"),
                        Leaf("one", Signal, "\"code\": 1"),
                        Leaf("two", Signal, "\"code\": 2"))
                    .At(0, NodeStatus.Success, s => s.Emits(1, 2)),
                Case(Signal, "a signal after a fact; nothing before it", "seq", new[] { Bool("go") },
                        Composite("seq", "aibt.core.memory-sequence", "wait", "say"),
                        Leaf("wait", S + "wait-until-true", "\"timeout-microseconds\": 0", "\"key\": \"go\""),
                        Leaf("say", Signal, "\"code\": 3"))
                    .At(0, null, s => s.Emits()).At(10, null, s => s.Emits())
                    .At(20, NodeStatus.Success, s => s.Fact("go", true).Emits(3)),
            };
            return cases;
        }

        // Branch A fires when the fact want-a is true and the shared deadline in "cooldown" has passed;
        // branch B likewise with want-b. Each firing starts the same 1000 µs cooldown, so neither branch
        // fires while the other's cooldown runs. A firing branch holds (idle) until its guard fails.
        private static TimingCase SharedCooldown(string s)
        {
            string Branch(string name) => Composite(name, "aibt.core.reactive-sequence", name + "-want", name + "-ready", name + "-fire");
            return Case(s + "set-deadline", "a cooldown shared by two branches through one key", "root",
                    new[] { Bool("want-a"), Bool("want-b"), Long("cooldown"), Int("a"), Int("b") },
                    Composite("root", "aibt.core.reactive-selector", "a", "b", "rest"),
                    Branch("a"),
                    Leaf("a-want", s + "bool-is-true", null, "\"key\": \"want-a\""),
                    Leaf("a-ready", s + "deadline-passed", null, "\"key\": \"cooldown\""),
                    Composite("a-fire", "aibt.core.memory-sequence", "a-start", "a-count", "a-hold"),
                    Leaf("a-start", s + "set-deadline", "\"duration-microseconds\": 1000", "\"key\": \"cooldown\""),
                    Leaf("a-count", s + "increment-int", "\"amount\": 1", "\"key\": \"a\""),
                    Leaf("a-hold", s + "idle"),
                    Branch("b"),
                    Leaf("b-want", s + "bool-is-true", null, "\"key\": \"want-b\""),
                    Leaf("b-ready", s + "deadline-passed", null, "\"key\": \"cooldown\""),
                    Composite("b-fire", "aibt.core.memory-sequence", "b-start", "b-count", "b-hold"),
                    Leaf("b-start", s + "set-deadline", "\"duration-microseconds\": 1000", "\"key\": \"cooldown\""),
                    Leaf("b-count", s + "increment-int", "\"amount\": 1", "\"key\": \"b\""),
                    Leaf("b-hold", s + "idle"),
                    Leaf("rest", s + "idle"))
                .At(0, null, st => st.Fact("want-a", true).Expect("a", 1).Expect("b", 0).Expect("cooldown", 1000L))
                .At(500, null, st => st.Fact("want-a", false).Fact("want-b", true).Expect("a", 1).Expect("b", 0).Expect("cooldown", 1000L))
                .At(999, null, st => st.Expect("b", 0))
                .At(1000, null, st => st.Expect("a", 1).Expect("b", 1).Expect("cooldown", 2000L))
                .At(1500, null, st => st.Fact("want-a", true).Fact("want-b", false).Expect("a", 1).Expect("b", 1).Expect("cooldown", 2000L))
                .At(2001, null, st => st.Expect("a", 2).Expect("b", 1).Expect("cooldown", 3001L));
        }

        internal static IEnumerable<string> CoveredNodes() => All.Select(c => c.Node).Distinct();
    }
}
