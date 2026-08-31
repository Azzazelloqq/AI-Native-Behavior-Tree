using System;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.PerProjectLeafRegistrationSpike
{
    /// <summary>
    /// P6-017 disposable spike. Tests whether a genuinely new, project-authored leaf type (not a
    /// renamed copy of an existing <c>aibt.test.*</c> fixture) can be registered and ticked through
    /// the real, unmodified <see cref="ReferenceExecutionMachine"/>.
    /// <para>
    /// Real finding: <see cref="ReferenceLeafRegistry"/> and <see cref="ReferenceLeafBinding"/> are
    /// already fully general -- the constructor takes any <c>IEnumerable&lt;ReferenceLeafBinding&gt;</c>,
    /// and <c>CreatePhase1Fixtures()</c> is just one convenience factory among possible others
    /// (exactly like <c>ReferenceCompilationPolicy.Phase1</c>, per <c>P6-014</c>'s own finding), not a
    /// hardcoded gate. No engine change is needed to combine built-ins with a new handler.
    /// </para>
    /// <para>
    /// The real blocker is narrower and different in kind: <see cref="IReferenceLeafHandler"/> and
    /// <see cref="ReferenceNodeContext"/> are both <c>internal</c> to <c>AIBT.Runtime</c>. An
    /// arbitrary external Unity project's own assembly -- by definition not one of the small, fixed
    /// set of assemblies <c>Runtime/AssemblyInfo.cs</c> names in its <c>InternalsVisibleTo</c> grants
    /// -- cannot implement <c>IReferenceLeafHandler</c> at all, and could not safely consume
    /// <c>ReferenceNodeContext</c> (an <c>internal ref struct</c> exposing raw
    /// <c>ReadOnlySpan&lt;byte&gt;</c>/<c>Span&lt;byte&gt;</c> views) even if it could. This spike
    /// therefore proves the achievable-today subset (a new leaf type authored inside an assembly
    /// that already has the grant, exactly like every existing test fixture) and treats "arbitrary
    /// external project assembly" as the separate, larger, unresolved case the ADR discloses rather
    /// than assumes solved.
    /// </para>
    /// Archived to <c>Spikes~/PerProjectLeafRegistration/</c> once proven.
    /// </summary>
    public sealed class SpikePerProjectLeafRegistration
    {
        // AddBuiltInForTest (the only internal path this spike could reach a handler binding
        // through) hardcodes NodeManifestSource.BuiltIn, which NodeRegistryBuilder's own
        // ValidateSource requires to start with "aibt.core." -- an artifact of using this
        // test-only path, not a claim that a real project-authored leaf would ship under AIBT's own
        // reserved namespace. See the ADR: NodeManifestSource.UserExtension (the real per-project
        // path) is explicitly, deliberately rejected by ValidateBinding whenever a handler binding
        // is attached, proving the wall by direct evidence rather than assumption.
        private const string CustomLeafTypeId = "aibt.core.p6017-spike-counter";

        [Test]
        public void GenuinelyNewLeafType_RegisteredAlongsideBuiltIns_TicksCorrectlyThroughTheRealMachine()
        {
            // A genuinely new leaf, not a renamed aibt.test.* fixture: counts its own ticks in
            // activation memory and succeeds on the third tick -- real, distinguishable behavior no
            // existing fixture has.
            var customHandler = new DoublingCounterLeafHandler();
            var combinedRegistry = new ReferenceLeafRegistry(
                BuiltInFixtureBindings().Append(new ReferenceLeafBinding(
                    StableHash.Fnv1A64(CustomLeafTypeId), 1, customHandler)));

            var manifest = CustomLeafManifest();
            // AddUserExtension (the only PUBLIC registration method) never attaches a
            // NodeHandlerBindingContract, and NodeHandlerBindingContract itself is internal -- so no
            // public API path exists to make a user-extension manifest carry a reference-handler
            // binding at all. AddBuiltInForTest (internal, test-only) is used here to reach the
            // achievable-today state; see this class's own doc comment and the ADR for the
            // full, disclosed three-layer wall this proves.
            var nodeRegistry = NodeRegistryBuilder.CreateWithBuiltIns()
                .AddBuiltInForTest(manifest, new NodeHandlerBindingContract("p6017.reference.doubling-counter", 1, NodeExecutionDomain.Burst))
                .Build().Registry;
            var document = SingleLeafTree();
            var options = new ReferenceCompilerOptions(
                "trees/p6-017-spike.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0));
            var compilation = ReferenceCompiler.Compile(document, nodeRegistry, options);
            Assert.That(compilation.Success, Is.True,
                string.Join(" | ", compilation.Diagnostics.Select(d => d.Code + ": " + d.Message)));

            var machine = new ReferenceExecutionMachine(
                compilation.Program, new TreeInstanceId(1), combinedRegistry, null,
                ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                RegisteredBlackboardRegistry.Empty,
                ReferenceObserverConditionRegistry.Empty);

            var first = machine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(first.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            var second = machine.Update(new ReferenceUpdateContext(2, new Revision(1), 0));
            Assert.That(second.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            var third = machine.Update(new ReferenceUpdateContext(3, new Revision(1), 0));

            Assert.That(third.Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            Assert.That(third.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(customHandler.EnterCount, Is.EqualTo(1), "Enter must fire exactly once, real lifecycle behavior");
            Assert.That(customHandler.TickCount, Is.EqualTo(3));
            Assert.That(customHandler.ExitCount, Is.EqualTo(1));
        }

        [Test]
        public void GenuineUserExtensionPath_CanNeverCarryAReferenceHandlerBinding_ConfirmedByDirectFailure()
        {
            // The real per-project path: AddUserExtension, the only PUBLIC registration method.
            // It provides no parameter to attach a NodeHandlerBindingContract at all (the type
            // itself is internal), so a genuinely new, non-reserved-namespace leaf registered this
            // way can never compile through the Phase 1 reference compiler -- confirmed here by
            // direct failure with the exact same AIBT3012 diagnostic, not assumed from reading code.
            var userManifest = CustomLeafManifest("project.counter.doubling");
            var nodeRegistry = NodeRegistryBuilder.CreateWithBuiltIns().AddUserExtension(userManifest).Build().Registry;
            Assert.That(nodeRegistry.TryGet(userManifest.TypeId, out var entry), Is.True);
            Assert.That(entry.HasReferenceHandlerBinding, Is.False);

            var document = SingleLeafTree(userManifest.TypeId);
            var options = new ReferenceCompilerOptions(
                "trees/p6-017-spike-user-extension.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0));
            var compilation = ReferenceCompiler.Compile(document, nodeRegistry, options);

            Assert.That(compilation.Success, Is.False);
            Assert.That(compilation.Diagnostics.Any(d => d.Code == ReferenceCompilerDiagnosticCodes.UnsupportedCapability), Is.True,
                string.Join(" | ", compilation.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        }

        [Test]
        public void RegistryConstructorIsAlreadyFullyGeneral_NoEngineChangeNeededToCombineBindings()
        {
            // Confirms CreatePhase1Fixtures is one convenience factory, not a hardcoded gate --
            // mirroring P6-014's own finding about ReferenceCompilationPolicy.Phase1.
            var builtIns = BuiltInFixtureBindings().ToArray();
            Assert.That(builtIns.Length, Is.GreaterThan(0));

            var combined = new ReferenceLeafRegistry(
                builtIns.Append(new ReferenceLeafBinding(StableHash.Fnv1A64(CustomLeafTypeId), 1, new DoublingCounterLeafHandler())));

            Assert.That(combined.TryGet(StableHash.Fnv1A64("aibt.test.success"), 1, out _), Is.True);
            Assert.That(combined.TryGet(StableHash.Fnv1A64(CustomLeafTypeId), 1, out var custom), Is.True);
            Assert.That(custom, Is.InstanceOf<DoublingCounterLeafHandler>());
        }

        private static System.Collections.Generic.IEnumerable<ReferenceLeafBinding> BuiltInFixtureBindings()
        {
            // ReferenceLeafRegistry.CreatePhase1Fixtures() has no public way to enumerate its own
            // bindings back out, so this spike rebuilds the equivalent set directly -- a real,
            // disclosed limitation for a future implementation card (see the ADR's own Consequences).
            yield return new ReferenceLeafBinding(StableHash.Fnv1A64("aibt.test.success"), 1, new ConstantReferenceLeafHandlerProxy(NodeStatus.Success));
            yield return new ReferenceLeafBinding(StableHash.Fnv1A64("aibt.test.failure"), 1, new ConstantReferenceLeafHandlerProxy(NodeStatus.Failure));
            yield return new ReferenceLeafBinding(StableHash.Fnv1A64("aibt.test.running"), 1, new ConstantReferenceLeafHandlerProxy(NodeStatus.Running));
        }

        private static NodeManifest CustomLeafManifest(string typeId = CustomLeafTypeId)
        {
            var childPolicy = new NodeChildPolicy(0, 0, true);
            return new NodeManifest(
                typeId, 1, "Ticks 3 times then succeeds.", "Spike",
                NodeBehaviorKind.Action, "Proves per-project leaf registration.", "Never in production.",
                NodeExecutionDomain.Burst, true,
                Array.Empty<NodeParameterContract>(), childPolicy,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Running },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                NodeCancellationMode.AbortOnly, NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Third tick succeeds", "{}", "Succeeds on the third tick.") });
        }

        private static TreeDocument SingleLeafTree(string typeId = CustomLeafTypeId)
        {
            var leaf = new NodeDocument(
                new NodeId("leaf"), typeId, 1, Array.Empty<NodeId>(),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.p6-017-spike"), "Spec", leaf.Id, new[] { leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }
    }

    internal sealed class ConstantReferenceLeafHandlerProxy : IReferenceLeafHandler
    {
        private readonly NodeStatus _status;
        internal ConstantReferenceLeafHandlerProxy(NodeStatus status) => _status = status;
        public void Enter(ref ReferenceNodeContext context) { }
        public NodeStatus Tick(ref ReferenceNodeContext context) => _status;
        public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason) { }
        public void Exit(ref ReferenceNodeContext context, NodeExitReason reason) { }
    }

    /// <summary>
    /// A genuinely new, project-authored-style leaf: counts real ticks and succeeds on the third.
    /// Deliberately not a copy of any existing <c>aibt.test.*</c> fixture's behavior.
    /// </summary>
    internal sealed class DoublingCounterLeafHandler : IReferenceLeafHandler
    {
        internal int EnterCount { get; private set; }
        internal int TickCount { get; private set; }
        internal int ExitCount { get; private set; }

        public void Enter(ref ReferenceNodeContext context) => EnterCount++;

        public NodeStatus Tick(ref ReferenceNodeContext context)
        {
            TickCount++;
            return TickCount >= 3 ? NodeStatus.Success : NodeStatus.Running;
        }

        public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason)
        {
        }

        public void Exit(ref ReferenceNodeContext context, NodeExitReason reason) => ExitCount++;
    }
}
