using System;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;

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
