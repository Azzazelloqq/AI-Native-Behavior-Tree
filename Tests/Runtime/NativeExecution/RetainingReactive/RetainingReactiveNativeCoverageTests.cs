using System;
using System.Collections.Generic;
using NUnit.Framework;
using static AIBT.Tests.Runtime.RetainingTree;

namespace AIBT.Tests.Runtime
{
    /// <summary>
    /// P10-014: native coverage the P10-013 code review found missing. Every scenario runs on the
    /// native lifecycle machine (production frame capacity, three drives) and must equal the
    /// reference executor record for record, with the same root results and progress; scenarios
    /// without a Parallel also compare semantic step counts.
    /// </summary>
    internal sealed class RetainingReactiveNativeCoverageTests
    {
        private const NodeStatus S = NodeStatus.Success;
        private const NodeStatus F = NodeStatus.Failure;
        private const NodeStatus R = NodeStatus.Running;

        private static P113TestNode Repeater(uint count, bool stopOnFailure, P113TestNode child)
            => P113TestProgram.Decorator("aibt.core.repeater", P113TestProgram.RepeaterConfiguration(count, stopOnFailure),
                4, 4, 4, NodeMemoryLifetime.Activation, child);

        private static P113TestNode Cooldown(long durationMicroseconds, NodeStatus result, ReferenceCooldownStartPolicy policy, P113TestNode child)
            => P113TestProgram.Decorator("aibt.core.cooldown",
                P113TestProgram.TimedConfiguration(durationMicroseconds, result, (byte)policy),
                8, 8, 8, NodeMemoryLifetime.Instance, child);

        internal sealed class Case
        {
            internal Case(string name, Func<RetainingTree> build, int updates, bool hasParallel = false)
            {
                Name = name;
                Build = build;
                Updates = updates;
                HasParallel = hasParallel;
            }

            internal string Name { get; }
            internal Func<RetainingTree> Build { get; }
            internal int Updates { get; }
            internal bool HasParallel { get; }
            public override string ToString() => Name;
        }

        private static IEnumerable<Case> Cases()
        {
            // A guard of a retaining composite that sits inside a Parallel branch takes over.
            yield return new Case("GuardTakeoverInsideAParallelBranch", () => Create(
                Parallel(ReferenceParallelPolicy.RequireAllSuccess,
                    RetainingSelector(MemorySequence(Leaf("alarm", F, S), Leaf("run")), Leaf("patrol")),
                    Leaf("other")),
                "par", "sel", "flee", "alarm", "run", "patrol", "other"), 3, hasParallel: true);
            yield return new Case("GuardTakeoverInsideTheSecondParallelBranch", () => Create(
                Parallel(ReferenceParallelPolicy.RequireAllSuccess,
                    Leaf("other"),
                    RetainingSelector(MemorySequence(Leaf("alarm", F, S), Leaf("run")), Leaf("patrol"))),
                "par", "other", "sel", "flee", "alarm", "run", "patrol"), 3, hasParallel: true);

            // Cooldown: as the retained child (it is not re-entered, so its deadline does not move),
            // and as a guard (its blocked result ends the composite or lets the next guard run).
            yield return new Case("CooldownAsRetainedChild_OnEnter", () => Create(
                RetainingSequence(Leaf("ok", S, S, S, S, S),
                    Cooldown(2_500, F, ReferenceCooldownStartPolicy.OnEnter, Leaf("work", R, R, S, S))),
                "seq", "ok", "cooldown", "work"), 5);
            yield return new Case("CooldownAsGuard_BlockedEndsTheSequence", () => Create(
                RetainingSequence(Cooldown(2_500, F, ReferenceCooldownStartPolicy.OnSuccessfulExit, Leaf("cond", S, S, S, S)),
                    Leaf("work")),
                "seq", "cooldown", "cond", "work"), 3);
            yield return new Case("CooldownAsGuard_SelectorBlockedGuardLetsTheRetainedChildRun", () => Create(
                RetainingSelector(Cooldown(1_500, F, ReferenceCooldownStartPolicy.OnEnter, Leaf("alarm", F, F, R)),
                    Leaf("patrol", R, R, R, R)),
                "sel", "cooldown", "alarm", "patrol"), 4);

            // Repeater: as the retained child (its count survives the guard pass) and as a guard
            // that takes over when its child runs.
            yield return new Case("RepeaterAsRetainedChild", () => Create(
                RetainingSequence(Leaf("ok", S, S, S, S, S),
                    Repeater(2, false, Leaf("work", R, S, R, S))),
                "seq", "ok", "rep", "work"), 4);
            yield return new Case("RepeaterAsGuard_TakesOver", () => Create(
                RetainingSelector(Repeater(2, true, Leaf("alarm", F, S, R)), Leaf("patrol")),
                "sel", "rep", "alarm", "patrol"), 3);
            yield return new Case("RepeaterAsGuard_InSequence", () => Create(
                RetainingSequence(Repeater(2, true, Leaf("cond", S, S, S, S, S, F)), Leaf("work", R, R, R)),
                "seq", "rep", "cond", "work"), 4);
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
                    var expected = reference.Update((ulong)update, out var expectedResult);
                    var actual = native.Update((ulong)update, out var actualResult);
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
                Assert.That(native.Control[0].RetainPhase, Is.Zero, drive + ": no guard pass is left open.");
            }
        }

        /// <summary>
        /// Review point 4 (step count of a Parallel suspension), pinned rather than aligned: a branch
        /// that comes to rest on a leaf that already ticked Running costs the native machine one
        /// semantic step (the ParallelBranchSuspended step) and the reference none; a branch that
        /// rests after a nested Parallel's visit costs neither. A Parallel that completes costs the
        /// native machine one more step as well: it accepts the last branch's result and exits in
        /// two steps where the reference does both in one (seen step by step in P10-014). Everything
        /// else is equal, so per update native = reference + leaf rests + completing Parallels.
        /// Native therefore reaches MaximumStepsPerUpdate / MaximumStepsPerThink slightly earlier in
        /// Parallel trees. Which side is normative is a decision for the owner (P10-014 evidence);
        /// neither is changed.
        /// </summary>
        [Test]
        public void ParallelStepCountsDifferExactlyByLeafRestsAndParallelCompletions()
        {
            Func<RetainingTree> build = () => Create(
                Parallel(ReferenceParallelPolicy.RequireAllSuccess,
                    Leaf("a", R, R, S),
                    MemorySequence(Leaf("x", S, S, S), Leaf("b", R, R, R, S)),
                    Parallel(ReferenceParallelPolicy.RequireAllSuccess, Leaf("c", R, R, S), Leaf("d", R, R, R, S))),
                "outer", "a", "seq", "x", "b", "inner", "c", "d");
            // Leaf rests per update (a leaf ticked Running directly under a Parallel's branch):
            // update 1: a, b, c, d; update 2: a, b, c, d; update 3: b, d (a, c succeed);
            // update 4: none (b and d succeed, all done).
            // Completing Parallels per update: none in updates 1-3; update 4: inner and outer.
            var leafRests = new ulong[] { 4, 4, 2, 0 };
            var parallelCompletions = new ulong[] { 0, 0, 0, 2 };
            var reference = build();
            using var native = new RetainingNativeTwin(build());
            for (var update = 1; update <= leafRests.Length; update++)
            {
                var expected = reference.Update((ulong)update, out var expectedResult);
                var actual = native.Update((ulong)update, out var actualResult);
                Assert.That(actual, Is.EqualTo(expected), "Update " + update + " records.");
                Assert.That(actualResult.Steps,
                    Is.EqualTo(expectedResult.CumulativeSteps + leafRests[update - 1] + parallelCompletions[update - 1]),
                    "Update " + update + ": native steps = reference steps + leaf rests + completing Parallels.");
            }
        }
    }
}
