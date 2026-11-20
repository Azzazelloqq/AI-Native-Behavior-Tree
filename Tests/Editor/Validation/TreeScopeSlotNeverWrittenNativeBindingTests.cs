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
    /// P11-003: <c>AIBT2043 TreeScopeSlotNeverWritten</c> for native (generated) nodes. A key bound
    /// through a <c>bindings</c> member whose declared access is Write or ReadWrite counts as written
    /// (no note); a key bound only through Read members stays an input (note). The access comes from
    /// the generated descriptor, looked up by the registry entry's manifest instance
    /// (<see cref="GeneratedBindingWriteIndex"/>); the generated compiler drops the notes of its
    /// semantic projection, where every key looks unwritten.
    /// </summary>
    public sealed class TreeScopeSlotNeverWrittenNativeBindingTests
    {
        private const string SourceId = "Tests/P11003/native.aibt.json";
        private ProjectNodeCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = Catalog();
            Assert.That(_catalog.Problems, Is.Empty, string.Join("\n", _catalog.Problems));
            Assert.That(_catalog.Generated.ContainsKey("aibt.native.set-bool"), Is.True);
            Assert.That(_catalog.Generated.ContainsKey("aibt.tests.external-tree-write-writer"), Is.True);
        }

        // ---- the rule --------------------------------------------------------------------------

        [Test]
        public void AKeyAWriteMemberBinds_IsNotAnInput()
        {
            var document = Parse(Tree("tests.p11003.write", "root",
                new[] { Bool("flag"), Bool("fed") },
                Composite("root", "aibt.core.memory-sequence", "check", "set"),
                Leaf("check", "aibt.native.bool-is-true", null, "\"key\": \"fed\""),
                Leaf("set", "aibt.native.set-bool", "\"value\": true", "\"key\": \"flag\"")));

            Assert.That(Inputs(Validate(document)), Is.EqualTo(new[] { "fed" }));
        }

        [Test]
        public void AKeyAReadWriteMemberBinds_IsNotAnInput()
        {
            var document = Parse(Tree("tests.p11003.read-write", "root",
                new[] { Int("count"), Bool("fed") },
                Composite("root", "aibt.core.memory-sequence", "check", "count-up"),
                Leaf("check", "aibt.native.bool-is-true", null, "\"key\": \"fed\""),
                Leaf("count-up", "aibt.native.increment-int", "\"amount\": 1", "\"key\": \"count\"")));

            Assert.That(Inputs(Validate(document)), Is.EqualTo(new[] { "fed" }));
        }

        [Test]
        public void AKeyOnlyReadMembersBind_StaysAnInput()
        {
            var document = Parse(Tree("tests.p11003.read", "root",
                new[] { Bool("seen"), Long("ready-at") },
                Composite("root", "aibt.core.memory-sequence", "is-seen", "ready"),
                Leaf("is-seen", "aibt.native.bool-is-true", null, "\"key\": \"seen\""),
                Leaf("ready", "aibt.native.deadline-passed", null, "\"key\": \"ready-at\"")));

            Assert.That(Inputs(Validate(document)), Is.EqualTo(new[] { "ready-at", "seen" }));
        }

        [Test]
        public void AKeyOneNodeWritesAndAnotherReads_IsNotAnInput_AndEachMemberOfANodeCounts()
        {
            // set-deadline writes ready-at, deadline-passed reads it; copy-bool reads "from" and writes "to".
            var document = Parse(Tree("tests.p11003.write-read", "root",
                new[] { Long("ready-at"), Bool("source"), Bool("target") },
                Composite("root", "aibt.core.memory-sequence", "ready", "cool", "copy"),
                Leaf("ready", "aibt.native.deadline-passed", null, "\"key\": \"ready-at\""),
                Leaf("cool", "aibt.native.set-deadline", "\"duration-microseconds\": 1000", "\"key\": \"ready-at\""),
                Leaf("copy", "aibt.native.copy-bool", null, "\"from\": \"source\", \"to\": \"target\"")));

            Assert.That(Inputs(Validate(document)), Is.EqualTo(new[] { "source" }));
        }

        [Test]
        public void AProjectBurstNode_CountsByItsDeclaredAccess()
        {
            // The P7-039 fixture shard is a project (non-package) shard: "owned-target" is a Write
            // member, "live-target" a Read member.
            var document = Parse(Tree("tests.p11003.project-nodes", "root",
                new[] { Int("owned"), Float("live") },
                Composite("root", "aibt.core.memory-sequence", "write", "read"),
                Leaf("write", "aibt.tests.external-tree-write-writer", "\"value\": 3", "\"owned-target\": \"owned\""),
                Leaf("read", "aibt.tests.external-tree-write-reader", null, "\"live-target\": \"live\"")));

            Assert.That(Inputs(Validate(document)), Is.EqualTo(new[] { "live" }));
        }

        [Test]
        public void ANativeTreeWithAManagedKeyParameter_KeepsTheManagedRule_AndAddsTheNativeOne()
        {
            var catalog = ProjectNodeCatalog.Build(Shards(), Array.Empty<IReferenceLeafBehaviorProvider>(), null,
                AIBT.Tests.Editor.Agents.StandardNodeKit.Nodes);
            Assert.That(catalog.Problems, Is.Empty, string.Join("\n", catalog.Problems));
            var document = Parse(Tree("tests.p11003.mixed", "root",
                new[] { Bool("managed-key"), Bool("native-written"), Bool("native-read"), Bool("unused") },
                Composite("root", "aibt.core.memory-sequence", "managed", "native-set", "native-check"),
                "\"managed\": { \"type\": \"aibt.unity.set-bool\", \"typeVersion\": 1, \"parameters\": { \"key\": \"managed-key\", \"value\": true } }",
                Leaf("native-set", "aibt.native.set-bool", "\"value\": true", "\"key\": \"native-written\""),
                Leaf("native-check", "aibt.native.bool-is-true", null, "\"key\": \"native-read\"")));

            var diagnostics = TreeValidator.Validate(document, catalog.Registry, Options(), catalog.RegisteredTypes);

            Assert.That(Inputs(diagnostics), Is.EqualTo(new[] { "native-read", "unused" }));
        }

        [Test]
        public void AManifestNoGeneratedDescriptorRegistered_KeepsTheEarlierBehaviour()
        {
            // A Burst manifest built by hand (as a test fixture or a manifest read back from JSON
            // would be) is not in the index: its bindings do not count, so the note stays.
            var manifest = Manifest("tests.p11003.unregistered-writer");
            Assert.That(GeneratedBindingWriteIndex.TryGetWrittenBindings(manifest, out _), Is.False);
            var registry = new NodeRegistryBuilder().AddUserExtension(manifest).Build().Registry;
            var document = new TreeDocument(TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion, new TreeId("tests.p11003.unregistered"),
                "Unregistered", new NodeId("root"),
                new[] { new NodeDocument(new NodeId("root"), manifest.TypeId, 1, null, null, null, null, null, null,
                    new NodeBindingMap(new[] { new KeyValuePair<string, string>("key", "flag") })) },
                new[] { new BlackboardKeyDefinition("flag", "flag", BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool), BlackboardScope.Tree, BlackboardDefaultValue.Bool(false)) });

            Assert.That(Inputs(TreeValidator.Validate(document, registry)), Is.EqualTo(new[] { "flag" }));
        }

        // ---- the index ---------------------------------------------------------------------------

        [Test]
        public void TheIndexHoldsOnlyTreeScopeWriteAndReadWriteMembers()
        {
            Assert.That(Written("aibt.native.set-bool"), Is.EqualTo(new[] { "key" }));
            Assert.That(Written("aibt.native.increment-int"), Is.EqualTo(new[] { "key" }));
            Assert.That(Written("aibt.native.copy-bool"), Is.EqualTo(new[] { "to" }));
            Assert.That(Written("aibt.native.bool-is-true"), Is.Empty);
            Assert.That(Written("aibt.tests.external-tree-write-writer"), Is.EqualTo(new[] { "owned-target" }));
            Assert.That(Written("aibt.tests.external-tree-write-reader"), Is.Empty);
        }

        [Test]
        public void ASecondDescriptorForTheSameManifest_DoesNotThrow_AndKeepsTheWrites()
        {
            var descriptor = _catalog.Generated["aibt.native.set-bool"];
            GeneratedNodeDescriptor again = null;
            Assert.DoesNotThrow(() => again = new GeneratedNodeDescriptor(descriptor.Manifest, descriptor.Configuration, descriptor.Memory,
                descriptor.Bindings, descriptor.CallbackCapabilities, descriptor.HasRandomStream));
            Assert.That(again.Manifest, Is.SameAs(descriptor.Manifest));
            Assert.That(Written("aibt.native.set-bool"), Is.EqualTo(new[] { "key" }));

            // Building the catalog again materializes new manifests; the earlier ones keep their entries.
            var rebuilt = Catalog();
            Assert.That(rebuilt.Generated["aibt.native.set-bool"].Manifest, Is.Not.SameAs(descriptor.Manifest));
            Assert.That(GeneratedBindingWriteIndex.TryGetWrittenBindings(rebuilt.Generated["aibt.native.set-bool"].Manifest, out var fresh), Is.True);
            Assert.That(fresh, Is.EquivalentTo(new[] { "key" }));
            Assert.That(Written("aibt.native.set-bool"), Is.EqualTo(new[] { "key" }));
        }

        [Test]
        public void DescriptorsWithDifferentWritesForOneManifest_KeepOnlyTheMembersEveryOneWrites()
        {
            var manifest = Manifest("tests.p11003.shared-manifest");
            var copy = _catalog.Generated["aibt.native.copy-bool"].Bindings;       // "to" is written
            var set = _catalog.Generated["aibt.native.set-bool"].Bindings;         // "key" is written
            var copyAgain = _catalog.Generated["aibt.native.copy-int"].Bindings;   // "to" is written

            Assert.DoesNotThrow(() => GeneratedBindingWriteIndex.Register(manifest, copy));
            Assert.DoesNotThrow(() => GeneratedBindingWriteIndex.Register(manifest, copyAgain));
            Assert.That(GeneratedBindingWriteIndex.TryGetWrittenBindings(manifest, out var both), Is.True);
            Assert.That(both, Is.EquivalentTo(new[] { "to" }));

            Assert.DoesNotThrow(() => GeneratedBindingWriteIndex.Register(manifest, set));
            Assert.That(GeneratedBindingWriteIndex.TryGetWrittenBindings(manifest, out var none), Is.True);
            Assert.That(none, Is.Empty, "An ambiguous member never removes the note.");
        }

        // ---- the compiler's projection -------------------------------------------------------------

        [Test]
        public void TheFilterMatchesOnlyTheValidatorsInfoNote()
        {
            var note = TreeValidationDiagnosticCatalog.Create(TreeValidationDiagnosticCodes.TreeScopeSlotNeverWritten, "Input key 'x': ...",
                new DiagnosticLocation("doc", "/blackboard/x"));
            Assert.That(note.Severity, Is.EqualTo(DiagnosticSeverity.Info));
            Assert.That(TreeValidator.IsTreeScopeSlotNeverWrittenNote(note), Is.True);

            // The compiler's ScopeContractMismatch shares the code AIBT2043 and is an Error.
            var mismatch = ReferenceCompilerDiagnostics.Create(ReferenceCompilerDiagnosticCodes.ScopeContractMismatch, "mismatch", "doc", "/blackboardContracts");
            Assert.That(mismatch.Code.Value, Is.EqualTo("AIBT2043"));
            Assert.That(mismatch.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(TreeValidator.IsTreeScopeSlotNeverWrittenNote(mismatch), Is.False);

            Assert.That(TreeValidator.IsTreeScopeSlotNeverWrittenNote(new Diagnostic(new DiagnosticCode("AIBT2043"), DiagnosticSeverity.Warning, "x", new DiagnosticLocation("doc"))), Is.False);
            Assert.That(TreeValidator.IsTreeScopeSlotNeverWrittenNote(new Diagnostic(TreeValidationDiagnosticCodes.MigrationApplied, DiagnosticSeverity.Info, "x", new DiagnosticLocation("doc"))), Is.False);
            Assert.That(TreeValidator.IsTreeScopeSlotNeverWrittenNote(null), Is.False);
        }

        [Test]
        public void TheGeneratedCompiler_ReportsEachInputKeyOnce_AndNoKeyANativeNodeWrites()
        {
            var document = Parse(Tree("tests.p11003.compiler", "root",
                new[] { Bool("flag"), Bool("fed"), Int("count"), Long("ready-at") },
                Composite("root", "aibt.core.memory-sequence", "check", "set", "count-up", "ready"),
                Leaf("check", "aibt.native.bool-is-true", null, "\"key\": \"fed\""),
                Leaf("set", "aibt.native.set-bool", "\"value\": true", "\"key\": \"flag\""),
                Leaf("count-up", "aibt.native.increment-int", "\"amount\": 1", "\"key\": \"count\""),
                Leaf("ready", "aibt.native.deadline-passed", null, "\"key\": \"ready-at\"")));

            var compiled = GeneratedCompiledProgramV2Compiler.Compile(document, _catalog.Generated.Values, _catalog.RegisteredTypes, CompilerOptions());

            Assert.That(compiled.Success, Is.True, Describe(compiled.Diagnostics));
            Assert.That(compiled.Diagnostics.Count(TreeValidator.IsTreeScopeSlotNeverWrittenNote), Is.EqualTo(2), Describe(compiled.Diagnostics));
            Assert.That(Inputs(compiled.Diagnostics), Is.EqualTo(new[] { "fed", "ready-at" }));
        }

        [Test]
        public void TheReferenceCompilerDirectly_ReportsTheSameNotesAsTheValidator()
        {
            // The managed agent's path (ReferenceCompiler.Compile without GeneratedCompiledProgramV2) is unchanged.
            const string json = @"{ ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11003.managed"", ""name"": ""Managed"", ""root"": ""set"",
  ""blackboard"": {
    ""flag"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false },
    ""unused"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false } },
  ""nodes"": { ""set"": { ""type"": ""aibt.unity.set-bool"", ""typeVersion"": 1, ""parameters"": { ""key"": ""flag"", ""value"": true } } } }";
            var document = Parse(json);
            var options = AIBT.Agents.ManagedTreeProgram.CreateOptions(SourceId);
            var registry = AIBT.Tests.Editor.Agents.StandardNodeKit.Nodes.Registry;

            var compiled = ReferenceCompiler.Compile(document, registry, options);
            var validated = TreeValidator.Validate(document, registry, options.Policy.CreateValidationOptions(options.SourceId));

            Assert.That(Inputs(compiled.Diagnostics), Is.EqualTo(new[] { "unused" }));
            Assert.That(Inputs(validated), Is.EqualTo(new[] { "unused" }));
        }

        // ---- subtrees -------------------------------------------------------------------------------

        [Test]
        public void ASubtree_NativeWritesOfAPrivateKeyAndOfAnInterfaceKey_AreNotInputs()
        {
            var resolver = new InMemorySubtreeResolver().Add(Parse(CooldownSubtree, "Tests/P11003/cooldown.aibt"), "Tests/P11003/cooldown.aibt");
            var parent = Parse(ParentTree, "Tests/P11003/parent.aibt");

            // The subtree expander: the private key becomes "strike:spot".
            var expansion = SubtreeExpander.Expand(parent, resolver, _catalog.Registry, Options(), _catalog.RegisteredTypes);
            Assert.That(expansion.Success, Is.True, Describe(expansion.Diagnostics));
            Assert.That(expansion.Document.Blackboard.Select(key => key.Id), Does.Contain("strike:spot"));
            var expected = new[] { "idle-flag", "intruder-seen" };
            Assert.That(Inputs(TreeValidator.Validate(expansion.Document, _catalog.Registry, Options(), _catalog.RegisteredTypes)), Is.EqualTo(expected));

            // The generated compiler's expansion branch (node source IDs present).
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(parent, _catalog.Generated.Values, _catalog.RegisteredTypes, CompilerOptions(), resolver);
            Assert.That(compiled.Success, Is.True, Describe(compiled.Diagnostics));
            Assert.That(Inputs(compiled.Diagnostics), Is.EqualTo(expected));

            // The graph's validation of a tree with subtrees.
            Assert.That(Inputs(_catalog.ValidateAuthoring(parent, CompilerOptions(), resolver)), Is.EqualTo(expected));
        }

        // ---- every path that validates: the registry entry holds the descriptor's manifest -------

        [Test]
        public void EveryRegistryANativeTreeIsValidatedWith_HoldsTheDescriptorsOwnManifest()
        {
            AssertSameManifests(_catalog.Registry, _catalog.Generated.Values, "ProjectNodeCatalog.Build (import expansion, graph, MCP)");
            AssertSameManifests(GeneratedNodeRegistry.Build(_catalog.Generated.Values).Registry, _catalog.Generated.Values,
                "GeneratedNodeRegistry.Build (generated compiler, prepare_runtime)");
            var forCatalog = ProjectNodeCatalog.ForCatalog(FindType("AIBT.Tests.Integration.NativeRuntime.NativeLibraryCatalog"));
            Assert.That(forCatalog.Problems, Is.Empty, string.Join("\n", forCatalog.Problems));
            AssertSameManifests(forCatalog.Registry, forCatalog.Generated.Values, "ProjectNodeCatalog.ForCatalog (bake, prepare_runtime)");
            var current = ProjectNodeCatalogCache.Current;
            Assume.That(current.Generated.ContainsKey("aibt.native.set-bool"), Is.True, "The package's native library is a Player assembly here.");
            AssertSameManifests(current.Registry, current.Generated.Values, "ProjectNodeCatalogCache.Current (graph, MCP validate and edits)");
        }

        [Test]
        public void TheGraphPath_ValidateAuthoring_HasNoNoteForAKeyANativeNodeWrites()
        {
            Assert.That(Inputs(_catalog.ValidateAuthoring(Parse(WriteAndReadTree), CompilerOptions())), Is.EqualTo(new[] { "fed" }));
        }

        [Test]
        public void TheGraphTreeList_ShowsANativeTreeAsNeedingNativeCompile_WithoutNotes()
        {
            // The tree list validates with the project registry but describes a valid native tree by
            // its status only, so no note reaches it (before P11-003 as after).
            var path = Path.Combine(Path.GetTempPath(), "aibt-p11003-" + Guid.NewGuid().ToString("N") + ".aibt");
            try
            {
                File.WriteAllText(path, WriteAndReadTree);
                var entry = TreeCatalogEntry.Read(path, _catalog.Registry);
                Assert.That(entry.Status, Is.EqualTo("Needs native compile"), entry.Details);
                Assert.That(entry.Details, Does.Not.Contain("AIBT2043"), entry.Details);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ThePreparePath_TheBaker_HasNoNoteForAKeyANativeNodeWrites()
        {
            var asset = ProductionTreeAssetBaker.Create(Parse(WriteAndReadTree), SourceId,
                FindType("AIBT.Tests.Integration.NativeRuntime.NativeLibraryCatalog"), out var diagnostics);
            try
            {
                Assert.That(asset, Is.Not.Null, Describe(diagnostics));
                Assert.That(Inputs(diagnostics), Is.EqualTo(new[] { "fed" }));
            }
            finally
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void McpValidateAndMcpEdits_HaveNoNoteForAKeyANativeNodeWrites()
        {
            Assume.That(ProjectNodeCatalogCache.Current.Generated.ContainsKey("aibt.native.set-bool"), Is.True,
                "The package's native library is a Player assembly here.");
            var projectRoot = Path.Combine(Path.GetTempPath(), "aibt-p11003-mcp-" + Guid.NewGuid().ToString("N"));
            var assets = Path.Combine(projectRoot, "Assets");
            Directory.CreateDirectory(assets);
            try
            {
                File.WriteAllText(Path.Combine(assets, "native.aibt"), WriteAndReadTree);
                var validated = Dispatch(assets, "validate", new JObject { ["treeId"] = "tests.p11003.graph" }, "Compilation");
                Assert.That(McpInputs(validated), Is.EqualTo(new[] { "fed" }), validated.ToString());

                var created = Dispatch(assets, "create_tree", new JObject
                {
                    ["treeId"] = "tests.p11003.mcp-edit", ["name"] = "MCP edit", ["path"] = "edit.aibt", ["formatVersion"] = 2,
                    ["description"] = "A native tree edited through MCP.",
                    ["rootNode"] = new JObject { ["id"] = "root", ["typeId"] = "aibt.core.memory-sequence", ["typeVersion"] = 1, ["description"] = "Root." },
                    ["blackboard"] = new JArray(
                        new JObject { ["id"] = "flag", ["valueType"] = "Bool", ["description"] = "Written by a node." },
                        new JObject { ["id"] = "fed", ["valueType"] = "Bool", ["description"] = "Fed by the game." }),
                }, "SemanticEdit");
                Assert.That((bool)created["result"]["accepted"], Is.True, created.ToString());
                Assert.That(McpInputs(created), Is.EqualTo(new[] { "fed", "flag" }), "Before a node binds them both keys are inputs.");

                var added = Dispatch(assets, "add_node", new JObject
                {
                    ["treeId"] = "tests.p11003.mcp-edit", ["expectedHash"] = (string)created["result"]["contentHash"], ["parentId"] = "root",
                    ["node"] = new JObject
                    {
                        ["id"] = "set", ["typeId"] = "aibt.native.set-bool", ["typeVersion"] = 1, ["description"] = "Sets the flag.",
                        ["parameters"] = new JObject { ["value"] = true }, ["bindings"] = new JObject { ["key"] = "flag" },
                    },
                }, "SemanticEdit");
                Assert.That((bool)added["result"]["accepted"], Is.True, added.ToString());
                Assert.That(McpInputs(added), Is.EqualTo(new[] { "fed" }), added.ToString());
            }
            finally
            {
                if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true);
            }
        }

        // ---- helpers -------------------------------------------------------------------------------

        private const string WriteAndReadTree = @"{ ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11003.graph"", ""name"": ""Graph"", ""root"": ""root"",
  ""blackboard"": {
    ""flag"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false },
    ""fed"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false } },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""check"", ""set""] },
    ""check"": { ""type"": ""aibt.native.bool-is-true"", ""typeVersion"": 1, ""bindings"": { ""key"": ""fed"" } },
    ""set"": { ""type"": ""aibt.native.set-bool"", ""typeVersion"": 1, ""parameters"": { ""value"": true }, ""bindings"": { ""key"": ""flag"" } } } }";

        // Interface keys: ready-at (written by set-deadline), seen (only read). Private key: spot (written by random-int).
        private const string CooldownSubtree = @"{ ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11003.cooldown"", ""name"": ""Cooldown"", ""root"": ""seq"",
  ""interface"": { ""version"": 1, ""keys"": [""ready-at"", ""seen""] },
  ""blackboard"": {
    ""ready-at"": { ""type"": ""Int64"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0 },
    ""seen"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false },
    ""spot"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0 } },
  ""nodes"": {
    ""seq"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""is-seen"", ""ready"", ""cool"", ""pick""] },
    ""is-seen"": { ""type"": ""aibt.native.bool-is-true"", ""typeVersion"": 1, ""bindings"": { ""key"": ""seen"" } },
    ""ready"": { ""type"": ""aibt.native.deadline-passed"", ""typeVersion"": 1, ""bindings"": { ""key"": ""ready-at"" } },
    ""cool"": { ""type"": ""aibt.native.set-deadline"", ""typeVersion"": 1, ""parameters"": { ""duration-microseconds"": 1000000 }, ""bindings"": { ""key"": ""ready-at"" } },
    ""pick"": { ""type"": ""aibt.native.random-int"", ""typeVersion"": 1, ""parameters"": { ""min"": 0, ""max"": 2 }, ""bindings"": { ""key"": ""spot"" } } } }";

        private const string ParentTree = @"{ ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11003.parent"", ""name"": ""Parent"", ""root"": ""root"",
  ""blackboard"": {
    ""weapon-ready-at"": { ""type"": ""Int64"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0 },
    ""intruder-seen"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false },
    ""alarm-raised"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false },
    ""idle-flag"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false } },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""strike"", ""alarm""] },
    ""strike"": { ""subtree"": { ""treeId"": ""tests.p11003.cooldown"", ""interfaceVersion"": 1, ""keys"": { ""ready-at"": ""weapon-ready-at"", ""seen"": ""intruder-seen"" } } },
    ""alarm"": { ""type"": ""aibt.native.set-bool"", ""typeVersion"": 1, ""parameters"": { ""value"": true }, ""bindings"": { ""key"": ""alarm-raised"" } } } }";

        private static IEnumerable<Type> Shards() => new[]
        {
            FindType("AIBT.StandardNodes.StandardNativeShard"),
            FindType("AIBT.Tests.CodeGen.GenerationP7039.ExternalTreeWriteFixtureShard"),
        };

        private static ProjectNodeCatalog Catalog() => ProjectNodeCatalog.Build(Shards(), Array.Empty<IReferenceLeafBehaviorProvider>());

        private static Type FindType(string fullName)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(fullName)).FirstOrDefault(found => found != null);
            Assert.That(type, Is.Not.Null, fullName);
            return type;
        }

        private static ValidationOptions Options() => new ValidationOptions(SourceId, UnreachableNodePolicy.Error, true, true);

        private static ReferenceCompilerOptions CompilerOptions()
            => new ReferenceCompilerOptions(SourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0));

        private DiagnosticCollection Validate(TreeDocument document)
            => TreeValidator.Validate(document, _catalog.Registry, Options(), _catalog.RegisteredTypes);

        private string[] Written(string typeId)
        {
            Assert.That(GeneratedBindingWriteIndex.TryGetWrittenBindings(_catalog.Generated[typeId].Manifest, out var written), Is.True, typeId);
            return written.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }

        private static void AssertSameManifests(AIBT.Authoring.NodeRegistry registry, IEnumerable<GeneratedNodeDescriptor> descriptors, string path)
        {
            Assert.That(registry, Is.Not.Null, path);
            var checkedCount = 0;
            foreach (var descriptor in descriptors.Where(item => item.Bindings.Any(binding => binding.IsBlackboard)))
            {
                Assert.That(registry.TryGet(descriptor.Manifest.TypeId, out var entry), Is.True, path + ": " + descriptor.Manifest.TypeId);
                Assert.That(entry.Manifest, Is.SameAs(descriptor.Manifest), path + ": " + descriptor.Manifest.TypeId);
                checkedCount++;
            }
            Assert.That(checkedCount, Is.GreaterThan(0), path);
        }

        private static string[] Inputs(IEnumerable<Diagnostic> diagnostics)
            => diagnostics.Where(TreeValidator.IsTreeScopeSlotNeverWrittenNote)
                .Select(diagnostic => diagnostic.Location.JsonPointer.Substring("/blackboard/".Length))
                .OrderBy(key => key, StringComparer.Ordinal).ToArray();

        private static string[] McpInputs(JObject response)
            => ((JArray)response["result"]["diagnostics"])
                .Where(item => (string)item["code"] == "AIBT2043" && (string)item["severity"] == "info")
                .Select(item => ((string)item["jsonPointer"]).Substring("/blackboard/".Length))
                .OrderBy(key => key, StringComparer.Ordinal).ToArray();

        private static JObject Dispatch(string assets, string tool, JObject args, string category)
        {
            var request = new JObject { ["tool"] = tool, ["args"] = args, ["grantedCategories"] = new JArray(category) };
            return JObject.Parse(McpToolDispatcher.Dispatch(request.ToString(Newtonsoft.Json.Formatting.None), assets));
        }

        private static string Describe(IEnumerable<Diagnostic> diagnostics)
            => string.Join("\n", diagnostics.Select(diagnostic => diagnostic.Code.Value + " " + diagnostic.Severity + ": " + diagnostic.Message));

        private static TreeDocument Parse(string json, string sourceId = SourceId)
        {
            var read = CanonicalTreeJson.Parse(json, sourceId);
            Assert.That(read.Document, Is.Not.Null, Describe(read.Diagnostics) + "\n" + json);
            return read.Document;
        }

        private static string Tree(string treeId, string root, IEnumerable<string> keys, params string[] nodes)
            => "{ \"format\": \"aibt.tree\", \"formatVersion\": 2, \"treeId\": \"" + treeId + "\", \"name\": \"" + treeId + "\", \"root\": \"" + root + "\", "
               + "\"blackboard\": { " + string.Join(", ", keys) + " }, \"nodes\": { " + string.Join(", ", nodes) + " } }";

        private static string Key(string name, string type, string value)
            => "\"" + name + "\": { \"type\": \"" + type + "\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": " + value + " }";

        private static string Bool(string name) => Key(name, "Bool", "false");
        private static string Int(string name) => Key(name, "Int32", "0");
        private static string Long(string name) => Key(name, "Int64", "0");
        private static string Float(string name) => Key(name, "Float32", "0");

        private static string Leaf(string id, string type, string parameters, string bindings)
            => "\"" + id + "\": { \"type\": \"" + type + "\", \"typeVersion\": 1"
               + (parameters != null ? ", \"parameters\": { " + parameters + " }" : "")
               + (bindings != null ? ", \"bindings\": { " + bindings + " }" : "") + " }";

        private static string Composite(string id, string type, params string[] children)
            => "\"" + id + "\": { \"type\": \"" + type + "\", \"typeVersion\": 1, \"children\": [\"" + string.Join("\", \"", children) + "\"] }";

        private static NodeManifest Manifest(string typeId)
            => new NodeManifest(typeId, 1, "Test node.", "Test", NodeBehaviorKind.Action, "Use in validation tests.", "Do not use in production.",
                NodeExecutionDomain.Burst, true, Array.Empty<NodeParameterContract>(), new NodeChildPolicy(0, 0, true),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), new[] { NodeStatus.Success },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                NodeCancellationMode.AbortOnly, NodeCostHint.Trivial, new[] { new NodeManifestExample("Success", "{}", "Succeeds.") });
    }
}
