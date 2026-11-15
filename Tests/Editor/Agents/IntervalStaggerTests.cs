using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;
using Object = UnityEngine.Object;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P10-001: <see cref="BehaviorTreeAgent.StaggerIntervalTicks"/> spreads the ticks of many
    /// Interval agents over frames, keeps them spread after a frame longer than the interval,
    /// passes only real time to the tree, and leaves the default (off) exactly as before: every
    /// agent ticks on the same frame.
    /// Frames are driven through <see cref="BehaviorTreeAgent.AdvanceFrame"/>, Update's body.
    /// </summary>
    public sealed class IntervalStaggerTests
    {
        private const float Interval = 0.1f;
        private const float FrameDelta = 1f / 60f;
        private const int Agents = 120;
        private const int Frames = 600;

        private readonly List<Object> _created = new List<Object>();
        private BehaviorTreeSource _source;

        [SetUp]
        public void SetUp()
        {
            _source = BehaviorTreeSource.FromJson(
                Tree("hold", null, Node("hold", "aibt.stdlib.wait", "\"ticks\": 4000000000")),
                "stagger-test",
                StandardNodeKit.Nodes);
            _created.Add(_source);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        [Test]
        public void WithoutStaggerEveryIntervalAgentTicksOnTheSameFrames()
        {
            var agents = CreateAgents(stagger: false);
            var perFrame = Run(agents, out _);

            Assert.That(perFrame.Where(count => count != 0), Is.All.EqualTo(Agents),
                "The default is unchanged: all agents created together tick together.");
        }

        [Test]
        public void WithStaggerTicksSpreadOverTheFramesOfAnInterval()
        {
            var plain = Run(CreateAgents(stagger: false), out _);
            var staggered = Run(CreateAgents(stagger: true), out _);

            var framesPerInterval = Mathf.CeilToInt(Interval / FrameDelta);
            var fairShare = Mathf.CeilToInt((float)Agents / framesPerInterval);
            Assert.That(staggered.Max(), Is.LessThanOrEqualTo(fairShare + 2),
                "No frame carries much more than its share of the agents' ticks.");
            Assert.That(plain.Max(), Is.EqualTo(Agents));
            Assert.That(staggered.Sum(), Is.InRange(plain.Sum(), plain.Sum() + Agents),
                "Staggering moves ticks between frames; at most one extra (earlier) first tick per agent.");
        }

        [Test]
        public void StaggeredTicksReceiveTheRealTimeAndKeepTheInterval()
        {
            var staggered = CreateAgents(stagger: true, count: 1)[0];
            Run(new[] { staggered }, out var deltas);

            var ticks = deltas[staggered];
            Assert.That(ticks.Count, Is.GreaterThan(50));
            Assert.That(ticks.Sum(tick => tick.Delta), Is.EqualTo((ticks[ticks.Count - 1].Frame + 1) * FrameDelta).Within(1e-3f),
                "The deltas add up to the real time; the stagger offset is never passed to a node.");
            var framesPerInterval = Mathf.RoundToInt(Interval / FrameDelta);
            for (var index = 1; index < ticks.Count; index++)
                Assert.That(ticks[index].Frame - ticks[index - 1].Frame, Is.InRange(framesPerInterval - 1, framesPerInterval + 1),
                    "After the first tick the agent ticks once per interval.");
        }

        [Test]
        public void ALongFirstFrameDoesNotSynchronizeStaggeredAgents()
        {
            // The frame after spawning many agents is often longer than the interval.
            var plain = Run(CreateAgents(stagger: false), out _, firstFrameDelta: 0.5f);
            var staggered = Run(CreateAgents(stagger: true), out _, firstFrameDelta: 0.5f);

            var framesPerInterval = Mathf.CeilToInt(Interval / FrameDelta);
            var fairShare = Mathf.CeilToInt((float)Agents / framesPerInterval);
            Assert.That(plain.Skip(1).Where(count => count != 0), Is.All.EqualTo(Agents),
                "Unchanged default: after a long frame all agents tick together.");
            Assert.That(staggered[0], Is.EqualTo(Agents), "The long frame itself is due for every agent.");
            Assert.That(staggered.Skip(1).Max(), Is.LessThanOrEqualTo(fairShare + 2),
                "After it, the staggered agents keep their phases instead of ticking together.");
        }

        private BehaviorTreeAgent[] CreateAgents(bool stagger, int count = Agents)
        {
            var agents = new BehaviorTreeAgent[count];
            for (var index = 0; index < count; index++)
            {
                var gameObject = new GameObject("stagger-agent");
                _created.Add(gameObject);
                var agent = gameObject.AddComponent<BehaviorTreeAgent>();
                agent.TickMode = AgentTickMode.Interval;
                agent.TickInterval = Interval;
                agent.StaggerIntervalTicks = stagger;
                agent.Tree = _source;
                Assert.That(agent.Blackboard, Is.Not.Null, agent.LastError);
                agents[index] = agent;
            }

            return agents;
        }

        private static int[] Run(IReadOnlyList<BehaviorTreeAgent> agents, out Dictionary<BehaviorTreeAgent, List<(int Frame, float Delta)>> ticks, float firstFrameDelta = FrameDelta)
        {
            var perFrame = new int[Frames];
            ticks = agents.ToDictionary(agent => agent, _ => new List<(int Frame, float Delta)>());
            for (var frame = 0; frame < Frames; frame++)
            {
                foreach (var agent in agents)
                {
                    var before = agent.Instance.TickCount;
                    agent.AdvanceFrame(frame == 0 ? firstFrameDelta : FrameDelta);
                    if (agent.Instance.TickCount == before) continue;
                    perFrame[frame]++;
                    ticks[agent].Add((frame, agent.Instance.DeltaTime));
                }
            }

            return perFrame;
        }
    }
}
