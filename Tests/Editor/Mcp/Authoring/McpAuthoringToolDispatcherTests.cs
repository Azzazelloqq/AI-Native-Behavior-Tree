using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp.Authoring
{
    /// <summary>
    /// P6-006's real end-to-end proof at the same dispatcher entry point
    /// <see cref="AIBT.Mcp.McpBridgeListener"/> calls for every real request -- mirrors
    /// Tests/Editor/Mcp/Discovery/McpToolDispatcherTests.cs's exact fixture shape. Every fixture
    /// tree uses only genuinely built-in production node types (aibt.core.*), matching
    /// McpAuthoringToolDispatcher's own production registry (NodeRegistryBuilder.CreateWithBuiltIns(),
    /// no test fixtures) -- an empty aibt.core.memory-sequence (0..N children per its own
    /// NodeChildPolicy) stands in for a "generic node," since Phase 1's built-in catalog has no
    /// zero-child leaf action type at all (only aibt.core.repeater has simple, unambiguous
    /// parameters: count/UInt32, stopOnFailure/Boolean).
    ///
    /// Every mutating call's precondition is "expectedHash" (a semantic content hash), not a
    /// revision counter: TreeDocument.Revision is never persisted to *.aibt.json (a real,
    /// pre-existing gap found while building this suite -- CanonicalTreeJsonWriter never writes
    /// it, CanonicalTreeJson.ReadDocument hard-codes `default` on every parse), so it always
    /// resets to 1 across the reload-per-call boundary every real MCP call crosses. The dispatcher
    /// checks a computed hash instead, the same fix ADR-P6-002 already made for LayoutDocument.
    /// </summary>
    public sealed class McpAuthoringToolDispatcherTests
    {
        private string _projectRoot;
        private string _assetsDir;

        [SetUp]
        public void CreateTempProject()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "aibt-mcp-authoring-" + System.Guid.NewGuid().ToString("N"));
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

        [Test]
        public void FullAuthoringSessionCreatesAddsConfiguresAndReadsBackDiffAndContentHash()
        {
            var created = Dispatch("create_tree", new JObject
            {
                ["treeId"] = "tree.session",
                ["name"] = "Session Tree",
                ["path"] = "session.aibt.json",
                ["rootNode"] = new JObject { ["id"] = "root", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
            }, "SemanticEdit");

            Assert.That((bool)created["result"]["accepted"], Is.True, created.ToString());
            var hashAfterCreate = (string)created["result"]["contentHash"];
            Assert.That(hashAfterCreate, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(Path.Combine(_assetsDir, "session.aibt.json")), Is.True);

            // A repeater decorator always requires exactly one child, so adding it and its own
            // child happens atomically in one composed patch (intermediate states inside a
            // multi-operation patch are never separately validated -- only the final document is).
            var patched = Dispatch("apply_domain_patch", new JObject
            {
                ["treeId"] = "tree.session",
                ["expectedHash"] = hashAfterCreate,
                ["operations"] = new JArray(
                    new JObject
                    {
                        ["op"] = "add",
                        ["parentId"] = "root",
                        ["node"] = new JObject
                        {
                            ["id"] = "repeater",
                            ["typeId"] = BuiltInNodeManifests.RepeaterTypeId,
                            ["typeVersion"] = 1,
                            ["parameters"] = new JObject { ["count"] = 3, ["stopOnFailure"] = false },
                        },
                    },
                    new JObject { ["op"] = "add", ["parentId"] = "repeater", ["node"] = new JObject { ["id"] = "leaf", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 } }),
            }, "SemanticEdit");

            Assert.That((bool)patched["result"]["accepted"], Is.True, patched.ToString());
            var hashAfterPatch = (string)patched["result"]["contentHash"];
            Assert.That(hashAfterPatch, Is.Not.EqualTo(hashAfterCreate), "A real accepted patch must change the content hash.");

            var configured = Dispatch("configure_node", new JObject
            {
                ["treeId"] = "tree.session",
                ["expectedHash"] = hashAfterPatch,
                ["nodeId"] = "repeater",
                ["parameterName"] = "stopOnFailure",
                ["value"] = true,
            }, "SemanticEdit");

            Assert.That((bool)configured["result"]["accepted"], Is.True, configured.ToString());
            var hashAfterConfigure = (string)configured["result"]["contentHash"];
            Assert.That(hashAfterConfigure, Is.Not.EqualTo(hashAfterPatch));
            var diffEntries = (JArray)configured["result"]["diff"]["entries"];
            Assert.That(diffEntries, Has.Some.Matches<JToken>(e => (string)e["nodeId"] == "repeater" && (string)e["kind"] == "Changed"));

            var onDisk = CanonicalTreeJson.Parse(File.ReadAllText(Path.Combine(_assetsDir, "session.aibt.json")), documentId: "session.aibt.json");
            Assert.That(onDisk.Success, Is.True);
            var repeater = onDisk.Document.Nodes.Single(n => n.Id.Value == "repeater");
            repeater.Parameters.TryGetValue("stopOnFailure", out var stopOnFailureValue);
            stopOnFailureValue.TryGetBoolean(out var stopOnFailure);
            Assert.That(stopOnFailure, Is.True, "The persisted file must reflect the last accepted patch.");
        }

        [Test]
        public void DryRunProducesTheSameResultAsARealCallAndPersistsNothing()
        {
            var initialHash = CreateSessionTree();
            var beforeBytes = File.ReadAllBytes(TreePath());
            var addArgs = new JObject
            {
                ["treeId"] = "tree.session", ["expectedHash"] = initialHash, ["parentId"] = "root",
                ["node"] = new JObject { ["id"] = "child", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
            };

            var dryRun = Dispatch("add_node", new JObject(addArgs) { ["dryRun"] = true }, "SemanticEdit");
            Assert.That((bool)dryRun["result"]["accepted"], Is.True, dryRun.ToString());

            var afterDryRunBytes = File.ReadAllBytes(TreePath());
            Assert.That(afterDryRunBytes, Is.EqualTo(beforeBytes), "A dry-run call must never touch the persisted file.");

            var reloaded = CanonicalTreeJson.Parse(File.ReadAllText(TreePath()), documentId: TreePath());
            Assert.That(reloaded.Success, Is.True);
            Assert.That(reloaded.Document.Nodes.Count, Is.EqualTo(1), "A follow-up read must still see just the root node, unchanged, after a dry-run.");

            var realRun = Dispatch("add_node", addArgs, "SemanticEdit");
            Assert.That((bool)realRun["result"]["accepted"], Is.True);
            Assert.That((bool)realRun["result"]["accepted"], Is.EqualTo((bool)dryRun["result"]["accepted"]), "A dry-run must produce the same accept/reject outcome as the real call.");
            Assert.That(File.ReadAllBytes(TreePath()), Is.Not.EqualTo(beforeBytes), "The real (non-dry-run) call must persist.");
        }

        [TestCase("add_node", "SemanticEdit")]
        [TestCase("remove_node", "SemanticEdit")]
        [TestCase("move_node", "SemanticEdit")]
        [TestCase("replace_node", "SemanticEdit")]
        [TestCase("configure_node", "SemanticEdit")]
        [TestCase("set_blackboard_keys", "SemanticEdit")]
        [TestCase("extract_subtree", "SemanticEdit")]
        [TestCase("inline_subtree", "SemanticEdit")]
        [TestCase("apply_domain_patch", "SemanticEdit")]
        public void ASemanticEditToolIsRejectedForASessionHoldingOnlyLayoutEditPermission(string tool, string requiredCategory)
        {
            CreateSessionTree();
            var response = Dispatch(tool, new JObject { ["treeId"] = "tree.session", ["expectedHash"] = "irrelevant-permission-checked-first" }, "LayoutEdit");

            Assert.That(response["error"], Is.Not.Null, "Tool '" + tool + "' declares " + requiredCategory + " but was granted only LayoutEdit.");
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9012"));
        }

        [Test]
        public void ALayoutEditToolIsRejectedForASessionHoldingOnlyReadPermission()
        {
            CreateSessionTree();
            var response = Dispatch("request_layout", new JObject { ["treeId"] = "tree.session" }, "Read");

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9012"));
        }

        [Test]
        public void ContentHashMismatchIsRejectedBeforeAnyOperationRunsThroughTheRealDispatcher()
        {
            CreateSessionTree();
            var response = Dispatch("remove_node", new JObject { ["treeId"] = "tree.session", ["expectedHash"] = "not-the-real-hash", ["nodeId"] = "root" }, "SemanticEdit");

            Assert.That((bool)response["result"]["accepted"], Is.False);
            Assert.That((string)response["result"]["diagnostics"][0]["code"], Is.EqualTo("AIBT9021"));
            var onDisk = CanonicalTreeJson.Parse(File.ReadAllText(TreePath()), documentId: TreePath());
            Assert.That(onDisk.Document.Nodes.Count, Is.EqualTo(1), "root must be the only node -- remove_node must never have run.");
        }

        [Test]
        public void MoveNodeRelocatesUnderTheNewParentAndRemovesTheOldEdge()
        {
            var hash = CreateSessionTree();
            var addResult = Dispatch("apply_domain_patch", new JObject
            {
                ["treeId"] = "tree.session",
                ["expectedHash"] = hash,
                ["operations"] = new JArray(
                    new JObject { ["op"] = "add", ["parentId"] = "root", ["node"] = new JObject { ["id"] = "a", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 } },
                    new JObject { ["op"] = "add", ["parentId"] = "root", ["node"] = new JObject { ["id"] = "b", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 } }),
            }, "SemanticEdit");
            Assert.That((bool)addResult["result"]["accepted"], Is.True, addResult.ToString());

            var moved = Dispatch("move_node", new JObject { ["treeId"] = "tree.session", ["expectedHash"] = (string)addResult["result"]["contentHash"], ["nodeId"] = "b", ["newParentId"] = "a" }, "SemanticEdit");
            Assert.That((bool)moved["result"]["accepted"], Is.True, moved.ToString());

            var onDisk = CanonicalTreeJson.Parse(File.ReadAllText(TreePath()), documentId: TreePath()).Document;
            var root = onDisk.Nodes.Single(n => n.Id.Value == "root");
            var a = onDisk.Nodes.Single(n => n.Id.Value == "a");
            Assert.That(root.Children.Select(c => c.Value), Is.EquivalentTo(new[] { "a" }), "b must no longer be a direct child of root.");
            Assert.That(a.Children.Select(c => c.Value), Is.EquivalentTo(new[] { "b" }));
        }

        [Test]
        public void ReplaceNodeSwapsTypeAndParametersKeepingIdAndChildren()
        {
            var hash = CreateSessionTree();
            var addResult = Dispatch("add_node", new JObject
            {
                ["treeId"] = "tree.session", ["expectedHash"] = hash, ["parentId"] = "root",
                ["node"] = new JObject { ["id"] = "child", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
            }, "SemanticEdit");
            Assert.That((bool)addResult["result"]["accepted"], Is.True, addResult.ToString());

            var replaced = Dispatch("replace_node", new JObject
            {
                ["treeId"] = "tree.session", ["expectedHash"] = (string)addResult["result"]["contentHash"], ["nodeId"] = "child",
                ["newTypeId"] = BuiltInNodeManifests.ReactiveSelectorTypeId, ["newTypeVersion"] = 1,
            }, "SemanticEdit");

            Assert.That((bool)replaced["result"]["accepted"], Is.True, replaced.ToString());
            var onDisk = CanonicalTreeJson.Parse(File.ReadAllText(TreePath()), documentId: TreePath()).Document;
            var child = onDisk.Nodes.Single(n => n.Id.Value == "child");
            Assert.That(child.TypeId, Is.EqualTo(BuiltInNodeManifests.ReactiveSelectorTypeId));
            Assert.That(onDisk.Nodes.Single(n => n.Id.Value == "root").Children.Select(c => c.Value), Is.EquivalentTo(new[] { "child" }), "Replace must not change the node's position in the tree.");
        }

        [Test]
        public void SetBlackboardKeysReplacesTheFullDeclaration()
        {
            var hash = CreateSessionTree();
            var response = Dispatch("set_blackboard_keys", new JObject
            {
                ["treeId"] = "tree.session", ["expectedHash"] = hash,
                ["keys"] = new JArray(new JObject { ["id"] = "health", ["valueType"] = "Float32", ["description"] = "Current health." }),
            }, "SemanticEdit");

            Assert.That((bool)response["result"]["accepted"], Is.True, response.ToString());
            var onDisk = CanonicalTreeJson.Parse(File.ReadAllText(TreePath()), documentId: TreePath()).Document;
            Assert.That(onDisk.Blackboard.Count, Is.EqualTo(1));
            Assert.That(onDisk.Blackboard[0].Id, Is.EqualTo("health"));
        }

        [Test]
        public void CreateTreeWithAgentScopeKeyRequiresPolicyOptInAndWritesV2()
        {
            var withoutOptIn = Dispatch("create_tree", new JObject
            {
                ["treeId"] = "tree.agent",
                ["name"] = "Agent Tree",
                ["path"] = "agent.aibt.json",
                ["rootNode"] = new JObject { ["id"] = "root", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
                ["blackboard"] = new JArray(new JObject
                {
                    ["id"] = "danger", ["valueType"] = "Bool", ["scope"] = "agent", ["default"] = false,
                }),
                ["agentContract"] = new JObject { ["contractId"] = "contract.agent", ["contractVersion"] = 1 },
            }, "SemanticEdit");

            Assert.That((bool)withoutOptIn["result"]["accepted"], Is.False,
                "Without a project policy opting in, Agent scope must still be rejected exactly as before.");
            Assert.That(File.Exists(Path.Combine(_assetsDir, "agent.aibt.json")), Is.False);

            WritePolicy(supportsAgentScope: true, supportsSharedScope: false);

            var withOptIn = Dispatch("create_tree", new JObject
            {
                ["treeId"] = "tree.agent",
                ["name"] = "Agent Tree",
                ["path"] = "agent.aibt.json",
                ["rootNode"] = new JObject { ["id"] = "root", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
                ["blackboard"] = new JArray(new JObject
                {
                    ["id"] = "danger", ["valueType"] = "Bool", ["scope"] = "agent", ["default"] = false,
                }),
                ["agentContract"] = new JObject { ["contractId"] = "contract.agent", ["contractVersion"] = 1 },
            }, "SemanticEdit");

            Assert.That((bool)withOptIn["result"]["accepted"], Is.True, withOptIn.ToString());
            var written = File.ReadAllText(Path.Combine(_assetsDir, "agent.aibt.json"));
            var document = CanonicalTreeJson.Parse(written, documentId: "agent.aibt.json").Document;
            Assert.That(document.FormatVersion, Is.EqualTo(2));
            Assert.That(document.Blackboard.Single().Scope, Is.EqualTo(BlackboardScope.Agent));
            Assert.That(document.AgentContract.ContractId, Is.EqualTo("contract.agent"));
        }

        [Test]
        public void CreateTreeWithOnlyTreeScopeKeysStillWritesV1()
        {
            var response = Dispatch("create_tree", new JObject
            {
                ["treeId"] = "tree.plain",
                ["name"] = "Plain Tree",
                ["path"] = "plain.aibt.json",
                ["rootNode"] = new JObject { ["id"] = "root", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
                ["blackboard"] = new JArray(new JObject { ["id"] = "health", ["valueType"] = "Float32" }),
            }, "SemanticEdit");

            Assert.That((bool)response["result"]["accepted"], Is.True, response.ToString());
            var document = CanonicalTreeJson.Parse(File.ReadAllText(Path.Combine(_assetsDir, "plain.aibt.json")), documentId: "plain.aibt.json").Document;
            Assert.That(document.FormatVersion, Is.EqualTo(1));
        }

        private void WritePolicy(bool supportsAgentScope, bool supportsSharedScope)
        {
            var policyDir = Path.Combine(_projectRoot, ".aibt");
            Directory.CreateDirectory(policyDir);
            var json = "{\n"
                + "  \"format\": \"aibt.policy\",\n"
                + "  \"formatVersion\": 1,\n"
                + "  \"allowManagedNodes\": true,\n"
                + "  \"allowMainThreadNodes\": true,\n"
                + "  \"requireTreeDescription\": false,\n"
                + "  \"requireNodeDescriptions\": false,\n"
                + "  \"blackboardNaming\": \"any\",\n"
                + "  \"requireDeterministicNodes\": true,\n"
                + "  \"allowSideEffects\": true,\n"
                + "  \"unreachableNodes\": \"error\",\n"
                + "  \"supportsAgentScope\": " + (supportsAgentScope ? "true" : "false") + ",\n"
                + "  \"supportsSharedScope\": " + (supportsSharedScope ? "true" : "false") + ",\n"
                + "  \"forbiddenNodeTypes\": [],\n"
                + "  \"warningsAsErrors\": [],\n"
                + "  \"performance\": { \"forbidUnboundedRepeaters\": false, \"requireEventDrivenServices\": false }\n"
                + "}";
            File.WriteAllText(Path.Combine(policyDir, "policy.json"), json);
        }

        [Test]
        public void ExtractThenInlineSubtreeRoundTripsToASemanticallyEquivalentTreeByCompiledContentHash()
        {
            var hash = CreateSessionTree();
            var addResult = Dispatch("apply_domain_patch", new JObject
            {
                ["treeId"] = "tree.session",
                ["expectedHash"] = hash,
                ["operations"] = new JArray(
                    new JObject { ["op"] = "add", ["parentId"] = "root", ["node"] = new JObject { ["id"] = "branch", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 } },
                    new JObject { ["op"] = "add", ["parentId"] = "branch", ["node"] = new JObject { ["id"] = "leaf", ["typeId"] = BuiltInNodeManifests.ReactiveSelectorTypeId, ["typeVersion"] = 1 } }),
            }, "SemanticEdit");
            Assert.That((bool)addResult["result"]["accepted"], Is.True, addResult.ToString());

            var beforeCompiledHash = CompiledHashOf(TreePath());

            var extracted = Dispatch("extract_subtree", new JObject { ["treeId"] = "tree.session", ["expectedHash"] = (string)addResult["result"]["contentHash"], ["nodeId"] = "branch" }, "SemanticEdit");
            Assert.That((bool)extracted["result"]["accepted"], Is.True, extracted.ToString());
            var afterExtractCompiledHash = CompiledHashOf(TreePath());
            Assert.That(afterExtractCompiledHash, Is.Not.EqualTo(beforeCompiledHash), "Extracting a real subtree must actually change the compiled program.");

            var attachment = extracted["result"]["attachment"];
            var inlined = Dispatch("inline_subtree", new JObject
            {
                ["treeId"] = "tree.session",
                ["expectedHash"] = (string)extracted["result"]["contentHash"],
                ["nodes"] = extracted["result"]["extractedNodes"],
                ["subtreeRootId"] = (string)attachment["rootNodeId"],
                ["parentId"] = (string)attachment["parentId"],
                ["insertIndex"] = (int)attachment["insertIndex"],
            }, "SemanticEdit");

            Assert.That((bool)inlined["result"]["accepted"], Is.True, inlined.ToString());
            var afterInlineCompiledHash = CompiledHashOf(TreePath());
            Assert.That(afterInlineCompiledHash, Is.EqualTo(beforeCompiledHash), "extract then inline back at the same attachment point must reproduce the original compiled program exactly.");
        }

        [Test]
        public void CreateTreeOnAnInvalidTreeReturnsCanonicalDiagnosticJsonByteForByte()
        {
            var response = Dispatch("create_tree", new JObject
            {
                ["treeId"] = "tree.invalid",
                ["name"] = "Invalid",
                ["path"] = "invalid.aibt.json",
                ["rootNode"] = new JObject { ["id"] = "root", ["typeId"] = BuiltInNodeManifests.InverterTypeId, ["typeVersion"] = 1 },
            }, "SemanticEdit");

            Assert.That((bool)response["result"]["accepted"], Is.False, response.ToString());
            var toolDiagnostics = (JArray)response["result"]["diagnostics"];
            Assert.That(toolDiagnostics.Count, Is.GreaterThan(0));

            var document = new TreeDocument(
                TreeDocument.CurrentFormat,
                TreeDocument.CurrentFormatVersion,
                new TreeId("tree.invalid"),
                "Invalid",
                new NodeId("root"),
                new[] { new NodeDocument(new NodeId("root"), BuiltInNodeManifests.InverterTypeId, 1) });
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var options = new ReferenceCompilerOptions("invalid.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0));
            var direct = ReferenceCompiler.Compile(document, registry, options);

            Assert.That(direct.Success, Is.False, "An inverter root with no child must fail to compile.");
            Assert.That(toolDiagnostics.Count, Is.EqualTo(direct.Diagnostics.Count));
            for (var index = 0; index < direct.Diagnostics.Count; index++)
            {
                var directJson = JObject.Parse(DiagnosticJson.Serialize(new AuthoringDiagnostic(direct.Diagnostics[index])));
                Assert.That(JToken.DeepEquals(toolDiagnostics[index], directJson), Is.True,
                    "Diagnostic " + index + " differs (this is the exact bug the P6-006 diagnostic-JSON-unification fix closed -- " +
                    "the tool must use the real canonical AIBT.Authoring.DiagnosticJson.Serialize shape, not a hand-rolled subset).\n" +
                    "Tool: " + toolDiagnostics[index] + "\nDirect: " + directJson);
            }
        }

        /// <summary>Creates "tree.session" at "session.aibt.json" and returns its contentHash.</summary>
        private string CreateSessionTree()
        {
            var created = Dispatch("create_tree", new JObject
            {
                ["treeId"] = "tree.session",
                ["name"] = "Session Tree",
                ["path"] = "session.aibt.json",
                ["rootNode"] = new JObject { ["id"] = "root", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
            }, "SemanticEdit");
            Assert.That((bool)created["result"]["accepted"], Is.True, created.ToString());
            return (string)created["result"]["contentHash"];
        }

        private string TreePath() => Path.Combine(_assetsDir, "session.aibt.json");

        private static CompiledHash CompiledHashOf(string treePath)
        {
            var document = CanonicalTreeJson.Parse(File.ReadAllText(treePath), documentId: treePath).Document;
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var options = new ReferenceCompilerOptions("session.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0));
            var result = ReferenceCompiler.Compile(document, registry, options);
            Assert.That(result.Success, Is.True, "Fixture must compile: " + string.Join("; ", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            return result.Program.Header.CompiledContentHash;
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
