using System.Collections.Generic;
using System.IO;
using AIBT.Authoring;
using AIBT.Editor.Layout;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Layout
{
    public sealed class DeterministicAutoLayoutServiceTests
    {
        [TestCase("shallow-wide-4")]
        [TestCase("deep-chain")]
        [TestCase("mixed")]
        public void MatchesGoldenLayoutBytes(string fixtureName)
        {
            var document = ParseFixture(fixtureName + ".aibt.json");
            var expected = File.ReadAllBytes(FixturePath(fixtureName + ".expected.aibt.layout.json"));

            var actual = DeterministicAutoLayoutService.LayoutToBytes(document);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase("shallow-wide-4")]
        [TestCase("deep-chain")]
        [TestCase("mixed")]
        public void RunningTwiceProducesByteIdenticalOutput(string fixtureName)
        {
            var document = ParseFixture(fixtureName + ".aibt.json");

            var first = DeterministicAutoLayoutService.LayoutToBytes(document);
            var second = DeterministicAutoLayoutService.LayoutToBytes(document);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void SupersetTreeDoesNotRepositionPreviouslyPlacedNodes()
        {
            var before = ParseFixture("shallow-wide-3.aibt.json");
            var beforeLayout = DeterministicAutoLayoutService.Layout(before);

            var after = ParseFixture("shallow-wide-4.aibt.json");
            var afterLayout = DeterministicAutoLayoutService.Layout(after, beforeLayout);

            foreach (var kvp in beforeLayout.Nodes)
            {
                Assert.That(afterLayout.Nodes.ContainsKey(kvp.Key), Is.True);
                Assert.That(afterLayout.Nodes[kvp.Key].Position, Is.EqualTo(kvp.Value.Position),
                    $"Node '{kvp.Key}' must keep its previous position after {nameof(after)} appended a node.");
            }

            Assert.That(afterLayout.Nodes.Count, Is.EqualTo(5));
            Assert.That(afterLayout.Nodes.ContainsKey(new NodeId("d")), Is.True, "The newly appended node must still be placed.");
        }

        [Test]
        public void LargeSyntheticTreeLaysOutWithoutOverlappingPositions()
        {
            // Same scale class as P3-001's spike fixture (240 nodes): a synthetic tree of
            // sequence/selector/condition/action-shaped nodes, root-to-leaf connected.
            var document = BuildSyntheticTree(nodeCount: 240, branchingFactor: 3);

            var layout = DeterministicAutoLayoutService.Layout(document);

            Assert.That(layout.Nodes.Count, Is.EqualTo(240));

            var seenPositions = new HashSet<LayoutPoint>();
            foreach (var placement in layout.Nodes.Values)
            {
                Assert.That(seenPositions.Add(placement.Position), Is.True,
                    "No two nodes should occupy the exact same position (a proxy for 'not visually degenerate').");
            }
        }

        private static TreeDocument ParseFixture(string fileName)
        {
            var path = FixturePath(fileName);
            var result = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
            Assert.That(result.Success, Is.True);
            return result.Document;
        }

        private static string FixturePath(string fileName)
        {
            return EditorTestPackagePaths.Resolve("Tests", "Editor", "Layout", "Fixtures", fileName);
        }

        private static TreeDocument BuildSyntheticTree(int nodeCount, int branchingFactor)
        {
            var nodes = new List<NodeDocument>();
            var idQueue = new Queue<NodeId>();
            var rootId = new NodeId("n0");
            idQueue.Enqueue(rootId);
            var nextIndex = 1;

            while (idQueue.Count > 0 && nextIndex < nodeCount)
            {
                var parentId = idQueue.Dequeue();
                var children = new List<NodeId>();
                for (var branch = 0; branch < branchingFactor && nextIndex < nodeCount; branch++)
                {
                    var childId = new NodeId("n" + nextIndex);
                    nextIndex++;
                    children.Add(childId);
                    idQueue.Enqueue(childId);
                }

                nodes.Add(new NodeDocument(parentId, "sample.composite", 1, children));
            }

            // Any queued node that never got expanded into a parent (a leaf) still needs an entry.
            var declared = new HashSet<NodeId>();
            foreach (var node in nodes)
            {
                declared.Add(node.Id);
            }

            for (var index = 0; index < nextIndex; index++)
            {
                var id = new NodeId("n" + index);
                if (!declared.Contains(id))
                {
                    nodes.Add(new NodeDocument(id, "sample.leaf", 1, children: null));
                }
            }

            return new TreeDocument("aibt.tree", 1, new TreeId("tree.test.layout-synthetic-large"), "Synthetic Large", rootId, nodes);
        }
    }
}
