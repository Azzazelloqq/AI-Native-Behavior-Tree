using AIBT.Editor.PlayerBuild;
using NUnit.Framework;

namespace AIBT.Tests.Editor.PlayerBuild
{
    /// <summary>
    /// P9-006: the link.xml that keeps reflection-created AIBT types in a stripped Player. The
    /// Player run itself is recorded in Planning~/Evidence/P9-006.
    /// </summary>
    public sealed class AibtLinkXmlProcessorTests
    {
        [Test]
        public void PreservesEveryManagedNodeClassAndGeneratedCatalogOfThePlayerAssemblies()
        {
            var xml = AibtLinkXmlProcessor.Generate(new[] { "AIBT.Agents", "AIBT.Consumer.Catalog" });

            Assert.That(xml, Does.StartWith("<linker>"));
            Assert.That(xml, Does.Contain("<assembly fullname=\"AIBT.Agents\">"));
            Assert.That(xml, Does.Contain("<type fullname=\"AIBT.Nodes.MoveTowardsNode\" preserve=\"all\"/>"));
            Assert.That(xml, Does.Contain("<assembly fullname=\"AIBT.Consumer.Catalog\">"));
            Assert.That(xml, Does.Contain("<type fullname=\"AIBT.Tests.Consumer.ConsumerCatalog\" preserve=\"all\"/>"));
            Assert.That(xml, Does.Not.Contain("\"AIBT.BtAction\""), "Abstract bases are not listed.");
        }

        [Test]
        public void SkipsAssembliesThatAreNotPartOfThePlayer()
        {
            var xml = AibtLinkXmlProcessor.Generate(new[] { "AIBT.Agents" });

            Assert.That(xml, Does.Not.Contain("AIBT.Consumer.Catalog"));
            Assert.That(xml, Does.Not.Contain("AIBT.Editor.Tests"), "Node classes declared in test assemblies are never listed.");
        }
    }
}
