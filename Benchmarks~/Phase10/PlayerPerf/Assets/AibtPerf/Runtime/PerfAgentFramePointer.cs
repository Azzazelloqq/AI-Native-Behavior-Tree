using System;
using System.Reflection;
using AIBT.AgentFrame;
using AIBT.Burst;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine;

namespace AIBT.Perf
{
    /// <summary>
    /// P10-004 gate, benchmark only: the JobsAgentFrame job with the node body reached through a Burst
    /// function pointer instead of the catalog's emitted agent-frame job. A function pointer cannot
    /// return a struct by value, so the entry writes the result through a ref parameter. The perf
    /// catalog's executor is swapped through reflection for scenarios ending in "-agentframe-fp".
    /// </summary>
    [BurstCompile]
    public static class PerfAgentFramePointer
    {
        public delegate void ExecuteDelegate(ref BurstExecutionBatch batch, ref BurstExecutionResult result);

        private static FunctionPointer<ExecuteDelegate> s_pointer;
        private static bool s_compiled;

        [BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(ExecuteDelegate))]
        public static void Execute(ref BurstExecutionBatch batch, ref BurstExecutionResult result)
            => result = PerfCatalog.ExecuteImmediate(ref batch);

        private struct PointerBody : IAgentFrameBodyV1
        {
            public FunctionPointer<ExecuteDelegate> Pointer;

            public BurstExecutionResult Execute(ref BurstExecutionBatch batch)
            {
                var result = default(BurstExecutionResult);
                Pointer.Invoke(ref batch, ref result);
                return result;
            }
        }

        [BurstCompile]
        private struct PointerJob : IJobFor
        {
            public AgentFrameJobDataV1 Data;
            public FunctionPointer<ExecuteDelegate> Pointer;

            public void Execute(int index)
            {
                var body = new PointerBody { Pointer = Pointer };
                AgentFrameJobRunnerV1.ExecuteAgent(ref Data, index, ref body);
            }
        }

        private sealed class PointerExecutor : IGeneratedBurstCatalogExecutorV2, IGeneratedAgentFrameExecutorV1
        {
            private readonly IGeneratedBurstCatalogExecutorV2 _inner;
            internal PointerExecutor(IGeneratedBurstCatalogExecutorV2 inner) => _inner = inner;
            public BurstExecutionResult ExecuteImmediate(ref BurstExecutionBatch batch) => _inner.ExecuteImmediate(ref batch);
            public JobHandle Schedule(ref BurstExecutionBatch batch, JobHandle dependency) => _inner.Schedule(ref batch, dependency);
            public bool TryGetEffectDescriptor(uint catalogCaseIndex, uint bindingOrdinal, out ProductionTreeEffectDescriptor descriptor)
                => _inner.TryGetEffectDescriptor(catalogCaseIndex, bindingOrdinal, out descriptor);
            public JobHandle ScheduleAgentFrame(AgentFrameJobDataV1 data, int agentCount, int innerBatchCount, JobHandle dependency)
                => new PointerJob { Data = data, Pointer = s_pointer }.ScheduleParallel(agentCount, innerBatchCount, dependency);
        }

        private static object s_catalog;
        private static object s_original;

        /// <summary>Puts the catalog's emitted executor back (scenario teardown).</summary>
        public static void Restore()
        {
            if (s_catalog == null) return;
            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            s_catalog.GetType().GetProperty("Executor", any).SetValue(s_catalog, s_original);
            s_catalog = null;
            s_original = null;
        }

        /// <summary>Swaps the catalog behind <paramref name="host"/> to the function-pointer executor once.</summary>
        public static bool TryInstall(ProductionTreeHost host)
        {
            try
            {
                if (!s_compiled)
                {
                    s_pointer = BurstCompiler.CompileFunctionPointer<ExecuteDelegate>(Execute);
                    s_compiled = true;
                }
                const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var adapter = typeof(ProductionTreeHost).GetProperty("GeneratedDispatchAdapter", any).GetValue(host);
                var catalog = adapter.GetType().GetProperty("Catalog", any).GetValue(adapter);
                var executorProperty = catalog.GetType().GetProperty("Executor", any);
                var executor = executorProperty.GetValue(catalog);
                if (executor is PointerExecutor) return true;
                s_catalog = catalog;
                s_original = executor;
                executorProperty.SetValue(catalog, new PointerExecutor((IGeneratedBurstCatalogExecutorV2)executor));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("AIBT_PERF|function-pointer executor could not be installed: " + exception);
                return false;
            }
        }
    }
}
