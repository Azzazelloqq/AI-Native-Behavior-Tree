using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-003: the note <c>AIBT2043</c> ("Input key ...: no node in this tree writes it") agrees with
    /// what a native host accepts. For a tree prepared for a <see cref="ProductionTreeHost"/>, the
    /// Tree-scope keys without the note are exactly the keys
    /// <see cref="ProductionTreeHost.TryResolveExternalTreeWrite{T}"/> refuses (a node writes them),
    /// and the keys with the note are exactly the keys it accepts (game code feeds them).
    /// </summary>
    public sealed class NativeBindingInputNoteHostRuleTests
    {
        private static readonly string Tree = LibraryTrees.Tree("tests.p11003.host-rule", "root",
            new[]
            {
                Bool("flag"), Bool("fed"), Int("count"), Long("ready-at"), Bool("source"), Bool("target"),
                Int("owned"), Float("live"), Bool("unused"),
            },
            Composite("root", "aibt.core.memory-sequence", "check", "set", "count-up", "ready", "cool", "copy", "write", "read"),
            Leaf("check", "aibt.native.bool-is-true", bindings: "\"key\": \"fed\""),                               // Read
            Leaf("set", "aibt.native.set-bool", "\"value\": true", "\"key\": \"flag\""),                            // Write
            Leaf("count-up", "aibt.native.increment-int", "\"amount\": 1", "\"key\": \"count\""),                   // ReadWrite
            Leaf("ready", "aibt.native.deadline-passed", bindings: "\"key\": \"ready-at\""),                       // Read ...
            Leaf("cool", "aibt.native.set-deadline", "\"duration-microseconds\": 1000", "\"key\": \"ready-at\""),   // ... and Write
            Leaf("copy", "aibt.native.copy-bool", bindings: "\"from\": \"source\", \"to\": \"target\""),             // Read + Write
            Leaf("write", "aibt.tests.external-tree-write-writer", "\"value\": 3", "\"owned-target\": \"owned\""),  // project node, Write
            Leaf("read", "aibt.tests.external-tree-write-reader", bindings: "\"live-target\": \"live\""));         // project node, Read

        [Test]
        public void KeysWithoutTheNote_AreExactlyTheKeysTheHostRefusesForAnExternalWrite()
        {
            var compiled = NativeLibraryKit.CompileResult(Tree);
            Assert.That(compiled.Success, Is.True, string.Join("\n", compiled.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var noted = Notes(compiled.Diagnostics);

            // The validator alone, on the registry the generated compiler builds, says the same.
            var nodes = NativeLibraryKit.Materialize(typeof(BuiltInLeafShard)).Nodes
                .Concat(NativeLibraryKit.Materialize(typeof(StandardNativeShard)).Nodes)
                .Concat(NativeLibraryKit.Materialize(typeof(ExternalTreeWriteFixtureShard)).Nodes).ToArray();
            var document = CanonicalTreeJson.Parse(Tree, "Tests/P11003/host-rule.aibt.json").Document;
            var validated = TreeValidator.Validate(document, GeneratedNodeRegistry.Build(nodes).Registry,
                new ValidationOptions("Tests/P11003/host-rule.aibt.json", UnreachableNodePolicy.Error, true, true));
            Assert.That(Notes(validated), Is.EqualTo(noted));

            using (var kit = new NativeLibraryKit())
            {
                var host = kit.Start(kit.Prepare(Tree)).Host;
                var refused = new SortedSet<string>(StringComparer.Ordinal);
                var accepted = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var key in document.Blackboard.Where(key => key.Scope == BlackboardScope.Tree))
                    (Resolves(host, key) ? accepted : refused).Add(key.Id);

                Assert.That(refused, Is.EqualTo(new[] { "count", "flag", "owned", "ready-at", "target" }));
                Assert.That(accepted, Is.EqualTo(noted), "The note marks exactly the keys game code may set.");
                Assert.That(refused.Concat(noted).OrderBy(id => id, StringComparer.Ordinal),
                    Is.EqualTo(document.Blackboard.Select(key => key.Id).OrderBy(id => id, StringComparer.Ordinal)));
            }
        }

        private static bool Resolves(ProductionTreeHost host, BlackboardKeyDefinition key)
        {
            switch (key.Type.ValueType)
            {
                case BlackboardValueType.Bool: return host.TryResolveExternalTreeWrite<bool>(key.Id, NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Bool), out _, out _);
                case BlackboardValueType.Int32: return host.TryResolveExternalTreeWrite<int>(key.Id, NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32), out _, out _);
                case BlackboardValueType.Int64: return host.TryResolveExternalTreeWrite<long>(key.Id, NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int64), out _, out _);
                case BlackboardValueType.Float32: return host.TryResolveExternalTreeWrite<float>(key.Id, NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32), out _, out _);
                default: throw new ArgumentOutOfRangeException(nameof(key), key.Type.ValueType, "Not used by this tree.");
            }
        }

        private static string[] Notes(IEnumerable<Diagnostic> diagnostics)
            => diagnostics.Where(TreeValidator.IsTreeScopeSlotNeverWrittenNote)
                .Select(diagnostic => diagnostic.Location.JsonPointer.Substring("/blackboard/".Length))
                .OrderBy(key => key, StringComparer.Ordinal).ToArray();
    }
}
