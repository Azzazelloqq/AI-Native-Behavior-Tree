using System;
using System.Globalization;
using AIBT.Burst;
using Unity.Collections;
using UnityEngine;

namespace AIBT
{
    public delegate bool ProductionTreeCatalogFactory(Allocator allocator, out GeneratedBurstCatalogV2 catalog, out BurstContextResult failure);

    /// <summary>Editor-prepared compiled data. The authored tree remains the source of truth.</summary>
    public sealed class ProductionTreeAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] private byte[] _payload;
        [SerializeField, HideInInspector] private string _payloadHash;
        [SerializeField, HideInInspector] private string _catalogFingerprint;
        [SerializeField, HideInInspector] private string _sourceId;

        public string SourceId => _sourceId;

        public static ProductionTreeAsset Create(NativeProgramBlackboardBindingV2 binding, BurstCatalogFingerprint catalogFingerprint)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            if (CompiledProgramContentHashV1.Compute(binding.SemanticProgram) != binding.SemanticProgram.Header.CompiledContentHash)
                throw new ArgumentException("The compiled semantic program hash is invalid.", nameof(binding));
            var asset = CreateInstance<ProductionTreeAsset>();
            asset._payload = ProductionTreeArchive.Write(binding);
            asset._payloadHash = StableHash.Sha256Hex(asset._payload);
            asset._catalogFingerprint = FingerprintText(catalogFingerprint);
            asset._sourceId = binding.SemanticProgram.DebugMap.Count == 0 ? string.Empty : binding.SemanticProgram.DebugMap[0].SourcePath;
            return asset;
        }

        /// <summary>Creates one explicitly owned program/catalog, shareable by multiple hosts.</summary>
        public bool TryCreateProgram(ProductionTreeCatalogFactory factory, out ProductionTreeProgram program, out BurstContextResult failure)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            program = null;
            if (!TryRead(out var binding))
            {
                failure = BurstContextResult.InvalidEncoding;
                return false;
            }
            if (!factory(Allocator.Persistent, out var catalog, out failure)) return false;
            if (catalog == null || !catalog.IsCreated || FingerprintText(catalog.Fingerprint) != _catalogFingerprint)
            {
                catalog?.TryDispose(out _);
                failure = BurstContextResult.TypeMismatch;
                return false;
            }
            if (!GeneratedTreeRuntimeDefinitionV2.TryCreate(binding, catalog, out var definition, out failure))
            {
                catalog?.TryDispose(out _);
                return false;
            }
            program = new ProductionTreeProgram(definition, catalog);
            return true;
        }

        private bool TryRead(out NativeProgramBlackboardBindingV2 binding)
        {
            binding = null;
            if (_payload == null || _payload.Length == 0 || StableHash.Sha256Hex(_payload) != _payloadHash) return false;
            return ProductionTreeArchive.TryRead(_payload, out binding);
        }

        private static string FingerprintText(BurstCatalogFingerprint fingerprint)
        {
            var value = fingerprint.Value;
            return value.Word0.ToString("x8", CultureInfo.InvariantCulture) + value.Word1.ToString("x8", CultureInfo.InvariantCulture)
                + value.Word2.ToString("x8", CultureInfo.InvariantCulture) + value.Word3.ToString("x8", CultureInfo.InvariantCulture)
                + value.Word4.ToString("x8", CultureInfo.InvariantCulture) + value.Word5.ToString("x8", CultureInfo.InvariantCulture)
                + value.Word6.ToString("x8", CultureInfo.InvariantCulture) + value.Word7.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
