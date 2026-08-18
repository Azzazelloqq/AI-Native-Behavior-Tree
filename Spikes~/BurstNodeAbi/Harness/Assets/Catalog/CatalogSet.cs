using AIBT.Burst;

namespace AIBT.BurstAbi.Catalog
{
    [AibtCatalogSet("consumer.catalog", 1u, typeof(RuntimeBuiltins.RuntimeBuiltinsShard), typeof(NodesA.CanaryShard), typeof(NodesB.ObserverShard))]
    public static partial class GeneratedCatalog { }
}
