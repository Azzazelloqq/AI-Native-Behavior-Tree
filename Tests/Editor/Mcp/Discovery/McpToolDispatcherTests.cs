using System.IO;
using System.Linq;
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
        public void ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog()
        {
            var response = Dispatch("search_nodes", new JObject { ["keyword"] = "" }, "Read");

            var entries = (JArray)response["result"]["entries"];
            var typeIds = entries.Select(e => (string)e["typeId"]).OrderBy(id => id, System.StringComparer.Ordinal).ToArray();

            var expectedTypeIds = AIBT.Authoring.BuiltInNodeManifests.All
                .Select(m => m.TypeId)
                .OrderBy(id => id, System.StringComparer.Ordinal)
                .ToArray();
            Assert.That(typeIds, Is.EqualTo(expectedTypeIds),
                "With no custom nodes registered, the catalog must be exactly the built-in set -- honestly, not padded or trimmed.");
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
