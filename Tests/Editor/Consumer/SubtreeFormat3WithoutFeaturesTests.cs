using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using NUnit.Framework;

namespace AIBT.Tests.Consumer
{
    /// <summary>
    /// P11-007 (review F2): a tree in format 3 that has no reference node and no interface (after
    /// Unpack, after its last reference was deleted, or written so) compiles natively. It is its own
    /// expansion, and an expansion is a format-2 document (ADR-P11-001 §4.4 step 5), so its program's
    /// semantic hash equals the same tree's written as format 2 (§4.5). Before the fix the native
    /// compiler refused it with AIBT2042 twice.
    /// </summary>
    public sealed class SubtreeFormat3WithoutFeaturesTests
    {
        private static string Tree(int formatVersion) => Q(@"{ 'format': 'aibt.tree', 'formatVersion': " + formatVersion + @", 'treeId': 'tests.p11007.plain', 'name': 'P11-007 plain', 'root': 'root',
  'blackboard': { 'distance': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 } },
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['near'] },
    'near': { 'type': 'aibt.tests.consumer.radius', 'typeVersion': 1, 'parameters': { 'radius': 1.5 }, 'bindings': { 'distance': 'distance' } } } }");

        private const string ChildSourceId = "trees/p11007-child.aibt";
        private const string ParentSourceId = "trees/p11007-parent.aibt";

        private static readonly string ChildJson = Q(@"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11007.child', 'name': 'P11-007 child', 'root': 'root',
  'interface': { 'version': 1, 'keys': ['distance'] },
  'blackboard': {
    'distance': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 },
    'range': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 3.0 } },
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['near', 'far'] },
    'near': { 'type': 'aibt.tests.consumer.radius', 'typeVersion': 1, 'parameters': { 'radius': 1.5 }, 'bindings': { 'distance': 'distance' } },
    'far': { 'type': 'aibt.tests.consumer.radius', 'typeVersion': 1, 'parameters': { 'radius': 2.5 }, 'bindings': { 'distance': 'range' } } } }");

        [Test]
        public void Format3WithoutReferenceOrInterface_CompilesNatively_WithTheHashOfTheSameTreeAsFormat2()
        {
            var v2 = Parse(Tree(2), "trees/plain.aibt");
            var v3 = Parse(Tree(3), "trees/plain.aibt");
            Assert.That(v3.FormatVersion, Is.EqualTo(3));
            Assert.That(SubtreeExpander.RequiresExpansion(v3), Is.False);

            var expected = CompileOk(v2, null);
            var withoutResolver = CompileOk(v3, null);
            var withResolver = CompileOk(v3, new InMemorySubtreeResolver());
            var catalog = Catalog();
            var throughExpansion = GeneratedCompiledProgramV2Compiler.Compile(
                SubtreeExpander.Expand(v3, null, catalog.Registry, null), catalog.Generated.Values, catalog.RegisteredTypes, Options());
            Assert.That(throughExpansion.Success, Is.True, Describe(throughExpansion.Diagnostics));

            foreach (var program in new[] { withoutResolver, withResolver, throughExpansion.Program })
            {
                Assert.That(program.SemanticProgram.Header.CanonicalSemanticHash, Is.EqualTo(expected.SemanticProgram.Header.CanonicalSemanticHash));
                Assert.That(program.SemanticProgram.Header.CompiledContentHash, Is.EqualTo(expected.SemanticProgram.Header.CompiledContentHash));
            }
            Assert.That(v3.FormatVersion, Is.EqualTo(3), "The caller's document is not changed.");
        }

        [Test]
        public void AddReferenceThenUnpack_LeavesFormat3WithoutFeatures_AndThatTreePrepares()
        {
            var child = Parse(ChildJson, ChildSourceId);
            var resolver = new InMemorySubtreeResolver().Add(child, ChildSourceId);
            var catalog = Catalog();
            var parent = Parse(Q(@"{ 'format': 'aibt.tree', 'formatVersion': 2, 'treeId': 'tests.p11007.parent', 'name': 'P11-007 parent', 'root': 'root',
  'blackboard': { 'distance': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 } },
  'nodes': { 'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': [] } } }"), ParentSourceId);

            var withReference = SubtreeGraphEdits.AddReference(parent, SubtreeGraphEdits.CreateReference(parent, child, new NodeId("use")), new NodeId("root"));
            Assert.That(withReference.FormatVersion, Is.EqualTo(TreeDocument.SubtreeFormatVersion), "Adding a reference raises the format to 3.");
            var referenced = ProductionTreeAssetBaker.Create(withReference, ParentSourceId, typeof(ConsumerCatalog), resolver, out var diagnostics);
            Assert.That(referenced, Is.Not.Null, "Control: the tree with its reference prepares. " + Describe(diagnostics));

            var unpacked = SubtreeGraphEdits.Unpack(withReference, new NodeId("use"), resolver, catalog.Registry, catalog.RegisteredTypes, out diagnostics, out _);
            Assert.That(unpacked, Is.Not.Null, Describe(diagnostics));
            Assert.That(unpacked.FormatVersion, Is.EqualTo(TreeDocument.SubtreeFormatVersion), "Unpack keeps the file's format.");
            Assert.That(SubtreeExpander.RequiresExpansion(unpacked), Is.False);

            var prepared = ProductionTreeAssetBaker.Create(unpacked, ParentSourceId, typeof(ConsumerCatalog), resolver, out diagnostics);
            Assert.That(prepared, Is.Not.Null, Describe(diagnostics));
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.False, Describe(diagnostics));
            Assert.That(prepared.TryCreateProgram(ConsumerCatalog.TryCreateRuntimeCatalog, out var program, out var failure), Is.True, failure.ToString());
            program.Dispose();
            Assert.That(CompileOk(unpacked, resolver).SemanticProgram.Header.CanonicalSemanticHash, Is.EqualTo(CompileOk(withReference, resolver).SemanticProgram.Header.CanonicalSemanticHash),
                "The unpacked tree is the reference's expansion, so it compiles to the same semantic hash.");
            UnityEngine.Object.DestroyImmediate(referenced);
            UnityEngine.Object.DestroyImmediate(prepared);
        }

        private static ProjectNodeCatalog Catalog() => ProjectNodeCatalog.ForCatalog(typeof(ConsumerCatalog));

        private static ReferenceCompilerOptions Options()
            => new ReferenceCompilerOptions("trees/plain.aibt", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6));

        private static GeneratedCompiledProgramV2 CompileOk(TreeDocument document, ISubtreeResolver resolver)
        {
            var catalog = Catalog();
            var result = resolver == null
                ? GeneratedCompiledProgramV2Compiler.Compile(document, catalog.Generated.Values, catalog.RegisteredTypes, Options())
                : GeneratedCompiledProgramV2Compiler.Compile(document, catalog.Generated.Values, catalog.RegisteredTypes, Options(), resolver);
            Assert.That(result.Success, Is.True, Describe(result.Diagnostics));
            return result.Program;
        }

        private static TreeDocument Parse(string json, string sourceId)
        {
            var read = CanonicalTreeJson.Parse(json, sourceId);
            Assert.That(read.Success, Is.True, Describe(read.Diagnostics));
            return read.Document;
        }

        private static string Describe(DiagnosticCollection diagnostics)
            => diagnostics == null ? string.Empty : string.Join(" | ", diagnostics.Select(item => item.Code.Value + ": " + item.Message));

        private static string Q(string text) => text.Replace('\'', '"');
    }
}
