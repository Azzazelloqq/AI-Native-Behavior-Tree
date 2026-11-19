using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    // P11-002 stage 1: a project catalog set listing the package's native library shard next to the
    // built-in leaves and a project shard (the P7-039 fixture shard stands in for a project's nodes).
    [AibtCatalogSet("aibt.tests.p11002.native-library", 1u, typeof(BuiltInLeafShard), typeof(StandardNativeShard), typeof(ExternalTreeWriteFixtureShard))]
    public static partial class NativeLibraryCatalog { }

    /// <summary>Every node of the package shard aibt.native.standard after P11-002 stage 2.</summary>
    internal static class StandardNativeLibraryIds
    {
        /// <summary>The four P9-002 nodes.</summary>
        internal static readonly string[] Seed =
        {
            "aibt.native.bool-is-true", "aibt.native.float-at-least", "aibt.native.set-float", "aibt.native.wait-microseconds",
        };

        /// <summary>The 20 nodes of ADR-P11-002 §5.1 groups 1-3.</summary>
        internal static readonly string[] Stage1 =
        {
            "aibt.native.int-at-least", "aibt.native.int-equals", "aibt.native.int-keys-at-least", "aibt.native.float-keys-at-least",
            "aibt.native.float3-within-distance", "aibt.native.float3-is-set",
            "aibt.native.set-bool", "aibt.native.set-int", "aibt.native.set-float3",
            "aibt.native.copy-bool", "aibt.native.copy-int", "aibt.native.copy-float", "aibt.native.copy-float3",
            "aibt.native.increment-int", "aibt.native.increment-float", "aibt.native.toggle-bool", "aibt.native.float3-distance",
            "aibt.native.succeed", "aibt.native.fail", "aibt.native.idle",
        };

        /// <summary>The 4 timing and signal nodes of ADR-P11-002 §5.1 group 4.</summary>
        internal static readonly string[] Stage2 =
        {
            "aibt.native.wait-until-true", "aibt.native.set-deadline", "aibt.native.deadline-passed", "aibt.native.signal",
        };

        internal static readonly string[] All = Seed.Concat(Stage1).Concat(Stage2).ToArray();
    }

    /// <summary>JSON builders for the library test trees (canonical aibt.tree, format 2, Tree-scope keys only).</summary>
    internal static class LibraryTrees
    {
        internal static string Tree(string treeId, string root, IEnumerable<string> keys, params string[] nodes)
            => "{ \"format\": \"aibt.tree\", \"formatVersion\": 2, \"treeId\": \"" + treeId + "\", \"name\": \"" + treeId + "\", \"root\": \"" + root + "\", "
               + "\"blackboard\": { " + string.Join(", ", keys) + " }, \"nodes\": { " + string.Join(", ", nodes) + " } }";

        internal static string Bool(string name, bool value = false) => Key(name, "Bool", value ? "true" : "false");

        internal static string Int(string name, int value = 0) => Key(name, "Int32", value.ToString(CultureInfo.InvariantCulture));

        internal static string Float(string name, float value = 0f) => Key(name, "Float32", Number(value));

        internal static string Long(string name, long value = 0L) => Key(name, "Int64", value.ToString(CultureInfo.InvariantCulture));

        internal static string Float3(string name, float x = 0f, float y = 0f, float z = 0f)
            => Key(name, "Float3", "{ \"x\": " + Number(x) + ", \"y\": " + Number(y) + ", \"z\": " + Number(z) + " }");

        internal static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>A leaf: <paramref name="parameters"/> and <paramref name="bindings"/> are JSON object bodies or null.</summary>
        internal static string Leaf(string id, string type, string parameters = null, string bindings = null, string observer = null)
        {
            var builder = new StringBuilder();
            builder.Append('"').Append(id).Append("\": { \"type\": \"").Append(type).Append("\", \"typeVersion\": 1");
            if (parameters != null) builder.Append(", \"parameters\": { ").Append(parameters).Append(" }");
            if (bindings != null) builder.Append(", \"bindings\": { ").Append(bindings).Append(" }");
            if (observer != null) builder.Append(", \"observer\": { ").Append(observer).Append(" }");
            return builder.Append(" }").ToString();
        }

        internal static string Composite(string id, string type, params string[] children) => CompositeWith(id, type, null, children);

        /// <summary>A composite or decorator with parameters (a JSON object body); a separate name so a child id never binds as parameters.</summary>
        internal static string CompositeWith(string id, string type, string parameters, params string[] children)
        {
            var builder = new StringBuilder();
            builder.Append('"').Append(id).Append("\": { \"type\": \"").Append(type).Append("\", \"typeVersion\": 1");
            if (parameters != null) builder.Append(", \"parameters\": { ").Append(parameters).Append(" }");
            builder.Append(", \"children\": [\"").Append(string.Join("\", \"", children)).Append("\"]");
            return builder.Append(" }").ToString();
        }

        private static string Key(string name, string type, string value)
            => "\"" + name + "\": { \"type\": \"" + type + "\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": " + value + " }";
    }

    /// <summary>
    /// Prepares library trees the way the Editor does (generated compiler, ProductionTreeAsset recorded
    /// against <see cref="NativeLibraryCatalog"/>) and starts ProductionTreeHosts from them. Disposes
    /// every host, program and asset it made.
    /// </summary>
    internal sealed class NativeLibraryKit : IDisposable
    {
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        private readonly List<ProductionTreeProgram> _programs = new List<ProductionTreeProgram>();
        private readonly List<GameObject> _hosts = new List<GameObject>();

        internal sealed class Prepared
        {
            internal ProductionTreeAsset Asset;
            internal ProductionTreeProgram Program;
            internal Dictionary<string, GeneratedScopeSlot> Slots;
        }

        internal static GeneratedShardMetadataArtifact Materialize(Type shard)
        {
            var metadata = shard.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public);
            object Field(string name) => metadata.GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return GeneratedShardMetadataMaterializer.MaterializeArtifact(
                (string)Field("ShardId"), (uint)Field("ShardVersion"), (string)Field("CanonicalDescriptorJson"),
                (string)Field("DescriptorHash"), (string)Field("ManifestRegistryJson"), (string)Field("NodeRegistryHash"));
        }

        internal static GeneratedCompiledProgramV2 Compile(string json)
        {
            var result = CompileResult(json);
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)) + "\n" + json);
            return result.Program;
        }

        /// <summary>Compiles without asserting success (for trees the compiler must refuse).</summary>
        internal static GeneratedCompiledProgramV2Result CompileResult(string json)
        {
            var standard = Materialize(typeof(StandardNativeShard));
            var nodes = Materialize(typeof(BuiltInLeafShard)).Nodes
                .Concat(standard.Nodes)
                .Concat(Materialize(typeof(ExternalTreeWriteFixtureShard)).Nodes)
                .ToArray();
            const string sourceId = "Tests/P11002/library.aibt.json";
            var parsed = CanonicalTreeJson.Parse(json, sourceId);
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)) + "\n" + json);
            // The default Phase1 policy: every library node is declared deterministic.
            return GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, standard.RegisteredTypes,
                new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
        }

        internal Prepared Prepare(string json)
        {
            var compiled = Compile(json);
            var standard = Materialize(typeof(StandardNativeShard));
            var asset = ProductionTreeAsset.Create(compiled.CreateNativeBlackboardBindingV2(standard.RegisteredTypes), NativeLibraryCatalog.Fingerprint);
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(NativeLibraryCatalog).FullName + ", " + typeof(NativeLibraryCatalog).Assembly.GetName().Name);
            _objects.Add(asset);
            Assert.That(asset.TryCreateProgram(NativeLibraryCatalog.TryCreateRuntimeCatalog, out var program, out var failure), Is.True, failure.ToString());
            _programs.Add(program);
            return new Prepared
            {
                Asset = asset,
                Program = program,
                Slots = compiled.Slots.Where(slot => slot.Slot.Key.Scope == BlackboardScope.Tree)
                    .ToDictionary(slot => slot.Slot.Key.Id, slot => slot.Slot, StringComparer.Ordinal),
            };
        }

        /// <summary>Starts a host; <paramref name="effectCapacity"/> above 0 enables its committed-effect queue (stage 2 signals).</summary>
        internal LibraryHost Start(Prepared prepared, Func<long> clock = null, int effectCapacity = 0)
        {
            var gameObject = new GameObject("AIBT.Tests.P11002.LibraryHost");
            _hosts.Add(gameObject);
            var host = gameObject.AddComponent<ProductionTreeHost>();
            Assert.That(prepared.Program.TryBootstrap(host, out var failure, effectCapacity, clock ?? (() => 0L)), Is.True, failure.Code.ToString());
            return new LibraryHost(host, prepared.Slots);
        }

        public void Dispose()
        {
            foreach (var gameObject in _hosts)
            {
                if (gameObject == null) continue;
                // EditMode sends no OnDestroy to a plain MonoBehaviour: release the host as Unity would.
                var host = gameObject.GetComponent<ProductionTreeHost>();
                if (host != null)
                    typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
            _hosts.Clear();
            foreach (var program in _programs) program.Dispose();
            _programs.Clear();
            foreach (var item in _objects) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            _objects.Clear();
        }
    }

    /// <summary>One ProductionTreeHost with typed fact writes and typed reads of its Tree-scope values.</summary>
    internal sealed class LibraryHost
    {
        private readonly Dictionary<string, GeneratedScopeSlot> _slots;

        internal LibraryHost(ProductionTreeHost host, Dictionary<string, GeneratedScopeSlot> slots)
        {
            Host = host;
            _slots = slots;
        }

        internal ProductionTreeHost Host { get; }

        internal NodeStatus? Root => Host.LastRootResult;

        internal NodeStatus? Update()
        {
            Host.DriveOneUpdate();
            Assert.That(Host.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None), "The host must not fail.");
            return Host.LastRootResult;
        }

        internal void Fact(string key, bool value) => Write(key, value, BuiltInBlackboardTypes.Bool);

        internal void Fact(string key, int value) => Write(key, value, BuiltInBlackboardTypes.Int32);

        internal void Fact(string key, float value) => Write(key, value, BuiltInBlackboardTypes.Float32);

        internal void Fact(string key, Float3Value value) => Write(key, value, BuiltInBlackboardTypes.Float3);

        internal void Fact(string key, long value) => Write(key, value, BuiltInBlackboardTypes.Int64);

        private void Write<T>(string key, T value, BlackboardTypeDescriptor type) where T : unmanaged
        {
            Assert.That(Host.TryResolveExternalTreeWrite<T>(key, NativeBlackboardTypeIdV2.BuiltIn(type), out var handle, out var failure),
                Is.True, key + ": " + failure.Code);
            Assert.That(Host.TryWriteExternalTreeValue(handle, value, out _, out failure), Is.True, key + ": " + failure.Code);
        }

        internal bool ReadBool(string key) => Bytes(key, 1)[0] != 0;

        internal int ReadInt(string key) => BitConverter.ToInt32(Bytes(key, 4), 0);

        internal float ReadFloat(string key) => BitConverter.ToSingle(Bytes(key, 4), 0);

        internal long ReadLong(string key) => BitConverter.ToInt64(Bytes(key, 8), 0);

        /// <summary>Drains the host's committed effects (enabled by an effect capacity at start) as (stable id, Int32 value).</summary>
        internal List<(ulong Id, int Value)> DrainEffects()
        {
            var receiver = new EffectRecorder();
            Assert.That(Host.TryDrainEffects(receiver, out var failure), Is.True, failure.Code.ToString());
            return receiver.Effects;
        }

        private sealed class EffectRecorder : IProductionTreeEffectReceiver
        {
            internal readonly List<(ulong Id, int Value)> Effects = new List<(ulong Id, int Value)>();

            public void Receive(in ProductionTreeEffect effect)
            {
                Assert.That(effect.TryGetInt32(out var value), Is.True, "Library signals are Int32 effects.");
                Effects.Add((effect.StableId, value));
            }
        }

        internal Float3Value ReadFloat3(string key)
        {
            var bytes = Bytes(key, 12);
            return new Float3Value(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8));
        }

        private byte[] Bytes(string key, int count)
        {
            Assert.That(_slots.TryGetValue(key, out var slot), Is.True, "Unknown Tree key " + key);
            var bytes = new byte[count];
            var adapter = Host.GeneratedDispatchAdapter;
            for (var index = 0; index < count; index++)
                Assert.That(adapter.TryReadTreeValueByte(slot.Offset + (uint)index, out bytes[index]), Is.True, key);
            return bytes;
        }
    }
}
