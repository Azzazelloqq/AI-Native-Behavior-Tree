using System.IO;
using AIBT.Authoring;
using UnityEditor;
using UnityEngine;

namespace AIBT.Mcp.Documentation
{
    /// <summary>
    /// Explicit, opt-in regeneration of the P6-011 generated agent documentation set -- mirrors
    /// McpBridgeWindow/HotReloadWorkflowWindow's own "never automatic" pattern. Writes into
    /// Documentation~/generated/, committed as static generated artifacts; a dedicated drift test
    /// (Tests/Editor/Documentation/) proves the committed files match a fresh in-memory
    /// regeneration, so a forgotten "regenerate" step after a registry/tool change is caught rather
    /// than silently going stale.
    /// </summary>
    internal static class McpDocumentationRegenerateCommand
    {
        [MenuItem("AIBT/MCP/Regenerate Documentation")]
        public static void Regenerate()
        {
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var directory = Path.Combine(Application.dataPath, "AIBT", "Documentation~", "generated");
            Directory.CreateDirectory(directory);

            File.WriteAllText(Path.Combine(directory, "node-catalog.md"), McpNodeCatalogDocumentGenerator.Generate(registry));
            File.WriteAllText(Path.Combine(directory, "workflow-guide.md"), McpWorkflowGuideGenerator.Generate());
            File.WriteAllText(Path.Combine(directory, "recipes.md"), McpRecipesDocumentGenerator.Generate());
            File.WriteAllText(Path.Combine(directory, "anti-patterns.md"), McpAntiPatternsDocumentGenerator.Generate());
            File.WriteAllText(Path.Combine(directory, "migrations.md"), McpMigrationsDocumentGenerator.Generate());

            Debug.Log("AIBT generated documentation written to " + directory);
        }
    }
}
