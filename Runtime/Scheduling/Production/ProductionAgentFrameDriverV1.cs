using System;
using System.Collections.Generic;
using AIBT.AgentFrame;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace AIBT
{
    /// <summary>
    /// P10-004: drives one <see cref="ProductionTreeScheduler"/> batch under
    /// <see cref="SchedulingPolicy.JobsAgentFrame"/>. Begins each member's update in agent order on
    /// the main thread (eligibility, clock, update begin), schedules the catalog's generated
    /// agent-frame job once for the whole batch (one work item runs one agent's whole update, node
    /// bodies included), completes it, then finishes each member in the same order (trace replay,
    /// effect delivery into the host's queue, terminal and failure bookkeeping). The agent table is
    /// kept across frames and only grows, so a warmed-up batch allocates nothing.
    /// </summary>
    internal sealed class ProductionAgentFrameDriverV1 : IDisposable
    {
        private readonly List<ProductionTreeHost> _hosts = new List<ProductionTreeHost>();
        private UnsafeList<AgentFrameAgentV1> _agents;
        private bool _running;
        private bool _disposed;

        internal bool IsDisposed => _disposed && !_agents.IsCreated;

        /// <summary>
        /// Runs <paramref name="members"/> (all eligible for the job, all on the catalog behind
        /// <paramref name="executor"/>). <paramref name="isRegistered"/> is checked before each
        /// begin: once it is false (the scheduler was destroyed, or the host left it, from a clock
        /// callback) no later member is begun, as the Immediate loop stops driving. Returns how
        /// many members ran in the job.
        /// </summary>
        internal int Run(
            IGeneratedAgentFrameExecutorV1 executor,
            IReadOnlyList<ProductionTreeHost> members,
            Func<ProductionTreeHost, bool> isRegistered,
            out ulong totalSteps)
        {
            totalSteps = 0;
            if (members == null || members.Count == 0) return 0;
            if (_running || _disposed)
            {
                using (var temporary = new ProductionAgentFrameDriverV1())
                    return temporary.Run(executor, members, isRegistered, out totalSteps);
            }
            _running = true;
            try
            {
                return RunCore(executor, members, isRegistered, ref totalSteps);
            }
            finally
            {
                _hosts.Clear();
                if (_agents.IsCreated) _agents.Clear();
                _running = false;
                if (_disposed) DisposeStorage();
            }
        }

        private int RunCore(
            IGeneratedAgentFrameExecutorV1 executor,
            IReadOnlyList<ProductionTreeHost> members,
            Func<ProductionTreeHost, bool> isRegistered,
            ref ulong totalSteps)
        {
            if (!_agents.IsCreated) _agents = new UnsafeList<AgentFrameAgentV1>(Math.Max(64, members.Count), Allocator.Persistent);
            _agents.Clear();
            _hosts.Clear();
            for (var index = 0; index < members.Count; index++)
            {
                var host = members[index];
                if (_disposed || (isRegistered != null && !isRegistered(host))) break;
                _agents.Add(default);
                host.BeginAgentFrame(out var included, ref _agents.ElementAt(_agents.Length - 1));
                if (included) _hosts.Add(host);
                else _agents.RemoveAt(_agents.Length - 1);
            }
            var count = _hosts.Count;
            if (count == 0) return 0;

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                var workers = JobsUtility.JobWorkerCount;
                var innerBatch = Math.Max(1, Math.Min(64, count / (4 * (Math.Max(0, workers) + 1))));
                executor.ScheduleAgentFrame(new AgentFrameJobDataV1 { Agents = _agents }, count, innerBatch, default)
                    .Complete();
            }
            catch (Exception exception)
            {
                // Nothing ran: release every begun host with a structured failure, never a held lock.
                for (var index = 0; index < count; index++)
                    _hosts[index].AbandonAgentFrame(
                        new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid),
                        "The agent-frame job could not run: " + exception.GetType().Name + ": " + exception.Message);
                return 0;
            }
            AgentFrameDiagnosticsV1.JobTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            AgentFrameDiagnosticsV1.Batches++;
            AgentFrameDiagnosticsV1.Agents += count;
            for (var index = 0; index < count; index++)
            {
                ref var agent = ref _agents.ElementAt(index);
                totalSteps += agent.StepsThisUpdate;
                AgentFrameDiagnosticsV1.Dispatches += agent.Dispatches;
                if (agent.ThreadIndex != 0) AgentFrameDiagnosticsV1.AgentsOnWorkers++;
                if (agent.RanManaged != 0) AgentFrameDiagnosticsV1.ManagedAgents++;
                _hosts[index].FinishAgentFrame(ref agent);
            }
            return count;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_running) DisposeStorage();
        }

        private void DisposeStorage()
        {
            if (_agents.IsCreated) _agents.Dispose();
            _agents = default;
        }
    }
}
