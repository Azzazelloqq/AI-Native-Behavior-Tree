using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static AIBT.Tests.Runtime.RetainingTree;

namespace AIBT.Tests.Runtime
{
    /// <summary>
    /// ADR-P10-013 stage 2: the native lifecycle machine runs every stage-1 reference scenario of
    /// the retaining reactive composites with the same lifecycle records (callbacks, order, abort
    /// reasons and sources), the same root results and the same semantic step counts, immediately,
    /// with a one-step budget and through the batched job owner.
    /// </summary>
    internal sealed class RetainingReactiveNativeEquivalenceTests
    {
        private const NodeStatus S = NodeStatus.Success;
        private const NodeStatus F = NodeStatus.Failure;
        private const NodeStatus R = NodeStatus.Running;

        internal sealed class Scenario
        {
            internal Scenario(string name, Func<RetainingTree> build, int updates, long[] times = null, bool hasParallel = false)
            {
                Name = name;
                Build = build;
                Updates = updates;
                Times = times ?? Array.Empty<long>();
                HasParallel = hasParallel;
            }

            internal string Name { get; }
            internal Func<RetainingTree> Build { get; }
            internal int Updates { get; }
            internal long[] Times { get; }
            internal bool HasParallel { get; }
            public override string ToString() => Name;
        }

        private static IEnumerable<Scenario> Scenarios()
        {
            yield return new Scenario("Example1_KeptJob", () => RetainingReactiveExampleTests.Example1Tree(new[] { F, F, F }, new[] { R, R, S }), 4);
            yield return new Scenario("Example2_Preemption", () => Create(
                MemorySequence(RetainingSelector(Leaf("alarm", F, S), Leaf("job", R, S)), Leaf("hold")),
                "crew", "top", "alarm", "job", "hold"), 3);
            yield return new Scenario("Example3_Wait", () => Create(
                RetainingSequence(Leaf("ok", S, S, S), Leaf("wait", R, R, S)), "seq", "ok", "wait"), 3);
            yield return new Scenario("Example3Alt_GuardFails", () => Create(
                RetainingSequence(Leaf("ok", S, S, F), Leaf("wait")), "seq", "ok", "wait"), 3);
            yield return new Scenario("Example4_Takeover", () => Create(
                RetainingSelector(MemorySequence(Leaf("alarm", F, F, S, F), Leaf("run")), Leaf("patrol")),
                "sel", "flee", "alarm", "run", "patrol"), 5);
            yield return new Scenario("Example5_Pitfall", () => Create(
                RetainingSequence(Leaf("cond", S, S, S, S), Leaf("stepA", R, S, R, S), Leaf("stepB", R, R)),
                "seq", "cond", "stepA", "stepB"), 4);
            yield return new Scenario("Example6_Timeout", () => Create(
                RetainingSequence(Leaf("ok", S, S, S, S), Timeout(3_000_000, F, Leaf("wait"))),
                "seq", "ok", "timeout", "wait"), 4, new long[] { 0, 1_000_000, 2_000_000, 3_000_000 });
            yield return new Scenario("TwoGuardsThenResume", () => Create(
                RetainingSequence(Leaf("g1", S, S, S), Leaf("g2", S, S, S), Leaf("work")),
                "seq", "g1", "g2", "work"), 3);
            yield return new Scenario("EmptyGuardPass", () => Create(
                RetainingSequence(Leaf("work", R, R, S)), "seq", "work"), 3);
            yield return new Scenario("Nested_InnerTakeoverOuterResume", () => Create(
                RetainingSequence(
                    Leaf("outerOk", S, S, S),
                    RetainingSelector(Leaf("alarm", F, F, S), Leaf("work")),
                    Leaf("after")),
                "outer", "outerOk", "inner", "alarm", "work", "after"), 3);
            yield return new Scenario("Nested_OuterGuardFailsDeepestFirst", () => Create(
                RetainingSequence(Leaf("outerOk", S, F), RetainingSelector(Leaf("alarm", F), Leaf("work"))),
                "outer", "outerOk", "inner", "alarm", "work"), 2);
            yield return new Scenario("Nested_DeepRetainedBranchTakeover", () => Create(
                RetainingSelector(
                    MemorySequence(Leaf("alarm", F, S), Leaf("run")),
                    MemorySequence(Leaf("prep", S), MemorySequence(Leaf("walk", R, R, R)))),
                "sel", "flee", "alarm", "run", "patrolSeq", "prep", "inner", "walk"), 3);
            yield return new Scenario("RetainingInsideParallel", () => Create(
                Parallel(ReferenceParallelPolicy.RequireAllSuccess,
                    RetainingSequence(Leaf("ok", S, S, F), Leaf("work")),
                    Leaf("other")),
                "par", "seq", "ok", "work", "other"), 3, hasParallel: true);
            yield return new Scenario("ParallelGuardTakesOver", () => Create(
                RetainingSelector(
                    Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a", F, R), Leaf("b", R)),
                    Leaf("patrol")),
                "sel", "par", "a", "b", "patrol"), 3, hasParallel: true);
            yield return new Scenario("ParallelInRetainedBranchAbortedByGuard", () => Create(
                RetainingSequence(
                    Leaf("ok", S, S, F),
                    Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a", R, R, R), Leaf("b", R, R, R))),
                "seq", "ok", "par", "a", "b"), 3, hasParallel: true);
            yield return new Scenario("V1InsideRetained", () => Create(
                RetainingSequence(Leaf("ok", S, S, S), ReactiveSequence(Leaf("cond", S, S, S), Leaf("work"))),
                "seq", "ok", "v1", "cond", "work"), 3);
            yield return new Scenario("V1AboveRetaining", () => Create(
                ReactiveSequence(Leaf("cond", S, S, S), RetainingSequence(Leaf("ok", S, S, S), Leaf("work"))),
                "v1", "cond", "seq", "ok", "work"), 3);
            yield return new Scenario("TimeoutAboveRetainingFiresFirst", () => Create(
                Timeout(1_500_000, F, RetainingSequence(Leaf("ok", S, S, S), Leaf("work"))),
                "timeout", "seq", "ok", "work"), 3, new long[] { 0, 1_000_000, 2_000_000 });
            yield return new Scenario("SelectorGuardSucceedsEndsComposite", () => Create(
                MemorySequence(RetainingSelector(Leaf("alarm", F, F, S), MemorySequence(Leaf("x", S), Leaf("job"))), Leaf("hold")),
                "crew", "top", "alarm", "jobSeq", "x", "job", "hold"), 4);
            yield return new Scenario("EmptySequence", () => Create(RetainingSequence(), "seq"), 1);
            yield return new Scenario("EmptySelector", () => Create(RetainingSelector(), "sel"), 1);
            yield return new Scenario("RetainedChildCompletesAndNextRuns", () => Create(
                RetainingSequence(Leaf("ok", S, S, S, S), Leaf("a", R, S), Leaf("b", R, R, S)),
                "seq", "ok", "a", "b"), 4);
        }

        [TestCaseSource(nameof(Scenarios))]
        public void NativeMatchesTheReferenceRecordForRecord(Scenario scenario)
        {
            foreach (RetainingNativeDrive drive in Enum.GetValues(typeof(RetainingNativeDrive)))
            {
                var reference = scenario.Build();
                using var native = new RetainingNativeTwin(scenario.Build(), drive);
                for (var update = 1; update <= scenario.Updates; update++)
                {
                    var time = scenario.Times.Length == 0 ? -1 : scenario.Times[update - 1];
                    var expected = reference.Update((ulong)update, out var expectedResult, time);
                    var actual = native.Update((ulong)update, out var actualResult, time);
                    var label = drive + ", update " + update;
                    Assert.That(actual, Is.EqualTo(expected),
                        label + "\nReference: " + Format(expected) + "\n   Native: " + Format(actual));
                    var completed = expectedResult.Progress == ReferenceExecutionProgress.Completed;
                    Assert.That(actualResult.End,
                        Is.EqualTo(completed ? NativeLifecycleStepKindV1.Completed : NativeLifecycleStepKindV1.Waiting),
                        label + ": progress.");
                    Assert.That(actualResult.Root, Is.EqualTo(expectedResult.RootResult), label + ": root result.");
                    if (!scenario.HasParallel)
                        Assert.That(actualResult.Steps, Is.EqualTo(expectedResult.CumulativeSteps), label + ": semantic steps.");
                    if (completed) break;
                }
                Assert.That(native.Control[0].RetainPhase, Is.Zero, drive + ": no guard pass is left open between updates.");
            }
        }

        /// <summary>
        /// P10-013 stage 2 finding, pre-existing in v1 and not changed by this stage: with the
        /// production frame array (NodeCount frames) a Parallel whose first branch is suspended with
        /// two or more frames while a later branch is suspended too loses the later branch's frames
        /// when the first one is restored (RestoreSuspendedBranch copies the segment down before
        /// moving the lower segments up). Native then ticks "work" twice and never "other". Programs
        /// with a retaining composite use the overlap-safe restore and pass the same scenario
        /// (RetainingInsideParallel). Un-ignore when the owner decides to fix v1.
        /// </summary>
        [Test]
        [Ignore("P10-013 finding F-S2-1: pre-existing v1 native Parallel restore overwrites a suspended branch when no frame is free; v1 left unchanged by decision of the stage constraints.")]
        public void V1ParallelWithTwoSuspendedBranchesMatchesTheReference()
        {
            Func<RetainingTree> build = () => Create(
                Parallel(ReferenceParallelPolicy.RequireAllSuccess,
                    MemorySequence(Leaf("ok", S, S, F), Leaf("work")),
                    Leaf("other")),
                "par", "seq", "ok", "work", "other");
            var reference = build();
            using var native = new RetainingNativeTwin(build());
            for (var update = 1; update <= 3; update++)
                Assert.That(native.Update((ulong)update), Is.EqualTo(reference.Update((ulong)update)), "Update " + update);
        }

        [Test]
        public void StartAndResumeOfAnEmptyGuardPassCostTwoSteps()
        {
            using var retaining = new RetainingNativeTwin(Create(RetainingSequence(Leaf("work")), "seq", "work"));
            using var memory = new RetainingNativeTwin(Create(MemorySequence(Leaf("work")), "seq", "work"));
            retaining.Update(1);
            memory.Update(1);
            retaining.Update(2, out var withPass);
            memory.Update(2, out var plain);
            Assert.That(withPass.Steps, Is.EqualTo(plain.Steps + 2));
        }

        // ---- Abort from above ---------------------------------------------------------------

        [TestCase("E g2", new[] { "A g2 TreeStopped@seq", "X(Ab) g2", "A work TreeStopped@seq", "X(Ab) work", "A seq TreeStopped@seq", "X(Ab) seq" })]
        [TestCase("X g1", new[] { "A work TreeStopped@seq", "X(Ab) work", "A seq TreeStopped@seq", "X(Ab) seq" })]
        [TestCase("T g1 S", null)]
        public void AnAbortDuringAGuardPassAbortsTheGuardBranchThenTheRetainedBranch(string stopAfter, string[] expectedAbort)
        {
            // Same scenario as the reference test of the same name, plus a stop between a guard's
            // terminal Tick and its Exit; native records must equal the reference's in all three.
            Func<RetainingTree> build = () => Create(
                RetainingSequence(Leaf("g1", S, S), Leaf("g2", S), Leaf("work")),
                "seq", "g1", "g2", "work");
            using var native = new RetainingNativeTwin(build());
            native.Update(1);
            native.Begin(2);
            var lines = new List<string>();
            for (var guard = 0; !lines.Contains(stopAfter); guard++)
            {
                Assert.That(guard, Is.LessThan(100), "Never reached '" + stopAfter + "'.");
                native.Step(lines);
            }
            var reached = lines.Count;
            Assert.That(native.Machine.TryRequestAbort(Burst.BurstNodeAbortReason.TreeStopped, out var failure), Is.True, failure.Code.ToString());
            Assert.That(native.RunToRest(lines), Is.EqualTo(NativeLifecycleStepKindV1.Completed));
            var actual = lines.Skip(reached).ToArray();

            var reference = build();
            reference.Update(1);
            var before = reference.Fixture.Trace.Records.Count;
            reference.Machine.BeginUpdate(At(2));
            while (!reference.Render(before).Contains(stopAfter)) reference.Machine.AdvanceOneStep();
            var referenceReached = reference.Render(before).Length;
            reference.Machine.RequestAbort(NodeAbortReason.TreeStopped, new RuntimeNodeIndex(0));
            ReferenceExecutionEnvelope result;
            do result = reference.Machine.AdvanceOneStep();
            while (result.Progress == ReferenceExecutionProgress.Suspended);
            RetainingAssert.Records(actual, reference.Render(before).Skip(referenceReached).ToArray());
            if (expectedAbort != null) RetainingAssert.Records(actual, expectedAbort);
            Assert.That(native.Control[0].Depth, Is.Zero);
            Assert.That(native.Control[0].RetainPhase, Is.Zero);
        }

        [Test]
        public void AnAbortBetweenUpdatesAbortsTheRetainedBranchAsForAnyComposite()
        {
            using var native = new RetainingNativeTwin(Create(RetainingSequence(Leaf("ok", S), Leaf("work")), "seq", "ok", "work"));
            native.Update(1);
            native.Begin(2);
            Assert.That(native.Machine.TryRequestAbort(Burst.BurstNodeAbortReason.TreeStopped, out var failure), Is.True, failure.Code.ToString());
            var lines = new List<string>();
            Assert.That(native.RunToRest(lines), Is.EqualTo(NativeLifecycleStepKindV1.Completed));
            RetainingAssert.Records(lines.ToArray(), "A work TreeStopped@seq", "X(Ab) work", "A seq TreeStopped@seq", "X(Ab) seq");
        }

        [Test]
        public void AStopDuringATakeoverAbortsTheRetainedRemainderThenTheHeldGuardBranch()
        {
            // Precision 7 of stage 1: the takeover abort of [prep, inner[walk]] is under way when
            // the tree is stopped; the retained remainder goes first, then the held guard branch.
            using var native = new RetainingNativeTwin(Create(
                RetainingSelector(
                    MemorySequence(Leaf("alarm", F, S), Leaf("run")),
                    MemorySequence(Leaf("prep", S), MemorySequence(Leaf("walk")))),
                "sel", "flee", "alarm", "run", "patrolSeq", "prep", "inner", "walk"));
            native.Update(1);
            native.Begin(2);
            var lines = new List<string>();
            while (!lines.Contains("A walk Explicit@flee")) native.Step(lines);
            var reached = lines.Count;
            Assert.That(native.Machine.TryRequestAbort(Burst.BurstNodeAbortReason.TreeStopped, out var failure,
                replacePendingForTreeStop: true), Is.True, failure.Code.ToString());
            Assert.That(native.RunToRest(lines), Is.EqualTo(NativeLifecycleStepKindV1.Completed));
            RetainingAssert.Records(lines.Skip(reached).ToArray(),
                "X(Ab) walk", "A inner TreeStopped@sel", "X(Ab) inner", "A patrolSeq TreeStopped@sel", "X(Ab) patrolSeq",
                "A run TreeStopped@sel", "X(Ab) run", "A flee TreeStopped@sel", "X(Ab) flee",
                "A sel TreeStopped@sel", "X(Ab) sel");
            Assert.That(native.Control[0].Depth, Is.Zero);
            Assert.That(native.Control[0].RetainPhase, Is.Zero);
            Assert.That(native.Control[0].SuspendedFrameStart, Is.EqualTo((uint)native.Frames.Length), "No parked frame is left behind.");
        }

        // ---- Memory layout and capacity ---------------------------------------------------------

        [Test]
        public void TheCursorIsAtOffsetZeroAndTheRetainedOrdinalPlusOneAtOffsetFourDuringAPass()
        {
            using var native = new RetainingNativeTwin(Create(
                RetainingSequence(Leaf("g1", S, S), Leaf("g2", S, S), Leaf("work")),
                "seq", "g1", "g2", "work"));
            native.Update(1);
            Assert.That(native.ReadMemory(0, 0), Is.EqualTo(2u));
            Assert.That(native.ReadMemory(0, 4), Is.EqualTo(0u));
            native.Begin(2);
            var lines = new List<string>();
            while (!lines.Contains("E g2")) native.Step(lines);
            Assert.That(native.ReadMemory(0, 0), Is.EqualTo(1u), "Offset 0: the guard cursor.");
            Assert.That(native.ReadMemory(0, 4), Is.EqualTo(3u), "Offset 4: retained ordinal + 1.");
            Assert.That(native.Control[0].RetainedFrameCount, Is.EqualTo(1u), "The retained leaf is parked.");
            native.RunToRest(lines);
            Assert.That(native.ReadMemory(0, 0), Is.EqualTo(2u));
            Assert.That(native.ReadMemory(0, 4), Is.EqualTo(0u));
        }

        [Test]
        public void ATakeoverWithEveryNodeLiveFitsInNodeCountFramesWithoutCorruption()
        {
            // Worst case for the frame array (NodeCount frames, as the arena allocates): every
            // node of the tree is live during the takeover (guard branch held, retained branch with
            // a suspended Parallel branch restored for its abort).
            Func<RetainingTree> build = () => Create(
                RetainingSelector(
                    MemorySequence(Leaf("alarm", F, S), Leaf("run")),
                    Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a", R, R), Leaf("b", R, R))),
                "sel", "flee", "alarm", "run", "par", "a", "b");
            var reference = build();
            using var native = new RetainingNativeTwin(build());
            Assert.That(native.Frames.Length, Is.EqualTo(7));
            for (var update = 1; update <= 3; update++)
            {
                var expected = reference.Update((ulong)update);
                var actual = native.Update((ulong)update);
                Assert.That(actual, Is.EqualTo(expected), "Update " + update + "\nReference: " + Format(expected) + "\n   Native: " + Format(actual));
            }
            Assert.That(native.Control[0].Faulted, Is.Zero);
        }
    }
}
