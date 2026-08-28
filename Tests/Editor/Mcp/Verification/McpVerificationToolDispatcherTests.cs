using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp.Verification
{
    /// <summary>
    /// P6-007's real end-to-end proof at the same dispatcher entry point
    /// <see cref="AIBT.Mcp.McpBridgeListener"/> calls for every real request -- mirrors
    /// Tests/Editor/Mcp/Authoring/McpAuthoringToolDispatcherTests.cs's fixture shape. validate/
    /// compile use the real production registry (NodeRegistryBuilder.CreateWithBuiltIns());
    /// simulate is fixed to ReferencePreviewDriver's own Phase 1 fixture/built-in set, so it
    /// reuses Tests/Editor/Preview/Fixtures/success-then-running.aibt.json (P3-009's own proven
    /// fixture, already known to compile against exactly that registry) rather than a real
    /// *.aibtcase.json file -- neither positive-minimal.aibtcase.json (its "tree" field names a
    /// file that does not exist anywhere in the repo; that test drives a mock executor, not a
    /// real tree) nor patrol-react.aibtcase.json (its real tree uses aibt.test.alert-condition/
    /// aibt.test.raise-alert, node types outside ReferencePreviewFixtureEnvironment's set) is
    /// actually drivable through ReferencePreviewDriver -- a real, disclosed finding recorded in
    /// this card's evidence.
    /// </summary>
    public sealed class McpVerificationToolDispatcherTests
    {
        private const string InvertedWithNoChildTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 1, ""treeId"": ""tree.invalid-inverter"",
  ""name"": ""Invalid"", ""root"": ""root"",
  ""nodes"": { ""root"": { ""type"": ""aibt.core.inverter"", ""typeVersion"": 1 } }
}";

        private const string ValidTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 1, ""treeId"": ""tree.valid"",
  ""name"": ""Valid"", ""root"": ""root"",
  ""nodes"": { ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1 } }
}";

        private string _projectRoot;
        private string _assetsDir;

        [SetUp]
        public void CreateTempProject()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "aibt-mcp-verification-" + System.Guid.NewGuid().ToString("N"));
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

        // ---- validate ------------------------------------------------------------------------

        [Test]
        public void ValidateReturnsTheSameDiagnosticsAsADirectTreeValidatorCallByteForByte()
        {
            WriteTree("invalid.aibt.json", InvertedWithNoChildTree);
            var response = Dispatch("validate", new JObject { ["treeId"] = "tree.invalid-inverter" }, "Compilation");

            Assert.That((bool)response["result"]["valid"], Is.False);
            Assert.That((bool)response["result"]["policyApplied"], Is.False, "No .aibt/policy.json exists in this temp project.");
            var toolDiagnostics = (JArray)response["result"]["diagnostics"];
            Assert.That(toolDiagnostics, Has.Some.Matches<JToken>(d => (string)d["code"] == "AIBT2023"), "Expected the real child-count-policy violation.");

            var document = CanonicalTreeJson.Parse(File.ReadAllText(TreePath("invalid.aibt.json")), documentId: "invalid.aibt.json").Document;
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var direct = TreeValidator.Validate(document, registry, new ValidationOptions("invalid.aibt.json"));

            Assert.That(toolDiagnostics.Count, Is.EqualTo(direct.Count));
            for (var index = 0; index < direct.Count; index++)
            {
                var directJson = JObject.Parse(DiagnosticJson.Serialize(new AuthoringDiagnostic(direct[index])));
                Assert.That(JToken.DeepEquals(toolDiagnostics[index], directJson), Is.True,
                    "Diagnostic " + index + " differs.\nTool: " + toolDiagnostics[index] + "\nDirect: " + directJson);
            }
        }

        [Test]
        public void ValidateOnAValidTreeReportsValidWithNoDiagnostics()
        {
            WriteTree("valid.aibt.json", ValidTree);
            var response = Dispatch("validate", new JObject { ["treeId"] = "tree.valid" }, "Compilation");

            Assert.That((bool)response["result"]["valid"], Is.True, response.ToString());
            Assert.That(((JArray)response["result"]["diagnostics"]).Count, Is.EqualTo(0));
        }

        // ---- compile -------------------------------------------------------------------------

        [Test]
        public void CompileReturnsTheSameContentHashAsADirectReferenceCompilerCall()
        {
            WriteTree("valid.aibt.json", ValidTree);
            var response = Dispatch("compile", new JObject { ["treeId"] = "tree.valid" }, "Compilation");

            Assert.That((bool)response["result"]["success"], Is.True, response.ToString());
            var toolHash = (string)response["result"]["contentHash"];
            Assert.That(toolHash, Is.Not.Null.And.Not.Empty);

            var document = CanonicalTreeJson.Parse(File.ReadAllText(TreePath("valid.aibt.json")), documentId: "valid.aibt.json").Document;
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var options = new ReferenceCompilerOptions("valid.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0));
            var direct = ReferenceCompiler.Compile(document, registry, options);

            Assert.That(direct.Success, Is.True);
            Assert.That(toolHash, Is.EqualTo(direct.Program.Header.CompiledContentHash.HexadecimalValue));
        }

        [Test]
        public void CompileOnAnInvalidTreeReturnsDiagnosticsNeverABareBoolean()
        {
            WriteTree("invalid.aibt.json", InvertedWithNoChildTree);
            var response = Dispatch("compile", new JObject { ["treeId"] = "tree.invalid-inverter" }, "Compilation");

            Assert.That((bool)response["result"]["success"], Is.False);
            Assert.That(response["result"]["contentHash"], Is.Null.Or.Property("Type").EqualTo(JTokenType.Null));
            Assert.That(((JArray)response["result"]["diagnostics"]).Count, Is.GreaterThan(0));
        }

        // ---- simulate ------------------------------------------------------------------------

        [Test]
        public void SimulateReproducesTheSameStepAsARawReferenceExecutionMachineOracle()
        {
            WriteTree("preview.aibt.json", File.ReadAllText(PreviewFixturePath()));
            var response = Dispatch("simulate", new JObject
            {
                ["treeId"] = "tree.test.preview-success-then-running",
                ["steps"] = new JArray(new JObject { ["operation"] = "update", ["updateId"] = 1, ["snapshotRevision"] = 1, ["timeMicroseconds"] = 0 }),
            }, "TestExecution");

            Assert.That((bool)response["result"]["accepted"], Is.True, response.ToString());
            Assert.That((string)response["result"]["backend"], Does.Contain("ReferencePreviewDriver"));
            Assert.That((string)response["result"]["nodeSet"], Does.Contain("Phase 1"));

            var steps = (JArray)response["result"]["steps"];
            Assert.That(steps.Count, Is.EqualTo(1));
            Assert.That((string)steps[0]["progress"], Is.EqualTo("Waiting"), "The reference executor's own oracle behavior for this exact fixture (P3-009's ReferencePreviewParityTests) is Waiting/no terminal result after one tick -- the always-Running leaf never lets the tree reach a terminal status on its own.");
            Assert.That(steps[0]["rootResult"], Is.Null.Or.Property("Type").EqualTo(JTokenType.Null));

            var traceKinds = ((JArray)steps[0]["traceEvents"]).Select(e => (string)e["kind"]).ToList();
            Assert.That(traceKinds, Does.Contain("NodeEntered"));
            Assert.That(traceKinds, Does.Contain("NodeExited"), "Leaf 'a' (aibt.test.success) must enter and exit within the same tick.");
        }

        [Test]
        public void SimulateRejectsAStepCarryingExternalEvents()
        {
            WriteTree("preview.aibt.json", File.ReadAllText(PreviewFixturePath()));
            var response = Dispatch("simulate", new JObject
            {
                ["treeId"] = "tree.test.preview-success-then-running",
                ["steps"] = new JArray(new JObject
                {
                    ["operation"] = "update", ["updateId"] = 1, ["snapshotRevision"] = 1, ["timeMicroseconds"] = 0,
                    ["events"] = new JArray(),
                }),
            }, "TestExecution");

            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9024"));
        }

        [Test]
        public void SimulateRejectsAMismatchedUpdateId()
        {
            WriteTree("preview.aibt.json", File.ReadAllText(PreviewFixturePath()));
            var response = Dispatch("simulate", new JObject
            {
                ["treeId"] = "tree.test.preview-success-then-running",
                ["steps"] = new JArray(new JObject { ["operation"] = "update", ["updateId"] = 5, ["snapshotRevision"] = 5, ["timeMicroseconds"] = 0 }),
            }, "TestExecution");

            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9024"));
        }

        // ---- explain-diagnostic ---------------------------------------------------------------

        [Test]
        public void ExplainDiagnosticReturnsCatalogFactsForAReachableCode()
        {
            var response = Dispatch("explain_diagnostic", new JObject
            {
                ["diagnostic"] = new JObject { ["code"] = "AIBT2023", ["severity"] = "error", ["message"] = "irrelevant for this lookup" },
            }, "Read");

            Assert.That((bool)response["result"]["catalogReachable"], Is.True, response.ToString());
            Assert.That((string)response["result"]["subsystem"], Is.EqualTo("SemanticValidation"));
            Assert.That(response["result"]["suggestedOperation"], Is.Null);
        }

        [Test]
        public void ExplainDiagnosticEchoesBackASuppliedSuggestedOperationButNeverInventsOne()
        {
            var suggested = new JObject { ["operationId"] = "aibt.test.op", ["payloadType"] = "aibt.test.payload", ["payload"] = new JObject { ["x"] = 1 } };
            var withOperation = Dispatch("explain_diagnostic", new JObject
            {
                ["diagnostic"] = new JObject { ["code"] = "AIBT2023", ["suggestedOperation"] = suggested },
            }, "Read");
            Assert.That(JToken.DeepEquals(withOperation["result"]["suggestedOperation"], suggested), Is.True);

            var withoutOperation = Dispatch("explain_diagnostic", new JObject
            {
                ["diagnostic"] = new JObject { ["code"] = "AIBT2023" },
            }, "Read");
            Assert.That(withoutOperation["result"]["suggestedOperation"], Is.Null, "Never invent a suggested operation that was not supplied.");
        }

        [Test]
        public void ExplainDiagnosticReportsUnreachableForAnMcpToolLevelCodeWithNoCatalogAtAll()
        {
            // AIBT9012 (McpDiagnostics.PermissionDenied) has no DiagnosticCatalog anywhere --
            // confirmed by grep: none of McpDiagnostics/McpAuthoringDiagnostics/
            // McpVerificationDiagnostics (the AIBT9xxx MCP-tool-level codes) declare one. This is
            // permanently unreachable regardless of any InternalsVisibleTo grant, unlike AIBT3010
            // (ReferenceCompilerDiagnostics) which used to be the example here: that one is merely
            // unreachable today because its Catalog field is private, not because of a missing
            // grant -- a distinction that matters after the 2026-08-28 InternalsVisibleTo widening
            // (see below), which made AIBT.Mcp able to see three more catalogs.
            var response = Dispatch("explain_diagnostic", new JObject
            {
                ["diagnostic"] = new JObject { ["code"] = "AIBT9012" },
            }, "Read");

            Assert.That((bool)response["result"]["catalogReachable"], Is.False, response.ToString());
        }

        [TestCase("AIBT1004", "SyntaxAndSerialization")] // TreeJsonDiagnostics (Authoring)
        [TestCase("AIBT3001", "RegistryAndCompiler")] // NodeRegistryDiagnostics (Authoring)
        [TestCase("AIBT1104", "SyntaxAndSerialization")] // LayoutJsonDiagnostics (Editor)
        public void ExplainDiagnosticReturnsCatalogFactsForEachNewlyReachableCatalog(string code, string expectedSubsystem)
        {
            // Proves the 2026-08-28 InternalsVisibleTo widening (Authoring/Runtime/Editor ->
            // AIBT.Mcp) actually made these three catalogs reachable, not just that the grant
            // compiles -- each of these three codes previously reported catalogReachable: false.
            var response = Dispatch("explain_diagnostic", new JObject
            {
                ["diagnostic"] = new JObject { ["code"] = code },
            }, "Read");

            Assert.That((bool)response["result"]["catalogReachable"], Is.True, response.ToString());
            Assert.That((string)response["result"]["subsystem"], Is.EqualTo(expectedSubsystem));
            Assert.That((string)response["result"]["defaultSeverity"], Is.Not.Null.And.Not.Empty);
        }

        // ---- permission negative matrix --------------------------------------------------------

        [TestCase("validate", "Read")]
        [TestCase("compile", "Read")]
        [TestCase("simulate", "Read")]
        [TestCase("explain_diagnostic", "Compilation")]
        public void EachVerificationToolIsRejectedWithoutItsDeclaredPermission(string tool, string wrongCategory)
        {
            var response = Dispatch(tool, new JObject { ["treeId"] = "tree.valid", ["diagnostic"] = new JObject { ["code"] = "AIBT2023" } }, wrongCategory);

            Assert.That(response["error"], Is.Not.Null, "Tool '" + tool + "' must reject a call granted only '" + wrongCategory + "'.");
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9012"));
        }

        private void WriteTree(string fileName, string json)
        {
            File.WriteAllText(TreePath(fileName), json);
        }

        private string TreePath(string fileName) => Path.Combine(_assetsDir, fileName);

        private static string PreviewFixturePath()
            => EditorTestPackagePaths.Resolve("Tests", "Editor", "Preview", "Fixtures", "success-then-running.aibt.json");

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
