using System.Linq;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Editor.Graph
{
    /// <summary>
    /// P11-012 (ADR-P11-008): the Native Tree Agent inspector shows a help box when the assigned tree has
    /// random nodes and <see cref="NativeTreeAgent.RandomStreamId"/> is 0 (runs are not repeatable across
    /// sessions); none for a tree without random nodes or with an id set.
    /// </summary>
    public sealed class NativeTreeAgentRandomStreamHintTests
    {
        private const string RandomTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11012.hint-random"", ""name"": ""P11-012 hint random"",
  ""root"": ""pick"",
  ""blackboard"": { ""choice"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0 } },
  ""nodes"": {
    ""pick"": { ""type"": ""aibt.native.random-int"", ""typeVersion"": 1, ""parameters"": { ""min"": 0, ""max"": 3 }, ""bindings"": { ""key"": ""choice"" } }
  }
}";

        private const string PlainTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11012.hint-plain"", ""name"": ""P11-012 hint plain"",
  ""root"": ""hold"",
  ""nodes"": {
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } }
  }
}";

        private GameObject _gameObject;
        private ProductionTreeAsset _random;
        private ProductionTreeAsset _plain;

        [SetUp]
        public void SetUp()
        {
            _random = Prepare(RandomTree);
            _plain = Prepare(PlainTree);
            _gameObject = new GameObject("AIBT.Tests.P11012.HintAgent");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
            Object.DestroyImmediate(_random);
            Object.DestroyImmediate(_plain);
        }

        [Test]
        public void ARandomTreeWithStreamIdZero_GetsTheHint_OtherwiseNone()
        {
            var catalog = ProjectNodeCatalogCache.Current;
            Assert.That(NativeTreeAgentInspector.HasRandomNodes(_random, catalog), Is.True);
            Assert.That(NativeTreeAgentInspector.HasRandomNodes(_plain, catalog), Is.False);

            var agent = _gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = _random;
            var hint = NativeTreeAgentInspector.RandomStreamHint(agent, catalog);
            Assert.That(hint, Does.Contain("Random Stream Id").And.Contain("repeat"));
            agent.RandomStreamId = 5UL;
            Assert.That(NativeTreeAgentInspector.RandomStreamHint(agent, catalog), Is.Null, "an id is set");
            agent.RandomStreamId = 0UL;
            agent.Tree = _plain;
            Assert.That(NativeTreeAgentInspector.RandomStreamHint(agent, catalog), Is.Null, "no random node");
            agent.Tree = null;
            Assert.That(NativeTreeAgentInspector.RandomStreamHint(agent, catalog), Is.Null, "no tree");
        }

        /// <summary>
        /// P11-014 item 14 (review): the inspector caches the answer per asset. An asset prepared again in
        /// place (the Graph's Prepare Runtime copies the new content into the existing asset) must be
        /// looked at again.
        /// </summary>
        [Test]
        public void TheInspectorsCache_FollowsAnAssetPreparedAgainInPlace()
        {
            var catalog = ProjectNodeCatalogCache.Current;
            var agent = _gameObject.AddComponent<NativeTreeAgent>();
            var asset = Prepare(PlainTree);
            agent.Tree = asset;
            var inspector = (NativeTreeAgentInspector)UnityEditor.Editor.CreateEditor(agent, typeof(NativeTreeAgentInspector));
            try
            {
                Assert.That(inspector.TreeHasRandomNodes(asset, catalog), Is.False, "plain tree");
                UnityEditor.EditorUtility.CopySerialized(_random, asset);
                Assert.That(inspector.TreeHasRandomNodes(asset, catalog), Is.True, "the same asset, prepared again with a random node");
                UnityEditor.EditorUtility.CopySerialized(_plain, asset);
                Assert.That(inspector.TreeHasRandomNodes(asset, catalog), Is.False, "and back");
            }
            finally
            {
                Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(asset);
            }
        }

        private static ProductionTreeAsset Prepare(string json)
        {
            var catalog = ProjectNodeCatalogCache.Current;
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P11012/hint.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, catalog.Generated.Values, catalog.RegisteredTypes,
                new ReferenceCompilerOptions("Tests/P11012/hint.aibt.json", new ReferenceCompilationPolicy(requireDeterministicNodes: false),
                    new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            return ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(catalog.RegisteredTypes),
                new BurstCatalogFingerprint(default));
        }
    }
}
