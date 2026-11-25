using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AIBT.Simulation;
using AIBT.Tests.CodeGen.GenerationP12001;
using NUnit.Framework;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// P12-001 measurement (Editor, opt-in: ignored in a normal run, see <see cref="Switch"/>): what wake on change costs at 500
    /// agents with <see cref="ThinkSchedule.Spread"/>(5), one step per tick, the tree of the reactive pattern
    /// (<see cref="TreeSimulationReactivePatternTests.SelectorTree"/>). Variants:
    /// A no wake on any channel; B wake on the trigger channel (never changing); C as B with the positions
    /// channel (16 bytes per row, no wake) rewritten every tick; D wake on the positions channel, rewritten
    /// every tick (every agent wakes every step). Only <c>Step</c> is timed; the game's row writes are not.
    /// Rounds interleave A B C D for both paths (in place, jobs), alternating the path order. Raw per-step
    /// times go to <c>%TEMP%/aibt-p12001-wake-cost/&lt;stamp&gt;/</c> (the test prints the folder).
    /// </summary>
    public sealed class TreeSimulationWakeCostBenchmark
    {
        private const int Population = 500;
        private const int Rounds = 7;
        private const int WarmUpSteps = 60;
        private const int MeasuredSteps = 300;

        private sealed class Cell : IDisposable
        {
            internal string Variant;
            internal bool UseJobs;
            internal TreeSimulationReactivePatternTests.Rig Rig;
            internal uint Tick;
            internal bool RewritePositions;

            public void Dispose() => Rig.Dispose();
        }

        /// <summary>The Editor-process environment variable that turns the measurement on (value 1).</summary>
        internal const string Switch = "AIBT_P12001_BENCHMARK";

        // The Unity test pipeline used here does not run [Explicit] tests, so the measurement is opt-in through
        // an environment variable of the Editor process and is reported as ignored otherwise.
        // About 13 minutes in the Editor (the in-place path runs managed code there): above the default 180 s timeout.
        [Test, Category("AibtBenchmarkP12001"), Timeout(3_600_000)]
        public void WakeOnChangeCost_500Agents_Spread5()
        {
            if (System.Environment.GetEnvironmentVariable(Switch) != "1")
                Assert.Ignore("P12-001 measurement: set the Editor process environment variable " + Switch + "=1 to run it.");
            var folder = Path.Combine(Path.GetTempPath(), "aibt-p12001-wake-cost", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            // P12-002 review follow-up: never overwrite an earlier run's raw data.
            if (File.Exists(Path.Combine(folder, "steps.csv")))
                Assert.Fail("P12-001 measurement: " + folder + " already holds steps.csv from an earlier run; start it again in a new second.");
            Directory.CreateDirectory(folder);
            var asset = TreeSimulationReactivePatternTests.PrepareAsset(TreeSimulationReactivePatternTests.SelectorTree);
            Assert.That(asset.TryCreateProgram(ReactivePatternCatalog.TryCreateRuntimeCatalog, out var program, out var failure), Is.True, failure.ToString());
            var cells = new List<Cell>();
            var csv = new StringBuilder("round,order,path,variant,step,tick,us,thinks,wokenByInput\n");
            try
            {
                foreach (var useJobs in new[] { false, true })
                foreach (var variant in new[] { "A", "B", "C", "D" })
                {
                    var rig = TreeSimulationReactivePatternTests.NewRig(program, useJobs,
                        triggerWake: variant != "A", positionWake: variant == "D", agents: Population, addAgents: false);
                    var settings = rig.Simulation.Settings;
                    Assert.That(settings.MinimumJobBatch, Is.EqualTo(1));
                    for (var agent = 0; agent < Population; agent++)
                        Assert.That(rig.Group.TryAdd(ThinkSchedule.Spread(5), out rig.Agents[agent]), Is.EqualTo(SimulationResult.Success));
                    rig.MovePositions(0);
                    cells.Add(new Cell { Variant = variant, UseJobs = useJobs, Rig = rig, RewritePositions = variant == "C" || variant == "D" });
                }

                var order = 0;
                for (var round = 0; round < Rounds; round++)
                {
                    var paths = round % 2 == 0 ? new[] { false, true } : new[] { true, false };
                    foreach (var useJobs in paths)
                    foreach (var cell in cells.Where(c => c.UseJobs == useJobs))
                    {
                        order++;
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        for (var step = -WarmUpSteps; step < MeasuredSteps; step++)
                        {
                            cell.Tick++;
                            if (cell.RewritePositions) cell.Rig.MovePositions(cell.Tick);
                            var updatesBefore = TotalUpdates(cell.Rig);
                            var wokenBefore = cell.Rig.Group.WokenByInputForTests;
                            var start = Stopwatch.GetTimestamp();
                            var result = cell.Rig.Simulation.Step(cell.Tick);
                            var stop = Stopwatch.GetTimestamp();
                            Assert.That(result, Is.EqualTo(SimulationResult.Success));
                            if (step < 0) continue;
                            var us = (stop - start) * 1_000_000.0 / Stopwatch.Frequency;
                            csv.Append(round).Append(',').Append(order).Append(',').Append(useJobs ? "jobs" : "inplace").Append(',')
                                .Append(cell.Variant).Append(',').Append(step).Append(',').Append(cell.Tick).Append(',')
                                .Append(us.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                                .Append(TotalUpdates(cell.Rig) - updatesBefore).Append(',')
                                .Append(cell.Rig.Group.WokenByInputForTests - wokenBefore).Append('\n');
                        }
                        Assert.That(cell.Rig.Group.GetFaults().Length, Is.Zero);
                    }
                }

                foreach (var cell in cells)
                    Assert.That(cell.UseJobs ? cell.Rig.Group.JobBatchesForTests > 0 : cell.Rig.Group.JobBatchesForTests == 0, Is.True);
                File.WriteAllText(Path.Combine(folder, "steps.csv"), csv.ToString());
                File.WriteAllText(Path.Combine(folder, "environment.json"), Environment(), new UTF8Encoding(false));
                TestContext.WriteLine("P12-001 wake cost raw data: " + folder);
                UnityEngine.Debug.Log("P12-001 wake cost raw data: " + folder);
            }
            finally
            {
                foreach (var cell in cells) cell.Dispose();
                program.Dispose();
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static ulong TotalUpdates(TreeSimulationReactivePatternTests.Rig rig)
        {
            ulong total = 0;
            for (var index = 0; index < rig.Agents.Length; index++) total += rig.Group.CoreForTests(index).TotalUpdates;
            return total;
        }

        private static string Environment()
            => "{\n"
               + "  \"where\": \"Unity Editor, EditMode test\",\n"
               + "  \"unityVersion\": \"" + UnityEngine.Application.unityVersion + "\",\n"
               + "  \"burstCompilation\": " + (BurstCompiler.Options.EnableBurstCompilation ? "true" : "false") + ",\n"
               + "  \"burstSafetyChecks\": " + (BurstCompiler.Options.EnableBurstSafetyChecks ? "true" : "false") + ",\n"
               + "  \"jobsDebugger\": " + (JobsUtility.JobDebuggerEnabled ? "true" : "false") + ",\n"
               + "  \"jobWorkerCount\": " + JobsUtility.JobWorkerCount + ",\n"
               + "  \"processorCount\": " + System.Environment.ProcessorCount + ",\n"
               + "  \"population\": " + Population + ", \"rounds\": " + Rounds + ", \"warmUpSteps\": " + WarmUpSteps + ", \"measuredSteps\": " + MeasuredSteps + ",\n"
               + "  \"tickMicroseconds\": " + TreeSimulationReactivePatternTests.TickMicroseconds + ", \"schedule\": \"Spread(5)\", \"minimumJobBatch\": 1,\n"
               + "  \"utc\": \"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\"\n"
               + "}\n";
    }
}
