using System.IO;
using System.Linq;
using AIBT.Mcp;
using AIBT.Mcp.NodeDevelopment;
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

        [TestCase("../Outside")]
        [TestCase("..\\AssetsSibling\\Node")]
        [TestCase("/Rooted")]
        [TestCase("\\Rooted")]
        [TestCase("C:Relative")]
        [TestCase("C:\\Absolute")]
        [TestCase("\\\\server\\share")]
        public void MoveRejectsEscapingPathsWithoutMutation(string destination)
        {
            StagingSlot.WriteNode(_assetsDir, "Node.cs", "original");
            var before = Directory.GetFiles(_projectRoot, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
            Assert.Throws<System.ArgumentException>(() => StagingSlot.MoveTo(_assetsDir, destination));
            Assert.That(Directory.GetFiles(_projectRoot, "*", SearchOption.AllDirectories).OrderBy(p => p), Is.EqualTo(before));
            Assert.That(File.ReadAllText(Path.Combine(StagingSlot.RootPath(_assetsDir), "Node.cs")), Is.EqualTo("original"));
        }

        [TestCase("Generated/Nested/Node")]
        [TestCase("Generated\\Nested\\Node")]
        public void MoveAcceptsNestedAssetsDestination(string destination)
        {
            StagingSlot.WriteNode(_assetsDir, "Node.cs", "original");
            var moved = StagingSlot.MoveTo(_assetsDir, destination);
            Assert.That(moved, Has.Count.EqualTo(2));
            Assert.That(File.ReadAllText(Path.Combine(_assetsDir, "Generated", "Nested", "Node", "Node.cs")), Is.EqualTo("original"));
            Assert.That(StagingSlot.ListStagedFiles(_assetsDir), Is.Empty);
        }

        [Test]
        public void MoveRejectsExistingDestinationWithoutChangingStaging()
        {
            StagingSlot.WriteNode(_assetsDir, "Node.cs", "original");
            Directory.CreateDirectory(Path.Combine(_assetsDir, "Existing"));
            Assert.Throws<System.InvalidOperationException>(() => StagingSlot.MoveTo(_assetsDir, "Existing"));
            Assert.That(StagingSlot.ListStagedFiles(_assetsDir).Count, Is.EqualTo(1));
        }

        [SetUp]
        public void CreateTempProject()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "aibt-mcp-nodedev-" + System.Guid.NewGuid().ToString("N"));
            _assetsDir = Path.Combine(_projectRoot, "Assets");
            Directory.CreateDirectory(_assetsDir);
        }

        [Test]
        public void SuccessfulMoveClearsVerificationCatalogWithoutShippingIt()
        {
            StagingSlot.WriteNode(_assetsDir, "Node.cs", "node");
            StagingSlot.WriteCatalogSet(_assetsDir, "Catalog.cs", "verification only");
            var moved = StagingSlot.MoveTo(_assetsDir, "Generated/Applied");
            Assert.That(StagingSlot.ListStagedFiles(_assetsDir), Is.Empty);
            Assert.That(moved.Select(Path.GetFileName), Does.Not.Contain("Catalog.cs"));
            Assert.That(File.ReadAllText(Path.Combine(_assetsDir, "Generated/Applied/Node.cs")), Is.EqualTo("node"));
        }

        [Test]
        public void RejectedMovePreservesNodeAndVerificationCatalog()
        {
            StagingSlot.WriteNode(_assetsDir, "Node.cs", "node");
            StagingSlot.WriteCatalogSet(_assetsDir, "Catalog.cs", "verification only");
            var hash = StagingSlot.ComputeContentHash(_assetsDir);
            Assert.Throws<System.ArgumentException>(() => StagingSlot.MoveTo(_assetsDir, "../Outside"));
            Assert.That(StagingSlot.ComputeContentHash(_assetsDir), Is.EqualTo(hash));
            Assert.That(StagingSlot.ListStagedFiles(_assetsDir).Count, Is.EqualTo(2));
        }

        [Test]
        public void GeneratedDispatchRunsOnBackgroundThreadWithOwnedNativeStorage()
        {
            Assert.That(GeneratedNodeReflectionHarness.TryFindShardType("AIBT.NodeDevelopment.ShardFixture", out var shard, out var reason), Is.True, reason);
            Assert.That(GeneratedNodeReflectionHarness.TryReflectMetadata(shard, out var metadata, out reason), Is.True, reason);
            Assert.That(GeneratedNodeReflectionHarness.TryMaterializeArtifact(metadata, out var artifact, out reason), Is.True, reason);
            Assert.That(GeneratedNodeReflectionHarness.TryFindCatalogSetType("AIBT.NodeDevelopment.CatalogFixture", out var catalog, out reason), Is.True, reason);
            Assert.That(artifact.Nodes[0].Manifest.TypeId, Is.EqualTo("aibt.tests.node-development-condition"));
            GenericNodeDispatchRunner.RunResult result = default;
            System.Exception failure = null;
            var worker = new System.Threading.Thread(() =>
            {
                try { result = GenericNodeDispatchRunner.Run(artifact, catalog); }
                catch (System.Exception exception) { failure = exception; }
            }) { IsBackground = true };
            worker.Start();
            Assert.That(worker.Join(10000), Is.True, "Native dispatch must finish on the requesting thread.");
            Assert.That(failure, Is.Null, failure?.ToString());
            Assert.That(result.DispatchProven, Is.True, result.Reason);
            Assert.That(result.EnteredSuccessfully, Is.True);
            Assert.That(result.TickStatus, Is.EqualTo("Success"));
            Assert.That(result.CallbackFailure, Is.EqualTo("Success"));
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
            var catalogSetTypeName = (string)response["result"]["catalogSetTypeName"];
            Assert.That(catalogSetTypeName, Is.EqualTo("MyThresholdNodeShardCatalog"));
            Assert.That(File.Exists(Path.Combine(_assetsDir, "AIBT-Generated", "_Staging", "Pending", "Catalog", catalogSetTypeName + ".cs")), Is.True,
                "generate-node must also stage a companion [AibtCatalogSet] file (P7-009) so ExecuteImmediate is actually generated.");
            Assert.That(File.Exists(Path.Combine(_assetsDir, "AIBT-Generated", "_Staging", "Pending", "Catalog", "AIBT.Generated.Staging.Catalog.asmdef")), Is.True,
                "The companion catalog set needs its own assembly, referencing the staging assembly by name (AIBT5011 -- a shard's generated authority is only visible across an assembly reference, confirmed empirically).");
        }

        [Test]
        public void GenerateNodeCondition_BoolBlackboardType_UsesEqualityNotThresholdComparison()
        {
            // P7-009: current >= config.Threshold does not compile for bool (CS0019) -- the
            // template must emit == for Bool, unlike every other supported value type.
            var args = ConditionArgs();
            args["blackboardReadType"] = "Bool";
            var response = Dispatch("generate_node", args, "CodeGeneration");

            Assert.That(response["error"], Is.Null, response.ToString());
            var source = (string)response["result"]["source"];
            Assert.That(source, Does.Contain("BlackboardReadHandle<bool> Current;"));
            Assert.That(source, Does.Contain("current == config.Minimum"));
            Assert.That(source, Does.Not.Contain("current >= config.Minimum"));
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
            var csFiles = Directory.GetFiles(stagingDir, "*.cs", SearchOption.AllDirectories);
            // The node file plus its companion [AibtCatalogSet] file (P7-009, in its own Catalog/
            // sub-assembly) -- still one reserved slot, not accumulation: the first generation's own
            // node+catalog pair must be gone.
            Assert.That(csFiles, Has.Length.EqualTo(2), "generate-node must overwrite the single reserved slot, not accumulate files.");
            var names = csFiles.Select(Path.GetFileName).ToArray();
            Assert.That(names, Does.Contain("MyAsyncNode.cs"));
            Assert.That(names, Has.None.Contain("MyThresholdNode"), "The prior condition generation's files must be gone, not merely superseded.");
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
            // The node file plus its companion [AibtCatalogSet] file (P7-009).
            Assert.That(files.Count, Is.EqualTo(2));
            var fileNames = files.Select(file => (string)file["fileName"]).ToArray();
            Assert.That(fileNames, Does.Contain("MyThresholdNode.cs"));
            Assert.That(fileNames, Does.Contain("MyThresholdNodeShardCatalog.cs"));
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
        public void AnalyzeAndCompileNodeStartReturnsPendingWithAttemptIdentity()
        {
            Dispatch("generate_node", ConditionArgs(), "CodeGeneration");
            var response = Dispatch("analyze_and_compile_node", new JObject { ["mode"] = "start" }, "Compilation");

            Assert.That(response["error"], Is.Null, response.ToString());
            Assert.That((string)response["result"]["status"], Is.EqualTo("pending"));
            Assert.That(response["result"]["attemptId"], Is.Not.Null);
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
        public void LegacyCompileCheckRequiresAttemptIdentity()
        {
            var response = Dispatch("analyze_and_compile_node", new JObject
            {
                ["mode"] = "check", ["logPositionBefore"] = 0,
            }, "Compilation");
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9030"));
            Assert.That(response["error"].ToString(), Does.Contain("attemptId"));
        }

        [Test]
        public void ApplyRejectsEscapeWithStructuredDiagnosticBeforeHashCheck()
        {
            Dispatch("generate_node", ConditionArgs(), "CodeGeneration");
            var before = StagingSlot.ComputeContentHash(_assetsDir);
            var response = Dispatch("apply_node", new JObject
            {
                ["expectedContentHash"] = "irrelevant", ["destinationPath"] = "../Outside",
            }, "CodeGeneration");
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9030"));
            Assert.That(StagingSlot.ComputeContentHash(_assetsDir), Is.EqualTo(before));
            Assert.That(Directory.Exists(Path.Combine(_projectRoot, "Outside")), Is.False);
        }

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
