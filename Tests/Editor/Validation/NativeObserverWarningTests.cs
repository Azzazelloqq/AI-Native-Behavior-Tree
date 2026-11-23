using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using AIBT.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Validation
{
    /// <summary>
    /// P11-009 (review F10, native half): native hosts (<c>NativeTreeAgent</c>, <c>ProductionTreeHost</c>,
    /// <c>ProductionTreeScheduler</c>, <c>TreeSimulation</c>) do not re-evaluate observers, so an
    /// <c>observer</c> block in a native tree compiles and changes no outcome. Every place the Editor
    /// validates or prepares a native tree now says so with the warning <c>AIBT2054</c>, naming the
    /// node: the graph (and MCP edits), the tree list, MCP <c>validate</c> and the baker. The tree still
    /// compiles to the same program; a managed tree keeps its error AIBT2051 and gets no AIBT2054.
    /// </summary>
    public sealed class NativeObserverWarningTests
    {
        private const string SourceId = "Tests/P11009/native-observer.aibt.json";
        private const string Code = "AIBT2054";
        private ProjectNodeCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = ProjectNodeCatalog.Build(new[] { FindType("AIBT.StandardNodes.StandardNativeShard") }, Array.Empty<IReferenceLeafBehaviorProvider>());
            Assert.That(_catalog.Problems, Is.Empty, string.Join("\n", _catalog.Problems));
            Assert.That(_catalog.Generated.ContainsKey("aibt.native.bool-is-true"), Is.True);
        }

        private static string GuardTree(bool withObserver, string treeId = "tests.p11009.native-observer")
            => "{ \"format\": \"aibt.tree\", \"formatVersion\": 2, \"treeId\": \"" + treeId + "\", \"name\": \"Native observer\", \"root\": \"root\", "
               + "\"blackboard\": { "
               + "\"alarm\": { \"type\": \"Bool\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": false }, "
               + "\"flag\": { \"type\": \"Bool\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": false } }, "
               + "\"nodes\": { "
               + "\"root\": { \"type\": \"aibt.core.reactive-sequence\", \"typeVersion\": 1, \"children\": [\"guard\", \"set\"] }, "
               + "\"guard\": { \"type\": \"aibt.native.bool-is-true\", \"typeVersion\": 1, \"bindings\": { \"key\": \"alarm\" }"
               + (withObserver ? ", \"observer\": { \"mode\": \"self\", \"watchedKeys\": [\"alarm\"] }" : "") + " }, "
               + "\"set\": { \"type\": \"aibt.native.set-bool\", \"typeVersion\": 1, \"parameters\": { \"value\": true }, \"bindings\": { \"key\": \"flag\" } } } }";

        [Test]
        public void TheGraphPath_ValidateAuthoring_WarnsNamingTheNode()
        {
            var diagnostics = _catalog.ValidateAuthoring(Parse(GuardTree(true)), CompilerOptions());

            Assert.That(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty, Describe(diagnostics));
            var warning = diagnostics.Where(d => d.Code.Value == Code).ToArray();
            Assert.That(warning.Length, Is.EqualTo(1), Describe(diagnostics));
            AssertWarning(warning[0]);
            Assert.That(diagnostics.Select(d => d.Code.Value), Does.Not.Contain("AIBT2051"), "a native tree is not refused");
        }

        [Test]
        public void TheSameTreeWithoutTheBlock_HasNoWarning()
        {
            var diagnostics = _catalog.ValidateAuthoring(Parse(GuardTree(false)), CompilerOptions());

            Assert.That(diagnostics.Select(d => d.Code.Value), Does.Not.Contain(Code), Describe(diagnostics));
        }

        [Test]
        public void TheTreeStillCompiles_ToTheSameProgramAsBefore()
        {
            // The warning is added by the Editor's validation; the compiler's own output is unchanged, so
            // the program (bytes and content hash) is what it was before P11-009.
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(Parse(GuardTree(true)), _catalog.Generated.Values, _catalog.RegisteredTypes, CompilerOptions());

            Assert.That(compiled.Success, Is.True, Describe(compiled.Diagnostics));
            Assert.That(compiled.Diagnostics.Select(d => d.Code.Value), Does.Not.Contain(Code));
        }

        [Test]
        public void TheTreeList_ShowsTheWarning()
        {
            var path = Path.Combine(Path.GetTempPath(), "aibt-p11009-" + Guid.NewGuid().ToString("N") + ".aibt");
            try
            {
                File.WriteAllText(path, GuardTree(true));
                var entry = TreeCatalogEntry.Read(path, _catalog.Registry, null,
                    new TreeCatalogEntry.NativeFacts(_catalog.Generated, Array.Empty<(string, string)>()));
                Assert.That(entry.Status, Is.EqualTo("Warnings"), entry.Details);
                Assert.That(entry.Details, Does.Contain(Code + ": ").And.Contain("'guard'"), entry.Details);

                File.WriteAllText(path, GuardTree(false));
                var without = TreeCatalogEntry.Read(path, _catalog.Registry, null,
                    new TreeCatalogEntry.NativeFacts(_catalog.Generated, Array.Empty<(string, string)>()));
                Assert.That(without.Status, Is.EqualTo("Valid"), without.Details);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ThePreparePath_TheBaker_WarnsAndStillBakes()
        {
            var asset = ProductionTreeAssetBaker.Create(Parse(GuardTree(true)), SourceId,
                FindType("AIBT.Tests.Integration.NativeRuntime.NativeLibraryCatalog"), out var diagnostics);
            try
            {
                Assert.That(asset, Is.Not.Null, Describe(diagnostics));
                var warning = diagnostics.Where(d => d.Code.Value == Code).ToArray();
                Assert.That(warning.Length, Is.EqualTo(1), Describe(diagnostics));
                AssertWarning(warning[0]);
            }
            finally
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void McpValidate_ShowsTheWarning_AndTheTreeStaysValid()
        {
            Assume.That(ProjectNodeCatalogCache.Current.Generated.ContainsKey("aibt.native.bool-is-true"), Is.True,
                "The package's native library is a Player assembly here.");
            var projectRoot = Path.Combine(Path.GetTempPath(), "aibt-p11009-mcp-" + Guid.NewGuid().ToString("N"));
            var assets = Path.Combine(projectRoot, "Assets");
            Directory.CreateDirectory(assets);
            try
            {
                File.WriteAllText(Path.Combine(assets, "native-observer.aibt"), GuardTree(true));
                var validated = Dispatch(assets, "validate", new JObject { ["treeId"] = "tests.p11009.native-observer" }, "Compilation");
                var result = validated["result"];
                Assert.That((bool)result["valid"], Is.True, validated.ToString());
                var warnings = ((JArray)result["diagnostics"]).Where(item => (string)item["code"] == Code).ToArray();
                Assert.That(warnings.Length, Is.EqualTo(1), validated.ToString());
                Assert.That((string)warnings[0]["severity"], Is.EqualTo("warning"), validated.ToString());
                Assert.That((string)warnings[0]["jsonPointer"], Is.EqualTo("/nodes/guard/observer"), validated.ToString());
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true);
            }
        }

        // ---- P11-014 items 1-3 -------------------------------------------------------------

        /// <summary>A managed tree (package node aibt.unity.is-true) whose guard has an observer block.</summary>
        private static string ManagedGuardTree(string treeId)
            => "{ \"format\": \"aibt.tree\", \"formatVersion\": 2, \"treeId\": \"" + treeId + "\", \"name\": \"Managed observer\", \"root\": \"root\", "
               + "\"blackboard\": { "
               + "\"alarm\": { \"type\": \"Bool\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": false }, "
               + "\"flag\": { \"type\": \"Bool\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": false } }, "
               + "\"nodes\": { "
               + "\"root\": { \"type\": \"aibt.core.reactive-sequence\", \"typeVersion\": 1, \"children\": [\"guard\", \"set\"] }, "
               + "\"guard\": { \"type\": \"aibt.unity.is-true\", \"typeVersion\": 1, \"parameters\": { \"key\": \"alarm\" }, "
               + "\"observer\": { \"mode\": \"self\", \"watchedKeys\": [\"alarm\"] } }, "
               + "\"set\": { \"type\": \"aibt.unity.set-bool\", \"typeVersion\": 1, \"parameters\": { \"key\": \"flag\" } } } }";

        /// <summary>Only built-in nodes: the random-condition guard and a wait. Either host can run it.</summary>
        private const string BuiltInGuardTree = "{ \"format\": \"aibt.tree\", \"formatVersion\": 2, \"treeId\": \"tests.p11014.builtin-observer\", "
            + "\"name\": \"Built-in observer\", \"root\": \"root\", "
            + "\"blackboard\": { \"alarm\": { \"type\": \"Bool\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": false } }, "
            + "\"nodes\": { "
            + "\"root\": { \"type\": \"aibt.core.reactive-sequence\", \"typeVersion\": 1, \"children\": [\"guard\", \"hold\"] }, "
            + "\"guard\": { \"type\": \"aibt.stdlib.random-condition\", \"typeVersion\": 1, \"parameters\": { \"success-chance-percent\": 50 }, "
            + "\"observer\": { \"mode\": \"self\", \"watchedKeys\": [\"alarm\"] } }, "
            + "\"hold\": { \"type\": \"aibt.stdlib.wait\", \"typeVersion\": 1, \"parameters\": { \"ticks\": 2 } } } }";

        /// <summary>Item 1: MCP validate reports what BehaviorTreeAgent does with a managed tree: AIBT2051, not valid.</summary>
        [Test]
        public void McpValidate_AManagedTreeWithAnObserver_IsRefusedAsTheAgentRefusesIt()
        {
            Assume.That(ProjectNodeCatalogCache.Current.Registry.TryGet("aibt.unity.is-true", out _), Is.True);
            WithMcpProject("p11014-managed-observer", ManagedGuardTree("tests.p11014.managed-observer"), assets =>
            {
                var validated = Dispatch(assets, "validate", new JObject { ["treeId"] = "tests.p11014.managed-observer" }, "Compilation");
                var result = validated["result"];
                Assert.That((bool)result["valid"], Is.False, validated.ToString());
                var refusal = ((JArray)result["diagnostics"]).Where(item => (string)item["code"] == "AIBT2051").ToArray();
                Assert.That(refusal.Length, Is.EqualTo(1), validated.ToString());
                Assert.That((string)refusal[0]["severity"], Is.EqualTo("error"), validated.ToString());
                Assert.That((string)refusal[0]["jsonPointer"], Is.EqualTo("/nodes/guard/observer"), validated.ToString());
                Assert.That(((JArray)result["diagnostics"]).Any(item => (string)item["code"] == Code), Is.False, "no native warning for a managed tree");
            });
        }

        /// <summary>Item 1: MCP compile does not report success for a tree the agent refuses.</summary>
        [Test]
        public void McpCompile_AManagedTreeWithAnObserver_DoesNotSucceed()
        {
            Assume.That(ProjectNodeCatalogCache.Current.Registry.TryGet("aibt.unity.is-true", out _), Is.True);
            WithMcpProject("p11014-managed-observer-compile", ManagedGuardTree("tests.p11014.managed-observer-compile"), assets =>
            {
                var compiled = Dispatch(assets, "compile", new JObject { ["treeId"] = "tests.p11014.managed-observer-compile" }, "Compilation");
                var result = compiled["result"];
                Assert.That((bool)result["success"], Is.False, compiled.ToString());
                Assert.That(((JArray)result["diagnostics"]).Count(item => (string)item["code"] == "AIBT2051"), Is.EqualTo(1), compiled.ToString());
            });
        }

        /// <summary>Item 2: the bake warns for an observer inside a subtree (the expanded tree, as the graph sees it).</summary>
        [Test]
        public void ThePreparePath_TheBaker_WarnsForAnObserverInsideASubtree()
        {
            const string ChildId = "Tests/P11014/guard-child.aibt";
            var child = Parse(GuardTree(true, "tests.p11014.guard-child")
                .Replace("\"formatVersion\": 2", "\"formatVersion\": 3")
                .Replace("\"root\": \"root\", ", "\"root\": \"root\", \"interface\": { \"version\": 1, \"keys\": [\"alarm\", \"flag\"] }, "));
            var parent = Parse("{ \"format\": \"aibt.tree\", \"formatVersion\": 2, \"treeId\": \"tests.p11014.guard-parent\", \"name\": \"Parent\", \"root\": \"root\", "
                + "\"blackboard\": { "
                + "\"alarm\": { \"type\": \"Bool\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": false }, "
                + "\"flag\": { \"type\": \"Bool\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": false } }, "
                + "\"nodes\": { \"root\": { \"type\": \"aibt.core.memory-sequence\", \"typeVersion\": 1, \"children\": [] } } }");
            var withReference = SubtreeGraphEdits.AddReference(parent, SubtreeGraphEdits.CreateReference(parent, child, new NodeId("use")), new NodeId("root"));
            var resolver = new InMemorySubtreeResolver().Add(child, ChildId);
            var asset = ProductionTreeAssetBaker.Create(withReference, SourceId,
                FindType("AIBT.Tests.Integration.NativeRuntime.NativeLibraryCatalog"), resolver, out var diagnostics);
            try
            {
                Assert.That(asset, Is.Not.Null, Describe(diagnostics));
                var warning = diagnostics.Where(d => d.Code.Value == Code).ToArray();
                Assert.That(warning.Length, Is.EqualTo(1), Describe(diagnostics));
                Assert.That(warning[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
                Assert.That(warning[0].Message, Does.Contain("native"));
            }
            finally
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        /// <summary>
        /// Item 3: a tree of built-in nodes runs in either host. The warning keeps its code and says what
        /// each host does: BehaviorTreeAgent refuses the tree (AIBT2051), a native host ignores the block.
        /// </summary>
        [Test]
        public void ABuiltInOnlyTree_TheWarningSaysWhatEachHostDoes()
        {
            var options = new ReferenceCompilerOptions(SourceId, new ReferenceCompilationPolicy(requireDeterministicNodes: false),
                new CompiledCompilerVersion(1, 0, 0, 6));
            var diagnostics = _catalog.ValidateAuthoring(Parse(BuiltInGuardTree), options);
            var warning = diagnostics.Where(d => d.Code.Value == Code).ToArray();
            Assert.That(warning.Length, Is.EqualTo(1), Describe(diagnostics));
            Assert.That(warning[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(warning[0].Message, Does.Contain("BehaviorTreeAgent").And.Contain("AIBT2051").And.Contain("native"), warning[0].Message);
            Assert.That(diagnostics.Select(d => d.Code.Value), Does.Not.Contain("AIBT2051"), "the Editor does not refuse a tree a native host can run");

            var nativeWarning = _catalog.ValidateAuthoring(Parse(GuardTree(true)), CompilerOptions()).Single(d => d.Code.Value == Code);
            Assert.That(nativeWarning.Message, Does.Not.Contain("AIBT2051"), "a tree with native library nodes runs only in a native host");
        }

        private static void WithMcpProject(string name, string json, Action<string> body)
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "aibt-" + name + "-" + Guid.NewGuid().ToString("N"));
            var assets = Path.Combine(projectRoot, "Assets");
            Directory.CreateDirectory(assets);
            try
            {
                File.WriteAllText(Path.Combine(assets, name + ".aibt"), json);
                // A project whose policy admits managed, main-thread and nondeterministic nodes, as one that
                // runs trees in a BehaviorTreeAgent has: only the observer block stands in the way.
                Directory.CreateDirectory(Path.Combine(projectRoot, ".aibt"));
                File.WriteAllText(Path.Combine(projectRoot, ".aibt", "policy.json"), new JObject
                {
                    ["format"] = "aibt.policy", ["formatVersion"] = 1, ["allowManagedNodes"] = true, ["allowMainThreadNodes"] = true,
                    ["requireTreeDescription"] = false, ["requireNodeDescriptions"] = false, ["blackboardNaming"] = "any",
                    ["requireDeterministicNodes"] = false, ["allowSideEffects"] = true, ["unreachableNodes"] = "error",
                    ["supportsAgentScope"] = false, ["supportsSharedScope"] = false,
                    ["forbiddenNodeTypes"] = new JArray(), ["warningsAsErrors"] = new JArray(),
                    ["performance"] = new JObject { ["forbidUnboundedRepeaters"] = false, ["requireEventDrivenServices"] = false },
                }.ToString());
                body(assets);
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true);
            }
        }

        private static void AssertWarning(Diagnostic warning)
        {
            Assert.That(warning.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(warning.Location.NodeId.Value, Is.EqualTo("guard"));
            Assert.That(warning.Location.JsonPointer, Is.EqualTo("/nodes/guard/observer"));
            Assert.That(warning.Message, Does.Contain("'guard'").And.Contain("native").And.Contain("aibt.core.reactive-sequence"));
        }

        private static ReferenceCompilerOptions CompilerOptions()
            => new ReferenceCompilerOptions(SourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6));

        private static Type FindType(string fullName)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(fullName)).FirstOrDefault(found => found != null);
            Assert.That(type, Is.Not.Null, fullName);
            return type;
        }

        private static JObject Dispatch(string assets, string tool, JObject args, string category)
        {
            var request = new JObject { ["tool"] = tool, ["args"] = args, ["grantedCategories"] = new JArray(category) };
            return JObject.Parse(McpToolDispatcher.Dispatch(request.ToString(Newtonsoft.Json.Formatting.None), assets));
        }

        private static string Describe(IEnumerable<Diagnostic> diagnostics)
            => string.Join("\n", diagnostics.Select(diagnostic => diagnostic.Code.Value + " " + diagnostic.Severity + ": " + diagnostic.Message));

        private static TreeDocument Parse(string json)
        {
            var read = CanonicalTreeJson.Parse(json, SourceId);
            Assert.That(read.Document, Is.Not.Null, Describe(read.Diagnostics) + "\n" + json);
            return read.Document;
        }
    }
}
