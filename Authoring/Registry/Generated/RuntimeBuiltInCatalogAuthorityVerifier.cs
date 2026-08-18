using System;
using System.Text;

namespace AIBT.Authoring
{
    internal static class RuntimeBuiltInCatalogAuthorityVerifier
    {
        internal static string RebuildManifestRegistryJson()
        {
            return SerializeRegistry(RebuildRegistry());
        }

        internal static string RebuildNodeRegistryHash()
        {
            return RebuildRegistry().Hash;
        }

        internal static void Validate(string manifestRegistryJson, string nodeRegistryHash)
        {
            var registry = RebuildRegistry();
            var canonicalJson = SerializeRegistry(registry);
            var canonicalHash = registry.Hash;
            if (!string.Equals(manifestRegistryJson, canonicalJson, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Runtime built-in catalog JSON differs from the canonical Authoring registry bytes.");
            }

            if (!string.Equals(nodeRegistryHash, canonicalHash, StringComparison.Ordinal)
                || !string.Equals(
                    StableHash.Sha256Hex(Encoding.UTF8.GetBytes(manifestRegistryJson)),
                    nodeRegistryHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Runtime built-in catalog hash differs from the canonical Authoring registry hash.");
            }
        }

        private static string SerializeRegistry(NodeRegistry registry)
        {
            var entries = new NodeRegistryEntry[registry.Count];
            for (var index = 0; index < registry.Count; index++)
            {
                entries[index] = registry[index];
            }

            return NodeManifestCanonicalJson.SerializeRegistry(entries);
        }

        private static NodeRegistry RebuildRegistry()
        {
            var result = NodeRegistryBuilder.CreateWithBuiltIns().Build();
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    "Canonical built-in node registry could not be built; inspect registry collision diagnostics.");
            }

            return result.Registry;
        }
    }
}
