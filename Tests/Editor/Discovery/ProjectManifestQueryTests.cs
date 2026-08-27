using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Tests.Editor.NodeRegistry;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Discovery
{
    public sealed class ProjectManifestQueryTests
    {
        private const string PolicyJson = @"{
  ""format"": ""aibt.policy"",
  ""formatVersion"": 1,
  ""allowManagedNodes"": true,
  ""allowMainThreadNodes"": true,
  ""requireTreeDescription"": true,
  ""requireNodeDescriptions"": true,
  ""blackboardNaming"": ""snake_case"",
  ""requireDeterministicNodes"": true,
  ""allowSideEffects"": true,
  ""unreachableNodes"": ""error"",
  ""supportsAgentScope"": false,
  ""supportsSharedScope"": false,
  ""forbiddenNodeTypes"": [],
  ""warningsAsErrors"": [],
  ""performance"": { ""forbidUnboundedRepeaters"": true, ""requireEventDrivenServices"": false }
}";

        [Test]
        public void TreeListingReflectsRealRevisionIncludingAfterASemanticEdit()
        {
            var tree = NewTree("tree.discovery.a", "Tree A");
            Assert.That(tree.Revision.Value, Is.EqualTo(1), "TreeDocument normalizes an unset revision to 1 (Revision.IsValid check).");

            var edited = SemanticEditOperations.SetParameter(
                tree, new NodeId("root"), "enabled", SemanticValue.FromBoolean(true));
            Assert.That(edited.Revision.Value, Is.EqualTo(2), "Every semantic edit increments the revision by exactly one.");

            var query = new ProjectManifestQuery(BuildRegistry(), ReadPolicy());
            var manifest = query.Build(new[] { edited });

            var treeEntry = manifest["trees"].Single();
            Assert.That((string)treeEntry["treeId"], Is.EqualTo("tree.discovery.a"));
            Assert.That((ulong)treeEntry["revision"], Is.EqualTo(2UL));
        }

        [Test]
        public void PolicySummaryMatchesTheSourceDocumentInMeaning()
        {
            var query = new ProjectManifestQuery(BuildRegistry(), ReadPolicy());
            var manifest = query.Build(System.Array.Empty<TreeDocument>());

            var policy = manifest["policy"];
            Assert.That((bool)policy["allowManagedNodes"], Is.True);
            Assert.That((string)policy["blackboardNaming"], Is.EqualTo("snake_case"));
            Assert.That((string)policy["unreachableNodes"], Is.EqualTo("error"));
            Assert.That((bool)policy["performance"]["forbidUnboundedRepeaters"], Is.True);
        }

        [Test]
        public void TreeListingIsOrderedByTreeIdRegardlessOfInputOrder()
        {
            var query = new ProjectManifestQuery(BuildRegistry(), ReadPolicy());
            var manifest = query.Build(new[]
            {
                NewTree("tree.discovery.zebra", "Zebra"),
                NewTree("tree.discovery.alpha", "Alpha"),
            });

            var ids = manifest["trees"].Select(t => (string)t["treeId"]).ToArray();
            Assert.That(ids, Is.EqualTo(new[] { "tree.discovery.alpha", "tree.discovery.zebra" }));
        }

        private static ProjectPolicySnapshot ReadPolicy()
        {
            Assert.That(ProjectPolicySnapshot.TryParse(PolicyJson, out var snapshot, out var error), Is.True, error?.Message);
            return snapshot;
        }

        private static AIBT.Authoring.NodeRegistry BuildRegistry()
        {
            var result = new NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create("example.discovery.manifest"))
                .Build();
            Assert.That(result.Success, Is.True);
            return result.Registry;
        }

        private static TreeDocument NewTree(string treeId, string name)
        {
            var root = new NodeDocument(new NodeId("root"), "aibt.core.memory-sequence", 1, null, SemanticObject.Empty, null, null, null, TagSet.Empty);
            return new TreeDocument(
                "aibt.tree", 1, new TreeId(treeId), name, new NodeId("root"), new[] { root },
                blackboard: null, description: null, tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }
    }
}
