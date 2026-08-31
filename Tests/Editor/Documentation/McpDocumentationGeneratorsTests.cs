using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using AIBT.Authoring;
using AIBT.Mcp;
using AIBT.Mcp.Documentation;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Documentation
{
    public sealed class McpDocumentationGeneratorsTests
    {
        private static AIBT.Authoring.NodeRegistry BuiltInRegistry()
        {
            return AIBT.Authoring.NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
        }

        private static NodeManifest DiffLocalityFixture()
        {
            return new NodeManifest(
                "aibt.p6011-test.diff-fixture",
                1,
                "Diff-locality fixture node.",
                "P6-011 Test",
                NodeBehaviorKind.Composite,
                "Test only.",
                "Test only.",
                NodeExecutionDomain.Managed,
                true,
                Array.Empty<NodeParameterContract>(),
                new NodeChildPolicy(0, null, true),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { NodeStatus.Success },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                NodeCancellationMode.NotApplicable,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Base", "{}", "Does nothing.") });
        }

        // ---- determinism -----------------------------------------------------------------------

        [Test]
        public void NodeCatalogGenerationIsDeterministic()
        {
            var registry = BuiltInRegistry();
            Assert.That(McpNodeCatalogDocumentGenerator.Generate(registry), Is.EqualTo(McpNodeCatalogDocumentGenerator.Generate(registry)));
        }

        [Test]
        public void WorkflowGuideGenerationIsDeterministic()
        {
            Assert.That(McpWorkflowGuideGenerator.Generate(), Is.EqualTo(McpWorkflowGuideGenerator.Generate()));
        }

        [Test]
        public void RecipesGenerationIsDeterministic()
        {
            Assert.That(McpRecipesDocumentGenerator.Generate(), Is.EqualTo(McpRecipesDocumentGenerator.Generate()));
        }

        [Test]
        public void AntiPatternsGenerationIsDeterministic()
        {
            Assert.That(McpAntiPatternsDocumentGenerator.Generate(), Is.EqualTo(McpAntiPatternsDocumentGenerator.Generate()));
        }

        [Test]
        public void MigrationsGenerationIsDeterministic()
        {
            Assert.That(McpMigrationsDocumentGenerator.Generate(), Is.EqualTo(McpMigrationsDocumentGenerator.Generate()));
        }

        // ---- node catalog: diff locality and field-for-field parity ----------------------------

        [Test]
        public void AddingOneFixtureNodeChangesOnlyThatNodesSection()
        {
            var before = McpNodeCatalogDocumentGenerator.Generate(BuiltInRegistry());
            var afterRegistry = AIBT.Authoring.NodeRegistryBuilder.CreateWithBuiltIns().AddUserExtension(DiffLocalityFixture()).Build().Registry;
            var after = McpNodeCatalogDocumentGenerator.Generate(afterRegistry);

            Assert.That(after, Does.Contain("aibt.p6011-test.diff-fixture"));
            Assert.That(before, Does.Not.Contain("aibt.p6011-test.diff-fixture"));

            // Every per-node section already present before the addition must reappear byte-for-byte
            // in the "after" document -- only a new section was inserted, nothing existing changed.
            // Index 0 is the document header/intro, whose own node-count line legitimately differs
            // (one more node registered), so it is excluded from this check.
            var beforeSections = before.Split(new[] { "\n---\n\n" }, StringSplitOptions.None);
            for (var index = 1; index < beforeSections.Length; index++)
            {
                Assert.That(after, Does.Contain(beforeSections[index]), "An existing node's section must not change when an unrelated node is added.");
            }
        }

        [Test]
        public void EveryBuiltInNodesEmbeddedContractMatchesP6003FieldForField()
        {
            var registry = BuiltInRegistry();
            var query = new NodeCatalogQuery(registry);
            var document = McpNodeCatalogDocumentGenerator.Generate(registry);

            foreach (var entry in query.Search(string.Empty))
            {
                query.TryGetContract(entry.Manifest.TypeId, out var expectedContract);
                var embedded = ExtractJsonBlockFor(document, entry.Manifest.TypeId);
                var actualContract = JObject.Parse(embedded);

                Assert.That(JToken.DeepEquals(actualContract, expectedContract), Is.True,
                    "Embedded contract for '" + entry.Manifest.TypeId + "' must match NodeCatalogQuery.TryGetContract field for field.");
            }
        }

        private static string ExtractJsonBlockFor(string document, string typeId)
        {
            var marker = "### `" + typeId + "`";
            var sectionStart = document.IndexOf(marker, StringComparison.Ordinal);
            Assert.That(sectionStart, Is.GreaterThanOrEqualTo(0), "Section header not found for " + typeId);

            var fenceStart = document.IndexOf("```json\n", sectionStart, StringComparison.Ordinal) + "```json\n".Length;
            var fenceEnd = document.IndexOf("\n```", fenceStart, StringComparison.Ordinal);
            return document.Substring(fenceStart, fenceEnd - fenceStart);
        }

        // ---- no machine path / timestamp / locale-dependent text -------------------------------

        private static readonly Regex DateLikePattern = new Regex(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.Compiled);

        [Test]
        public void GeneratedDocumentsContainNoMachinePathOrRealisticDate()
        {
            var machinePathFragment = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var documents = new[]
            {
                McpNodeCatalogDocumentGenerator.Generate(BuiltInRegistry()),
                McpWorkflowGuideGenerator.Generate(),
                McpRecipesDocumentGenerator.Generate(),
                McpAntiPatternsDocumentGenerator.Generate(),
                McpMigrationsDocumentGenerator.Generate(),
            };

            foreach (var document in documents)
            {
                Assert.That(document, Does.Not.Contain(machinePathFragment));
                Assert.That(document, Does.Not.Contain(Environment.UserName));
                Assert.That(DateLikePattern.IsMatch(document), Is.False, "Generated documents must not embed a real calendar date.");
            }
        }

        // ---- workflow guide references only real, registered tool names ------------------------

        [Test]
        public void WorkflowGuideNeverReferencesAnInventedToolName()
        {
            // McpWorkflowGuideGenerator's own Tool() helper throws if a name is not a real
            // registered bridge tool -- successful generation already proves this, but assert the
            // real names it does use are drawn from the single shared list, not a fork of it.
            var guide = McpWorkflowGuideGenerator.Generate();
            foreach (var name in McpBuiltInTools.BridgeToolNames)
            {
                if (name == "get_static_resource")
                {
                    continue; // internal resource lookup, not part of the agent-facing workflow narrative
                }

                Assert.That(guide, Does.Contain("aibt_" + name), "Workflow guide should mention every real registered tool: " + name);
            }
        }

        // ---- drift check: committed generated files match a fresh regeneration -----------------

        [Test]
        public void CommittedGeneratedFilesMatchAFreshRegeneration()
        {
            var directory = FindGeneratedDocumentationDirectory();
            AssertFileMatches(Path.Combine(directory, "node-catalog.md"), McpNodeCatalogDocumentGenerator.Generate(BuiltInRegistry()));
            AssertFileMatches(Path.Combine(directory, "workflow-guide.md"), McpWorkflowGuideGenerator.Generate());
            AssertFileMatches(Path.Combine(directory, "recipes.md"), McpRecipesDocumentGenerator.Generate());
            AssertFileMatches(Path.Combine(directory, "anti-patterns.md"), McpAntiPatternsDocumentGenerator.Generate());
            AssertFileMatches(Path.Combine(directory, "migrations.md"), McpMigrationsDocumentGenerator.Generate());
        }

        private static void AssertFileMatches(string path, string expected)
        {
            Assert.That(File.Exists(path), Is.True, "Missing committed generated file: " + path);
            Assert.That(File.ReadAllText(path), Is.EqualTo(expected),
                "Committed file is stale -- run 'AIBT/MCP/Regenerate Documentation' and commit the result: " + path);
        }

        private static string FindGeneratedDocumentationDirectory()
        {
            // Found live by P6-012 running this exact test against a detached UPM harness: this host
            // project embeds AIBT directly under Assets/ (a plain folder containing package.json, not
            // registered under Packages/), so PackageManager.PackageInfo.FindForAssembly returns null
            // here -- but a real file:/registry UPM consumer (the detached harness) resolves it via
            // PackageInfo instead, at a completely different physical path. Documentation~/generated/
            // is a committed subfolder of the package's own git tree either way, so try the real
            // package-manager resolution first and fall back to the embedded-layout assumption only
            // when Package Manager genuinely does not know about it.
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(McpBuiltInTools).Assembly);
            var packageRoot = packageInfo != null
                ? packageInfo.resolvedPath
                : Path.Combine(UnityEngine.Application.dataPath, "AIBT");
            return Path.Combine(packageRoot, "Documentation~", "generated");
        }
    }
}
