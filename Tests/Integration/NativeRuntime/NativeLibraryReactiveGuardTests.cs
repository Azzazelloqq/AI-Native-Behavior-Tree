using System;
using System.Linq;
using AIBT.Burst;
using AIBT.Simulation;
using NUnit.Framework;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-002 stage 1: a library condition guards a memory composite natively as the first child of a
    /// reactive sequence. The reactive sequence re-ticks the guard on every update, so when the guarded
    /// key changes (by a node of the running branch or by an external fact write) the memory composite
    /// is aborted at the next update. Native hosts do not run observer re-evaluation today
    /// (NativeObserverQueueV1 has no caller in Runtime), so an <c>observer</c> block compiles, runs and
    /// changes nothing: every case is checked with and without one. An observer outside a reactive
    /// composite is refused at compile time.
    /// </summary>
    public sealed class NativeLibraryReactiveGuardTests
    {
        private const string S = "aibt.native.";
        private NativeLibraryKit _kit;

        [SetUp]
        public void SetUp() => _kit = new NativeLibraryKit();

        [TearDown]
        public void TearDown() => _kit.Dispose();

        private static string Observer(bool withBlock, string key)
            => withBlock ? "\"mode\": \"self\", \"watchedKeys\": [\"" + key + "\"]" : null;

        // reactive-sequence [ fuel >= 1, memory-sequence [ fuel -= 1, fuel -= 1, done = true, idle ] ], fuel = 2.
        private static string SpendTree(bool withBlock)
            => Tree("tests.p11002.guard-node-write." + withBlock, "root", new[] { Int("fuel", 2), Bool("done") },
                Composite("root", "aibt.core.reactive-sequence", "guard", "work"),
                Leaf("guard", S + "int-at-least", "\"threshold\": 1", "\"key\": \"fuel\"", Observer(withBlock, "fuel")),
                Composite("work", "aibt.core.memory-sequence", "spend", "spend-again", "mark", "hold"),
                Leaf("spend", S + "increment-int", "\"amount\": 4294967295", "\"key\": \"fuel\""),
                Leaf("spend-again", S + "increment-int", "\"amount\": 4294967295", "\"key\": \"fuel\""),
                Leaf("mark", S + "set-bool", "\"value\": true", "\"key\": \"done\""),
                Leaf("hold", S + "idle"));

        [TestCase(true)]
        [TestCase(false)]
        public void AReactiveGuardAbortsTheMemorySequenceOnTheNextUpdateAfterANodeWritesItsKey(bool withObserverBlock)
        {
            var host = _kit.Start(_kit.Prepare(SpendTree(withObserverBlock)));

            Assert.That(host.Update(), Is.Null, "the memory sequence reaches idle in the update that spends the fuel");
            Assert.That(host.ReadInt("fuel"), Is.EqualTo(0));
            Assert.That(host.ReadBool("done"), Is.True, "nothing aborts the sequence inside the update (no native observer re-evaluation)");

            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Failure), "the next update re-ticks the guard, aborts the memory sequence and fails");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AFloat3ReactiveGuardAbortsTheMemorySequenceOnTheNextUpdateAfterAnExternalFactWrite(bool withObserverBlock)
        {
            var json = Tree("tests.p11002.guard-fact." + withObserverBlock, "root", new[] { Float3("target", 2f, 0f, 1f), Bool("started") },
                Composite("root", "aibt.core.reactive-sequence", "guard", "work"),
                Leaf("guard", S + "float3-is-set", null, "\"key\": \"target\"", Observer(withObserverBlock, "target")),
                Composite("work", "aibt.core.memory-sequence", "start", "hold"),
                Leaf("start", S + "set-bool", "\"value\": true", "\"key\": \"started\""),
                Leaf("hold", S + "idle"));
            var host = _kit.Start(_kit.Prepare(json));

            Assert.That(host.Update(), Is.Null);
            Assert.That(host.ReadBool("started"), Is.True);
            Assert.That(host.Update(), Is.Null, "while the target stays set the guarded sequence keeps running");

            host.Fact("target", new Float3Value(0f, 0f, 0f));
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Failure), "the update after the fact write aborts the memory sequence");
        }

        /// <summary>TreeSimulation behaves as the hosts: the abort comes at the next tick (no fact writes in a simulation).</summary>
        [TestCase(false, true)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public void TreeSimulation_AReactiveGuardAbortsTheMemorySequenceOnTheNextTick(bool useJobs, bool withObserverBlock)
        {
            var prepared = _kit.Prepare(SpendTree(withObserverBlock));
            var settings = TreeSimulationSettings.Default(AIBT.Tests.Integration.Simulation.SimulationTestPrograms.TickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = 1;
            using (var simulation = new TreeSimulation(settings))
            {
                Assert.That(simulation.TryAddGroup(prepared.Program, TreeGroupSettings.Default(1), out var group), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.TryAdd(ThinkSchedule.Every(1u, 0u), out _), Is.EqualTo(SimulationResult.Success));

                Assert.That(simulation.Step(1u), Is.EqualTo(SimulationResult.Success));
                Assert.That(group.GetFaults().Length, Is.Zero);
                Assert.That(group.GetRootCompletions().Length, Is.Zero, "still running after the first tick");
                var adapter = group.CoreForTests(0).GeneratedDispatch;
                Assert.That(ReadByte(adapter, prepared, "done", 0), Is.EqualTo(1), "mark ran in the first tick");
                Assert.That(ReadByte(adapter, prepared, "fuel", 0), Is.Zero);

                Assert.That(simulation.Step(2u), Is.EqualTo(SimulationResult.Success));
                var completions = group.GetRootCompletions();
                Assert.That(completions.Length, Is.EqualTo(1));
                Assert.That(completions[0].Result, Is.EqualTo(NodeStatus.Failure), "the second tick aborts the memory sequence");
            }
        }

        /// <summary>An observer block outside a reactive composite is not silently ignored: validation refuses it (AIBT2034).</summary>
        [Test]
        public void AnObserverBlockOutsideAReactiveCompositeIsRefusedAtCompile()
        {
            var json = Tree("tests.p11002.observer-outside-reactive", "work", new[] { Int("fuel", 2), Bool("done") },
                Composite("work", "aibt.core.memory-sequence", "guard", "mark"),
                Leaf("guard", S + "int-at-least", "\"threshold\": 1", "\"key\": \"fuel\"", Observer(true, "fuel")),
                Leaf("mark", S + "set-bool", "\"value\": true", "\"key\": \"done\""));
            var result = NativeLibraryKit.CompileResult(json);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Select(d => d.Code.Value), Does.Contain("AIBT2034"));
        }

        private static byte ReadByte(GeneratedTreeDispatchAdapterV2 adapter, NativeLibraryKit.Prepared prepared, string key, uint index)
        {
            Assert.That(adapter.TryReadTreeValueByte(prepared.Slots[key].Offset + index, out var value), Is.True, key);
            return value;
        }
    }
}
