using System.Collections.Generic;
using System.Linq;
using AIBT.Simulation;
using AIBT.Tests.Integration.NativeRuntime;
using NUnit.Framework;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.Simulation
{
    /// <summary>
    /// P11-002 stage 1 (ADR-P11-002 §7): every library behaviour tree of <see cref="LibraryCases"/> runs
    /// as its own TreeSimulation group of agents with different think schedules, once in place and
    /// once with Jobs. Every tick's digest (slots, results, active paths, every Tree-scope byte, root
    /// completions and faults) must be identical, and each group's root completions must carry the
    /// result the same tree gives on ProductionTreeHost.
    /// </summary>
    public sealed class NativeLibrarySimulationTests
    {
        private const int AgentsPerGroup = 3;
        private const int Ticks = 24;

        private sealed class Outcome
        {
            internal readonly List<ulong> Digests = new List<ulong>();
            internal readonly Dictionary<string, List<NodeStatus>> Completions = new Dictionary<string, List<NodeStatus>>();
            internal int Faults;
            internal long JobBatches;
        }

        private Outcome Run(IReadOnlyList<LibraryCase> cases, IReadOnlyList<ProductionTreeProgram> programs, bool useJobs)
        {
            var outcome = new Outcome();
            var settings = TreeSimulationSettings.Default(TickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = 1;
            var path = new List<uint>();
            using (var simulation = new TreeSimulation(settings))
            {
                var groups = new TreeGroup[programs.Count];
                for (var index = 0; index < programs.Count; index++)
                {
                    Assert.That(simulation.TryAddGroup(programs[index], TreeGroupSettings.Default(AgentsPerGroup), out groups[index]),
                        Is.EqualTo(SimulationResult.Success), cases[index].Name);
                    for (var agent = 0; agent < AgentsPerGroup; agent++)
                        Assert.That(groups[index].TryAdd(ThinkSchedule.Every((uint)(1 + agent), 0u), out _), Is.EqualTo(SimulationResult.Success));
                    outcome.Completions[cases[index].Name] = new List<NodeStatus>();
                }
                for (uint tick = 1; tick <= Ticks; tick++)
                {
                    Assert.That(simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
                    var digest = Fnv.Create();
                    for (var index = 0; index < groups.Length; index++)
                    {
                        AppendDigest(groups[index], ref digest, path);
                        var completions = groups[index].GetRootCompletions();
                        for (var item = 0; item < completions.Length; item++)
                            outcome.Completions[cases[index].Name].Add(completions[item].Result);
                        outcome.Faults += groups[index].GetFaults().Length;
                    }
                    outcome.Digests.Add(digest.Value);
                }
                outcome.JobBatches = groups.Sum(group => group.JobBatchesForTests);
            }
            return outcome;
        }

        [Test]
        public void EveryLibraryTree_GivesIdenticalPerTickDigestsInPlaceAndWithJobs_AndTheHostResult()
        {
            using (var kit = new NativeLibraryKit())
            {
                var cases = LibraryCases.All;
                var programs = cases.Select(c => kit.Prepare(c.Json).Program).ToArray();

                var inPlace = Run(cases, programs, useJobs: false);
                var jobs = Run(cases, programs, useJobs: true);

                Assert.That(inPlace.Faults, Is.Zero, "No agent faulted.");
                Assert.That(inPlace.JobBatches, Is.Zero, "UseJobs=false runs on the calling thread.");
                Assert.That(jobs.JobBatches, Is.GreaterThanOrEqualTo(Ticks), "UseJobs with MinimumJobBatch=1 runs the job.");
                Assert.That(jobs.Digests.Count, Is.EqualTo(inPlace.Digests.Count));
                for (var tick = 0; tick < inPlace.Digests.Count; tick++)
                    Assert.That(jobs.Digests[tick], Is.EqualTo(inPlace.Digests[tick]), "Jobs digest differs from in place at tick " + (tick + 1));

                foreach (var libraryCase in cases)
                {
                    var completed = inPlace.Completions[libraryCase.Name];
                    if (libraryCase.Root.HasValue)
                    {
                        Assert.That(completed, Is.Not.Empty, libraryCase.Name + " completes in the container");
                        Assert.That(completed, Is.All.EqualTo(libraryCase.Root.Value), libraryCase.Name + ": the host's root result");
                    }
                    else Assert.That(completed, Is.Empty, libraryCase.Name + " keeps running");
                    Assert.That(jobs.Completions[libraryCase.Name], Is.EqualTo(completed), libraryCase.Name + ": completions with Jobs");
                }
            }
        }
    }
}
