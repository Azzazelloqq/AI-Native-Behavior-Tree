using System;
using System.Linq;
using System.Text;
using AIBT.Authoring;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// ADR-P10-013 stage 1: the retaining reactive composites have no native kind yet. The native
    /// path must refuse them explicitly — the generated (native) compiler with an
    /// <see cref="ReferenceCompilerDiagnosticCodes.UnsupportedCapability"/> diagnostic at the node,
    /// and the native machine build for a program that reaches it some other way — instead of
    /// running them as v1 reactive composites or as an unknown leaf.
    /// </summary>
    public sealed class RetainingReactiveNativeRejectionTests
    {
        private static readonly CompiledCompilerVersion CompilerVersion = new CompiledCompilerVersion(1, 0, 0, 0);

        [TestCase(BuiltInNodeManifests.RetainingReactiveSequenceTypeId)]
        [TestCase(BuiltInNodeManifests.RetainingReactiveSelectorTypeId)]
        public void TheGeneratedCompilerRejectsARetainingNodeWithAClearDiagnostic(string typeId)
        {
            var document = Parse(Tree(typeId, 2));
            var result = GeneratedCompiledProgramV2Compiler.Compile(document, Array.Empty<GeneratedNodeDescriptor>(), Options());

            Assert.That(result.Success, Is.False);
            var unsupported = result.Diagnostics.Where(item => item.Code == ReferenceCompilerDiagnosticCodes.UnsupportedCapability).ToArray();
            Assert.That(unsupported, Has.Length.EqualTo(1), string.Join("\n", result.Diagnostics.Select(item => item.Code + ": " + item.Message)));
            Assert.That(unsupported[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(unsupported[0].Location.JsonPointer, Is.EqualTo("/nodes/guarded/type"));
            Assert.That(unsupported[0].Message, Does.Contain(typeId));
            Assert.That(unsupported[0].Message, Does.Contain("not supported by the native executor yet"));
        }

        [Test]
        public void TheGeneratedCompilerStillAcceptsTheSameTreeWithAV1ReactiveComposite()
        {
            var document = Parse(Tree(BuiltInNodeManifests.ReactiveSequenceTypeId, 2));
            var result = GeneratedCompiledProgramV2Compiler.Compile(document, Array.Empty<GeneratedNodeDescriptor>(), Options());

            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(item => item.Code + ": " + item.Message)));
        }

        [TestCase(BuiltInNodeManifests.RetainingReactiveSequenceTypeId)]
        [TestCase(BuiltInNodeManifests.RetainingReactiveSelectorTypeId)]
        public void TheNativeMachineBuildRefusesAProgramWithARetainingNode(string typeId)
        {
            var program = CompileReference(Tree(typeId));

            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var instance, out var failure), Is.False,
                "A retaining node must not be built as a generated leaf or run as a v1 reactive composite.");
            Assert.That(failure.Code, Is.Not.EqualTo(default(NativeRuntimeFailureV1).Code), "The refusal carries a failure code.");
            var kind = NativeHotReloadInstance.ClassifyKind(StableHash.Fnv1A64(typeId));
            Assert.That(kind != NativeLifecycleNodeKindV1.ReactiveSequence && kind != NativeLifecycleNodeKindV1.ReactiveSelector, Is.True,
                "Retaining nodes are never classified as v1 reactive.");
        }

        [Test]
        public void TheNativeMachineBuildStillAcceptsTheSameTreeWithAV1ReactiveComposite()
        {
            var program = CompileReference(Tree(BuiltInNodeManifests.ReactiveSequenceTypeId));

            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var instance, out var failure), Is.True,
                failure.Code.ToString());
            instance.Dispose();
        }

        // root: memory-sequence [guarded: <type> [] ] — an empty child list keeps the tree free of
        // leaves, so only the composite under test decides the outcome.
        private static string Tree(string guardedType, int formatVersion = 1)
            => "{\n  \"format\": \"aibt.tree\",\n  \"formatVersion\": " + formatVersion + ",\n  \"treeId\": \"tree.p10013.native\",\n"
                + "  \"name\": \"Native rejection\",\n  \"root\": \"root\",\n  \"nodes\": {\n"
                + "    \"guarded\": {\n      \"type\": \"" + guardedType + "\",\n      \"typeVersion\": 1\n    },\n"
                + "    \"root\": {\n      \"type\": \"aibt.core.memory-sequence\",\n      \"typeVersion\": 1,\n"
                + "      \"children\": [\n        \"guarded\"\n      ]\n    }\n  }\n}\n";

        private static TreeDocument Parse(string json)
        {
            var read = CanonicalTreeJson.Parse(Encoding.UTF8.GetBytes(json), "p10013-native.aibt.json");
            Assert.That(read.Success, Is.True, string.Join("\n", read.Diagnostics.Select(item => item.Message)));
            return read.Document;
        }

        private static CompiledProgram CompileReference(string json)
        {
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build();
            Assert.That(registry.Success, Is.True);
            var result = ReferenceCompiler.Compile(Parse(json), registry.Registry, Options());
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(item => item.Message)));
            return result.Program;
        }

        private static ReferenceCompilerOptions Options()
            => new ReferenceCompilerOptions("p10013-native.aibt.json", ReferenceCompilationPolicy.Phase1, CompilerVersion);
    }
}
