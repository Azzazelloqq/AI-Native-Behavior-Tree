using System;
using System.Collections.Generic;
using AIBT.Authoring;
using UnityEditor;

namespace AIBT.Mcp.Authoring
{
    /// <summary>
    /// Discovers a project's own <see cref="IReferenceLeafBehaviorProvider"/> implementations and
    /// folds them into a <see cref="NodeRegistryBuilder"/> alongside the built-in manifests, so the
    /// discovery-facing MCP tools (<c>aibt_search_nodes</c>, <c>aibt_get_node_contract</c>) see a
    /// project's own registered nodes -- closing the P6-012 gate's own live-reproduced gap
    /// (P7-008, applying ADR-P6-017). Split the same way P6-010's
    /// <see cref="CustomTools.CustomMcpToolProviderDiscovery"/> is: <see cref="DiscoverViaTypeCache"/>
    /// is the only assembly-scanning call (Editor-only, via UnityEditor.TypeCache -- this asmdef
    /// already restricts includePlatforms to Editor), while <see cref="AddDiscovered"/> is pure and
    /// independently testable without needing TypeCache in a test.
    /// </summary>
    internal static class ProjectLeafExtensionDiscovery
    {
        internal static IReadOnlyList<IReferenceLeafBehaviorProvider> DiscoverViaTypeCache()
        {
            var providers = new List<IReferenceLeafBehaviorProvider>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IReferenceLeafBehaviorProvider>())
            {
                if (type.IsAbstract || type.IsInterface)
                {
                    continue;
                }

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                try
                {
                    providers.Add((IReferenceLeafBehaviorProvider)Activator.CreateInstance(type));
                }
                catch (Exception)
                {
                    // A single bad provider must not take every other one -- or the built-ins --
                    // down with it. Nothing currently surfaces this as a diagnostic (P6-010's own
                    // ICustomMcpToolProvider path does, via CustomMcpToolProviderDiscovery.Build's
                    // diagnostics; this path has no discovery-tool-facing diagnostics channel to
                    // report into yet).
                }
            }

            return providers;
        }

        // Pure: adds every discovered provider's manifest+behavior into the given builder via the
        // same public AddProjectExtension path a project itself would use.
        internal static NodeRegistryBuilder AddDiscovered(NodeRegistryBuilder builder, IEnumerable<IReferenceLeafBehaviorProvider> providers)
        {
            foreach (var provider in providers)
            {
                if (provider == null)
                {
                    continue;
                }

                try
                {
                    builder.AddProjectExtension(provider.Manifest, provider.CreateBehavior());
                }
                catch (Exception)
                {
                    // A malformed provider (null manifest/behavior, etc.) must not take every
                    // other provider or the built-ins down with it -- mirrors DiscoverViaTypeCache's
                    // own per-provider isolation above.
                }
            }

            return builder;
        }

        // A malformed project registration (schema/binding diagnostic, not just a construction-time
        // exception) must not break discovery for the built-in catalog -- degrades to a built-ins-only
        // build rather than surfacing a null registry to every discovery tool if the combined build
        // itself fails validation.
        internal static NodeRegistryBuildResult BuildWithBuiltInsAndProjectExtensions()
        {
            var combined = AddDiscovered(NodeRegistryBuilder.CreateWithBuiltIns(), DiscoverViaTypeCache()).Build();
            return combined.Success ? combined : NodeRegistryBuilder.CreateWithBuiltIns().Build();
        }
    }
}
