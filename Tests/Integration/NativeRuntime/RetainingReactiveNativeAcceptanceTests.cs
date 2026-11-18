using System;
using System.Linq;
using System.Text;
using AIBT.Authoring;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// ADR-P10-013 stage 2: the retaining reactive composites have native kinds. The generated
    /// (native) compiler accepts them, the native machine build classifies them as
    /// <see cref="NativeLifecycleNodeKindV1.RetainingReactiveSequence"/> /
    /// <see cref="NativeLifecycleNodeKindV1.RetainingReactiveSelector"/> (never as v1 reactive or
    /// as a generated leaf), and the built program runs. Replaces the stage-1 rejection tests.
    /// </summary>
    public sealed class RetainingReactiveNativeAcceptanceTests
    {
        private static readonly CompiledCompilerVersion CompilerVersion = new CompiledCompilerVersion(1, 0, 0, 0);

        [TestCase(BuiltInNodeManifests.RetainingReactiveSequenceTypeId)]
        [TestCase(BuiltInNodeManifests.RetainingReactiveSelectorTypeId)]
        [TestCase(BuiltInNodeManifests.ReactiveSequenceTypeId)]
        public void TheGeneratedCompilerAcceptsTheTree(string typeId)
        {
            var document = Parse(Tree(typeId, 2));
            var result = GeneratedCompiledProgramV2Compiler.Compile(document, Array.Empty<GeneratedNodeDescriptor>(), Options());

            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(item => item.Code + ": " + item.Message)));
            Assert.That(result.Diagnostics.Any(item => item.Code == ReferenceCompilerDiagnosticCodes.UnsupportedCapability), Is.False);
        }

        [TestCase(BuiltInNodeManifests.RetainingReactiveSequenceTypeId, (byte)NativeLifecycleNodeKindV1.RetainingReactiveSequence, NodeStatus.Success)]
        [TestCase(BuiltInNodeManifests.RetainingReactiveSelectorTypeId, (byte)NativeLifecycleNodeKindV1.RetainingReactiveSelector, NodeStatus.Failure)]
        public void TheNativeMachineBuildsClassifiesAndRunsARetainingNode(
            string typeId, byte expectedKindValue, NodeStatus expectedRoot)
        {
            var expectedKind = (NativeLifecycleNodeKindV1)expectedKindValue;
            var program = CompileReference(Tree(typeId));
            Assert.That(NativeHotReloadInstance.ClassifyKind(StableHash.Fnv1A64(typeId)), Is.EqualTo(expectedKind));

            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var instance, out var failure), Is.True,
                failure.Code.ToString());
            try
            {
                var guarded = program.Nodes.Select((node, index) => (node, index))
                    .Single(item => item.node.NodeTypeId == StableHash.Fnv1A64(typeId)).index;
                Assert.That(instance.Bindings[guarded].Kind, Is.EqualTo(expectedKind));

                // An empty retaining sequence succeeds, an empty retaining selector fails, exactly
                // as their v1 counterparts; the memory-sequence root passes the result up.
                Assert.That(instance.Machine.TryBeginUpdate(1, 0, out failure), Is.True, failure.Code.ToString());
                NativeLifecycleStepResultV1 step = default;
                for (var guard = 0; guard < 32; guard++)
                {
                    Assert.That(instance.Machine.TryAdvance(out step, out failure), Is.True, failure.Code.ToString());
                    Assert.That(step.Kind, Is.Not.EqualTo(NativeLifecycleStepKindV1.DispatchRequired), "The tree has no leaves.");
                    if (step.Kind == NativeLifecycleStepKindV1.Completed || step.Kind == NativeLifecycleStepKindV1.Waiting) break;
                }
                Assert.That(step.Kind, Is.EqualTo(NativeLifecycleStepKindV1.Completed));
                Assert.That(step.RootStatus, Is.EqualTo(expectedRoot));
            }
            finally
            {
                instance.Dispose();
            }
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
                + "  \"name\": \"Native acceptance\",\n  \"root\": \"root\",\n  \"nodes\": {\n"
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
