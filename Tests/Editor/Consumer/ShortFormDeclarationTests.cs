using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace AIBT.Tests.Consumer
{
    /// <summary>
    /// P9-005: a node declared with the AIBT.Native short attributes (assembly AIBT.Consumer.ShortNodes)
    /// produces exactly the generated metadata of the same node declared with the long AIBT.Burst
    /// attributes (AIBT.Consumer.Nodes). The short assembly is read by reflection because both declare
    /// the same type names.
    /// </summary>
    public sealed class ShortFormDeclarationTests
    {
        private static readonly string[] MetadataConstants =
        {
            "ShardId", "ShardVersion", "CanonicalDescriptorJson", "DescriptorHash", "ManifestRegistryJson", "NodeRegistryHash",
        };

        [Test]
        public void ShortForm_ProducesTheSameManifestAndMetadataAsTheLongForm()
        {
            var shortShard = ShortAssembly().GetType("AIBT.Tests.Consumer.ConsumerShard", true);
            Assert.That(shortShard.GetField("IsUsable", BindingFlags.Public | BindingFlags.Static)?.GetValue(null), Is.EqualTo(true),
                "The short-form shard must be usable.");
            Assert.That(shortShard.GetField("AbiVersion", BindingFlags.Public | BindingFlags.Static)?.GetValue(null), Is.EqualTo(2u));

            var shortMetadata = shortShard.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public | BindingFlags.NonPublic);
            var longMetadata = typeof(ConsumerShard).GetNestedType("AibtGeneratedMetadata", BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(shortMetadata, Is.Not.Null);
            foreach (var name in MetadataConstants)
                Assert.That(Constant(shortMetadata, name), Is.EqualTo(Constant(longMetadata, name)), name);
            Assert.That((string)Constant(shortMetadata, "ManifestRegistryJson"), Does.Contain("aibt.tests.consumer.radius"));
        }

        private static Assembly ShortAssembly()
            => AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly => assembly.GetName().Name == "AIBT.Consumer.ShortNodes")
               ?? Assembly.Load("AIBT.Consumer.ShortNodes");

        private static object Constant(Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, type.FullName + "." + name);
            return field.GetValue(null);
        }
    }
}
