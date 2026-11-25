using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AIBT.Simulation;
using AIBT.Tests.CodeGen.GenerationP10010;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// P12-002 measurement (Editor, opt-in: ignored in a normal run, see <see cref="Switch"/>): what reading squad
    /// data through an indexed input channel costs against the slot-row channel, at 500 agents in 50 squads of 10
    /// with <see cref="ThinkSchedule.Spread"/>(5), one step per tick, the same row type (<c>SenseInput</c>, 12
    /// bytes) and the same tree (<see cref="TreeSimulationIndexedInputTests.ReportTree"/>: one order per think (orders per step = thinks per step) from
    /// the row it reads). Variants: S slot-row channel (500 rows, the game writes each member's squad value into
    /// its slot row every tick); I indexed channel (50 rows written every tick, rowOfSlot = slot / 10 written
    /// once); SW and IW the same with wake on change. Five squads change value per tick ((tick + squad) % 10 == 0).
    /// Only <c>Step</c> is timed; the game's writes are timed separately. Rounds run every cell back to back; the
    /// path order alternates and the variant order rotates by one position per round. Per block: GC.Alloc
    /// samples over the measured steps, an order digest (S equals I, SW equals IW, in place equals jobs), and how
    /// many agents of the last job batch ran without Burst. Raw data goes to the folder in
    /// <see cref="FolderVariable"/>, else <c>%TEMP%/aibt-p12002-indexed-input/&lt;stamp&gt;/</c> (the test prints it).
    /// </summary>
    public sealed class TreeSimulationIndexedInputBenchmark
    {
        private const int Population = 500;
        private const int Squads = 50;
        private const int Rounds = 8;
        private const int WarmUpSteps = 60;
        private const int MeasuredSteps = 300;
        private static readonly string[] Variants = { "S", "I", "SW", "IW" };
        private static readonly InputChannel<SenseInput> Sense = InputChannel<SenseInput>.Create("sense");
        private static readonly CommandChannel<OrderCommand> Order = CommandChannel<OrderCommand>.Create("order");

        /// <summary>The Editor-process environment variable that turns the measurement on (value 1).</summary>
        internal const string Switch = "AIBT_P12002_BENCHMARK";

        /// <summary>The Editor-process environment variable naming the raw-data folder (optional).</summary>
        internal const string FolderVariable = "AIBT_P12002_BENCHMARK_DIR";

        private sealed class Cell : IDisposable
        {
            internal string Variant;
            internal bool UseJobs;
            internal bool Indexed;
            internal TreeSimulation Simulation;
            internal TreeGroup Group;
            internal NativeArray<SenseInput> Rows;
            internal NativeArray<int> RowOf;
            internal uint Tick;

            public void Dispose()
            {
                Simulation?.Dispose();
                if (Rows.IsCreated) Rows.Dispose();
                if (RowOf.IsCreated) RowOf.Dispose();
            }
        }

        // The Unity test pipeline used here does not run [Explicit] tests, so the measurement is opt-in through
        // an environment variable of the Editor process and is reported as ignored otherwise.
        [Test, Category("AibtBenchmarkP12002"), Timeout(3_600_000)]
        public void IndexedAgainstSlotRows_500Agents_50Squads_Spread5()
        {
            if (System.Environment.GetEnvironmentVariable(Switch) != "1")
                Assert.Ignore("P12-002 measurement: set the Editor process environment variable " + Switch + "=1 to run it.");
            var folder = System.Environment.GetEnvironmentVariable(FolderVariable);
            if (string.IsNullOrEmpty(folder))
                folder = Path.Combine(Path.GetTempPath(), "aibt-p12002-indexed-input", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            // Review follow-up: never overwrite an earlier run's raw data.
            if (File.Exists(Path.Combine(folder, "steps.csv")))
                Assert.Fail("P12-002 measurement: " + folder + " already holds steps.csv from an earlier run; name a new folder in " + FolderVariable + ".");
            Directory.CreateDirectory(folder);
            var asset = TreeSimulationIoTests.PrepareIoAsset(TreeSimulationIndexedInputTests.ReportTree);
            Assert.That(asset.TryCreateProgram(SimulationIoCatalog.TryCreateRuntimeCatalog, out var program, out var failure), Is.True, failure.ToString());
            var cells = new List<Cell>();
            var steps = new StringBuilder("round,order,position,path,variant,step,tick,us,writeUs,wokenByInput,orders\n");
            var blocks = new StringBuilder("round,order,position,path,variant,gcAllocSamples,digest,faults,jobBatches,managedAgentsLastBatch\n");
            var stepUs = new double[MeasuredSteps];
            var writeUs = new double[MeasuredSteps];
            var woken = new long[MeasuredSteps];
            var orders = new int[MeasuredSteps];
            var digests = new Dictionary<(int round, string variant, bool jobs), ulong>();
            try
            {
                foreach (var useJobs in new[] { false, true })
                foreach (var variant in Variants)
                    cells.Add(NewCell(program, variant, useJobs));

                var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
                recorder.FilterToCurrentThread();
                var order = 0;
                for (var round = 0; round < Rounds; round++)
                {
                    var paths = round % 2 == 0 ? new[] { false, true } : new[] { true, false };
                    var position = 0;
                    foreach (var useJobs in paths)
                    for (var slot = 0; slot < Variants.Length; slot++)
                    {
                        var variant = Variants[(slot + round) % Variants.Length];
                        var cell = cells.Single(c => c.UseJobs == useJobs && c.Variant == variant);
                        order++;
                        position++;
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        var digest = 14695981039346656037UL;
                        for (var step = -WarmUpSteps; step < MeasuredSteps; step++)
                        {
                            if (step == 0)
                            {
                                recorder.enabled = false;
                                recorder.enabled = true;
                            }
                            cell.Tick++;
                            var writeStart = Stopwatch.GetTimestamp();
                            Write(cell);
                            var start = Stopwatch.GetTimestamp();
                            var wokenBefore = cell.Group.WokenByInputForTests;
                            var stepStart = Stopwatch.GetTimestamp();
                            var result = cell.Simulation.Step(cell.Tick);
                            var stop = Stopwatch.GetTimestamp();
                            if (result != SimulationResult.Success) Assert.Fail("Step(" + cell.Tick + ") returned " + result);
                            var commands = cell.Group.GetCommands(Order);
                            if (step < 0) continue;
                            for (var index = 0; index < commands.Length; index++)
                            {
                                var command = commands[index];
                                digest = Mix(digest, (ulong)(uint)command.Agent.Index);
                                digest = Mix(digest, (ulong)(uint)command.Value.Target);
                                digest = Mix(digest, (ulong)(uint)BitConverter.SingleToInt32Bits(command.Value.Strength));
                                digest = Mix(digest, command.Value.Urgent ? 1UL : 0UL);
                            }
                            stepUs[step] = (stop - stepStart) * 1_000_000.0 / Stopwatch.Frequency;
                            writeUs[step] = (start - writeStart) * 1_000_000.0 / Stopwatch.Frequency;
                            woken[step] = cell.Group.WokenByInputForTests - wokenBefore;
                            orders[step] = commands.Length;
                        }
                        recorder.enabled = false;
                        var samples = recorder.sampleBlockCount;
                        var path = useJobs ? "jobs" : "inplace";
                        for (var step = 0; step < MeasuredSteps; step++)
                        {
                            steps.Append(round).Append(',').Append(order).Append(',').Append(position).Append(',').Append(path).Append(',')
                                .Append(variant).Append(',').Append(step).Append(',').Append(cell.Tick - (uint)(MeasuredSteps - 1 - step)).Append(',')
                                .Append(stepUs[step].ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                                .Append(writeUs[step].ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                                .Append(woken[step]).Append(',').Append(orders[step]).Append('\n');
                        }
                        blocks.Append(round).Append(',').Append(order).Append(',').Append(position).Append(',').Append(path).Append(',')
                            .Append(variant).Append(',').Append(samples).Append(',').Append(digest.ToString("x16", CultureInfo.InvariantCulture)).Append(',')
                            .Append(cell.Group.GetFaults().Length).Append(',').Append(cell.Group.JobBatchesForTests).Append(',')
                            .Append(useJobs ? ManagedAgentsInTable(cell.Group) : 0).Append('\n');
                        digests[(round, variant, useJobs)] = digest;
                        Assert.That(cell.Group.GetFaults().Length, Is.Zero, path + " " + variant + " faults");
                    }
                }

                File.WriteAllText(Path.Combine(folder, "steps.csv"), steps.ToString());
                File.WriteAllText(Path.Combine(folder, "blocks.csv"), blocks.ToString());
                File.WriteAllText(Path.Combine(folder, "environment.json"), Environment(), new UTF8Encoding(false));
                TestContext.WriteLine("P12-002 indexed input raw data: " + folder);
                UnityEngine.Debug.Log("P12-002 indexed input raw data: " + folder);

                // Every cell of one round ran the same ticks: the orders must agree across channel kinds and paths.
                for (var round = 0; round < Rounds; round++)
                {
                    Assert.That(digests[(round, "I", false)], Is.EqualTo(digests[(round, "S", false)]), "round " + round + ": I equals S in place");
                    Assert.That(digests[(round, "IW", false)], Is.EqualTo(digests[(round, "SW", false)]), "round " + round + ": IW equals SW in place");
                    foreach (var variant in Variants)
                        Assert.That(digests[(round, variant, true)], Is.EqualTo(digests[(round, variant, false)]), "round " + round + ": " + variant + " jobs equals in place");
                }
                foreach (var cell in cells)
                    Assert.That(cell.UseJobs ? cell.Group.JobBatchesForTests > 0 : cell.Group.JobBatchesForTests == 0, Is.True);
            }
            finally
            {
                foreach (var cell in cells) cell.Dispose();
                program.Dispose();
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static Cell NewCell(ProductionTreeProgram program, string variant, bool useJobs)
        {
            var settings = TreeSimulationSettings.Default(TickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = 1;
            settings.RandomSeed = 0x12_0002UL;
            var cell = new Cell { Variant = variant, UseJobs = useJobs, Indexed = variant[0] == 'I', Simulation = new TreeSimulation(settings) };
            Assert.That(cell.Simulation.TryAddGroup(program, TreeGroupSettings.Default(Population), out cell.Group), Is.EqualTo(SimulationResult.Success));
            if (cell.Indexed)
            {
                cell.Rows = new NativeArray<SenseInput>(Squads, Allocator.Persistent);
                cell.RowOf = new NativeArray<int>(Population, Allocator.Persistent);
                var rowOf = cell.RowOf;
                for (var slot = 0; slot < Population; slot++) rowOf[slot] = slot / 10;
                Assert.That(cell.Group.TryBindInput(Sense, cell.Rows, cell.RowOf), Is.EqualTo(SimulationResult.Success));
            }
            else
            {
                cell.Rows = new NativeArray<SenseInput>(Population, Allocator.Persistent);
                Assert.That(cell.Group.TryBindInput(Sense, cell.Rows), Is.EqualTo(SimulationResult.Success));
            }
            if (variant.EndsWith("W", StringComparison.Ordinal))
                Assert.That(cell.Group.TrySetWakeOnChange(Sense, true), Is.EqualTo(SimulationResult.Success));
            for (var agent = 0; agent < Population; agent++)
            {
                Assert.That(cell.Group.TryAdd(ThinkSchedule.Spread(5), out var handle), Is.EqualTo(SimulationResult.Success));
                Assert.That(handle.Index, Is.EqualTo(agent));
            }
            return cell;
        }

        /// <summary>Squad <paramref name="squad"/>'s value at <paramref name="tick"/>: it changes when (tick + squad) % 10 == 0.</summary>
        private static SenseInput SquadValue(int squad, uint tick)
        {
            var version = (int)((tick + (uint)squad) / 10u);
            return new SenseInput { Threat = (squad + version) % 7 / 7f, Ally = squad * 1000 + version % 1000, Flag = (version & 1) != 0 };
        }

        /// <summary>The game's writes before a step: one row per squad (indexed) or one per member (slot rows).</summary>
        private static void Write(Cell cell)
        {
            var rows = cell.Rows;
            if (cell.Indexed)
            {
                for (var squad = 0; squad < Squads; squad++) rows[squad] = SquadValue(squad, cell.Tick);
                return;
            }
            for (var slot = 0; slot < Population; slot++) rows[slot] = SquadValue(slot / 10, cell.Tick);
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            unchecked { return (hash ^ value) * 1099511628211UL; }
        }

        /// <summary>How many of the group's agent-frame entries ran without Burst in their last job run.</summary>
        private static int ManagedAgentsInTable(TreeGroup group)
        {
            var field = typeof(TreeGroup).GetField("_table", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var table = (UnsafeList<AgentFrameAgentV1>)field.GetValue(group);
            var managed = 0;
            for (var index = 0; index < Math.Min(table.Length, Population); index++)
                if (table[index].RanManaged != 0) managed++;
            return managed;
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
               + "  \"population\": " + Population + ", \"squads\": " + Squads + ", \"rounds\": " + Rounds + ", \"warmUpSteps\": " + WarmUpSteps + ", \"measuredSteps\": " + MeasuredSteps + ",\n"
               + "  \"variants\": \"S slot rows, I indexed, SW and IW with wake on change; 5 squads change per tick\",\n"
               + "  \"order\": \"path order alternates per round; variant order rotates by one position per round\",\n"
               + "  \"tickMicroseconds\": " + TickMicroseconds + ", \"schedule\": \"Spread(5)\", \"minimumJobBatch\": 1,\n"
               + "  \"utc\": \"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\"\n"
               + "}\n";
    }
}
