using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP10010;
using AIBT.Tests.CodeGen.GenerationP10011;

// P10-011: the catalog for the container external-action tests: the built-in leaves, the standard
// native nodes, the P10-010 input/command shard and the P10-011 action shard (its nodes live in
// AIBT.CodeGen.P10011SimulationActions.Tests).
namespace AIBT.Tests.Integration.Simulation
{
    [AibtCatalogSet("aibt.tests.p10011.simulation", 1u,
        typeof(BuiltInLeafShard), typeof(StandardNativeShard), typeof(SimulationIoShard), typeof(SimulationActionShard))]
    public static partial class SimulationActionCatalog { }
}
