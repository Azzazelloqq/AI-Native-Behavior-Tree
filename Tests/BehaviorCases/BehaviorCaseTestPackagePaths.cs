using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

namespace AIBT.Tests.BehaviorCases
{
    internal static class BehaviorCaseTestPackagePaths
    {
        internal static string Resolve(params string[] segments)
        {
            var package = PackageInfo.FindForAssembly(typeof(BehaviorCaseTestPackagePaths).Assembly);
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
