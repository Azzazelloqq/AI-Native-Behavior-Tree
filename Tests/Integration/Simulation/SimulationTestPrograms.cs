using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.Simulation;
using AIBT.StandardNodes;
using NUnit.Framework;

namespace AIBT.Tests.Integration.Simulation
{
    // Built-in tick leaves (wait, random condition) and the package's standard native nodes (float
    // keys, microsecond wait) in one project catalog for the simulation container tests.
    [AibtCatalogSet("aibt.tests.p10009.simulation", 1u, typeof(BuiltInLeafShard), typeof(StandardNativeShard))]
    public static partial class SimulationTestCatalog { }

    /// <summary>P10-009: trees, program creation and the per-tick digest shared by the container tests.</summary>
    internal static class SimulationTestPrograms
    {
        internal const uint TickMicroseconds = 33333u;

        // Never finishes. Each iteration holds at least one update (wait 2 ticks), then a reactive selector prefers the "hot"
        // branch while score >= 2 (wait 100 ms, cool down) over the "work" branch (a cooldown-gated
        // wait, a 40% random roll, raise the score). Reactive preemption aborts "work" when it raises.
        internal const string PatrolTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10009.patrol"", ""name"": ""P10-009 patrol"",
  ""root"": ""loop"",
  ""blackboard"": {
    ""score"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""cycle""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""cycle"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""hold"", ""top""] },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""top"": { ""type"": ""aibt.core.reactive-selector"", ""typeVersion"": 1, ""children"": [""hot"", ""work""] },
    ""hot"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""hot-check"", ""hot-wait"", ""cool""] },
    ""hot-check"": { ""type"": ""aibt.native.float-at-least"", ""typeVersion"": 1, ""parameters"": { ""threshold"": 2.0 }, ""bindings"": { ""key"": ""score"" } },
    ""hot-wait"": { ""type"": ""aibt.native.wait-microseconds"", ""typeVersion"": 1, ""parameters"": { ""duration-microseconds"": 100000 } },
    ""cool"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 0.0 }, ""bindings"": { ""key"": ""score"" } },
    ""work"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""gate"", ""roll"", ""raise""] },
    ""gate"": { ""type"": ""aibt.core.cooldown"", ""typeVersion"": 1, ""children"": [""gate-wait""], ""parameters"": { ""durationMicroseconds"": 200000, ""blockedResult"": ""failure"", ""startPolicy"": ""on-enter"" } },
    ""gate-wait"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""roll"": { ""type"": ""aibt.stdlib.random-condition"", ""typeVersion"": 1, ""parameters"": { ""success-chance-percent"": 40 } },
    ""raise"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 2.5 }, ""bindings"": { ""key"": ""score"" } }
  }
}";

        // Finishes every few thinks, with Success or Failure decided by a 70% random roll; under the
        // default RootCompletionPolicy.Restart it restarts as a fresh instance.
        internal const string ErrandTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10009.errand"", ""name"": ""P10-009 errand"",
  ""root"": ""root"",
  ""blackboard"": {
    ""progress"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""go"", ""roll"", ""mark"", ""check""] },
    ""go"": { ""type"": ""aibt.native.wait-microseconds"", ""typeVersion"": 1, ""parameters"": { ""duration-microseconds"": 133332 } },
    ""roll"": { ""type"": ""aibt.stdlib.random-condition"", ""typeVersion"": 1, ""parameters"": { ""success-chance-percent"": 70 } },
    ""mark"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 1.0 }, ""bindings"": { ""key"": ""progress"" } },
    ""check"": { ""type"": ""aibt.native.float-at-least"", ""typeVersion"": 1, ""parameters"": { ""threshold"": 0.5 }, ""bindings"": { ""key"": ""progress"" } }
  }
}";

        // Deterministic (no random node; written when hosts could not run random-capable generated
        // nodes, which they can since P11-002 stage 3): never finishes; cooldown, reactive selector,
        // waits and writes.
        internal const string DeterministicLoopTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10009.det-loop"", ""name"": ""P10-009 deterministic loop"",
  ""root"": ""loop"",
  ""blackboard"": {
    ""score"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""cycle""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""cycle"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""hold"", ""top""] },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""top"": { ""type"": ""aibt.core.reactive-selector"", ""typeVersion"": 1, ""children"": [""hot"", ""work""] },
    ""hot"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""hot-check"", ""hot-wait"", ""cool""] },
    ""hot-check"": { ""type"": ""aibt.native.float-at-least"", ""typeVersion"": 1, ""parameters"": { ""threshold"": 2.0 }, ""bindings"": { ""key"": ""score"" } },
    ""hot-wait"": { ""type"": ""aibt.native.wait-microseconds"", ""typeVersion"": 1, ""parameters"": { ""duration-microseconds"": 100000 } },
    ""cool"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 0.0 }, ""bindings"": { ""key"": ""score"" } },
    ""work"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""gate"", ""raise""] },
    ""gate"": { ""type"": ""aibt.core.cooldown"", ""typeVersion"": 1, ""children"": [""gate-wait""], ""parameters"": { ""durationMicroseconds"": 200000, ""blockedResult"": ""failure"", ""startPolicy"": ""on-enter"" } },
    ""gate-wait"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""raise"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 2.5 }, ""bindings"": { ""key"": ""score"" } }
  }
}";

        // Deterministic and finishes once (Success) after a few thinks.
        internal const string DeterministicErrandTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10009.det-errand"", ""name"": ""P10-009 deterministic errand"",
  ""root"": ""root"",
  ""blackboard"": {
    ""progress"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""go"", ""mark"", ""again"", ""check""] },
    ""go"": { ""type"": ""aibt.native.wait-microseconds"", ""typeVersion"": 1, ""parameters"": { ""duration-microseconds"": 133332 } },
    ""mark"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 1.0 }, ""bindings"": { ""key"": ""progress"" } },
    ""again"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""check"": { ""type"": ""aibt.native.float-at-least"", ""typeVersion"": 1, ""parameters"": { ""threshold"": 0.5 }, ""bindings"": { ""key"": ""progress"" } }
  }
}";

        // One random roll at the root: Success or Failure in the first think.
        internal const string CoinTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10009.coin"", ""name"": ""P10-009 coin"",
  ""root"": ""roll"",
  ""nodes"": {
    ""roll"": { ""type"": ""aibt.stdlib.random-condition"", ""typeVersion"": 1, ""parameters"": { ""success-chance-percent"": 50 } }
  }
}";

        // P11-002 stage 3: rolls every few updates for ever (the patrol tree never reaches its roll: its
        // v1 reactive selector re-enters the work branch at every update). The result of each roll is
        // written to 'score' (1 after a win, 0 after a loss).
        internal const string RollerTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11002.roller"", ""name"": ""P11-002 roller"",
  ""root"": ""loop"",
  ""blackboard"": {
    ""score"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.5 }
  },
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""cycle""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""cycle"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""hold"", ""pick""] },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""pick"": { ""type"": ""aibt.core.memory-selector"", ""typeVersion"": 1, ""children"": [""win"", ""lose""] },
    ""win"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""roll"", ""mark-win""] },
    ""roll"": { ""type"": ""aibt.stdlib.random-condition"", ""typeVersion"": 1, ""parameters"": { ""success-chance-percent"": 50 } },
    ""mark-win"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 1.0 }, ""bindings"": { ""key"": ""score"" } },
    ""lose"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 0.0 }, ""bindings"": { ""key"": ""score"" } }
  }
}";

        // P11-002 stage 3: the five library random nodes in one loop. Each cycle waits a random time
        // (20 000-120 000 us, so 1-4 ticks of TickMicroseconds), then picks an Int32, a Float32, a flat
        // and a 3D point around 'home', and rolls a 25 % chance into 'lucky'.
        internal const string RandomLibraryTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11002.random-library"", ""name"": ""P11-002 random library"",
  ""root"": ""loop"",
  ""blackboard"": {
    ""choice"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": -1 },
    ""speed"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""home"": { ""type"": ""Float3"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": { ""x"": 10.0, ""y"": 1.0, ""z"": -4.0 } },
    ""target"": { ""type"": ""Float3"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": { ""x"": 0.0, ""y"": 0.0, ""z"": 0.0 } },
    ""jump"": { ""type"": ""Float3"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": { ""x"": 0.0, ""y"": 0.0, ""z"": 0.0 } },
    ""lucky"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false }
  },
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""cycle""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""cycle"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""pause"", ""pick"", ""pace"", ""spot"", ""leap"", ""coin""] },
    ""pause"": { ""type"": ""aibt.native.wait-random-microseconds"", ""typeVersion"": 1, ""parameters"": { ""min-microseconds"": 20000, ""max-microseconds"": 120000 } },
    ""pick"": { ""type"": ""aibt.native.random-int"", ""typeVersion"": 1, ""parameters"": { ""min"": 0, ""max"": 3 }, ""bindings"": { ""key"": ""choice"" } },
    ""pace"": { ""type"": ""aibt.native.random-float"", ""typeVersion"": 1, ""parameters"": { ""min"": 0.5, ""max"": 2.0 }, ""bindings"": { ""key"": ""speed"" } },
    ""spot"": { ""type"": ""aibt.native.random-float3-in-radius"", ""typeVersion"": 1, ""parameters"": { ""min-radius"": 1.0, ""radius"": 5.0, ""flat"": true }, ""bindings"": { ""center"": ""home"", ""result"": ""target"" } },
    ""leap"": { ""type"": ""aibt.native.random-float3-in-radius"", ""typeVersion"": 1, ""parameters"": { ""min-radius"": 0.0, ""radius"": 2.0, ""flat"": false }, ""bindings"": { ""center"": ""home"", ""result"": ""jump"" } },
    ""coin"": { ""type"": ""aibt.core.memory-selector"", ""typeVersion"": 1, ""children"": [""win"", ""lose""] },
    ""win"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""roll"", ""mark-win""] },
    ""roll"": { ""type"": ""aibt.native.random-chance"", ""typeVersion"": 1, ""parameters"": { ""chance"": 0.25 } },
    ""mark-win"": { ""type"": ""aibt.native.set-bool"", ""typeVersion"": 1, ""parameters"": { ""value"": true }, ""bindings"": { ""key"": ""lucky"" } },
    ""lose"": { ""type"": ""aibt.native.set-bool"", ""typeVersion"": 1, ""parameters"": { ""value"": false }, ""bindings"": { ""key"": ""lucky"" } }
  }
}";

        internal static ProductionTreeAsset PrepareAsset(string json)
        {
            var standard = Materialize(typeof(StandardNativeShard));
            var nodes = Materialize(typeof(BuiltInLeafShard)).Nodes.Concat(standard.Nodes).ToArray();
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P10009/tree.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, standard.RegisteredTypes,
                new ReferenceCompilerOptions("Tests/P10009/tree.aibt.json", new ReferenceCompilationPolicy(requireDeterministicNodes: false), new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            return ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(standard.RegisteredTypes),
                SimulationTestCatalog.Fingerprint);
        }

        internal static ProductionTreeProgram CreateProgram(ProductionTreeAsset asset)
        {
            Assert.That(asset.TryCreateProgram(SimulationTestCatalog.TryCreateRuntimeCatalog, out var program, out var failure),
                Is.True, failure.ToString());
            return program;
        }

        private static GeneratedShardMetadataArtifact Materialize(Type shard)
        {
            var metadata = shard.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public);
            object Field(string name) => metadata.GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return GeneratedShardMetadataMaterializer.MaterializeArtifact(
                (string)Field("ShardId"), (uint)Field("ShardVersion"), (string)Field("CanonicalDescriptorJson"),
                (string)Field("DescriptorHash"), (string)Field("ManifestRegistryJson"), (string)Field("NodeRegistryHash"));
        }

        /// <summary>
        /// The observable state of one group after a step: for every slot below the high-water mark its
        /// liveness, generation, update count, root result, failure code, active path and every
        /// Tree/Agent-scope byte, then the step's root completions and faults in order.
        /// </summary>
        internal static void AppendDigest(TreeGroup group, ref Fnv digest, List<uint> path)
        {
            digest.Add((ulong)group.Id);
            for (var index = 0; index < group.HighWaterIndex; index++)
            {
                digest.Add((ulong)index);
                var alive = group.IsSlotAliveForTests(index);
                digest.Add(alive ? 1ul : 0ul);
                digest.Add(group.GenerationForTests(index));
                if (!alive) continue;
                AppendCore(group.CoreForTests(index), ref digest, path);
            }
            var completions = group.GetRootCompletions();
            digest.Add((ulong)completions.Length);
            for (var index = 0; index < completions.Length; index++)
            {
                digest.Add((ulong)completions[index].Agent.Index);
                digest.Add(completions[index].Agent.Generation);
                digest.Add((ulong)completions[index].Result);
            }
            var faults = group.GetFaults();
            digest.Add((ulong)faults.Length);
            for (var index = 0; index < faults.Length; index++)
            {
                digest.Add((ulong)faults[index].Agent.Index);
                digest.Add((ulong)faults[index].Failure.Code);
                digest.Add(faults[index].Failure.OwnerId);
            }
        }

        internal static void AppendCore(ProductionTreeAgentCore core, ref Fnv digest, List<uint> path)
        {
            digest.Add(core.InstanceId);
            digest.Add(core.TotalUpdates);
            digest.Add(core.LastRootResult.HasValue ? 1ul + (ulong)core.LastRootResult.Value : 0ul);
            digest.Add((ulong)core.LastFailure.Code);
            digest.Add(core.Ready ? 1ul : 0ul);
            core.Machine.CopyActivePath(path);
            digest.Add((ulong)path.Count);
            for (var node = 0; node < path.Count; node++) digest.Add(path[node]);
            var adapter = core.GeneratedDispatch;
            for (uint offset = 0; adapter.TryReadTreeValueByte(offset, out var value); offset++) digest.Add(value);
        }

        /// <summary>64-bit FNV-1a over 64-bit words.</summary>
        internal struct Fnv
        {
            private ulong _hash;
            internal static Fnv Create() => new Fnv { _hash = 14695981039346656037UL };
            internal ulong Value => _hash;
            internal void Add(ulong word)
            {
                for (var shift = 0; shift < 64; shift += 8)
                {
                    _hash ^= (byte)(word >> shift);
                    _hash *= 1099511628211UL;
                }
            }
        }
    }
}
