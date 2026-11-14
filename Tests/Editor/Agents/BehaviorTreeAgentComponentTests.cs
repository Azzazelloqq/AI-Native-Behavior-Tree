using System.Collections.Generic;
using System.Text.RegularExpressions;
using AIBT.Agents;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P9-001: the <see cref="BehaviorTreeAgent"/> component itself, driven with manual ticks in
    /// Edit Mode: automatic restart, Restart/Stop requested from inside a node, and fault reporting.
    /// </summary>
    public sealed class BehaviorTreeAgentComponentTests
    {
        private static readonly List<string> Log = new List<string>();
        private static bool s_requestRestart;
        private static bool s_requestStop;

        private ManagedNodeSet _nodes;
        private GameObject _gameObject;
        private BehaviorTreeAgent _agent;
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            Log.Clear();
            s_requestRestart = false;
            s_requestStop = false;
            _nodes = ManagedNodeSet.Create(new[] { typeof(RecordNode), typeof(HoldNode), typeof(ThrowNode) });
            Assert.That(_nodes.Problems, Is.Empty, string.Join("\n", _nodes.Problems));
            _gameObject = new GameObject("agent-under-test");
            _created.Add(_gameObject);
            _agent = _gameObject.AddComponent<BehaviorTreeAgent>();
            _agent.TickMode = AgentTickMode.Manual;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        [Test]
        public void ACompletedTreeRaisesCompletedAndStartsAgainOnTheNextTick()
        {
            Assign(Tree("sequence", null,
                Node("sequence", "aibt.core.memory-sequence", null, "a", "b"),
                Node("a", "test.record", "\"text\": \"a\""),
                Node("b", "test.record", "\"text\": \"b\"")));
            var completed = new List<NodeStatus>();
            _agent.Completed += completed.Add;

            _agent.Tick(0.1f);
            Assert.That(completed, Is.EqualTo(new[] { NodeStatus.Success }));
            Assert.That(_agent.IsRunning, Is.True, "A pending automatic restart counts as running.");

            _agent.Tick(0.1f);
            Assert.That(Log, Is.EqualTo(new[] { "a", "b", "a", "b" }));
            Assert.That(completed.Count, Is.EqualTo(2));
        }

        [Test]
        public void WithoutRestartOnCompleteAFinishedTreeStaysFinished()
        {
            _agent.RestartOnComplete = false;
            Assign(Tree("a", null, Node("a", "test.record", "\"text\": \"a\"")));

            _agent.Tick(0.1f);
            _agent.Tick(0.1f);
            Assert.That(Log, Is.EqualTo(new[] { "a" }));
            Assert.That(_agent.IsRunning, Is.False);
            Assert.That(_agent.LastResult, Is.EqualTo(NodeStatus.Success));

            _agent.Restart();
            _agent.Tick(0.1f);
            Assert.That(Log, Is.EqualTo(new[] { "a", "a" }));
        }

        [Test]
        public void RestartCalledFromInsideANodeHappensOnTheNextTick()
        {
            Assign(Tree("hold", null, Node("hold", "test.hold", "\"name\": \"hold\"")));
            s_requestRestart = true;

            _agent.Tick(0.1f);
            Assert.That(Log, Is.EqualTo(new[] { "enter hold", "tick hold" }),
                "The node that asked for the restart finishes its own callback undisturbed.");
            Assert.That(_agent.IsRunning, Is.True);

            Log.Clear();
            _agent.Tick(0.1f);
            Assert.That(Log, Is.EqualTo(new[] { "abort hold", "exit hold", "enter hold", "tick hold" }));
        }

        [Test]
        public void StopCalledFromInsideANodeAbortsAfterTheTickAndStaysStoppedUntilRestart()
        {
            Assign(Tree("hold", null, Node("hold", "test.hold", "\"name\": \"hold\"")));
            s_requestStop = true;

            _agent.Tick(0.1f);
            Assert.That(Log, Is.EqualTo(new[] { "enter hold", "tick hold", "abort hold", "exit hold" }));
            Assert.That(_agent.IsRunning, Is.False);

            Log.Clear();
            _agent.Tick(0.1f);
            Assert.That(Log, Is.Empty, "A stopped agent is not ticked.");

            _agent.Restart();
            _agent.Tick(0.1f);
            Assert.That(Log, Is.EqualTo(new[] { "enter hold", "tick hold" }));
            Assert.That(_agent.IsRunning, Is.True);
        }

        [Test]
        public void ANodeExceptionRaisesFaultedExactlyOnceAndStopsTheTree()
        {
            Assign(Tree("boom", null, Node("boom", "test.throw", null)));
            var faults = new List<string>();
            _agent.Faulted += faults.Add;

            LogAssert.Expect(LogType.Exception, new Regex("node failure"));
            LogAssert.Expect(LogType.Error, new Regex("stopped in node 'boom'"));
            _agent.Tick(0.1f);
            _agent.Tick(0.1f);
            _agent.Tick(0.1f);

            Assert.That(faults.Count, Is.EqualTo(1));
            Assert.That(faults[0], Does.Contain("stopped in node 'boom'"));
            Assert.That(_agent.HasFaulted, Is.True);
            Assert.That(_agent.LastError, Is.EqualTo(faults[0]));
            Assert.That(_agent.IsRunning, Is.False);
        }

        private void Assign(string json)
        {
            var source = BehaviorTreeSource.FromJson(json, "agent-test", _nodes);
            _created.Add(source);
            _agent.Tree = source;
            Assert.That(_agent.Blackboard, Is.Not.Null, _agent.LastError);
        }

        [BtNode("test.record")]
        private sealed class RecordNode : BtAction
        {
            public string text = "";

            protected override NodeStatus OnTick()
            {
                Log.Add(text);
                return NodeStatus.Success;
            }
        }

        [BtNode("test.hold")]
        private sealed class HoldNode : BtAction
        {
            public string name = "";

            protected override void OnEnter() => Log.Add("enter " + name);

            protected override NodeStatus OnTick()
            {
                Log.Add("tick " + name);
                if (s_requestRestart)
                {
                    s_requestRestart = false;
                    Agent.Restart();
                }

                if (s_requestStop)
                {
                    s_requestStop = false;
                    Agent.Stop();
                }

                return NodeStatus.Running;
            }

            protected override void OnAbort() => Log.Add("abort " + name);

            protected override void OnExit() => Log.Add("exit " + name);
        }

        [BtNode("test.throw")]
        private sealed class ThrowNode : BtAction
        {
            protected override NodeStatus OnTick() => throw new System.InvalidOperationException("node failure");
        }
    }
}
