using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.NativeExecution.HotReload
{
    /// <summary>
    /// ADR-P10-013 stage 2, hot reload (ADR table "Hot reload"): hot reload runs between updates,
    /// so no guard pass is in progress and the retained branch is on the active path. A retaining
    /// composite is a composite for the cursor-reset rule, and changing a node between v1 and
    /// retaining is a type change, classified Incompatible, which restarts the subtree.
    /// <para>
    /// Tree, hand-built like <see cref="NativeHotReloadTestProgram"/>: outer = memory-sequence
    /// [comp [a, b], c]; comp is a v1 reactive sequence or a retaining reactive sequence. Leaf a
    /// succeeds, b runs, so b is the running (retained) branch at rest.
    /// </para>
    /// </summary>
    public sealed class RetainingReactiveHotReloadTests
    {
        private const string Retaining = "aibt.core.reactive-sequence-retaining";
        private const string V1Reactive = "aibt.core.reactive-sequence";
        private static readonly IReadOnlyCollection<NodeId> NoExclusions = new List<NodeId>();

        [Test]
        public void AnUnchangedProgram_MigratesTheRetainedBranch_AndTheNextUpdateResumesIt()
        {
            var program = Tree(Retaining, reorder: false);
            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var old, out var failure), Is.True, failure.Code.ToString());
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Assert.That(old.Machine.TryBeginUpdate(1, 10, out _), Is.True);
                Assert.That(Drive(ref old.Machine, program), Is.EqualTo(new[] {
                    "a:Enter", "a:Tick", "a:Exit:Success", "b:Enter", "b:Tick" }));

                var classification = HotReloadCompatibilityClassifier.Classify(program, program);
                Assert.That(NativeHotReloadStateMigration.TryMigrate(old, program, program, classification, NoExclusions,
                    Allocator.Persistent, out fresh, out var report, out failure), Is.True, failure.Code.ToString());
                Assert.That(report.CursorResetNodeCount, Is.Zero);

                // The guard is re-evaluated, the retained branch resumed: no abort, no second Enter of b.
                Assert.That(fresh.Machine.TryBeginUpdate(2, 20, out _), Is.True);
                Assert.That(Drive(ref fresh.Machine, program), Is.EqualTo(new[] {
                    "a:Enter", "a:Tick", "a:Exit:Success", "b:Tick" }));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [Test]
        public void AStructuralEditUnderARetainingComposite_ResetsItsCursor_AndAbortsTheRetainedBranchWithHotReload()
        {
            var program = Tree(Retaining, reorder: false);
            var next = Tree(Retaining, reorder: true);
            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var old, out var failure), Is.True, failure.Code.ToString());
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Assert.That(old.Machine.TryBeginUpdate(1, 10, out _), Is.True);
                Drive(ref old.Machine, program);

                var classification = HotReloadCompatibilityClassifier.Classify(program, next);
                Assert.That(classification.StructuralChildChangeNodeIds, Is.EquivalentTo(new[] { new NodeId("comp") }));
                Assert.That(NativeHotReloadStateMigration.TryMigrate(old, program, next, classification, NoExclusions,
                    Allocator.Persistent, out fresh, out var report, out failure), Is.True, failure.Code.ToString());
                Assert.That(report.CursorResetNodeCount, Is.EqualTo(1u), "The retaining composite is a composite for the cursor reset.");

                // The retained branch is aborted with HotReload, then comp starts over in the new
                // order [b, a]: b runs first and becomes the retained branch.
                Assert.That(fresh.Machine.TryBeginUpdate(2, 20, out _), Is.True);
                Assert.That(Drive(ref fresh.Machine, next), Is.EqualTo(new[] {
                    "b:Abort:HotReload", "b:Exit:Aborted", "b:Enter", "b:Tick" }));
                Assert.That(fresh.Machine.TryBeginUpdate(3, 30, out _), Is.True);
                Assert.That(Drive(ref fresh.Machine, next), Is.EqualTo(new[] { "b:Tick" }),
                    "No guard precedes b in the new order; it is resumed.");
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [TestCase(V1Reactive, Retaining)]
        [TestCase(Retaining, V1Reactive)]
        public void ChangingBetweenV1AndRetaining_IsIncompatible_AndTheRestartedSubtreeRunsTheNewSemantics(string from, string to)
        {
            // outer = memory-sequence [c, comp [a, b]]: c runs while comp changes type, so the
            // active path does not cross the restarting subtree and the rest of the state migrates.
            var program = Tree(from, reorder: false, compLast: true);
            var next = Tree(to, reorder: false, compLast: true);
            var comp = new NodeId("comp");
            var classification = HotReloadCompatibilityClassifier.Classify(program, next);
            Assert.That(classification.NodeVerdicts[comp].Category, Is.EqualTo(HotReloadNodeVerdictCategory.IncompatibleRestart));
            Assert.That(classification.RestartSubtreeRootNodeIds, Is.EquivalentTo(new[] { comp }), "The smallest enclosing subtree restarts.");
            Assert.That(classification.RequiresFullRestart, Is.False);

            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var old, out var failure), Is.True, failure.Code.ToString());
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Assert.That(old.Machine.TryBeginUpdate(1, 10, out _), Is.True);
                Assert.That(Drive(ref old.Machine, program, id => NodeStatus.Running), Is.EqualTo(new[] { "c:Enter", "c:Tick" }));

                var excluded = new List<NodeId> { comp, new NodeId("a"), new NodeId("b") };
                Assert.That(NativeHotReloadStateMigration.TryMigrate(old, program, next, classification, excluded,
                    Allocator.Persistent, out fresh, out var report, out failure), Is.True, failure.Code.ToString());
                Assert.That(report.ResetNodeCount, Is.EqualTo(3u), "comp and its children start from defaults.");
                Assert.That(fresh.Bindings[(int)Index(next, "comp")].Kind, Is.EqualTo(Kind(to)));

                // c finishes; comp is entered for the first time with its new kind.
                Assert.That(fresh.Machine.TryBeginUpdate(2, 20, out _), Is.True);
                Assert.That(Drive(ref fresh.Machine, next, RunningB), Is.EqualTo(new[] {
                    "c:Tick", "c:Exit:Success", "a:Enter", "a:Tick", "a:Exit:Success", "b:Enter", "b:Tick" }));
                Assert.That(fresh.Machine.TryBeginUpdate(3, 30, out _), Is.True);
                AssertSecondUpdate(to, Drive(ref fresh.Machine, next, RunningB));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [Test]
        public void ARetainingCompositeOnTheActivePath_ChangedToV1_IsRestartedThroughTheFullRestart()
        {
            // The retained branch b is on the active path through the restarting subtree: state
            // migration refuses (ADR-P7-011 scope) and the caller restarts; the restart aborts the
            // retained branch and builds the v1 kind.
            var program = Tree(Retaining, reorder: false);
            var next = Tree(V1Reactive, reorder: false);
            var comp = new NodeId("comp");
            var classification = HotReloadCompatibilityClassifier.Classify(program, next);
            Assert.That(classification.NodeVerdicts[comp].Category, Is.EqualTo(HotReloadNodeVerdictCategory.IncompatibleRestart));

            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var old, out var failure), Is.True, failure.Code.ToString());
            var restarted = false;
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Assert.That(old.Machine.TryBeginUpdate(1, 10, out _), Is.True);
                Drive(ref old.Machine, program, RunningB);
                var excluded = new List<NodeId> { comp, new NodeId("a"), new NodeId("b") };
                Assert.That(NativeHotReloadStateMigration.TryMigrate(old, program, next, classification, excluded,
                    Allocator.Persistent, out _, out _, out _), Is.False);
                Assert.That(NativeHotReloadFullRestart.TryRestart(old, next, 2, Allocator.Persistent, out fresh, out var report, out failure),
                    Is.True, failure.Code.ToString());
                restarted = true;
                Assert.That(report.OldInstanceWasAborted, Is.True);
                Assert.That(fresh.Bindings[(int)Index(next, "comp")].Kind, Is.EqualTo(NativeLifecycleNodeKindV1.ReactiveSequence));

                Assert.That(fresh.Machine.TryBeginUpdate(1, 10, out _), Is.True);
                Assert.That(Drive(ref fresh.Machine, next, RunningB), Is.EqualTo(new[] {
                    "a:Enter", "a:Tick", "a:Exit:Success", "b:Enter", "b:Tick" }));
                Assert.That(fresh.Machine.TryBeginUpdate(2, 20, out _), Is.True);
                AssertSecondUpdate(V1Reactive, Drive(ref fresh.Machine, next, RunningB));
            }
            finally
            {
                fresh.Dispose();
                if (!restarted) old.Dispose(); // TryRestart disposes the old instance on success.
            }
        }

        [Test]
        [Ignore("Finding F-S2-2 (pre-existing, v1, test-only API): NativeHotReloadFullRestart.TryRestart begins an update and "
            + "then requests a HotReload abort, but a v1 reactive composite with a running branch has already scheduled its "
            + "reactive replacement at update start, so TryRequestAbort refuses (NativeLifetimeStateInvalid). Kept as evidence; "
            + "v1 behaviour is out of scope for ADR-P10-013.")]
        public void AV1ReactiveCompositeWithARunningBranch_CanBeFullyRestarted()
        {
            var program = Tree(V1Reactive, reorder: false);
            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var old, out var failure), Is.True, failure.Code.ToString());
            Assert.That(old.Machine.TryBeginUpdate(1, 10, out _), Is.True);
            Drive(ref old.Machine, program, RunningB);
            if (!NativeHotReloadFullRestart.TryRestart(old, program, 2, Allocator.Persistent, out var fresh, out _, out failure))
            {
                old.Dispose();
                Assert.Fail(failure.Code.ToString());
            }
            fresh.Dispose();
        }

        private static void AssertSecondUpdate(string kind, List<string> calls)
        {
            if (kind == Retaining)
                Assert.That(calls, Is.EqualTo(new[] { "a:Enter", "a:Tick", "a:Exit:Success", "b:Tick" }),
                    "Retaining: the running branch is resumed.");
            else
                Assert.That(calls, Is.EqualTo(new[] {
                    "b:Abort:Explicit", "b:Exit:Aborted", "a:Enter", "a:Tick", "a:Exit:Success", "b:Enter", "b:Tick" }),
                    "v1: the running branch is aborted and entered again.");
        }

        private static NodeStatus RunningB(string id) => id == "b" ? NodeStatus.Running : NodeStatus.Success;

        private static NativeLifecycleNodeKindV1 Kind(string type)
            => type == Retaining ? NativeLifecycleNodeKindV1.RetainingReactiveSequence : NativeLifecycleNodeKindV1.ReactiveSequence;

        private static uint Index(CompiledProgram program, string id)
            => program.DebugMap.Single(entry => entry.AuthoringNodeId.ToString() == id).RuntimeNodeIndex;

        private static List<string> Drive(ref NativeLifecycleMachineV1 machine, CompiledProgram program)
            => Drive(ref machine, program, RunningB);

        private static List<string> Drive(ref NativeLifecycleMachineV1 machine, CompiledProgram program, Func<string, NodeStatus> tick)
        {
            var calls = new List<string>();
            for (var guard = 0; guard < 128; guard++)
            {
                Assert.That(machine.TryAdvance(out var step, out var failure), Is.True, failure.Code.ToString());
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    var id = program.DebugMap[(int)step.NodeIndex].AuthoringNodeId.ToString();
                    var suffix = step.Phase == BurstCallbackPhase.Abort ? ":" + step.AbortReason
                        : step.Phase == BurstCallbackPhase.Exit ? ":" + step.ExitReason : "";
                    calls.Add(id + ":" + step.Phase + suffix);
                    var status = step.Phase != BurstCallbackPhase.Tick ? NodeStatus.Running : tick(id);
                    Assert.That(machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, status, out failure),
                        Is.True, failure.Code.ToString());
                }
                else if (step.Kind == NativeLifecycleStepKindV1.Completed || step.Kind == NativeLifecycleStepKindV1.Waiting) return calls;
            }
            Assert.Fail("No execution boundary reached.");
            return calls;
        }

        // outer = memory-sequence [comp [a, b] (or [b, a]), c], or [c, comp] with compLast;
        // compiled indices in preorder.
        private static CompiledProgram Tree(string compType, bool reorder, bool compLast = false)
        {
            var spec = new List<(string Id, ulong Type, uint Memory, string[] Children)>
            {
                ("outer", StableHash.Fnv1A64("aibt.core.memory-sequence"), 4u, compLast ? new[] { "c", "comp" } : new[] { "comp", "c" }),
            };
            if (compLast) spec.Add(("c", NativeHotReloadTestProgram.LeafATypeId, 4u, Array.Empty<string>()));
            spec.Add(("comp", StableHash.Fnv1A64(compType), compType == Retaining ? 8u : 4u, reorder ? new[] { "b", "a" } : new[] { "a", "b" }));
            if (reorder) spec.Add(("b", NativeHotReloadTestProgram.LeafBTypeId, 4u, Array.Empty<string>()));
            spec.Add(("a", NativeHotReloadTestProgram.LeafATypeId, 4u, Array.Empty<string>()));
            if (!reorder) spec.Add(("b", NativeHotReloadTestProgram.LeafBTypeId, 4u, Array.Empty<string>()));
            if (!compLast) spec.Add(("c", NativeHotReloadTestProgram.LeafATypeId, 4u, Array.Empty<string>()));

            var index = spec.Select((item, position) => (item.Id, position)).ToDictionary(item => item.Id, item => (uint)item.position);
            var nodes = new CompiledNodeRecord[spec.Count];
            var children = new List<uint>();
            var debug = new CompiledDebugMapEntry[spec.Count];
            uint memory = 0;
            for (var position = 0; position < spec.Count; position++)
            {
                var item = spec[position];
                var range = item.Children.Length == 0 ? default : new CompiledRange((uint)children.Count, (uint)item.Children.Length);
                children.AddRange(item.Children.Select(child => index[child]));
                nodes[position] = new CompiledNodeRecord(item.Type, 1, 0, 0, 1, memory, item.Memory, 4,
                    NodeMemoryLifetime.Activation, range, CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                    (uint)position, default, default);
                debug[position] = new CompiledDebugMapEntry((uint)position, new NodeId(item.Id), "test/" + item.Id);
                memory += item.Memory;
            }

            CompiledProgram Build(CompiledHash hash) => new CompiledProgram(
                new CompiledProgramHeader(1, 1, new CompiledCompilerVersion(1, 0, 0, 0), Hash('a'), Hash('b'), Hash('c'), 1,
                    hash, 0, (uint)nodes.Length, (uint)children.Count, 0, (uint)debug.Length, 0, memory, 4, 0, true),
                nodes, children.ToArray(),
                Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<CompiledBlackboardSlotRecord>(), Array.Empty<CompiledObserverRecord>(),
                Array.Empty<uint>(), Array.Empty<byte>(), Array.Empty<byte>(), debug);
            return Build(CompiledProgramContentHashV1.Compute(Build(Hash('d'))));
        }

        private static CompiledHash Hash(char value) => new CompiledHash(new string(value, 64));
    }
}
