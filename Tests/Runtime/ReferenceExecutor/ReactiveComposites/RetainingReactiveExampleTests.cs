using NUnit.Framework;
using static AIBT.Tests.Runtime.RetainingTree;

namespace AIBT.Tests.Runtime
{
    /// <summary>
    /// ADR-P10-013 §2.5, worked examples 1–6, on the reference executor with exact callback order.
    /// Examples 1 and 2 use a scripted stand-in for the simulation container's action leaf
    /// (Running until its "completion", then Success); the container itself is stage 2.
    /// </summary>
    internal sealed class RetainingReactiveExampleTests
    {
        private const NodeStatus S = NodeStatus.Success;
        private const NodeStatus F = NodeStatus.Failure;
        private const NodeStatus R = NodeStatus.Running;

        // Example 1: memory-seq [top = retaining-selector [alarm, job], hold]. job keeps running
        // through think 2 and reads its completion on think 3.
        [Test]
        public void Example1_TheJobKeepsRunningAndConsumesItsCompletion()
        {
            var tree = Example1Tree(alarm: new[] { F, F, F }, job: new[] { R, R, S });

            RetainingAssert.Records(tree.Update(1, out var first),
                "E crew", "E top", "E alarm", "T alarm F", "X alarm", "E job", "T job R");
            Assert.That(first.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));

            RetainingAssert.Records(tree.Update(2),
                "E alarm", "T alarm F", "X alarm", "T job R");

            RetainingAssert.Records(tree.Update(3),
                "E alarm", "T alarm F", "X alarm", "T job S", "X job", "T top S", "X top", "E hold", "T hold R");
        }

        // Example 2: the alarm rises on think 2. The guard's records come first and name the cause
        // of the abort; the job is not ticked again, so a completion it could have read is not consumed.
        [Test]
        public void Example2_TheAlarmPreemptsTheJobAfterTheGuardRuns()
        {
            var job = new ScriptedReferenceLeaf(R, S);
            var tree = Create(
                MemorySequence(
                    RetainingSelector(Leaf("alarm", F, S), P113TestProgram.Leaf("job", job)),
                    Leaf("hold")),
                "crew", "top", "alarm", "job", "hold");
            tree.Update(1);

            RetainingAssert.Records(tree.Update(2),
                "E alarm", "T alarm S", "X alarm", "A job Explicit@alarm", "X(Ab) job", "T top S", "X top", "E hold", "T hold R");
            Assert.That(job.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Abort:Explicit", "Exit:Aborted" }),
                "The job is ticked once: the guard runs before it and preempts it.");
        }

        // Example 3: retaining-seq [ok, wait(3)] — the wait makes progress across updates.
        [Test]
        public void Example3_AMultiUpdateLeafFinishesUnderARetainingSequence()
        {
            var wait = new ScriptedReferenceLeaf(R, R, S);
            var tree = Create(RetainingSequence(Leaf("ok", S, S, S), P113TestProgram.Leaf("wait", wait)), "seq", "ok", "wait");

            RetainingAssert.Records(tree.Update(1), "E seq", "E ok", "T ok S", "X ok", "E wait", "T wait R");
            RetainingAssert.Records(tree.Update(2), "E ok", "T ok S", "X ok", "T wait R");
            RetainingAssert.Records(tree.Update(3, out var result), "E ok", "T ok S", "X ok", "T wait S", "X wait", "T seq S", "X seq");
            Assert.That(result.RootResult, Is.EqualTo(S));
            Assert.That(wait.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Tick", "Tick", "Exit:Success" }),
                "The wait is entered once and ticked once per update.");
        }

        [Test]
        public void Example3Alternative_AFailingGuardAbortsTheRetainedLeafAfterItsOwnRecords()
        {
            var tree = Create(RetainingSequence(Leaf("ok", S, S, F), Leaf("wait")), "seq", "ok", "wait");
            tree.Update(1);
            tree.Update(2);

            RetainingAssert.Records(tree.Update(3, out var result),
                "E ok", "T ok F", "X ok", "A wait Explicit@ok", "X(Ab) wait", "T seq F", "X seq");
            Assert.That(result.RootResult, Is.EqualTo(F));
            Assert.That(tree.Machine.CaptureInspection().ActiveNodeCount, Is.Zero);
        }

        // Example 4: retaining-selector [flee = memory-seq [alarm, run], patrol].
        [Test]
        public void Example4_ARunningHigherPriorityBranchTakesOver()
        {
            var tree = Create(
                RetainingSelector(MemorySequence(Leaf("alarm", F, F, S, F), Leaf("run")), Leaf("patrol")),
                "sel", "flee", "alarm", "run", "patrol");

            RetainingAssert.Records(tree.Update(1),
                "E sel", "E flee", "E alarm", "T alarm F", "X alarm", "T flee F", "X flee", "E patrol", "T patrol R");
            RetainingAssert.Records(tree.Update(2),
                "E flee", "E alarm", "T alarm F", "X alarm", "T flee F", "X flee", "T patrol R");
            RetainingAssert.Records(tree.Update(3, out var takeover),
                "E flee", "E alarm", "T alarm S", "X alarm", "E run", "T run R", "A patrol Explicit@flee", "X(Ab) patrol");
            Assert.That(takeover.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            RetainingAssert.Records(tree.Update(4), "T run R");
        }

        // Example 5: the pitfall — every child before the running one is a guard, so stepA is
        // re-entered as a guard once stepB runs, takes over and stepB never finishes.
        [Test]
        public void Example5_TwoMultiUpdateChildrenInOneSequenceRestartTheSecond()
        {
            var tree = Create(
                RetainingSequence(Leaf("cond", S, S, S, S), Leaf("stepA", R, S, R, S), Leaf("stepB", R, R)),
                "seq", "cond", "stepA", "stepB");

            RetainingAssert.Records(tree.Update(1), "E seq", "E cond", "T cond S", "X cond", "E stepA", "T stepA R");
            RetainingAssert.Records(tree.Update(2), "E cond", "T cond S", "X cond", "T stepA S", "X stepA", "E stepB", "T stepB R");
            RetainingAssert.Records(tree.Update(3),
                "E cond", "T cond S", "X cond", "E stepA", "T stepA R", "A stepB Explicit@stepA", "X(Ab) stepB");
            RetainingAssert.Records(tree.Update(4), "E cond", "T cond S", "X cond", "T stepA S", "X stepA", "E stepB", "T stepB R");
        }

        // Example 6: retaining-seq [ok, timeout(3 s, failure) [wait(1000)]], one update per second.
        [Test]
        public void Example6_ATimeoutInTheRetainedBranchKeepsItsDeadlineAndFires()
        {
            var tree = Create(
                RetainingSequence(Leaf("ok", S, S, S, S), Timeout(3_000_000, F, Leaf("wait"))),
                "seq", "ok", "timeout", "wait");

            RetainingAssert.Records(tree.Update(1, 0), "E seq", "E ok", "T ok S", "X ok", "E timeout", "E wait", "T wait R");
            RetainingAssert.Records(tree.Update(2, 1_000_000), "E ok", "T ok S", "X ok", "T wait R");
            RetainingAssert.Records(tree.Update(3, 2_000_000), "E ok", "T ok S", "X ok", "T wait R");
            RetainingAssert.Records(tree.Update(4, out var result, 3_000_000),
                "E ok", "T ok S", "X ok", "A wait Timeout@timeout", "X(Ab) wait", "T timeout F", "X timeout", "T seq F", "X seq");
            Assert.That(result.RootResult, Is.EqualTo(F));
        }

        internal static RetainingTree Example1Tree(NodeStatus[] alarm, NodeStatus[] job)
            => Create(
                MemorySequence(RetainingSelector(Leaf("alarm", alarm), Leaf("job", job)), Leaf("hold")),
                "crew", "top", "alarm", "job", "hold");
    }
}
