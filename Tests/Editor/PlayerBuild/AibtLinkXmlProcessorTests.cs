using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using AIBT.Burst;
using AIBT.Editor.PlayerBuild;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;

namespace AIBT.Tests.Editor.PlayerBuild
{
    /// <summary>
    /// P9-006: the link.xml that keeps reflection-created AIBT types in a stripped Player. The
    /// Player run itself is recorded in Planning~/Evidence/P9-006. P11-016 (ADR-P9-004 amendment
    /// 2026-11-24): the <c>AIBT.Agents</c> element carries <c>ignoreIfUnreferenced="1"</c> unless the
    /// override is set, and the override's output equals the output before P11-016 byte for byte.
    /// </summary>
    public sealed class AibtLinkXmlProcessorTests
    {
        private const string AgentsElement = "<assembly fullname=\"AIBT.Agents\">";
        private const string AgentsElementIgnored = "<assembly fullname=\"AIBT.Agents\" ignoreIfUnreferenced=\"1\">";

        [Test]
        public void PreservesEveryManagedNodeClassAndGeneratedCatalogOfThePlayerAssemblies()
        {
            var xml = AibtLinkXmlProcessor.Generate(new[] { "AIBT.Agents", "AIBT.Consumer.Catalog" });

            Assert.That(xml, Does.StartWith("<linker>"));
            Assert.That(xml, Does.Contain(AgentsElementIgnored));
            Assert.That(xml, Does.Contain("<type fullname=\"AIBT.Nodes.MoveTowardsNode\" preserve=\"all\"/>"));
            Assert.That(xml, Does.Contain("<assembly fullname=\"AIBT.Consumer.Catalog\">"));
            Assert.That(xml, Does.Contain("<type fullname=\"AIBT.Tests.Consumer.ConsumerCatalog\" preserve=\"all\"/>"));
            Assert.That(xml, Does.Not.Contain("\"AIBT.BtAction\""), "Abstract bases are not listed.");
        }

        [Test]
        public void PreservesRegisteredValueTypesAndTheAttributesTheSimulationContainerReads()
        {
            // P10-010: TreeGroup.TryBindInput/TryGetCommands identify a payload type by its
            // [AibtBurstValue] and [AibtValueField] attributes, which High stripping removed.
            var xml = AibtLinkXmlProcessor.Generate(new[] { "AIBT.Runtime", "AIBT.CodeGen.P10010SimulationIo.Tests" });

            Assert.That(xml, Does.Contain("<assembly fullname=\"AIBT.Runtime\">"));
            Assert.That(xml, Does.Contain("<type fullname=\"AIBT.Burst.AibtBurstValueAttribute\" preserve=\"all\"/>"));
            Assert.That(xml, Does.Contain("<type fullname=\"AIBT.Burst.AibtValueFieldAttribute\" preserve=\"all\"/>"));
            Assert.That(xml, Does.Contain("<type fullname=\"AIBT.Tests.CodeGen.GenerationP10010.SenseInput\" preserve=\"all\"/>"));
            Assert.That(xml, Does.Contain("<type fullname=\"AIBT.Tests.CodeGen.GenerationP10010.OrderCommand\" preserve=\"all\"/>"));
        }

        [Test]
        public void SkipsAssembliesThatAreNotPartOfThePlayer()
        {
            var xml = AibtLinkXmlProcessor.Generate(new[] { "AIBT.Agents" });

            Assert.That(xml, Does.Not.Contain("AIBT.Consumer.Catalog"));
            Assert.That(xml, Does.Not.Contain("AIBT.Editor.Tests"), "Node classes declared in test assemblies are never listed.");
        }

        [TestCaseSource(nameof(PlayerSets))]
        public void TheOverrideWritesTheOutputBeforeP11016_ByteForByte(string[] players)
        {
            var today = Encoding.UTF8.GetBytes(OutputBeforeP11016(players));
            var overridden = Encoding.UTF8.GetBytes(AibtLinkXmlProcessor.Generate(players, keepManagedNodes: true));

            Assert.That(overridden, Is.EqualTo(today));
            Assert.That(AibtLinkXmlProcessor.Generate(players, keepManagedNodes: false),
                Is.EqualTo(AibtLinkXmlProcessor.Generate(players)), "Without the override is the default.");
        }

        [TestCaseSource(nameof(PlayerSets))]
        public void TheDefaultDiffersFromTheOutputBeforeP11016_OnlyInTheAgentsElement(string[] players)
        {
            var today = OutputBeforeP11016(players);
            var xml = AibtLinkXmlProcessor.Generate(players);

            if (players.Contains("AIBT.Agents"))
            {
                Assert.That(Count(today, AgentsElement), Is.EqualTo(1));
                Assert.That(Count(xml, AgentsElementIgnored), Is.EqualTo(1));
                Assert.That(Count(xml, "ignoreIfUnreferenced"), Is.EqualTo(1), "No other element is conditional.");
                Assert.That(xml, Is.EqualTo(today.Replace(AgentsElement, AgentsElementIgnored)));
            }
            else
            {
                Assert.That(xml, Is.EqualTo(today), "Without AIBT.Agents in the Player the output is unchanged.");
            }
        }

        [Test]
        public void ProjectNodeClassesStayUnconditional()
        {
            // A project's own [BtNode] class in a game assembly and its catalog keep an unconditional row.
            var players = new[] { "AIBT.Agents", "AIBT.Consumer.Catalog", "AIBT.Runtime", "AIBT.Authoring" };
            var projectNodes = TypeCache.GetTypesWithAttribute<BtNodeAttribute>()
                .Where(type => !type.IsAbstract && typeof(BtNode).IsAssignableFrom(type) && type.Assembly.GetName().Name != "AIBT.Agents")
                .Select(type => type.Assembly.GetName().Name)
                .Distinct().ToArray();
            var xml = AibtLinkXmlProcessor.Generate(players.Concat(projectNodes).ToArray());

            foreach (var assembly in projectNodes)
                Assert.That(xml, Does.Contain("<assembly fullname=\"" + SecurityElement.Escape(assembly) + "\">"), assembly);
            Assert.That(xml, Does.Contain("<assembly fullname=\"AIBT.Consumer.Catalog\">"));
            Assert.That(xml, Does.Contain("<assembly fullname=\"AIBT.Runtime\">"));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("AIBT_SCHEDULER_FRAME_PROFILE", false)]
        [TestCase("AIBT_LINKXML_KEEP_MANAGED_NODES", true)]
        [TestCase("A;AIBT_LINKXML_KEEP_MANAGED_NODES;B", true)]
        [TestCase("A, AIBT_LINKXML_KEEP_MANAGED_NODES", true)]
        [TestCase("AIBT_LINKXML_KEEP_MANAGED_NODES_X;XAIBT_LINKXML_KEEP_MANAGED_NODES", false)]
        public void TheOverrideIsTheScriptingDefine(string defines, bool expected)
        {
            Assert.That(AibtLinkXmlProcessor.KeepManagedNodesDefine, Is.EqualTo("AIBT_LINKXML_KEEP_MANAGED_NODES"));
            Assert.That(AibtLinkXmlProcessor.HasKeepManagedNodesDefine(defines), Is.EqualTo(expected));
        }

        private static IEnumerable<string[]> PlayerSets()
        {
            yield return new[] { "AIBT.Agents" };
            yield return new[] { "AIBT.Runtime" };
            yield return new[] { "AIBT.Agents", "AIBT.Consumer.Catalog" };
            yield return new[] { "AIBT.Agents", "AIBT.Runtime", "AIBT.Authoring", "AIBT.CodeGen.P10010SimulationIo.Tests", "AIBT.Consumer.Catalog" };
            yield return CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(assembly => assembly.name).ToArray();
        }

        private static int Count(string text, string part)
        {
            var count = 0;
            for (var index = text.IndexOf(part, StringComparison.Ordinal); index >= 0; index = text.IndexOf(part, index + part.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        // The generator as it was before P11-016 (commit 24de026), kept verbatim as the reference of the override.
        private static string OutputBeforeP11016(ICollection<string> playerAssemblies)
        {
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
                builder.Append("  <assembly fullname=\"").Append(SecurityElement.Escape(assembly.Key)).Append("\">\n");
                foreach (var name in assembly.Select(type => type.FullName.Replace('+', '/')).OrderBy(name => name, StringComparer.Ordinal))
                    builder.Append("    <type fullname=\"").Append(SecurityElement.Escape(name)).Append("\" preserve=\"all\"/>\n");
                builder.Append("  </assembly>\n");
            }

            return builder.Append("</linker>\n").ToString();
        }
    }
}
