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

        /// <summary>
        /// P7-018 made Agent scope in <c>create_tree</c> an opt-in through the policy file. P11-015 review
        /// follow-up (ADR-P9-001, second amendment): without the file the authoring tools use the default
        /// <c>validate</c> and the tree list use, so a native tree with an Agent key is accepted and written as
        /// format 2; a file whose flag is false still refuses it. Rewritten like its <c>compile</c> sibling
        /// (<c>CompileWithAgentScopeBlackboard_UsesTheEditorDefaultWithoutAPolicyFile_AndThePolicyWithOne</c>).
        /// </summary>
        [Test]
        public void CreateTreeWithAgentScopeKey_UsesTheEditorDefaultWithoutAPolicyFile_AndThePolicyWithOne()
        {
            var withoutFile = Dispatch("create_tree", AgentTreeArgs("tree.agent", "agent.aibt.json"), "SemanticEdit");

            Assert.That((bool)withoutFile["result"]["accepted"], Is.True, withoutFile.ToString());
            var written = File.ReadAllText(Path.Combine(_assetsDir, "agent.aibt.json"));
            var document = CanonicalTreeJson.Parse(written, documentId: "agent.aibt.json").Document;
            Assert.That(document.FormatVersion, Is.EqualTo(2));
            Assert.That(document.Blackboard.Single().Scope, Is.EqualTo(BlackboardScope.Agent));
            Assert.That(document.AgentContract.ContractId, Is.EqualTo("contract.agent"));

            WritePolicy(supportsAgentScope: false, supportsSharedScope: false);
            var flagFalse = Dispatch("create_tree", AgentTreeArgs("tree.agent-refused", "agent-refused.aibt.json"), "SemanticEdit");

            Assert.That((bool)flagFalse["result"]["accepted"], Is.False, flagFalse.ToString());
            Assert.That(Codes(flagFalse), Does.Contain("AIBT2030"));
            Assert.That(File.Exists(Path.Combine(_assetsDir, "agent-refused.aibt.json")), Is.False);
        }

        /// <summary>P11-015 review follow-up: the edit tools agree with <c>validate</c> on such a tree.</summary>
        [Test]
        public void EditToolsOnANativeAgentScopeTree_AreAcceptedWithoutAPolicyFile_AndRefusedByAFileWithTheFlagFalse()
        {
            var created = Dispatch("create_tree", AgentTreeArgs("tree.agent", "agent.aibt.json"), "SemanticEdit");
            Assert.That((bool)created["result"]["accepted"], Is.True, created.ToString());
            var validated = Dispatch("validate", new JObject { ["treeId"] = "tree.agent" }, "Compilation");
            Assert.That((bool)validated["result"]["valid"], Is.True, validated.ToString());

            var added = Dispatch("add_node", new JObject
            {
                ["treeId"] = "tree.agent", ["expectedHash"] = (string)created["result"]["contentHash"], ["parentId"] = "root",
                ["node"] = new JObject { ["id"] = "inner", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
            }, "SemanticEdit");
            Assert.That((bool)added["result"]["accepted"], Is.True, added.ToString());

            var keys = Dispatch("set_blackboard_keys", new JObject
            {
                ["treeId"] = "tree.agent", ["expectedHash"] = (string)added["result"]["contentHash"],
                ["keys"] = new JArray(
                    new JObject { ["id"] = "danger", ["valueType"] = "Bool", ["scope"] = "agent", ["default"] = false },
                    new JObject { ["id"] = "alert", ["valueType"] = "Bool", ["scope"] = "agent", ["default"] = true }),
            }, "SemanticEdit");
            Assert.That((bool)keys["result"]["accepted"], Is.True, keys.ToString());

            WritePolicy(supportsAgentScope: false, supportsSharedScope: false);
            var refused = Dispatch("add_node", new JObject
            {
                ["treeId"] = "tree.agent", ["expectedHash"] = (string)keys["result"]["contentHash"], ["parentId"] = "root",
                ["node"] = new JObject { ["id"] = "second", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
            }, "SemanticEdit");
            Assert.That((bool)refused["result"]["accepted"], Is.False, refused.ToString());
            Assert.That(Codes(refused), Does.Contain("AIBT2030"));
        }

        /// <summary>A tree with a managed node runs in a BehaviorTreeAgent, which has no Agent scope: still refused.</summary>
        [Test]
        public void CreateTree_OfAManagedTreeWithAnAgentScopeKey_IsStillRefusedWithoutAPolicyFile()
        {
            var args = AgentTreeArgs("tree.managed-agent", "managed-agent.aibt.json");
            args["rootNode"] = new JObject
            {
                ["id"] = "root", ["typeId"] = "aibt.unity.wait-seconds", ["typeVersion"] = 1,
                ["parameters"] = new JObject { ["seconds"] = 1.0 },
            };
            var response = Dispatch("create_tree", args, "SemanticEdit");

            Assert.That((bool)response["result"]["accepted"], Is.False, response.ToString());
            Assert.That(Codes(response), Does.Contain("AIBT2030"));
            Assert.That(File.Exists(Path.Combine(_assetsDir, "managed-agent.aibt.json")), Is.False);
        }

        /// <summary>An unreadable file is not applied and is reported as AIBT9008, as <c>validate</c> does.</summary>
        [Test]
        public void AnUnreadablePolicyFile_IsReportedAsAIBT9008_ByCreateTreeAndTheEditTools()
        {
            Directory.CreateDirectory(Path.Combine(_projectRoot, ".aibt"));
            File.WriteAllText(Path.Combine(_projectRoot, ".aibt", "policy.json"), "{ \"format\": \"aibt.policy\", \"formatVersion\": 1 }");

            var created = Dispatch("create_tree", AgentTreeArgs("tree.agent", "agent.aibt.json"), "SemanticEdit");
            Assert.That((bool)created["result"]["accepted"], Is.True, created.ToString());
            AssertPolicyWarning(created);

            var added = Dispatch("add_node", new JObject
            {
                ["treeId"] = "tree.agent", ["expectedHash"] = (string)created["result"]["contentHash"], ["parentId"] = "root",
                ["node"] = new JObject { ["id"] = "inner", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
            }, "SemanticEdit");
            Assert.That((bool)added["result"]["accepted"], Is.True, added.ToString());
            AssertPolicyWarning(added);
        }

        /// <summary>
        /// Neighbour (a): with a file the file decides for every tree. <c>validate</c> refuses a managed tree under
        /// <c>allowManagedNodes: false</c>; adding a managed node must be refused the same way.
        /// </summary>
        [Test]
        public void WithAFileThatForbidsManagedNodes_AddingAManagedNode_IsRefused_AsValidateRefusesTheTree()
        {
            var hash = CreateSessionTree();
            WritePolicy(supportsAgentScope: false, supportsSharedScope: false, allowManagedNodes: false);

            var added = Dispatch("add_node", new JObject
            {
                ["treeId"] = "tree.session", ["expectedHash"] = hash, ["parentId"] = "root",
                ["node"] = new JObject
                {
                    ["id"] = "wait", ["typeId"] = "aibt.unity.wait-seconds", ["typeVersion"] = 1,
                    ["parameters"] = new JObject { ["seconds"] = 1.0 },
                },
                ["dryRun"] = true,
            }, "SemanticEdit");
            UnityEngine.Debug.Log("P11-015 policy default, neighbour (a): " + added.ToString(Newtonsoft.Json.Formatting.None));
            Assert.That((bool)added["result"]["accepted"], Is.False, added.ToString());
            // The file's own rules apply (AIBT2039 execution domain, AIBT2040 nondeterministic: this file also
            // requires deterministic nodes), not the managed layer's policy.
            Assert.That(Codes(added).Intersect(new[] { "AIBT2039", "AIBT2040" }), Is.Not.Empty, added.ToString());
        }

        /// <summary>
        /// Neighbour (b): a tree with a subtree reference is checked like the same tree without one. Under a file
        /// whose Agent flag is false, a native tree with an Agent key is refused (AIBT2030) with or without a
        /// reference to a subtree whose only node has no reference handler (the native compiler expands with both
        /// scope flags on).
        /// </summary>
        [Test]
        public void AnAgentScopeTreeWithASubtreeReference_IsCheckedLikeTheSameTreeWithoutOne()
        {
            WritePolicy(supportsAgentScope: false, supportsSharedScope: false);
            File.WriteAllText(Path.Combine(_assetsDir, "hold.aibt.json"), @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tree.hold"", ""name"": ""Hold"", ""root"": ""hold"",
  ""nodes"": { ""hold"": { ""type"": ""aibt.native.fail"", ""typeVersion"": 1 } }
}");
            File.WriteAllText(Path.Combine(_assetsDir, "plain.aibt.json"), @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tree.plain"", ""name"": ""Plain"", ""root"": ""root"",
  ""blackboardContracts"": { ""agent"": { ""contractId"": ""contract.agent"", ""contractVersion"": 1 } },
  ""blackboard"": { ""danger"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""agent"", ""default"": false } },
  ""nodes"": { ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""hold""] },
    ""hold"": { ""type"": ""aibt.native.fail"", ""typeVersion"": 1 } }
}");
            File.WriteAllText(Path.Combine(_assetsDir, "referencing.aibt.json"), @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tree.referencing"", ""name"": ""Referencing"", ""root"": ""root"",
  ""blackboardContracts"": { ""agent"": { ""contractId"": ""contract.agent"", ""contractVersion"": 1 } },
  ""blackboard"": { ""danger"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""agent"", ""default"": false } },
  ""nodes"": { ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""use""] },
    ""use"": { ""subtree"": { ""treeId"": ""tree.hold"", ""interfaceVersion"": 1, ""keys"": { } } } }
}");

            JObject AddInner(string treeId)
            {
                var hash = (string)Dispatch("inspect_tree", new JObject { ["treeId"] = treeId }, "Read")["result"]["contentHash"];
                return Dispatch("add_node", new JObject
                {
                    ["treeId"] = treeId, ["expectedHash"] = hash, ["parentId"] = "root",
                    ["node"] = new JObject { ["id"] = "inner", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
                    ["dryRun"] = true,
                }, "SemanticEdit");
            }

            var plain = AddInner("tree.plain");
            var referencing = AddInner("tree.referencing");
            UnityEngine.Debug.Log("P11-015 policy default, neighbour (b): plain " + plain.ToString(Newtonsoft.Json.Formatting.None)
                + " referencing " + referencing.ToString(Newtonsoft.Json.Formatting.None));
            Assert.That((bool)plain["result"]["accepted"], Is.False, plain.ToString());
            Assert.That(Codes(plain), Does.Contain("AIBT2030"));
            Assert.That((bool)referencing["result"]["accepted"], Is.False, referencing.ToString());
            Assert.That(Codes(referencing), Does.Contain("AIBT2030"));
        }

        private static JObject AgentTreeArgs(string treeId, string path) => new JObject
        {
            ["treeId"] = treeId,
            ["name"] = "Agent Tree",
            ["path"] = path,
            ["rootNode"] = new JObject { ["id"] = "root", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
            ["blackboard"] = new JArray(new JObject
            {
                ["id"] = "danger", ["valueType"] = "Bool", ["scope"] = "agent", ["default"] = false,
            }),
            ["agentContract"] = new JObject { ["contractId"] = "contract.agent", ["contractVersion"] = 1 },
        };

        private static string[] Codes(JObject response)
            => ((response["result"]?["diagnostics"] as JArray) ?? new JArray()).Select(d => (string)d["code"]).ToArray();

        private static void AssertPolicyWarning(JObject response)
        {
            var warning = ((JArray)response["result"]["diagnostics"]).FirstOrDefault(d => (string)d["code"] == "AIBT9008");
            Assert.That(warning, Is.Not.Null, response.ToString());
            Assert.That((string)warning["severity"], Is.EqualTo("warning").IgnoreCase);
        }

        /// <summary>
        /// P11-014 item 4 (W10): create_tree writes format 2 unless the caller asks for 1, so later native
        /// authoring (bindings) is not refused with AIBT2042. An explicit formatVersion 1 keeps working.
        /// </summary>
        [Test]
        public void CreateTree_DefaultsToV2_AndAnExplicitV1StillWritesV1()
        {
            JObject Create(string id, int? formatVersion)
            {
                var args = new JObject
                {
                    ["treeId"] = "tree." + id,
                    ["name"] = "Plain Tree",
                    ["path"] = id + ".aibt.json",
                    ["rootNode"] = new JObject { ["id"] = "root", ["typeId"] = BuiltInNodeManifests.MemorySequenceTypeId, ["typeVersion"] = 1 },
                    ["blackboard"] = new JArray(new JObject { ["id"] = "health", ["valueType"] = "Float32" }),
                };
                if (formatVersion.HasValue) args["formatVersion"] = formatVersion.Value;
                return Dispatch("create_tree", args, "SemanticEdit");
            }

            var byDefault = Create("plain", null);
            Assert.That((bool)byDefault["result"]["accepted"], Is.True, byDefault.ToString());
            Assert.That(CanonicalTreeJson.Parse(File.ReadAllText(Path.Combine(_assetsDir, "plain.aibt.json")), documentId: "plain.aibt.json").Document.FormatVersion,
                Is.EqualTo(2), "the default");

            var explicitV1 = Create("plain-v1", 1);
            Assert.That((bool)explicitV1["result"]["accepted"], Is.True, explicitV1.ToString());
            Assert.That(CanonicalTreeJson.Parse(File.ReadAllText(Path.Combine(_assetsDir, "plain-v1.aibt.json")), documentId: "plain-v1.aibt.json").Document.FormatVersion,
                Is.EqualTo(1), "an explicit formatVersion 1");
        }

        private void WritePolicy(bool supportsAgentScope, bool supportsSharedScope, bool allowManagedNodes = true)
        {
            var policyDir = Path.Combine(_projectRoot, ".aibt");
            Directory.CreateDirectory(policyDir);
            var json = "{\n"
                + "  \"format\": \"aibt.policy\",\n"
                + "  \"formatVersion\": 1,\n"
                + "  \"allowManagedNodes\": " + (allowManagedNodes ? "true" : "false") + ",\n"
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
