using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Registry;
using UnityEditor;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    /// <summary>Whether a prepared runtime asset still matches its authored source and node catalog.</summary>
    public enum ProductionTreeAssetState
    {
        /// <summary>Source bytes and catalog fingerprint match the snapshot.</summary>
        Current = 0,
        /// <summary>
        /// The source was edited after preparation, or one of its subtrees was edited, removed or
        /// became ambiguous (ADR-P11-001).
        /// </summary>
        SourceChanged = 1,
        /// <summary>The generated catalog changed (nodes added, removed or edited).</summary>
        CatalogChanged = 2,
        /// <summary>The recorded source asset no longer exists.</summary>
        SourceMissing = 3,
        /// <summary>The recorded catalog type no longer exists.</summary>
        CatalogMissing = 4,
        /// <summary>Prepared before provenance was recorded; it can be linked by preparing it again.</summary>
        Unknown = 5,
    }

    /// <summary>
    /// Detects stale <see cref="ProductionTreeAsset"/>s and re-prepares them: after a tree source is
    /// imported (saved) and after every script reload (catalog changes). Only assets that recorded
    /// their source and catalog are rebuilt automatically; failures are logged and leave the old
    /// snapshot in place, visibly stale in its inspector.
    /// </summary>
    [InitializeOnLoad]
    public static class ProductionTreeAssetStaleness
    {
        static ProductionTreeAssetStaleness()
        {
            // After a domain reload a catalog may have changed. The AssetDatabase is not ready for
            // writes inside the static constructor; afterAssemblyReload runs once it is, even while
            // the Editor is in the background (delayCall would wait for the next Editor tick).
            AssemblyReloadEvents.afterAssemblyReload += RefreshAfterReload;
        }

        private static void RefreshAfterReload()
        {
            AssemblyReloadEvents.afterAssemblyReload -= RefreshAfterReload;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            RefreshAll(null);
        }

        /// <summary>Project path of the asset's authored source, or null.</summary>
        public static string SourcePath(ProductionTreeAsset asset)
        {
            if (asset == null) return null;
            if (!string.IsNullOrEmpty(asset.SourceGuid))
            {
                var path = AssetDatabase.GUIDToAssetPath(asset.SourceGuid);
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            }
            return !string.IsNullOrEmpty(asset.SourceId) && File.Exists(asset.SourceId) ? asset.SourceId : null;
        }

        /// <summary>Catalog type recorded in the asset, or inferred from its fingerprint for older assets.</summary>
        public static Type CatalogType(ProductionTreeAsset asset)
        {
            if (asset == null) return null;
            if (!string.IsNullOrEmpty(asset.CatalogTypeName))
                return Type.GetType(asset.CatalogTypeName, false);
            foreach (var type in TypeCache.GetTypesWithAttribute<AIBT.Burst.AibtCatalogSetAttribute>())
                if (TryCurrentFingerprint(type, out var fingerprint) && fingerprint == asset.CatalogFingerprint) return type;
            return null;
        }

        public static ProductionTreeAssetState Evaluate(ProductionTreeAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (string.IsNullOrEmpty(asset.SourceGuid) || string.IsNullOrEmpty(asset.CatalogTypeName)) return ProductionTreeAssetState.Unknown;
            var path = AssetDatabase.GUIDToAssetPath(asset.SourceGuid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return ProductionTreeAssetState.SourceMissing;
            var type = Type.GetType(asset.CatalogTypeName, false);
            if (type == null || !TryCurrentFingerprint(type, out var fingerprint)) return ProductionTreeAssetState.CatalogMissing;
            if (fingerprint != asset.CatalogFingerprint) return ProductionTreeAssetState.CatalogChanged;
            // ADR-P11-001 §4.5: an asset prepared from subtrees hashes the source and every subtree
            // file as the project resolves them now; an unresolvable subtree is a change.
            var current = asset.SubtreeSourceGuids.Length == 0
                ? StableHash.Sha256Hex(File.ReadAllBytes(path))
                : ProductionTreeAssetBaker.CurrentSourceHash(path);
            if (current != asset.SourceContentHash) return ProductionTreeAssetState.SourceChanged;
            return ProductionTreeAssetState.Current;
        }

        public static string Describe(ProductionTreeAssetState state)
        {
            switch (state)
            {
                case ProductionTreeAssetState.Current: return "Up to date with its source and node catalog.";
                case ProductionTreeAssetState.SourceChanged: return "Stale: the source tree changed after this snapshot was prepared.";
                case ProductionTreeAssetState.CatalogChanged: return "Stale: the node catalog changed after this snapshot was prepared.";
                case ProductionTreeAssetState.SourceMissing: return "The source tree recorded for this snapshot no longer exists.";
                case ProductionTreeAssetState.CatalogMissing: return "The node catalog recorded for this snapshot no longer exists.";
                default: return "Prepared before automatic updates; prepare it again to link it to its source and catalog.";
            }
        }

        /// <summary>
        /// Rebuilds <paramref name="asset"/> in place from its source and catalog (inferred for older
        /// assets). Returns false with an explanation and leaves the asset unchanged on failure.
        /// </summary>
        public static bool TryPrepareAgain(ProductionTreeAsset asset, out string error)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            var path = SourcePath(asset);
            if (path == null) { error = "The source tree is unavailable."; return false; }
            var type = CatalogType(asset);
            if (type == null) { error = "The node catalog is unavailable."; return false; }
            var parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
            if (!parsed.Success)
            {
                error = string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message));
                return false;
            }
            ProductionTreeAsset fresh = null;
            try
            {
                fresh = ProductionTreeAssetBaker.Create(parsed.Document, path, type, out var diagnostics);
                if (fresh == null)
                {
                    error = string.Join("\n", diagnostics.Select(d => d.Code.Value + ": " + d.Message));
                    return false;
                }
                var name = asset.name;
                EditorUtility.CopySerialized(fresh, asset);
                asset.name = name;
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                error = null;
                return true;
            }
            catch (InvalidOperationException exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                if (fresh != null) UnityEngine.Object.DestroyImmediate(fresh);
            }
        }

        /// <summary>
        /// Re-prepares every stale linked runtime asset, or only those prepared from
        /// <paramref name="sourceGuids"/> (as their source or one of their subtrees). Returns how many
        /// were rebuilt.
        /// </summary>
        public static int RefreshAll(ICollection<string> sourceGuids) => RefreshAll(sourceGuids, false);

        /// <param name="includeSubtreeDependants">
        /// Also evaluates every asset prepared from subtrees: a new, moved or removed tree file can
        /// change how its tree IDs resolve.
        /// </param>
        internal static int RefreshAll(ICollection<string> sourceGuids, bool includeSubtreeDependants)
        {
            var rebuilt = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(ProductionTreeAsset)))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(assetPath);
                if (asset == null || string.IsNullOrEmpty(asset.SourceGuid)) continue;
                if (sourceGuids != null && !sourceGuids.Contains(asset.SourceGuid)
                    && !(includeSubtreeDependants && asset.SubtreeSourceGuids.Length != 0)
                    && !asset.SubtreeSourceGuids.Any(sourceGuids.Contains)) continue;
                var state = Evaluate(asset);
                if (state != ProductionTreeAssetState.SourceChanged && state != ProductionTreeAssetState.CatalogChanged) continue;
                if (TryPrepareAgain(asset, out var error))
                {
                    rebuilt++;
                    Debug.Log("AIBT: re-prepared " + assetPath + " (" + (state == ProductionTreeAssetState.SourceChanged ? "source" : "catalog") + " changed).", asset);
                }
                else Debug.LogWarning("AIBT: " + assetPath + " is stale and could not be re-prepared: " + error, asset);
            }
            return rebuilt;
        }

        private static bool TryCurrentFingerprint(Type catalogType, out string fingerprint)
        {
            fingerprint = null;
            if (!GeneratedNodeReflectionHarness.TryReflectHandshake(catalogType, out var handshake, out _)) return false;
            fingerprint = ProductionTreeAsset.FormatFingerprint(handshake.Catalog);
            return true;
        }

        private sealed class SourcePostprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                var guids = new HashSet<string>(imported.Where(TreeSourcePaths.IsTree).Select(AssetDatabase.AssetPathToGUID)
                    .Where(guid => !string.IsNullOrEmpty(guid)), StringComparer.Ordinal);
                var treeFilesChanged = guids.Count != 0 || deleted.Concat(moved).Concat(movedFrom).Any(TreeSourcePaths.IsTree);
                if (!treeFilesChanged) return;
                // Synchronous on purpose: rewriting another asset here is allowed (Unity runs one more
                // postprocess round for it) and does not depend on Editor ticks.
                RefreshAll(guids, includeSubtreeDependants: true);
            }
        }
    }
}
