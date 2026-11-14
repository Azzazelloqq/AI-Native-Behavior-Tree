using System.IO;
using System.Linq;
using AIBT.Editor.Registry;
using AIBT.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp.Discovery
{
    public sealed class McpToolDispatcherTests
    {
        private const string ValidPolicyJson = @"{
  ""format"": ""aibt.policy"",
  ""formatVersion"": 1,
  ""allowManagedNodes"": true,
  ""allowMainThreadNodes"": true,
  ""requireTreeDescription"": false,
  ""requireNodeDescriptions"": false,
  ""blackboardNaming"": ""any"",
  ""requireDeterministicNodes"": false,
  ""allowSideEffects"": true,
  ""unreachableNodes"": ""warning"",
  ""supportsAgentScope"": false,
  ""supportsSharedScope"": false,
  ""forbiddenNodeTypes"": [],
  ""warningsAsErrors"": [],
  ""performance"": { ""forbidUnboundedRepeaters"": false, ""requireEventDrivenServices"": false }
}";

        private string _projectRoot;
        private string _assetsDir;

        [SetUp]
        public void CreateTempProject()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "aibt-mcp-dispatcher-" + System.Guid.NewGuid().ToString("N"));
            _assetsDir = Path.Combine(_projectRoot, "Assets");
            Directory.CreateDirectory(_assetsDir);
            Directory.CreateDirectory(Path.Combine(_projectRoot, ".aibt"));
            File.WriteAllText(Path.Combine(_projectRoot, ".aibt", "policy.json"), ValidPolicyJson);
        }

        [TearDown]
        public void RemoveTempProject()
        {
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, recursive: true);
            }
        }

        [Test]
        public void NodeSearchWorksFromTheBridgeWorkerThread()
        {
            ProjectNodeCatalogCache.Refresh();
            var response = System.Threading.Tasks.Task.Run(() => Dispatch("search_nodes", new JObject { ["keyword"] = "inverter" }, "Read"))
                .GetAwaiter().GetResult();
            Assert.That(response["error"], Is.Null, response.ToString());
            Assert.That(((JArray)response["result"]["entries"]).Any(entry => (string)entry["typeId"] == "aibt.core.inverter"), Is.True);
        }

        [Test]
        public void ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog()
        {
            // search_nodes pages by 50 by default; the package catalog is larger since P9-002.
            var response = Dispatch("search_nodes", new JObject { ["keyword"] = "", ["count"] = 1000 }, "Read");

            var entries = (JArray)response["result"]["entries"];
            var typeIds = entries.Select(e => (string)e["typeId"]).OrderBy(id => id, System.StringComparer.Ordinal).ToArray();

            // P9-001: the package's own managed nodes (the [BtNode] classes in AIBT.Agents) are part
            // of the standard catalog, like the built-in leaves. Read straight from the attributes so
            // the expectation does not depend on the discovery code under test.
            var expectedTypeIds = AIBT.Authoring.BuiltInNodeManifests.All
                .Select(m => m.TypeId)
                .Concat(AIBT.Authoring.BuiltInLeafManifests.All.Select(p => p.Manifest.TypeId))
                .Concat(PackageManagedNodeTypeIds())
                // P9-002: the package's native-only standard shard (assembly AIBT.StandardNodes).
                .Concat(new[] { "aibt.native.bool-is-true", "aibt.native.float-at-least", "aibt.native.set-float", "aibt.native.wait-microseconds" })
                .OrderBy(id => id, System.StringComparer.Ordinal)
                .ToArray();
            Assert.That(typeIds, Is.EqualTo(expectedTypeIds),
                "With no custom nodes registered, the catalog must be exactly the built-in set -- honestly, not padded or trimmed."
                + " Extra: [" + string.Join(", ", typeIds.Except(expectedTypeIds)) + "]"
                + " Missing: [" + string.Join(", ", expectedTypeIds.Except(typeIds)) + "]");
        }

        internal static string[] PackageManagedNodeTypeIds()
        {
            var ids = typeof(AIBT.BtNode).Assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(AIBT.BtNode).IsAssignableFrom(type))
                .Select(type => (AIBT.BtNodeAttribute)System.Attribute.GetCustomAttribute(type, typeof(AIBT.BtNodeAttribute), false))
                .Where(attribute => attribute != null)
                .Select(attribute => attribute.TypeId)
                .ToArray();
            Assert.That(ids, Is.Not.Empty.And.All.StartsWith("aibt.unity."), "AIBT.Agents ships its standard nodes under aibt.unity.*.");
            return ids;
        }

        [Test]
        public void GetProjectManifestReturnsThePolicyAndScannedTrees()
        {
            File.WriteAllText(Path.Combine(_assetsDir, "sample.aibt.json"), @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 1, ""treeId"": ""tree.dispatcher-test"",
  ""name"": ""Dispatcher Test"", ""root"": ""root"",
  ""nodes"": { ""root"": { ""type"": ""aibt.action.success"", ""typeVersion"": 1 } }
}");

            var response = Dispatch("get_project_manifest", new JObject(), "Read");

            var result = response["result"];
            Assert.That((string)result["format"], Is.EqualTo("aibt-project-manifest"));
            Assert.That((bool)result["policy"]["allowSideEffects"], Is.True);
            Assert.That(((JArray)result["trees"]).Count, Is.EqualTo(1));
            Assert.That((string)((JArray)result["trees"])[0]["treeId"], Is.EqualTo("tree.dispatcher-test"));
        }

        [Test]
        public void GetNodeContractRoundTripsARealBuiltIn()
        {
            var response = Dispatch("get_node_contract", new JObject { ["typeId"] = "aibt.core.memory-sequence" }, "Read");

            Assert.That((bool)response["result"]["found"], Is.True);
            Assert.That((string)response["result"]["manifest"]["typeId"], Is.EqualTo("aibt.core.memory-sequence"));
        }

        [TestCase("get_project_manifest")]
        [TestCase("search_nodes")]
        [TestCase("get_node_contract")]
        public void EveryDiscoveryToolRejectsACallWithoutTheReadPermission(string tool)
        {
            var response = Dispatch(tool, new JObject(), grantedCategory: null);

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9012"));
            Assert.That(response["result"], Is.Null);
        }

        [Test]
        public void UnknownToolNameIsRejectedWithAStructuredDiagnostic()
        {
            var request = new JObject { ["tool"] = "not_a_real_tool", ["args"] = new JObject(), ["grantedCategories"] = new JArray("Read") };
            var response = JObject.Parse(McpToolDispatcher.Dispatch(request.ToString(Newtonsoft.Json.Formatting.None), _assetsDir));

            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9013"));
        }

        private JObject Dispatch(string tool, JObject args, string grantedCategory)
        {
            var request = new JObject
            {
                ["tool"] = tool,
                ["args"] = args,
                ["grantedCategories"] = grantedCategory == null ? new JArray() : new JArray(grantedCategory),
            };
            var responseLine = McpToolDispatcher.Dispatch(request.ToString(Newtonsoft.Json.Formatting.None), _assetsDir);
            return JObject.Parse(responseLine);
        }
    }
}
