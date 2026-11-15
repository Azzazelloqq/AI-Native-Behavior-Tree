using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using AIBT.Burst;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.UnityLinker;

namespace AIBT.Editor.PlayerBuild
{
    /// <summary>
    /// P9-006: keeps the types AIBT creates by reflection in a Player built with managed code
    /// stripping. <see cref="BehaviorTreeAgent"/> discovers <c>[BtNode]</c> classes, instantiates them
    /// and assigns their fields by reflection, and <see cref="NativeTreeAgent"/> resolves the catalog
    /// recorded in a runtime asset by type name; nothing references those types statically, so
    /// UnityLinker removes them. This writes an additional link.xml for every <c>[BtNode]</c> class
    /// and every <c>[AibtCatalogSet]</c> catalog in the Player's assemblies.
    /// </summary>
    internal sealed class AibtLinkXmlProcessor : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var directory = Path.GetFullPath(Path.Combine("Temp", "AIBT"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "aibt-preserve.link.xml");
            File.WriteAllText(path, Generate(PlayerAssemblyNames()));
            return path;
        }

        /// <summary>The link.xml text for the given Player assembly names.</summary>
        internal static string Generate(ICollection<string> playerAssemblies)
        {
            var types = TypeCache.GetTypesWithAttribute<BtNodeAttribute>()
                .Where(type => !type.IsAbstract && typeof(BtNode).IsAssignableFrom(type))
                .Concat(TypeCache.GetTypesWithAttribute<AibtCatalogSetAttribute>())
                .Where(type => playerAssemblies.Contains(type.Assembly.GetName().Name))
                .Distinct()
                .GroupBy(type => type.Assembly.GetName().Name)
                .OrderBy(group => group.Key, StringComparer.Ordinal);

            var builder = new StringBuilder("<linker>\n");
            foreach (var assembly in types)
            {
                builder.Append("  <assembly fullname=\"").Append(SecurityElement.Escape(assembly.Key)).Append("\">\n");
                foreach (var name in assembly.Select(type => type.FullName.Replace('+', '/')).OrderBy(name => name, StringComparer.Ordinal))
                    builder.Append("    <type fullname=\"").Append(SecurityElement.Escape(name)).Append("\" preserve=\"all\"/>\n");
                builder.Append("  </assembly>\n");
            }

            return builder.Append("</linker>\n").ToString();
        }

        private static HashSet<string> PlayerAssemblyNames()
        {
            var names = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(assembly => assembly.name), StringComparer.Ordinal);
            return names;
        }
    }
}
