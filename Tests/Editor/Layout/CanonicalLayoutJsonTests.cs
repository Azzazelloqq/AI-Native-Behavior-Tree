using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Layout;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Layout
{
    public sealed class CanonicalLayoutJsonTests
    {
        [Test]
        public void RoundTripsGroupsNotesAndReroutesByteIdentically()
        {
            var tree = ParseTreeFixture("shallow-wide-4.aibt.json");
            var document = BuildDocumentWithOrganization(tree);

            var firstBytes = CanonicalLayoutJsonWriter.Write(document);
            var parsed = CanonicalLayoutJson.Parse(firstBytes, "in-memory", tree);
            Assert.That(parsed.Success, Is.True, DiagnosticMessages(parsed.Diagnostics));

            var secondBytes = CanonicalLayoutJsonWriter.Write(parsed.Document);
            Assert.That(secondBytes, Is.EqualTo(firstBytes));
        }

        [Test]
        public void RejectsUnknownNodeReference()
        {
            var tree = ParseTreeFixture("shallow-wide-4.aibt.json");
            var json = "{\"format\":\"aibt.layout\",\"formatVersion\":1,\"treeId\":\"" + tree.TreeId.Value + "\","
                + "\"nodes\":{\"nonexistent\":{\"position\":{\"x\":0,\"y\":0}}}}";

            var result = CanonicalLayoutJson.Parse(json, "invalid", tree);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(LayoutJsonDiagnosticCodes.UnknownNodeReference));
        }

        [Test]
        public void RejectsTreeIdMismatch()
        {
            var tree = ParseTreeFixture("shallow-wide-4.aibt.json");
            var json = "{\"format\":\"aibt.layout\",\"formatVersion\":1,\"treeId\":\"tree.test.some-other-tree\",\"nodes\":{}}";

            var result = CanonicalLayoutJson.Parse(json, "invalid", tree);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(LayoutJsonDiagnosticCodes.TreeIdMismatch));
        }

        [Test]
        public void RejectsDuplicateGroupMembership()
        {
            var tree = ParseTreeFixture("shallow-wide-4.aibt.json");
            var json = "{\"format\":\"aibt.layout\",\"formatVersion\":1,\"treeId\":\"" + tree.TreeId.Value + "\","
                + "\"nodes\":{},"
                + "\"groups\":{"
                + "\"g1\":{\"title\":\"G1\",\"memberNodeIds\":[\"a\"]},"
                + "\"g2\":{\"title\":\"G2\",\"memberNodeIds\":[\"a\"]}"
                + "}}";

            var result = CanonicalLayoutJson.Parse(json, "invalid", tree);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(LayoutJsonDiagnosticCodes.NodeInMultipleGroups));
        }

        [Test]
        public void RejectsOrphanedReroute()
        {
            var tree = ParseTreeFixture("shallow-wide-4.aibt.json");
            var json = "{\"format\":\"aibt.layout\",\"formatVersion\":1,\"treeId\":\"" + tree.TreeId.Value + "\","
                + "\"nodes\":{},"
                + "\"reroutes\":{\"a|b\":{\"waypoints\":[{\"x\":0,\"y\":0}]}}}";

            var result = CanonicalLayoutJson.Parse(json, "invalid", tree);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(LayoutJsonDiagnosticCodes.OrphanedReroute));
        }

        [Test]
        public void RejectsInvalidDirection()
        {
            var json = "{\"format\":\"aibt.layout\",\"formatVersion\":1,\"treeId\":\"tree.test.x\",\"direction\":\"sideways\",\"nodes\":{}}";

            var result = CanonicalLayoutJson.Parse(json);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(LayoutJsonDiagnosticCodes.InvalidDirection));
        }

        [Test]
        public void RejectsDuplicateProperty()
        {
            var json = "{\"format\":\"aibt.layout\",\"format\":\"aibt.layout\",\"formatVersion\":1,\"treeId\":\"tree.test.x\",\"nodes\":{}}";

            var result = CanonicalLayoutJson.Parse(json);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(LayoutJsonDiagnosticCodes.DuplicateProperty));
        }

        internal static TreeDocument ParseTreeFixture(string fileName)
        {
            var path = EditorTestPackagePaths.Resolve("Tests", "Editor", "Layout", "Fixtures", fileName);
            var result = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
            Assert.That(result.Success, Is.True);
            return result.Document;
        }

        internal static LayoutDocument BuildDocumentWithOrganization(TreeDocument tree)
        {
            var baseLayout = DeterministicAutoLayoutService.Layout(tree);
            var nodes = new Dictionary<NodeId, LayoutNodePlacement>(baseLayout.Nodes)
            {
                [new NodeId("a")] = new LayoutNodePlacement(new LayoutPoint(10, 20), pinned: true),
            };

            var groups = new Dictionary<string, LayoutGroup>
            {
                ["g1"] = new LayoutGroup("Group One", new[] { new NodeId("a"), new NodeId("b") }, "desc", "#FF0000", locked: true),
            };
            var notes = new Dictionary<string, LayoutNote>
            {
                ["n1"] = new LayoutNote("A note", new LayoutPoint(1, 2), new LayoutPoint(100, 50), "#00FF00"),
            };
            var reroutes = new Dictionary<LayoutEdgeKey, LayoutReroute>
            {
                [new LayoutEdgeKey(new NodeId("root"), new NodeId("c"))] = new LayoutReroute(new[] { new LayoutPoint(5, 5) }),
            };

            return new LayoutDocument(tree.TreeId, LayoutDirection.TopToBottom, nodes, groups, notes, reroutes);
        }

        private static string DiagnosticMessages(DiagnosticCollection diagnostics)
        {
            return diagnostics == null ? string.Empty : string.Join("; ", diagnostics.Select(d => d.Code.Value + ": " + d.Message));
        }
    }
}
