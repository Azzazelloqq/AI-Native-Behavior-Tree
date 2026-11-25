using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP11014Probe;
using NUnit.Framework;
using Unity.Burst;
using UnityEngine;
using UnityEngine.TestTools;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AIBT.Tests.Integration.NativeRuntime
{
    // P11-014 review follow-up (item 1): the built-in leaves, the package's native library and the probe node.
    [AibtCatalogSet("aibt.tests.p11014.burst-probe", 1u, typeof(BuiltInLeafShard), typeof(StandardNativeShard), typeof(BurstProbeFixtureShard))]
    public static partial class BurstProbeCatalog { }

    /// <summary>
    /// P11-014 review follow-up (item 1). Which scheduler paths run a native node's body Burst-compiled in
    /// the Editor, observed from inside the body (<c>aibt.tests.burst-probe</c>: a <c>[BurstDiscard]</c>
    /// call that only managed code executes), and a bit-for-bit comparison of
    /// <c>aibt.native.random-float3-in-radius</c> on a path observed Burst-compiled with a path observed
    /// managed. A path is updated, one Editor frame apart, until the probe reports Burst or a bound passes.
    /// Cold Burst cache (consumer report on 0.2.0): the generated agent-frame job carries
    /// <c>[BurstCompile]</c> without <c>CompileSynchronously</c>, so the Editor compiles it asynchronously
    /// on first use and runs it managed until the compilation ends; on a fresh <c>Library</c> that took
    /// longer than the old 6000-frame bound. The class therefore turns on
    /// <c>BurstCompiler.Options.EnableBurstCompileSynchronously</c> for its own run (only when Burst is
    /// enabled and the option is off) and restores it in <see cref="RestoreCompilation"/>: a job whose
    /// compilation is requested while it is on is compiled before it first runs, cold cache or warm. A job
    /// whose compilation was requested earlier in the domain is still waited for, by seconds, not frames.
    /// A path that never reports Burst within the bound fails as before.
    /// </summary>
    public sealed class BurstExecutionPathTests
    {
        private const string S = "aibt.native.";
        private const int ManagedTally = 1;
        private const int BurstTally = 1000;
        private const int MinimumFrames = 120;
        private const double MinimumSeconds = 8.0;
        // The wait for the path expected to compile. With synchronous compilation on, the first frame is
        // normally Burst already; the bound covers a compilation that was requested asynchronously before
        // this class ran. It is counted in seconds only: on a consumer's machine the old frame cap ended the
        // wait after 6000 frames, whatever the seconds.
        private const double CompileWaitSeconds = 300.0;
        // Above the test runner's default of 180 s: one compile wait, six other paths, and the frames recorded.
        private const int TestTimeoutMilliseconds = 900_000;
        private const int SettledFrames = 10;
        private const int RecordedFrames = 240;
        private const ulong Seed = 20261124ul;
        private const ulong StreamId = 77ul;

        private readonly List<Object> _created = new List<Object>();
        private readonly List<NativeTreeAgent> _agents = new List<NativeTreeAgent>();
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;
        private bool _restoreSynchronous;

        /// <summary>
        /// Turns on synchronous Burst compilation for this class when Burst is enabled and the option is off.
        /// Burst writes its options to EditorPrefs on every change (<c>BurstEditorOptions.GlobalOnOptionsChanged</c>,
        /// in Burst 1.8.29 and in the built-in Burst 2.0.0 of 7000.0.0a8), so the old value is written back in
        /// <see cref="RestoreCompilation"/>. The option triggers no recompilation in either.
        /// </summary>
        [OneTimeSetUp]
        public void CompileSynchronously()
        {
            if (!BurstCompiler.IsEnabled || BurstCompiler.Options.EnableBurstCompileSynchronously) return;
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            _restoreSynchronous = true;
        }

        /// <summary>Restores the synchronous-compilation option this class turned on.</summary>
        [OneTimeTearDown]
        public void RestoreCompilation()
        {
            if (!_restoreSynchronous) return;
            _restoreSynchronous = false;
            BurstCompiler.Options.EnableBurstCompileSynchronously = false;
        }

        [TearDown]
        public void TearDown()
        {
            DestroyWorld();
            foreach (var created in _created) if (created != null) Object.DestroyImmediate(created);
            _created.Clear();
        }

        /// <summary>
        /// Every production path that runs a node body, with the probe as the only leaf after a wait. Each
        /// path is updated until the probe has reported Burst for <see cref="SettledFrames"/> frames in a
        /// row, or until at least <see cref="MinimumFrames"/> frames and <see cref="MinimumSeconds"/>
        /// seconds (<see cref="CompileWaitSeconds"/> for the path expected to compile) have passed. The
        /// seconds only bound the wait; nothing is concluded from them. The table is in the log; the test
        /// pins it, so a change of a path's compilation shows here.
        /// </summary>
        [UnityTest, Timeout(TestTimeoutMilliseconds)]
        public IEnumerator EveryPath_ReportsFromInsideTheNodeBody_WhetherItRunsBurstCompiled()
        {
            Assume.That(BurstCompiler.IsEnabled, Is.True, "Burst compilation is off in this Editor (Jobs > Burst > Enable Compilation)");
            var asset = Prepare(ProbeTree("tests.p11014.burst-path-probe", roll: null));
            var expected = new (string Path, int Agents, bool Burst)[]
            {
                // Measured first: its first schedule requests the compilation of the catalog's agent-frame job
                // (synchronous while this class runs, unless it was requested earlier in the domain).
                ("agent-frame-job", 1, true),
                ("agent-frame-in-place", 1, false),
                ("immediate", 1, false),
                ("batched-jobs-same-frame", 1, false),
                ("batched-jobs-same-frame", 4, false),
                ("pipelined-jobs", 1, false),
                ("pipelined-jobs", 4, false),
            };
            var rows = new List<string>();
            var mismatches = new List<string>();
            foreach (var path in expected)
            {
                var outcome = new ProbeOutcome();
                yield return Probe(asset, path.Path, path.Agents, outcome, path.Burst ? CompileWaitSeconds : MinimumSeconds);
                var row = path.Path + " x" + path.Agents + ": " + (outcome.Burst ? "Burst" : "managed") + " (" + outcome.Frames + " frames, "
                    + outcome.Seconds.ToString("0.0") + " s, first Burst frame " + (outcome.FirstBurstFrame < 0 ? "none" : outcome.FirstBurstFrame.ToString())
                    + ", managed frames " + outcome.ManagedFrames + ", Burst frames " + outcome.BurstFrames + ")";
                rows.Add(row);
                if (outcome.Burst != path.Burst) mismatches.Add(row + ", expected " + (path.Burst ? "Burst" : "managed"));
            }
            Debug.Log("P11-014 item 1, execution paths (Burst enabled: " + BurstCompiler.IsEnabled + ", " + Compilation() + "):\n" + string.Join("\n", rows));
            Assert.That(mismatches, Is.Empty, Compilation() + "\n" + string.Join("\n", mismatches));
        }

        /// <summary>
        /// <c>aibt.native.random-float3-in-radius</c>, flat: one agent (fixed seed and stream id) records
        /// <see cref="RecordedFrames"/> written points on the JobsAgentFrame job, where the probe in the same
        /// tree reports Burst in every recorded frame, and on the Immediate path, where it reports managed
        /// code in every recorded frame. The two point sequences are compared bit for bit.
        /// </summary>
        [UnityTest, Timeout(TestTimeoutMilliseconds)]
        public IEnumerator RandomFloat3InRadius_Flat_IsBitIdentical_BurstCompiledAndManaged()
        {
            yield return CompareBurstWithManaged(flat: true);
        }

        /// <summary>As the flat case, with points in a ball (<c>flat: false</c>).</summary>
        [UnityTest, Timeout(TestTimeoutMilliseconds)]
        public IEnumerator RandomFloat3InRadius_3D_IsBitIdentical_BurstCompiledAndManaged()
        {
            yield return CompareBurstWithManaged(flat: false);
        }

        private IEnumerator CompareBurstWithManaged(bool flat)
        {
            Assume.That(BurstCompiler.IsEnabled, Is.True, "Burst compilation is off in this Editor (Jobs > Burst > Enable Compilation)");
            var roll = Leaf("roll", S + "random-float3-in-radius",
                "\"min-radius\": 0.5, \"radius\": 7.25, \"flat\": " + (flat ? "true" : "false"),
                "\"center\": \"c\", \"result\": \"r\"");
            var asset = Prepare(ProbeTree("tests.p11014.burst-parity-" + (flat ? "flat" : "3d"), roll));

            // Warm-up: a throwaway agent on the JobsAgentFrame job until its body runs Burst-compiled.
            var warm = new ProbeOutcome();
            yield return Probe(asset, "agent-frame-job", 1, warm, CompileWaitSeconds);
            Assert.That(warm.Burst, Is.True, "the JobsAgentFrame job ran the node body Burst-compiled within the bound ("
                + warm.Frames + " frames, " + warm.Seconds.ToString("0.0") + " s, " + Compilation() + ")");

            var burst = Record(asset, "agent-frame-job", BurstTally);
            var managed = Record(asset, "immediate", ManagedTally);

            Assert.That(burst.Count, Is.EqualTo(managed.Count));
            var differing = 0;
            var first = -1;
            for (var index = 0; index < managed.Count; index++)
            {
                if (managed[index] == burst[index]) continue;
                differing++;
                if (first < 0) first = index;
            }
            var distinct = new HashSet<(int, int, int)>(managed).Count;
            Debug.Log("P11-014 item 1 parity (" + (flat ? "flat" : "3D") + "): " + managed.Count + " frames, " + distinct
                + " distinct points, " + differing + " differ bit for bit" + (first < 0 ? "" : ", first at frame " + first
                    + ": managed " + Text(managed[first]) + ", Burst " + Text(burst[first]))
                + "; warm-up reached Burst at frame " + warm.FirstBurstFrame);
            Assert.That(distinct, Is.GreaterThan(RecordedFrames / 4), "the node drew many points");
            Assert.That(differing, Is.Zero, "the Burst-compiled and the managed node body write the same bits");
        }

        /// <summary>
        /// One agent (fixed seed and stream id) on <paramref name="path"/> for <see cref="RecordedFrames"/>
        /// frames; every frame the probe must have added <paramref name="tally"/> (one tick, compiled as
        /// expected) and the written point is recorded as bits.
        /// </summary>
        private List<(int, int, int)> Record(ProductionTreeAsset asset, string path, int tally)
        {
            var agents = Spawn(asset, path, 1);
            var agent = agents[0];
            var points = new List<(int, int, int)>();
            var previous = 0;
            for (var frame = 0; frame < RecordedFrames + 1; frame++)
            {
                Frame(agents);
                Assert.That(agent.LastError, Is.Null, path + " frame " + frame);
                Assert.That(agent.TryGetValue("tally", out int now), Is.True, path + " frame " + frame);
                if (frame == 0)
                {
                    // The first update only starts the wait; the probe has not run yet.
                    Assert.That(now, Is.Zero, path + ": the probe ran in the first update");
                    previous = now;
                    continue;
                }
                Assert.That(now - previous, Is.EqualTo(tally), path + " frame " + frame + ": the probe's tally step (1 managed, 1000 Burst)");
                previous = now;
                Assert.That(agent.TryGetValue("r", out Float3Value r), Is.True, path + " frame " + frame);
                points.Add((BitConverter.SingleToInt32Bits(r.X), BitConverter.SingleToInt32Bits(r.Y), BitConverter.SingleToInt32Bits(r.Z)));
            }
            DestroyWorld();
            return points;
        }

        private sealed class ProbeOutcome
        {
            internal bool Burst;
            internal int Frames;
            internal double Seconds;
            internal int FirstBurstFrame = -1;
            internal int ManagedFrames;
            internal int BurstFrames;
        }

        private IEnumerator Probe(ProductionTreeAsset asset, string path, int count, ProbeOutcome outcome, double waitSeconds = MinimumSeconds)
        {
            var agents = Spawn(asset, path, count);
            var previous = new int[count];
            var settled = 0;
            var clock = Stopwatch.StartNew();
            // No frame cap: the bound is MinimumFrames and waitSeconds, both passed.
            for (var frame = 0; ; frame++)
            {
                Frame(agents);
                var burstFrame = true;
                var ticked = 0;
                for (var index = 0; index < count; index++)
                {
                    Assert.That(agents[index].LastError, Is.Null, path + " agent " + index + " frame " + frame);
                    // PipelinedJobs refuses reads while the host executes between frames; read it next frame.
                    if (!agents[index].TryGetValue("tally", out int now))
                    {
                        Assert.That(path, Is.EqualTo("pipelined-jobs"), path + " agent " + index + " frame " + frame + ": tally unreadable");
                        continue;
                    }
                    var step = now - previous[index];
                    previous[index] = now;
                    if (step == 0) continue;
                    ticked++;
                    // Several ticks may fall between two reads (PipelinedJobs): all Burst only if a multiple of 1000.
                    if (step % BurstTally != 0) burstFrame = false;
                }
                if (ticked > 0)
                {
                    if (burstFrame)
                    {
                        outcome.BurstFrames++;
                        if (outcome.FirstBurstFrame < 0) outcome.FirstBurstFrame = frame;
                        settled++;
                    }
                    else
                    {
                        outcome.ManagedFrames++;
                        settled = 0;
                    }
                }
                outcome.Frames = frame + 1;
                outcome.Seconds = clock.Elapsed.TotalSeconds;
                if (settled >= SettledFrames) break;
                if (frame + 1 >= MinimumFrames && outcome.Seconds >= waitSeconds) break;
                // One Editor frame between updates, so the asynchronous Burst compilation can finish.
                yield return null;
            }
            outcome.Burst = settled >= SettledFrames;
            Assert.That(outcome.ManagedFrames + outcome.BurstFrames, Is.GreaterThan(0), path + ": the probe ran");
            DestroyWorld();
        }

        /// <summary>How this run compiled: whether this class turned synchronous compilation on, or found it on.</summary>
        private string Compilation()
            => "synchronous Burst compilation " + (_restoreSynchronous ? "on for this class" : BurstCompiler.Options.EnableBurstCompileSynchronously ? "already on" : "off");

        private static string ProbeTree(string treeId, string roll)
        {
            var children = roll == null ? new[] { "pace", "probe" } : new[] { "pace", "probe", "roll" };
            var nodes = new List<string>
            {
                CompositeWith("loop", "aibt.core.repeater", "\"count\": 4000000000, \"stopOnFailure\": false", "cycle"),
                Composite("cycle", "aibt.core.memory-sequence", children),
                Leaf("pace", "aibt.stdlib.wait", "\"ticks\": 2"),
                Leaf("probe", "aibt.tests.burst-probe", null, "\"tally\": \"tally\""),
            };
            if (roll != null) nodes.Add(roll);
            return Tree(treeId, "loop", new[] { Int("tally"), Float3("c", 10f, 1.5f, -4f), Float3("r") }, nodes.ToArray());
        }

        private ProductionTreeAsset Prepare(string json)
        {
            var standard = NativeLibraryKit.Materialize(typeof(StandardNativeShard));
            var nodes = NativeLibraryKit.Materialize(typeof(BuiltInLeafShard)).Nodes
                .Concat(standard.Nodes)
                .Concat(NativeLibraryKit.Materialize(typeof(BurstProbeFixtureShard)).Nodes)
                .ToArray();
            const string sourceId = "Tests/P11014/burst-probe.aibt.json";
            var parsed = CanonicalTreeJson.Parse(json, sourceId);
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)) + "\n" + json);
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, standard.RegisteredTypes,
                new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)) + "\n" + json);
            var asset = ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(standard.RegisteredTypes), BurstProbeCatalog.Fingerprint);
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(BurstProbeCatalog).FullName + ", " + typeof(BurstProbeCatalog).Assembly.GetName().Name);
            _created.Add(asset);
            return asset;
        }

        private List<NativeTreeAgent> Spawn(ProductionTreeAsset asset, string path, int count)
        {
            DestroyWorld();
            var worldObject = Track(new GameObject("AIBT.Tests.P11014.BurstPath.World"));
            _scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            _world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(_world, "Awake");
            InvokePrivate(_world, "OnEnable");
            var profile = ProfileFor(path);
            var agents = new List<NativeTreeAgent>();
            for (var index = 0; index < count; index++)
            {
                var gameObject = Track(new GameObject("AIBT.Tests.P11014.BurstPath.Agent" + index));
                var agent = gameObject.AddComponent<NativeTreeAgent>();
                agent.Tree = asset;
                agent.World = _world;
                agent.RestartMode = NativeTreeRestartMode.Never;
                agent.RandomSeed = Seed;
                agent.RandomStreamId = StreamId + (ulong)index;
                agent.CatalogFactory = BurstProbeCatalog.TryCreateRuntimeCatalog;
                agent.Profile = profile;
                _agents.Add(agent);
                agents.Add(agent);
                agent.Activate();
                Assert.That(agent.LastError, Is.Null, path + " agent " + index + " started");
            }
            return agents;
        }

        private SchedulingProfile ProfileFor(string path)
        {
            switch (path)
            {
                case "immediate":
                    return Forced(SchedulingPolicy.Immediate, false);
                case "batched-jobs-same-frame":
                    _scheduler.SetJobsCapabilities(Capabilities(pipelined: false));
                    return Forced(SchedulingPolicy.BatchedJobsSameFrame, false);
                case "pipelined-jobs":
                    _scheduler.SetJobsCapabilities(Capabilities(pipelined: true));
                    return Forced(SchedulingPolicy.PipelinedJobs, true);
                case "agent-frame-job":
                    _scheduler.SetJobsCapabilities(Capabilities(pipelined: false));
                    _scheduler.AgentFrameMinimumBatchSize = 1;
                    return Forced(SchedulingPolicy.JobsAgentFrame, false);
                case "agent-frame-in-place":
                    // Below the minimum batch (the default 64) a JobsAgentFrame batch runs each member in place.
                    _scheduler.SetJobsCapabilities(Capabilities(pipelined: false));
                    Assert.That(_scheduler.AgentFrameMinimumBatchSize, Is.EqualTo(ProductionTreeScheduler.DefaultAgentFrameMinimumBatchSize));
                    return Forced(SchedulingPolicy.JobsAgentFrame, false);
                default:
                    throw new ArgumentOutOfRangeException(nameof(path), path, null);
            }
        }

        private static SchedulingProfile Forced(SchedulingPolicy policy, bool pipelining)
        {
            Assert.That(SchedulingProfile.TryCreate("tests.p11014.burst-path." + policy, 0, 1u, null, null, pipelining, policy,
                out var profile, out var error), Is.True, error.ToString());
            return profile;
        }

        private static SchedulerJobsCapabilities Capabilities(bool pipelined)
        {
            Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, pipelined, out var capabilities, out var error),
                Is.True, error.ToString());
            return capabilities;
        }

        private void Frame(List<NativeTreeAgent> agents)
        {
            InvokePrivate(_scheduler, "Update");
            foreach (var agent in agents) agent.ProcessRootResult();
        }

        private static string Text((int X, int Y, int Z) bits)
            => "(" + BitConverter.Int32BitsToSingle(bits.X).ToString("R") + ", " + BitConverter.Int32BitsToSingle(bits.Y).ToString("R")
               + ", " + BitConverter.Int32BitsToSingle(bits.Z).ToString("R") + ")";

        private void DestroyWorld()
        {
            foreach (var agent in _agents) if (agent != null) agent.Deactivate();
            _agents.Clear();
            for (var index = _created.Count - 1; index >= 0; index--)
            {
                if (_created[index] is ProductionTreeAsset) continue;
                if (_created[index] != null) Object.DestroyImmediate(_created[index]);
                _created.RemoveAt(index);
            }
            _scheduler = null;
            _world = null;
        }

        private T Track<T>(T value) where T : Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
