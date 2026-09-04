using System;
using System.Linq;
using System.Text;

namespace AIBT.Authoring
{
    // The authority this verifier checks against (Runtime/Nodes/Contracts/
    // RuntimeBuiltInCatalogAuthority.cs) is a permanently frozen snapshot of only the original
    // aibt.core.* structural composites/decorators -- the ones with no [AibtBurstNode] shard at
    // all, interpreted directly via NativeLifecycleNodeKindV1. AIBT.CodeGen's BurstNodeGenerator
    // (AIBT5012) never allows a live [AibtCatalogSet] shard to ALSO claim an aibt.core.* identity
    // already present here (or any identity present here at all -- its merge step treats any
    // repeat as a duplicate), so this rebuild deliberately excludes every other built-in
    // (aibt.stdlib.* leaves, P7-028) even though NodeRegistryBuilder.CreateWithBuiltIns() itself
    // returns the full, larger built-in set.
    internal static class RuntimeBuiltInCatalogAuthorityVerifier
    {
        internal static string RebuildManifestRegistryJson()
        {
            return NodeManifestCanonicalJson.SerializeRegistry(RebuildAuthorityEntries());
        }

        internal static string RebuildNodeRegistryHash()
        {
            return StableHash.Sha256Hex(NodeManifestCanonicalJson.SerializeRegistryUtf8(RebuildAuthorityEntries()));
        }

        internal static void Validate(string manifestRegistryJson, string nodeRegistryHash)
        {
            var entries = RebuildAuthorityEntries();
            var canonicalJson = NodeManifestCanonicalJson.SerializeRegistry(entries);
            var canonicalHash = StableHash.Sha256Hex(NodeManifestCanonicalJson.SerializeRegistryUtf8(entries));
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

        private static NodeRegistryEntry[] RebuildAuthorityEntries()
        {
            var result = NodeRegistryBuilder.CreateWithBuiltIns().Build();
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    "Canonical built-in node registry could not be built; inspect registry collision diagnostics.");
            }

            return result.Registry
                .Where(entry => entry.Manifest.TypeId.StartsWith("aibt.core.", StringComparison.Ordinal))
                .ToArray();
        }
    }
}
