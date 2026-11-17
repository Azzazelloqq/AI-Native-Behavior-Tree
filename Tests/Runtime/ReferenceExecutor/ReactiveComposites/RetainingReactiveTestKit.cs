using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    /// <summary>
    /// Builds hand-made programs with retaining reactive composites (ADR-P10-013) on top of
    /// <see cref="P113TestProgram"/> and renders the lifecycle trace in the ADR's notation, so a
    /// test can state the exact callback order of one update as a list of short strings:
    /// <c>E n</c> Enter, <c>T n S|F|R</c> Tick (also a composite's result), <c>A n Reason@source</c>
    /// Abort, <c>X n</c> Exit after completion and <c>X(Ab) n</c> Exit after an abort.
    /// </summary>
    internal sealed class RetainingTree
    {
        private readonly Dictionary<uint, string> _names;

        private RetainingTree(P113Fixture fixture, Dictionary<uint, string> names)
        {
            Fixture = fixture;
            _names = names;
        }

        internal P113Fixture Fixture { get; }
        internal ReferenceExecutionMachine Machine => Fixture.Machine;

        internal static P113TestNode RetainingSequence(params P113TestNode[] children)
            => new P113TestNode(BuiltInNodeTypeIds.RetainingSequence, null, 1, 8, 4, NodeMemoryLifetime.Activation, null, children);

        internal static P113TestNode RetainingSelector(params P113TestNode[] children)
            => new P113TestNode(BuiltInNodeTypeIds.RetainingSelector, null, 1, 8, 4, NodeMemoryLifetime.Activation, null, children);

        internal static P113TestNode ReactiveSequence(params P113TestNode[] children)
            => new P113TestNode("aibt.core.reactive-sequence", null, 1, 4, 4, NodeMemoryLifetime.Activation, null, children);

        internal static P113TestNode ReactiveSelector(params P113TestNode[] children)
            => new P113TestNode("aibt.core.reactive-selector", null, 1, 4, 4, NodeMemoryLifetime.Activation, null, children);

        internal static P113TestNode MemorySequence(params P113TestNode[] children)
            => new P113TestNode("aibt.core.memory-sequence", null, 1, 4, 4, NodeMemoryLifetime.Activation, null, children);

        internal static P113TestNode MemorySelector(params P113TestNode[] children)
            => new P113TestNode("aibt.core.memory-selector", null, 1, 4, 4, NodeMemoryLifetime.Activation, null, children);

        internal static P113TestNode Timeout(long durationMicroseconds, NodeStatus result, P113TestNode child)
            => P113TestProgram.Decorator(
                "aibt.core.timeout",
                P113TestProgram.TimedConfiguration(durationMicroseconds, result),
                8, 8, 8, NodeMemoryLifetime.Activation,
                child);

        internal static P113TestNode Parallel(ReferenceParallelPolicy policy, params P113TestNode[] children)
            => P113TestProgram.Parallel(P113TestProgram.ParallelConfiguration(policy), children);

        /// <summary>A leaf named <paramref name="name"/> that returns <paramref name="statuses"/> in order, then Running.</summary>
        internal static P113TestNode Leaf(string name, params NodeStatus[] statuses)
            => P113TestProgram.Leaf(name, new ScriptedReferenceLeaf(statuses));

        /// <summary>Builds the tree; <paramref name="names"/> name the nodes in semantic (pre-) order.</summary>
        internal static RetainingTree Create(P113TestNode root, params string[] names)
        {
            var fixture = P113TestProgram.Create(root);
            var map = new Dictionary<uint, string>();
            for (var index = 0; index < names.Length; index++) map.Add((uint)index, names[index]);
            return new RetainingTree(fixture, map);
        }

        internal static ReferenceUpdateContext At(ulong updateId, long timeMicroseconds = -1)
            => new ReferenceUpdateContext(
                updateId,
                new Revision(updateId),
                timeMicroseconds < 0 ? checked((long)updateId * 1000) : timeMicroseconds);

        /// <summary>Runs one update without a budget and returns its lifecycle records.</summary>
        internal string[] Update(ulong updateId, out ReferenceExecutionEnvelope result, long timeMicroseconds = -1)
        {
            var before = Fixture.Trace.Records.Count;
            result = Machine.Update(At(updateId, timeMicroseconds));
            return Render(before);
        }

        internal string[] Update(ulong updateId, long timeMicroseconds = -1)
            => Update(updateId, out _, timeMicroseconds);

        /// <summary>
        /// Runs one update with a budget of <paramref name="stepsPerSegment"/> steps per segment,
        /// resuming until it is no longer suspended. Returns the lifecycle records and counts the
        /// suspensions.
        /// </summary>
        internal string[] UpdateInSegments(
            ulong updateId,
            ulong stepsPerSegment,
            out ReferenceExecutionEnvelope result,
            out int suspensions,
            long timeMicroseconds = -1)
        {
            var before = Fixture.Trace.Records.Count;
            suspensions = 0;
            result = Machine.Update(At(updateId, timeMicroseconds), ReferenceStepBudget.Limited(stepsPerSegment));
            while (result.Progress == ReferenceExecutionProgress.Suspended)
            {
                suspensions++;
                Assert.That(suspensions, Is.LessThan(10000), "The update never finished.");
                result = Machine.Resume(ReferenceStepBudget.Limited(stepsPerSegment));
            }
            return Render(before);
        }

        internal string[] Render(int fromRecord)
        {
            var lines = new List<string>();
            var records = Fixture.Trace.Records;
            for (var index = fromRecord; index < records.Count; index++)
            {
                var record = records[index];
                var name = Name(record.NodeIndex);
                switch (record.Kind)
                {
                    case ReferenceTraceEventKind.NodeEntered:
                        lines.Add("E " + name);
                        break;
                    case ReferenceTraceEventKind.NodeTicked:
                        lines.Add("T " + name + " " + Letter(record.Status));
                        break;
                    case ReferenceTraceEventKind.NodeAbortStarted:
                        lines.Add("A " + name + " " + record.AbortReason + "@" + Name(record.SourceNodeIndex));
                        break;
                    case ReferenceTraceEventKind.NodeExited:
                        lines.Add((record.ExitReason == NodeExitReason.Aborted ? "X(Ab) " : "X ") + name);
                        break;
                }
            }
            return lines.ToArray();
        }

        internal static string Format(IEnumerable<string> lines) => string.Join("; ", lines);

        private string Name(RuntimeNodeIndex index)
        {
            if (!index.IsValid) return "-";
            return _names.TryGetValue(index.Value, out var name) ? name : "#" + index.Value;
        }

        private static string Letter(NodeStatus? status)
        {
            if (!status.HasValue) return "?";
            switch (status.Value)
            {
                case NodeStatus.Success: return "S";
                case NodeStatus.Failure: return "F";
                case NodeStatus.Running: return "R";
                default: return status.Value.ToString();
            }
        }
    }

    internal static class BuiltInNodeTypeIds
    {
        internal const string RetainingSequence = "aibt.core.reactive-sequence-retaining";
        internal const string RetainingSelector = "aibt.core.reactive-selector-retaining";
    }

    internal static class RetainingAssert
    {
        internal static void Records(string[] actual, params string[] expected)
        {
            Assert.That(actual, Is.EqualTo(expected),
                "Expected: " + RetainingTree.Format(expected) + "\n  Actual: " + RetainingTree.Format(actual));
        }
    }
}
