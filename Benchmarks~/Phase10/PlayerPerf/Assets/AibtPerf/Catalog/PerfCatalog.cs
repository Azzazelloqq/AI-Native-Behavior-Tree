using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.Samples.FirstAgent;
using AIBT.StandardNodes;

namespace AIBT.Perf
{
    // Benchmark catalog: stdlib leaves (shared with the managed layer), the aibt.native.* standard
    // nodes and the First Agent move node (fact in, effect out).
    [AibtCatalogSet("aibt.perf.catalog", 1u, typeof(BuiltInLeafShard), typeof(StandardNativeShard), typeof(FirstAgentShard))]
    public static partial class PerfCatalog { }
}
