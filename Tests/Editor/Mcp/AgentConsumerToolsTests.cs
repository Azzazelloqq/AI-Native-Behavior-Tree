using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using AIBT.Mcp;
using AIBT.Mcp.NodeDevelopment;
using AIBT.Tests.Consumer;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Editor.Mcp
{
    public sealed class AgentConsumerToolsTests
    {
        private static readonly ReferenceCompilerOptions Options = new ReferenceCompilerOptions(
            "Assets/agent-test.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6));

        [Test]
        public void EveryRegisteredOperationHasAnArgumentAndPermissionContract()
        {
            var operations = (JArray)McpOperationCatalog.List()["operations"];
            Assert.That(operations.Select(item => (string)item["name"]), Is.EquivalentTo(McpBuiltInTools.BridgeToolNames));
            Assert.That(operations.All(item => item["inputSchema"]["required"] is JArray && !string.IsNullOrEmpty((string)item["permission"])), Is.True);
            Assert.That(McpOperationCatalog.Describe("explain_diagnostic")["inputSchema"]["properties"]["diagnostic"]["type"].Value<string>(), Is.EqualTo("object"));
        }

        [Test]
        public void UnsupportedCreateFormatIsRejectedBeforeAnySourceIsWritten()
        {
            var root = Path.Combine(Path.GetTempPath(), "aibt-format-" + Guid.NewGuid().ToString("N"), "Assets");
            var response = JObject.Parse(McpToolDispatcher.Dispatch(new JObject
            {
                ["tool"] = "create_tree", ["grantedCategories"] = new JArray("SemanticEdit"),
                ["args"] = new JObject { ["treeId"] = "tree.format", ["name"] = "Invalid Format", ["path"] = "invalid.aibt.json", ["formatVersion"] = 3,
                    ["rootNode"] = new JObject { ["id"] = "root", ["typeId"] = "aibt.core.memory-sequence", ["typeVersion"] = 1 } },
            }.ToString(), root));
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9018"));
            Assert.That(Directory.Exists(root), Is.False);
        }

        [Test]
        public void AuthoringRejectsAProjectForbiddenNodeWithoutPersistingTheTree()
        {
            var root = Path.Combine(Path.GetTempPath(), "aibt-agent-policy-" + Guid.NewGuid().ToString("N"));
            var assets = Path.Combine(root, "Assets");
            Directory.CreateDirectory(assets);
            Directory.CreateDirectory(Path.Combine(root, ".aibt"));
            try
            {
                File.WriteAllText(Path.Combine(root, ".aibt", "policy.json"), new JObject
                {
                    ["format"] = "aibt.policy", ["formatVersion"] = 1, ["allowManagedNodes"] = false, ["allowMainThreadNodes"] = false,
                    ["requireTreeDescription"] = false, ["requireNodeDescriptions"] = false, ["blackboardNaming"] = "any",
                    ["requireDeterministicNodes"] = true, ["allowSideEffects"] = true, ["unreachableNodes"] = "error",
                    ["supportsAgentScope"] = false, ["supportsSharedScope"] = false,
                    ["forbiddenNodeTypes"] = new JArray("aibt.core.memory-sequence"), ["warningsAsErrors"] = new JArray(),
                    ["performance"] = new JObject { ["forbidUnboundedRepeaters"] = false, ["requireEventDrivenServices"] = false },
                }.ToString());
                var response = JObject.Parse(McpToolDispatcher.Dispatch(new JObject
                {
                    ["tool"] = "create_tree", ["grantedCategories"] = new JArray("SemanticEdit"),
                    ["args"] = new JObject { ["treeId"] = "tree.policy-rejection", ["name"] = "Rejected", ["path"] = "rejected.aibt.json",
                        ["rootNode"] = new JObject { ["id"] = "root", ["typeId"] = "aibt.core.memory-sequence", ["typeVersion"] = 1 } },
                }.ToString(), assets));
                Assert.That((bool)response["result"]["accepted"], Is.False, response.ToString());
                Assert.That(File.Exists(Path.Combine(assets, "rejected.aibt.json")), Is.False);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void InstalledPackageResourcesDoNotDependOnAnAssetsAibtDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), "aibt-resource-" + Guid.NewGuid().ToString("N"), "Assets");
            var response = JObject.Parse(McpToolDispatcher.Dispatch(new JObject
            {
                ["tool"] = "get_static_resource", ["args"] = new JObject { ["key"] = "workflow" }, ["grantedCategories"] = new JArray("Read"),
            }.ToString(), root));
            Assert.That((bool)response["result"]["found"], Is.True, response.ToString());
            Assert.That((string)response["result"]["content"], Does.Contain("MCP agent workflow"));
            Assert.That(Directory.Exists(root), Is.False);
        }

        [Test]
        public void CatalogRebindingResolvesANativeDraftWithoutLosingUndoOrSource()
        {
            var tree = ProductionConsumerWorkflowTests.Tree(SemanticValue.FromNumber(0.5));
            var session = new GraphEditSession(tree, NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry, Options, CanonicalTreeJson.Serialize(tree).Utf8);
            session.Apply(document => SemanticEditOperations.SetParameter(document, document.Root, "radius", SemanticValue.FromNumber(1.5)));
            Assert.That(session.CanCompile, Is.False);
            var before = CanonicalTreeJson.Serialize(session.Document).Utf8;
            var catalog = ProjectNodeCatalog.Build(new[] { typeof(ConsumerShard) }, Array.Empty<IReferenceLeafBehaviorProvider>());
            session.RebindRegistry(catalog.Registry, document => catalog.ValidateAuthoring(document, Options));
            Assert.That(session.CanCompile, Is.True, string.Join("; ", session.Diagnostics));
            Assert.That(CanonicalTreeJson.Serialize(session.Document).Utf8, Is.EqualTo(before));
            Assert.That(session.CanUndo && session.IsDirty, Is.True);
            session.Undo();
            Assert.That(session.IsDirty, Is.False);
        }

        [TestCase(4f, "Running")]
        [TestCase(1.5f, "Success")]
        public void NativeBehaviorCasesCheckExplicitInputsStatusAndCommittedPayload(float distance, string expected)
        {
            var cases = new JArray(new JObject
            {
                ["configuration"] = new JObject { ["radius"] = 1.5f }, ["inputs"] = new JObject { ["distance"] = distance },
                ["expectedStatus"] = expected, ["expectedEffects"] = new JObject { ["value"] = 1.5f },
            });
            var result = NativeNodeBehaviorChecks.Run(ProductionConsumerWorkflowTests.Artifact(), typeof(ConsumerCatalog), cases);
            Assert.That((bool)result["behaviorProven"], Is.True, result.ToString());
            cases[0]["expectedStatus"] = "Failure";
            var wrong = NativeNodeBehaviorChecks.Run(ProductionConsumerWorkflowTests.Artifact(), typeof(ConsumerCatalog), cases);
            Assert.That((bool)wrong["behaviorProven"], Is.False, "Dispatch success must not hide a wrong behavior expectation.");
        }

        [TestCase("../Outside/a.asset")]
        [TestCase("Assets/../Outside/a.asset")]
        [TestCase("Assets/a.json")]
        public void NativePreparationRejectsEscapingOrWrongAssetPaths(string output)
        {
            var root = Path.Combine(Path.GetTempPath(), "aibt-path-" + Guid.NewGuid().ToString("N"), "Assets");
            Assert.Throws<AIBT.Mcp.Authoring.McpToolException>(() => McpRuntimeTools.ResolveAssetPath(root, output));
        }

        [Test]
        public void NativePreparationUsesForwardSlashProjectPathsForUnimportedAssets()
        {
            var assets = Path.Combine(Path.GetTempPath(), "aibt-logical-path-" + Guid.NewGuid().ToString("N"), "Assets");
            var file = Path.Combine(assets, "Nested", "not-imported.runtime.asset");
            Assert.That(McpRuntimeTools.UnityAssetPath(assets, file), Is.EqualTo("Assets/Nested/not-imported.runtime.asset"));
            Assert.That(Directory.Exists(assets), Is.False);
        }

        [TestCase(256u, false)]
        [TestCase(2u, true)]
        public void NativeTraceIdentifiesTheExactHostAndDisclosesIncompleteHistory(uint records, bool dropped)
        {
            var asset = ProductionTreeAssetBaker.Create(ProductionConsumerWorkflowTests.Tree(SemanticValue.FromNumber(1.5)),
                "Assets/agent-test.aibt.json", typeof(ConsumerCatalog), out var diagnostics);
            Assert.That(asset, Is.Not.Null, string.Join("; ", diagnostics));
            var gameObject = new GameObject("agent trace fixture");
            ProductionTreeProgram program = null;
            ProductionTreeHost host = null;
            try
            {
                Assert.That(asset.TryCreateProgram(ConsumerCatalog.TryCreateRuntimeCatalog, out program, out var failure), Is.True, failure.ToString());
                host = gameObject.AddComponent<ProductionTreeHost>();
                Assert.That(program.TryBootstrap(host, out _, effectCapacity: 4, traceCapacity: new NativeTraceChannelCapacityV1(records, 0u, 0u, 512u)), Is.True);
                typeof(ProductionTreeHost).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
                var result = McpRuntimeTools.Trace(new JObject { ["instanceId"] = host.InstanceId });
                Assert.That((bool)result["available"], Is.True, result.ToString());
                Assert.That((string)result["programHash"], Is.EqualTo(program.Program.Header.CompiledContentHash.HexadecimalValue));
                Assert.That((bool)result["complete"], Is.EqualTo(!dropped), result.ToString());
                Assert.That((string)result["rootStatus"], Is.EqualTo("Success"));
            }
            finally { host?.Stop(); UnityEngine.Object.DestroyImmediate(gameObject); program?.Dispose(); UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void NativeBuiltInLeafDescriptorsKeepTheirReferenceContractsWithoutDuplicateRegistration()
        {
            var shard = UnityEditor.TypeCache.GetTypesWithAttribute<AIBT.Burst.AibtCatalogShardAttribute>()
                .Single(type => type.FullName == "AIBT.Authoring.BuiltInLeaves.BuiltInLeafShard");
            var catalog = ProjectNodeCatalog.Build(new[] { shard }, Array.Empty<IReferenceLeafBehaviorProvider>());
            Assert.That(catalog.Problems, Is.Empty);
            Assert.That(catalog.Generated.ContainsKey("aibt.stdlib.wait"), Is.True);
            var node = new NodeDocument(new NodeId("wait"), "aibt.stdlib.wait", 1,
                parameters: new SemanticObject(new[] { new SemanticProperty("ticks", SemanticValue.FromInt64(1)) }), tags: TagSet.Empty);
            var tree = TreeDocument.CreateVersion2(new TreeId("tree.native-wait"), "Native Wait", node.Id, new[] { node }, null, null, tags: TagSet.Empty, metadata: SemanticObject.Empty);
            var result = GeneratedCompiledProgramV2Compiler.Compile(tree, catalog.Generated.Values, catalog.RegisteredTypes, Options);
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(catalog.Registry.TryGet("aibt.stdlib.wait", out var entry) && entry.HasReferenceHandlerBinding, Is.True);
            Assert.That(ProductionTreeAssetBaker.Create(tree, "Assets/native-wait.aibt.json", typeof(ConsumerCatalog), out var mismatch), Is.Null);
            Assert.That(mismatch.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Message.Contains("does not declare")), Is.True);
        }
    }
}
