using AIBT.Agents;
using NUnit.Framework;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P10-013 stage 1 (ADR-P10-013): the same subtrees that <see cref="ReactiveV1RestartPinTests"/>
    /// pins as never finishing under a v1 reactive sequence do finish under the retaining reactive
    /// sequence, end to end through the managed agent path (authoring JSON, reference compiler,
    /// <see cref="ManagedTreeInstance"/>) with the real standard leaves and core decorators.
    /// </summary>
    public sealed class RetainingReactiveManagedTests
    {
        private const string Retaining = "aibt.core.reactive-sequence-retaining";

        [Test]
        public void AMultiTickWaitUnderARetainingSequenceSucceedsOnItsThirdTick()
        {
            var instance = Start(Tree("root", null,
                Node("root", Retaining, null, "ok", "wait"),
                Node("ok", "aibt.unity.succeed", null),
                Node("wait", "aibt.stdlib.wait", "\"ticks\": 3")));

            Assert.That(instance.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(instance.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running), "The guard passes and the wait is resumed, not re-entered.");
            Assert.That(instance.Tick(1f), Is.EqualTo(ManagedTickOutcome.Completed));
            Assert.That(instance.LastResult, Is.EqualTo(NodeStatus.Success));
        }

        [Test]
        public void ATimeoutOverALongChildUnderARetainingSequenceFiresAtItsDeadline()
        {
            var instance = Start(Tree("root", null,
                Node("root", Retaining, null, "ok", "limit"),
                Node("ok", "aibt.unity.succeed", null),
                Node("limit", "aibt.core.timeout", "\"durationMicroseconds\": 3000000, \"terminalResult\": \"failure\"", "wait"),
                Node("wait", "aibt.stdlib.wait", "\"ticks\": 1000")));

            Assert.That(instance.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running), "1 s: deadline 4 s.");
            Assert.That(instance.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running), "2 s.");
            Assert.That(instance.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running), "3 s.");
            Assert.That(instance.Tick(1f), Is.EqualTo(ManagedTickOutcome.Completed), "4 s: the kept deadline is reached, as under a memory sequence.");
            Assert.That(instance.LastResult, Is.EqualTo(NodeStatus.Failure));
        }

        [Test]
        public void AnOnEnterCooldownOverALongChildUnderARetainingSequenceIsEnteredOnce()
        {
            var instance = Start(Tree("root", null,
                Node("root", Retaining, null, "ok", "gate"),
                Node("ok", "aibt.unity.succeed", null),
                Node("gate", "aibt.core.cooldown",
                    "\"durationMicroseconds\": 10000000, \"blockedResult\": \"failure\", \"startPolicy\": \"on-enter\"", "wait"),
                Node("wait", "aibt.stdlib.wait", "\"ticks\": 1000")));

            for (var update = 1; update <= 12; update++)
                Assert.That(instance.Tick(1f), Is.EqualTo(ManagedTickOutcome.Running), "Update " + update + ": the cooldown is not re-entered.");
        }

        private static ManagedTreeInstance Start(string json)
        {
            Assert.That(ManagedTreeProgram.TryCompile(json, "trees/test.aibt", StandardNodeKit.Nodes, out var program, out var diagnostics), Is.True,
                ManagedTreeProgram.Describe(diagnostics));
            return new ManagedTreeInstance(program, null);
        }
    }
}
