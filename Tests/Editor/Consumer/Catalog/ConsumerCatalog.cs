using AIBT.Burst;

namespace AIBT.Tests.Consumer
{
    [AibtCatalogSet("aibt.tests.consumer.catalog", 1u, typeof(ConsumerShard))]
    public static partial class ConsumerCatalog { }
}
