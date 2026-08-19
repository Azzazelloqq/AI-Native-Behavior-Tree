using System;
using System.IO;
using AIBT.Editor.Layout;
using AIBT.Editor.Organization;
using AIBT.Tests.Editor.Layout;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Organization
{
    public sealed class LayoutPersistenceControllerTests
    {
        private string _scratchDirectory;
        private string _treeJsonPath;

        [SetUp]
        public void SetUp()
        {
            _scratchDirectory = Path.Combine(Path.GetTempPath(), "aibt-p3-005-tests", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(_scratchDirectory);

            var fixturePath = EditorTestPackagePaths.Resolve("Tests", "Editor", "Layout", "Fixtures", "shallow-wide-4.aibt.json");
            _treeJsonPath = Path.Combine(_scratchDirectory, "shallow-wide-4.aibt.json");
            File.Copy(fixturePath, _treeJsonPath, overwrite: true);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_scratchDirectory))
            {
                Directory.Delete(_scratchDirectory, recursive: true);
            }
        }

        [Test]
        public void MissingLayoutFileFallsBackToAutoLayoutDefault()
        {
            var tree = CanonicalLayoutJsonTests.ParseTreeFixture("shallow-wide-4.aibt.json");

            var result = LayoutPersistenceController.Load(_treeJsonPath, tree);

            Assert.That(result.Success, Is.True);
            Assert.That(result.UsedDefault, Is.True);
            Assert.That(result.Document.Nodes.Count, Is.EqualTo(tree.Nodes.Count));
        }

        [Test]
        public void SaveThenLoadRoundTripsManualOrganizationByteForByte()
        {
            var tree = CanonicalLayoutJsonTests.ParseTreeFixture("shallow-wide-4.aibt.json");
            var loaded = LayoutPersistenceController.Load(_treeJsonPath, tree);

            var organized = LayoutOrganizationOperations.Pin(loaded.Document, new NodeId("a"));
            organized = LayoutOrganizationOperations.AddOrUpdateGroup(organized, "g1", "Group", new[] { new NodeId("b"), new NodeId("c") });
            organized = LayoutOrganizationOperations.AddOrUpdateNote(organized, "n1", "Note", new LayoutPoint(1, 2), new LayoutPoint(3, 4));
            organized = LayoutOrganizationOperations.AddOrUpdateReroute(organized, new NodeId("root"), new NodeId("d"), new[] { new LayoutPoint(9, 9) });

            LayoutPersistenceController.Save(_treeJsonPath, organized);
            var savedBytes = File.ReadAllBytes(LayoutPersistenceController.LayoutPathFor(_treeJsonPath));

            var reloaded = LayoutPersistenceController.Load(_treeJsonPath, tree);
            Assert.That(reloaded.Success, Is.True);
            var reserializedBytes = CanonicalLayoutJsonWriter.Write(reloaded.Document);

            Assert.That(reserializedBytes, Is.EqualTo(savedBytes));
            Assert.That(reloaded.Document.Nodes[new NodeId("a")].Pinned, Is.True);
            Assert.That(reloaded.Document.Groups.ContainsKey("g1"), Is.True);
            Assert.That(reloaded.Document.Notes.ContainsKey("n1"), Is.True);
            Assert.That(reloaded.Document.Reroutes.Count, Is.EqualTo(1));
        }

        [Test]
        public void ManualOrganizationNeverChangesTheSemanticTreeFile()
        {
            var beforeBytes = File.ReadAllBytes(_treeJsonPath);
            var tree = CanonicalLayoutJsonTests.ParseTreeFixture("shallow-wide-4.aibt.json");

            var loaded = LayoutPersistenceController.Load(_treeJsonPath, tree);
            var organized = LayoutOrganizationOperations.Pin(loaded.Document, new NodeId("a"));
            organized = LayoutOrganizationOperations.AddOrUpdateGroup(organized, "g1", "Group", new[] { new NodeId("b") });
            LayoutPersistenceController.Save(_treeJsonPath, organized);

            var afterBytes = File.ReadAllBytes(_treeJsonPath);
            Assert.That(afterBytes, Is.EqualTo(beforeBytes));
        }
    }
}
