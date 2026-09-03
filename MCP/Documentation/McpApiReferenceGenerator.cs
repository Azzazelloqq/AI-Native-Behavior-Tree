using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AIBT.Mcp.Documentation
{
    /// <summary>
    /// Generates a full, always-in-sync reference for AIBT's public C# surface (`P7-014`) --
    /// every public type/field/constructor/method/property in all four assemblies gets its own
    /// entry, satisfying the card's own "100% of public members have a generated reference entry"
    /// acceptance bar directly, without requiring XML-doc prose to exist first (current real
    /// coverage is ~2.4-4.5% of members, confirmed by source-parse before this generator was
    /// written -- see `Planning~/Evidence/P7-014/README.md`). Signatures come from live reflection
    /// (`AppDomain.CurrentDomain.GetAssemblies()`, mirroring `Tools~/Verification/P7/Audit/
    /// PublicApiDump.cs.txt`'s own `TypeDisplayName`/`MethodSignature` formatting so this document's
    /// signatures read identically to the already-established `public-api.txt` convention) --
    /// reflection cannot recover doc-comment text (Unity emits no `.xml` doc file for any assembly
    /// here, confirmed directly), so a type's own `&lt;summary&gt;`, where one exists in source, is
    /// inlined via a best-effort source-parse keyed on exact type `FullName` -- member-level
    /// correlation across overloads/generics was investigated and found too fragile to attempt this
    /// pass, disclosed rather than silently attempted and possibly wrong.
    /// </summary>
    internal static class McpApiReferenceGenerator
    {
        private static readonly string[] AssemblyFolders = { "Runtime", "Authoring", "Editor", "MCP" };
        private static readonly string[] AssemblyNames = { "AIBT.Runtime", "AIBT.Authoring", "AIBT.Editor", "AIBT.Mcp" };

        // (namespace, folder) -- MCP/'s own real assembly name is "AIBT.Mcp" but its source folder
        // is "MCP", so folder-name and assembly-name are looped in lockstep by index below rather
        // than assumed to match.
        internal static IReadOnlyDictionary<string, string> Generate()
        {
            var summaries = CollectTypeSummaries();
            var result = new Dictionary<string, string>(4);
            for (var index = 0; index < AssemblyNames.Length; index++)
            {
                result[AssemblyNames[index]] = GenerateForAssembly(AssemblyNames[index], summaries);
            }

            return result;
        }

        private static string GenerateForAssembly(string assemblyName, IReadOnlyDictionary<string, string> summaries)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName);
            var builder = new StringBuilder();
            builder.Append("# ").Append(assemblyName).Append(" -- public API reference (generated)\n\n");
            builder.Append("Source: live reflection over `").Append(assemblyName).Append("`'s own compiled public surface (`P7-014`). ");
            builder.Append("Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command. Do not hand-edit -- edits are overwritten on the next regeneration.\n\n");
            builder.Append("A type's own summary line is shown where an XML-doc `<summary>` exists in source; member-level doc-comment text is not yet correlated here (see this document's own generator comment for why) -- every member still gets its own full signature line regardless of whether prose exists for it.\n");

            if (assembly == null)
            {
                builder.Append("\n_Assembly not loaded in this AppDomain._\n");
                return builder.ToString().Replace("\r\n", "\n");
            }

            var publicTypes = assembly.GetExportedTypes().Where(t => t.IsPublic)
                .OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
            builder.Append(publicTypes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" public type(s).\n");

            foreach (var type in publicTypes)
            {
                builder.Append("\n---\n\n### `").Append(type.FullName).Append("`\n\n");
                if (summaries.TryGetValue(type.FullName, out var summary))
                {
                    builder.Append(summary).Append("\n\n");
                }

                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly;

                var memberLines = new List<string>();
                foreach (var field in type.GetFields(flags))
                {
                    if (field.Name.EndsWith(">k__BackingField", StringComparison.Ordinal)) continue;
                    memberLines.Add("- `FIELD " + TypeDisplayName(field.FieldType) + " " + field.Name + "`");
                }
                foreach (var ctor in type.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    memberLines.Add("- `" + MethodSignature(ctor) + "`");
                }
                foreach (var method in type.GetMethods(flags))
                {
                    if (method.IsSpecialName) continue;
                    memberLines.Add("- `" + MethodSignature(method) + "`");
                }
                foreach (var property in type.GetProperties(flags))
                {
                    memberLines.Add("- `PROPERTY " + TypeDisplayName(property.PropertyType) + " " + property.Name + "`");
                }

                if (memberLines.Count == 0)
                {
                    builder.Append("_No public members declared directly on this type._\n");
                    continue;
                }

                foreach (var line in memberLines.Distinct().OrderBy(s => s, StringComparer.Ordinal))
                {
                    builder.Append(line).Append('\n');
                }
            }

            return builder.ToString().Replace("\r\n", "\n");
        }

        // Mirrors Tools~/Verification/P7/Audit/PublicApiDump.cs.txt's own formatting exactly, so
        // this document's signature lines are byte-identical in shape to the already-established
        // public-api.txt convention (and so the coverage-check test can compare them directly).
        private static string TypeDisplayName(Type type)
        {
            if (type.IsGenericType)
            {
                var name = type.GetGenericTypeDefinition().FullName;
                var args = string.Join(",", type.GetGenericArguments().Select(TypeDisplayName));
                return name + "<" + args + ">";
            }
            return type.FullName;
        }

        private static string MethodSignature(System.Reflection.MethodBase method)
        {
            var name = method.IsConstructor ? ".ctor" : method.Name;
            var parameters = string.Join(",", method.GetParameters().Select(p => TypeDisplayName(p.ParameterType)));
            var returnType = method is System.Reflection.MethodInfo methodInfo ? TypeDisplayName(methodInfo.ReturnType) : "System.Void";
            return "METHOD " + returnType + " " + name + "(" + parameters + ")";
        }

        // Best-effort only (see this file's own class-level doc comment): a public type's own
        // FullName -> its XML-doc <summary> text, where one immediately precedes the type
        // declaration in source. Never attempted for members (overload/generic-argument matching
        // against reflected MethodInfo would be materially more fragile than this exact-FullName
        // type lookup).
        internal static Dictionary<string, string> CollectTypeSummaries()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            // P7-021: mirrors Tests/Editor/Documentation/McpDocumentationGeneratorsTests.cs's own
            // already-correct FindGeneratedDocumentationDirectory() pattern. Application.dataPath +
            // "AIBT" only resolves when this package is embedded directly under a host project's
            // Assets/ (this repo's own dev setup) -- for any real file:/registry UPM consumer the
            // package lives under Packages/ instead, so that path silently doesn't exist and this
            // method used to return nothing with zero error (found live by P7-016's own
            // detached-harness gate regression).
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(McpApiReferenceGenerator).Assembly);
            var aibtRoot = packageInfo != null ? packageInfo.resolvedPath : Path.Combine(Application.dataPath, "AIBT");
            var namespacePattern = new Regex(@"namespace\s+([\w.]+)", RegexOptions.Compiled);
            var summaryPattern = new Regex(
                @"((?:^[ \t]*///.*\r?\n)+)[ \t]*(?:\[[^\]]*\]\s*\r?\n[ \t]*)*public\s+(?:sealed\s+|abstract\s+|static\s+|readonly\s+|partial\s+)*(?:class|struct|interface|enum|record)\s+(\w+)",
                RegexOptions.Compiled | RegexOptions.Multiline);

            foreach (var folder in AssemblyFolders)
            {
                var folderPath = Path.Combine(aibtRoot, folder);
                if (!Directory.Exists(folderPath)) continue;

                foreach (var file in Directory.GetFiles(folderPath, "*.cs", SearchOption.AllDirectories))
                {
                    string text;
                    try { text = File.ReadAllText(file); }
                    catch (IOException) { continue; }

                    var namespaceMatch = namespacePattern.Match(text);
                    if (!namespaceMatch.Success) continue;
                    var ns = namespaceMatch.Groups[1].Value;

                    foreach (Match match in summaryPattern.Matches(text))
                    {
                        var commentBlock = match.Groups[1].Value;
                        var typeName = match.Groups[2].Value;
                        var summaryText = ExtractSummaryText(commentBlock);
                        if (string.IsNullOrEmpty(summaryText)) continue;

                        var fullName = ns + "." + typeName;
                        result[fullName] = summaryText;
                    }
                }
            }

            return result;
        }

        private static string ExtractSummaryText(string commentBlock)
        {
            var lines = commentBlock.Split('\n')
                .Select(line => line.TrimStart(' ', '\t').TrimStart('/').Trim())
                .ToList();
            var joined = string.Join(" ", lines);
            var summaryMatch = Regex.Match(joined, @"<summary>\s*(.*?)\s*</summary>", RegexOptions.Singleline);
            if (!summaryMatch.Success) return null;
            var value = Regex.Replace(summaryMatch.Groups[1].Value, @"\s+", " ").Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
