using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-002 stage 3 Part C: the five random nodes of aibt.native.standard (category Native/Random)
    /// on the real ProductionTreeHost, compiled under the default Phase1 policy against a project
    /// catalog set that lists the package shard next to a project shard.
    ///
    /// Every drawn value is checked exactly against a reference replay of the node's stream: the
    /// stream is derived independently (<c>TryDerive(seed, semantic hash, host instance id, node)</c>)
    /// and stepped with the spec's PCG step, so the test also pins how many draws each tick consumes
    /// (after every update the host's committed state must equal the replay's). On top of that, bounds
    /// and distributions are checked over many draws. The statistical margins are 6 standard
    /// deviations of the checked count or mean: a correct node fails one of them with probability
    /// below 1e-8 per run, whatever the host's instance id. A loop is paced by aibt.stdlib.wait (2
    /// ticks), so the random node runs at most once per update.
    /// </summary>
    public sealed class NativeLibraryRandomTests
    {
        private const string S = "aibt.native.";
        private const ulong Seed = 0x5EED_0003UL;
        private NativeLibraryKit _kit;

        [SetUp]
        public void SetUp() => _kit = new NativeLibraryKit();

        [TearDown]
        public void TearDown() => _kit.Dispose();

        // ------------------------------------------------------------------ trees

        /// <summary>repeater [ memory-sequence [ pace (stdlib.wait 2 ticks), body... ] ] for ever.</summary>
        private static string Loop(string treeId, IEnumerable<string> keys, string[] body, params string[] nodes)
            => Tree(treeId, "loop", keys,
                new[]
                {
                    CompositeWith("loop", "aibt.core.repeater", "\"count\": 4000000000, \"stopOnFailure\": false", "cycle"),
                    Composite("cycle", "aibt.core.memory-sequence", new[] { "pace" }.Concat(body).ToArray()),
                    Leaf("pace", "aibt.stdlib.wait", "\"ticks\": 2"),
                }.Concat(nodes).ToArray());

        /// <summary>The chance outcome is recorded in 'hit': memory-selector [ memory-sequence [ roll, hit = true ], hit = false ].</summary>
        private static string ChanceLoop(float chance)
            => Loop("tests.p11002.random-chance-loop", new[] { Bool("hit") }, new[] { "coin" },
                Composite("coin", "aibt.core.memory-selector", "win", "lose"),
                Composite("win", "aibt.core.memory-sequence", "roll", "mark"),
                Leaf("roll", S + "random-chance", "\"chance\": " + Number(chance)),
                Leaf("mark", S + "set-bool", "\"value\": true", "\"key\": \"hit\""),
                Leaf("lose", S + "set-bool", "\"value\": false", "\"key\": \"hit\""));

        private static string FloatLeaf(float min, float max)
            => Leaf("roll", S + "random-float", "\"min\": " + Number(min) + ", \"max\": " + Number(max), "\"key\": \"f\"");

        private static string IntLeaf(uint min, uint max)
            => Leaf("roll", S + "random-int", "\"min\": " + min + ", \"max\": " + max, "\"key\": \"i\"");

        private static string RadiusLeaf(float min, float radius, bool flat)
            => Leaf("roll", S + "random-float3-in-radius",
                "\"min-radius\": " + Number(min) + ", \"radius\": " + Number(radius) + ", \"flat\": " + (flat ? "true" : "false"),
                "\"center\": \"c\", \"result\": \"r\"");

        private static string WaitLeaf(uint min, uint max)
            => Leaf("w", S + "wait-random-microseconds", "\"min-microseconds\": " + min + ", \"max-microseconds\": " + max);

        private static string FloatLoop(float min, float max)
            => Loop("tests.p11002.random-float-loop", new[] { Float("f", -99f) }, new[] { "roll" }, FloatLeaf(min, max));

        private static string IntLoop(uint min, uint max)
            => Loop("tests.p11002.random-int-loop", new[] { Int("i", -1) }, new[] { "roll" }, IntLeaf(min, max));

        private static readonly Float3Value Center = new Float3Value(10f, 1.5f, -4f);

        private static string RadiusLoop(float min, float radius, bool flat)
            => Loop("tests.p11002.random-radius-loop", new[] { Float3("c", Center.X, Center.Y, Center.Z), Float3("r") }, new[] { "roll" },
                RadiusLeaf(min, radius, flat));

        /// <summary>Every tree these tests run, for the coverage check.</summary>
        private static IEnumerable<string> AllTrees()
        {
            yield return ChanceLoop(0.3f);
            yield return FloatLoop(-2f, 3f);
            yield return IntLoop(3, 7);
            yield return RadiusLoop(0f, 1f, true);
            yield return Tree("tests.p11002.wait-random", "w", new string[0], WaitLeaf(1000, 5000));
        }

        // ------------------------------------------------------------------ reference replay

        /// <summary>The node's stream replayed outside the host: the spec's derivation and PCG step.</summary>
        private sealed class Replay
        {
            internal ulong State;
            internal readonly ulong Increment;

            internal Replay(ulong state, ulong increment)
            {
                State = state;
                Increment = increment;
            }

            internal uint Next() => NativeRandomStreamDerivationV1.NextUInt32(ref State, Increment);

            internal float NextFloat() => (Next() >> 8) * (1.0f / 16777216.0f);

            /// <summary>time-and-random-v1 bounded draw: rejection below (2^32 mod bound).</summary>
            internal uint Bounded(uint bound)
            {
                var threshold = unchecked(0u - bound) % bound;
                uint sample;
                do sample = Next();
                while (sample < threshold);
                return sample % bound;
            }
        }

        private static uint RandomNode(LibraryHost host)
        {
            var found = new List<uint>();
            for (uint node = 0; node < host.Host.Program.Nodes.Count; node++)
                if (host.Host.GeneratedDispatchAdapter.TryReadRandomStream(node, out _, out _)) found.Add(node);
            Assert.That(found, Has.Count.EqualTo(1), "one random node in the test tree");
            return found[0];
        }

        private static Replay Derived(LibraryHost host, uint node, ulong seed)
        {
            Assert.That(NativeRandomStreamDerivationV1.TryDerive(seed, new NativeHash256V1(host.Host.Program.Header.CanonicalSemanticHash),
                host.Host.InstanceId, node, out var stream), Is.True);
            Assert.That(State(host, node), Is.EqualTo(stream.State), "the host seeded the node's stream");
            return new Replay(stream.State, stream.Increment);
        }

        private static ulong State(LibraryHost host, uint node)
        {
            Assert.That(host.Host.GeneratedDispatchAdapter.TryReadRandomStream(node, out var state, out _), Is.True);
            return state;
        }

        /// <summary>White-box: puts <paramref name="state"/> into the node's committed stream state (to reach rare draws).</summary>
        private static void OverwriteState(LibraryHost host, uint node, ulong state)
        {
            var adapter = host.Host.GeneratedDispatchAdapter;
            var slots = (Array)adapter.GetType().GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
            var slot = slots.GetValue((int)node);
            // A NativeArray<ulong> view: setting element 0 on the boxed copy writes the shared native buffer.
            var states = slot.GetType().GetField("_randomStates", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(slot);
            states.GetType().GetMethod("set_Item").Invoke(states, new object[] { 0, state });
            Assert.That(State(host, node), Is.EqualTo(state));
        }

        /// <summary>
        /// Updates the host until the node has ticked <paramref name="ticks"/> times. After each update in
        /// which the stream moved, <paramref name="tick"/> replays one tick on the reference (and checks
        /// the node's output); the host's committed state must then equal the replay's, so each update
        /// holds exactly one tick and each tick consumes exactly the replayed draws.
        /// </summary>
        private static void Drive(LibraryHost host, uint node, Replay replay, int ticks, Action<int> tick)
        {
            var updates = 0;
            for (var done = 0; done < ticks;)
            {
                Assert.That(host.Update(), Is.Null, "the loop never ends");
                Assert.That(++updates, Is.LessThan(ticks * 3 + 10), "the node keeps ticking");
                var state = State(host, node);
                if (state == replay.State) continue;
                tick(done);
                Assert.That(state, Is.EqualTo(replay.State), "tick " + done + ": the host consumed exactly the replayed draws");
                done++;
            }
        }

        private static void AssertWithinSixSigma(double observed, double expected, double sigma, string what)
            => Assert.That(Math.Abs(observed - expected), Is.LessThanOrEqualTo(6.0 * sigma),
                what + ": observed " + observed + ", expected " + expected + " +- 6 x " + sigma);

        // ------------------------------------------------------------------ policy, generator, coverage

        [Test]
        public void TheFiveRandomNodesPassTheDefaultPhase1Policy_WhileRandomConditionIsRefused()
        {
            foreach (var json in AllTrees())
            {
                var result = NativeLibraryKit.CompileResult(json);
                Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
                Assert.That(result.Diagnostics.Select(d => d.Code.Value), Has.No.Member("AIBT2040"));
            }
            var condition = NativeLibraryKit.CompileResult(Tree("tests.p11002.random-condition-phase1", "roll", new string[0],
                Leaf("roll", "aibt.stdlib.random-condition", "\"success-chance-percent\": 50")));
            Assert.That(condition.Success, Is.False, "aibt.stdlib.random-condition is declared non-deterministic");
            Assert.That(condition.Diagnostics.Select(d => d.Code.Value), Does.Contain("AIBT2040"));
        }

        [Test]
        public void TheGeneratorAcceptsTheRandomStreamNodesInThePackageShardNextToAProjectShard()
        {
            Assert.That(NativeLibraryCatalog.IsUsable, Is.True);
            var nodes = NativeLibraryKit.Materialize(typeof(AIBT.StandardNodes.StandardNativeShard)).Nodes
                .Where(node => StandardNativeLibraryIds.Stage3.Contains(node.Manifest.TypeId)).ToArray();
            Assert.That(nodes.Select(node => node.Manifest.TypeId), Is.EquivalentTo(StandardNativeLibraryIds.Stage3));
            foreach (var node in nodes)
            {
                Assert.That(node.Manifest.Category, Is.EqualTo("Native/Random"), node.Manifest.TypeId);
                Assert.That(node.Manifest.Deterministic, Is.True, node.Manifest.TypeId);
                Assert.That(node.Manifest.Version, Is.EqualTo(1u), node.Manifest.TypeId);
            }
        }

        [Test]
        public void EveryStage3NodeHasCases()
        {
            var trees = string.Join("\n", AllTrees());
            foreach (var id in StandardNativeLibraryIds.Stage3)
                Assert.That(trees, Does.Contain("\"type\": \"" + id + "\""), id);
        }

        // ------------------------------------------------------------------ random-chance

        /// <summary>2000 rolls of chance 0.3: each outcome is sample &lt; 0.3 for the replayed sample; hits within 6 sigma of 600.</summary>
        [Test]
        public void RandomChance_SucceedsExactlyWhenTheDrawIsBelowTheChance()
        {
            const int Rolls = 2000;
            const float Chance = 0.3f;
            var host = _kit.Start(_kit.Prepare(ChanceLoop(Chance)), randomSeed: Seed);
            var node = RandomNode(host);
            var replay = Derived(host, node, Seed);
            var hits = 0;
            Drive(host, node, replay, Rolls, done =>
            {
                var expected = replay.NextFloat() < Chance;
                Assert.That(host.ReadBool("hit"), Is.EqualTo(expected), "roll " + done);
                if (expected) hits++;
            });
            AssertWithinSixSigma(hits, Rolls * Chance, Math.Sqrt(Rolls * Chance * (1 - Chance)), "hits");
        }

        [TestCase(0f, false)]
        [TestCase(1f, true)]
        public void RandomChance_ZeroNeverSucceeds_OneAlwaysDoes_EachStillOneDraw(float chance, bool outcome)
        {
            var host = _kit.Start(_kit.Prepare(ChanceLoop(chance)), randomSeed: Seed);
            var node = RandomNode(host);
            var replay = Derived(host, node, Seed);
            Drive(host, node, replay, 200, done =>
            {
                replay.NextFloat();
                Assert.That(host.ReadBool("hit"), Is.EqualTo(outcome), "roll " + done);
            });
        }

        [TestCase(-0.25f)]
        [TestCase(1.5f)]
        public void RandomChance_OutsideZeroToOne_FailsWithoutDrawing(float chance)
        {
            var host = _kit.Start(_kit.Prepare(Tree("tests.p11002.random-chance-invalid", "roll", new string[0],
                Leaf("roll", S + "random-chance", "\"chance\": " + Number(chance)))), randomSeed: Seed);
            var node = RandomNode(host);
            var before = Derived(host, node, Seed).State;
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Failure));
            Assert.That(State(host, node), Is.EqualTo(before), "no draw");
        }

        // ------------------------------------------------------------------ random-float

        /// <summary>2000 draws in [-2, 3): every value equals the replay, lies in [min, max); mean within 6 sigma of 0.5.</summary>
        [Test]
        public void RandomFloat_WritesTheReplayedValueInTheHalfOpenRange()
        {
            const int Draws = 2000;
            const float Min = -2f, Max = 3f;
            var host = _kit.Start(_kit.Prepare(FloatLoop(Min, Max)), randomSeed: Seed);
            var node = RandomNode(host);
            var replay = Derived(host, node, Seed);
            var sum = 0.0;
            float low = float.MaxValue, high = float.MinValue;
            Drive(host, node, replay, Draws, done =>
            {
                var expected = ExpectedFloat(Min, Max, replay.NextFloat());
                var value = host.ReadFloat("f");
                Assert.That(value, Is.EqualTo(expected), "draw " + done);
                Assert.That(value, Is.GreaterThanOrEqualTo(Min).And.LessThan(Max), "draw " + done);
                sum += value;
                low = Math.Min(low, value);
                high = Math.Max(high, value);
            });
            AssertWithinSixSigma(sum / Draws, 0.5, (Max - Min) / Math.Sqrt(12.0 * Draws), "mean");
            Assert.That(low, Is.LessThan(Min + 0.05f), "the low end is reached");
            Assert.That(high, Is.GreaterThan(Max - 0.05f), "the high end is approached");
        }

        /// <summary>
        /// The upper end is excluded even when rounding reaches it: [1, 2) with the largest sample
        /// (1 - 2^-24) gives 1 + (1 - 2^-24), which rounds to 2.0f; the node writes the float below 2.
        /// The state is chosen so the next output has its top 24 bits set (rotation 0, bits 41-58 set).
        /// </summary>
        [Test]
        public void RandomFloat_NeverWritesMax_EvenWhenRoundingReachesIt()
        {
            var host = _kit.Start(_kit.Prepare(FloatLoop(1f, 2f)), randomSeed: Seed);
            var node = RandomNode(host);
            var replay = Derived(host, node, Seed);
            const ulong Top = ((1UL << 59) - 1) & ~((1UL << 41) - 1);
            var probe = new Replay(Top, replay.Increment);
            Assert.That(probe.Next() >> 8, Is.EqualTo(0xFFFFFFu), "the crafted state gives the largest sample");
            Assert.That((float)(1.0 + (1.0 - 1.0 / 16777216.0)), Is.EqualTo(2f), "rounding alone would give max");
            OverwriteState(host, node, Top);
            replay.State = Top;
            Drive(host, node, replay, 1, done =>
            {
                replay.Next();
                Assert.That(host.ReadFloat("f"), Is.EqualTo(BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(2f) - 1)));
                Assert.That(host.ReadFloat("f"), Is.LessThan(2f));
            });
        }

        [Test]
        public void RandomFloat_MinEqualsMax_WritesMin_AndStillDrawsOnce()
        {
            var host = _kit.Start(_kit.Prepare(FloatLoop(0.75f, 0.75f)), randomSeed: Seed);
            var node = RandomNode(host);
            var replay = Derived(host, node, Seed);
            Drive(host, node, replay, 50, done =>
            {
                replay.Next();
                Assert.That(host.ReadFloat("f"), Is.EqualTo(0.75f), "draw " + done);
            });
        }

        [Test]
        public void RandomFloat_MinAboveMax_FailsWithoutWritingOrDrawing()
        {
            var host = _kit.Start(_kit.Prepare(Tree("tests.p11002.random-float-invalid", "roll", new[] { Float("f", -99f) }, FloatLeaf(3f, 2f))),
                randomSeed: Seed);
            var node = RandomNode(host);
            var before = Derived(host, node, Seed).State;
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Failure));
            Assert.That(host.ReadFloat("f"), Is.EqualTo(-99f), "nothing written");
            Assert.That(State(host, node), Is.EqualTo(before), "no draw");
        }

        private static float ExpectedFloat(float min, float max, float sample)
        {
            if (min == max) return min;
            var value = (float)(min + (double)sample * ((double)max - min));
            return value < max ? value : BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(max) - (max > 0f ? 1 : -1));
        }

        // ------------------------------------------------------------------ random-int

        /// <summary>2500 draws in [3, 7]: each equals the replayed bounded draw; every value 3..7 within 6 sigma of 500.</summary>
        [Test]
        public void RandomInt_WritesTheReplayedValue_BothEndsIncluded()
        {
            const int Draws = 2500;
            var host = _kit.Start(_kit.Prepare(IntLoop(3, 7)), randomSeed: Seed);
            var node = RandomNode(host);
            var replay = Derived(host, node, Seed);
            var counts = new int[8];
            Drive(host, node, replay, Draws, done =>
            {
                var expected = 3 + (int)replay.Bounded(5);
                var value = host.ReadInt("i");
                Assert.That(value, Is.EqualTo(expected), "draw " + done);
                Assert.That(value, Is.InRange(3, 7));
                counts[value]++;
            });
            for (var value = 3; value <= 7; value++)
                AssertWithinSixSigma(counts[value], Draws / 5.0, Math.Sqrt(Draws * 0.2 * 0.8), "count of " + value);
        }

        /// <summary>
        /// A rejected sample is drawn again, as the spec's bounded draw does: [0, 2] has threshold
        /// (2^32 mod 3) = 1, so a sample of 0 (state 0, rotation 0) is rejected and the tick takes two steps.
        /// </summary>
        [Test]
        public void RandomInt_ARejectedSampleTakesASecondStep()
        {
            var host = _kit.Start(_kit.Prepare(IntLoop(0, 2)), randomSeed: Seed);
            var node = RandomNode(host);
            var replay = Derived(host, node, Seed);
            Assert.That(new Replay(0UL, replay.Increment).Next(), Is.EqualTo(0u));
            OverwriteState(host, node, 0UL);
            replay.State = 0UL;
            Drive(host, node, replay, 1, done =>
            {
                var expected = (int)replay.Bounded(3);
                Assert.That(host.ReadInt("i"), Is.EqualTo(expected));
            });
            var twoSteps = new Replay(0UL, replay.Increment);
            twoSteps.Next();
            twoSteps.Next();
            Assert.That(replay.State, Is.EqualTo(twoSteps.State), "two steps of the stream");
        }

        [TestCase(5u, 5u)]
        [TestCase(0u, 2147483647u)]
        public void RandomInt_MinEqualsMax_AndTheFullRange_DrawOncePerTick(uint min, uint max)
        {
            var host = _kit.Start(_kit.Prepare(IntLoop(min, max)), randomSeed: Seed);
            var node = RandomNode(host);
            var replay = Derived(host, node, Seed);
            Drive(host, node, replay, 100, done =>
            {
                var before = replay.State;
                var expected = (int)(min + replay.Bounded(max - min + 1u));
                var oneStep = new Replay(before, replay.Increment);
                oneStep.Next();
                Assert.That(replay.State, Is.EqualTo(oneStep.State), "no rejection is possible for these bounds");
                Assert.That(host.ReadInt("i"), Is.EqualTo(expected), "draw " + done);
            });
        }

        [TestCase(7u, 3u)]
        [TestCase(0u, 2147483648u)]
        [TestCase(4000000000u, 4294967295u)]
        public void RandomInt_MinAboveMax_OrMaxAboveInt32_FailsWithoutWritingOrDrawing(uint min, uint max)
        {
            var host = _kit.Start(_kit.Prepare(Tree("tests.p11002.random-int-invalid", "roll", new[] { Int("i", -1) }, IntLeaf(min, max))),
                randomSeed: Seed);
            var node = RandomNode(host);
            var before = Derived(host, node, Seed).State;
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Failure));
            Assert.That(host.ReadInt("i"), Is.EqualTo(-1), "nothing written");
            Assert.That(State(host, node), Is.EqualTo(before), "no draw");
        }

        // ------------------------------------------------------------------ wait-random-microseconds

        /// <summary>
        /// Against a controlled clock, for 40 hosts of different seeds: the duration is the replayed
        /// bounded draw in [1000, 5000] taken at Enter (the only draw); the wait is running one
        /// microsecond before it and succeeds exactly at it.
        /// </summary>
        [Test]
        public void WaitRandom_RunsForTheReplayedDuration_DrawnOnceAtEnter()
        {
            var prepared = _kit.Prepare(Tree("tests.p11002.wait-random", "w", new string[0], WaitLeaf(1000, 5000)));
            var durations = new List<uint>();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                long now = 10_000;
                var host = _kit.Start(prepared, () => now, randomSeed: seed);
                var node = RandomNode(host);
                var replay = Derived(host, node, seed);
                var duration = 1000u + replay.Bounded(4001u);
                durations.Add(duration);
                Assert.That(host.Update(), Is.Null, "seed " + seed + ": running after Enter");
                Assert.That(State(host, node), Is.EqualTo(replay.State), "seed " + seed + ": one bounded draw at Enter");
                now = 10_000 + duration - 1;
                Assert.That(host.Update(), Is.Null, "seed " + seed + ": still running 1 us before " + duration);
                now = 10_000 + duration;
                Assert.That(host.Update(), Is.EqualTo(NodeStatus.Success), "seed " + seed + ": done at " + duration);
                Assert.That(State(host, node), Is.EqualTo(replay.State), "seed " + seed + ": Tick draws nothing");
            }
            Assert.That(durations, Is.All.InRange(1000u, 5000u));
            Assert.That(durations.Distinct().Count(), Is.GreaterThan(30), "different seeds give different durations");
        }

        [TestCase(2500u, 2500u)]
        [TestCase(0u, 4294967295u)]
        public void WaitRandom_MinEqualsMax_AndTheFullRange_TakeOneDraw(uint min, uint max)
        {
            long now = 0;
            var host = _kit.Start(_kit.Prepare(Tree("tests.p11002.wait-random-edge", "w", new string[0], WaitLeaf(min, max))), () => now,
                randomSeed: Seed);
            var node = RandomNode(host);
            var replay = Derived(host, node, Seed);
            // The full range is one unbounded draw (2^32 does not fit a bound); min = max a bounded draw of 1.
            var duration = max - min == uint.MaxValue ? replay.Next() : min + replay.Bounded(max - min + 1u);
            var first = host.Update();
            Assert.That(State(host, node), Is.EqualTo(replay.State), "exactly one draw at Enter");
            if (duration == 0u)
            {
                Assert.That(first, Is.EqualTo(NodeStatus.Success), "a zero duration ends at once");
                return;
            }
            Assert.That(first, Is.Null);
            now = duration - 1L;
            Assert.That(host.Update(), Is.Null);
            now = duration;
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Success));
        }

        [Test]
        public void WaitRandom_MinAboveMax_FailsWithoutDrawing()
        {
            var host = _kit.Start(_kit.Prepare(Tree("tests.p11002.wait-random-invalid", "w", new string[0], WaitLeaf(5000, 1000))),
                randomSeed: Seed);
            var node = RandomNode(host);
            var before = Derived(host, node, Seed).State;
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Failure));
            Assert.That(State(host, node), Is.EqualTo(before), "no draw");
        }

        /// <summary>
        /// Aborted while waiting by a retaining reactive selector (as the stage 2 wait test): the abort
        /// draws nothing; started again, the wait takes the next draw of its stream and counts from the
        /// new start.
        /// </summary>
        [Test]
        public void WaitRandom_AbortedWhileWaiting_DrawsNothing_AndTheRestartTakesTheNextDraw()
        {
            long now = 0;
            var json = Tree("tests.p11002.wait-random-abort", "root", new[] { Bool("interrupt") },
                Composite("root", "aibt.core.reactive-selector-retaining", "guard", "w"),
                Composite("guard", "aibt.core.reactive-sequence", "interrupted", "hold"),
                Leaf("interrupted", S + "bool-is-true", null, "\"key\": \"interrupt\""),
                Leaf("hold", S + "idle"),
                WaitLeaf(100_000, 200_000));
            var host = _kit.Start(_kit.Prepare(json), () => now, randomSeed: Seed);
            var node = RandomNode(host);
            var replay = Derived(host, node, Seed);
            var first = 100_000u + replay.Bounded(100_001u);
            Assert.That(host.Update(), Is.Null);
            Assert.That(State(host, node), Is.EqualTo(replay.State), "first Enter: one draw");

            now = 50_000;
            host.Fact("interrupt", true);
            Assert.That(host.Update(), Is.Null, "the guard branch runs; the wait is aborted");
            Assert.That(State(host, node), Is.EqualTo(replay.State), "the abort draws nothing");

            now = 60_000;
            host.Fact("interrupt", false);
            var second = 100_000u + replay.Bounded(100_001u);
            Assert.That(host.Update(), Is.Null, "the wait is entered again");
            Assert.That(State(host, node), Is.EqualTo(replay.State), "the restart takes the next draw");
            TestContext.WriteLine("first duration " + first + " us, after the restart " + second + " us");

            now = 60_000 + second - 1;
            Assert.That(host.Update(), Is.Null, "counts from the new start, not from 0 + " + first);
            now = 60_000 + second;
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Success));
        }

        // ------------------------------------------------------------------ random-float3-in-radius

        /// <summary>
        /// 1500 draws each, flat and 3D, ring/shell [min, radius]: every point equals the replay
        /// (computed here with Math.Cos/Sin, within 1e-4), its distance from the centre lies in
        /// [min, radius] (within 1e-4), flat keeps the centre's Y exactly. Distributions (6 sigma): the
        /// share inside half the radius (area 1/4, volume 1/8 for min 0) and the mean offset per axis (0).
        /// </summary>
        [TestCase(true, 0f, 1f)]
        [TestCase(false, 0f, 1f)]
        [TestCase(true, 2f, 4f)]
        [TestCase(false, 2f, 4f)]
        public void RandomFloat3InRadius_WritesAPointInTheRingOrShell(bool flat, float min, float radius)
        {
            const int Draws = 1500;
            var host = _kit.Start(_kit.Prepare(RadiusLoop(min, radius, flat)), randomSeed: Seed);
            var node = RandomNode(host);
            var replay = Derived(host, node, Seed);
            var inside = 0;
            double sumX = 0, sumY = 0, sumZ = 0;
            var verticalSpread = 0.0;
            Drive(host, node, replay, Draws, done =>
            {
                var expected = ExpectedPoint(replay, min, radius, flat);
                var point = host.ReadFloat3("r");
                Assert.That(point.X, Is.EqualTo(expected.x).Within(1e-4), "draw " + done + " x");
                Assert.That(point.Y, Is.EqualTo(expected.y).Within(1e-4), "draw " + done + " y");
                Assert.That(point.Z, Is.EqualTo(expected.z).Within(1e-4), "draw " + done + " z");
                double dx = point.X - Center.X, dy = point.Y - Center.Y, dz = point.Z - Center.Z;
                var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                Assert.That(distance, Is.InRange(min - 1e-4, radius + 1e-4), "draw " + done + " distance");
                if (flat) Assert.That(point.Y, Is.EqualTo(Center.Y), "flat keeps the centre's height exactly");
                verticalSpread = Math.Max(verticalSpread, Math.Abs(dy));
                if (distance < radius / 2) inside++;
                sumX += dx;
                sumY += dy;
                sumZ += dz;
            });
            if (!flat) Assert.That(verticalSpread, Is.GreaterThan(radius / 2), "3D points leave the centre's plane");
            if (min == 0f)
            {
                var p = flat ? 0.25 : 0.125;
                AssertWithinSixSigma(inside / (double)Draws, p, Math.Sqrt(p * (1 - p) / Draws), "share inside half the radius");
            }
            else Assert.That(inside, Is.Zero, "min-radius is half the radius here: nothing inside it");
            // E[x^2] <= radius^2 / 2 (flat) or / 3 (3D); 6 sigma of a mean with that variance.
            var sigma = radius / Math.Sqrt(2.0 * Draws);
            AssertWithinSixSigma(sumX / Draws, 0, sigma, "mean x offset");
            AssertWithinSixSigma(sumZ / Draws, 0, sigma, "mean z offset");
            if (!flat) AssertWithinSixSigma(sumY / Draws, 0, sigma, "mean y offset");
        }

        private static (double x, double y, double z) ExpectedPoint(Replay replay, float min, float radius, bool flat)
        {
            var distanceSample = replay.NextFloat();
            var heightSample = flat ? 0f : replay.NextFloat();
            var angle = 2.0 * Math.PI * replay.NextFloat();
            double lo = min, hi = radius;
            if (flat)
            {
                var r = Math.Sqrt(lo * lo + distanceSample * (hi * hi - lo * lo));
                return (Center.X + r * Math.Cos(angle), Center.Y, Center.Z + r * Math.Sin(angle));
            }
            var shell = Math.Pow(lo * lo * lo + distanceSample * (hi * hi * hi - lo * lo * lo), 1.0 / 3.0);
            var h = 2.0 * heightSample - 1.0;
            var around = Math.Sqrt(Math.Max(0.0, 1.0 - h * h));
            return (Center.X + shell * around * Math.Cos(angle), Center.Y + shell * h, Center.Z + shell * around * Math.Sin(angle));
        }

        [TestCase(true, 2)]
        [TestCase(false, 3)]
        public void RandomFloat3InRadius_RadiusZero_WritesTheCentre_AndStillDraws(bool flat, int draws)
        {
            var host = _kit.Start(_kit.Prepare(RadiusLoop(0f, 0f, flat)), randomSeed: Seed);
            var node = RandomNode(host);
            var replay = Derived(host, node, Seed);
            Drive(host, node, replay, 20, done =>
            {
                for (var draw = 0; draw < draws; draw++) replay.Next();
                Assert.That(host.ReadFloat3("r"), Is.EqualTo(Center), "draw " + done);
            });
        }

        [TestCase(-1f, 2f)]
        [TestCase(0f, -2f)]
        [TestCase(3f, 2f)]
        public void RandomFloat3InRadius_InvalidRadii_FailWithoutWritingOrDrawing(float min, float radius)
        {
            var json = Tree("tests.p11002.random-radius-invalid", "roll", new[] { Float3("c", 1f, 2f, 3f), Float3("r", 7f, 7f, 7f) },
                RadiusLeaf(min, radius, true));
            var host = _kit.Start(_kit.Prepare(json), randomSeed: Seed);
            var node = RandomNode(host);
            var before = Derived(host, node, Seed).State;
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Failure));
            Assert.That(host.ReadFloat3("r"), Is.EqualTo(new Float3Value(7f, 7f, 7f)), "nothing written");
            Assert.That(State(host, node), Is.EqualTo(before), "no draw");
        }

        /// <summary>
        /// A point that overflows float fails without writing, after its draws (documented): centre and
        /// radius float.MaxValue on X and Z, so the point is finite only when the angle points into the
        /// negative X and Z quarter. Over 40 seeds each host's outcome follows its replayed draws, and
        /// both outcomes occur (a quarter of the angles stay finite; 40 seeds all alike: below 1e-5).
        /// </summary>
        [Test]
        public void RandomFloat3InRadius_ANonFiniteResult_FailsWithoutWriting_AfterDrawing()
        {
            const float Max = float.MaxValue;
            var prepared = _kit.Prepare(Tree("tests.p11002.random-radius-overflow", "roll",
                new[] { Float3("c", Max, 0f, Max), Float3("r", 7f, 7f, 7f) }, RadiusLeaf(Max, Max, true)));
            int failures = 0, successes = 0;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var host = _kit.Start(prepared, randomSeed: seed);
                var node = RandomNode(host);
                var replay = Derived(host, node, seed);
                replay.NextFloat();
                var angle = 2.0 * Math.PI * replay.NextFloat();
                var x = (float)(Max + (double)Max * Math.Cos(angle));
                var z = (float)(Max + (double)Max * Math.Sin(angle));
                var finite = !float.IsInfinity(x) && !float.IsInfinity(z);
                var root = host.Update();
                Assert.That(State(host, node), Is.EqualTo(replay.State), "seed " + seed + ": the two flat draws were taken");
                if (finite)
                {
                    successes++;
                    Assert.That(root, Is.EqualTo(NodeStatus.Success), "seed " + seed);
                    Assert.That(host.ReadFloat3("r").X, Is.EqualTo(x).Within(Max * 1e-6f), "seed " + seed);
                }
                else
                {
                    failures++;
                    Assert.That(root, Is.EqualTo(NodeStatus.Failure), "seed " + seed);
                    Assert.That(host.ReadFloat3("r"), Is.EqualTo(new Float3Value(7f, 7f, 7f)), "seed " + seed + ": nothing written");
                }
            }
            TestContext.WriteLine("finite " + successes + ", overflowed " + failures);
            Assert.That(failures, Is.GreaterThan(0));
            Assert.That(successes, Is.GreaterThan(0));
        }
    }
}
