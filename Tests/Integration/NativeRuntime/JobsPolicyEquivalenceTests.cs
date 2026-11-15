using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.StandardNodes;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    // Built-in tick leaves, the package's standard native nodes and the P7-039 fact/effect fixture
    // nodes in one project catalog, so one scheduler group can hold every tree below.
    [AibtCatalogSet("aibt.tests.p10002.jobs-equivalence", 1u, typeof(BuiltInLeafShard), typeof(StandardNativeShard), typeof(ExternalTreeWriteFixtureShard))]
    public static partial class JobsEquivalenceCatalog { }

    /// <summary>
    /// P10-002 safety net for the Jobs speed-up: the same population of <see cref="NativeTreeAgent"/>s
    /// with the same scripted facts runs under the default profile, a forced <c>Immediate</c> profile
    /// and a forced <c>BatchedJobsSameFrame</c> profile, and every frame's observable outcome must be
    /// identical: each agent's trace records (kind, update, sequence, node, status, exit and abort
    /// reason), root result, running state and completed-run count, and the order and payload of
    /// every delivered effect. The trees restart, abort reactively, run a parallel, loop, read facts
    /// written between frames and emit effects. Async completions are not covered: the facade does
    /// not wire them (generated group dispatch passes an empty completion set).
    /// </summary>
    public sealed class JobsPolicyEquivalenceTests
    {
        private const int FrameCount = 90;
        private const int AgentsPerTree = 4;

        // Completes every few updates and restarts; fails while the fact is not positive.
        private const string SequenceTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10002.sequence"", ""name"": ""P10-002 sequence"",
  ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""score"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""read"", ""wait"", ""set"", ""check""] },
    ""read"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""wait"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""set"": { ""type"": ""aibt.native.set-float"", ""typeVersion"": 1, ""parameters"": { ""value"": 2.5 }, ""bindings"": { ""key"": ""score"" } },
    ""check"": { ""type"": ""aibt.native.float-at-least"", ""typeVersion"": 1, ""parameters"": { ""threshold"": 2.0 }, ""bindings"": { ""key"": ""score"" } }
  }
}";

        // A reactive guard over a running effect emitter. The reactive sequence re-evaluates the guard
        // and resets the running branch every update (abort, then enter again); clearing 'armed' fails it.
        private const string ReactiveEffectTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10002.reactive-effect"", ""name"": ""P10-002 reactive effect"",
  ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""armed"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": false }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.reactive-sequence"", ""typeVersion"": 1, ""children"": [""armed"", ""emit""] },
    ""armed"": { ""type"": ""aibt.native.bool-is-true"", ""typeVersion"": 1, ""bindings"": { ""key"": ""armed"" } },
    ""emit"": { ""type"": ""aibt.tests.external-tree-effect"", ""typeVersion"": 1, ""parameters"": { ""value"": 17 }, ""bindings"": { ""live-target"": ""live-target"" } }
  }
}";

        // Never finishes: a repeater over a parallel whose branches complete at different updates.
        // Every iteration holds for at least one update (the reader is wrapped in a succeeder), so
        // the repeater never spins inside one update.
        private const string LoopTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p10002.loop"", ""name"": ""P10-002 loop"",
  ""root"": ""loop"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 }
  },
  ""nodes"": {
    ""loop"": { ""type"": ""aibt.core.repeater"", ""typeVersion"": 1, ""children"": [""both""], ""parameters"": { ""count"": 4000000000, ""stopOnFailure"": false } },
    ""both"": { ""type"": ""aibt.core.parallel"", ""typeVersion"": 1, ""parameters"": { ""policy"": ""require-all-success"" }, ""children"": [""choose"", ""observe"", ""hold""] },
    ""choose"": { ""type"": ""aibt.core.memory-selector"", ""typeVersion"": 1, ""children"": [""gate"", ""patrol""] },
    ""gate"": { ""type"": ""aibt.core.failer"", ""typeVersion"": 1, ""children"": [""gate-wait""] },
    ""gate-wait"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 1 } },
    ""patrol"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 3 } },
    ""observe"": { ""type"": ""aibt.core.succeeder"", ""typeVersion"": 1, ""children"": [""read""] },
    ""read"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } }
  }
}";

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private int _baselinePrograms;

        [SetUp]
        public void SetUp() => _baselinePrograms = NativeTreeProgramCache.LiveProgramCount;

        [TearDown]
        public void TearDown()
        {
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) UnityEngine.Object.DestroyImmediate(_created[index]);
            _created.Clear();
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms), "A test leaked a shared program.");
        }

        [Test]
        public void BatchedJobsSameFrame_IsObservablyIdenticalToImmediate_FrameByFrame()
        {
            var trees = new[] { Prepare(SequenceTree), Prepare(ReactiveEffectTree), Prepare(LoopTree) };

            var normal = Run(trees, Mode.DefaultProfile);
            var immediate = Run(trees, Mode.ForcedImmediate);
            var batched = Run(trees, Mode.ForcedBatchedJobs);

            // The scenario must actually exercise what it claims to compare.
            Assert.That(normal.Effects, Is.GreaterThan(20), "Effects are delivered.");
            Assert.That(normal.Restarts, Is.GreaterThan(20), "Trees complete and restart.");
            Assert.That(normal.Lines.Count(l => l.Contains(" NodeAbortStarted ")), Is.GreaterThan(5), "Reactive aborts happen.");
            Assert.That(normal.Lines.Count(l => l.Contains(" result=Failure")), Is.GreaterThan(5), "Trees also fail.");

            AssertSame(normal, immediate, "forced Immediate");
            AssertSame(normal, batched, "forced BatchedJobsSameFrame");
        }

        // P10-003: per-frame SHA-256 (first 16 hex digits) of every observation of
        // AllPolicies_MatchTheOutcomeRecordedBeforeP10003_FrameByFrame, recorded on the runtime at
        // 4dee8ec (before any dispatch-framing change). One entry per frame.
        private static readonly string[] RecordedFrameDigests =
        {
            "c3c9c77fed8a3bc4",
            "e5a2a7c36ab1abc5",
            "f0ff5c40cd0c5c03",
            "c4171b60b0b9bfc4",
            "a9a8c91f446fb7ef",
            "1f79d53aaff309cb",
            "8b32f1691b63f96c",
            "cf66a06ae5a38651",
            "efd7d1289d2299fa",
            "fd5188ddc400c199",
            "fc153ed0b4660e44",
            "5f85ffaad9b6ac0b",
            "ab75f45088cc0dd6",
            "2da71c4f73b276b3",
            "fc898ca96975a0d8",
            "fe2e5c8987d92955",
            "d9e5da0c094538a7",
            "21f8b950b85510ed",
            "a4c38db82a779830",
            "65afc200546062f3",
            "9f0abbd5b3f7f9c8",
            "603c3da882e3094f",
            "be05332f4393d830",
            "8d0281b2405942e8",
            "61707468bce35c58",
            "01b84ddad2495ef0",
            "bda6d27d6264623b",
            "79bb4af1e082e4f7",
            "f38b42fe785fb202",
            "8eb78fdf212dc531",
            "49a98b2c0a0b385f",
            "2d5b8b992356a81e",
            "e1338d69415725d3",
            "933ef304dbfcce6f",
            "6c3edc5ebf11278b",
            "a6c09a78db6f9d77",
            "af6e05441a54d7ca",
            "fa0ca447bb1310b5",
            "97a83bade0612f92",
            "1f36bfe61f309934",
            "b4183933e6d18cfe",
            "903ccc0129d27a00",
            "c3ab1e936eb3f64d",
            "9ec66ba0e27670e3",
            "03a5f842afba1f81",
            "bf87f4781cd74a11",
            "2975a48d7a7a572a",
            "133d527894d8e348",
            "f71008c14b8ee209",
            "9ef6a9355280ef04",
            "2cc9183681aa8c04",
            "6411a3b99e60553b",
            "c30b1b06151e1142",
            "8692a56d4a4de39d",
            "421ff254f43d9e6c",
            "9fc723f76d2138d7",
            "09d7860986865c02",
            "ba515516901592bf",
            "b777d3e30cbcb218",
            "05c8740ea6c71085",
            "9069cd5e6378fdc5",
            "b65e89a9c6cd249c",
            "0815a04e09037016",
            "b4df66ff3b2bd716",
            "4afdffcdaebb99d3",
            "9fdb9ee1e92be6f5",
            "67b9744cff58c628",
            "e1c457e95b46bdf0",
            "9cede807208c73bf",
            "801d14dd5e58d83e",
            "a425c13338143a9b",
            "908bdeb07679e350",
            "89139f89a9e1c23a",
            "156f4ec97a10fe22",
            "07c81e69f6200966",
            "75436d7bf77269e8",
            "841eff6b0fc1e168",
            "31e028c6c36eb076",
            "455b00e73f513ec1",
            "af840e72c58dfbef",
            "56187fe463fe6fa8",
            "4a263aed90d635ad",
            "6cb326e213c84cdc",
            "c297280220561e87",
            "c67fdfd76351c0a7",
            "ccf58bb55dffd4d6",
            "dbed7e2c2ceef819",
            "a382baa71e6f8e94",
            "d9e8798d4bb4b948",
            "ee37d7c298f576aa"
        };

        [Test]
        public void AllPolicies_MatchTheOutcomeRecordedBeforeP10003_FrameByFrame()
        {
            // P10-003 guard for the dispatch-framing optimizations: the observable outcome of every
            // frame (trace records, root result, running state, run count, error, delivered effects
            // and every byte of each agent's Tree/Agent blackboard storage) must equal what the
            // unoptimized runtime produced, under all three profiles.
            var trees = new[] { Prepare(SequenceTree), Prepare(ReactiveEffectTree), Prepare(LoopTree) };
            foreach (var mode in new[] { Mode.DefaultProfile, Mode.ForcedImmediate, Mode.ForcedBatchedJobs })
            {
                var outcome = Run(trees, mode, withTreeValues: true);
                var digests = FrameDigests(outcome.Lines);
                Assert.That(digests.Count, Is.EqualTo(FrameCount), mode + ": frames observed");
                if (RecordedFrameDigests.Length == 0)
                    Assert.Fail("No recorded digests. Recorded now (" + mode + "):\n"
                        + string.Join(",\n", digests.Select(d => "            \"" + d + "\"")));
                for (var frame = 0; frame < FrameCount; frame++)
                    if (digests[frame] != RecordedFrameDigests[frame])
                        Assert.Fail(mode + ": frame " + frame + " differs from the recorded outcome.\n"
                            + string.Join("\n", outcome.Lines.Where(l => l.StartsWith("f" + frame + " ", StringComparison.Ordinal)).Take(40)));
            }
        }

        private static List<string> FrameDigests(List<string> lines)
        {
            var digests = new List<string>();
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var frame = 0;
                var text = new StringBuilder();
                void Flush()
                {
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                    digests.Add(BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant());
                    text.Clear();
                }
                foreach (var line in lines)
                {
                    var space = line.IndexOf(' ');
                    var lineFrame = int.Parse(line.Substring(1, space - 1), System.Globalization.CultureInfo.InvariantCulture);
                    while (lineFrame > frame) { Flush(); frame++; }
                    text.Append(line).Append('\n');
                }
                Flush();
            }
            return digests;
        }

        [Test]
        public void WarmedUpImmediateFrames_DoNotAllocateManagedMemory()
        {
            // P10-003: the same population as the BatchedJobsSameFrame allocation test below, driven
            // through NativeTreeAgent under a forced Immediate profile (single-host dispatch path).
            var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            recorder.FilterToCurrentThread();
            object kept = null;
            Assert.That(CountAllocations(recorder, () => kept = new byte[64], 10), Is.EqualTo(10), "The recorder must see allocations.");
            Assert.That(kept, Is.Not.Null);

            var trees = new[] { Prepare(LoopTree), Prepare(ReactiveEffectTree) };
            var worldObject = Track(new GameObject("AIBT.Tests.P10003.AllocationWorld"));
            var scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            var world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(world, "Awake");
            InvokePrivate(world, "OnEnable");
            var profile = Forced(SchedulingPolicy.Immediate);
            var update = (Action)Delegate.CreateDelegate(typeof(Action), scheduler,
                typeof(ProductionTreeScheduler).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic));

            var agents = new List<NativeTreeAgent>();
            var effects = 0;
            try
            {
                for (var index = 0; index < 32; index++)
                {
                    var agent = Track(new GameObject("AIBT.Tests.P10003.AllocationAgent" + index)).AddComponent<NativeTreeAgent>();
                    agent.Tree = trees[index % trees.Length];
                    agent.World = world;
                    agent.RestartMode = NativeTreeRestartMode.Never;
                    agent.Profile = profile;
                    agent.OnEffect("hit", (int value) => effects++);
                    agent.Activate();
                    agent.SetFact("live-target", 1f);
                    if (index % trees.Length == 1) agent.SetFact("armed", true);
                    agents.Add(agent);
                }
                for (var frame = 0; frame < 60; frame++) update();
                var effectsBefore = effects;

                var allocations = CountAllocations(recorder, update, 200);

                Assert.That(effects - effectsBefore, Is.EqualTo(200 * 16), "Every emitter delivers one effect per frame while measured.");
                foreach (var agent in agents)
                {
                    Assert.That(agent.LastError, Is.Null);
                    Assert.That(agent.IsRunning, Is.True, "The trees keep running; nothing restarts.");
                }
                Assert.That(scheduler.TryGetFrameEntry(0, out var entry) && entry.SelectedPolicy == SchedulingPolicy.Immediate, Is.True,
                    "The frame ran under Immediate.");
                Assert.That(allocations, Is.Zero,
                    allocations + " managed allocation(s) in 200 warmed-up Immediate frames; the tolerance is zero.");
            }
            finally
            {
                foreach (var agent in agents) if (agent != null) agent.Deactivate();
            }
        }

        [Test]
        public void WarmedUpBatchedJobsFrames_DoNotAllocateManagedMemory()
        {
            // P10-002: a BatchedJobsSameFrame frame of trees that do not restart (a looping parallel
            // and a reactive effect emitter) allocates nothing once warmed up: round lists, machine
            // array, lane storage and dispatch scratch are reused. Allocations are counted with the
            // profiler's GC.Alloc recorder on this thread; the tolerance is zero.
            var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            recorder.FilterToCurrentThread();
            object kept = null;
            Assert.That(CountAllocations(recorder, () => kept = new byte[64], 10), Is.EqualTo(10), "The recorder must see allocations.");
            Assert.That(kept, Is.Not.Null);

            var trees = new[] { Prepare(LoopTree), Prepare(ReactiveEffectTree) };
            var worldObject = Track(new GameObject("AIBT.Tests.P10002.AllocationWorld"));
            var scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            var world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(world, "Awake");
            InvokePrivate(world, "OnEnable");
            Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, false, out var capabilities, out var error),
                Is.True, error.ToString());
            scheduler.SetJobsCapabilities(capabilities);
            var profile = Forced(SchedulingPolicy.BatchedJobsSameFrame);
            var update = (Action)Delegate.CreateDelegate(typeof(Action), scheduler,
                typeof(ProductionTreeScheduler).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic));

            var agents = new List<NativeTreeAgent>();
            var effects = 0;
            try
            {
                for (var index = 0; index < 32; index++)
                {
                    var agent = Track(new GameObject("AIBT.Tests.P10002.AllocationAgent" + index)).AddComponent<NativeTreeAgent>();
                    agent.Tree = trees[index % trees.Length];
                    agent.World = world;
                    agent.RestartMode = NativeTreeRestartMode.Never;
                    agent.Profile = profile;
                    agent.OnEffect("hit", (int value) => effects++);
                    agent.Activate();
                    agent.SetFact("live-target", 1f);
                    if (index % trees.Length == 1) agent.SetFact("armed", true);
                    agents.Add(agent);
                }
                for (var frame = 0; frame < 60; frame++) update();
                var effectsBefore = effects;

                var allocations = CountAllocations(recorder, update, 200);

                Assert.That(effects - effectsBefore, Is.EqualTo(200 * 16), "Every emitter delivers one effect per frame while measured.");
                foreach (var agent in agents)
                {
                    Assert.That(agent.LastError, Is.Null);
                    Assert.That(agent.IsRunning, Is.True, "The trees keep running; nothing restarts.");
                }
                Assert.That(scheduler.FrameEntryCount, Is.EqualTo(agents.Count));
                Assert.That(scheduler.TryGetFrameEntry(0, out var entry) && entry.SelectedPolicy == SchedulingPolicy.BatchedJobsSameFrame, Is.True,
                    "The frame ran under BatchedJobsSameFrame.");
                Assert.That(allocations, Is.Zero,
                    allocations + " managed allocation(s) in 200 warmed-up BatchedJobsSameFrame frames; the tolerance is zero.");
            }
            finally
            {
                foreach (var agent in agents) if (agent != null) agent.Deactivate();
            }
        }

        private static int CountAllocations(UnityEngine.Profiling.Recorder recorder, Action action, int calls)
        {
            recorder.enabled = false;
            recorder.enabled = true;
            for (var index = 0; index < calls; index++) action();
            recorder.enabled = false;
            return recorder.sampleBlockCount;
        }

        private enum Mode { DefaultProfile, ForcedImmediate, ForcedBatchedJobs }

        private sealed class Outcome
        {
            internal readonly List<string> Lines = new List<string>();
            internal int Effects;
            internal int Restarts;
        }

        private static void AssertSame(Outcome expected, Outcome actual, string what)
        {
            var count = Math.Min(expected.Lines.Count, actual.Lines.Count);
            for (var index = 0; index < count; index++)
            {
                if (expected.Lines[index] != actual.Lines[index])
                    Assert.Fail(what + ": first difference at line " + index + "\n expected: " + expected.Lines[index]
                        + "\n   actual: " + actual.Lines[index] + "\n" + Context(expected.Lines, index));
            }
            Assert.That(actual.Lines.Count, Is.EqualTo(expected.Lines.Count), what + ": number of observations");
        }

        private static string Context(List<string> lines, int index)
        {
            var text = new StringBuilder("preceding expected lines:\n");
            for (var line = Math.Max(0, index - 6); line < index; line++) text.Append("  ").Append(lines[line]).Append('\n');
            return text.ToString();
        }

        private Outcome Run(ProductionTreeAsset[] trees, Mode mode, bool withTreeValues = false)
        {
            var outcome = new Outcome();
            var worldObject = Track(new GameObject("AIBT.Tests.P10002.World." + mode));
            var scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            var world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(world, "Awake");
            InvokePrivate(world, "OnEnable");
            SchedulingProfile profile = null;
            if (mode == Mode.ForcedImmediate) profile = Forced(SchedulingPolicy.Immediate);
            if (mode == Mode.ForcedBatchedJobs)
            {
                Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, false, out var capabilities, out var error),
                    Is.True, error.ToString());
                scheduler.SetJobsCapabilities(capabilities);
                profile = Forced(SchedulingPolicy.BatchedJobsSameFrame);
            }

            var agents = new List<NativeTreeAgent>();
            var frame = 0;
            for (var index = 0; index < trees.Length * AgentsPerTree; index++)
            {
                var gameObject = Track(new GameObject("AIBT.Tests.P10002.Agent" + index));
                var agent = gameObject.AddComponent<NativeTreeAgent>();
                agent.Tree = trees[index % trees.Length];
                agent.World = world;
                agent.RestartMode = index % trees.Length == 2 ? NativeTreeRestartMode.Never : NativeTreeRestartMode.Always;
                if (profile != null) agent.Profile = profile;
                var label = index;
                agent.OnEffect("hit", (int value) =>
                {
                    outcome.Effects++;
                    outcome.Lines.Add("f" + frame + " effect a" + label + " " + value);
                });
                agent.Activate();
                agents.Add(agent);
            }

            try
            {
                for (frame = 0; frame < FrameCount; frame++)
                {
                    for (var index = 0; index < agents.Count; index++)
                    {
                        // Facts change between frames, at a different phase per agent.
                        agents[index].SetFact("live-target", (frame + index) % 7 < 5 ? 1f : 0f);
                        if (index % trees.Length == 1) agents[index].SetFact("armed", (frame / (3 + index % 4)) % 3 != 2);
                    }
                    InvokePrivate(scheduler, "Update");
                    for (var index = 0; index < agents.Count; index++)
                    {
                        var agent = agents[index];
                        var prefix = "f" + frame + " a" + index;
                        CaptureTrace(agent.Host, prefix, outcome.Lines);
                        if (withTreeValues) CaptureTreeValues(agent.Host, prefix, outcome.Lines);
                        outcome.Lines.Add(prefix + " result=" + (agent.LastResult?.ToString() ?? "none") + " running=" + agent.IsRunning
                            + " runs=" + agent.CompletedRuns + " error=" + (agent.LastError ?? "none"));
                        var runs = agent.CompletedRuns;
                        agent.ProcessRootResult();
                        if (agent.CompletedRuns != runs && agent.RestartMode == NativeTreeRestartMode.Always) outcome.Restarts++;
                    }
                }
                Assert.That(world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None), mode.ToString());
            }
            finally
            {
                foreach (var agent in agents) if (agent != null) agent.Deactivate();
            }
            return outcome;
        }

        private static void CaptureTrace(ProductionTreeHost host, string prefix, List<string> lines)
        {
            if (host == null || host.TraceChannelOwner == null) { lines.Add(prefix + " no-host"); return; }
            var owner = host.TraceChannelOwner;
            Assert.That(owner.TryGetSnapshot(out var snapshot, out var failure), Is.True, failure.ToString());
            for (var index = 0; index < snapshot.RecordCount; index++)
            {
                var r = snapshot.Records[index];
                // Tree instance ids differ between runs (allocated per host); everything else is compared.
                lines.Add(prefix + " " + r.Kind + " u" + r.UpdateId + " s" + r.Sequence + " n" + r.RuntimeNodeIndex
                    + " o" + (int)r.OptionalFields + " st" + r.Status + " x" + r.ExitReason + " ab" + r.AbortReason
                    + " ph" + r.Phase + " w" + r.WorkerOrdinal);
            }
            Assert.That(snapshot.DroppedCount, Is.Zero, "The trace channel must hold one frame.");
            Assert.That(owner.TryReset(out failure), Is.True, failure.ToString());
        }

        private static void CaptureTreeValues(ProductionTreeHost host, string prefix, List<string> lines)
        {
            var adapter = host != null ? host.GeneratedDispatchAdapter : null;
            if (adapter == null) { lines.Add(prefix + " values none"); return; }
            var text = new StringBuilder(prefix).Append(" values ");
            for (uint offset = 0; adapter.TryReadTreeValueByte(offset, out var value); offset++)
                text.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            lines.Add(text.ToString());
        }

        private static SchedulingProfile Forced(SchedulingPolicy policy)
        {
            Assert.That(SchedulingProfile.TryCreate("tests.p10002." + policy, 0, 1u, null, null, false, policy,
                out var profile, out var error), Is.True, error.ToString());
            return profile;
        }

        private ProductionTreeAsset Prepare(string json)
        {
            var standard = Materialize(typeof(StandardNativeShard));
            var nodes = Materialize(typeof(BuiltInLeafShard)).Nodes
                .Concat(standard.Nodes)
                .Concat(Materialize(typeof(ExternalTreeWriteFixtureShard)).Nodes)
                .ToArray();
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P10002/tree.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, standard.RegisteredTypes,
                new ReferenceCompilerOptions("Tests/P10002/tree.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var asset = ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(standard.RegisteredTypes),
                JobsEquivalenceCatalog.Fingerprint);
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(JobsEquivalenceCatalog).FullName + ", " + typeof(JobsEquivalenceCatalog).Assembly.GetName().Name);
            return Track(asset);
        }

        private static GeneratedShardMetadataArtifact Materialize(Type shard)
        {
            var metadata = shard.GetNestedType("AibtGeneratedMetadata", BindingFlags.Public);
            object Field(string name) => metadata.GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return GeneratedShardMetadataMaterializer.MaterializeArtifact(
                (string)Field("ShardId"), (uint)Field("ShardVersion"), (string)Field("CanonicalDescriptorJson"),
                (string)Field("DescriptorHash"), (string)Field("ManifestRegistryJson"), (string)Field("NodeRegistryHash"));
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
