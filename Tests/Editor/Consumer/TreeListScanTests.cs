using System.IO;
using System.Linq;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using NUnit.Framework;
using UnityEditor;

namespace AIBT.Tests.Consumer
{
    /// <summary>
    /// P11-007 (review F4): one tree-list refresh walks the project's tree files at most once, however
    /// many listed trees use subtrees, and none when no listed tree does. Before the fix it walked them
    /// once per tree that requires expansion. Works in a temporary folder under Assets/ that it deletes.
    /// </summary>
    public sealed class TreeListScanTests
    {
        private const string FolderName = "AibtP11007TreeListTest";
        private const string Folder = "Assets/" + FolderName;

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            Directory.CreateDirectory(Folder);
            Write("child.aibt", Q(@"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11007.list.child', 'name': 'child', 'root': 'root',
  'nodes': { 'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['hold'] },
    'hold': { 'type': 'aibt.stdlib.wait', 'typeVersion': 1, 'parameters': { 'ticks': 2 } } } }"));
            for (var index = 0; index < 3; index++)
                Write("parent-" + index + ".aibt", Q(@"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11007.list.parent-" + index + @"', 'name': 'parent', 'root': 'root',
  'nodes': { 'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['use'] },
    'use': { 'subtree': { 'treeId': 'tests.p11007.list.child', 'interfaceVersion': 1, 'keys': { } } } } }"));
            Write("plain.aibt", Q(@"{ 'format': 'aibt.tree', 'formatVersion': 2, 'treeId': 'tests.p11007.list.plain', 'name': 'plain', 'root': 'hold',
  'nodes': { 'hold': { 'type': 'aibt.stdlib.wait', 'typeVersion': 1, 'parameters': { 'ticks': 2 } } } }"));
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
            if (File.Exists(Folder + ".meta")) File.Delete(Folder + ".meta");
        }

        [Test]
        public void ARefreshWithThreeSubtreeUsers_ScansOnce_AndExpandsEachOfThem()
        {
            var paths = Directory.GetFiles(Folder, "*.aibt").Select(path => path.Replace('\\', '/')).OrderBy(path => path, System.StringComparer.Ordinal).ToArray();
            Assert.That(paths, Has.Length.EqualTo(5));
            var before = ProjectTreeIndex.ScanCount;

            var entries = TreeCatalogEntry.ReadAll(paths, ProjectNodeCatalogCache.Current.Registry, null);

            Assert.That(ProjectTreeIndex.ScanCount - before, Is.EqualTo(1), "one walk of the tree files per refresh");
            var parents = entries.Where(entry => entry.TreeId.StartsWith("tests.p11007.list.parent-", System.StringComparison.Ordinal)).ToList();
            Assert.That(parents, Has.Count.EqualTo(3));
            foreach (var parent in parents)
            {
                Assert.That(parent.Expansion, Is.Not.Null, parent.Path);
                Assert.That(parent.Expansion.Success, Is.True, parent.Details);
                Assert.That(parent.NodeCount, Is.EqualTo(3), "root plus the subtree's two nodes");
            }
        }

        [Test]
        public void ARefreshWithoutSubtreeUsers_DoesNotScan()
        {
            var before = ProjectTreeIndex.ScanCount;
            var entries = TreeCatalogEntry.ReadAll(new[] { Folder + "/plain.aibt", Folder + "/child.aibt" }, ProjectNodeCatalogCache.Current.Registry, null);
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(ProjectTreeIndex.ScanCount - before, Is.EqualTo(0));
        }

        private static void Write(string name, string json) => File.WriteAllText(Folder + "/" + name, json);

        private static string Q(string text) => text.Replace('\'', '"');
    }
}
