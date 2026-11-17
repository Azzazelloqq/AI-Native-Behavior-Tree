using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP10010;

// P10-010: the catalog for the container input and command tests: the built-in leaves, the standard
// native nodes and the P10-010 fixture shard (its nodes live in AIBT.CodeGen.P10010SimulationIo.Tests).
namespace AIBT.Tests.Integration.Simulation
{
    [AibtCatalogSet("aibt.tests.p10010.simulation", 1u, typeof(BuiltInLeafShard), typeof(StandardNativeShard), typeof(SimulationIoShard))]
    public static partial class SimulationIoCatalog { }
}
