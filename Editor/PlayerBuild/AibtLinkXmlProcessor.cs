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
    /// and every <c>[AibtCatalogSet]</c> catalog in the Player's assemblies, and (P10-010) for every
    /// <c>[AibtBurstValue]</c> value type with the attribute types the simulation container reads.
    /// P11-016 (ADR-P9-004 amendment 2026-11-24): the rows of the package's managed nodes
    /// (<c>AIBT.Agents</c>) apply only when the Player references that assembly
    /// (<c>ignoreIfUnreferenced="1"</c>); the scripting define <see cref="KeepManagedNodesDefine"/>
    /// restores the previous output.
    /// </summary>
    internal sealed class AibtLinkXmlProcessor : IUnityLinkerProcessor
    {
        /// <summary>The Player scripting define that keeps the managed node rows unconditional, as before P11-016.</summary>
        internal const string KeepManagedNodesDefine = "AIBT_LINKXML_KEEP_MANAGED_NODES";

        private const string ManagedNodesAssembly = "AIBT.Agents";

        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var directory = Path.GetFullPath(Path.Combine("Temp", "AIBT"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "aibt-preserve.link.xml");
            File.WriteAllText(path, Generate(PlayerAssemblyNames(), KeepManagedNodes(report)));
            return path;
        }

        /// <summary>The link.xml text for the given Player assembly names, without the override.</summary>
        internal static string Generate(ICollection<string> playerAssemblies) => Generate(playerAssemblies, false);

        /// <summary>
        /// The link.xml text for the given Player assembly names. Without <paramref name="keepManagedNodes"/>
        /// the <c>AIBT.Agents</c> element carries <c>ignoreIfUnreferenced="1"</c>; with it the text is the
        /// output before P11-016.
        /// </summary>
        internal static string Generate(ICollection<string> playerAssemblies, bool keepManagedNodes)
        {
            // P10-010: the simulation container matches a game payload type to its catalog value by
            // reading [AibtBurstValue] and [AibtValueField] through reflection (TreeGroup.TryBindInput,
            // TryGetCommands). Stripping removed both attributes, so the registered value types and the
            // two attribute types are kept as well.
            var types = TypeCache.GetTypesWithAttribute<BtNodeAttribute>()
                .Where(type => !type.IsAbstract && typeof(BtNode).IsAssignableFrom(type))
                .Concat(TypeCache.GetTypesWithAttribute<AibtCatalogSetAttribute>())
                .Concat(TypeCache.GetTypesWithAttribute<AibtBurstValueAttribute>())
                .Concat(new[] { typeof(AibtBurstValueAttribute), typeof(AibtValueFieldAttribute) })
                .Where(type => playerAssemblies.Contains(type.Assembly.GetName().Name))
                .Distinct()
                .GroupBy(type => type.Assembly.GetName().Name)
                .OrderBy(group => group.Key, StringComparer.Ordinal);

            var builder = new StringBuilder("<linker>\n");
            foreach (var assembly in types)
            {
                builder.Append("  <assembly fullname=\"").Append(SecurityElement.Escape(assembly.Key)).Append('"');
                if (!keepManagedNodes && assembly.Key == ManagedNodesAssembly) builder.Append(" ignoreIfUnreferenced=\"1\"");
                builder.Append(">\n");
                foreach (var name in assembly.Select(type => type.FullName.Replace('+', '/')).OrderBy(name => name, StringComparer.Ordinal))
                    builder.Append("    <type fullname=\"").Append(SecurityElement.Escape(name)).Append("\" preserve=\"all\"/>\n");
                builder.Append("  </assembly>\n");
            }

            return builder.Append("</linker>\n").ToString();
        }

        /// <summary>True when the scripting define list (separated by ';', ',' or spaces) holds <see cref="KeepManagedNodesDefine"/>.</summary>
        internal static bool HasKeepManagedNodesDefine(string defines)
            => !string.IsNullOrEmpty(defines)
               && defines.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                   .Any(define => string.Equals(define.Trim(), KeepManagedNodesDefine, StringComparison.Ordinal));

        private static bool KeepManagedNodes(BuildReport report)
        {
            var group = BuildPipeline.GetBuildTargetGroup(report.summary.platform);
            if (group == BuildTargetGroup.Unknown) return false;
            var target = group == BuildTargetGroup.Standalone
                         && report.summary.GetSubtarget<StandaloneBuildSubtarget>() == StandaloneBuildSubtarget.Server
                ? NamedBuildTarget.Server
                : NamedBuildTarget.FromBuildTargetGroup(group);
            return HasKeepManagedNodesDefine(PlayerSettings.GetScriptingDefineSymbols(target));
        }

        private static HashSet<string> PlayerAssemblyNames()
        {
            var names = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(assembly => assembly.name), StringComparer.Ordinal);
            return names;
        }
    }
}
