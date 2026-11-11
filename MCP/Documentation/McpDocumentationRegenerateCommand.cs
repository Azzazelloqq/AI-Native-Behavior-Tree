using System.IO;
using AIBT.Authoring;
using UnityEditor;
using UnityEngine;

namespace AIBT.Mcp.Documentation
{
    /// <summary>
    /// Explicitly writes project-aware agent documentation under Assets/AIBT-Documentation.
    /// Installed package caches remain untouched; canonical package baselines are separate.
    /// </summary>
    internal static class McpDocumentationRegenerateCommand
    {
        [MenuItem("AIBT/MCP/Regenerate Documentation")]
        public static void Regenerate()
        {
            var registry = AIBT.Editor.Registry.ProjectNodeCatalogCache.Current.Registry;
            var directory = Path.Combine(Application.dataPath, "AIBT-Documentation");
            Directory.CreateDirectory(directory);

            File.WriteAllText(Path.Combine(directory, "node-catalog.md"), McpNodeCatalogDocumentGenerator.Generate(registry));
            File.WriteAllText(Path.Combine(directory, "workflow-guide.md"), McpWorkflowGuideGenerator.Generate());
            File.WriteAllText(Path.Combine(directory, "recipes.md"), McpRecipesDocumentGenerator.Generate());
            File.WriteAllText(Path.Combine(directory, "anti-patterns.md"), McpAntiPatternsDocumentGenerator.Generate());
            File.WriteAllText(Path.Combine(directory, "migrations.md"), McpMigrationsDocumentGenerator.Generate());

            var apiReference = McpApiReferenceGenerator.Generate();
            File.WriteAllText(Path.Combine(directory, "api-reference-runtime.md"), apiReference["AIBT.Runtime"]);
            File.WriteAllText(Path.Combine(directory, "api-reference-authoring.md"), apiReference["AIBT.Authoring"]);
            File.WriteAllText(Path.Combine(directory, "api-reference-editor.md"), apiReference["AIBT.Editor"]);
            File.WriteAllText(Path.Combine(directory, "api-reference-mcp.md"), apiReference["AIBT.Mcp"]);

            Debug.Log("AIBT generated documentation written to " + directory);
        }
    }
}
