using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace AIBT.Mcp.CustomTools
{
    /// <summary>
    /// P6-010's discovery mechanism. Split the same way <c>AIBT.Authoring.NodeRegistryBuilder</c>
    /// separates "what to seed" from "how to validate/build": <see cref="DiscoverViaTypeCache"/> is
    /// the only assembly-scanning call in this feature (Editor-only, via Unity's own idiomatic
    /// <see cref="TypeCache"/> API -- AIBT.Mcp's asmdef already restricts includePlatforms to
    /// Editor, so this never reaches player/runtime code), while <see cref="Build"/> is pure and
    /// independently testable without needing TypeCache in a test.
    /// </summary>
    internal static class CustomMcpToolProviderDiscovery
    {
        /// <summary>Bare tool names already used by a built-in AIBT MCP tool (McpToolDispatcher's own
        /// switch keys, mirroring MCP~/Server/'s "aibt_"-prefixed static tool names minus the prefix
        /// stripped, plus the discovery-tool bridge keys directly). A custom tool colliding with one
        /// of these is rejected -- it would otherwise be ambiguous which handler actually owns it.</summary>
        private static readonly HashSet<string> ReservedToolNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_project_manifest", "search_nodes", "get_node_contract", "get_static_resource",
            "create_tree", "add_node", "remove_node", "move_node", "replace_node", "configure_node",
            "set_blackboard_keys", "extract_subtree", "inline_subtree", "apply_domain_patch", "request_layout",
            "validate", "compile", "simulate", "explain_diagnostic",
            "run_tests", "run_benchmark",
            "generate_node", "preview_node_diff", "generate_node_tests_and_manifest",
            "analyze_and_compile_node", "test_node", "apply_node",
            "list_custom_tools", "call_custom_tool",
        };

        internal readonly struct BuildResult
        {
            internal BuildResult(IReadOnlyDictionary<string, ICustomMcpToolProvider> byToolName, DiagnosticCollection diagnostics)
            {
                ByToolName = byToolName;
                Diagnostics = diagnostics;
            }

            internal IReadOnlyDictionary<string, ICustomMcpToolProvider> ByToolName { get; }

            internal DiagnosticCollection Diagnostics { get; }
        }

        internal static IReadOnlyList<ICustomMcpToolProvider> DiscoverViaTypeCache()
        {
            var providers = new List<ICustomMcpToolProvider>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<ICustomMcpToolProvider>())
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
                    providers.Add((ICustomMcpToolProvider)Activator.CreateInstance(type));
                }
                catch (Exception)
                {
                    // Recorded as a diagnostic by Build(), not swallowed silently -- but discovery
                    // itself must not throw for one bad provider and take every other one down with it.
                }
            }

            return providers;
        }

        internal static BuildResult Build(IEnumerable<ICustomMcpToolProvider> providers)
        {
            var diagnostics = new List<Diagnostic>();
            var byToolName = new Dictionary<string, ICustomMcpToolProvider>(StringComparer.Ordinal);

            foreach (var provider in providers.OrderBy(p => p.ToolName, StringComparer.Ordinal))
            {
                if (provider == null)
                {
                    continue;
                }

                var toolName = provider.ToolName;
                if (string.IsNullOrWhiteSpace(toolName))
                {
                    diagnostics.Add(new Diagnostic(
                        McpCustomToolsDiagnostics.ProviderInstantiationFailed,
                        DiagnosticSeverity.Error,
                        "A custom MCP tool provider (" + provider.GetType().FullName + ") declared a null or empty ToolName."));
                    continue;
                }

                if (ReservedToolNames.Contains(toolName))
                {
                    diagnostics.Add(new Diagnostic(
                        McpCustomToolsDiagnostics.ReservedToolName,
                        DiagnosticSeverity.Error,
                        "Custom tool '" + toolName + "' (" + provider.GetType().FullName + ") collides with a built-in AIBT MCP tool name."));
                    continue;
                }

                if (byToolName.ContainsKey(toolName))
                {
                    diagnostics.Add(new Diagnostic(
                        McpCustomToolsDiagnostics.DuplicateToolName,
                        DiagnosticSeverity.Error,
                        "Custom tool name '" + toolName + "' is registered by more than one provider."));
                    continue;
                }

                byToolName.Add(toolName, provider);
            }

            return new BuildResult(byToolName, new DiagnosticCollection(diagnostics));
        }

        internal static string OwningAssemblyName(ICustomMcpToolProvider provider)
        {
            return provider.GetType().Assembly.GetName().Name;
        }
    }
}
