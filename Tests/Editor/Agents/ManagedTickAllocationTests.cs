using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;
using static AIBT.Tests.Editor.Agents.StandardNodeKit;
using Object = UnityEngine.Object;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P10-001: a warmed-up <see cref="BehaviorTreeAgent"/> tick allocates no managed memory.
    /// Allocations are counted with the profiler's "GC.Alloc" recorder filtered to this thread
    /// (<c>GC.GetAllocatedBytesForCurrentThread</c> always returns 0 under Mono), so the measure is
    /// the number of managed allocations, and the tolerance is zero: one allocation in
    /// <see cref="MeasuredTicks"/> ticks fails the test.
    /// </summary>
    public sealed class ManagedTickAllocationTests
    {
        private const int WarmupTicks = 300;
        private const int MeasuredTicks = 400;
        private const float Delta = 1f / 60f;

        private readonly List<Object> _created = new List<Object>();
        private Recorder _recorder;

        [SetUp]
        public void SetUp()
        {
            _recorder = Recorder.Get("GC.Alloc");
            _recorder.FilterToCurrentThread();
            _recorder.enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            _recorder.enabled = false;
            _recorder.CollectFromAllThreads();
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        [Test]
        public void TheRecorderCountsAllocationsOnThisThread()
        {
            // Guards the measurement itself: a recorder that sees nothing would pass every test below.
            object kept = null;
            var count = CountAllocations(() => kept = new byte[64], 10);
            Assert.That(kept, Is.Not.Null);
            Assert.That(count, Is.EqualTo(10));
        }

        [Test]
        public void ATreeThatCompletesAndRestartsEveryFewTicksDoesNotAllocate()
        {
            // Memory composites, a decorator and a restart about every fifth tick.
            var agent = Create(Tree("root", null,
                Node("root", "aibt.core.memory-selector", null, "first", "second"),
                Node("first", "aibt.core.inverter", null, "succeed"),
                Node("succeed", "aibt.unity.succeed", null),
                Node("second", "aibt.core.memory-sequence", null, "wait-a", "wait-b"),
                Node("wait-a", "aibt.unity.wait-frames", "\"frames\": 2"),
                Node("wait-b", "aibt.stdlib.wait", "\"ticks\": 2")));
            var completions = 0;
            agent.Completed += _ => completions++;

            AssertTicksDoNotAllocate(agent, () => agent.Tick(Delta));
            Assert.That(completions, Is.GreaterThan(100), "The tree must actually complete and restart while measured.");
        }

        [Test]
        public void AReactiveTreeThatReevaluatesEveryConditionEveryTickDoesNotAllocate()
        {
            var agent = Create(Tree("root", Keys(Bool("a", true), Bool("b", true), Bool("c", true), Float("level", 5f)),
                Node("root", "aibt.core.reactive-sequence", null, "a", "b", "c", "level", "hold"),
                Node("a", "aibt.unity.is-true", "\"key\": \"a\""),
                Node("b", "aibt.unity.is-true", "\"key\": \"b\""),
                Node("c", "aibt.unity.is-true", "\"key\": \"c\""),
                Node("level", "aibt.unity.compare-float", "\"key\": \"level\", \"comparison\": \"greater-or-equal\", \"value\": 1.0"),
                Node("hold", "aibt.stdlib.wait", "\"ticks\": 4000000000")));

            AssertTicksDoNotAllocate(agent, () => agent.Tick(Delta));
            Assert.That(agent.IsRunning, Is.True);
        }

        [Test]
        public void BlackboardReadsAndWritesFromNodesAndGameCodeDoNotAllocate()
        {
            // Nodes write and compare floats; a watched condition (interrupt "both") switches
            // branches when game code toggles its key; game code also reads every tick.
            var agent = Create(Tree("priority", Keys(Bool("visible"), Float("alert"), Float("phase"), Float("distance"), Int("count")),
                Node("priority", "aibt.core.memory-selector", null, "chase", "patrol"),
                Node("chase", "aibt.core.memory-sequence", null, "sees", "alert-on", "chase-wait"),
                Node("sees", "aibt.unity.is-true", "\"key\": \"visible\", \"interrupt\": \"both\""),
                Node("alert-on", "aibt.unity.set-float", "\"key\": \"alert\", \"value\": 1.0"),
                Node("chase-wait", "aibt.unity.wait-frames", "\"frames\": 5"),
                Node("patrol", "aibt.core.memory-sequence", null, "alert-off", "step", "check", "patrol-wait"),
                Node("alert-off", "aibt.unity.set-float", "\"key\": \"alert\", \"value\": 0.0"),
                Node("step", "aibt.unity.set-float", "\"key\": \"phase\", \"value\": 2.0"),
                Node("check", "aibt.unity.compare-float", "\"key\": \"phase\", \"comparison\": \"greater-or-equal\", \"value\": 1.0"),
                Node("patrol-wait", "aibt.unity.wait-frames", "\"frames\": 10")));
            var blackboard = agent.Blackboard;
            var frame = 0;
            var alertSum = 0f;

            AssertTicksDoNotAllocate(agent, () =>
            {
                frame++;
                blackboard.Set("visible", frame / 7 % 2 == 0);
                blackboard.Set("distance", frame * 0.25f);
                blackboard.Set("count", frame);
                agent.Tick(Delta);
                alertSum += blackboard.Get<float>("alert");
                if (!blackboard.TryGet<int>("count", out var count) || count != frame) Assert.Fail("Game code must read back its own write.");
            });
            Assert.That(alertSum, Is.GreaterThan(0f), "The chase branch must run while measured.");
            Assert.That(blackboard.Get<float>("phase"), Is.EqualTo(2f), "The patrol branch must run while measured.");
        }

        [Test]
        public void DecoratorsThatAreReactivatedEveryFewTicksDoNotAllocate()
        {
            // Repeater, timeout, cooldown, succeeder and failer decode their configuration on every
            // activation; a reactive selector tries a failing child before its fallback.
            var agent = Create(Tree("root", null,
                Node("root", "aibt.core.memory-sequence", null, "repeat", "limit", "gate", "guarded"),
                Node("repeat", "aibt.core.repeater", "\"count\": 2, \"stopOnFailure\": false", "repeat-wait"),
                Node("repeat-wait", "aibt.unity.wait-frames", "\"frames\": 1"),
                Node("limit", "aibt.core.timeout", "\"durationMicroseconds\": 1000000000, \"terminalResult\": \"failure\"", "limit-wait"),
                Node("limit-wait", "aibt.unity.wait-frames", "\"frames\": 2"),
                Node("gate", "aibt.core.cooldown",
                    "\"durationMicroseconds\": 1, \"blockedResult\": \"success\", \"startPolicy\": \"on-enter\"", "gate-wait"),
                Node("gate-wait", "aibt.unity.wait-frames", "\"frames\": 1"),
                Node("guarded", "aibt.core.reactive-selector", null, "never", "fallback"),
                Node("never", "aibt.core.failer", null, "never-succeed"),
                Node("never-succeed", "aibt.unity.succeed", null),
                Node("fallback", "aibt.core.succeeder", null, "fallback-wait"),
                Node("fallback-wait", "aibt.unity.succeed", null)));
            var completions = 0;
            agent.Completed += _ => completions++;

            AssertTicksDoNotAllocate(agent, () => agent.Tick(Delta));
            Assert.That(completions, Is.GreaterThan(20), "The tree must actually complete and restart while measured.");
        }

        [Test]
        public void AParallelThatSuspendsAndResumesBranchesEveryTickDoesNotAllocate()
        {
            // Each update suspends every running branch and resumes it on the next one; the tree
            // completes and restarts about every fourth tick.
            var agent = Create(Tree("root", null,
                Node("root", "aibt.core.parallel", "\"policy\": \"require-all-success\"", "left", "right"),
                Node("left", "aibt.core.memory-sequence", null, "left-wait", "left-done"),
                Node("left-wait", "aibt.unity.wait-frames", "\"frames\": 3"),
                Node("left-done", "aibt.unity.succeed", null),
                Node("right", "aibt.unity.wait-frames", "\"frames\": 2")));
            var completions = 0;
            agent.Completed += _ => completions++;

            AssertTicksDoNotAllocate(agent, () => agent.Tick(Delta));
            Assert.That(completions, Is.GreaterThan(50), "The parallel must actually complete and restart while measured.");
        }

        [Test]
        public void ExplicitRestartsWithAndWithoutBlackboardResetDoNotAllocate()
        {
            var agent = Create(Tree("root", Keys(Float("phase"), Bool("flag")),
                Node("root", "aibt.core.memory-sequence", null, "step", "hold"),
                Node("step", "aibt.unity.set-float", "\"key\": \"phase\", \"value\": 3.0"),
                Node("hold", "aibt.unity.wait-frames", "\"frames\": 1000000")));
            var blackboard = agent.Blackboard;
            var resets = 0;

            AssertTicksDoNotAllocate(agent, () =>
            {
                agent.Tick(Delta);
                blackboard.Set("flag", resets % 2 == 0);
                agent.Restart(resetBlackboard: ++resets % 2 == 0);
                agent.Tick(Delta);
            });
            Assert.That(blackboard.Get<float>("phase"), Is.EqualTo(3f));
        }

        private BehaviorTreeAgent Create(string json)
        {
            var source = BehaviorTreeSource.FromJson(json, "allocation-test", StandardNodeKit.Nodes);
            _created.Add(source);
            var gameObject = new GameObject("allocation-test-agent");
            _created.Add(gameObject);
            var agent = gameObject.AddComponent<BehaviorTreeAgent>();
            agent.TickMode = AgentTickMode.Manual;
            agent.Tree = source;
            Assert.That(agent.Blackboard, Is.Not.Null, agent.LastError);
            return agent;
        }

        private void AssertTicksDoNotAllocate(BehaviorTreeAgent agent, Action tick)
        {
            for (var index = 0; index < WarmupTicks; index++) tick();
            Assert.That(agent.LastError, Is.Null);
            var allocations = CountAllocations(tick, MeasuredTicks);
            Assert.That(agent.LastError, Is.Null);
            Assert.That(allocations, Is.Zero,
                allocations + " managed allocation(s) in " + MeasuredTicks + " warmed-up ticks; the tolerance is zero.");
        }

        private int CountAllocations(Action action, int calls)
        {
            _recorder.enabled = false;
            _recorder.enabled = true;
            for (var index = 0; index < calls; index++) action();
            _recorder.enabled = false;
            return _recorder.sampleBlockCount;
        }
    }
}
