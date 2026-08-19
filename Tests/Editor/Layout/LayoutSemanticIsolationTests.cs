using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Editor.Layout;
using AIBT.Editor.Organization;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Layout
{
    /// <summary>
    /// P3-007: proves, as an executable regression test rather than a review convention, that a
    /// layout-only edit (any P3-005 manual-organization action, or a P3-004 auto-layout pass)
    /// never changes the compiled program -- and that a genuine P3-006 semantic edit does, so the
    /// comparison is not vacuously true.
    /// </summary>
    public sealed class LayoutSemanticIsolationTests
    {
        [Test]
        public void EveryManualOrganizationActionAndAutoLayoutLeaveTheCompiledProgramByteIdentical()
        {
            var (registry, options) = BuildRegistryAndOptions();
            var tree = ParseFixture();

            var before = ReferenceCompiler.Compile(tree, registry, options);
            Assert.That(before.Success, Is.True, DiagnosticsText(before.Diagnostics));

            // Exercise every P3-005 manual-organization action kind, plus a P3-004 auto-layout
            // pass -- none of these read or write TreeDocument at all (LayoutOrganizationOperations
            // and DeterministicAutoLayoutService only ever touch LayoutDocument), so this section
            // never assigns back into `tree`.
            var layout = DeterministicAutoLayoutService.Layout(tree);
            layout = LayoutOrganizationOperations.Pin(layout, new NodeId("leaf1"));
            layout = LayoutOrganizationOperations.AddOrUpdateGroup(layout, "g1", "Group", new[] { new NodeId("leaf1"), new NodeId("leaf2") });
            // "Sticky notes and free-form comments" are one schema concept in editor-layout-v1.md
            // (LayoutNote) -- one note call covers both named action kinds.
            layout = LayoutOrganizationOperations.AddOrUpdateNote(layout, "n1", "A comment / sticky note", new LayoutPoint(1, 2), new LayoutPoint(50, 30));
            layout = LayoutOrganizationOperations.AddOrUpdateReroute(layout, new NodeId("root"), new NodeId("guard"), new[] { new LayoutPoint(9, 9) });
            layout = DeterministicAutoLayoutService.Layout(tree, layout); // re-run auto-layout on the rest of the tree

            Assert.That(layout.Nodes[new NodeId("leaf1")].Pinned, Is.True, "sanity: the pin actually took effect on the layout document.");
            Assert.That(layout.Groups.ContainsKey("g1"), Is.True);
            Assert.That(layout.Notes.ContainsKey("n1"), Is.True);
            Assert.That(layout.Reroutes.Count, Is.EqualTo(1));

            var after = ReferenceCompiler.Compile(tree, registry, options);
            Assert.That(after.Success, Is.True, DiagnosticsText(after.Diagnostics));

            Assert.That(after.Program.Header.CompiledContentHash, Is.EqualTo(before.Program.Header.CompiledContentHash),
                "A layout-only edit must never change the compiled program.");
        }

        [Test]
        public void AGenuineSemanticEditDoesChangeTheCompiledProgram()
        {
            var (registry, options) = BuildRegistryAndOptions();
            var tree = ParseFixture();

            var before = ReferenceCompiler.Compile(tree, registry, options);
            Assert.That(before.Success, Is.True, DiagnosticsText(before.Diagnostics));

            var edited = SemanticEditOperations.Disconnect(tree, new NodeId("root"), new NodeId("leaf2"));
            edited = SemanticEditOperations.RemoveNode(edited, new NodeId("leaf2"));

            var after = ReferenceCompiler.Compile(edited, registry, options);
            Assert.That(after.Success, Is.True, DiagnosticsText(after.Diagnostics));

            Assert.That(after.Program.Header.CompiledContentHash, Is.Not.EqualTo(before.Program.Header.CompiledContentHash),
                "The comparison must be able to detect a real semantic difference -- otherwise the positive case is vacuous.");
        }

        private static TreeDocument ParseFixture()
        {
            var path = EditorTestPackagePaths.Resolve("Tests", "Editor", "Layout", "Fixtures", "isolation-proof.aibt.json");
            var result = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
            Assert.That(result.Success, Is.True);
            return result.Document;
        }

        // Fully qualified: this file's namespace (AIBT.Tests.Editor.Layout) is nested under
        // AIBT.Tests.Editor, which also contains the sibling namespace AIBT.Tests.Editor.NodeRegistry
        // -- an unqualified "NodeRegistry" resolves to that namespace instead of
        // AIBT.Authoring.NodeRegistry (CS0118), same recurring issue as P3-003/P3-006.
        private static (AIBT.Authoring.NodeRegistry Registry, ReferenceCompilerOptions Options) BuildRegistryAndOptions()
        {
            var buildResult = NodeRegistryBuilder.CreateWithBuiltIns().AddTestFixtures().Build();
            Assert.That(buildResult.Success, Is.True, DiagnosticsText(buildResult.Diagnostics));

            var options = new ReferenceCompilerOptions(
                "trees/p3-007-isolation-proof.aibt.json",
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
