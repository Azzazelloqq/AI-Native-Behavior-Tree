using System.Linq;
using AIBT.Authoring;
using AIBT.Tests.Editor.NodeRegistry;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Discovery
{
    public sealed class NodeCatalogQueryTests
    {
        [Test]
        public void SearchIsCaseInsensitiveAndDeterministicAcrossRepeatedCalls()
        {
            var registry = BuildRegistry(
                NodeManifestTestFactory.Create("example.discovery.zebra"),
                NodeManifestTestFactory.Create("example.discovery.alpha"));
            var query = new NodeCatalogQuery(registry);

            var first = query.Search("DISCOVERY").Select(e => e.Manifest.TypeId).ToArray();
            var second = query.Search("discovery").Select(e => e.Manifest.TypeId).ToArray();

            Assert.That(first, Is.EqualTo(new[] { "example.discovery.alpha", "example.discovery.zebra" }),
                "Results are ordered by TypeId regardless of registration order.");
            Assert.That(second, Is.EqualTo(first), "Repeated searches with different casing return the same order.");
        }

        [Test]
        public void SearchMatchesCategoryAndSummaryToo()
        {
            var registry = BuildRegistry(NodeManifestTestFactory.Create("example.discovery.only"));
            var query = new NodeCatalogQuery(registry);

            Assert.That(query.Search("Test").Count, Is.EqualTo(1), "NodeManifestTestFactory sets Category to 'Test'.");
            Assert.That(query.Search("no-such-keyword"), Is.Empty);
        }

        [Test]
        public void PageReturnsDeterministicSlicesAndEmptyPastTheEnd()
        {
            var registry = BuildRegistry(
                NodeManifestTestFactory.Create("example.discovery.b"),
                NodeManifestTestFactory.Create("example.discovery.a"),
                NodeManifestTestFactory.Create("example.discovery.c"));
            var query = new NodeCatalogQuery(registry);

            var firstPage = query.Page(0, 2).Select(e => e.Manifest.TypeId).ToArray();
            var secondPage = query.Page(2, 2).Select(e => e.Manifest.TypeId).ToArray();
            var pastEnd = query.Page(10, 2);

            Assert.That(firstPage, Is.EqualTo(new[] { "example.discovery.a", "example.discovery.b" }));
            Assert.That(secondPage, Is.EqualTo(new[] { "example.discovery.c" }));
            Assert.That(pastEnd, Is.Empty);
        }

        [Test]
        public void TryGetContractDelegatesToNodeManifestCanonicalJson()
        {
            var manifest = NodeManifestTestFactory.Create("example.discovery.contract");
            var registry = BuildRegistry(manifest);
            var query = new NodeCatalogQuery(registry);

            var found = query.TryGetContract("example.discovery.contract", out var contractJson);
            var notFound = query.TryGetContract("example.discovery.missing", out var missingJson);

            Assert.That(found, Is.True);
            Assert.That(contractJson.ToString(), Is.EqualTo(NodeManifestCanonicalJson.ToJson(manifest).ToString()),
                "Discovery must format via NodeManifestCanonicalJson directly, never a second formatter.");
            Assert.That(notFound, Is.False);
            Assert.That(missingJson, Is.Null);
        }

        [Test]
        public void NewlyRegisteredFixtureNodeAppearsWithoutAnyCodeChangeHere()
        {
            // The card's own acceptance criterion: catalog output is generated from the registry,
            // never hand-authored. Registering one more node and rebuilding is the whole test.
            var registryWithOne = BuildRegistry(NodeManifestTestFactory.Create("example.discovery.first"));
            var registryWithTwo = BuildRegistry(
                NodeManifestTestFactory.Create("example.discovery.first"),
                NodeManifestTestFactory.Create("example.discovery.second"));

            Assert.That(new NodeCatalogQuery(registryWithOne).Count, Is.EqualTo(1));
            Assert.That(new NodeCatalogQuery(registryWithTwo).Count, Is.EqualTo(2));
        }

        private static AIBT.Authoring.NodeRegistry BuildRegistry(params NodeManifest[] extensions)
        {
            var builder = new NodeRegistryBuilder();
            foreach (var manifest in extensions)
            {
                builder.AddUserExtension(manifest);
            }

            var result = builder.Build();
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            return result.Registry;
        }
    }
}
