using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Editing
{
    public sealed class SemanticEditTransactionTests
    {
        [Test]
        public void SequenceOfEditsProducesCanonicalBytesIdenticalToHandAuthoring()
        {
            var (registry, options) = BuildRegistryAndOptions();

            var root = SemanticEditOperationsTests.NewNode("root", "aibt.core.memory-sequence");
            var viaEdits = SemanticEditOperationsTests.NewTree(new[] { root });

            var guard = SemanticEditOperationsTests.NewNode("guard", "aibt.core.inverter");
            viaEdits = SemanticEditOperations.AddNode(viaEdits, guard, new NodeId("root"));

            var leaf = SemanticEditOperationsTests.NewNode("leaf", "aibt.core.test-leaf", parameters: SemanticEditOperationsTests.OneBooleanParameter(true));
            viaEdits = SemanticEditOperations.AddNode(viaEdits, leaf, new NodeId("guard"));

            var handAuthoredRoot = SemanticEditOperationsTests.NewNode("root", "aibt.core.memory-sequence", children: new[] { new NodeId("guard") });
            var handAuthoredGuard = SemanticEditOperationsTests.NewNode("guard", "aibt.core.inverter", children: new[] { new NodeId("leaf") });
            var handAuthoredLeaf = SemanticEditOperationsTests.NewNode("leaf", "aibt.core.test-leaf", parameters: SemanticEditOperationsTests.OneBooleanParameter(true));
            var handAuthored = SemanticEditOperationsTests.NewTree(new[] { handAuthoredRoot, handAuthoredGuard, handAuthoredLeaf });

            var viaEditsBytes = CanonicalTreeJson.Serialize(viaEdits);
            var handAuthoredBytes = CanonicalTreeJson.Serialize(handAuthored);
            Assert.That(viaEditsBytes.Success, Is.True, DiagnosticsText(viaEditsBytes.Diagnostics));
            Assert.That(handAuthoredBytes.Success, Is.True, DiagnosticsText(handAuthoredBytes.Diagnostics));
            Assert.That(viaEditsBytes.Utf8, Is.EqualTo(handAuthoredBytes.Utf8));

            var compileResult = ReferenceCompiler.Compile(viaEdits, registry, options);
            Assert.That(compileResult.Success, Is.True, DiagnosticsText(compileResult.Diagnostics));
        }

        [Test]
        public void InvalidEditIsRejectedWithTheSameDiagnosticAnOutOfBandValidationPassWouldProduce()
        {
            var (registry, options) = BuildRegistryAndOptions();
            var before = SemanticEditOperationsTests.ThreeLevelTree();

            // Detaching the inverter's only child violates its ChildPolicy(1, 1, ...).
            var result = SemanticEditTransaction.Apply(
                before,
                document => SemanticEditOperations.Disconnect(document, new NodeId("guard"), new NodeId("leaf")),
                registry,
                options);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Document, Is.EqualTo(before), "A rejected edit must leave the document unchanged.");

            var brokenDocument = SemanticEditOperations.Disconnect(before, new NodeId("guard"), new NodeId("leaf"));
            var outOfBandDiagnostics = TreeValidator.Validate(brokenDocument, registry);

            Assert.That(result.Diagnostics.Select(d => d.Code), Is.EquivalentTo(outOfBandDiagnostics.Select(d => d.Code)),
                "The transaction must surface exactly the diagnostics an independent TreeValidator.Validate pass would.");
        }

        [Test]
        public void UndoRedoCoversSemanticEdits()
        {
            var (registry, options) = BuildRegistryAndOptions();
            var initial = SemanticEditOperationsTests.ThreeLevelTree();
            var history = new SemanticEditHistory(initial);

            var afterSetParameter = Accept(SemanticEditTransaction.Apply(
                history.Current,
                document => SemanticEditOperations.SetParameter(document, new NodeId("leaf"), "enabled", SemanticValue.FromBoolean(false)),
                registry, options));
            history.Do(afterSetParameter);

            var leaf = SemanticEditOperationsTests.NewNode("leaf2", "aibt.core.test-leaf", parameters: SemanticEditOperationsTests.OneBooleanParameter(true));
            var afterAdd = Accept(SemanticEditTransaction.Apply(
                history.Current,
                document => SemanticEditOperations.AddNode(document, leaf, new NodeId("root")),
                registry, options));
            history.Do(afterAdd);

            Assert.That(history.Current.Nodes.Count, Is.EqualTo(4));

            var undoOnce = history.Undo();
            Assert.That(undoOnce.Nodes.Count, Is.EqualTo(3));

            var undoTwice = history.Undo();
            Assert.That(undoTwice.Nodes.Single(n => n.Id == new NodeId("leaf")).Parameters.TryGetValue("enabled", out var restored) && restored.TryGetBoolean(out var restoredFlag) && restoredFlag);
            Assert.That(history.CanUndo, Is.False);

            var redoOnce = history.Redo();
            Assert.That(redoOnce.Nodes.Single(n => n.Id == new NodeId("leaf")).Parameters.TryGetValue("enabled", out var flagAfterRedo) && flagAfterRedo.TryGetBoolean(out var flagValue) && !flagValue);

            var redoTwice = history.Redo();
            Assert.That(redoTwice.Nodes.Count, Is.EqualTo(4));
            Assert.That(history.CanRedo, Is.False);
        }

        private static TreeDocument Accept(SemanticEditResult result)
        {
            Assert.That(result.Accepted, Is.True, DiagnosticsText(result.Diagnostics));
            return result.Document;
        }

        // Fully qualified: this file's namespace (AIBT.Tests.Editor.Editing) is nested under
        // AIBT.Tests.Editor, which also contains the sibling namespace AIBT.Tests.Editor.NodeRegistry
        // -- an unqualified "NodeRegistry" resolves to that namespace instead of
        // AIBT.Authoring.NodeRegistry (CS0118), same issue as P3-003's adapter tests.
        private static (AIBT.Authoring.NodeRegistry Registry, ReferenceCompilerOptions Options) BuildRegistryAndOptions()
        {
            var leafManifest = new NodeManifest(
                "aibt.core.test-leaf",
                1,
                "A test leaf action.",
                "Test",
                NodeBehaviorKind.Action,
                "Use in semantic-edit tests.",
                "Do not use in production.",
                NodeExecutionDomain.Burst,
                true,
                new[] { new NodeParameterContract("enabled", NodeParameterType.Boolean, true) },
                new NodeChildPolicy(0, 0, true),
                System.Array.Empty<string>(),
                System.Array.Empty<string>(),
                System.Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Failure },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(1, 1, new[] { new NodeConfigurationField("enabled", 0, 1, 1) }),
                NodeCancellationMode.NotApplicable,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Success", "{\"enabled\":true}", "Returns success.") });

            // Phase 1's ReferenceCompiler only executes BuiltIn/TestFixture-sourced node types
            // (UserExtension manifests validate but have no reference-handler binding and cannot
            // compile -- AIBT3012). AddBuiltInForTest is the same internal test-only registration
            // path Authoring's own ReferenceCompilerTests uses for a bound, parameterized node.
            var buildResult = NodeRegistryBuilder.CreateWithBuiltIns()
                .AddBuiltInForTest(leafManifest, new NodeHandlerBindingContract("aibt.reference.test-leaf", leafManifest.Version, leafManifest.ExecutionDomain))
                .Build();
            Assert.That(buildResult.Success, Is.True, DiagnosticsText(buildResult.Diagnostics));

            var options = new ReferenceCompilerOptions(
                "trees/p3-006-test.aibt.json",
                ReferenceCompilationPolicy.Phase1,
                new CompiledCompilerVersion(1, 0, 0, 0));

            return (buildResult.Registry, options);
        }

        private static string DiagnosticsText(DiagnosticCollection diagnostics)
        {
            return diagnostics == null ? string.Empty : string.Join("; ", diagnostics.Select(d => d.Code.Value + ": " + d.Message));
        }
    }
}
