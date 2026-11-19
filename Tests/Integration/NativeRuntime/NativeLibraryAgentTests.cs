using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-002 stage 2 (ADR-P11-002 §7): the timing and signal nodes on <see cref="NativeTreeAgent"/>,
    /// driven through the real scheduler and world: game facts by <see cref="NativeTreeAgent.SetFact{T}"/>
    /// (Bool and Int64), signals by <see cref="NativeTreeAgent.OnEffect(string, System.Action{int})"/> on
    /// the package channel aibt.native.signal. The agent's host reads Unity time; the controlled-clock
    /// cases run on the same host type in <see cref="NativeLibraryTimingTests"/>.
    /// </summary>
    public sealed class NativeLibraryAgentTests
    {
        private const string S = "aibt.native.";
        private const string SignalChannel = "aibt.native.signal";

        private readonly List<Object> _created = new List<Object>();
        private readonly List<NativeTreeAgent> _agents = new List<NativeTreeAgent>();
        private NativeLibraryKit _kit;
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;
        private int _baselinePrograms;

        [SetUp]
        public void SetUp()
        {
            _kit = new NativeLibraryKit();
            _baselinePrograms = NativeTreeProgramCache.LiveProgramCount;
            var worldObject = Track(new GameObject("AIBT.Tests.P11002.World"));
            _scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            _world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(_world, "Awake");
            InvokePrivate(_world, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var agent in _agents) if (agent != null) agent.Deactivate();
            _agents.Clear();
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) Object.DestroyImmediate(_created[index]);
            _created.Clear();
            _kit.Dispose();
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms), "A test leaked a shared program.");
        }

        [Test]
        public void Signals_ReachOnEffect_ExactlyOncePerEmission_AfterAFactWrite()
        {
            var agent = Spawn(Tree("tests.p11002.agent.signals", "seq", new[] { Bool("go") },
                Composite("seq", "aibt.core.memory-sequence", "wait", "five", "zero", "max"),
                Leaf("wait", S + "wait-until-true", "\"timeout-microseconds\": 0", "\"key\": \"go\""),
                Leaf("five", S + "signal", "\"code\": 5"),
                Leaf("zero", S + "signal", "\"code\": 0"),
                Leaf("max", S + "signal", "\"code\": 2147483647")));
            var codes = new List<int>();
            agent.OnEffect(SignalChannel, (int code) => codes.Add(code));

            Frames(3);
            Assert.That(codes, Is.Empty, "Nothing is emitted while wait-until-true waits.");
            Assert.That(agent.IsRunning, Is.True);

            agent.SetFact("go", true);
            Frames(1);
            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(codes, Is.EqualTo(new[] { 5, 0, int.MaxValue }), "Each emission arrives once, in tree order.");

            Frames(3);
            Assert.That(codes, Has.Count.EqualTo(3), "Delivered effects are never replayed.");
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
        }

        [Test]
        public void ASignalCodeAboveInt32Max_FailsAndEmitsNothing()
        {
            var agent = Spawn(Tree("tests.p11002.agent.signal-too-large", "seq", new string[0],
                Composite("seq", "aibt.core.memory-sequence", "large", "after"),
                Leaf("large", S + "signal", "\"code\": 2147483648"),
                Leaf("after", S + "signal", "\"code\": 1")));
            var codes = new List<int>();
            agent.OnEffect(SignalChannel, (int code) => codes.Add(code));

            Frames(2);
            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Failure));
            Assert.That(codes, Is.Empty);
        }

        [Test]
        public void AnInt64DeadlineFact_ReachesDeadlinePassed()
        {
            var agent = Spawn(Tree("tests.p11002.agent.deadline-fact", "c", new[] { Long("ready-at") },
                Leaf("c", S + "deadline-passed", null, "\"key\": \"ready-at\"")));

            agent.SetFact("ready-at", long.MaxValue);
            Frames(1);
            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Failure), "Unity time has not reached long.MaxValue.");

            agent.SetFact("ready-at", long.MinValue);
            agent.Restart();
            Frames(1);
            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(agent.CompletedRuns, Is.EqualTo(2));
        }

        [Test]
        public void SetDeadlineAndDeadlinePassed_ShareOneTimePointPerUpdate()
        {
            // Both nodes read the same update time point, so a zero duration has passed in the same
            // update (exactly at the deadline), and a 4000 s duration has not.
            var now = Spawn(Tree("tests.p11002.agent.deadline-now", "seq", new[] { Long("d") },
                Composite("seq", "aibt.core.memory-sequence", "start", "check"),
                Leaf("start", S + "set-deadline", "\"duration-microseconds\": 0", "\"key\": \"d\""),
                Leaf("check", S + "deadline-passed", null, "\"key\": \"d\"")));
            var later = Spawn(Tree("tests.p11002.agent.deadline-later", "seq", new[] { Long("d") },
                Composite("seq", "aibt.core.memory-sequence", "start", "not"),
                Leaf("start", S + "set-deadline", "\"duration-microseconds\": 4000000000", "\"key\": \"d\""),
                Composite("not", "aibt.core.inverter", "check"),
                Leaf("check", S + "deadline-passed", null, "\"key\": \"d\"")));

            Frames(1);
            Assert.That(now.LastResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(later.LastResult, Is.EqualTo(NodeStatus.Success));
        }

        private NativeTreeAgent Spawn(string json)
        {
            var prepared = _kit.Prepare(json);
            var gameObject = Track(new GameObject("AIBT.Tests.P11002.Agent" + _agents.Count));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = prepared.Asset;
            agent.World = _world;
            agent.RestartMode = NativeTreeRestartMode.Never;
            _agents.Add(agent);
            agent.Activate();
            Assert.That(agent.LastError, Is.Null, "The agent starts.");
            return agent;
        }

        private void Frames(int count)
        {
            for (var frame = 0; frame < count; frame++)
            {
                InvokePrivate(_scheduler, "Update");
                foreach (var agent in _agents) if (agent != null) agent.ProcessRootResult();
            }
        }

        private T Track<T>(T value) where T : Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
