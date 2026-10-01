using System;
using System.IO;
using System.Linq;

namespace AIBT.Authoring
{
    /// <summary>Source-container names only; both extensions contain the same canonical JSON.</summary>
    public static class TreeSourcePaths
    {
        public const string Extension = ".aibt";
        public const string LegacyExtension = ".aibt.json";

        public static bool IsTree(string path) => !string.IsNullOrEmpty(path)
            && (path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase));

        public static string LayoutPathFor(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (!IsTree(path)) throw new ArgumentException("Expected .aibt or .aibt.json.", nameof(path));
            var suffix = path.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase) ? LegacyExtension : Extension;
            return path.Substring(0, path.Length - suffix.Length) + ".aibt.layout.json";
        }

        public static string[] FindFiles(string root) => Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(IsTree)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
    }
}
