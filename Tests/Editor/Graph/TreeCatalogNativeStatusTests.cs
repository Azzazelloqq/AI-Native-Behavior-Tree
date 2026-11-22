using System;
using System.Collections.Generic;
using System.IO;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using AIBT.Tests.Consumer;
using NUnit.Framework;
using UnityEditor;

namespace AIBT.Tests.Editor.Graph
{
    /// <summary>
    /// P11-005: the tree list's status of a valid native tree. "Needs native compile" only when a
    /// native node type has no generated code in the project catalog; otherwise "Valid", with a
    /// detail that names the prepared runtime assets compiled from the tree (as its source or as a
    /// subtree) or says there is none. The tree uses <c>aibt.tests.consumer.radius</c>, a generated
    /// node without a reference handler. Works in a temporary folder under Assets/ that it deletes.
    /// </summary>
    public sealed class TreeCatalogNativeStatusTests
    {
        private const string FolderName = "AibtP11005StatusTest";
        private const string Folder = "Assets/" + FolderName;
        private const string TreePath = Folder + "/native.aibt";
        private const string AssetPath = Folder + "/native.runtime.asset";
        private const string NodeType = "aibt.tests.consumer.radius";

        private ProjectNodeCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = ProjectNodeCatalog.ForCatalog(typeof(ConsumerCatalog));
            Assert.That(_catalog.Problems, Is.Empty, string.Join("\n", _catalog.Problems));
            Assert.That(_catalog.Generated.ContainsKey(NodeType), Is.True);
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", FolderName);
            File.WriteAllBytes(TreePath, CanonicalTreeJson.Serialize(ProductionConsumerWorkflowTests.Tree(SemanticValue.FromNumber(1.5))).Utf8);
            AssetDatabase.ImportAsset(TreePath);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void GeneratedCodeAndAPreparedAsset_IsValid_AndNamesTheAsset_UntilTheAssetIsDeleted()
        {
            var document = CanonicalTreeJson.Parse(File.ReadAllBytes(TreePath), TreePath).Document;
            var asset = ProductionTreeAssetBaker.Create(document, TreePath, typeof(ConsumerCatalog), out var diagnostics);
            Assert.That(asset, Is.Not.Null, string.Join("; ", diagnostics));
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();

            var entry = Read(TreeCatalogEntry.NativeFacts.FromProject(_catalog));
            Assert.That(entry.Status, Is.EqualTo("Valid"), entry.Details);
            Assert.That(entry.Details, Is.EqualTo("Native nodes have generated code. Prepared runtime asset: " + AssetPath + "."));

            AssetDatabase.DeleteAsset(AssetPath);
            entry = Read(TreeCatalogEntry.NativeFacts.FromProject(_catalog));
            Assert.That(entry.Status, Is.EqualTo("Valid"), entry.Details);
            Assert.That(entry.Details, Is.EqualTo("Native nodes have generated code. No runtime asset is prepared from this tree yet."),
                "Deleting the .asset clears the cached index.");
        }

        [Test]
        public void GeneratedCodeAndAnAssetPreparedWithTheTreeAsASubtree_NamesThatAsset()
        {
            // Any asset GUID with a path stands for the parent's runtime asset here.
            var treeGuid = AssetDatabase.AssetPathToGUID(TreePath);
            var parentAssetGuid = AssetDatabase.AssetPathToGUID(Folder);
            var facts = new TreeCatalogEntry.NativeFacts(_catalog.Generated, new[] { (treeGuid, parentAssetGuid) });
            var entry = Read(facts);
            Assert.That(entry.Status, Is.EqualTo("Valid"), entry.Details);
            Assert.That(entry.Details, Is.EqualTo("Native nodes have generated code. Prepared runtime asset: " + Folder + "."));
        }

        [Test]
        public void ANativeNodeWithoutGeneratedCode_NeedsNativeCompile()
        {
            var facts = new TreeCatalogEntry.NativeFacts(new Dictionary<string, GeneratedNodeDescriptor>(), Array.Empty<(string, string)>());
            var entry = Read(facts);
            Assert.That(entry.Status, Is.EqualTo("Needs native compile"), entry.Details);
            Assert.That(entry.Details, Is.EqualTo("No generated code in the project for: " + NodeType + ". It is written when scripts compile."));
        }

        [Test]
        public void WithoutProjectFacts_TheEntryIsValid_AndSaysWhatWasNotChecked()
        {
            var entry = TreeCatalogEntry.Read(TreePath, _catalog.Registry);
            Assert.That(entry.Status, Is.EqualTo("Valid"), entry.Details);
            Assert.That(entry.Details, Is.EqualTo("Node contracts validated; generated code and prepared runtime assets are not checked here."));
        }

        [Test]
        public void ATreeWithoutNativeNodes_KeepsItsStatusAndDetail()
        {
            const string path = Folder + "/built-in.aibt";
            File.WriteAllText(path, @"{ ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11005.built-in"", ""name"": ""Built-in"", ""root"": ""hold"",
  ""nodes"": { ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } } } }");
            AssetDatabase.ImportAsset(path);
            var entry = TreeCatalogEntry.Read(path, _catalog.Registry, null,
                new TreeCatalogEntry.NativeFacts(new Dictionary<string, GeneratedNodeDescriptor>(), Array.Empty<(string, string)>()));
            Assert.That(entry.Status, Is.EqualTo("Valid"), entry.Details);
            Assert.That(entry.Details, Is.EqualTo("Node contracts validated; runtime compilation is a separate step."));
        }

        private TreeCatalogEntry Read(TreeCatalogEntry.NativeFacts facts) => TreeCatalogEntry.Read(TreePath, _catalog.Registry, null, facts);
    }
}
