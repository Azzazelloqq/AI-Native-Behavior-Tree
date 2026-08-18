using AIBT.Tests.CodeGen.Generation;
using NUnit.Framework;

namespace AIBT.Tests.CodeGen.GenerationConsumer
{
    public sealed class GeneratedMetadataConsumerTests
    {
        [Test]
        public void PublicGeneratedMetadata_IsDirectlyAccessibleAcrossAssemblies()
        {
            Assert.That(GenerationShard.AbiVersion, Is.EqualTo(2u));
            Assert.That(GenerationShard.AibtGeneratedMetadata.CanonicalDescriptorJson, Is.Not.Empty);
            Assert.That(GenerationShard.AibtGeneratedMetadata.ManifestRegistryJson, Is.Not.Empty);
        }
    }
}
