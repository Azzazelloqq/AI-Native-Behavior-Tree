using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Authoring.Migration;
using AIBT.Editor.Migration;
using NUnit.Framework;
using UnityEngine;
using AuthoringNodeRegistry = AIBT.Authoring.NodeRegistry;

namespace AIBT.Tests.Editor.Migration
{
    /// <summary>P7-006 (ADR-P7-005): <see cref="MigrationNotificationWindow"/>'s scan/list logic.</summary>
    public sealed class MigrationNotificationWindowTests
    {
        private const string TypeId = "aibt.core.migration-window-test-node";
        private const string Tree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 1, ""treeId"": ""tree.migration-window-test"",
  ""name"": ""Migration window test"", ""root"": ""root"",
  ""nodes"": { ""root"": { ""type"": """ + TypeId + @""", ""typeVersion"": 1 } }
}";
        private const string UnaffectedTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 1, ""treeId"": ""tree.migration-window-unaffected"",
  ""name"": ""Unaffected"", ""root"": ""root"",
  ""nodes"": { ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1 } }
}";

        private string _scanRoot;

        [SetUp]
        public void CreateFixtureDirectory()
        {
            _scanRoot = Path.Combine(Path.GetTempPath(), "aibt-migration-window-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scanRoot);
            File.WriteAllText(Path.Combine(_scanRoot, "migratable.aibt.json"), Tree);
            File.WriteAllText(Path.Combine(_scanRoot, "unaffected.aibt.json"), UnaffectedTree);
        }

        [TearDown]
        public void RemoveFixtureDirectory()
        {
            if (Directory.Exists(_scanRoot)) Directory.Delete(_scanRoot, recursive: true);
        }

        [Test]
        public void Scan_WithARegisteredRule_ListsOnlyTheMigratableDocument()
        {
            var window = ScriptableObject.CreateInstance<MigrationNotificationWindow>();
            try
            {
                var rules = NodeMigrationRegistry.Empty.WithRule(new NodeMigrationRule(
                    TypeId, sourceVersion: 1,
                    additions: new[] { new NodeFieldAddition("acceleration", SemanticValue.FromUInt64(5)) }));

                window.Scan(rules, _scanRoot, RegistryWithFixtureManifest());

                Assert.That(window.LastScanMigratableTreeIds, Has.Count.EqualTo(1));
                Assert.That(window.LastScanMigratableTreeIds[0].Value, Is.EqualTo("tree.migration-window-test"));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Scan_WithNoRegisteredRule_ListsNothing()
        {
            var window = ScriptableObject.CreateInstance<MigrationNotificationWindow>();
            try
            {
                window.Scan(NodeMigrationRegistry.Empty, _scanRoot, RegistryWithFixtureManifest());

                Assert.That(window.LastScanMigratableTreeIds, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        private static AuthoringNodeRegistry RegistryWithFixtureManifest()
        {
            var parameters = new[] { new NodeParameterContract("acceleration", NodeParameterType.UInt32, true) };
            var configuration = new NodeConfigurationDescriptor(4, 4, new[] { new NodeConfigurationField("acceleration", 0, 4, 4) });
            var childPolicy = new NodeChildPolicy(0, 0, true);
            var manifest = new NodeManifest(
                TypeId, 2, "P7-006 window test fixture node.", "Test", NodeBehaviorKind.Action,
                "Used only by MigrationNotificationWindowTests.", "Do not use in production.",
                NodeExecutionDomain.Burst, true, parameters, childPolicy,
                reads: new string[0], writes: new string[0], sideEffects: new string[0],
                new[] { NodeStatus.Success }, new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                configuration, NodeCancellationMode.AbortOnly, NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Success", "{}", "Returns success.") });

            var builder = NodeRegistryBuilder.CreateWithBuiltIns();
            builder.AddBuiltInForTest(manifest, new NodeHandlerBindingContract(
                "aibt.reference.core.migration-window-test-node", manifest.Version, manifest.ExecutionDomain));
            var result = builder.Build();
            Assert.That(result.Success, Is.True, string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            return result.Registry;
        }
    }
}
