using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Editor.Validation;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Validation
{
    public sealed class DiagnosticGraphPresentationTests
    {
        [Test]
        public void ChildPolicyAndParameterTypeDiagnosticsResolveToStableNodeAndFieldLocations()
        {
            var (registry, options) = BuildRegistryAndOptions();
            var tree = ParseFixture();

            // Break ChildPolicy on "guard" (aibt.core.inverter requires exactly 1 child) by
            // removing its only child outright -- Disconnect alone would leave "leaf1" present
            // but unreachable, which the validator flags with its own extra diagnostic and would
            // make this fixture test two unrelated things at once.
            var broken = SemanticEditOperations.RemoveNode(tree, new NodeId("leaf1"));
            // Break ParameterType on "leaf2" (its "enabled" parameter is Boolean).
            broken = SemanticEditOperations.SetParameter(broken, new NodeId("leaf2"), "enabled", SemanticValue.FromInt64(5));

            var compileResult = ReferenceCompiler.Compile(broken, registry, options);
            Assert.That(compileResult.Success, Is.False, "the tree is deliberately broken.");

            var summary = DiagnosticGraphSummary.Build(compileResult.Diagnostics);

            Assert.That(summary.ErrorCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(summary.Markers, Has.None.Matches<DiagnosticGraphLocation>(m => m.Kind == DiagnosticGraphLocationKind.Document),
                "Every diagnostic in this fixture is node-scoped; none should fall through to Document.");

            var guardMarker = summary.Markers.SingleOrDefault(m => m.Kind == DiagnosticGraphLocationKind.Node && m.NodeId == new NodeId("guard"));
            Assert.That(guardMarker, Is.Not.Null, "The ChildPolicy violation on 'guard' must resolve to a stable node location.");

            var leaf2Marker = summary.Markers.SingleOrDefault(m => m.Kind == DiagnosticGraphLocationKind.Field && m.NodeId == new NodeId("leaf2"));
            Assert.That(leaf2Marker, Is.Not.Null, "The ParameterType violation on 'leaf2' must resolve to a stable node+field location.");
            Assert.That(leaf2Marker.FieldName, Is.EqualTo("enabled"));

            Assert.That(summary.NodesWithMarkers(), Is.EquivalentTo(new[] { new NodeId("guard"), new NodeId("leaf2") }));
        }

        [Test]
        public void ATreeWithZeroDiagnosticsShowsNoMarkers()
        {
            var (registry, options) = BuildRegistryAndOptions();
            var tree = ParseFixture();

            var compileResult = ReferenceCompiler.Compile(tree, registry, options);
            Assert.That(compileResult.Success, Is.True, DiagnosticsText(compileResult.Diagnostics));

            var summary = DiagnosticGraphSummary.Build(compileResult.Diagnostics);

            Assert.That(summary.TotalCount, Is.EqualTo(0));
            Assert.That(summary.Markers, Is.Empty);
            Assert.That(summary.NodesWithMarkers(), Is.Empty);
        }

        [Test]
        public void FixingTheUnderlyingIssueClearsTheMarkerWithoutAnyManualRefreshStep()
        {
            var (registry, options) = BuildRegistryAndOptions();
            var tree = ParseFixture();

            var broken = SemanticEditOperations.Disconnect(tree, new NodeId("guard"), new NodeId("leaf1"));
            var brokenResult = ReferenceCompiler.Compile(broken, registry, options);
            Assert.That(brokenResult.Success, Is.False);
            var brokenSummary = DiagnosticGraphSummary.Build(brokenResult.Diagnostics);
            Assert.That(brokenSummary.NodesWithMarkers(), Does.Contain(new NodeId("guard")));

            // The fix: reconnect. DiagnosticGraphSummary.Build is always called fresh against
            // whatever ReferenceCompiler.Compile returns for the *current* document -- there is
            // no cache to invalidate, so the marker simply does not appear this time.
            var fixedDocument = SemanticEditOperations.Connect(broken, new NodeId("guard"), new NodeId("leaf1"));
            var fixedResult = ReferenceCompiler.Compile(fixedDocument, registry, options);
            Assert.That(fixedResult.Success, Is.True, DiagnosticsText(fixedResult.Diagnostics));
            var fixedSummary = DiagnosticGraphSummary.Build(fixedResult.Diagnostics);

            Assert.That(fixedSummary.TotalCount, Is.EqualTo(0));
            Assert.That(fixedSummary.NodesWithMarkers(), Is.Empty);
        }

        private static TreeDocument ParseFixture()
        {
            var path = EditorTestPackagePaths.Resolve("Tests", "Editor", "Validation", "Fixtures", "validation-ux.aibt.json");
            var result = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
            Assert.That(result.Success, Is.True);
            return result.Document;
        }

        // Fully qualified: this file's namespace (AIBT.Tests.Editor.Validation) is nested under
        // AIBT.Tests.Editor, which also contains the sibling namespace AIBT.Tests.Editor.NodeRegistry
        // -- an unqualified "NodeRegistry" resolves to that namespace instead of
        // AIBT.Authoring.NodeRegistry (CS0118), same recurring issue as P3-003/P3-006/P3-007.
        private static (AIBT.Authoring.NodeRegistry Registry, ReferenceCompilerOptions Options) BuildRegistryAndOptions()
        {
            var leafManifest = new NodeManifest(
                "aibt.core.test-validation-leaf",
                1,
                "A test leaf action with a parameter.",
                "Test",
                NodeBehaviorKind.Action,
                "Use in validation UX tests.",
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

            var buildResult = NodeRegistryBuilder.CreateWithBuiltIns()
                .AddTestFixtures()
                .AddBuiltInForTest(leafManifest, new NodeHandlerBindingContract("aibt.reference.test-validation-leaf", leafManifest.Version, leafManifest.ExecutionDomain))
                .Build();
            Assert.That(buildResult.Success, Is.True, DiagnosticsText(buildResult.Diagnostics));

            var options = new ReferenceCompilerOptions(
                "trees/p3-008-validation-ux.aibt.json",
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
