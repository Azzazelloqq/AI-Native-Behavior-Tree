using System;
using System.IO;
using AIBT.Agents;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AIBT.Tests.Consumer
{
    /// <summary>
    /// P11-014 review follow-up (item 3), through the importer: a tree, or a subtree it uses, with a key of
    /// a registered blackboard type imports with the project catalog's registered types (not "Errors"), and
    /// an import that could not expand its subtrees because of node types the project catalog does not know
    /// ("Needs registry") puts its own message before the compile's "no subtree resolver" (W3). Works in a
    /// temporary folder under Assets/ that it deletes.
    /// </summary>
    public sealed class ImporterRegisteredTypeAndStatusTests
    {
        private const string FolderName = "AibtP11014ImporterTest";
        private const string Folder = "Assets/" + FolderName;
        private const string TypeId = "aibt.tests.p10010.sense";

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", FolderName);
        }

        [TearDown]
        public void TearDown()
        {
            BehaviorTreeImporter.CatalogForTests = null;
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// The project catalog lists player assemblies only and no player assembly here registers a type,
        /// so the importer is given a catalog built from the P10-010 fixture shard, which registers
        /// <c>aibt.tests.p10010.sense</c>.
        /// </summary>
        private static ProjectNodeCatalog FixtureCatalog()
        {
            var shard = Type.GetType("AIBT.Tests.CodeGen.GenerationP10010.SimulationIoShard, AIBT.CodeGen.P10010SimulationIo.Tests");
            Assert.That(shard, Is.Not.Null, "the P10-010 fixture shard");
            var catalog = ProjectNodeCatalog.Build(new[] { shard }, Array.Empty<IReferenceLeafBehaviorProvider>());
            Assert.That(catalog.RegisteredTypes.TryGet(TypeId, 1u, out _), Is.True);
            return catalog;
        }

        [Test]
        public void ATreeWithARegisteredTypeKey_ImportsWithoutErrors()
        {
            var catalog = FixtureCatalog();
            BehaviorTreeImporter.CatalogForTests = () => catalog;
            var path = Folder + "/sense.aibt";
            Write(path, @"{ 'format': 'aibt.tree', 'formatVersion': 2, 'treeId': 'tests.p11014.item3.plain', 'name': 'Item 3 plain', 'root': 'hold',
  'blackboard': { 'sense': { 'type': '" + TypeId + @"', 'typeVersion': 1, 'scope': 'tree' } },
  'nodes': { 'hold': { 'type': 'aibt.stdlib.wait', 'typeVersion': 1, 'parameters': { 'ticks': 2 } } } }");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var source = AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(path);
            Assert.That(source, Is.Not.Null);
            Debug.Log("P11-014 item 3, import of a registered-type tree: " + source.Status + "; " + source.Details);
            Assert.That(source.Status, Is.Not.EqualTo("Errors"), source.Details);
        }

        [Test]
        public void AParentWhoseSubtreeHasARegisteredTypeKey_IsExpandedAtImport()
        {
            var catalog = FixtureCatalog();
            BehaviorTreeImporter.CatalogForTests = () => catalog;
            Write(Folder + "/sub.aibt", @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11014.item3.sub', 'name': 'Item 3 sub', 'root': 'hold',
  'interface': { 'version': 1, 'keys': ['hits'] },
  'blackboard': { 'hits': { 'type': 'Int32', 'default': 0 }, 'sense': { 'type': '" + TypeId + @"', 'typeVersion': 1, 'scope': 'tree' } },
  'nodes': { 'hold': { 'type': 'aibt.stdlib.wait', 'typeVersion': 1, 'parameters': { 'ticks': 2 } } } }");
            var parentPath = Folder + "/parent.aibt";
            Write(parentPath, @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11014.item3.parent', 'name': 'Item 3 parent', 'root': 'run',
  'blackboard': { 'hits': { 'type': 'Int32', 'default': 0 } },
  'nodes': { 'run': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['use'] },
    'use': { 'subtree': { 'treeId': 'tests.p11014.item3.sub', 'interfaceVersion': 1, 'keys': { 'hits': 'hits' } } } } }");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(parentPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            var source = AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(parentPath);
            Assert.That(source, Is.Not.Null);
            Debug.Log("P11-014 item 3, import of a parent of a registered-type subtree: " + source.Status + "; " + source.Details);
            Assert.That(source.Status, Is.Not.EqualTo("Errors"), source.Details);
            Assert.That(source.NodeCount, Is.EqualTo(2), "the expanded tree: the parent's root and the subtree's leaf");
        }

        /// <summary>
        /// W3: the subtree uses a node type the project catalog does not know, so the import cannot expand it
        /// and reads "Needs registry". A BehaviorTreeAgent on that source then gets the import's message
        /// first, not only AIBT3020 ("no subtree resolver").
        /// </summary>
        [Test]
        public void AnImportThatNeedsARegistry_ReportsItsOwnMessageFirst()
        {
            Write(Folder + "/sub.aibt", @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11014.item3.unknown-sub', 'name': 'Item 3 unknown', 'root': 'odd',
  'nodes': { 'odd': { 'type': 'tests.p11014.no-such-node', 'typeVersion': 1 } } }");
            var parentPath = Folder + "/parent.aibt";
            Write(parentPath, @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11014.item3.unknown-parent', 'name': 'Item 3 unknown parent', 'root': 'run',
  'nodes': { 'run': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['use'] },
    'use': { 'subtree': { 'treeId': 'tests.p11014.item3.unknown-sub', 'interfaceVersion': 1, 'keys': { } } } } }");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(parentPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            var source = AssetDatabase.LoadAssetAtPath<BehaviorTreeSource>(parentPath);
            Assert.That(source, Is.Not.Null);
            Assert.That(source.Status, Is.EqualTo("Needs registry"), source.Details);
            Assert.That(source.TryGetProgram(out _, out var error), Is.False);
            Debug.Log("P11-014 item 3 (W3), Needs registry import: details " + source.Details + "; error " + error);
            Assert.That(error, Does.StartWith(source.Details), error);
            Assert.That(error, Does.Contain("AIBT3020"), "the compile's own diagnostics follow");
        }

        private static void Write(string path, string json) => File.WriteAllText(path, json.Replace('\'', '"'));
    }
}
