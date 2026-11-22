using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using NUnit.Framework;
using UnityEditor;

namespace AIBT.Tests.Consumer
{
    /// <summary>
    /// P11-007 (review F3): the state the NativeTreeAgent and ProductionTreeAsset inspectors show on
    /// every repaint costs no project scan once it is known, and still follows a re-prepared asset, a
    /// removed subtree file and a catalog change. Before the fix every repaint of an asset prepared
    /// from subtrees walked the project's tree files and hashed them. Works in a temporary folder
    /// under Assets/ that it deletes.
    /// </summary>
    public sealed class ProductionTreeAssetInspectorStateTests
    {
        private const string FolderName = "AibtP11007InspectorTest";
        private const string Folder = "Assets/" + FolderName;
        private const string ChildPath = Folder + "/child.aibt";
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
        public void RepaintsCostNoScan_AndTheStateFollowsReprepareSubtreeRemovalAndCatalogChange()
        {
            Write(ChildPath, ChildJson("1.5"));
            Write(ParentPath, ParentJson);
            AssetDatabase.Refresh();
            var asset = Bake();
            Assert.That(asset.SubtreeSourceGuids, Is.Not.Empty, "prepared from a subtree");
            Assert.That(ProductionTreeAssetStaleness.EvaluateForInspector(asset), Is.EqualTo(ProductionTreeAssetState.Current));

            var scans = ProjectTreeIndex.ScanCount;
            for (var repaint = 0; repaint < 20; repaint++)
                Assert.That(ProductionTreeAssetStaleness.EvaluateForInspector(asset), Is.EqualTo(ProductionTreeAssetState.Current));
            Assert.That(ProjectTreeIndex.ScanCount - scans, Is.EqualTo(0), "20 repaints of a known state walk no tree files");

            // The subtree file is edited and imported: the asset is re-prepared and shown current.
            var hash = asset.SourceContentHash;
            Write(ChildPath, ChildJson("1.75"));
            AssetDatabase.Refresh();
            Assert.That(asset.SourceContentHash, Is.Not.EqualTo(hash), "re-prepared on import");
            Assert.That(ProductionTreeAssetStaleness.EvaluateForInspector(asset), Is.EqualTo(ProductionTreeAssetState.Current));

            // The node catalog changes (rediscovered): the state is evaluated again, once. Counted
            // from after the change, since other listeners (an open graph window) may scan for it.
            ProjectNodeCatalogCache.Refresh();
            scans = ProjectTreeIndex.ScanCount;
            for (var repaint = 0; repaint < 5; repaint++)
                Assert.That(ProductionTreeAssetStaleness.EvaluateForInspector(asset), Is.EqualTo(ProductionTreeAssetState.Current));
            Assert.That(ProjectTreeIndex.ScanCount - scans, Is.EqualTo(1), "evaluated once after the catalog changed");

            // The subtree file is removed: the asset cannot be re-prepared and is shown stale.
            AssetDatabase.DeleteAsset(ChildPath);
            AssetDatabase.Refresh();
            Assert.That(ProductionTreeAssetStaleness.EvaluateForInspector(asset), Is.EqualTo(ProductionTreeAssetState.SourceChanged));
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.SourceChanged), "agrees with a fresh evaluation");
        }

        private static ProductionTreeAsset Bake()
        {
            var parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(ParentPath), ParentPath);
            Assert.That(parsed.Success, Is.True);
            var asset = ProductionTreeAssetBaker.Create(parsed.Document, ParentPath, typeof(ConsumerCatalog), out var diagnostics);
            Assert.That(asset, Is.Not.Null, string.Join("; ", diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(AssetPath);
        }

        private static void Write(string path, string json) => File.WriteAllText(path, json.Replace('\'', '"'));

        private static string ChildJson(string near)
            => @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11007.inspector.child', 'name': 'P11-007 inspector child', 'root': 'root',
  'interface': { 'version': 1, 'keys': ['distance'] },
  'blackboard': {
    'distance': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 },
    'range': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 3.0 } },
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['near', 'far'] },
    'near': { 'type': 'aibt.tests.consumer.radius', 'typeVersion': 1, 'parameters': { 'radius': " + near + @" }, 'bindings': { 'distance': 'distance' } },
    'far': { 'type': 'aibt.tests.consumer.radius', 'typeVersion': 1, 'parameters': { 'radius': 2.5 }, 'bindings': { 'distance': 'range' } } } }";

        private const string ParentJson = @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11007.inspector.parent', 'name': 'P11-007 inspector parent', 'root': 'root',
  'blackboard': { 'distance': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 } },
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['use'] },
    'use': { 'subtree': { 'treeId': 'tests.p11007.inspector.child', 'interfaceVersion': 1, 'keys': { 'distance': 'distance' } } } } }";
    }
}
