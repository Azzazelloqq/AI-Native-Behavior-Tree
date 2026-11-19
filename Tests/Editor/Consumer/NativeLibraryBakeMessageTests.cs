using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using NUnit.Framework;

namespace AIBT.Tests.Consumer
{
    /// <summary>
    /// P11-002 (ADR-P11-002 §4.2): baking a tree that uses a package library node with a catalog set
    /// that does not list <c>StandardNativeShard</c> keeps the same diagnostic and conditions, and
    /// its message names the fix. A node of any other shard keeps the old message.
    /// </summary>
    public sealed class NativeLibraryBakeMessageTests
    {
        private const string TreeJson = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11002.bake-message"", ""name"": ""P11-002 bake message"",
  ""root"": ""root"",
  ""blackboard"": {
    ""armed"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""arm"", ""other""] },
    ""arm"": { ""type"": ""aibt.native.set-bool"", ""typeVersion"": 1, ""parameters"": { ""value"": true }, ""bindings"": { ""key"": ""armed"" } },
    ""other"": { ""type"": ""aibt.tests.p11002.not-in-any-shard"", ""typeVersion"": 1 }
  }
}";

        [Test]
        public void AnUnlistedPackageLibraryNodeNamesTheShardToAdd()
        {
            var parsed = CanonicalTreeJson.Parse(TreeJson, "trees/p11002-bake-message.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));

            var asset = ProductionTreeAssetBaker.Create(parsed.Document, "trees/p11002-bake-message.aibt.json", typeof(ConsumerCatalog), out var diagnostics);

            Assert.That(asset, Is.Null);
            var byNode = diagnostics.ToDictionary(d => d.Location.NodeId.Value);
            Assert.That(byNode.Keys, Is.EquivalentTo(new[] { "arm", "other" }));
            Assert.That(diagnostics.Select(d => d.Code), Is.All.EqualTo(ReferenceCompilerDiagnosticCodes.UnsupportedCapability));
            Assert.That(diagnostics.Select(d => d.Severity), Is.All.EqualTo(DiagnosticSeverity.Error));
            Assert.That(byNode["arm"].Message, Is.EqualTo(
                "Selected native catalog does not declare node 'aibt.native.set-bool'. It is a package node of shard aibt.native.standard:"
                + " add typeof(AIBT.StandardNodes.StandardNativeShard) to your catalog set ([AibtCatalogSet])."));
            Assert.That(byNode["other"].Message, Is.EqualTo(
                "Selected native catalog does not declare node 'aibt.tests.p11002.not-in-any-shard'."));
        }
    }
}
