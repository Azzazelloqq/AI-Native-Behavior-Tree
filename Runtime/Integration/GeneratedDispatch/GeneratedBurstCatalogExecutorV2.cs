using Unity.Jobs;

namespace AIBT.Burst
{
    /// <summary>Closed generated-catalog entry point retained by the production runtime.</summary>
    public interface IGeneratedBurstCatalogExecutorV2
    {
        BurstExecutionResult ExecuteImmediate(ref BurstExecutionBatch batch);
        JobHandle Schedule(ref BurstExecutionBatch batch, JobHandle dependency);
    }
}
