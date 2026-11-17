using System;
using AIBT.Agents;
using NUnit.Framework;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P10-013 v1 pins (ADR-P10-013 §1.3, finding F2): a v1 reactive composite aborts and
    /// re-enters its running child on every update, so multi-update work under it never makes
    /// progress. These tests pin that behaviour with the real standard leaves and core decorators
    /// on the reference executor, so the retaining composites added beside v1 cannot change it by
    /// accident. Each pin has a memory-sequence control that shows the same subtree does finish.
    /// </summary>
    public sealed class ReactiveV1RestartPinTests
    {
        private const string ReactiveSequence = "aibt.core.reactive-sequence";
        private const string MemorySequence = "aibt.core.memory-sequence";
        private const int Updates = 12;

        [Test]
        public void AMultiTickWaitUnderAV1ReactiveSequenceNeverSucceeds()
        {
            var reactive = Start(WaitTree(ReactiveSequence));
            for (var update = 1; update <= Updates; update++)
                Assert.That(reactive.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running),
                    "Update " + update + ": the wait is re-entered with Elapsed = 0 on every update.");

            var memory = Start(WaitTree(MemorySequence));
            Assert.That(memory.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(memory.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(memory.Tick(1f), Is.EqualTo(ManagedTickOutcome.Completed), "Control: wait(3) succeeds on its third tick.");
            Assert.That(memory.LastResult, Is.EqualTo(NodeStatus.Success));
        }

        [Test]
        public void ATimeoutOverALongChildUnderAV1ReactiveSequenceNeverFires()
        {
            var reactive = Start(TimeoutTree(ReactiveSequence));
            for (var update = 1; update <= Updates; update++)
                Assert.That(reactive.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running),
                    "Update " + update + " (" + update + " s): the timeout is re-entered and its deadline moves forward.");

            var memory = Start(TimeoutTree(MemorySequence));
            Assert.That(memory.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running), "1 s: deadline 4 s.");
            Assert.That(memory.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running), "2 s.");
            Assert.That(memory.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running), "3 s.");
            Assert.That(memory.Tick(1f), Is.EqualTo(ManagedTickOutcome.Completed), "Control: 4 s reaches the deadline.");
            Assert.That(memory.LastResult, Is.EqualTo(NodeStatus.Failure), "The timeout's configured terminal result.");
        }

        [Test]
        public void AnOnEnterCooldownOverALongChildUnderAV1ReactiveSequenceBlocksOnTheSecondUpdate()
        {
            var reactive = Start(CooldownTree(ReactiveSequence));
            Assert.That(reactive.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running), "First update: the cooldown records its deadline and enters the child.");
            Assert.That(reactive.Tick(1f), Is.EqualTo(ManagedTickOutcome.Completed),
                "Second update: the re-entered cooldown is still inside its own cooldown and returns its blocked result.");
            Assert.That(reactive.LastResult, Is.EqualTo(NodeStatus.Failure));

            var memory = Start(CooldownTree(MemorySequence));
            for (var update = 1; update <= Updates; update++)
                Assert.That(memory.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running), "Control: the cooldown is entered once and the child keeps running.");
        }

        private static string WaitTree(string composite)
            => Tree("root", null,
                Node("root", composite, null, "ok", "wait"),
                Node("ok", "aibt.unity.succeed", null),
                Node("wait", "aibt.stdlib.wait", "\"ticks\": 3"));

        private static string TimeoutTree(string composite)
            => Tree("root", null,
                Node("root", composite, null, "ok", "limit"),
                Node("ok", "aibt.unity.succeed", null),
                Node("limit", "aibt.core.timeout", "\"durationMicroseconds\": 3000000, \"terminalResult\": \"failure\"", "wait"),
                Node("wait", "aibt.stdlib.wait", "\"ticks\": 1000"));

        private static string CooldownTree(string composite)
            => Tree("root", null,
                Node("root", composite, null, "ok", "gate"),
                Node("ok", "aibt.unity.succeed", null),
                Node("gate", "aibt.core.cooldown",
                    "\"durationMicroseconds\": 10000000, \"blockedResult\": \"failure\", \"startPolicy\": \"on-enter\"", "wait"),
                Node("wait", "aibt.stdlib.wait", "\"ticks\": 1000"));

        private static ManagedTreeInstance Start(string json)
        {
            Assert.That(ManagedTreeProgram.TryCompile(json, "trees/test.aibt", StandardNodeKit.Nodes, out var program, out var diagnostics), Is.True,
                ManagedTreeProgram.Describe(diagnostics));
            return new ManagedTreeInstance(program, null);
        }
    }
}
