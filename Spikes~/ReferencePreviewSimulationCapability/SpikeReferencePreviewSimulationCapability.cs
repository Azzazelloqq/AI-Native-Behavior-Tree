using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.ReferencePreviewSpike
{
    // Disposable P6-013 spike. Proves the recommended ReferencePreviewDriver widening (completions
    // injection, resume-with-step-budget, abort, caller-supplied TreeInstanceId) against the real,
    // unmodified ReferenceExecutionMachine, through a temporary spike-only facade mirroring the
    // driver's own incremental BeginTick/StepAtomic shape -- not the production file itself (this
    // card's own Forbidden changes). Archived to Spikes~/ReferencePreviewSimulationCapability/ and
    // Planning~/Evidence/P6-013/ after this session; deleted from Tests/ once archived, mirroring
    // P5-001's own SpikeHotReloadCompatibilityModel precedent.
    public sealed class SpikeReferencePreviewSimulationCapability
    {
        // ---- a minimal spike-only facade, mirroring exactly the widening this card recommends ----

        private sealed class SpikePreviewFacade
        {
            private readonly ReferenceExecutionMachine _machine;
            private ulong _updateId;
            private bool _hasOpenTick;

            internal SpikePreviewFacade(ReferenceExecutionMachine machine)
            {
                _machine = machine;
            }

            // Recommendation 1: an optional CompletionBatch parameter -- CompletionBatch is already
            // public (Runtime/Commands/CompletionContracts.cs), so this is pure facade surfacing.
            internal ReferenceExecutionEnvelope BeginTick(CompletionBatch completions = null, long timeMicroseconds = 0)
            {
                if (_hasOpenTick) throw new InvalidOperationException("A tick is already open.");
                _updateId++;
                var context = new ReferenceUpdateContext(_updateId, new Revision(_updateId), timeMicroseconds, completions);
                var envelope = _machine.BeginUpdate(context);
                _hasOpenTick = envelope.Progress == ReferenceExecutionProgress.Suspended;
                return envelope;
            }

            internal ReferenceExecutionEnvelope StepAtomic()
            {
                if (!_hasOpenTick) throw new InvalidOperationException("No tick is open.");
                var envelope = _machine.AdvanceOneStep();
                _hasOpenTick = envelope.Progress == ReferenceExecutionProgress.Suspended;
                return envelope;
            }

            // Recommendation 3: abort by RuntimeNodeIndex, always as an explicit, caller-initiated
            // abort -- NodeAbortReason itself stays internal; a real driver would hardcode
            // NodeAbortReason.Explicit internally (the only reason an external caller's abort could
            // ever mean) rather than widen the enum's own accessibility.
            //
            // Uses the Abort(update, reason, index) overload, not RequestAbort(reason, index) --
            // found live by this spike: RequestAbort requires an *already open* update
            // (_hasOpenUpdate) and is rejected outright once a tick has reached a Waiting boundary,
            // which is exactly the state a preview caller wants to cancel from. Abort(...) opens its
            // own fresh update context and runs the whole abort traversal to a real boundary in one
            // call, matching how a caller-driven "cancel the operation I'm currently waiting on"
            // action actually needs to work.
            internal ReferenceExecutionEnvelope Abort(RuntimeNodeIndex sourceNodeIndex, long timeMicroseconds = 0)
            {
                _updateId++;
                var context = new ReferenceUpdateContext(_updateId, new Revision(_updateId), timeMicroseconds);
                var envelope = _machine.Abort(context, NodeAbortReason.Explicit, sourceNodeIndex);
                _hasOpenTick = envelope.Progress == ReferenceExecutionProgress.Suspended;
                return envelope;
            }

            // Recommendation 4: TreeInstanceId is already public with a public constructor --
            // surfaced simply by threading the caller's own value into the machine constructor
            // instead of the driver's current hardcoded `new TreeInstanceId(1)`.
            internal static SpikePreviewFacade CreateWithInstanceId(TreeInstanceId instanceId, IReferenceTraceSink trace = null)
            {
                var program = SingleAsyncActionProgram();
                var machine = new ReferenceExecutionMachine(
                    program,
                    instanceId,
                    ReferenceLeafRegistry.CreatePhase1Fixtures(),
                    trace,
                    ReferencePreviewFixtureEnvironment.CreateMemoryCompositeRegistry(),
                    ReferencePreviewFixtureEnvironment.CreateReactiveCompositeRegistry(),
                    ReferencePreviewFixtureEnvironment.CreateDecoratorRegistry(),
                    ReferencePreviewFixtureEnvironment.CreateParallelRegistry(),
                    RegisteredBlackboardRegistry.Empty,
                    ReferencePreviewFixtureEnvironment.CreateObserverRegistry());
                return new SpikePreviewFacade(machine);
            }
        }

        // ---- 1. completions injection round trip ----------------------------------------------

        [Test]
        public void CompletionsInjectionRoundTrip()
        {
            var facade = SpikePreviewFacade.CreateWithInstanceId(new TreeInstanceId(1));

            var start = facade.BeginTick();
            while (start.Progress == ReferenceExecutionProgress.Suspended)
            {
                start = facade.StepAtomic();
            }

            Assert.That(start.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting), "The async action leaf must start and wait for a completion.");
            var operationId = start.Commands.Records.Single().OperationId;

            var completion = new CompletionRecord(operationId, CompletionOutcome.Succeeded, default, 0, 0, 1, 1, new Revision(1));
            var batch = new CompletionBatch(new[] { completion }, Array.Empty<byte>());

            var resumed = facade.BeginTick(completions: batch);
            while (resumed.Progress == ReferenceExecutionProgress.Suspended)
            {
                resumed = facade.StepAtomic();
            }

            Assert.That(resumed.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(resumed.RootResult, Is.EqualTo(NodeStatus.Success), "The caller-supplied CompletionBatch was actually consumed and mapped to the leaf's terminal status.");
        }

        // ---- 2. abort mid-tick ------------------------------------------------------------------

        [Test]
        public void AbortMidTick()
        {
            var facade = SpikePreviewFacade.CreateWithInstanceId(new TreeInstanceId(2));

            var start = facade.BeginTick();
            while (start.Progress == ReferenceExecutionProgress.Suspended)
            {
                start = facade.StepAtomic();
            }
            Assert.That(start.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting), "Precondition: the leaf is active and mid-flight before aborting it.");

            // Abort(update, reason, index) drives its own fresh update to a full boundary in one
            // call (ReferenceStepBudget.Unlimited by default), so no step loop is needed here --
            // unlike RequestAbort, which only opens the traversal and requires the caller to keep
            // stepping (and which this spike found cannot even be called once a tick reaches
            // Waiting -- see the facade's own Abort() comment).
            var aborted = facade.Abort(new RuntimeNodeIndex(0));

            Assert.That(aborted.Commands.Records.Count, Is.EqualTo(1));
            Assert.That(aborted.Commands.Records[0].Phase, Is.EqualTo(CommandPhase.Cancel), "Abort() actually reached the real machine and produced a real cancel command, not a no-op.");
        }

        // ---- 3. resume after a step-budget yield -------------------------------------------------

        [Test]
        public void ResumeAfterStepBudgetYield()
        {
            var facade = new SpikePreviewFacadeForBudget();

            var withBudget = facade.UpdateWithBudget(1, stepLimit: 1);
            Assert.That(withBudget.Progress, Is.EqualTo(ReferenceExecutionProgress.Suspended), "A 1-step budget on a multi-step tick must yield, not run to completion.");

            var resumed = facade.Resume(stepLimit: null);
            Assert.That(resumed.Progress, Is.Not.EqualTo(ReferenceExecutionProgress.Suspended), "Resume(unlimited) must actually continue the same suspended tick to a real boundary.");
            Assert.That(resumed.RootResult, Is.EqualTo(NodeStatus.Success));
        }

        private sealed class SpikePreviewFacadeForBudget
        {
            private readonly ReferenceExecutionMachine _machine;

            internal SpikePreviewFacadeForBudget()
            {
                // Root sequence -> child sequence -> aibt.test.success leaf: a real TreeDocument
                // compiled through the exact same ReferenceCompiler.Compile + fixture registry the
                // driver itself uses (ReferencePreviewFixtureEnvironment.CreateNodeRegistry()),
                // giving Enter/Tick/Exit through 3 real nodes -- more than a 1-step budget can
                // finish in one call, so a real yield/resume boundary actually exists to test.
                var leaf = new NodeDocument(new NodeId("leaf"), ReferenceFixtureNodeManifests.SuccessTypeId, 1, Array.Empty<NodeId>(), parameters: SemanticObject.Empty, tags: TagSet.Empty);
                var child = new NodeDocument(new NodeId("child"), BuiltInNodeManifests.MemorySequenceTypeId, 1, new[] { new NodeId("leaf") }, parameters: SemanticObject.Empty, tags: TagSet.Empty);
                var root = new NodeDocument(new NodeId("root"), BuiltInNodeManifests.MemorySequenceTypeId, 1, new[] { new NodeId("child") }, parameters: SemanticObject.Empty, tags: TagSet.Empty);
                var document = new TreeDocument(
                    TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                    new TreeId("tree.p6013-spike-budget"), "P6-013 Spike", root.Id, new[] { root, child, leaf },
                    tags: TagSet.Empty, metadata: SemanticObject.Empty);

                var registry = ReferencePreviewFixtureEnvironment.CreateNodeRegistry();
                var options = new ReferenceCompilerOptions("p6013-spike", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 1));
                var compilation = ReferenceCompiler.Compile(document, registry, options);
                Assert.That(compilation.Success, Is.True, "Spike precondition: the nested-sequence fixture tree must compile against the driver's own real fixture registry.");

                _machine = new ReferenceExecutionMachine(
                    compilation.Program,
                    new TreeInstanceId(3),
                    ReferencePreviewFixtureEnvironment.CreateLeafRegistry(),
                    null,
                    ReferencePreviewFixtureEnvironment.CreateMemoryCompositeRegistry(),
                    ReferencePreviewFixtureEnvironment.CreateReactiveCompositeRegistry(),
                    ReferencePreviewFixtureEnvironment.CreateDecoratorRegistry(),
                    ReferencePreviewFixtureEnvironment.CreateParallelRegistry(),
                    RegisteredBlackboardRegistry.Empty,
                    ReferencePreviewFixtureEnvironment.CreateObserverRegistry());
            }

            internal ReferenceExecutionEnvelope UpdateWithBudget(ulong updateId, ulong stepLimit)
                => _machine.Update(new ReferenceUpdateContext(updateId, new Revision(updateId), 0), ReferenceStepBudget.Limited(stepLimit));

            internal ReferenceExecutionEnvelope Resume(ulong? stepLimit)
                => _machine.Resume(stepLimit.HasValue ? ReferenceStepBudget.Limited(stepLimit.Value) : ReferenceStepBudget.Unlimited);
        }

        // ---- 4. two concurrent sessions with distinct TreeInstanceIds not interfering -----------

        [Test]
        public void TwoConcurrentSessionsWithDistinctInstanceIdsDoNotInterfere()
        {
            var traceA = new RecordingTraceSink();
            var traceB = new RecordingTraceSink();
            var sessionA = SpikePreviewFacade.CreateWithInstanceId(new TreeInstanceId(101), traceA);
            var sessionB = SpikePreviewFacade.CreateWithInstanceId(new TreeInstanceId(202), traceB);

            var stepA = sessionA.BeginTick();
            while (stepA.Progress == ReferenceExecutionProgress.Suspended) stepA = sessionA.StepAtomic();
            var stepB = sessionB.BeginTick();
            while (stepB.Progress == ReferenceExecutionProgress.Suspended) stepB = sessionB.StepAtomic();

            Assert.That(stepA.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(stepB.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));

            // Complete only session A's operation; session B must remain entirely unaffected.
            var operationA = stepA.Commands.Records.Single().OperationId;
            var completionA = new CompletionBatch(
                new[] { new CompletionRecord(operationA, CompletionOutcome.Succeeded, default, 0, 0, 1, 1, new Revision(1)) },
                Array.Empty<byte>());

            var resumedA = sessionA.BeginTick(completions: completionA);
            while (resumedA.Progress == ReferenceExecutionProgress.Suspended) resumedA = sessionA.StepAtomic();

            Assert.That(resumedA.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(traceB.Records.Count(r => r.Kind == ReferenceTraceEventKind.CompletionConsumed), Is.Zero,
                "Session B's own trace must show zero completions consumed -- session A's completion never crossed instance boundaries.");
            Assert.That(operationA.TreeInstanceId, Is.EqualTo(new TreeInstanceId(101)), "The caller-supplied TreeInstanceId flows all the way into emitted OperationIds.");
        }

        private sealed class RecordingTraceSink : IReferenceTraceSink
        {
            private readonly List<ReferenceTraceRecord> _records = new List<ReferenceTraceRecord>();
            internal IReadOnlyList<ReferenceTraceRecord> Records => _records;
            public void Record(in ReferenceTraceRecord record) => _records.Add(record);
        }

        // ---- shared async-action fixture program (hand-built CompiledProgram, mirroring the real
        // ReferenceAsyncLifecycleTests.cs's own AsyncProgram.CreateProgram technique exactly -- the
        // driver's own fixture node-registry has no manifest for the async-action leaf type, so
        // this cannot be compiled through the normal TreeDocument/ReferenceCompiler path; only a
        // hand-built CompiledProgram can exercise it, exactly as the existing accepted test does) --

        private static readonly CompiledHash Hash = new CompiledHash(new string('c', CompiledHash.HexLength));

        private static CompiledProgram SingleAsyncActionProgram()
        {
            var node = new CompiledNodeRecord(
                StableHash.Fnv1A64(ReferenceAsyncActionHandler.TypeId),
                1, 0, 2, 1, 0,
                16, 8, NodeMemoryLifetime.Activation,
                new CompiledRange(0, 0),
                CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                CompiledIndex.Invalid,
                new CompiledRange(0, 0), new CompiledRange(0, 0));
            var header = new CompiledProgramHeader(
                1, 1, new CompiledCompilerVersion(1, 0, 0, 0),
                Hash, Hash, Hash, 1, Hash,
                0, 1, 0, 0, 0, 2, 16, 8, 0, true);
            return new CompiledProgram(
                header, new[] { node },
                Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<uint>(),
                Array.Empty<CompiledBlackboardSlotRecord>(),
                Array.Empty<CompiledObserverRecord>(), Array.Empty<uint>(),
                new byte[] { 7, 8 }, Array.Empty<byte>(), Array.Empty<CompiledDebugMapEntry>());
        }
    }
}
