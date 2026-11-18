using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static AIBT.Tests.Runtime.RetainingTree;

namespace AIBT.Tests.Runtime
{
    /// <summary>
    /// P10-014, review finding D3: when a Parallel guard of a retaining composite takes over, the
    /// last step of the retained branch's abort leaves the rest of the Parallel's visit pending
    /// (RetainVisitRestPending). An abort requested in exactly that window (a tree stop by the
    /// host after a budget yield, or an observer reset) replaces the rest of the visit; the
    /// pending flag must not survive it into the next update.
    /// </summary>
    internal sealed class RetainingVisitRestAbortTests
    {
        private const NodeStatus S = NodeStatus.Success;
        private const NodeStatus F = NodeStatus.Failure;
        private const NodeStatus R = NodeStatus.Running;

        private static RetainingNativeTwin ParallelGuardTakeover(RetainingNativeDrive drive)
            => new RetainingNativeTwin(Create(
                RetainingSelector(
                    Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a", F, R), Leaf("b", R)),
                    Leaf("patrol")),
                "sel", "par", "a", "b", "patrol"), drive);

        private static List<string> RunUntilRetainedAbortEnds(RetainingNativeTwin native, ulong update)
        {
            native.Begin(update);
            var lines = new List<string>();
            for (var guard = 0; !lines.Contains("X(Ab) patrol"); guard++)
            {
                Assert.That(guard, Is.LessThan(100), "Never reached the end of the retained abort.");
                native.Step(lines);
            }
            Assert.That(native.Control[0].RetainVisitRestPending, Is.EqualTo(1),
                "The window: the takeover has completed and the rest of the guard's visit is pending.");
            return lines;
        }

        [TestCase(RetainingNativeDrive.Immediate)]
        [TestCase(RetainingNativeDrive.OneStepBudget)]
        public void ATreeStopInTheWindowClearsTheRestOfTheVisitAndTheMachineRunsAgain(RetainingNativeDrive drive)
        {
            using var native = ParallelGuardTakeover(drive);
            Assert.That(native.Update(1), Does.Contain("T patrol R"));
            var lines = RunUntilRetainedAbortEnds(native, 2);
            var reached = lines.Count;
            Assert.That(native.Machine.TryRequestAbort(Burst.BurstNodeAbortReason.TreeStopped, out var failure),
                Is.True, failure.Code.ToString());
            Assert.That(native.RunToRest(lines), Is.EqualTo(NativeLifecycleStepKindV1.Completed));
            RetainingAssert.Records(lines.Skip(reached).ToArray(),
                "A b TreeStopped@sel", "X(Ab) b", "A a TreeStopped@sel", "X(Ab) a",
                "A par TreeStopped@sel", "X(Ab) par", "A sel TreeStopped@sel", "X(Ab) sel");
            Assert.That(native.Control[0].RetainVisitRestPending, Is.Zero, "The stop replaced the rest of the visit.");
            Assert.That(native.Control[0].Depth, Is.Zero);
            Assert.That(native.Control[0].SuspendedFrameStart, Is.EqualTo((uint)native.Frames.Length), "No frame is left suspended.");

            // The host reuses the machine without TryResetInstance: the next update enters the root.
            var again = native.Update(3, out var outcome);
            Assert.That(again.First(), Is.EqualTo("E sel"));
            Assert.That(outcome.End, Is.EqualTo(NativeLifecycleStepKindV1.Waiting));
        }

        [Test]
        public void AnObserverResetInTheWindowClearsTheRestOfTheVisit()
        {
            // The observer's owner is a v1 reactive guard of the retaining selector whose Parallel
            // takes over (a v1 reactive owner above the retaining composite would reset it at every
            // update start, before any guard pass; observers under a retaining composite's own
            // children are allowed only through such a v1 owner).
            using var native = new RetainingNativeTwin(Create(
                RetainingSelector(
                    ReactiveSequence(Leaf("cond", S, S, S),
                        Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a", F, R, R), Leaf("b", R))),
                    Leaf("patrol")),
                "sel", "v1", "cond", "par", "a", "b", "patrol"));
            Assert.That(native.Update(1), Does.Contain("T patrol R"));
            var lines = RunUntilRetainedAbortEnds(native, 2);
            var reached = lines.Count;
            Assert.That(native.Machine.TryApplyObserverTransition(
                new NativeObserverTransitionV1(2, 1, CompiledObserverMode.Self, Burst.BurstNodeAbortReason.ObserverSelf),
                out var failure), Is.True, failure.Code.ToString());
            Assert.That(native.RunToRest(lines), Is.EqualTo(NativeLifecycleStepKindV1.Waiting));
            RetainingAssert.Records(lines.Skip(reached).ToArray(),
                "A b ObserverSelf@cond", "X(Ab) b", "A a ObserverSelf@cond", "X(Ab) a",
                "A par ObserverSelf@cond", "X(Ab) par",
                "E cond", "T cond S", "X cond", "E par", "E a", "T a R", "E b", "T b R");
            Assert.That(native.Control[0].RetainVisitRestPending, Is.Zero);
            Assert.That(native.Control[0].Faulted, Is.Zero);
        }
    }
}
