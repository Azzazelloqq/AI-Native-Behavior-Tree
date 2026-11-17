using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.Perf.IoNodes;
using AIBT.Samples.FirstAgent;
using AIBT.StandardNodes;

namespace AIBT.Perf
{
    // Benchmark catalog: stdlib leaves (shared with the managed layer), the aibt.native.* standard
    // nodes, the First Agent move node (fact in, effect out) and the P10-010 input/command nodes.
    [AibtCatalogSet("aibt.perf.catalog", 1u, typeof(BuiltInLeafShard), typeof(StandardNativeShard), typeof(FirstAgentShard), typeof(PerfIoShard))]
    public static partial class PerfCatalog { }
}
