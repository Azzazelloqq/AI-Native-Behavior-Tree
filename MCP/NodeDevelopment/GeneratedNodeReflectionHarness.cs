using System;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Burst;

namespace AIBT.Mcp.NodeDevelopment
{
    internal readonly struct GeneratedShardReflection
    {
        internal GeneratedShardReflection(string shardId, uint shardVersion, string descriptorJson, string descriptorHash, string registryJson, string registryHash)
        {
            ShardId = shardId;
            ShardVersion = shardVersion;
            DescriptorJson = descriptorJson;
            DescriptorHash = descriptorHash;
            RegistryJson = registryJson;
            RegistryHash = registryHash;
        }

        internal string ShardId { get; }
        internal uint ShardVersion { get; }
        internal string DescriptorJson { get; }
        internal string DescriptorHash { get; }
        internal string RegistryJson { get; }
        internal string RegistryHash { get; }
    }

    /// <summary>
    /// Reflects the constants CodeGen~/AIBT.CodeGen's GeneratedMetadataEmitter emits onto a
    /// compiled shard's nested <c>AibtGeneratedMetadata</c> class -- pure consumption of already-
    /// generated output, never a second metadata-generation mechanism. Scoped (per this card's own
    /// Scope correction) to structural/registry validation only; it does not drive generated
    /// dispatch (see P6-022).
    /// </summary>
    internal static class GeneratedNodeReflectionHarness
    {
        internal static bool TryFindShardType(string assemblyName, out Type shardType, out string failureReason)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, assemblyName, StringComparison.Ordinal));
            if (assembly == null)
            {
                shardType = null;
                failureReason = "Assembly '" + assemblyName + "' is not currently loaded.";
                return false;
            }

            shardType = assembly.GetTypes().FirstOrDefault(candidate =>
                candidate.GetCustomAttributesData().Any(data => data.AttributeType.Name == "AibtCatalogShardAttribute"));
            if (shardType == null)
            {
                failureReason = "No [AibtCatalogShard] type was found in assembly '" + assemblyName + "'.";
                return false;
            }

            failureReason = null;
            return true;
        }

        internal static bool TryReflectMetadata(Type shardType, out GeneratedShardReflection reflection, out string failureReason)
        {
            var metadataType = shardType.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public | BindingFlags.NonPublic);
            if (metadataType == null)
            {
                reflection = default;
                failureReason = "Shard type '" + shardType.FullName + "' has no generated AibtGeneratedMetadata -- it did not compile through the packaged analyzer, or compiled with errors.";
                return false;
            }

            if (!TryReadString(metadataType, "ShardId", out var shardId, out failureReason)
                || !TryReadUInt32(metadataType, "ShardVersion", out var shardVersion, out failureReason)
                || !TryReadString(metadataType, "CanonicalDescriptorJson", out var descriptorJson, out failureReason)
                || !TryReadString(metadataType, "DescriptorHash", out var descriptorHash, out failureReason)
                || !TryReadString(metadataType, "ManifestRegistryJson", out var registryJson, out failureReason)
                || !TryReadString(metadataType, "NodeRegistryHash", out var registryHash, out failureReason))
            {
                reflection = default;
                return false;
            }

            reflection = new GeneratedShardReflection(shardId, shardVersion, descriptorJson, descriptorHash, registryJson, registryHash);
            failureReason = null;
            return true;
        }

        /// <summary>P7-009: materializes the shard's own artifact directly, so a caller (test-node's
        /// dispatch-driving path) can read the target node's real TypeId/manifest without rebuilding
        /// a registry it doesn't need.</summary>
        internal static bool TryMaterializeArtifact(GeneratedShardReflection reflection, out GeneratedShardMetadataArtifact artifact, out string failureReason)
        {
            try
            {
                artifact = GeneratedShardMetadataMaterializer.MaterializeArtifact(
                    reflection.ShardId, reflection.ShardVersion,
                    reflection.DescriptorJson, reflection.DescriptorHash,
                    reflection.RegistryJson, reflection.RegistryHash);
                failureReason = null;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                artifact = default;
                failureReason = ex.Message;
                return false;
            }
        }

        /// <summary>Materializes and rebuilds the real project registry (built-ins plus this shard's nodes) from reflected metadata -- proves the generated node is structurally valid and registry-registerable, via the real, already-accepted production entry points.</summary>
        internal static bool TryBuildRegistry(GeneratedShardReflection reflection, out NodeRegistry registry, out string failureReason)
        {
            try
            {
                var artifact = GeneratedShardMetadataMaterializer.MaterializeArtifact(
                    reflection.ShardId, reflection.ShardVersion,
                    reflection.DescriptorJson, reflection.DescriptorHash,
                    reflection.RegistryJson, reflection.RegistryHash);
                var result = GeneratedNodeRegistry.Build(artifact.Nodes);
                if (!result.Success)
                {
                    registry = default;
                    failureReason = "Registry build reported failure for an unspecified reason.";
                    return false;
                }

                registry = result.Registry;
                failureReason = null;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                registry = default;
                failureReason = ex.Message;
                return false;
            }
        }

        // P7-009: locates the companion [AibtCatalogSet]-decorated type generate_node now stages
        // alongside the node (see StagingSlot.WriteCatalogSet) -- ExecuteImmediate and the
        // fingerprint properties BurstCatalogHandshake needs are only ever emitted on this type,
        // never on the [AibtCatalogShard] type TryFindShardType locates.
        internal static bool TryFindCatalogSetType(string assemblyName, out Type catalogSetType, out string failureReason)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, assemblyName, StringComparison.Ordinal));
            if (assembly == null)
            {
                catalogSetType = null;
                failureReason = "Assembly '" + assemblyName + "' is not currently loaded.";
                return false;
            }

            catalogSetType = assembly.GetTypes().FirstOrDefault(candidate =>
                candidate.GetCustomAttributesData().Any(data => data.AttributeType.Name == "AibtCatalogSetAttribute"));
            if (catalogSetType == null)
            {
                failureReason = "No [AibtCatalogSet] type was found in assembly '" + assemblyName + "'.";
                return false;
            }

            failureReason = null;
            return true;
        }

        // Mirrors Spikes~/GenericNativeDispatchTestHarness's own GeneratedHandshake(): the handshake
        // is read from the real generated catalog's own reflected fingerprint properties, never
        // recomputed (ADR-P6-022 decision 1).
        internal static bool TryReflectHandshake(Type catalogSetType, out BurstCatalogHandshake handshake, out string failureReason)
        {
            if (!TryReadStaticProperty<BurstCatalogFingerprint>(catalogSetType, "Fingerprint", out var fingerprint, out failureReason)
                || !TryReadStaticProperty<BurstHash256>(catalogSetType, "NodeRegistryFingerprint", out var nodeRegistry, out failureReason)
                || !TryReadStaticProperty<BurstHash256>(catalogSetType, "ConfigurationLayoutFingerprint", out var configurationLayout, out failureReason)
                || !TryReadStaticProperty<BurstHash256>(catalogSetType, "MemoryLayoutFingerprint", out var memoryLayout, out failureReason)
                || !TryReadStaticProperty<BurstHash256>(catalogSetType, "AccessLayoutFingerprint", out var accessLayout, out failureReason))
            {
                handshake = default;
                return false;
            }

            handshake = new BurstCatalogHandshake(2u, fingerprint, nodeRegistry, 1u, 1u, configurationLayout, memoryLayout, accessLayout);
            failureReason = null;
            return true;
        }

        internal static bool TryGetExecuteImmediate(Type catalogSetType, out MethodInfo executeImmediate, out string failureReason)
        {
            executeImmediate = catalogSetType.GetMethod("ExecuteImmediate", BindingFlags.Public | BindingFlags.Static);
            if (executeImmediate == null)
            {
                failureReason = "Catalog set type '" + catalogSetType.FullName + "' has no generated ExecuteImmediate -- it did not compile through the packaged analyzer, or compiled with errors.";
                return false;
            }

            failureReason = null;
            return true;
        }

        private static bool TryReadStaticProperty<T>(Type type, string propertyName, out T value, out string failureReason)
        {
            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property == null)
            {
                value = default;
                failureReason = "Generated catalog set is missing property '" + propertyName + "'.";
                return false;
            }

            value = (T)property.GetValue(null);
            failureReason = null;
            return true;
        }

        private static bool TryReadString(Type type, string fieldName, out string value, out string failureReason)
        {
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
            {
                value = null;
                failureReason = "Generated metadata is missing field '" + fieldName + "'.";
                return false;
            }

            value = (string)field.GetValue(null);
            failureReason = null;
            return true;
        }

        private static bool TryReadUInt32(Type type, string fieldName, out uint value, out string failureReason)
        {
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
            {
                value = 0;
                failureReason = "Generated metadata is missing field '" + fieldName + "'.";
                return false;
            }

            value = (uint)field.GetValue(null);
            failureReason = null;
            return true;
        }
    }
}
