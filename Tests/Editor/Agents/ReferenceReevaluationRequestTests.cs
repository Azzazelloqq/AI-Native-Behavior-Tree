using System;
using System.Collections.Generic;
using AIBT.Agents;
using NUnit.Framework;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P9-001: <c>ReferenceExecutionMachine.TryRequestReevaluation</c>, the managed layer's
    /// interrupt primitive. It is accepted only for a memory composite with a running child on the
    /// active path, and it never overrides a reactive composite above it.
    /// </summary>
    public sealed class ReferenceReevaluationRequestTests
    {
        private static readonly List<string> Log = new List<string>();

        private ManagedNodeSet _nodes;

        [SetUp]
        public void SetUp()
        {
            Log.Clear();
            _nodes = ManagedNodeSet.Create(new[] { typeof(HoldNode), typeof(RecordNode), typeof(FailingCheckNode) });
            Assert.That(_nodes.Problems, Is.Empty, string.Join("\n", _nodes.Problems));
        }

        [Test]
        public void AnAcceptedRequestMakesAMemorySequenceStartAgainFromItsFirstChild()
        {
            var instance = Start(SequenceTree("aibt.core.memory-sequence"));
            instance.Tick(0f);
            Assert.That(Log, Is.EqualTo(new[] { "a", "enter hold" }));

            Assert.That(instance.Machine.TryRequestReevaluation(Index(instance, "sequence")), Is.True);
            Log.Clear();
            instance.Tick(0f);
            Assert.That(Log, Is.EqualTo(new[] { "abort hold", "exit hold", "a", "enter hold" }));
        }

        [Test]
        public void ARequestIsRefusedWhileTheCompositeHasNoRunningChild()
        {
            var instance = Start(SequenceTree("aibt.core.memory-sequence"));
            Assert.That(instance.Machine.TryRequestReevaluation(Index(instance, "sequence")), Is.False,
                "Before the first update nothing is active.");

            var finished = Start(Tree("sequence", null,
                Node("sequence", "aibt.core.memory-sequence", null, "a"),
                Node("a", "test.record", "\"text\": \"a\"")));
            Assert.That(finished.Tick(0f), Is.EqualTo(ManagedTickOutcome.Completed));
            Assert.That(finished.Machine.TryRequestReevaluation(Index(finished, "sequence")), Is.False,
                "A finished tree has no running child to abandon.");
        }

        [Test]
        public void ARequestIsRefusedForACompositeOffTheActivePath()
        {
            var instance = Start(Tree("priority", null,
                Node("priority", "aibt.core.memory-selector", null, "first", "fallback"),
                Node("first", "aibt.core.memory-sequence", null, "check", "never"),
                Node("check", "test.failing-check", null),
                Node("never", "test.record", "\"text\": \"never\""),
                Node("fallback", "test.hold", "\"name\": \"hold\"")));
            instance.Tick(0f);
            Assert.That(instance.Machine.TryRequestReevaluation(Index(instance, "first")), Is.False,
                "The sequence already failed and is not on the active path.");
            Assert.That(instance.Machine.TryRequestReevaluation(Index(instance, "priority")), Is.True);
        }

        [Test]
        public void ARequestIsRefusedForANonMemoryCompositeOrALeaf()
        {
            var reactive = Start(SequenceTree("aibt.core.reactive-sequence"));
            reactive.Tick(0f);
            Assert.That(reactive.Machine.TryRequestReevaluation(Index(reactive, "sequence")), Is.False,
                "A reactive composite already reevaluates on every update.");
            Assert.That(reactive.Machine.TryRequestReevaluation(Index(reactive, "hold")), Is.False, "A leaf is not a composite.");
        }

        [Test]
        public void ARequestUnderAReactiveCompositeDefersToTheNormativeReactiveReset()
        {
            // reactive-selector > [failing check, memory-sequence > (a, hold)]. The reactive reset
            // re-checks the first child every update; an accepted request on the inner memory
            // sequence must not replace that reset with a narrower one that skips the check.
            var withRequest = Start(ReactiveAboveTree());
            var withoutRequest = Start(ReactiveAboveTree());
            withRequest.Tick(0f);
            withoutRequest.Tick(0f);

            Log.Clear();
            withoutRequest.Tick(0f);
            var expected = Log.ToArray();
            Assert.That(expected, Has.Member("check"));

            Assert.That(withRequest.Machine.TryRequestReevaluation(Index(withRequest, "sequence")), Is.True);
            Log.Clear();
            withRequest.Tick(0f);
            Assert.That(Log, Is.EqualTo(expected));
        }

        private ManagedTreeInstance Start(string json)
        {
            Assert.That(ManagedTreeProgram.TryCompile(json, "trees/test.aibt", _nodes, out var program, out var diagnostics), Is.True,
                ManagedTreeProgram.Describe(diagnostics));
            return new ManagedTreeInstance(program, null);
        }

        private static RuntimeNodeIndex Index(ManagedTreeInstance instance, string nodeId)
        {
            var index = Array.IndexOf(instance.NodeIds, nodeId);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Unknown node '" + nodeId + "'.");
            return new RuntimeNodeIndex((uint)index);
        }

        private static string SequenceTree(string compositeType)
        {
            return Tree("sequence", null,
                Node("sequence", compositeType, null, "a", "hold"),
                Node("a", "test.record", "\"text\": \"a\""),
                Node("hold", "test.hold", "\"name\": \"hold\""));
        }

        private static string ReactiveAboveTree()
        {
            return Tree("priority", null,
                Node("priority", "aibt.core.reactive-selector", null, "check", "sequence"),
                Node("check", "test.failing-check", null),
                Node("sequence", "aibt.core.memory-sequence", null, "a", "hold"),
                Node("a", "test.record", "\"text\": \"a\""),
                Node("hold", "test.hold", "\"name\": \"hold\""));
        }

        [BtNode("test.hold")]
        private sealed class HoldNode : BtAction
        {
            public string name = "";

            protected override void OnEnter() => Log.Add("enter " + name);

            protected override NodeStatus OnTick() => NodeStatus.Running;

            protected override void OnAbort() => Log.Add("abort " + name);

            protected override void OnExit() => Log.Add("exit " + name);
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

        [BtNode("test.failing-check")]
        private sealed class FailingCheckNode : BtCondition
        {
            protected override bool Check()
            {
                Log.Add("check");
                return false;
            }
        }
    }
}
