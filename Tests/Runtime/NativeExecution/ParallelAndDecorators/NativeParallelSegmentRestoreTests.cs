using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static AIBT.Tests.Runtime.RetainingTree;

namespace AIBT.Tests.Runtime
{
    /// <summary>
    /// P10-014, review finding D2 (= P10-013 finding F-S2-1): restoring a suspended Parallel
    /// branch that is not the lowest segment of the suspended-frame region, while fewer frames are
    /// free than the branch is long, must not overwrite the lower segments. v1 programs at the
    /// production frame capacity (one frame per node) must match the reference record for record.
    /// </summary>
    internal sealed class NativeParallelSegmentRestoreTests
    {
        private const NodeStatus S = NodeStatus.Success;
        private const NodeStatus F = NodeStatus.Failure;
        private const NodeStatus R = NodeStatus.Running;

        private static P113TestNode Repeater(uint count, P113TestNode child)
            => P113TestProgram.Decorator(
                "aibt.core.repeater", P113TestProgram.RepeaterConfiguration(count, false),
                4, 4, 4, NodeMemoryLifetime.Activation, child);

        private static IEnumerable<TestCaseData> Trees()
        {
            // parallel [memory-sequence [ok, work], other]: 5 nodes, 1 free frame, segment of 2.
            yield return new TestCaseData((Func<RetainingTree>)(() => Create(
                Parallel(ReferenceParallelPolicy.RequireAllSuccess,
                    MemorySequence(Leaf("ok", S, S, F), Leaf("work")),
                    Leaf("other")),
                "par", "seq", "ok", "work", "other")), 3).SetName("Review_ParallelOfMemorySequenceAndLeaf");
            // parallel [repeater [sequence [a, b]], repeater [sequence [c, d]]]: 9 nodes, 2 free, segment of 3.
            yield return new TestCaseData((Func<RetainingTree>)(() => Create(
                Parallel(ReferenceParallelPolicy.RequireAllSuccess,
                    Repeater(3, MemorySequence(Leaf("a", S, S, S, S), Leaf("b", R, S, R, S, R))),
                    Repeater(3, MemorySequence(Leaf("c", S, S, S, S), Leaf("d", R, R, S, R, S)))),
                "par", "rep1", "seq1", "a", "b", "rep2", "seq2", "c", "d")), 6).SetName("Review_ParallelOfTwoRepeaters");
            // Three branches of different lengths: the middle one is restored over both others.
            yield return new TestCaseData((Func<RetainingTree>)(() => Create(
                Parallel(ReferenceParallelPolicy.RequireAllSuccess,
                    Leaf("x"),
                    MemorySequence(MemorySequence(Leaf("y"))),
                    MemorySequence(Leaf("z"))),
                "par", "x", "s1", "s2", "y", "s3", "z")), 3).SetName("ThreeBranchesOfDifferentLengths");
        }

        /// <summary>
        /// For every layout of up to 10 frames (stack depth d, suspended region from s, restored
        /// segment [first, first + count)), the rotation leaves exactly the active frames
        /// [0, d + count) and suspended frames [s + count, length) that the original copy-then-shift
        /// left wherever that copy was correct (d + count &lt;= s), and keeps every lower segment
        /// where it was not. Free frames in between are never read and may differ.
        /// </summary>
        [Test]
        public void TheRotationEqualsTheOriginalCopyWhereverTheCopyWasCorrect()
        {
            var correctLayouts = 0;
            var formerlyCorruptLayouts = 0;
            for (var length = 1; length <= 10; length++)
            for (var depth = 0; depth < length; depth++)
            for (var suspended = depth; suspended < length; suspended++)
            for (var first = suspended; first < length; first++)
            for (var count = 1; first + count <= length; count++)
            {
                var original = Layout(length);
                var rotated = Layout(length);
                try
                {
                    NativeLifecycleMachineV1.MoveSegmentToStackTop(rotated, (uint)suspended, (uint)first, (uint)count, (uint)depth);
                    var expectedActive = new List<uint>();
                    for (var index = 0; index < depth; index++) expectedActive.Add((uint)index);
                    for (var index = first; index < first + count; index++) expectedActive.Add((uint)index);
                    var expectedSuspended = new List<uint>();
                    for (var index = suspended; index < first; index++) expectedSuspended.Add((uint)index);
                    for (var index = first + count; index < length; index++) expectedSuspended.Add((uint)index);
                    Assert.That(Ids(rotated, 0, depth + count), Is.EqualTo(expectedActive), "Active frames.");
                    Assert.That(Ids(rotated, suspended + count, length), Is.EqualTo(expectedSuspended), "Suspended frames.");

                    OriginalCopyThenShift(original, suspended, first, count, depth);
                    if (depth + count <= suspended)
                    {
                        correctLayouts++;
                        Assert.That(Ids(rotated, 0, depth + count), Is.EqualTo(Ids(original, 0, depth + count)));
                        Assert.That(Ids(rotated, suspended + count, length), Is.EqualTo(Ids(original, suspended + count, length)));
                    }
                    else if (first > suspended && !Ids(original, suspended + count, length).SequenceEqual(expectedSuspended))
                        formerlyCorruptLayouts++;
                }
                finally
                {
                    original.Dispose();
                    rotated.Dispose();
                }
            }
            Assert.That(correctLayouts, Is.GreaterThan(100));
            Assert.That(formerlyCorruptLayouts, Is.GreaterThan(0), "The original copy corrupts some layouts.");
        }

        private static Unity.Collections.NativeArray<NativeFrameStateV1> Layout(int length)
        {
            var frames = new Unity.Collections.NativeArray<NativeFrameStateV1>(length, Unity.Collections.Allocator.Temp);
            for (var index = 0; index < length; index++) frames[index] = new NativeFrameStateV1 { NodeIndex = (uint)index };
            return frames;
        }

        private static List<uint> Ids(Unity.Collections.NativeArray<NativeFrameStateV1> frames, int start, int end)
        {
            var ids = new List<uint>();
            for (var index = start; index < end; index++) ids.Add(frames[index].NodeIndex);
            return ids;
        }

        /// <summary>The v1 restore before P10-014, verbatim apart from the names.</summary>
        private static void OriginalCopyThenShift(
            Unity.Collections.NativeArray<NativeFrameStateV1> frames, int suspended, int first, int count, int destination)
        {
            for (var offset = 0; offset < count; offset++) frames[destination + offset] = frames[first + offset];
            for (var source = first - 1; source >= suspended; source--) frames[source + count] = frames[source];
        }

        [TestCaseSource(nameof(Trees))]
        public void V1ParallelAtProductionCapacityMatchesTheReference(Func<RetainingTree> build, int updates)
        {
            foreach (RetainingNativeDrive drive in Enum.GetValues(typeof(RetainingNativeDrive)))
            {
                var reference = build();
                using var native = new RetainingNativeTwin(build(), drive);
                for (var update = 1; update <= updates; update++)
                {
                    var expected = reference.Update((ulong)update, out var expectedResult);
                    var actual = native.Update((ulong)update, out var actualResult);
                    var label = drive + ", update " + update;
                    Assert.That(actual, Is.EqualTo(expected),
                        label + "\nReference: " + Format(expected) + "\n   Native: " + Format(actual));
                    Assert.That(actualResult.Root, Is.EqualTo(expectedResult.RootResult), label + ": root result.");
                    if (expectedResult.Progress == ReferenceExecutionProgress.Completed) break;
                }
            }
        }
    }
}
