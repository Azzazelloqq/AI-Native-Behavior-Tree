using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// ADR-P11-001 stage 2: subtrees on the generated (native) path. The generated V2 compiler expands
    /// references through a resolver; a native node inside a subtree binds to an instance's private
    /// key (<c>a:armed</c>) and to an interface key given a constant (<c>b:gate</c>), which owner
    /// decision 9 allows; a <see cref="NativeTreeAgent"/> runs a subtree used twice; and the program's
    /// node count and instance memory grow linearly with the number of uses (no timing).
    /// </summary>
    public sealed class SubtreeNativeTests
    {
        private const string ParentSource = "Tests/P11/parent.aibt";
        private const string ChildSource = "Tests/P11/child.aibt";
        private const string ChildTreeId = "tests.p11.native.child";

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private readonly List<NativeTreeAgent> _agents = new List<NativeTreeAgent>();
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;
        private int _baselinePrograms;

        [SetUp]
        public void SetUp()
        {
            _baselinePrograms = NativeTreeProgramCache.LiveProgramCount;
            var worldObject = Track(new GameObject("AIBT.Tests.P11.World"));
            _scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            _world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(_world, "Awake");
            InvokePrivate(_world, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var agent in _agents) if (agent != null) agent.Deactivate();
            _agents.Clear();
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) UnityEngine.Object.DestroyImmediate(_created[index]);
            _created.Clear();
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms), "A test leaked a shared program.");
        }

        // ------------------------------------------------------------------ compiler

        [Test]
        public void ResolverOverload_ExpandsBothInstances_AndBindsNativeNodesToRenamedKeys()
        {
            var artifact = Artifact();
            var result = Compile(Parse(ParentJson(), ParentSource), artifact, Resolver(ChildJson()));
            Assert.That(result.Success, Is.True, Describe(result.Diagnostics));
            var program = result.Program;

            var debug = program.SemanticProgram.DebugMap.ToDictionary(entry => entry.AuthoringNodeId.Value);
            Assert.That(debug.Keys.OrderBy(id => id, StringComparer.Ordinal),
                Is.EqualTo(new[] { "a:check", "a:emit", "a:root", "b:check", "b:emit", "b:root", "root" }));
            Assert.That(debug["root"].SourcePath, Is.EqualTo(ParentSource));
            foreach (var id in new[] { "a:root", "a:check", "a:emit", "b:root", "b:check", "b:emit" })
                Assert.That(debug[id].SourcePath, Is.EqualTo(ChildSource), id + " keeps its subtree file as its source.");

            Assert.That(BoundKey(program, debug["a:check"].RuntimeNodeIndex), Is.EqualTo("a:armed"), "Private key of instance a.");
            Assert.That(BoundKey(program, debug["b:check"].RuntimeNodeIndex), Is.EqualTo("b:armed"), "Private key of instance b.");
            Assert.That(BoundKey(program, debug["a:emit"].RuntimeNodeIndex), Is.EqualTo("live-target"), "Interface key bound to a parent key.");
            Assert.That(BoundKey(program, debug["b:emit"].RuntimeNodeIndex), Is.EqualTo("b:gate"), "Interface key given a constant.");
            var gate = program.Slots.Single(slot => slot.Slot.Key.Id == "b:gate").Slot;
            Assert.That(BitConverter.ToSingle(gate.DefaultBytes, 0), Is.EqualTo(1f), "The constant is the private key's default.");
        }

        [Test]
        public void ResolverOverload_EqualsCompilingTheExpansion_AndTheSerializedExpansionHasTheSameSemantics()
        {
            var artifact = Artifact();
            var parent = Parse(ParentJson(), ParentSource);
            var viaResolver = Compile(parent, artifact, Resolver(ChildJson()));
            Assert.That(viaResolver.Success, Is.True, Describe(viaResolver.Diagnostics));

            var registry = GeneratedNodeRegistry.Build(artifact.Nodes, includeBuiltIns: true);
            Assert.That(registry.Success, Is.True);
            var expansion = SubtreeExpander.Expand(parent, Resolver(ChildJson()), registry.Registry,
                new ValidationOptions(ParentSource, supportsAgentScope: true, supportsSharedScope: true), artifact.RegisteredTypes);
            Assert.That(expansion.Success, Is.True, Describe(expansion.Diagnostics));
            var viaExpansion = GeneratedCompiledProgramV2Compiler.Compile(expansion, artifact.Nodes, artifact.RegisteredTypes, Options(ParentSource));
            Assert.That(viaExpansion.Success, Is.True, Describe(viaExpansion.Diagnostics));
            Assert.That(viaExpansion.Program.ContentHash, Is.EqualTo(viaResolver.Program.ContentHash));

            // The expanded document saved as a format-2 tree (what the importer stores) compiles through
            // the overload without a resolver to the same semantics; only its debug-map source paths
            // (all the parent's) differ.
            var saved = CanonicalTreeJson.Parse(CanonicalTreeJson.Serialize(expansion.Document).Utf8, ParentSource);
            Assert.That(saved.Success, Is.True, Describe(saved.Diagnostics));
            Assert.That(saved.Document.FormatVersion, Is.EqualTo(2));
            var flat = GeneratedCompiledProgramV2Compiler.Compile(saved.Document, artifact.Nodes, artifact.RegisteredTypes, Options(ParentSource));
            Assert.That(flat.Success, Is.True, Describe(flat.Diagnostics));
            Assert.That(flat.Program.SemanticProgram.Header.CanonicalSemanticHash,
                Is.EqualTo(viaResolver.Program.SemanticProgram.Header.CanonicalSemanticHash));
            Assert.That(flat.Program.GetConfigBlobCopy(), Is.EqualTo(viaResolver.Program.GetConfigBlobCopy()));
            Assert.That(flat.Program.GetDefaultValueBlobCopy(), Is.EqualTo(viaResolver.Program.GetDefaultValueBlobCopy()));
            Assert.That(flat.Program.SemanticProgram.DebugMap.All(entry => entry.SourcePath == ParentSource), Is.True);
        }

        [Test]
        public void OverloadWithoutResolver_ReportsUnresolvedReferences()
        {
            var artifact = Artifact();
            var result = GeneratedCompiledProgramV2Compiler.Compile(Parse(ParentJson(), ParentSource), artifact.Nodes, artifact.RegisteredTypes, Options(ParentSource));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Code.Value).Distinct(),
                Is.EqualTo(new[] { "AIBT3020" }));
        }

        [Test]
        public void TreeWithoutSubtrees_CompilesIdenticallyThroughEitherOverload()
        {
            var artifact = Artifact();
            var document = Parse(Q(@"{ 'format': 'aibt.tree', 'formatVersion': 2, 'treeId': 'tests.p11.native.plain', 'name': 'plain', 'root': 'emit',
  'blackboard': { 'live-target': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 } },
  'nodes': { 'emit': { 'type': 'aibt.tests.external-tree-effect', 'typeVersion': 1, 'parameters': { 'value': 17 }, 'bindings': { 'live-target': 'live-target' } } } }"), ParentSource);
            var plain = GeneratedCompiledProgramV2Compiler.Compile(document, artifact.Nodes, artifact.RegisteredTypes, Options(ParentSource));
            var resolved = Compile(document, artifact, Resolver(ChildJson()));

            Assert.That(plain.Success && resolved.Success, Is.True);
            Assert.That(resolved.Program.GetBytesCopy(), Is.EqualTo(plain.Program.GetBytesCopy()));
        }

        // ------------------------------------------------------------------ runtime

        [Test]
        public void NativeTreeAgent_RunsASubtreeUsedTwice_WithPrivateAndConstantKeys()
        {
            var agent = Spawn(PrepareAsset(ParentJson(), Resolver(ChildJson())));
            var hits = new List<int>();
            agent.OnEffect("hit", (int value) => hits.Add(value));

            Frames(3);
            Assert.That(agent.LastError, Is.Null);
            Assert.That(hits, Is.EqualTo(new[] { 17, 17, 17 }),
                "Only instance b emits: its gate is the constant 1, instance a's gate is the parent's live-target (0).");

            agent.SetFact("live-target", 1f);
            hits.Clear();
            Frames(3);
            Assert.That(hits, Has.Count.EqualTo(6), "Both instances emit once the parent key is positive.");
            Assert.That(agent.IsRunning, Is.True);
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
        }

        [Test]
        public void NativeTreeAgent_PrivateKeysBelongToEachInstance()
        {
            // Instance a's private 'armed' is 2 (default) and the check passes; with 'armed' 0 in the
            // subtree every instance's check fails, so the parallel root fails at once.
            var disarmed = Spawn(PrepareAsset(ParentJson(), Resolver(ChildJson(armed: "0.0"))));
            Frames(1);
            Assert.That(disarmed.LastResult, Is.EqualTo(NodeStatus.Failure));

            var armed = Spawn(PrepareAsset(ParentJson(), Resolver(ChildJson())));
            Frames(1);
            Assert.That(armed.IsRunning, Is.True, "Both checks pass and both emitters keep running.");
        }

        [Test]
        public void SubtreeUsedOnceAndSixteenTimes_ProgramGrowsLinearly()
        {
            var artifact = Artifact();
            var one = Shape(artifact, 1);
            var two = Shape(artifact, 2);
            var sixteen = Shape(artifact, 16);

            // Deterministic numbers only (ADR-P11-001 risk "same subtree x16"); no timing.
            Debug.Log("P11-001 subtree scale | uses=1 " + one + " | uses=2 " + two + " | uses=16 " + sixteen);
            TestContext.Out.WriteLine("uses=1 " + one);
            TestContext.Out.WriteLine("uses=16 " + sixteen);

            Assert.That(one.Nodes, Is.EqualTo(1 + 3));
            Assert.That(sixteen.Nodes, Is.EqualTo(1 + 16 * 3), "One root plus three nodes per use.");
            Assert.That(sixteen.Slots, Is.EqualTo(1 + 16), "The parent's key plus one private key per use.");
            Assert.That(sixteen.Memory - one.Memory, Is.EqualTo(15 * (two.Memory - one.Memory)), "Instance memory is linear in the uses.");
            Assert.That(sixteen.Defaults - one.Defaults, Is.EqualTo(15 * (two.Defaults - one.Defaults)), "Default values are linear in the uses.");
        }

        // ------------------------------------------------------------------ trees

        private static string ChildJson(string armed = "2.0")
            => Q(@"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': '" + ChildTreeId + @"', 'name': 'P11 native child', 'root': 'root',
  'interface': { 'version': 1, 'keys': ['gate'] },
  'blackboard': {
    'gate': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 },
    'armed': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': " + armed + @" } },
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['check', 'emit'] },
    'check': { 'type': 'aibt.tests.external-tree-write-reader', 'typeVersion': 1, 'bindings': { 'live-target': 'armed' } },
    'emit': { 'type': 'aibt.tests.external-tree-effect', 'typeVersion': 1, 'parameters': { 'value': 17 }, 'bindings': { 'live-target': 'gate' } } } }");

        private static string ParentJson()
            => Q(@"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11.native.parent', 'name': 'P11 native parent', 'root': 'root',
  'blackboard': { 'live-target': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 } },
  'nodes': {
    'root': { 'type': 'aibt.core.parallel', 'typeVersion': 1, 'parameters': { 'policy': 'require-all-success' }, 'children': ['a', 'b'] },
    'a': { 'subtree': { 'treeId': '" + ChildTreeId + @"', 'interfaceVersion': 1, 'keys': { 'gate': 'live-target' } } },
    'b': { 'subtree': { 'treeId': '" + ChildTreeId + @"', 'interfaceVersion': 1, 'keys': { 'gate': { 'value': 1.0 } } } } } }");

        private static string ManyUsesJson(int uses)
        {
            var ids = Enumerable.Range(0, uses).Select(index => "u" + index).ToArray();
            return Q(@"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11.native.many', 'name': 'P11 many uses', 'root': 'root',
  'blackboard': { 'live-target': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 } },
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': [" + string.Join(", ", ids.Select(id => "'" + id + "'")) + @"] },
    " + string.Join(",\n    ", ids.Select(id => "'" + id + "': { 'subtree': { 'treeId': '" + ChildTreeId + "', 'interfaceVersion': 1, 'keys': { 'gate': 'live-target' } } }")) + @" } }");
        }

        // ------------------------------------------------------------------ helpers

        private readonly struct ProgramShape
        {
            internal ProgramShape(uint nodes, uint slots, uint memory, int defaults)
            {
                Nodes = nodes; Slots = slots; Memory = memory; Defaults = defaults;
            }

            internal uint Nodes { get; }
            internal uint Slots { get; }
            internal uint Memory { get; }
            internal int Defaults { get; }

            public override string ToString()
                => "nodes=" + Nodes + " blackboardSlots=" + Slots + " instanceNodeMemoryBytes=" + Memory + " defaultValueBytes=" + Defaults;
        }

        private static ProgramShape Shape(GeneratedShardMetadataArtifact artifact, int uses)
        {
            var result = Compile(Parse(ManyUsesJson(uses), ParentSource), artifact, Resolver(ChildJson()));
            Assert.That(result.Success, Is.True, Describe(result.Diagnostics));
            var header = result.Program.SemanticProgram.Header;
            return new ProgramShape(header.NodeCount, header.BlackboardSlotCount, header.InstanceNodeMemorySize,
                result.Program.GetDefaultValueBlobCopy().Length);
        }

        private static string BoundKey(GeneratedCompiledProgramV2 program, uint nodeIndex)
        {
            var access = program.Accesses.Single(item => item.NodeIndex == nodeIndex);
            return program.Slots.Single(slot => slot.Slot.SlotIndex == access.SlotIndex && slot.Slot.Key.Scope == access.Scope).Slot.Key.Id;
        }

        private static GeneratedShardMetadataArtifact Artifact()
            => GeneratedShardMetadataMaterializer.MaterializeArtifact(
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ShardId,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ShardVersion,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.CanonicalDescriptorJson,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.DescriptorHash,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ManifestRegistryJson,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.NodeRegistryHash);

        private static ReferenceCompilerOptions Options(string sourceId)
            => new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6));

        private static GeneratedCompiledProgramV2Result Compile(TreeDocument document, GeneratedShardMetadataArtifact artifact, ISubtreeResolver resolver)
            => GeneratedCompiledProgramV2Compiler.Compile(document, artifact.Nodes, artifact.RegisteredTypes, Options(ParentSource), resolver);

        private static InMemorySubtreeResolver Resolver(string childJson)
        {
            var resolver = new InMemorySubtreeResolver();
            resolver.Add(Parse(childJson, ChildSource), ChildSource);
            return resolver;
        }

        private static TreeDocument Parse(string json, string sourceId)
        {
            var parsed = CanonicalTreeJson.Parse(json, sourceId);
            Assert.That(parsed.Success, Is.True, Describe(parsed.Diagnostics) + "\n" + json);
            return parsed.Document;
        }

        private static string Q(string text) => text.Replace('\'', '"');

        private static string Describe(DiagnosticCollection diagnostics)
            => string.Join("\n", diagnostics.Select(d => d.Code.Value + " " + d.Location.DocumentId + d.Location.JsonPointer + ": " + d.Message));

        private ProductionTreeAsset PrepareAsset(string parentJson, ISubtreeResolver resolver)
        {
            var artifact = Artifact();
            var result = Compile(Parse(parentJson, ParentSource), artifact, resolver);
            Assert.That(result.Success, Is.True, Describe(result.Diagnostics));
            var asset = ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(artifact.RegisteredTypes),
                ExternalTreeWriteFixtureCatalog.Fingerprint);
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(ExternalTreeWriteFixtureCatalog).FullName + ", " + typeof(ExternalTreeWriteFixtureCatalog).Assembly.GetName().Name);
            return Track(asset);
        }

        private NativeTreeAgent Spawn(ProductionTreeAsset asset)
        {
            var gameObject = Track(new GameObject("AIBT.Tests.P11.Agent" + _agents.Count));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = asset;
            agent.World = _world;
            agent.RestartMode = NativeTreeRestartMode.Never;
            _agents.Add(agent);
            agent.Activate();
            return agent;
        }

        private void Frames(int count)
        {
            for (var frame = 0; frame < count; frame++)
            {
                InvokePrivate(_scheduler, "Update");
                foreach (var agent in _agents) if (agent != null) agent.ProcessRootResult();
            }
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
