using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.Runtime.Scheduling;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P10-002 J2 spike (not package code; kept under Spikes~ after the run). Questions:
    /// (1) can one Burst job advance a machine through several lifecycle steps up to a barrier, with
    ///     the machines held in a plain UnsafeList (no nested native-container field), under the
    ///     Editor's safety checks, and does it produce exactly the steps the current one-step-per-job
    ///     rounds produce? (2) what does it cost compared with the rounds? (3) can a runtime-owned job
    ///     call a catalog's generated ExecuteImmediate through a Burst function pointer as it is?
    /// </summary>
    public sealed class J2SpikeTests
    {
        private const string LoopTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10002.spike-loop"", ""name"": ""spike loop"",
  ""root"": ""loop"",
  ""blackboard"": { ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 } },
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""both""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""both"": { ""type"": ""aibt.core.parallel"", ""typeVersion"": 1, ""parameters"": { ""policy"": ""require-all-success"" }, ""children"": [""choose"", ""hold""] },
    ""choose"": { ""type"": ""aibt.core.memory-selector"", ""typeVersion"": 1, ""children"": [""gate"", ""patrol""] },
    ""gate"": { ""type"": ""aibt.core.failer"", ""typeVersion"": 1, ""children"": [""gate-wait""] },
    ""gate-wait"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 1 } },
    ""patrol"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 3 } },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } }
  }
}";

        private delegate BurstExecutionResult ExecuteImmediateDelegate(ref BurstExecutionBatch batch);

        [Test]
        public void Spike_MultiStepAdvanceInOneJob_MatchesOneStepRounds_AndCosts()
        {
            var program = Compile(LoopTree);
            var kinds = new NativeLifecycleNodeKindV1[program.Nodes.Count];
            for (var index = 0; index < kinds.Length; index++) kinds[index] = NativeHotReloadInstance.ClassifyKind(program.Nodes[index].NodeTypeId);
            var report = new StringBuilder();
            foreach (var count in new[] { 64, 1000, 5000 })
            {
                Assert.That(SchedulingPolicyDriver.TryCreateAgents(program, kinds, count, Allocator.Persistent, out var rounds, out var failure), Is.True, failure.ToString());
                Assert.That(SchedulingPolicyDriver.TryCreateAgents(program, kinds, count, Allocator.Persistent, out var single, out failure), Is.True, failure.ToString());
                var machines = new UnsafeList<NativeLifecycleMachineV1>(count, Allocator.Persistent);
                var emptyBytes = new List<NativeArray<byte>>();
                var emptyParallel = new List<NativeArray<NativeParallelBranchStateV1>>();
                try
                {
                    for (var index = 0; index < count; index++)
                    {
                        Assert.That(rounds[index].Machine.TryBeginUpdate(1, 0, out failure), Is.True, failure.ToString());
                        Assert.That(single[index].Machine.TryBeginUpdate(1, 0, out failure), Is.True, failure.ToString());
                        var machine = single[index].Machine;
                        var bytes = new NativeArray<byte>(0, Allocator.Persistent);
                        var parallel = new NativeArray<NativeParallelBranchStateV1>(0, Allocator.Persistent);
                        emptyBytes.Add(bytes);
                        emptyParallel.Add(parallel);
                        machine.TryAttachCreatedEmptyJobStorage(bytes, parallel);
                        machines.Add(machine);
                    }

                    // Current shape: one IJob per machine per step, main thread between steps.
                    var roundWatch = Stopwatch.StartNew();
                    var roundKinds = new List<NativeLifecycleStepKindV1>[count];
                    for (var index = 0; index < count; index++) roundKinds[index] = new List<NativeLifecycleStepKindV1>();
                    var open = Enumerable.Range(0, count).ToList();
                    var roundCount = 0;
                    while (open.Count > 0)
                    {
                        roundCount++;
                        var batch = open.Select(i => rounds[i].Machine).ToArray();
                        Assert.That(NativeBatchedLifecycleOwnerV1.TryCreate(batch, Allocator.Persistent, out var owner, out failure), Is.True, failure.ToString());
                        var results = new NativeArray<NativeLifecycleStepResultV1>(batch.Length, Allocator.Temp);
                        var failures = new NativeArray<NativeRuntimeFailureV1>(batch.Length, Allocator.Temp);
                        Assert.That(owner.TrySchedule(64u, default, out var handle, out failure), Is.True);
                        handle.Complete();
                        Assert.That(owner.TryComplete(results, failures, out failure), Is.True, failure.ToString());
                        owner.TryDispose(out _);
                        var next = new List<int>();
                        for (var lane = 0; lane < open.Count; lane++)
                        {
                            var kind = results[lane].Kind;
                            roundKinds[open[lane]].Add(kind);
                            if (!IsBarrier(kind)) next.Add(open[lane]);
                        }
                        results.Dispose();
                        failures.Dispose();
                        open = next;
                    }
                    roundWatch.Stop();

                    // J2 shape: one IJobFor over all machines, each advanced until its barrier.
                    var steps = new NativeArray<int>(count, Allocator.Persistent);
                    var log = new NativeArray<byte>(count * 64, Allocator.Persistent);
                    var ok = new NativeArray<byte>(count, Allocator.Persistent);
                    try
                    {
                        var jobWatch = Stopwatch.StartNew();
                        new AdvanceToBarrierJob { Machines = machines, Steps = steps, Log = log, Ok = ok }
                            .ScheduleParallel(count, 64, default).Complete();
                        jobWatch.Stop();
                        for (var index = 0; index < count; index++)
                        {
                            Assert.That(ok[index], Is.EqualTo(1), "agent " + index + " failed to advance in the job");
                            var jobKinds = Enumerable.Range(0, steps[index]).Select(s => (NativeLifecycleStepKindV1)log[index * 64 + s]).ToList();
                            Assert.That(jobKinds, Is.EqualTo(roundKinds[index]), "agent " + index + ": same step kinds as the one-step rounds");
                        }
                        report.Append("N=").Append(count).Append(": steps to first barrier ").Append(steps[0])
                            .Append(" (").Append(string.Join(",", roundKinds[0])).Append("), rounds ").Append(roundCount)
                            .Append(", one-step rounds ").Append((roundWatch.Elapsed.TotalMilliseconds * 1000.0).ToString("0"))
                            .Append(" us, one multi-step IJobFor ").Append((jobWatch.Elapsed.TotalMilliseconds * 1000.0).ToString("0")).Append(" us\n");
                    }
                    finally
                    {
                        steps.Dispose();
                        log.Dispose();
                        ok.Dispose();
                    }
                }
                finally
                {
                    machines.Dispose();
                    foreach (var array in emptyBytes) array.Dispose();
                    foreach (var array in emptyParallel) array.Dispose();
                    foreach (var agent in rounds) agent.Dispose();
                    foreach (var agent in single) agent.Dispose();
                }
            }
            UnityEngine.Debug.Log("P10-002 J2 spike (Editor, safety checks on, Burst " + (BurstCompiler.IsEnabled ? "on" : "off") + "):\n" + report);
        }

        [Test]
        public void Spike_GeneratedExecuteImmediate_AsBurstFunctionPointer()
        {
            string outcome;
            try
            {
                var pointer = BurstCompiler.CompileFunctionPointer<ExecuteImmediateDelegate>(JobsEquivalenceCatalog.ExecuteImmediate);
                outcome = "compiled: " + pointer.IsCreated;
            }
            catch (Exception exception)
            {
                outcome = exception.GetType().Name + ": " + exception.Message;
            }
            UnityEngine.Debug.Log("P10-002 J2 spike, function pointer to generated ExecuteImmediate: " + outcome);
            Assert.Pass(outcome);
        }

        private static bool IsBarrier(NativeLifecycleStepKindV1 kind)
            => kind == NativeLifecycleStepKindV1.DispatchRequired || kind == NativeLifecycleStepKindV1.Waiting
                || kind == NativeLifecycleStepKindV1.Completed;

        [BurstCompile(CompileSynchronously = true)]
        private struct AdvanceToBarrierJob : IJobFor
        {
            [NativeDisableContainerSafetyRestriction] public UnsafeList<NativeLifecycleMachineV1> Machines;
            public NativeArray<int> Steps;
            [NativeDisableParallelForRestriction] public NativeArray<byte> Log;
            public NativeArray<byte> Ok;

            public void Execute(int index)
            {
                ref var machine = ref Machines.ElementAt(index);
                var count = 0;
                Ok[index] = 1;
                while (count < 64)
                {
                    if (!machine.TryAdvance(out var step, out _)) { Ok[index] = 0; break; }
                    Log[index * 64 + count] = (byte)step.Kind;
                    count++;
                    if (IsBarrier(step.Kind)) break;
                }
                Steps[index] = count;
            }
        }

        private static CompiledProgram Compile(string json)
        {
            var standard = Materialize(typeof(StandardNativeShard));
            var nodes = Materialize(typeof(BuiltInLeafShard)).Nodes.Concat(standard.Nodes)
                .Concat(Materialize(typeof(ExternalTreeWriteFixtureShard)).Nodes).ToArray();
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P10002/spike.aibt.json");
            Assert.That(parsed.Success, Is.True);
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, standard.RegisteredTypes,
                new ReferenceCompilerOptions("Tests/P10002/spike.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            return result.Program.CreateNativeBlackboardBindingV2(standard.RegisteredTypes).SemanticProgram;
        }

        private static GeneratedShardMetadataArtifact Materialize(Type shard)
        {
            var metadata = shard.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public);
            object Field(string name) => metadata.GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return GeneratedShardMetadataMaterializer.MaterializeArtifact(
                (string)Field("ShardId"), (uint)Field("ShardVersion"), (string)Field("CanonicalDescriptorJson"),
                (string)Field("DescriptorHash"), (string)Field("ManifestRegistryJson"), (string)Field("NodeRegistryHash"));
        }
    }
}
