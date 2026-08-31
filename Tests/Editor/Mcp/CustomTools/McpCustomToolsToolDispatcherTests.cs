using System.IO;
using System.Linq;
using AIBT.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp.CustomTools
{
    /// <summary>
    /// End-to-end proof that McpToolDispatcher discovers AIBT.SampleCustomTool's two providers
    /// purely via TypeCache -- this test file (in AIBT.Editor.Tests) never references that
    /// assembly, and neither does any AIBT production assembly (confirmed: AIBT.Mcp.asmdef has no
    /// reference to it). Every assertion goes through the real McpToolDispatcher.Dispatch entry
    /// point, the same one every other MCP tool test in this project uses.
    /// </summary>
    public sealed class McpCustomToolsToolDispatcherTests
    {
        private string _projectRoot;

        [SetUp]
        public void CreateTempProject()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "aibt-mcp-customtools-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectRoot);
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
        public void ListCustomToolsReturnsBothSampleProvidersWithDeclaredMetadata()
        {
            var response = Dispatch("list_custom_tools", new JObject(), "Read");

            var tools = ((JArray)response["result"]["tools"]).ToDictionary(t => (string)t["toolName"], t => t);
            Assert.That(tools.ContainsKey("aibt_custom_sample_echo"), Is.True);
            Assert.That(tools.ContainsKey("aibt_custom_sample_write_marker"), Is.True);

            var echo = tools["aibt_custom_sample_echo"];
            Assert.That((string)echo["permissionCategory"], Is.EqualTo("Read"));
            Assert.That((bool)echo["supportsDryRun"], Is.True);
            Assert.That((string)echo["owningAssembly"], Is.EqualTo("AIBT.SampleCustomTool"));

            var marker = tools["aibt_custom_sample_write_marker"];
            Assert.That((string)marker["permissionCategory"], Is.EqualTo("SemanticEdit"));
        }

        [Test]
        public void CallCustomToolPositivePathEchoesTheMessage()
        {
            var response = Dispatch(
                "call_custom_tool",
                new JObject { ["toolName"] = "aibt_custom_sample_echo", ["args"] = new JObject { ["message"] = "hello" } },
                "Read");

            Assert.That(response["error"], Is.Null);
            Assert.That((string)response["result"]["echoed"], Is.EqualTo("hello"));
        }

        [Test]
        public void CallCustomToolWithoutTheDeclaredCategoryIsRejectedAndNeverTouchesTheFile()
        {
            var markerRelativePath = "p6-010-negative-" + System.Guid.NewGuid().ToString("N") + ".marker";
            var fullPath = Path.Combine(_projectRoot, markerRelativePath);

            var response = Dispatch(
                "call_custom_tool",
                new JObject
                {
                    ["toolName"] = "aibt_custom_sample_write_marker",
                    ["args"] = new JObject { ["markerRelativePath"] = markerRelativePath },
                },
                grantedCategory: "Read"); // the tool declares SemanticEdit, not Read

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9012"));
            Assert.That(response["result"], Is.Null);
            Assert.That(File.Exists(fullPath), Is.False,
                "A Read-only session must never let a SemanticEdit-declared custom tool's Invoke run at all -- proven by the file's absence, not just by trusting the error response.");
        }

        [Test]
        public void CallCustomToolDryRunDoesNotPersist()
        {
            var markerRelativePath = "p6-010-dryrun-" + System.Guid.NewGuid().ToString("N") + ".marker";
            var fullPath = Path.Combine(_projectRoot, markerRelativePath);

            var response = Dispatch(
                "call_custom_tool",
                new JObject
                {
                    ["toolName"] = "aibt_custom_sample_write_marker",
                    ["args"] = new JObject { ["markerRelativePath"] = markerRelativePath },
                    ["dryRun"] = true,
                },
                grantedCategory: "SemanticEdit");

            Assert.That(response["error"], Is.Null);
            Assert.That((bool)response["result"]["written"], Is.False);
            Assert.That(File.Exists(fullPath), Is.False);
        }

        [Test]
        public void CallCustomToolForRealWritesTheMarkerFile()
        {
            var markerRelativePath = "p6-010-real-" + System.Guid.NewGuid().ToString("N") + ".marker";
            var fullPath = Path.Combine(_projectRoot, markerRelativePath);

            var response = Dispatch(
                "call_custom_tool",
                new JObject
                {
                    ["toolName"] = "aibt_custom_sample_write_marker",
                    ["args"] = new JObject { ["markerRelativePath"] = markerRelativePath },
                },
                grantedCategory: "SemanticEdit");

            Assert.That(response["error"], Is.Null);
            Assert.That((bool)response["result"]["written"], Is.True);
            Assert.That(File.Exists(fullPath), Is.True);
        }

        [Test]
        public void UnknownCustomToolNameIsRejectedWithAStructuredDiagnostic()
        {
            var response = Dispatch(
                "call_custom_tool",
                new JObject { ["toolName"] = "aibt_custom_does_not_exist", ["args"] = new JObject() },
                grantedCategory: "Read");

            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9041"));
        }

        [Test]
        public void ListCustomToolsRejectsACallWithoutTheReadPermission()
        {
            var response = Dispatch("list_custom_tools", new JObject(), grantedCategory: null);

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9012"));
        }

        private JObject Dispatch(string tool, JObject args, string grantedCategory)
        {
            var request = new JObject
            {
                ["tool"] = tool,
                ["args"] = args,
                ["grantedCategories"] = grantedCategory == null ? new JArray() : new JArray(grantedCategory),
            };
            var responseLine = McpToolDispatcher.Dispatch(request.ToString(Newtonsoft.Json.Formatting.None), _projectRoot);
            return JObject.Parse(responseLine);
        }
    }
}
