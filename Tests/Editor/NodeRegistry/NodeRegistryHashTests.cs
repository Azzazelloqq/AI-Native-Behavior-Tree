using System;
using System.Text;
using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.NodeRegistry
{
    public sealed class NodeRegistryHashTests
    {
        [Test]
        public void Hash_ChangesWhenDeterministicPackingChanges()
        {
            var tightlyPacked = new Authoring.NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.packing", configurationOffset: 0))
                .Build();
            var padded = new Authoring.NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.packing", configurationOffset: 4))
                .Build();

            Assert.That(tightlyPacked.Success, Is.True);
            Assert.That(padded.Success, Is.True);
            Assert.That(tightlyPacked.Registry.Hash, Is.Not.EqualTo(padded.Registry.Hash));
        }

        [Test]
        public void Hash_ChangesWhenConfigurationEnvelopeChanges()
        {
            var compact = new Authoring.NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.envelope", configurationSize: 1))
                .Build();
            var trailingPadding = new Authoring.NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.envelope", configurationSize: 4))
                .Build();

            Assert.That(compact.Success, Is.True);
            Assert.That(trailingPadding.Success, Is.True);
            Assert.That(compact.Registry.Hash, Is.Not.EqualTo(trailingPadding.Registry.Hash));
        }

        [Test]
        public void Hash_ChangesWhenMemoryLifetimeChanges()
        {
            var activation = new Authoring.NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create(
                    "example.nodes.activation-memory",
                    memoryLifetime: NodeMemoryLifetime.Activation))
                .Build();
            var instance = new Authoring.NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create(
                    "example.nodes.activation-memory",
                    memoryLifetime: NodeMemoryLifetime.Instance))
                .Build();

            Assert.That(activation.Success, Is.True);
            Assert.That(instance.Success, Is.True);
            Assert.That(activation.Registry.Hash, Is.Not.EqualTo(instance.Registry.Hash));
        }

        [Test]
        public void CanonicalManifestJson_UsesSchemaPropertyOrderAndSortedSets()
        {
            var manifest = NodeManifestTestFactory.Create(
                "example.nodes.canonical",
                reads: new[] { "z", "a" },
                writes: new[] { "y", "b" },
                sideEffects: new[] { "two", "one" });

            var entry = new Authoring.NodeRegistryBuilder().AddUserExtension(manifest).Build().Registry.Single();
            var json = Authoring.NodeManifestCanonicalJson.SerializeRegistry(new[] { entry });

            Assert.That(json.IndexOf("\"typeId\"", StringComparison.Ordinal), Is.LessThan(json.IndexOf("\"version\"", StringComparison.Ordinal)));
            Assert.That(json.IndexOf("\"version\"", StringComparison.Ordinal), Is.LessThan(json.IndexOf("\"summary\"", StringComparison.Ordinal)));
            Assert.That(json, Does.Contain("\"reads\": [\n        \"a\",\n        \"z\"\n      ]"));
            Assert.That(json, Does.Contain("\"writes\": [\n        \"b\",\n        \"y\"\n      ]"));
            Assert.That(json, Does.Contain("\"sideEffects\": [\n        \"one\",\n        \"two\"\n      ]"));
            Assert.That(json, Does.Contain("\"configuration\": {\n        \"size\": 1,\n        \"alignment\": 1\n      }"));
            Assert.That(json, Does.Contain("\"memory\": {\n        \"size\": 0,\n        \"alignment\": 1,\n        \"lifetime\": \"activation\"\n      }"));
            Assert.That(json, Does.EndWith("\n"));
            Assert.That(json, Does.Not.Contain("\r"));
            Assert.That(Encoding.UTF8.GetString(Authoring.NodeManifestCanonicalJson.SerializeRegistryUtf8(new[] { entry })), Is.EqualTo(json));
        }

        [Test]
        public void SetLikeStrings_AreOrderedByOrdinalUtf8Bytes()
        {
            const string privateUse = "\ue000";
            const string supplementary = "\ud800\udc00";
            var manifest = NodeManifestTestFactory.Create(
                "example.nodes.utf8-order",
                reads: new[] { supplementary, privateUse });
            var entry = new Authoring.NodeRegistryBuilder().AddUserExtension(manifest).Build().Registry.Single();
            var json = Authoring.NodeManifestCanonicalJson.SerializeRegistry(new[] { entry });

            Assert.That(json.IndexOf(privateUse, StringComparison.Ordinal),
                Is.LessThan(json.IndexOf(supplementary, StringComparison.Ordinal)));
        }

        [Test]
        public void BuiltInRegistry_HasGoldenCanonicalHash()
        {
            var result = Authoring.NodeRegistryBuilder.CreateWithBuiltIns().Build();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Registry.Hash, Is.EqualTo("d10147e9bdad70aaefeb3cde15df78d601a553262673fb067a8aeffe4d0fccf1"));
            var bytes = Authoring.NodeManifestCanonicalJson.SerializeRegistryUtf8(result.Registry.ToArray());
            Assert.That(result.Registry.Hash, Is.EqualTo(StableHash.Sha256Hex(bytes)));
            Assert.That(bytes[bytes.Length - 1], Is.EqualTo((byte)'\n'));
        }

        [Test]
        public void CanonicalRegistryBytes_AreIndependentOfEntryArrayOrder()
        {
            var result = new Authoring.NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.alpha"))
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.beta"))
                .Build();
            var ascending = result.Registry.ToArray();
            var descending = ascending.Reverse().ToArray();

            Assert.That(
                Authoring.NodeManifestCanonicalJson.SerializeRegistryUtf8(descending),
                Is.EqualTo(Authoring.NodeManifestCanonicalJson.SerializeRegistryUtf8(ascending)));
        }

        [Test]
        public void EmptyRegistry_HasGoldenCanonicalBytesAndHash()
        {
            const string expected = "{\n"
                + "  \"format\": \"aibt-node-registry\",\n"
                + "  \"formatVersion\": 1,\n"
                + "  \"manifests\": []\n"
                + "}\n";

            var value = Authoring.NodeManifestCanonicalJson.SerializeRegistry(Array.Empty<Authoring.NodeRegistryEntry>());

            Assert.That(value, Is.EqualTo(expected));
            Assert.That(
                StableHash.Sha256Hex(Encoding.UTF8.GetBytes(value)),
                Is.EqualTo("d8ff7ad9b660ca8f567881d0388178d0f14b7052d1f89699b00b5da130655acc"));
        }

        [Test]
        public void NumericTypeId_UsesStableFnvOfCanonicalString()
        {
            var result = new Authoring.NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.hash"))
                .Build();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Registry.Single().NumericTypeId, Is.EqualTo(StableHash.Fnv1A64("example.nodes.hash")));
            Assert.That(result.Registry.TryGet("example.nodes.hash", out var byString), Is.True);
            Assert.That(result.Registry.TryGet(byString.NumericTypeId, out var byNumber), Is.True);
            Assert.That(byNumber, Is.SameAs(byString));
        }
    }
}
