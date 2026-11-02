using System;
using System.Collections.Generic;
using AIBT.Authoring;
using UnityEditor;

namespace AIBT.Editor.Registry
{
    /// <summary>
    /// Discovers a project's own <see cref="IReferenceLeafBehaviorProvider"/> implementations and
    /// folds them into a <see cref="NodeRegistryBuilder"/> alongside the built-in manifests, so the
    /// discovery-facing MCP tools (<c>aibt_search_nodes</c>, <c>aibt_get_node_contract</c>) see a
    /// project's own registered nodes -- closing the P6-012 gate's own live-reproduced gap
    /// (P7-008, applying ADR-P6-017). Split the same way P6-010's
    /// the MCP custom tool discovery is: <see cref="DiscoverViaTypeCache"/>
    /// is the only assembly-scanning call (Editor-only, via UnityEditor.TypeCache -- this asmdef
    /// already restricts includePlatforms to Editor), while <see cref="AddDiscovered"/> is pure and
    /// independently testable without needing TypeCache in a test.
    /// </summary>
    internal static class ProjectLeafExtensionDiscovery
    {
        internal static IReadOnlyList<IReferenceLeafBehaviorProvider> DiscoverViaTypeCache(ICollection<string> problems = null)
        {
            var providers = new List<IReferenceLeafBehaviorProvider>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IReferenceLeafBehaviorProvider>())
            {
                if (type.IsAbstract || type.IsInterface || type.DeclaringType == typeof(BuiltInLeafManifests))
                {
                    continue;
                }

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    if (type.IsVisible) problems?.Add("Node provider " + type.FullName + " requires a public parameterless constructor.");
                    continue;
                }

                try
                {
                    providers.Add((IReferenceLeafBehaviorProvider)Activator.CreateInstance(type));
                }
                catch (Exception exception)
                {
                    problems?.Add("Could not create node provider " + type.FullName + ": " + exception.Message);
                }
            }

            return providers;
        }

        // Pure: adds every discovered provider's manifest+behavior into the given builder via the
        // same public AddProjectExtension path a project itself would use.
        internal static NodeRegistryBuilder AddDiscovered(NodeRegistryBuilder builder, IEnumerable<IReferenceLeafBehaviorProvider> providers, ICollection<string> problems = null)
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
                catch (Exception exception)
                {
                    problems?.Add("Invalid node provider " + provider.GetType().FullName + ": " + exception.Message);
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
            return ProjectNodeCatalogCache.Current.BuildResult;
        }
    }
}
