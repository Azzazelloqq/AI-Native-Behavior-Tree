using System.Collections.Generic;
using System.Linq;
using System.Text;
using AIBT.Authoring;
using NUnit.Framework;
using AuthoringNodeRegistry = AIBT.Authoring.NodeRegistry;

namespace AIBT.Tests.Editor.MigrationToolingSpike
{
    // Disposable P7-005 spike. Proves the ADR-P7-005 migration mechanism (declarative,
    // authoring-layer-only rename/add-with-default rules, applied in memory, never inside the
    // Burst-compiled node) against a real fixture node type and a real authored document, compiled
    // through the real, accepted ReferenceCompiler -- not a mock. Archived to
    // Spikes~/MigrationToolingDecision/ and Planning~/Evidence/P7-005/ after this session; deleted
    // from Tests/ once archived, per this card's own Forbidden changes.
    public sealed class SpikeMigrationTooling
    {
        private const string TypeId = "aibt.core.spike-migrated-node";

        // --- The mechanism under test: one v1->v2 migration rule (rename + add-with-default) ---

        private readonly struct MigrationChange
        {
            internal MigrationChange(string description) => Description = description;
            internal string Description { get; }
        }

        private readonly struct MigrationAppliedDiagnostic
        {
            internal MigrationAppliedDiagnostic(
                TreeId treeId, NodeId nodeId, string typeId, uint fromVersion, uint toVersion,
                IReadOnlyList<MigrationChange> changes)
            {
                TreeId = treeId;
                NodeId = nodeId;
                TypeId = typeId;
                FromVersion = fromVersion;
                ToVersion = toVersion;
                Changes = changes;
            }

            internal TreeId TreeId { get; }
            internal NodeId NodeId { get; }
            internal string TypeId { get; }
            internal uint FromVersion { get; }
            internal uint ToVersion { get; }
            internal IReadOnlyList<MigrationChange> Changes { get; }
        }

        // Applies the one registered v1->v2 rule (rename "moveSpeed" -> "speed", add
        // "acceleration" with a default) to every node of TypeId at version 1 in the document.
        // Mirrors ADR-P7-005's proposed shape: pure JSON-parameter transform, no Burst/execution
        // involvement, non-blocking (returns diagnostics, never throws on an unrelated node).
        private static TreeDocument MigrateV1ToV2(TreeDocument document, out List<MigrationAppliedDiagnostic> diagnostics)
        {
            diagnostics = new List<MigrationAppliedDiagnostic>();
            var newNodes = new List<NodeDocument>();
            foreach (var node in document.Nodes)
            {
                if (node.TypeId != TypeId || node.TypeVersion != 1)
                {
                    newNodes.Add(node);
                    continue;
                }

                var changes = new List<MigrationChange>();
                var props = new List<SemanticProperty>();
                foreach (var property in node.Parameters.Properties)
                {
                    if (property.Name == "moveSpeed")
                    {
                        props.Add(new SemanticProperty("speed", property.Value));
                        changes.Add(new MigrationChange("field 'moveSpeed' renamed to 'speed'"));
                    }
                    else
                    {
                        props.Add(property);
                    }
                }
                props.Add(new SemanticProperty("acceleration", SemanticValue.FromUInt64(5)));
                changes.Add(new MigrationChange("field 'acceleration' added, default 5"));

                var migrated = new NodeDocument(
                    node.Id, node.TypeId, 2, node.Children, new SemanticObject(props),
                    node.Observer, node.DisplayName, node.Description, node.Tags);
                newNodes.Add(migrated);
                diagnostics.Add(new MigrationAppliedDiagnostic(
                    document.TreeId, node.Id, node.TypeId, 1, 2, changes));
            }

            return new TreeDocument(
                document.Format, document.FormatVersion, document.TreeId, document.Name,
                document.Root, newNodes, tags: document.Tags, metadata: document.Metadata);
        }

        [Test]
        public void RealVersionBump_MigratesInMemoryAndCompilesAgainstV2Manifest()
        {
            var registry = RegistryWithV2Manifest();
            var v1Document = AuthoredV1Document();

            var migrated = MigrateV1ToV2(v1Document, out var diagnostics);

            Assert.That(diagnostics, Has.Count.EqualTo(1), "Expected exactly one migrated node.");
            var diagnostic = diagnostics[0];
            Assert.That(diagnostic.FromVersion, Is.EqualTo(1u));
            Assert.That(diagnostic.ToVersion, Is.EqualTo(2u));
            Assert.That(diagnostic.Changes.Select(c => c.Description), Is.EquivalentTo(new[]
            {
                "field 'moveSpeed' renamed to 'speed'",
                "field 'acceleration' added, default 5",
            }));

            var migratedNode = migrated.Nodes.Single(n => n.TypeId == TypeId);
            Assert.That(migratedNode.TypeVersion, Is.EqualTo(2));
            Assert.That(migratedNode.Parameters.TryGetValue("speed", out var speedValue), Is.True);
            Assert.That(migratedNode.Parameters.TryGetValue("moveSpeed", out _), Is.False,
                "Old field name must not survive migration.");
            Assert.That(migratedNode.Parameters.TryGetValue("acceleration", out var accelerationValue), Is.True);

            var compileOptions = new ReferenceCompilerOptions(
                "spikes/migration-tooling.aibt.json", ReferenceCompilationPolicy.Phase1,
                new CompiledCompilerVersion(1, 0, 0, 1));
            var result = ReferenceCompiler.Compile(migrated, registry, compileOptions);

            Assert.That(result.Success, Is.True,
                "Migrated document must compile against the v2 manifest through the real ReferenceCompiler: "
                + string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));

            // Diff preview: reuse the real canonical writer (Authoring/Serialization/Json/CanonicalTreeJsonWriter.cs),
            // exactly as ADR-P7-005 proposes -- no new diffing infrastructure.
            var before = Encoding.UTF8.GetString(CanonicalTreeJsonWriter.Write(v1Document, semanticOnly: true));
            var after = Encoding.UTF8.GetString(CanonicalTreeJsonWriter.Write(migrated, semanticOnly: true));
            Assert.That(before, Does.Contain("\"moveSpeed\""));
            Assert.That(before, Does.Not.Contain("\"acceleration\""));
            Assert.That(after, Does.Contain("\"speed\""));
            Assert.That(after, Does.Not.Contain("\"moveSpeed\""));
            Assert.That(after, Does.Contain("\"acceleration\""));
            UnityEngine.Debug.Log(
                "AIBT_P7_005_SPIKE_DIFF|before=" + before + "\n---\nafter=" + after);
        }

        [Test]
        public void UnregisteredVersionGap_StillHardFailsThroughTheExistingValidator()
        {
            // No v2->v3 rule is registered anywhere (a removed field is genuinely unhandled, per
            // ADR-P7-005's own disclosed scope). A v2-authored document against a v3-registered
            // manifest must fail exactly as it does today -- UnsupportedNodeVersion, unchanged.
            var registryV3Only = RegistryWithV3ManifestOnly();
            var v2Document = AuthoredV2Document();

            var compileOptions = new ReferenceCompilerOptions(
                "spikes/migration-tooling-negative.aibt.json", ReferenceCompilationPolicy.Phase1,
                new CompiledCompilerVersion(1, 0, 0, 1));
            var result = ReferenceCompiler.Compile(v2Document, registryV3Only, compileOptions);

            Assert.That(result.Success, Is.False, "An unhandled version gap must never silently compile.");
            Assert.That(result.Diagnostics.Select(d => d.Code.Value),
                Has.Some.EqualTo(TreeValidationDiagnosticCodes.UnsupportedNodeVersion.Value));
        }

        private static TreeDocument AuthoredV1Document()
        {
            var root = new NodeDocument(
                new NodeId("root"), TypeId, 1, children: null,
                parameters: new SemanticObject(new[]
                {
                    new SemanticProperty("moveSpeed", SemanticValue.FromUInt64(10)),
                }),
                tags: TagSet.Empty);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.spike.migration"), "Migration spike", root.Id, new[] { root },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument AuthoredV2Document()
        {
            var root = new NodeDocument(
                new NodeId("root"), TypeId, 2, children: null,
                parameters: new SemanticObject(new[]
                {
                    new SemanticProperty("speed", SemanticValue.FromUInt64(10)),
                    new SemanticProperty("acceleration", SemanticValue.FromUInt64(5)),
                }),
                tags: TagSet.Empty);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.spike.migration.negative"), "Migration spike negative", root.Id,
                new[] { root }, tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static AuthoringNodeRegistry RegistryWithV2Manifest()
        {
            var manifest = ManifestV2();
            var builder = NodeRegistryBuilder.CreateWithBuiltIns();
            builder.AddBuiltInForTest(
                manifest, new NodeHandlerBindingContract("aibt.reference.core.spike-migrated-node", manifest.Version, manifest.ExecutionDomain));
            var result = builder.Build();
            Assert.That(result.Success, Is.True,
                string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            return result.Registry;
        }

        private static AuthoringNodeRegistry RegistryWithV3ManifestOnly()
        {
            // v3 removed "acceleration" entirely relative to v2 -- a genuine, unhandled field
            // removal, no migration rule registered for it anywhere in this spike.
            var manifest = ManifestV3RemovesAcceleration();
            var builder = NodeRegistryBuilder.CreateWithBuiltIns();
            builder.AddBuiltInForTest(
                manifest, new NodeHandlerBindingContract("aibt.reference.core.spike-migrated-node", manifest.Version, manifest.ExecutionDomain));
            var result = builder.Build();
            Assert.That(result.Success, Is.True,
                string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            return result.Registry;
        }

        private static NodeManifest ManifestV2()
        {
            var parameters = new[]
            {
                new NodeParameterContract("speed", NodeParameterType.UInt32, true),
                new NodeParameterContract("acceleration", NodeParameterType.UInt32, true),
            };
            var configuration = new NodeConfigurationDescriptor(8, 4, new[]
            {
                new NodeConfigurationField("speed", 0, 4, 4),
                new NodeConfigurationField("acceleration", 4, 4, 4),
            });
            return BuildManifest(2, parameters, configuration);
        }

        private static NodeManifest ManifestV3RemovesAcceleration()
        {
            var parameters = new[]
            {
                new NodeParameterContract("speed", NodeParameterType.UInt32, true),
            };
            var configuration = new NodeConfigurationDescriptor(4, 4, new[]
            {
                new NodeConfigurationField("speed", 0, 4, 4),
            });
            return BuildManifest(3, parameters, configuration);
        }

        private static NodeManifest BuildManifest(
            uint version, NodeParameterContract[] parameters, NodeConfigurationDescriptor configuration)
        {
            var childPolicy = new NodeChildPolicy(0, 0, true);
            return new NodeManifest(
                TypeId,
                version,
                "P7-005 spike fixture node.",
                "Spike",
                NodeBehaviorKind.Action,
                "Used only by the disposable P7-005 migration-tooling spike.",
                "Do not use in production.",
                NodeExecutionDomain.Burst,
                true,
                parameters,
                childPolicy,
                reads: new string[0],
                writes: new string[0],
                sideEffects: new string[0],
                new[] { NodeStatus.Success },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                configuration,
                NodeCancellationMode.AbortOnly,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Success", "{}", "Returns success.") });
        }
    }
}
