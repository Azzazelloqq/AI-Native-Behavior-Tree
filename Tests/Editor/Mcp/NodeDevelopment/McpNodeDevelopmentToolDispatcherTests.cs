using System.IO;
using AIBT.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp.NodeDevelopment
{
    /// <summary>
    /// P6-009's dispatcher-level proof, mirroring every other P6 card's own
    /// McpToolDispatcher.Dispatch-entry-point pattern. Scoped (disclosed in this card's evidence)
    /// to what is reliably testable synchronously: generate/preview/tests-scaffold (pure file I/O,
    /// no compilation), analyze-and-compile-node's 'start' call and malformed-input paths, and
    /// test-node/apply-node's stale-hash/no-pending-generation refusal paths. The full gate
    /// through a real compile (analyze-and-compile-node's 'check' reaching status 'compiled',
    /// test-node/apply-node's happy path) triggers a genuine Unity domain reload that can outlive
    /// a single NUnit EditMode test method -- no other P6 card's tools ever compiled anything, so
    /// none needed this; this card's own live end-to-end verification (see Planning~/Evidence/
    /// P6-009/) exercises that path instead, matching the card's own "real MCP client" requirement.
    /// </summary>
    public sealed class McpNodeDevelopmentToolDispatcherTests
    {
        private string _projectRoot;
        private string _assetsDir;

        [SetUp]
        public void CreateTempProject()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "aibt-mcp-nodedev-" + System.Guid.NewGuid().ToString("N"));
            _assetsDir = Path.Combine(_projectRoot, "Assets");
            Directory.CreateDirectory(_assetsDir);
        }

        [TearDown]
        public void RemoveTempProject()
        {
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, recursive: true);
            }
        }

        // ---- generate-node ----------------------------------------------------------------------

        [Test]
        public void GenerateNodeConditionWritesExpectedStagedSource()
        {
            var response = Dispatch("generate_node", ConditionArgs(), "CodeGeneration");

            Assert.That(response["error"], Is.Null, response.ToString());
            var fileName = (string)response["result"]["fileName"];
            Assert.That(fileName, Is.EqualTo("MyThresholdNode.cs"));
            var source = (string)response["result"]["source"];
            Assert.That(source, Does.Contain("[AibtBurstNode(").And.Contain("\"aibt.mcp-test.my-threshold\"").And.Contain("BurstNodeKind.Condition"));
            Assert.That(source, Does.Contain("BlackboardReadHandle<uint> Current;"));
            Assert.That(File.Exists(Path.Combine(_assetsDir, "AIBT-Generated", "_Staging", "Pending", fileName)), Is.True);
            Assert.That(File.Exists(Path.Combine(_assetsDir, "AIBT-Generated", "_Staging", "Pending", "AIBT.Generated.Staging.asmdef")), Is.True);
        }

        [Test]
        public void GenerateNodeActionWritesExpectedStagedSource()
        {
            var response = Dispatch("generate_node", ActionArgs(), "CodeGeneration");

            Assert.That(response["error"], Is.Null, response.ToString());
            var fileName = (string)response["result"]["fileName"];
            Assert.That(fileName, Is.EqualTo("MyAsyncNode.cs"));
            var source = (string)response["result"]["source"];
            Assert.That(source, Does.Contain("BurstNodeKind.Action").And.Contain("BurstCancellationMode.Command"));
            Assert.That(source, Does.Contain("AsyncOperationHandle<int, int> Operation;"));
        }

        [Test]
        public void GenerateNodeUnknownKindReportsStructuredDiagnostic()
        {
            var args = ConditionArgs();
            args["kind"] = "decorator";
            var response = Dispatch("generate_node", args, "CodeGeneration");

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9031"));
        }

        [Test]
        public void GenerateNodeOverwritesAnyPriorPendingGeneration()
        {
            Dispatch("generate_node", ConditionArgs(), "CodeGeneration");
            Dispatch("generate_node", ActionArgs(), "CodeGeneration");

            var stagingDir = Path.Combine(_assetsDir, "AIBT-Generated", "_Staging", "Pending");
            var csFiles = Directory.GetFiles(stagingDir, "*.cs");
            Assert.That(csFiles, Has.Length.EqualTo(1), "generate-node must overwrite the single reserved slot, not accumulate files.");
            Assert.That(Path.GetFileName(csFiles[0]), Is.EqualTo("MyAsyncNode.cs"));
        }

        // ---- preview-node-diff --------------------------------------------------------------------

        [Test]
        public void PreviewNodeDiffReflectsStagedContentWithoutMutatingDisk()
        {
            Dispatch("generate_node", ConditionArgs(), "CodeGeneration");
            var stagingDir = Path.Combine(_assetsDir, "AIBT-Generated", "_Staging", "Pending");
            var before = Directory.GetFiles(stagingDir).Length;

            var response = Dispatch("preview_node_diff", new JObject(), "Read");

            Assert.That(response["error"], Is.Null, response.ToString());
            var files = (JArray)response["result"]["files"];
            Assert.That(files.Count, Is.EqualTo(1));
            Assert.That((string)files[0]["fileName"], Is.EqualTo("MyThresholdNode.cs"));
            Assert.That(Directory.GetFiles(stagingDir).Length, Is.EqualTo(before), "preview-node-diff must not change staged file count.");
        }

        // ---- generate-node-tests-and-manifest -----------------------------------------------------

        [Test]
        public void GenerateNodeTestsAndManifestWithNoPendingGenerationReportsStructuredDiagnostic()
        {
            var response = Dispatch("generate_node_tests_and_manifest", new JObject(), "CodeGeneration");

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9032"));
        }

        [Test]
        public void GenerateNodeTestsAndManifestWritesScaffoldNextToStagedNode()
        {
            Dispatch("generate_node", ConditionArgs(), "CodeGeneration");
            var response = Dispatch("generate_node_tests_and_manifest", new JObject(), "CodeGeneration");

            Assert.That(response["error"], Is.Null, response.ToString());
            var fileName = (string)response["result"]["fileName"];
            Assert.That(fileName, Is.EqualTo("MyThresholdNodeTests.cs"));
            Assert.That(File.Exists(Path.Combine(_assetsDir, "AIBT-Generated", "_Staging", "Pending", fileName)), Is.True);
        }

        // ---- analyze-and-compile-node --------------------------------------------------------------

        [Test]
        public void AnalyzeAndCompileNodeStartWithNoPendingGenerationReportsStructuredDiagnostic()
        {
            var response = Dispatch("analyze_and_compile_node", new JObject { ["mode"] = "start" }, "Compilation");

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9032"));
        }

        [Test]
        public void AnalyzeAndCompileNodeStartReturnsPendingWithLogPosition()
        {
            Dispatch("generate_node", ConditionArgs(), "CodeGeneration");
            var response = Dispatch("analyze_and_compile_node", new JObject { ["mode"] = "start" }, "Compilation");

            Assert.That(response["error"], Is.Null, response.ToString());
            Assert.That((string)response["result"]["status"], Is.EqualTo("pending"));
            Assert.That(response["result"]["logPositionBefore"], Is.Not.Null);
        }

        [Test]
        public void AnalyzeAndCompileNodeUnknownModeReportsStructuredDiagnostic()
        {
            var response = Dispatch("analyze_and_compile_node", new JObject { ["mode"] = "finish" }, "Compilation");

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9030"));
        }

        // ---- test-node / apply-node stale-hash refusal ----------------------------------------------

        [Test]
        public void TestNodeWithStaleHashIsRejected()
        {
            Dispatch("generate_node", ConditionArgs(), "CodeGeneration");
            var response = Dispatch("test_node", new JObject { ["expectedContentHash"] = "not-the-real-hash" }, "TestExecution");

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9032"));
        }

        [Test]
        public void ApplyNodeWithStaleHashIsRejected()
        {
            Dispatch("generate_node", ConditionArgs(), "CodeGeneration");
            var response = Dispatch("apply_node", new JObject
            {
                ["expectedContentHash"] = "not-the-real-hash",
                ["destinationPath"] = "SomeProject/GeneratedNodes/MyThreshold",
            }, "CodeGeneration");

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9032"));
        }

        // ---- permission negative matrix --------------------------------------------------------

        [TestCase("generate_node", "Read")]
        [TestCase("preview_node_diff", "CodeGeneration")]
        [TestCase("generate_node_tests_and_manifest", "Read")]
        [TestCase("analyze_and_compile_node", "Read")]
        [TestCase("test_node", "Read")]
        [TestCase("apply_node", "Read")]
        public void EachNodeDevelopmentToolIsRejectedWithoutItsDeclaredPermission(string tool, string wrongCategory)
        {
            var args = ConditionArgs();
            args["mode"] = "start";
            args["expectedContentHash"] = "irrelevant";
            args["destinationPath"] = "SomeProject/GeneratedNodes/MyThreshold";
            var response = Dispatch(tool, args, wrongCategory);

            Assert.That(response["error"], Is.Not.Null, "Tool '" + tool + "' must reject a call granted only '" + wrongCategory + "'.");
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9012"));
        }

        private static JObject ConditionArgs() => new JObject
        {
            ["kind"] = "condition",
            ["typeId"] = "aibt.mcp-test.my-threshold",
            ["namespace"] = "AibtMcpTest.Generated",
            ["summary"] = "Test condition.",
            ["category"] = "McpTest/Conditions",
            ["whenToUse"] = "In a test.",
            ["whenNotToUse"] = "Never in production.",
            ["blackboardReadKey"] = "current",
            ["blackboardReadType"] = "UInt32",
        };

        private static JObject ActionArgs() => new JObject
        {
            ["kind"] = "action",
            ["typeId"] = "aibt.mcp-test.my-async",
            ["namespace"] = "AibtMcpTest.Generated",
            ["summary"] = "Test action.",
            ["category"] = "McpTest/Actions",
            ["whenToUse"] = "In a test.",
            ["whenNotToUse"] = "Never in production.",
            ["blackboardReadKey"] = "source",
            ["blackboardReadType"] = "Int32",
            ["blackboardWriteKey"] = "destination",
            ["blackboardWriteType"] = "Int32",
            ["commandKey"] = "effect",
            ["commandType"] = "Int32",
            ["asyncOperationKey"] = "operation",
            ["asyncStartType"] = "Int32",
            ["asyncCompletionType"] = "Int32",
            ["completionKey"] = "completion",
        };

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
