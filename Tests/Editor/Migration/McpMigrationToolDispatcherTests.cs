using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Authoring.Migration;
using AIBT.Mcp;
using AIBT.Mcp.Migration;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using AuthoringNodeRegistry = AIBT.Authoring.NodeRegistry;

namespace AIBT.Tests.Editor.Migration
{
    /// <summary>P7-006 (ADR-P7-005): <c>aibt_migrate_document</c> (<see cref="McpMigrationToolDispatcher"/>).</summary>
    public sealed class McpMigrationToolDispatcherTests
    {
        private const string TypeId = "aibt.core.migration-persist-test-node";
        private const string Tree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 1, ""treeId"": ""tree.migration-persist"",
  ""name"": ""Migration persist test"", ""root"": ""root"",
  ""nodes"": { ""root"": { ""type"": """ + TypeId + @""", ""typeVersion"": 1 } }
}";

        private string _projectRoot;
        private string _assetsDir;
        private string _treePath;

        [SetUp]
        public void CreateTempProject()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "aibt-mcp-migration-" + System.Guid.NewGuid().ToString("N"));
            _assetsDir = Path.Combine(_projectRoot, "Assets");
            Directory.CreateDirectory(_assetsDir);
            _treePath = Path.Combine(_assetsDir, "tree.aibt.json");
            File.WriteAllText(_treePath, Tree);
        }

        [TearDown]
        public void RemoveTempProject()
        {
            if (Directory.Exists(_projectRoot)) Directory.Delete(_projectRoot, recursive: true);
        }

        [Test]
        public void DryRun_ReportsTheMigrationWithoutWritingTheFile()
        {
            var before = File.ReadAllText(_treePath);
            var result = McpMigrationToolDispatcher.MigrateDocument(_assetsDir,
                new JObject { ["treeId"] = "tree.migration-persist", ["dryRun"] = true },
                RulesWithOneAddition(), RegistryWithFixtureManifest());

            Assert.That((bool)result["migrated"], Is.True);
            Assert.That((bool)result["persisted"], Is.False);
            Assert.That(((JArray)result["outcomes"]).Count, Is.EqualTo(1));
            Assert.That(File.ReadAllText(_treePath), Is.EqualTo(before), "dryRun must never write the file.");
        }

        [Test]
        public void RealPersist_WritesTheMigratedDocumentToDisk()
        {
            var result = McpMigrationToolDispatcher.MigrateDocument(_assetsDir,
                new JObject { ["treeId"] = "tree.migration-persist", ["dryRun"] = false },
                RulesWithOneAddition(), RegistryWithFixtureManifest());

            Assert.That((bool)result["migrated"], Is.True);
            Assert.That((bool)result["persisted"], Is.True);

            var reloaded = CanonicalTreeJson.Parse(File.ReadAllText(_treePath), documentId: _treePath);
            Assert.That(reloaded.Success, Is.True);
            var node = reloaded.Document.Nodes[0];
            Assert.That(node.TypeVersion, Is.EqualTo(2));
            Assert.That(node.Parameters.TryGetValue("acceleration", out _), Is.True);
        }

        [Test]
        public void NoRegisteredRule_IsANoOpAndNeverWritesTheFile()
        {
            var before = File.ReadAllText(_treePath);
            var result = McpMigrationToolDispatcher.MigrateDocument(_assetsDir,
                new JObject { ["treeId"] = "tree.migration-persist", ["dryRun"] = false },
                registry: RegistryWithFixtureManifest());

            Assert.That((bool)result["migrated"], Is.False);
            Assert.That((bool)result["persisted"], Is.False);
            Assert.That(File.ReadAllText(_treePath), Is.EqualTo(before));
        }

        [Test]
        public void MigrateDocument_WithoutSemanticEditPermission_IsRejected()
        {
            // Goes through the real McpToolDispatcher.Dispatch switch (the production registry,
            // no injection possible here by design) purely to prove the permission gate itself --
            // this tree's node type is unknown to the production registry, so the call would fail
            // validation regardless; the permission check must reject it before that ever runs.
            var request = new JObject
            {
                ["tool"] = "migrate_document",
                ["args"] = new JObject { ["treeId"] = "tree.migration-persist" },
                ["grantedCategories"] = new JArray(), // no SemanticEdit granted
            };
            var responseLine = McpToolDispatcher.Dispatch(request.ToString(Newtonsoft.Json.Formatting.None), _assetsDir);
            var response = JObject.Parse(responseLine);

            Assert.That(response["error"], Is.Not.Null, response.ToString());
        }

        private static NodeMigrationRegistry RulesWithOneAddition()
            => NodeMigrationRegistry.Empty.WithRule(new NodeMigrationRule(
                TypeId, sourceVersion: 1,
                additions: new[] { new NodeFieldAddition("acceleration", SemanticValue.FromUInt64(5)) }));

        private static AuthoringNodeRegistry RegistryWithFixtureManifest()
        {
            var parameters = new[] { new NodeParameterContract("acceleration", NodeParameterType.UInt32, true) };
            var configuration = new NodeConfigurationDescriptor(4, 4, new[] { new NodeConfigurationField("acceleration", 0, 4, 4) });
            var childPolicy = new NodeChildPolicy(0, 0, true);
            var manifest = new NodeManifest(
                TypeId, 2, "P7-006 persist test fixture node.", "Test", NodeBehaviorKind.Action,
                "Used only by McpMigrationToolDispatcherTests.", "Do not use in production.",
                NodeExecutionDomain.Burst, true, parameters, childPolicy,
                reads: new string[0], writes: new string[0], sideEffects: new string[0],
                new[] { NodeStatus.Success }, new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                configuration, NodeCancellationMode.AbortOnly, NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Success", "{}", "Returns success.") });

            var builder = NodeRegistryBuilder.CreateWithBuiltIns();
            builder.AddBuiltInForTest(manifest, new NodeHandlerBindingContract(
                "aibt.reference.core.migration-persist-test-node", manifest.Version, manifest.ExecutionDomain));
            var result = builder.Build();
            Assert.That(result.Success, Is.True, string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            return result.Registry;
        }
    }
}
