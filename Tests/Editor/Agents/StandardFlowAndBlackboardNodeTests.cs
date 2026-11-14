using AIBT.Agents;
using NUnit.Framework;
using UnityEngine;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;
using static AIBT.Tests.Editor.Agents.StandardNodeKit;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P9-002: the standard wait, blackboard and time-gate nodes, run headless on the managed
    /// executor. Each test pins the node's Running/Success/Failure contract.
    /// </summary>
    public sealed class StandardFlowAndBlackboardNodeTests
    {
        // ---- waits ---------------------------------------------------------------------------

        [Test]
        public void WaitRandomRunsForADurationInsideItsRange()
        {
            var instance = Headless(Tree("wait", null, Node("wait", "aibt.unity.wait-random", "\"min-seconds\": 0.5, \"max-seconds\": 0.5")));
            Assert.That(instance.Tick(0.3f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(instance.Tick(0.3f), Is.EqualTo(ManagedTickOutcome.Completed));
            Assert.That(instance.LastResult, Is.EqualTo(NodeStatus.Success));

            Random.InitState(7);
            var ranged = Headless(Tree("wait", null, Node("wait", "aibt.unity.wait-random", "\"min-seconds\": 1.0, \"max-seconds\": 2.0")));
            for (var run = 0; run < 20; run++)
            {
                var ticks = 0;
                if (run > 0) Assert.That(ranged.Restart(false), Is.True);
                while (ranged.Tick(0.01f) == ManagedTickOutcome.Running) ticks++;
                Assert.That(ticks + 1, Is.InRange(100, 201), "Each run lasts between min and max seconds.");
            }
        }

        [Test]
        public void WaitUntilRunsUntilTheFlagHasTheExpectedValue()
        {
            var instance = Headless(Tree("wait", Bool("door-open"), Node("wait", "aibt.unity.wait-until", "\"key\": \"door-open\"")));
            Assert.That(instance.Tick(0.1f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(instance.Tick(5f), Is.EqualTo(ManagedTickOutcome.Running), "Without a timeout it waits indefinitely.");
            instance.Blackboard.Set("door-open", true);
            Assert.That(instance.Tick(0.1f), Is.EqualTo(ManagedTickOutcome.Completed));
            Assert.That(instance.LastResult, Is.EqualTo(NodeStatus.Success));
        }

        [Test]
        public void WaitUntilFailsWhenItsTimeoutPasses()
        {
            var instance = Headless(Tree("wait", Bool("flag", true),
                Node("wait", "aibt.unity.wait-until", "\"key\": \"flag\", \"expected\": false, \"timeout-seconds\": 0.5")));
            Assert.That(instance.Tick(0.3f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(RunToEnd(instance, 0.3f), Is.EqualTo(NodeStatus.Failure));
        }

        [Test]
        public void WaitFramesRunsForExactlyThatManyTicksAfterItStarts()
        {
            var instance = Headless(Tree("wait", null, Node("wait", "aibt.unity.wait-frames", "\"frames\": 2")));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Completed));

            var none = Headless(Tree("wait", null, Node("wait", "aibt.unity.wait-frames", "\"frames\": 0")));
            Assert.That(none.Tick(0f), Is.EqualTo(ManagedTickOutcome.Completed));
        }

        // ---- set / copy / increment / toggle ---------------------------------------------------

        [Test]
        public void SetVector3WritesItsConstant()
        {
            var instance = Headless(Tree("set", Vec3("home"), Node("set", "aibt.unity.set-vector3", "\"key\": \"home\", \"x\": 1.5, \"y\": 2, \"z\": -3")));
            Assert.That(RunToEnd(instance), Is.EqualTo(NodeStatus.Success));
            Assert.That(instance.Blackboard.Get<Vector3>("home"), Is.EqualTo(new Vector3(1.5f, 2f, -3f)));
        }

        [Test]
        public void CopyNodesCopyEachSupportedType()
        {
            var instance = Headless(Tree("copy",
                Keys(Bool("b1", true), Bool("b2"), Int("i1", 7), Int("i2"), Float("f1", 2.5f), Float("f2"), Vec3("v1", new Vector3(1, 2, 3)), Vec3("v2")),
                Node("copy", "aibt.core.memory-sequence", null, "cb", "ci", "cf", "cv"),
                Node("cb", "aibt.unity.copy-bool", "\"from\": \"b1\", \"to\": \"b2\""),
                Node("ci", "aibt.unity.copy-int", "\"from\": \"i1\", \"to\": \"i2\""),
                Node("cf", "aibt.unity.copy-float", "\"from\": \"f1\", \"to\": \"f2\""),
                Node("cv", "aibt.unity.copy-vector3", "\"from\": \"v1\", \"to\": \"v2\"")));
            Assert.That(RunToEnd(instance), Is.EqualTo(NodeStatus.Success));
            Assert.That(instance.Blackboard.Get<bool>("b2"), Is.True);
            Assert.That(instance.Blackboard.Get<int>("i2"), Is.EqualTo(7));
            Assert.That(instance.Blackboard.Get<float>("f2"), Is.EqualTo(2.5f));
            Assert.That(instance.Blackboard.Get<Vector3>("v2"), Is.EqualTo(new Vector3(1, 2, 3)));
        }

        [Test]
        public void IncrementNodesAddTheirAmountAndDefaultToOne()
        {
            var instance = Headless(Tree("steps", Keys(Int("count", 3), Float("heat", 1f)),
                Node("steps", "aibt.core.memory-sequence", null, "up", "down", "warm"),
                Node("up", "aibt.unity.increment-int", "\"key\": \"count\""),
                Node("down", "aibt.unity.increment-int", "\"key\": \"count\", \"amount\": -5"),
                Node("warm", "aibt.unity.increment-float", "\"key\": \"heat\", \"amount\": 0.5")));
            Assert.That(RunToEnd(instance), Is.EqualTo(NodeStatus.Success));
            Assert.That(instance.Blackboard.Get<int>("count"), Is.EqualTo(-1));
            Assert.That(instance.Blackboard.Get<float>("heat"), Is.EqualTo(1.5f));
        }

        [Test]
        public void ToggleBoolInvertsTheValue()
        {
            var instance = Headless(Tree("toggle", Bool("lights"), Node("toggle", "aibt.unity.toggle-bool", "\"key\": \"lights\"")));
            RunToEnd(instance);
            Assert.That(instance.Blackboard.Get<bool>("lights"), Is.True);
            RunAgain(instance);
            Assert.That(instance.Blackboard.Get<bool>("lights"), Is.False);
        }

        // ---- comparisons ---------------------------------------------------------------------

        [Test]
        public void CompareKeyNodesCompareTwoLiveValues()
        {
            var ints = Headless(Tree("cmp", Keys(Int("ammo", 3), Int("needed", 5)),
                Node("cmp", "aibt.unity.compare-int-keys", "\"left\": \"ammo\", \"comparison\": \"greater-or-equal\", \"right\": \"needed\"")));
            Assert.That(RunToEnd(ints), Is.EqualTo(NodeStatus.Failure));
            ints.Blackboard.Set("ammo", 5);
            Assert.That(RunAgain(ints), Is.EqualTo(NodeStatus.Success));

            var floats = Headless(Tree("cmp", Keys(Float("health", 20f), Float("flee", 25f)),
                Node("cmp", "aibt.unity.compare-float-keys", "\"left\": \"health\", \"right\": \"flee\"")));
            Assert.That(RunToEnd(floats), Is.EqualTo(NodeStatus.Success), "The default comparison is less-than.");
            floats.Blackboard.Set("health", 30f);
            Assert.That(RunAgain(floats), Is.EqualTo(NodeStatus.Failure));
        }

        [Test]
        public void IsVector3SetFailsForAnUnwrittenKeyAndSucceedsOnceItIsFilledIn()
        {
            var instance = Headless(Tree("is-set", Vec3("last-seen"), Node("is-set", "aibt.unity.is-vector3-set", "\"key\": \"last-seen\"")));
            Assert.That(RunToEnd(instance), Is.EqualTo(NodeStatus.Failure));
            instance.Blackboard.Set("last-seen", new Vector3(0f, 0f, 0.01f));
            Assert.That(RunAgain(instance), Is.EqualTo(NodeStatus.Success));
        }

        // ---- random values -------------------------------------------------------------------

        [Test]
        public void RandomFloatAndRandomIntStayInsideTheirBoundsAndRandomIntIncludesMax()
        {
            Random.InitState(11);
            var floats = Headless(Tree("roll", Float("value", -1f), Node("roll", "aibt.unity.random-float", "\"key\": \"value\", \"min\": 2, \"max\": 3")));
            var ints = Headless(Tree("roll", Int("value"), Node("roll", "aibt.unity.random-int", "\"key\": \"value\", \"min\": 1, \"max\": 2")));
            var seenOne = false;
            var seenTwo = false;
            for (var run = 0; run < 200; run++)
            {
                if (run == 0) RunToEnd(floats); else RunAgain(floats);
                Assert.That(floats.Blackboard.Get<float>("value"), Is.InRange(2f, 3f));
                if (run == 0) RunToEnd(ints); else RunAgain(ints);
                var rolled = ints.Blackboard.Get<int>("value");
                Assert.That(rolled, Is.InRange(1, 2));
                seenOne |= rolled == 1;
                seenTwo |= rolled == 2;
            }

            Assert.That(seenOne && seenTwo, Is.True, "Both bounds of random-int are reachable.");
        }

        [Test]
        public void RandomPointLandsInsideTheRingAroundTheCenterKeyOnItsPlane()
        {
            Random.InitState(3);
            var center = new Vector3(10f, 2f, -4f);
            var instance = Headless(Tree("pick", Keys(Vec3("center", center), Vec3("point")),
                Node("pick", "aibt.unity.random-point", "\"result\": \"point\", \"center\": \"center\", \"radius\": 5, \"min-radius\": 2")));
            for (var run = 0; run < 100; run++)
            {
                if (run == 0) RunToEnd(instance); else RunAgain(instance);
                var point = instance.Blackboard.Get<Vector3>("point");
                Assert.That(point.y, Is.EqualTo(center.y), "flat keeps the center's height");
                Assert.That(Vector3.Distance(point, center), Is.InRange(1.999f, 5.001f));
            }
        }

        [Test]
        public void DistanceWritesTheDistanceBetweenTwoKeys()
        {
            var instance = Headless(Tree("measure", Keys(Vec3("a", new Vector3(0, 0, 0)), Vec3("b", new Vector3(3, 10, 4)), Float("d")),
                Node("measure", "aibt.core.memory-sequence", null, "full", "flat"),
                Node("full", "aibt.unity.distance", "\"from\": \"a\", \"to\": \"b\", \"result\": \"d\""),
                Node("flat", "aibt.unity.distance", "\"from\": \"a\", \"to\": \"b\", \"result\": \"d\", \"flat\": true")));
            Assert.That(RunToEnd(instance), Is.EqualTo(NodeStatus.Success));
            Assert.That(instance.Blackboard.Get<float>("d"), Is.EqualTo(5f).Within(0.0001f), "flat ignores the height difference");
        }

        // ---- time gates ----------------------------------------------------------------------

        [Test]
        public void ACooldownStartedByOneNodeGatesAnotherUntilItsTimePasses()
        {
            var instance = Headless(Tree("ability", Float("attack-ready-at"),
                Node("ability", "aibt.core.memory-sequence", null, "ready", "start"),
                Node("ready", "aibt.unity.is-cooldown-ready", "\"key\": \"attack-ready-at\""),
                Node("start", "aibt.unity.start-cooldown", "\"key\": \"attack-ready-at\", \"seconds\": 1")));
            Assert.That(RunToEnd(instance, 0.1f), Is.EqualTo(NodeStatus.Success), "A cooldown that never started is ready.");
            Assert.That(RunAgain(instance, 0.5f), Is.EqualTo(NodeStatus.Failure), "0.5 s after starting a 1 s cooldown.");
            Assert.That(RunAgain(instance, 0.6f), Is.EqualTo(NodeStatus.Success), "1.1 s after starting it.");
        }

        [Test]
        public void TimeSinceChangedCountsFromTheLastWriteThatChangedTheValue()
        {
            var instance = Headless(Tree("lost", Int("target-id"),
                Node("lost", "aibt.unity.time-since-changed", "\"key\": \"target-id\", \"seconds\": 1")));
            Assert.That(RunToEnd(instance, 0.6f), Is.EqualTo(NodeStatus.Failure));
            Assert.That(RunAgain(instance, 0.6f), Is.EqualTo(NodeStatus.Success), "1.2 s without a change.");

            instance.Blackboard.Set("target-id", 4);
            Assert.That(RunAgain(instance, 0.6f), Is.EqualTo(NodeStatus.Failure), "A changed value restarts the timer.");
            instance.Blackboard.Set("target-id", 4);
            Assert.That(RunAgain(instance, 0.6f), Is.EqualTo(NodeStatus.Success), "Writing the same value is not a change.");

            var recent = Headless(Tree("recent", Bool("alarm"),
                Node("recent", "aibt.unity.time-since-changed", "\"key\": \"alarm\", \"comparison\": \"less-than\", \"seconds\": 0.5")));
            RunToEnd(recent, 1f);
            recent.Blackboard.Set("alarm", true);
            Assert.That(RunAgain(recent, 0.2f), Is.EqualTo(NodeStatus.Success), "Raised 0.2 s ago.");
        }

        [Test]
        public void RandomChanceRespectsItsBounds()
        {
            var never = Headless(Tree("roll", null, Node("roll", "aibt.unity.random-chance", "\"chance\": 0")));
            var always = Headless(Tree("roll", null, Node("roll", "aibt.unity.random-chance", "\"chance\": 1")));
            for (var run = 0; run < 50; run++)
            {
                Assert.That(run == 0 ? RunToEnd(never) : RunAgain(never), Is.EqualTo(NodeStatus.Failure));
                Assert.That(run == 0 ? RunToEnd(always) : RunAgain(always), Is.EqualTo(NodeStatus.Success));
            }

            Random.InitState(5);
            var half = Headless(Tree("roll", null, Node("roll", "aibt.unity.random-chance", null)));
            var successes = 0;
            for (var run = 0; run < 400; run++)
                successes += (run == 0 ? RunToEnd(half) : RunAgain(half)) == NodeStatus.Success ? 1 : 0;
            Assert.That(successes, Is.InRange(140, 260), "The default chance is one half.");
        }
    }
}
