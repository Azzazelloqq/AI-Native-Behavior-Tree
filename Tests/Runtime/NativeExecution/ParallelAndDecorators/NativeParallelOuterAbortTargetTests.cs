using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static AIBT.Tests.Runtime.RetainingTree;

namespace AIBT.Tests.Runtime
{
    /// <summary>
    /// P10-014, review finding D1: an abort whose target lies above a Parallel with running
    /// branches (a Timeout at its deadline, a v1 reactive reset, an observer reset, a retaining
    /// composite's takeover or end, an outer Parallel's terminal decision, a tree stop) must reach
    /// its own target after the Parallel has aborted its suspended branches, exactly as the
    /// reference executor does with its per-Parallel abort resume state. Native runs at the
    /// production frame capacity (one frame per node) and its records, root results and progress
    /// must equal the reference's.
    /// </summary>
    internal sealed class NativeParallelOuterAbortTargetTests
    {
        private const NodeStatus S = NodeStatus.Success;
        private const NodeStatus F = NodeStatus.Failure;
        private const NodeStatus R = NodeStatus.Running;

        internal sealed class Case
        {
            internal Case(string name, Func<RetainingTree> build, int updates, long[] times = null)
            {
                Name = name;
                Build = build;
                Updates = updates;
                Times = times ?? Array.Empty<long>();
            }

            internal string Name { get; }
            internal Func<RetainingTree> Build { get; }
            internal int Updates { get; }
            internal long[] Times { get; }
            internal long Time(int update) => Times.Length == 0 ? -1 : Times[update - 1];
            public override string ToString() => Name;
        }

        internal static RetainingTree TimeoutOverParallel() => Create(
            Timeout(3_000_000, F, Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a"), Leaf("b"))),
            "timeout", "par", "a", "b");

        internal static RetainingTree V1ReactiveOverParallel() => Create(
            ReactiveSequence(Leaf("cond", S, S, S, S),
                Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a"), Leaf("b"))),
            "v1", "cond", "par", "a", "b");

        internal static RetainingTree NestedParallelTerminal() => Create(
            Parallel(ReferenceParallelPolicy.RequireAnySuccess,
                Leaf("ok", R, S),
                Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a"), Leaf("b"))),
            "outer", "ok", "inner", "a", "b");

        private static IEnumerable<Case> Cases()
        {
            yield return new Case("TimeoutOverParallel_AtDeadline", TimeoutOverParallel, 3,
                new long[] { 0, 1_000_000, 3_000_000 });
            yield return new Case("TimeoutOverDeepParallel_AtDeadline", () => Create(
                Timeout(3_000_000, S, MemorySequence(Parallel(ReferenceParallelPolicy.RequireAllSuccess,
                    MemorySequence(Leaf("a")), Leaf("b")))),
                "timeout", "seq", "par", "aSeq", "a", "b"), 3, new long[] { 0, 1_000_000, 3_000_000 });
            yield return new Case("V1ReactiveSequenceOverParallel_SecondUpdate", V1ReactiveOverParallel, 3);
            yield return new Case("V1ReactiveSelectorOverParallel_SecondUpdate", () => Create(
                ReactiveSelector(Leaf("cond", F, F, F),
                    Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a"), Leaf("b"))),
                "v1", "cond", "par", "a", "b"), 3);
            yield return new Case("NestedParallel_OuterTerminalAbortsInner", NestedParallelTerminal, 3);
            yield return new Case("TimeoutOverParallelInsideRetainedBranch", () => Create(
                RetainingSequence(Leaf("ok", S, S, S),
                    Timeout(1_500_000, F, Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a"), Leaf("b")))),
                "seq", "ok", "timeout", "par", "a", "b"), 3, new long[] { 0, 1_000_000, 2_000_000 });
            yield return new Case("V1ReactiveOverParallelInsideRetainedBranch", () => Create(
                RetainingSequence(Leaf("ok", S, S, S),
                    ReactiveSequence(Leaf("cond", S, S, S),
                        Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a"), Leaf("b")))),
                "seq", "ok", "v1", "cond", "par", "a", "b"), 3);
            yield return new Case("RetainingTakeoverAboveParallel", () => Create(
                RetainingSelector(MemorySequence(Leaf("alarm", F, S), Leaf("run")),
                    Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a"), Leaf("b"))),
                "sel", "flee", "alarm", "run", "par", "a", "b"), 3);
            yield return new Case("TimeoutAboveRetainingTakeoverAboveParallel", () => Create(
                Timeout(5_000_000, F, RetainingSelector(MemorySequence(Leaf("alarm", F, S), Leaf("run")),
                    Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a"), Leaf("b")))),
                "timeout", "sel", "flee", "alarm", "run", "par", "a", "b"), 3,
                new long[] { 0, 1_000_000, 6_000_000 });
        }

        [TestCaseSource(nameof(Cases))]
        public void NativeMatchesTheReferenceRecordForRecord(Case scenario)
        {
            foreach (RetainingNativeDrive drive in Enum.GetValues(typeof(RetainingNativeDrive)))
            {
                var reference = scenario.Build();
                using var native = new RetainingNativeTwin(scenario.Build(), drive);
                for (var update = 1; update <= scenario.Updates; update++)
                {
                    var expected = reference.Update((ulong)update, out var expectedResult, scenario.Time(update));
                    var actual = native.Update((ulong)update, out var actualResult, scenario.Time(update));
                    var label = drive + ", update " + update;
                    Assert.That(actual, Is.EqualTo(expected),
                        label + "\nReference: " + Format(expected) + "\n   Native: " + Format(actual));
                    var completed = expectedResult.Progress == ReferenceExecutionProgress.Completed;
                    Assert.That(actualResult.End,
                        Is.EqualTo(completed ? NativeLifecycleStepKindV1.Completed : NativeLifecycleStepKindV1.Waiting),
                        label + ": progress.");
                    Assert.That(actualResult.Root, Is.EqualTo(expectedResult.RootResult), label + ": root result.");
                    if (completed) break;
                }
                AssertNoLeakedFrames(native, drive.ToString());
                AssertNoSavedOuterAbort(native);
            }
        }

        // ---- Tree stop over the same shapes ------------------------------------------------------

        private static IEnumerable<TestCaseData> Stops()
        {
            // Stop at the start of the second update with no abort pending: equal to the reference.
            yield return new TestCaseData("TimeoutOverParallel", 1_000_000L, null).SetName("Stop_TimeoutOverParallel_UpdateStart");
            yield return new TestCaseData("NestedParallelTerminal", -1L, null).SetName("Stop_NestedParallel_UpdateStart");
            yield return new TestCaseData("TimeoutOverParallelInsideRetainedBranch", 1_000_000L, null).SetName("Stop_RetainedTimeoutOverParallel_UpdateStart");
        }

        /// <summary>
        /// A tree stop that replaces an abort already pending or in progress (the production
        /// teardown, <c>replacePendingForTreeStop</c>) continues to the root and the tree ends
        /// without a result, as native did before P10-014. Not compared with the reference: its
        /// RequestAbort in this situation resumes the interrupted timeout or reset when the
        /// Parallel's branch aborts finish, so the stop is lost (recorded in the P10-014 evidence
        /// as an open question for the owner; the reference is not changed).
        /// </summary>
        private static IEnumerable<TestCaseData> StopsReplacingAPendingAbort()
        {
            yield return new TestCaseData("V1ReactiveOverParallel", -1L, null, new[]
            {
                "A b TreeStopped@v1", "X(Ab) b", "A a TreeStopped@v1", "X(Ab) a",
                "A par TreeStopped@v1", "X(Ab) par", "A v1 TreeStopped@v1", "X(Ab) v1",
            }).SetName("StopWins_V1ReactiveOverParallel_ResetPendingAtUpdateStart");
            yield return new TestCaseData("TimeoutOverParallel", 3_000_000L, "X(Ab) b", new[]
            {
                "A a TreeStopped@timeout", "X(Ab) a", "A par TreeStopped@timeout", "X(Ab) par",
                "A timeout TreeStopped@timeout", "X(Ab) timeout",
            }).SetName("StopWins_TimeoutOverParallel_MidTimeoutAbort");
            yield return new TestCaseData("V1ReactiveOverParallel", -1L, "X(Ab) b", new[]
            {
                "A a TreeStopped@v1", "X(Ab) a", "A par TreeStopped@v1", "X(Ab) par",
                "A v1 TreeStopped@v1", "X(Ab) v1",
            }).SetName("StopWins_V1ReactiveOverParallel_MidResetAbort");
            yield return new TestCaseData("NestedParallelTerminal", -1L, "X(Ab) b", new[]
            {
                "A a TreeStopped@outer", "X(Ab) a", "A inner TreeStopped@outer", "X(Ab) inner",
                "A outer TreeStopped@outer", "X(Ab) outer",
            }).SetName("StopWins_NestedParallel_MidTerminalAbort");
        }

        private static Func<RetainingTree> Tree(string tree)
            => tree == "TimeoutOverParallel" ? TimeoutOverParallel
                : tree == "V1ReactiveOverParallel" ? V1ReactiveOverParallel
                : tree == "NestedParallelTerminal" ? (Func<RetainingTree>)NestedParallelTerminal
                : () => Create(
                    RetainingSequence(Leaf("ok", S, S, S),
                        Timeout(1_500_000, F, Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a"), Leaf("b")))),
                    "seq", "ok", "timeout", "par", "a", "b");

        [TestCaseSource(nameof(StopsReplacingAPendingAbort))]
        public void ATreeStopReplacingAPendingAbortReachesTheRoot(string tree, long time, string stopAfter, string[] expected)
        {
            {
                using var native = new RetainingNativeTwin(Tree(tree)());
                native.Update(1, 0);
                native.Begin(2, time);
                var lines = new List<string>();
                for (var guard = 0; stopAfter != null && !lines.Contains(stopAfter); guard++)
                {
                    Assert.That(guard, Is.LessThan(100), "Never reached '" + stopAfter + "'.");
                    native.Step(lines);
                }
                var reached = lines.Count;
                Assert.That(native.Machine.TryRequestAbort(Burst.BurstNodeAbortReason.TreeStopped, out var failure,
                    replacePendingForTreeStop: true), Is.True, failure.Code.ToString());
                Assert.That(native.RunToRest(lines), Is.EqualTo(NativeLifecycleStepKindV1.Completed));
                RetainingAssert.Records(lines.Skip(reached).ToArray(), expected);
                Assert.That(native.Control[0].HasRootStatus, Is.Zero, "A stopped tree has no root result.");
                AssertNoLeakedFrames(native, tree);
                AssertNoSavedOuterAbort(native);
            }
        }

        [TestCaseSource(nameof(Stops))]
        public void ATreeStopOverTheSameShapesMatchesTheReference(string tree, long time, string stopAfter)
        {
            var build = Tree(tree);
            using var native = new RetainingNativeTwin(build());
            native.Update(1, 0);
            native.Begin(2, time);
            var lines = new List<string>();
            for (var guard = 0; stopAfter != null && !lines.Contains(stopAfter); guard++)
            {
                Assert.That(guard, Is.LessThan(100), "Never reached '" + stopAfter + "'.");
                native.Step(lines);
            }
            var reached = lines.Count;
            Assert.That(native.Machine.TryRequestAbort(Burst.BurstNodeAbortReason.TreeStopped, out var failure,
                replacePendingForTreeStop: true), Is.True, failure.Code.ToString());
            Assert.That(native.RunToRest(lines), Is.EqualTo(NativeLifecycleStepKindV1.Completed));
            var actual = lines.Skip(reached).ToArray();

            var reference = build();
            reference.Update(1, 0);
            var before = reference.Fixture.Trace.Records.Count;
            reference.Machine.BeginUpdate(At(2, time));
            while (stopAfter != null && !reference.Render(before).Contains(stopAfter)) reference.Machine.AdvanceOneStep();
            var referenceReached = reference.Render(before).Length;
            Assert.That(lines.Take(reached).ToArray(), Is.EqualTo(reference.Render(before).Take(referenceReached).ToArray()),
                "Records before the stop.");
            reference.Machine.RequestAbort(NodeAbortReason.TreeStopped, new RuntimeNodeIndex(0));
            ReferenceExecutionEnvelope result;
            do result = reference.Machine.AdvanceOneStep();
            while (result.Progress == ReferenceExecutionProgress.Suspended);
            RetainingAssert.Records(actual, reference.Render(before).Skip(referenceReached).ToArray());
            Assert.That(native.Control[0].Depth, Is.Zero);
            Assert.That(native.Control[0].HasRootStatus, Is.Zero, "A stopped tree has no root result.");
            AssertNoLeakedFrames(native, tree);
        }

        // ---- Observer reset over a Parallel ----------------------------------------------------

        [Test]
        public void AnObserverResetOfAV1ReactiveOwnerAboveAParallelAbortsBothBranchesAndRestartsTheOwner()
        {
            // The transition is applied directly, in the first update right after b's Running tick:
            // b is on the stack and a is suspended in the Parallel. The reset uses the same handler
            // as the v1 reactive reset: abort b, then the Parallel's suspended a, then the Parallel,
            // then reset the owner and continue the update from child 0.
            using var native = new RetainingNativeTwin(V1ReactiveOverParallel());
            native.Begin(1);
            var lines = new List<string>();
            for (var guard = 0; !lines.Contains("T b R"); guard++)
            {
                Assert.That(guard, Is.LessThan(100), "Never reached 'T b R'.");
                native.Step(lines);
            }
            var reached = lines.Count;
            Assert.That(native.Machine.TryApplyObserverTransition(
                new NativeObserverTransitionV1(1, 0, CompiledObserverMode.Self, Burst.BurstNodeAbortReason.ObserverSelf),
                out var failure), Is.True, failure.Code.ToString());
            Assert.That(native.RunToRest(lines), Is.EqualTo(NativeLifecycleStepKindV1.Waiting));
            var after = lines.Skip(reached).ToArray();
            RetainingAssert.Records(after.Take(6).ToArray(),
                "A b ObserverSelf@cond", "X(Ab) b", "A a ObserverSelf@cond", "X(Ab) a",
                "A par ObserverSelf@cond", "X(Ab) par");
            RetainingAssert.Records(after.Skip(6).ToArray(),
                "E cond", "T cond S", "X cond", "E par", "E a", "T a R", "E b", "T b R");
            AssertNoLeakedFrames(native, "observer");
        }

        private static void AssertNoSavedOuterAbort(RetainingNativeTwin native)
        {
            for (var index = 0; index < native.ParallelBranches.Length; index++)
                Assert.That(native.ParallelBranches[index].AbortResumeFlags, Is.Zero,
                    "Branch record " + index + " keeps no saved outer abort.");
        }

        private static void AssertNoLeakedFrames(RetainingNativeTwin native, string label)
        {
            var control = native.Control[0];
            var parked = native.Frames.Length - (int)control.SuspendedFrameStart;
            Assert.That(control.Faulted, Is.Zero, label + ": the machine is not faulted.");
            Assert.That(control.AbortRequested, Is.Zero, label + ": no abort is left pending.");
            Assert.That(control.ParallelAbortPending, Is.Zero, label + ": no Parallel abort is left pending.");
            if (control.Depth == 0)
                Assert.That(parked, Is.Zero, label + ": a finished tree holds no suspended frames.");
            Assert.That(control.Depth + (uint)parked, Is.LessThanOrEqualTo((uint)native.Frames.Length), label + ": frame accounting.");
        }
    }
}
