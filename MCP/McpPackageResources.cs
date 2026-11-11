using System.IO;
using UnityEditor;
using UnityEngine;

namespace AIBT.Mcp
{
    [InitializeOnLoad]
    internal static class McpPackageResources
    {
        internal static readonly string Root;

        static McpPackageResources()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(McpPackageResources).Assembly);
            Root = package != null ? package.resolvedPath : Path.Combine(Application.dataPath, "AIBT");
        }
    }
}
