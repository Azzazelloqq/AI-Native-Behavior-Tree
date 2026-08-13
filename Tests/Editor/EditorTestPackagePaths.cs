using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

namespace AIBT.Tests.Editor
{
    internal static class EditorTestPackagePaths
    {
        internal static string Resolve(params string[] segments)
        {
            var package = PackageInfo.FindForAssembly(typeof(EditorTestPackagePaths).Assembly);
            var root = package == null
                ? Path.Combine(Application.dataPath, "AIBT")
                : package.resolvedPath;

            var path = root;
            for (var index = 0; index < segments.Length; index++)
                path = Path.Combine(path, segments[index]);
            return path;
        }
    }
}
