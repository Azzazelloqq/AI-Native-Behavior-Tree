using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AIBT
{
    /// <summary>
    /// Reference-counted <see cref="ProductionTreeProgram"/> cache shared by every
    /// <see cref="NativeTreeAgent"/> that runs the same runtime asset with the same generated catalog.
    /// The program (and its native catalog) is disposed when the last agent releases it.
    /// </summary>
    internal static class NativeTreeProgramCache
    {
        private readonly struct Key : IEquatable<Key>
        {
            internal Key(ProductionTreeAsset asset, MethodInfo factory)
            {
                Asset = asset;
                Factory = factory;
            }

            internal ProductionTreeAsset Asset { get; }
            internal MethodInfo Factory { get; }

            public bool Equals(Key other) => ReferenceEquals(Asset, other.Asset) && Equals(Factory, other.Factory);
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode()
                => ((Asset == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Asset)) * 397)
                    ^ (Factory == null ? 0 : Factory.GetHashCode());
        }

        private sealed class Entry
        {
            internal ProductionTreeProgram Program;
            internal int References;
        }

        private static readonly Dictionary<Key, Entry> s_entries = new Dictionary<Key, Entry>();
        private static readonly Dictionary<string, ProductionTreeCatalogFactory> s_factories =
            new Dictionary<string, ProductionTreeCatalogFactory>(StringComparer.Ordinal);

        /// <summary>Programs currently alive (diagnostics and leak tests).</summary>
        internal static int LiveProgramCount => s_entries.Count;

        internal static int ReferenceCount(ProductionTreeProgram program)
        {
            foreach (var entry in s_entries.Values)
                if (ReferenceEquals(entry.Program, program)) return entry.References;
            return 0;
        }

        internal static bool TryAcquire(ProductionTreeAsset asset, ProductionTreeCatalogFactory factory,
            out ProductionTreeProgram program, out string error)
        {
            program = null;
            if (asset == null) { error = "No runtime tree asset is assigned."; return false; }
            if (factory == null && !TryResolveFactory(asset, out factory, out error)) return false;
            var key = new Key(asset, factory.Method);
            if (s_entries.TryGetValue(key, out var entry))
            {
                entry.References++;
                program = entry.Program;
                error = null;
                return true;
            }
            if (!asset.TryCreateProgram(factory, out program, out var failure))
            {
                error = failure == Burst.BurstContextResult.TypeMismatch
                    ? "The runtime asset was prepared for a different node catalog. Prepare it again for this catalog."
                    : "The runtime asset could not create its program (" + failure + "). Prepare it again.";
                return false;
            }
            s_entries.Add(key, new Entry { Program = program, References = 1 });
            error = null;
            return true;
        }

        internal static void Release(ProductionTreeProgram program)
        {
            if (program == null) return;
            foreach (var pair in s_entries)
            {
                if (!ReferenceEquals(pair.Value.Program, program)) continue;
                if (--pair.Value.References > 0) return;
                s_entries.Remove(pair.Key);
                if (!program.TryDispose(out var failure))
                    Debug.LogError("AIBT NativeTreeAgent: the shared program could not be disposed (" + failure + "); a host is still alive.");
                return;
            }
        }

        /// <summary>Resolves the generated <c>TryCreateRuntimeCatalog</c> of the catalog type recorded in the asset.</summary>
        internal static bool TryResolveFactory(ProductionTreeAsset asset, out ProductionTreeCatalogFactory factory, out string error)
        {
            factory = null;
            var typeName = asset.CatalogTypeName;
            if (string.IsNullOrEmpty(typeName))
            {
                error = "The runtime asset does not record its node catalog. Prepare it again, or assign NativeTreeAgent.CatalogFactory from code.";
                return false;
            }
            if (s_factories.TryGetValue(typeName, out factory))
            {
                error = null;
                return true;
            }
            var type = Type.GetType(typeName, false);
            var method = type?.GetMethod("TryCreateRuntimeCatalog", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                error = "The node catalog '" + typeName + "' is unavailable (renamed, removed or stripped). Prepare the asset again, or assign NativeTreeAgent.CatalogFactory from code.";
                return false;
            }
            try
            {
                factory = (ProductionTreeCatalogFactory)Delegate.CreateDelegate(typeof(ProductionTreeCatalogFactory), method);
            }
            catch (ArgumentException)
            {
                error = "'" + typeName + "' is not a generated AIBT catalog.";
                return false;
            }
            s_factories[typeName] = factory;
            error = null;
            return true;
        }

        // With Enter Play Mode Options (no domain reload) statics survive between sessions. Agents
        // release their programs in OnDisable, so this only frees what an abnormal exit left behind.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            foreach (var entry in s_entries.Values) entry.Program.TryDispose(out _);
            s_entries.Clear();
            s_factories.Clear();
        }
    }
}
