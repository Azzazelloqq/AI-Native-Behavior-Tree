using System;
using System.IO;
using System.Linq;
using AIBT.Agents;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using NUnit.Framework;
using UnityEditor;

namespace AIBT.Tests.Consumer
{
    /// <summary>
    /// ADR-P11-001 stage 2: the importer stores a tree that uses subtrees as its expanded document and
    /// re-imports it when a subtree file changes; a runtime asset baked from it records the subtree
    /// file and is re-prepared when that file changes; a second file with the same tree ID makes
    /// every dependant report AIBT3021. Works in a temporary folder under Assets/ that it deletes.
    /// </summary>
    public sealed class SubtreeImportAndBakeTests
    {
        private const string FolderName = "AibtP11SubtreeImportTest";
        private const string Folder = "Assets/" + FolderName;
        private const string ChildPath = Folder + "/child.aibt";
        private const string ChildCopyPath = Folder + "/child-copy.aibt";
        private const string ParentPath = Folder + "/parent.aibt";
        private const string AssetPath = Folder + "/parent.runtime.asset";

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", FolderName);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.Refresh();
        }

        [Test]
        public void ImportedParent_StoresTheExpandedTree_AndIsReimportedWhenTheSubtreeChanges()
        {
            Write(ChildPath, NativeChildJson("1.5"));
            Write(ParentPath, ParentJson);
            AssetDatabase.Refresh();

            var source = AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(ParentPath);
            Assert.That(source, Is.Not.Null);
            Assert.That(source.TreeId, Is.EqualTo("tests.p11.import.parent"));
            Assert.That(source.Status, Is.Not.EqualTo("Errors"), source.Details);
            Assert.That(source.NodeCount, Is.EqualTo(4), "The expanded tree: root plus the subtree's three nodes.");
            var stored = CanonicalTreeJson.Parse(source.Json, ParentPath);
            Assert.That(stored.Success, Is.True);
            Assert.That(stored.Document.FormatVersion, Is.EqualTo(2), "The stored document is the expansion, without references.");
            Assert.That(stored.Document.Nodes.Select(node => node.Id.Value).OrderBy(id => id),
                Is.EqualTo(new[] { "root", "use:far", "use:near", "use:root" }));
            Assert.That(stored.Document.Nodes.Single(node => node.Id.Value == "use:far").Bindings.Values["key"], Is.EqualTo("use:range"),
                "A binding to the subtree's private key names the instance's key.");
            Assert.That(stored.Document.Nodes.Single(node => node.Id.Value == "use:near").Bindings.Values["key"], Is.EqualTo("distance"));
            Assert.That(source.Json, Does.Contain("1.5"));
            var sources = new SerializedObject(source).FindProperty("_subtreeNodeSourceIds");
            Assert.That(Enumerable.Range(0, sources.arraySize).Select(index => sources.GetArrayElementAtIndex(index).stringValue).Distinct(),
                Is.EqualTo(new[] { ChildPath }), "Inner nodes keep the subtree file as their source.");

            Write(ChildPath, NativeChildJson("1.75"));
            AssetDatabase.Refresh();

            source = AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(ParentPath);
            Assert.That(source.Json, Does.Contain("1.75").And.Not.Contain("1.5"), "Editing the subtree file re-imports the parent.");
        }

        [Test]
        public void BakedAsset_RecordsTheSubtreeFile_AndIsRePreparedWhenItChanges()
        {
            Write(ChildPath, ChildJson("1.5"));
            Write(ParentPath, ParentJson);
            AssetDatabase.Refresh();
            var asset = Bake();

            Assert.That(asset.SubtreeSourceGuids, Is.EqualTo(new[] { AssetDatabase.AssetPathToGUID(ChildPath) }));
            Assert.That(asset.SourceGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(ParentPath)));
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.Current));
            var oldHash = asset.SourceContentHash;
            Assert.That(oldHash, Is.Not.EqualTo(StableHash.Sha256Hex(File.ReadAllBytes(ParentPath))),
                "With subtrees the hash covers the parent and the subtree files (ADR-P11-001 section 4.5).");

            // Edited on disk, not imported yet: stale although the parent file did not change.
            Write(ChildPath, ChildJson("1.75"));
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.SourceChanged));

            AssetDatabase.Refresh();
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.Current),
                "Importing the edited subtree re-prepares the dependent asset.");
            Assert.That(asset.SourceContentHash, Is.Not.EqualTo(oldHash));
            Assert.That(asset.SubtreeSourceGuids, Is.EqualTo(new[] { AssetDatabase.AssetPathToGUID(ChildPath) }));
            Assert.That(asset.TryCreateProgram(ConsumerCatalog.TryCreateRuntimeCatalog, out var program, out var failure), Is.True, failure.ToString());
            program.Dispose();
        }

        [Test]
        public void SecondFileWithTheSameTreeId_GivesAIBT3021_InDependants_UntilItIsRemoved()
        {
            // Built-in leaves: the project catalog knows them at import, and they have a player catalog to bake with.
            var catalog = Type.GetType("AIBT.Authoring.BuiltInLeaves.BuiltInLeafCatalog, AIBT.Authoring.BuiltInLeaves.Catalog", true);
            Write(ChildPath, WaitChildJson(2));
            Write(ParentPath, WaitParentJson);
            AssetDatabase.Refresh();
            Assert.That(AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(ParentPath).Status, Is.Not.EqualTo("Errors"));
            var asset = Bake(catalog);
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.Current));

            Write(ChildCopyPath, WaitChildJson(3));
            AssetDatabase.Refresh();
            AssetDatabase.Refresh(); // the tree-ID dependency registered by the first refresh re-imports the parent

            var source = AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(ParentPath);
            Assert.That(source.Status, Is.EqualTo("Errors"));
            Assert.That(source.Details, Does.Contain("AIBT3021"));
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.Not.EqualTo(ProductionTreeAssetState.Current));
            Assert.That(ProductionTreeAssetStaleness.TryPrepareAgain(asset, out var error), Is.False);
            Assert.That(error, Does.Contain("AIBT3021"));

            AssetDatabase.DeleteAsset(ChildCopyPath);
            AssetDatabase.Refresh();
            AssetDatabase.Refresh();

            source = AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(ParentPath);
            Assert.That(source.Status, Is.Not.EqualTo("Errors"), source.Details);
            Assert.That(ProductionTreeAssetStaleness.TryPrepareAgain(asset, out error), Is.True, error);
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.Current));
        }

        [Test]
        public void SubtreeWithNodeTypesTheProjectCatalogDoesNotKnow_ImportsAsNeedsRegistry()
        {
            // aibt.tests.consumer.radius lives in a test assembly, which ProjectNodeCatalog.Discover() leaves out.
            Write(ChildPath, ChildJson("1.5"));
            Write(ParentPath, ParentJson);
            Write(PlainPath, PlainJson);
            AssetDatabase.Refresh();

            var plain = AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(PlainPath);
            Assert.That(plain.Status, Is.EqualTo("Needs registry"), "A tree without subtrees with the same node type.");
            var source = AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(ParentPath);
            Assert.That(source.Status, Is.EqualTo("Needs registry"), source.Details);
            Assert.That(source.Details, Does.Contain("AIBT2021"));
            Assert.That(CanonicalTreeJson.Parse(source.Json, ParentPath).Document.FormatVersion, Is.EqualTo(3),
                "The source keeps the tree as written; it cannot be expanded without the node contracts.");
        }

        [Test]
        public void RealExpansionErrorsNextToUnknownNodeTypes_StayErrors()
        {
            Write(ChildPath, ChildJson("1.5"));
            Write(ParentPath, ParentWithMissingSubtreeJson);
            AssetDatabase.Refresh();

            var source = AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(ParentPath);
            Assert.That(source.Status, Is.EqualTo("Errors"));
            Assert.That(source.Details, Does.Contain("AIBT3020").And.Contain("AIBT2021"));
        }

        [Test]
        public void OnlyUnknownNodeTypes_IsTrueOnlyWhenEveryErrorIsAIBT2021()
        {
            Diagnostic Error(string code) => new Diagnostic(new DiagnosticCode(code), DiagnosticSeverity.Error, "x", new DiagnosticLocation("doc"));
            var warning = new Diagnostic(new DiagnosticCode("AIBT2043"), DiagnosticSeverity.Warning, "x", new DiagnosticLocation("doc"));
            Assert.That(TreeCatalogEntry.OnlyUnknownNodeTypes(new DiagnosticCollection(new[] { Error("AIBT2021"), Error("AIBT2021"), warning })), Is.True);
            Assert.That(TreeCatalogEntry.OnlyUnknownNodeTypes(new DiagnosticCollection(new[] { Error("AIBT2021"), Error("AIBT3020") })), Is.False);
            foreach (var code in new[] { "AIBT3020", "AIBT3021", "AIBT3022", "AIBT3023", "AIBT3024", "AIBT3025", "AIBT3026", "AIBT3027", "AIBT3028", "AIBT2047" })
                Assert.That(TreeCatalogEntry.OnlyUnknownNodeTypes(new DiagnosticCollection(new[] { Error(code) })), Is.False, code);
            Assert.That(TreeCatalogEntry.OnlyUnknownNodeTypes(new DiagnosticCollection(new[] { warning })), Is.False);
        }

        private static ProductionTreeAsset Bake() => Bake(typeof(ConsumerCatalog));

        private static ProductionTreeAsset Bake(Type catalog)
        {
            var parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(ParentPath), ParentPath);
            Assert.That(parsed.Success, Is.True);
            var asset = ProductionTreeAssetBaker.Create(parsed.Document, ParentPath, catalog, out var diagnostics);
            Assert.That(asset, Is.Not.Null, string.Join("; ", diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(AssetPath);
        }

        private static void Write(string path, string json) => File.WriteAllText(path, json.Replace('\'', '"'));

        private static string ChildJson(string near)
            => @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11.import.child', 'name': 'P11 import child', 'root': 'root',
  'interface': { 'version': 1, 'keys': ['distance'] },
  'blackboard': {
    'distance': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 },
    'range': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 3.0 } },
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['near', 'far'] },
    'near': { 'type': 'aibt.tests.consumer.radius', 'typeVersion': 1, 'parameters': { 'radius': " + near + @" }, 'bindings': { 'distance': 'distance' } },
    'far': { 'type': 'aibt.tests.consumer.radius', 'typeVersion': 1, 'parameters': { 'radius': 2.5 }, 'bindings': { 'distance': 'range' } } } }";

        private static string NativeChildJson(string near)
            => @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11.import.child', 'name': 'P11 import child', 'root': 'root',
  'interface': { 'version': 1, 'keys': ['distance'] },
  'blackboard': {
    'distance': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 },
    'range': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 3.0 } },
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['near', 'far'] },
    'near': { 'type': 'aibt.native.float-at-least', 'typeVersion': 1, 'parameters': { 'threshold': " + near + @" }, 'bindings': { 'key': 'distance' } },
    'far': { 'type': 'aibt.native.float-at-least', 'typeVersion': 1, 'parameters': { 'threshold': 2.5 }, 'bindings': { 'key': 'range' } } } }";

        private static string WaitChildJson(int ticks)
            => @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11.import.child', 'name': 'P11 import child', 'root': 'root',
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['hold'] },
    'hold': { 'type': 'aibt.stdlib.wait', 'typeVersion': 1, 'parameters': { 'ticks': " + ticks + @" } } } }";

        private const string WaitParentJson = @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11.import.parent', 'name': 'P11 import parent', 'root': 'root',
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['use'] },
    'use': { 'subtree': { 'treeId': 'tests.p11.import.child', 'interfaceVersion': 1, 'keys': { } } } } }";

        private const string PlainPath = Folder + "/plain.aibt";

        private const string PlainJson = @"{ 'format': 'aibt.tree', 'formatVersion': 2, 'treeId': 'tests.p11.import.plain', 'name': 'P11 import plain', 'root': 'near',
  'blackboard': { 'distance': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 } },
  'nodes': { 'near': { 'type': 'aibt.tests.consumer.radius', 'typeVersion': 1, 'parameters': { 'radius': 1.5 }, 'bindings': { 'distance': 'distance' } } } }";

        private const string ParentWithMissingSubtreeJson = @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11.import.parent', 'name': 'P11 import parent', 'root': 'root',
  'blackboard': { 'distance': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 } },
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['use', 'gone'] },
    'gone': { 'subtree': { 'treeId': 'tests.p11.import.missing', 'interfaceVersion': 1, 'keys': { } } },
    'use': { 'subtree': { 'treeId': 'tests.p11.import.child', 'interfaceVersion': 1, 'keys': { 'distance': 'distance' } } } } }";

        private const string ParentJson = @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11.import.parent', 'name': 'P11 import parent', 'root': 'root',
  'blackboard': { 'distance': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 } },
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['use'] },
    'use': { 'subtree': { 'treeId': 'tests.p11.import.child', 'interfaceVersion': 1, 'keys': { 'distance': 'distance' } } } } }";
    }
}
