using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AIBT.Mcp.NodeDevelopment
{
    /// <summary>
    /// The single reserved staging location a project-relative <c>generate-node</c> call writes
    /// into: <c>Assets/AIBT-Generated/_Staging/Pending/</c>. One slot, not one folder per call --
    /// a fresh <c>generate-node</c> overwrites whatever was pending, mirroring this project's own
    /// "one pending mutation at a time" domain-patch discipline. Every file here is quarantined:
    /// never referenced by the real node registry until <see cref="McpNodeDevelopmentToolDispatcher.ApplyNode"/>
    /// moves it out.
    /// </summary>
    internal static class StagingSlot
    {
        private const string RelativeRoot = "AIBT-Generated/_Staging/Pending";
        private const string AnalyzerGuid = "31a3f09584684895a0a72916d3ad4de0";

        internal static string RootPath(string projectRoot) => Path.Combine(projectRoot, RelativeRoot.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>Clears any prior pending generation and writes a fresh node source file plus the staging asmdef (created once, left in place across generations).</summary>
        internal static string WriteNode(string projectRoot, string nodeFileName, string nodeSource)
        {
            var root = RootPath(projectRoot);
            Directory.CreateDirectory(root);
            foreach (var existing in Directory.GetFiles(root, "*.cs"))
            {
                File.Delete(existing);
            }

            EnsureAsmdef(root);
            var nodePath = Path.Combine(root, nodeFileName);
            File.WriteAllText(nodePath, nodeSource);
            return nodePath;
        }

        internal static string WriteTests(string projectRoot, string testFileName, string testSource)
        {
            var root = RootPath(projectRoot);
            Directory.CreateDirectory(root);
            var testPath = Path.Combine(root, testFileName);
            File.WriteAllText(testPath, testSource);
            return testPath;
        }

        internal static IReadOnlyList<string> ListStagedFiles(string projectRoot)
        {
            var root = RootPath(projectRoot);
            if (!Directory.Exists(root))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(root, "*.cs").OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        internal static bool TryReadStaged(string projectRoot, string fileName, out string content)
        {
            var path = Path.Combine(RootPath(projectRoot), fileName);
            if (!File.Exists(path))
            {
                content = null;
                return false;
            }

            content = File.ReadAllText(path);
            return true;
        }

        /// <summary>
        /// Moves the current staged file set to a real project location, clearing the slot. The
        /// destination folder is created fresh; it must not already exist. A fresh asmdef (with
        /// the analyzer attached, mirroring the staging slot's own) is written alongside the
        /// moved files -- a node assembly may declare exactly one shard (AIBT5011), so every
        /// applied node needs its own assembly unless the caller already placed it inside one via
        /// destinationRelativePath pointing at an existing asmdef's own folder (detected by
        /// walking up from the destination; if found, no new asmdef is written).
        /// </summary>
        internal static IReadOnlyList<string> MoveTo(string projectRoot, string destinationRelativePath)
        {
            var root = RootPath(projectRoot);
            var destination = Path.Combine(projectRoot, destinationRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(destination))
            {
                throw new InvalidOperationException("Apply destination already exists: " + destinationRelativePath);
            }

            Directory.CreateDirectory(destination);
            var moved = new List<string>();
            foreach (var file in Directory.GetFiles(root, "*.cs"))
            {
                var target = Path.Combine(destination, Path.GetFileName(file));
                File.Move(file, target);
                moved.Add(target);
            }

            if (!HasEnclosingAsmdef(projectRoot, destination))
            {
                var asmdefPath = WriteDestinationAsmdef(destination, destinationRelativePath);
                moved.Add(asmdefPath);
            }

            return moved;
        }

        private static bool HasEnclosingAsmdef(string projectRoot, string destination)
        {
            var directory = new DirectoryInfo(destination);
            var stopAt = new DirectoryInfo(projectRoot);
            while (directory != null && !string.Equals(directory.FullName, stopAt.FullName, StringComparison.OrdinalIgnoreCase))
            {
                if (directory.GetFiles("*.asmdef").Length > 0)
                {
                    return true;
                }

                directory = directory.Parent;
            }

            return false;
        }

        private static string WriteDestinationAsmdef(string destination, string destinationRelativePath)
        {
            var name = "AIBT.Generated." + destinationRelativePath.Replace('/', '.').Replace('\\', '.');
            var asmdefPath = Path.Combine(destination, SanitizeFileName(name) + ".asmdef");
            var asmdef = "{\n"
                + "  \"name\": \"" + name + "\",\n"
                + "  \"rootNamespace\": \"" + name + "\",\n"
                + "  \"references\": [\"AIBT.Runtime\"],\n"
                + "  \"includePlatforms\": [\"Editor\"],\n"
                + "  \"autoReferenced\": false,\n"
                + "  \"analyzers\": [\"GUID:" + AnalyzerGuid + "\"]\n"
                + "}\n";
            File.WriteAllText(asmdefPath, asmdef);
            return asmdefPath;
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '-');
            }

            return value;
        }

        /// <summary>Removes every staged file (used when a caller discards a pending generation without applying it).</summary>
        internal static void Clear(string projectRoot)
        {
            var root = RootPath(projectRoot);
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(root, "*.cs"))
            {
                File.Delete(file);
            }
        }

        private static void EnsureAsmdef(string root)
        {
            var asmdefPath = Path.Combine(root, "AIBT.Generated.Staging.asmdef");
            if (File.Exists(asmdefPath))
            {
                return;
            }

            var asmdef = "{\n"
                + "  \"name\": \"AIBT.Generated.Staging\",\n"
                + "  \"rootNamespace\": \"AIBT.Generated.Staging\",\n"
                + "  \"references\": [\"AIBT.Runtime\"],\n"
                + "  \"includePlatforms\": [\"Editor\"],\n"
                + "  \"autoReferenced\": false,\n"
                + "  \"analyzers\": [\"GUID:" + AnalyzerGuid + "\"]\n"
                + "}\n";
            File.WriteAllText(asmdefPath, asmdef);
        }
    }
}
