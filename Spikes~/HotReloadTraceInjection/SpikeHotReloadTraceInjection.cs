using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.HotReloadTraceInjectionSpike
{
    /// <summary>
    /// P6-020 disposable spike. Proves the recommended injection mechanism -- a new, purely
    /// additive <c>internal</c> overload on <see cref="HotReloadPreviewDriver"/>'s own shape
    /// accepting an <c>IReferenceTraceSink</c>, never a public facade change -- actually captures
    /// real trace records from a real <c>HotReloadStateMigration.Migrate</c>/
    /// <c>HotReloadFullRestart.Restart</c> pass. Mirrors <see cref="HotReloadPreviewDriver"/>'s own
    /// real code (copied here, not modified in place, per this card's own Forbidden-changes clause)
    /// with exactly one addition: a <c>traceSink</c> parameter threaded to the one call site that
    /// needs it. Archived to <c>Spikes~/HotReloadTraceInjection/</c> once proven.
    /// </summary>
    public sealed class SpikeHotReloadTraceInjection
    {
        [Test]
        public void Reload_CompatibleMigration_OnAnIdleInstance_CapturesRealTraceRecords()
        {
            var oldProgram = Compile(RepeaterTree(5));
            var oldMachine = CreateMachine(oldProgram, null);
            var completed = oldMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(completed.RootResult, Is.EqualTo(NodeStatus.Success),
                "precondition: a Repeater over an always-Success leaf completes within one tick");
            Assert.That(oldMachine.CaptureInspection().ActiveNodeCount, Is.EqualTo(0u),
                "precondition: the old instance must be idle so Migrate takes the real migration path, not the full-restart fallback");

            var sink = new SpikeCollectingTraceSink();
            var newProgram = Compile(RepeaterTree(5, stopOnFailure: false));
            var classification = HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram);

            var freshMachine = HotReloadStateMigration.Migrate(
                oldMachine, oldProgram, newProgram, classification,
                new ReferenceUpdateContext(2, new Revision(1), 0), new TreeInstanceId(1),
                ReferencePreviewFixtureEnvironment.CreateLeafRegistry(), sink,
                ReferencePreviewFixtureEnvironment.CreateMemoryCompositeRegistry(),
                ReferencePreviewFixtureEnvironment.CreateReactiveCompositeRegistry(),
                ReferencePreviewFixtureEnvironment.CreateDecoratorRegistry(),
                ReferencePreviewFixtureEnvironment.CreateParallelRegistry(),
                RegisteredBlackboardRegistry.Empty,
                ReferencePreviewFixtureEnvironment.CreateObserverRegistry(),
                out var report);

            Assert.That(report.FellBackToFullRestart, Is.False, "must exercise the real Migrate path, not the fallback, for this test to prove what it claims");

            // Real finding: Migrate/Restart thread the sink into the FRESH machine's own
            // constructor (mirroring HotReloadStateMigration.cs's own `new ReferenceExecutionMachine(
            // newProgram, ..., traceSink, ...)` call) -- it starts collecting once that machine
            // ticks, not during the migration procedure itself. One tick is enough to prove it is
            // genuinely wired, not merely accepted and discarded.
            freshMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(sink.Records.Count, Is.GreaterThan(0),
                "a caller-supplied sink must actually observe real records once the migrated instance ticks");
        }

        [Test]
        public void Reload_OfAnActiveInstance_FallsBackToFullRestart_AndTheSameSinkStillCapturesRealRecords()
        {
            var oldProgram = Compile(SequenceOverLeafTree(ReferenceFixtureNodeManifests.RunningTypeId));
            var oldMachine = CreateMachine(oldProgram, null);
            var active = oldMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(active.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting),
                "precondition: the old instance must actually be active (not idle) for this test to prove the fallback path");

            var sink = new SpikeCollectingTraceSink();
            var newProgram = Compile(SequenceOverLeafTree(ReferenceFixtureNodeManifests.RunningTypeId));
            var classification = HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram);

            var freshMachine = HotReloadStateMigration.Migrate(
                oldMachine, oldProgram, newProgram, classification,
                new ReferenceUpdateContext(2, new Revision(1), 0), new TreeInstanceId(1),
                ReferencePreviewFixtureEnvironment.CreateLeafRegistry(), sink,
                ReferencePreviewFixtureEnvironment.CreateMemoryCompositeRegistry(),
                ReferencePreviewFixtureEnvironment.CreateReactiveCompositeRegistry(),
                ReferencePreviewFixtureEnvironment.CreateDecoratorRegistry(),
                ReferencePreviewFixtureEnvironment.CreateParallelRegistry(),
                RegisteredBlackboardRegistry.Empty,
                ReferencePreviewFixtureEnvironment.CreateObserverRegistry(),
                out var report);

            // Real, disclosed finding: HotReloadStateMigration.Migrate owns the idle-vs-active
            // branch internally and forwards the SAME traceSink to HotReloadFullRestart.Restart
            // when it falls back -- confirmed here, not assumed. This means HotReloadPreviewDriver
            // needs exactly ONE injection point (its own call into Migrate), not a second one for
            // HotReloadFullRestart, since Migrate is the sole entry point the driver ever calls.
            Assert.That(report.FellBackToFullRestart, Is.True);

            freshMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(sink.Records.Count, Is.GreaterThan(0),
                "the same caller-supplied sink must also observe real records once the full-restart instance ticks");
        }

        // --- spike-local copies of HotReloadPreviewDriver's own real construction, unmodified in
        // shape -- proving the injection works against the exact code the real driver calls,
        // without touching Authoring/HotReload/HotReloadPreviewDriver.cs itself. ---

        private static ReferenceExecutionMachine CreateMachine(CompiledProgram program, AIBT.IReferenceTraceSink traceSink)
        {
            return new ReferenceExecutionMachine(
                program,
                new TreeInstanceId(1),
                ReferencePreviewFixtureEnvironment.CreateLeafRegistry(),
                traceSink,
                ReferencePreviewFixtureEnvironment.CreateMemoryCompositeRegistry(),
                ReferencePreviewFixtureEnvironment.CreateReactiveCompositeRegistry(),
                ReferencePreviewFixtureEnvironment.CreateDecoratorRegistry(),
                ReferencePreviewFixtureEnvironment.CreateParallelRegistry(),
                RegisteredBlackboardRegistry.Empty,
                ReferencePreviewFixtureEnvironment.CreateObserverRegistry());
        }

        private static CompiledProgram Compile(TreeDocument document)
        {
            var registry = ReferencePreviewFixtureEnvironment.CreateNodeRegistry();
            var options = new ReferenceCompilerOptions(
                "trees/p6-020-spike.aibt.json", ReferenceCompilationPolicy.Phase1,
                new CompiledCompilerVersion(1, 0, 0, 1));
            var result = ReferenceCompiler.Compile(document, registry, options);
            Assert.That(result.Success, Is.True,
                string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            return result.Program;
        }

        private static TreeDocument SequenceOverLeafTree(string leafTypeId)
        {
            var leaf = Node("leaf", leafTypeId);
            var root = new NodeDocument(
                new NodeId("root"), BuiltInNodeManifests.MemorySequenceTypeId, 1, new[] { new NodeId("leaf") },
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.p6-020-spike"), "Spec", root.Id, new[] { root, leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument RepeaterTree(uint count, bool stopOnFailure = true)
        {
            var parameters = new SemanticObject(new[]
            {
                new SemanticProperty("stopOnFailure", SemanticValue.FromBoolean(stopOnFailure)),
                new SemanticProperty("count", SemanticValue.FromUInt64(count)),
            });
            var root = new NodeDocument(
                new NodeId("root"), BuiltInNodeManifests.RepeaterTypeId, 1,
                new[] { new NodeId("leaf") }, parameters, tags: TagSet.Empty);
            var leaf = Node("leaf", ReferenceFixtureNodeManifests.SuccessTypeId);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.p6-020-spike"), "Spec", root.Id, new[] { root, leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static NodeDocument Node(string id, string typeId) =>
            new NodeDocument(
                new NodeId(id), typeId, 1, Array.Empty<NodeId>(),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
    }

    /// <summary>
    /// Spike-only sink mirroring the exact shape <c>Authoring/BehaviorCases/
    /// AuthoringBehaviorCaseExecutorFactory.cs</c>'s own already-accepted <c>CollectingTraceSink</c>
    /// uses -- proof that a benchmark-owning caller can implement this internal interface the same
    /// way an existing accepted card already does, needing no new mechanism beyond an
    /// <c>InternalsVisibleTo</c> grant naming that caller's own assembly.
    /// </summary>
    internal sealed class SpikeCollectingTraceSink : AIBT.IReferenceTraceSink
    {
        internal readonly List<ReferenceTraceRecord> Records = new List<ReferenceTraceRecord>();

        public void Record(in ReferenceTraceRecord record) => Records.Add(record);
    }
}
