using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;
using AuthoringNodeRegistry = AIBT.Authoring.NodeRegistry;

namespace AIBT.Tests.Editor.Subtrees
{
    /// <summary>
    /// ADR-P11-001 test helpers: tree JSON written with single quotes (turned into double quotes),
    /// a node registry of the built-ins, the reference test fixtures and two fixed-name key leaves,
    /// and shortcuts for expansion and compilation. Plain C#, so the dotnet harness compiles it.
    /// </summary>
    internal static class SubtreeTestKit
    {
        internal const string ReadsFlagTypeId = "tests.p11.reads-flag";
        internal const string Success = ReferenceFixtureNodeManifests.SuccessTypeId;
        internal const string Failure = ReferenceFixtureNodeManifests.FailureTypeId;
        internal const string Running = ReferenceFixtureNodeManifests.RunningTypeId;
        internal const string Sequence = BuiltInNodeManifests.MemorySequenceTypeId;
        internal const string ReactiveSelector = BuiltInNodeManifests.ReactiveSelectorTypeId;
        internal const string Repeater = BuiltInNodeManifests.RepeaterTypeId;

        private static AuthoringNodeRegistry s_registry;

        internal static AuthoringNodeRegistry Registry
        {
            get
            {
                if (s_registry != null) return s_registry;
                var build = NodeRegistryBuilder.CreateWithBuiltIns()
                    .AddTestFixtures()
                    .AddUserExtension(FixedNameLeaf(ReadsFlagTypeId, new[] { "flag" }))
                    .Build();
                Assert.That(build.Success, Is.True, Describe(build.Diagnostics));
                s_registry = build.Registry;
                return s_registry;
            }
        }

        /// <summary>A path inside the AIBT package (the dotnet harness sets AIBT_PACKAGE_ROOT).</summary>
        internal static string PackagePath(params string[] segments)
        {
#if UNITY_EDITOR
            return EditorTestPackagePaths.Resolve(segments);
#else
            var path = Environment.GetEnvironmentVariable("AIBT_PACKAGE_ROOT");
            Assert.That(path, Is.Not.Null.And.Not.Empty, "Set AIBT_PACKAGE_ROOT to the package directory.");
            for (var index = 0; index < segments.Length; index++) path = System.IO.Path.Combine(path, segments[index]);
            return path;
#endif
        }

        internal static string Q(string text) => text.Replace('\'', '"');

        internal static string Tree(
            string treeId,
            string root,
            string nodes,
            string blackboard = null,
            string subtreeInterface = null,
            int formatVersion = 3,
            string contracts = null)
            => Q("{ 'format': 'aibt.tree', 'formatVersion': " + formatVersion + ", 'treeId': '" + treeId + "', 'name': '" + treeId
                + "', 'root': '" + root + "'"
                + (subtreeInterface == null ? string.Empty : ", 'interface': " + subtreeInterface)
                + (contracts == null ? string.Empty : ", 'blackboardContracts': " + contracts)
                + (blackboard == null ? string.Empty : ", 'blackboard': { " + blackboard + " }")
                + ", 'nodes': { " + nodes + " } }");

        internal static string Interface(uint version, params string[] keys)
            => "{ 'version': " + version + ", 'keys': [" + string.Join(", ", keys.Select(key => "'" + key + "'")) + "] }";

        internal static string Leaf(string id, string type = Success, string members = null)
            => "'" + id + "': { 'type': '" + type + "', 'typeVersion': 1" + (members == null ? string.Empty : ", " + members) + " }";

        internal static string Composite(string id, string type, params string[] children)
            => "'" + id + "': { 'type': '" + type + "', 'typeVersion': 1, 'children': [" + string.Join(", ", children.Select(child => "'" + child + "'")) + "] }";

        internal static string Seq(string id, params string[] children) => Composite(id, Sequence, children);

        internal static string NodeList(params string[] nodes) => string.Join(", ", nodes);

        internal static string Ref(string id, string treeId, uint interfaceVersion = 1, string keys = "", string displayName = null)
            => "'" + id + "': { " + (displayName == null ? string.Empty : "'displayName': '" + displayName + "', ")
                + "'subtree': { 'treeId': '" + treeId + "', 'interfaceVersion': " + interfaceVersion + ", 'keys': { " + keys + " } } }";

        internal static string Bind(string interfaceKey, string parentKey) => "'" + interfaceKey + "': '" + parentKey + "'";

        internal static string Constant(string interfaceKey, string valueJson) => "'" + interfaceKey + "': { 'value': " + valueJson + " }";

        internal static string BoolKey(string id, bool value = false)
            => "'" + id + "': { 'type': 'Bool', 'default': " + (value ? "true" : "false") + " }";

        internal static string IntKey(string id, int value = 0)
            => "'" + id + "': { 'type': 'Int32', 'default': " + value + " }";

        internal static string Keys(params string[] keys) => string.Join(", ", keys);

        internal static TreeDocument Parse(string json, string sourceId = "trees/parent.aibt")
        {
            var read = CanonicalTreeJson.Parse(json, sourceId);
            Assert.That(read.Success, Is.True, Describe(read.Diagnostics) + "\n" + json);
            return read.Document;
        }

        /// <summary>An in-memory resolver of (source ID, JSON) pairs.</summary>
        internal static InMemorySubtreeResolver Resolver(params string[] sourceIdAndJson)
        {
            var resolver = new InMemorySubtreeResolver();
            for (var index = 0; index < sourceIdAndJson.Length; index += 2)
                resolver.Add(Parse(sourceIdAndJson[index + 1], sourceIdAndJson[index]), sourceIdAndJson[index]);
            return resolver;
        }

        internal static SubtreeExpansion Expand(
            TreeDocument document,
            ISubtreeResolver resolver,
            string sourceId = "trees/parent.aibt",
            AuthoringNodeRegistry registry = null)
            => SubtreeExpander.Expand(
                document,
                resolver,
                registry ?? Registry,
                ReferenceCompilationPolicy.Phase1.CreateValidationOptions(sourceId));

        internal static SubtreeExpansion ExpandOk(TreeDocument document, ISubtreeResolver resolver, string sourceId = "trees/parent.aibt")
        {
            var expansion = Expand(document, resolver, sourceId);
            Assert.That(expansion.Success, Is.True, Describe(expansion.Diagnostics));
            return expansion;
        }

        internal static Diagnostic SingleError(SubtreeExpansion expansion, DiagnosticCode code)
        {
            Assert.That(expansion.Success, Is.False, "The expansion was expected to fail with " + code.Value + ".");
            Assert.That(expansion.Document, Is.Null);
            var matches = expansion.Diagnostics.Where(item => item.Code.Equals(code)).ToList();
            Assert.That(matches, Has.Count.GreaterThanOrEqualTo(1), Describe(expansion.Diagnostics));
            Assert.That(expansion.Diagnostics.All(item => item.Severity == DiagnosticSeverity.Error), Is.True);
            return matches[0];
        }

        internal static ReferenceCompilerOptions Options(string sourceId = "trees/parent.aibt")
            => new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 1));

        internal static CompiledProgram CompileOk(TreeDocument document, ISubtreeResolver resolver, string sourceId = "trees/parent.aibt")
        {
            var result = ReferenceCompiler.Compile(document, Registry, Options(sourceId), resolver);
            Assert.That(result.Success, Is.True, Describe(result.Diagnostics));
            return result.Program;
        }

        internal static string Describe(DiagnosticCollection diagnostics)
            => string.Join(" | ", diagnostics.Select(item => item.Code.Value + " " + item.Location.DocumentId + item.Location.JsonPointer + ": " + item.Message));

        internal static NodeDocument NodeById(TreeDocument document, string id)
        {
            var node = document.Nodes.FirstOrDefault(item => item.Id.Value == id);
            Assert.That(node, Is.Not.Null, "No node '" + id + "' in " + string.Join(", ", document.Nodes.Select(item => item.Id.Value)));
            return node;
        }

        internal static BlackboardKeyDefinition KeyById(TreeDocument document, string id)
        {
            var key = document.Blackboard.FirstOrDefault(item => item.Id == id);
            Assert.That(key, Is.Not.Null, "No key '" + id + "' in " + string.Join(", ", document.Blackboard.Select(item => item.Id)));
            return key;
        }

        internal static IReadOnlyList<string> NodeIds(TreeDocument document)
            => document.Nodes.Select(item => item.Id.Value).OrderBy(item => item, StringComparer.Ordinal).ToList();

        internal static IReadOnlyList<string> KeyIds(TreeDocument document)
            => document.Blackboard.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal).ToList();

        private static NodeManifest FixedNameLeaf(string typeId, IEnumerable<string> reads)
            => new NodeManifest(
                typeId,
                1,
                "P11-001 fixed-name key leaf.",
                "Subtree tests",
                NodeBehaviorKind.Condition,
                "Use only in subtree tests.",
                "Do not ship.",
                NodeExecutionDomain.Burst,
                true,
                Array.Empty<NodeParameterContract>(),
                new NodeChildPolicy(0, 0, true),
                reads,
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Failure },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                NodeCancellationMode.NotApplicable,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Fixture", "{}", "Reads a fixed key.") });
    }
}
