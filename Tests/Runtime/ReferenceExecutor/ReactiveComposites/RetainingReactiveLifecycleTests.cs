using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static AIBT.Tests.Runtime.RetainingTree;

namespace AIBT.Tests.Runtime
{
    /// <summary>
    /// ADR-P10-013 stage 1, reference executor: budget suspension at every step of a guard pass,
    /// nesting, Parallel, mixing with v1 reactive composites, abort from above during a guard pass,
    /// empty composites, root completion, memory layout and the deferred reevaluation request.
    /// </summary>
    internal sealed class RetainingReactiveLifecycleTests
    {
        private const NodeStatus S = NodeStatus.Success;
        private const NodeStatus F = NodeStatus.Failure;
        private const NodeStatus R = NodeStatus.Running;

        // ---- Budget suspension -------------------------------------------------------------

        private static IEnumerable<TestCaseData> BudgetScenarios()
        {
            yield return new TestCaseData((Func<RetainingTree>)(() =>
                RetainingReactiveExampleTests.Example1Tree(new[] { F, F, F }, new[] { R, R, S })), 4, new long[0])
                .SetName("Budget_Example1_KeptJob");
            yield return new TestCaseData((Func<RetainingTree>)(() => Create(
                MemorySequence(RetainingSelector(Leaf("alarm", F, S), Leaf("job", R, S)), Leaf("hold")),
                "crew", "top", "alarm", "job", "hold")), 3, new long[0])
                .SetName("Budget_Example2_Preemption");
            yield return new TestCaseData((Func<RetainingTree>)(() => Create(
                RetainingSequence(Leaf("ok", S, S, F), Leaf("wait")), "seq", "ok", "wait")), 3, new long[0])
                .SetName("Budget_Example3_GuardFails");
            yield return new TestCaseData((Func<RetainingTree>)(() => Create(
                RetainingSelector(MemorySequence(Leaf("alarm", F, F, S, F), Leaf("run")), Leaf("patrol")),
                "sel", "flee", "alarm", "run", "patrol")), 5, new long[0])
                .SetName("Budget_Example4_Takeover");
            yield return new TestCaseData((Func<RetainingTree>)(() => Create(
                RetainingSequence(Leaf("cond", S, S, S, S), Leaf("stepA", R, S, R, S), Leaf("stepB", R, R)),
                "seq", "cond", "stepA", "stepB")), 4, new long[0])
                .SetName("Budget_Example5_Pitfall");
            yield return new TestCaseData((Func<RetainingTree>)(() => Create(
                RetainingSequence(Leaf("ok", S, S, S, S), Timeout(3_000_000, F, Leaf("wait"))),
                "seq", "ok", "timeout", "wait")), 4, new long[] { 0, 1_000_000, 2_000_000, 3_000_000 })
                .SetName("Budget_Example6_Timeout");
            yield return new TestCaseData((Func<RetainingTree>)(() => Create(
                RetainingSequence(Leaf("g1", S, S, S), Leaf("g2", S, S, S), Leaf("work")),
                "seq", "g1", "g2", "work")), 3, new long[0])
                .SetName("Budget_TwoGuardsThenResume");
            yield return new TestCaseData((Func<RetainingTree>)(NestedTree), 3, new long[0])
                .SetName("Budget_Nested");
            yield return new TestCaseData((Func<RetainingTree>)(ParallelGuardTree), 3, new long[0])
                .SetName("Budget_ParallelGuardTakeover");
            yield return new TestCaseData((Func<RetainingTree>)(RetainingInsideParallelTree), 3, new long[0])
                .SetName("Budget_RetainingInsideParallel");
        }

        [TestCaseSource(nameof(BudgetScenarios))]
        public void BudgetSuspensionAtEveryStepMatchesAnUnlimitedRun(Func<RetainingTree> build, int updates, long[] times)
        {
            var unlimited = build();
            var stepped = build();
            for (var update = 1; update <= updates; update++)
            {
                var time = times.Length == 0 ? -1 : times[update - 1];
                var expected = unlimited.Update((ulong)update, out var expectedResult, time);
                var actual = stepped.UpdateInSegments((ulong)update, 1, out var actualResult, out var suspensions, time);

                RetainingAssert.Records(actual, expected);
                Assert.That(actualResult.Progress, Is.EqualTo(expectedResult.Progress), "Update " + update + " progress.");
                Assert.That(actualResult.RootResult, Is.EqualTo(expectedResult.RootResult), "Update " + update + " root result.");
                Assert.That(actualResult.CumulativeSteps, Is.EqualTo(expectedResult.CumulativeSteps),
                    "Update " + update + ": suspension must not add or repeat steps (a resumed update starts no second guard pass).");
                Assert.That((ulong)suspensions, Is.EqualTo(expectedResult.CumulativeSteps - 1),
                    "Update " + update + ": a one-step budget suspends between every pair of steps.");
                Assert.That(stepped.Machine.CaptureInspection().ActiveNodeCount,
                    Is.EqualTo(unlimited.Machine.CaptureInspection().ActiveNodeCount));
                if (expectedResult.Progress == ReferenceExecutionProgress.Completed) break;
            }
        }

        [Test]
        public void StartingAndResumingAGuardPassAreOneStepEach()
        {
            // Update 2 of retaining-seq [ok, work]: start pass (1), select ok (1), enter+tick ok
            // (counted as for any child), accept ok, resume (1), tick work.
            var withPass = Create(RetainingSequence(Leaf("ok", S, S), Leaf("work")), "seq", "ok", "work");
            withPass.Update(1);
            withPass.Update(2, out var second);

            // The same update with the guard removed: an empty guard pass still starts and resumes.
            var noGuards = Create(RetainingSequence(Leaf("work")), "seq", "work");
            noGuards.Update(1);
            noGuards.Update(2, out var emptyPass);
            RetainingAssert.Records(noGuards.Render(0).Skip(3).ToArray(), "T work R");

            var plain = Create(MemorySequence(Leaf("work")), "seq", "work");
            plain.Update(1);
            plain.Update(2, out var memory);

            Assert.That(emptyPass.CumulativeSteps, Is.EqualTo(memory.CumulativeSteps + 2),
                "An empty guard pass costs exactly its start and resume transitions.");
            Assert.That(second.CumulativeSteps, Is.GreaterThan(emptyPass.CumulativeSteps));
        }

        // ---- Nesting ------------------------------------------------------------------------

        private static RetainingTree NestedTree()
            => Create(
                RetainingSequence(
                    Leaf("outerOk", S, S, S),
                    RetainingSelector(Leaf("alarm", F, F, S), Leaf("work")),
                    Leaf("after")),
                "outer", "outerOk", "inner", "alarm", "work", "after");

        [Test]
        public void NestedRetainingCompositesEachRunTheirGuardPassInOrder()
        {
            var tree = NestedTree();
            RetainingAssert.Records(tree.Update(1),
                "E outer", "E outerOk", "T outerOk S", "X outerOk", "E inner", "E alarm", "T alarm F", "X alarm", "E work", "T work R");
            RetainingAssert.Records(tree.Update(2),
                "E outerOk", "T outerOk S", "X outerOk", "E alarm", "T alarm F", "X alarm", "T work R");
            RetainingAssert.Records(tree.Update(3),
                "E outerOk", "T outerOk S", "X outerOk", "E alarm", "T alarm S", "X alarm",
                "A work Explicit@alarm", "X(Ab) work", "T inner S", "X inner", "E after", "T after R");
        }

        [Test]
        public void AnOuterGuardFailureAbortsTheWholeRetainedBranchDeepestFirst()
        {
            var tree = Create(
                RetainingSequence(
                    Leaf("outerOk", S, F),
                    RetainingSelector(Leaf("alarm", F), Leaf("work"))),
                "outer", "outerOk", "inner", "alarm", "work");
            tree.Update(1);

            RetainingAssert.Records(tree.Update(2, out var result),
                "E outerOk", "T outerOk F", "X outerOk",
                "A work Explicit@outerOk", "X(Ab) work", "A inner Explicit@outerOk", "X(Ab) inner",
                "T outer F", "X outer");
            Assert.That(result.RootResult, Is.EqualTo(F));
            Assert.That(tree.Machine.CaptureInspection().ActiveNodeCount, Is.Zero);
        }

        // ---- Parallel -----------------------------------------------------------------------

        private static RetainingTree RetainingInsideParallelTree()
            => Create(
                Parallel(ReferenceParallelPolicy.RequireAllSuccess,
                    RetainingSequence(Leaf("ok", S, S, F), Leaf("work")),
                    Leaf("other")),
                "par", "seq", "ok", "work", "other");

        [Test]
        public void ARetainingCompositeInsideAParallelBranchRunsItsGuardPassWhenTheBranchIsRestored()
        {
            var tree = RetainingInsideParallelTree();
            RetainingAssert.Records(tree.Update(1),
                "E par", "E seq", "E ok", "T ok S", "X ok", "E work", "T work R", "E other", "T other R");
            RetainingAssert.Records(tree.Update(2),
                "E ok", "T ok S", "X ok", "T work R", "T other R");

            var third = tree.Update(3, out var result);
            Assert.That(third.Take(7), Is.EqualTo(new[]
            {
                "E ok", "T ok F", "X ok", "A work Explicit@ok", "X(Ab) work", "T seq F", "X seq",
            }), RetainingTree.Format(third));
            Assert.That(third, Does.Contain("X(Ab) other"), "Parallel completion aborts the other branch.");
            Assert.That(result.RootResult, Is.EqualTo(F));
        }

        private static RetainingTree ParallelGuardTree()
            => Create(
                RetainingSelector(
                    Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("a", F, R), Leaf("b", R)),
                    Leaf("patrol")),
                "sel", "par", "a", "b", "patrol");

        [Test]
        public void AParallelGuardThatRunsTakesOverAndIsThenRetained()
        {
            var tree = ParallelGuardTree();
            var first = tree.Update(1);
            Assert.That(first.Last(), Is.EqualTo("T patrol R"), RetainingTree.Format(first));

            RetainingAssert.Records(tree.Update(2),
                "E par", "E a", "T a R", "E b", "T b R", "A patrol Explicit@par", "X(Ab) patrol");
            RetainingAssert.Records(tree.Update(3), "T a R", "T b R");
        }

        // ---- Mixing with v1 -----------------------------------------------------------------

        [Test]
        public void AV1ReactiveCompositeInsideARetainedBranchStillRestartsItsRunningChild()
        {
            var tree = Create(
                RetainingSequence(Leaf("ok", S, S), ReactiveSequence(Leaf("cond", S, S), Leaf("work"))),
                "seq", "ok", "v1", "cond", "work");
            RetainingAssert.Records(tree.Update(1),
                "E seq", "E ok", "T ok S", "X ok", "E v1", "E cond", "T cond S", "X cond", "E work", "T work R");
            RetainingAssert.Records(tree.Update(2),
                "E ok", "T ok S", "X ok", "A work Explicit@v1", "X(Ab) work", "E cond", "T cond S", "X cond", "E work", "T work R");
        }

        [Test]
        public void AV1ReactiveCompositeAboveARetainingCompositeResetsItWithEverythingBelow()
        {
            var tree = Create(
                ReactiveSequence(Leaf("cond", S, S), RetainingSequence(Leaf("ok", S, S), Leaf("work"))),
                "v1", "cond", "seq", "ok", "work");
            tree.Update(1);
            RetainingAssert.Records(tree.Update(2),
                "A work Explicit@v1", "X(Ab) work", "A seq Explicit@v1", "X(Ab) seq",
                "E cond", "T cond S", "X cond", "E seq", "E ok", "T ok S", "X ok", "E work", "T work R");
        }

        // ---- Abort from above during a guard pass ---------------------------------------------

        [TestCase("E g2", new[] { "A g2 TreeStopped@seq", "X(Ab) g2", "A work TreeStopped@seq", "X(Ab) work", "A seq TreeStopped@seq", "X(Ab) seq" })]
        [TestCase("X g1", new[] { "A work TreeStopped@seq", "X(Ab) work", "A seq TreeStopped@seq", "X(Ab) seq" })]
        public void AnAbortDuringAGuardPassAbortsTheGuardBranchThenTheRetainedBranch(string stopAfter, string[] expectedAbort)
        {
            // Stopping right after "E g2" catches the pass with an active guard branch; after
            // "X g1" it catches the pass between two guards.
            var tree = Create(
                RetainingSequence(Leaf("g1", S, S), Leaf("g2", S), Leaf("work")),
                "seq", "g1", "g2", "work");
            tree.Update(1);

            var before = tree.Fixture.Trace.Records.Count;
            Assert.That(tree.Machine.BeginUpdate(At(2)).Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            var guard = 0;
            while (!tree.Render(before).Contains(stopAfter))
            {
                Assert.That(++guard, Is.LessThan(100), "Never reached '" + stopAfter + "'.");
                Assert.That(tree.Machine.AdvanceOneStep().Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended));
            }

            var reached = tree.Render(before).Length;
            Assert.That(tree.Machine.RequestAbort(NodeAbortReason.TreeStopped, new RuntimeNodeIndex(0)).Progress,
                Is.EqualTo(ReferenceExecutionProgress.Suspended));
            ReferenceExecutionEnvelope result;
            guard = 0;
            do
            {
                Assert.That(++guard, Is.LessThan(100));
                result = tree.Machine.AdvanceOneStep();
            }
            while (result.Progress == ReferenceExecutionProgress.Suspended);

            RetainingAssert.Records(tree.Render(before).Skip(reached).ToArray(), expectedAbort);
            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(tree.Machine.CaptureInspection().ActiveNodeCount, Is.Zero);
        }

        [Test]
        public void AnExternalAbortBetweenUpdatesAbortsTheRetainedBranchAsForAnyComposite()
        {
            var tree = Create(RetainingSequence(Leaf("ok", S), Leaf("work")), "seq", "ok", "work");
            tree.Update(1);
            var before = tree.Fixture.Trace.Records.Count;
            tree.Machine.Abort(At(2), NodeAbortReason.TreeStopped, new RuntimeNodeIndex(0));
            RetainingAssert.Records(tree.Render(before),
                "A work TreeStopped@seq", "X(Ab) work", "A seq TreeStopped@seq", "X(Ab) seq");
        }

        // ---- Empty composites and root completion ---------------------------------------------

        [Test]
        public void AnEmptyRetainingSequenceSucceeds()
        {
            var tree = Create(RetainingSequence(), "seq");
            RetainingAssert.Records(tree.Update(1, out var result), "E seq", "T seq S", "X seq");
            Assert.That(result.RootResult, Is.EqualTo(S));
        }

        [Test]
        public void AnEmptyRetainingSelectorFails()
        {
            var tree = Create(RetainingSelector(), "sel");
            RetainingAssert.Records(tree.Update(1, out var result), "E sel", "T sel F", "X sel");
            Assert.That(result.RootResult, Is.EqualTo(F));
        }

        [Test]
        public void ARootThatCompletesIsEnteredAfreshWithoutAGuardPassOnTheNextRun()
        {
            var tree = Create(RetainingSequence(Leaf("ok", S, S, S, S), Leaf("work", R, S, R)), "seq", "ok", "work");
            tree.Update(1);
            tree.Update(2, out var completed);
            Assert.That(completed.RootResult, Is.EqualTo(S));
            Assert.That(completed.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));

            tree.Machine.Restart();
            RetainingAssert.Records(tree.Update(3), "E seq", "E ok", "T ok S", "X ok", "E work", "T work R");
        }

        // ---- Memory layout --------------------------------------------------------------------

        [Test]
        public void TheCursorIsAtOffsetZeroAndTheRetainedOrdinalPlusOneAtOffsetFourDuringAPass()
        {
            var tree = Create(
                RetainingSequence(Leaf("g1", S, S), Leaf("g2", S, S), Leaf("work")),
                "seq", "g1", "g2", "work");
            tree.Update(1);
            AssertMemory(tree, cursor: 2, pass: 0);

            var before = tree.Fixture.Trace.Records.Count;
            tree.Machine.BeginUpdate(At(2));
            var guard = 0;
            while (!tree.Render(before).Contains("E g2"))
            {
                Assert.That(++guard, Is.LessThan(100));
                tree.Machine.AdvanceOneStep();
            }
            AssertMemory(tree, cursor: 1, pass: 3);

            ReferenceExecutionEnvelope result;
            do result = tree.Machine.AdvanceOneStep();
            while (result.Progress == ReferenceExecutionProgress.Suspended);
            AssertMemory(tree, cursor: 2, pass: 0);
        }

        private static void AssertMemory(RetainingTree tree, uint cursor, uint pass)
        {
            var memory = tree.Machine.CaptureNodeState(new RuntimeNodeIndex(0)).Memory;
            Assert.That(memory.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToUInt32(memory, 0), Is.EqualTo(cursor), "Offset 0: the child cursor.");
            Assert.That(BitConverter.ToUInt32(memory, 4), Is.EqualTo(pass), "Offset 4: retained ordinal + 1 during a guard pass, else 0.");
        }

        // ---- Requested reevaluation (P9-001) under a retaining composite ----------------------

        [Test]
        public void AReevaluationRequestBelowARetainingCompositeWaitsForItsGuardPass()
        {
            var tree = Create(
                RetainingSequence(Leaf("ok", S, S), MemorySequence(Leaf("a", S, S), Leaf("b"))),
                "seq", "ok", "mseq", "a", "b");
            tree.Update(1);
            Assert.That(tree.Machine.TryRequestReevaluation(new RuntimeNodeIndex(2)), Is.True);

            RetainingAssert.Records(tree.Update(2),
                "E ok", "T ok S", "X ok", "A b Explicit@mseq", "X(Ab) b", "E a", "T a S", "X a", "E b", "T b R");
        }

        [Test]
        public void AReevaluationRequestIsDroppedWhenTheGuardPassEndsTheComposite()
        {
            var tree = Create(
                RetainingSequence(Leaf("ok", S, F), MemorySequence(Leaf("a", S), Leaf("b"))),
                "seq", "ok", "mseq", "a", "b");
            tree.Update(1);
            Assert.That(tree.Machine.TryRequestReevaluation(new RuntimeNodeIndex(2)), Is.True);

            RetainingAssert.Records(tree.Update(2, out var result),
                "E ok", "T ok F", "X ok", "A b Explicit@ok", "X(Ab) b", "A mseq Explicit@ok", "X(Ab) mseq", "T seq F", "X seq");
            Assert.That(result.RootResult, Is.EqualTo(F));
        }
    }
}
