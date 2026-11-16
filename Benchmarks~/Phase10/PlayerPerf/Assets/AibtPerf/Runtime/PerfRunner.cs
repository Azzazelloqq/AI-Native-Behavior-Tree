using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AIBT.Samples.SimpleAgent;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

namespace AIBT.Perf
{
    public enum ScenarioKind { Empty, AllocProbe, Managed, Native, Realistic, ApiOnly }

    [Serializable]
    public sealed class NamedText
    {
        public string name;
        public TextAsset json;
    }

    [Serializable]
    public sealed class NamedNative
    {
        public string name;
        public ProductionTreeAsset asset;
    }

    public sealed class Scenario
    {
        public string Id;
        public ScenarioKind Kind;
        public string Tree;
        public int N;
        public FeederMode Feeder;
        public AgentTickMode TickMode = AgentTickMode.EveryFrame;
        public float Interval;
        public bool EffectHandler;
        public bool Jobs;
        public bool Pipelined;
        public bool AgentFrame;
        public bool AgentFramePointer;
        public bool Stagger;
        public int Warmup = 150;
        public int Measure = 400;
    }

    /// <summary>
    /// Runs the AIBT managed-versus-native benchmark matrix. The work of the runner itself happens in
    /// <see cref="AfterFrame"/>, after the frame bracket closed, so it is never inside a measurement.
    /// </summary>
    public sealed class PerfRunner : MonoBehaviour
    {
        public List<NamedText> managedTrees = new List<NamedText>();
        public List<NamedNative> nativeTrees = new List<NamedNative>();
        public BehaviorTreeSource guardTree;
        public BehaviorTreeSource wanderTree;
        public BehaviorTreeSource scoutTree;
        public PerfFeeder feeder;

        private readonly Dictionary<string, BehaviorTreeSource> _sources = new Dictionary<string, BehaviorTreeSource>();
        private readonly List<Scenario> _plan = new List<Scenario>();
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<BehaviorTreeAgent> _managed = new List<BehaviorTreeAgent>();
        private readonly List<NativeTreeAgent> _native = new List<NativeTreeAgent>();
        private string _out;
        private int _runs = 3;
        private int _planIndex = -1;
        private int _run;
        private int _phase;
        private int _frame;
        private Scenario _current;
        private StreamWriter _csv;
        private StringBuilder _meta;
        private ProductionTreeScheduler _scheduler;
        private ProfilerRecorder _systemMemory;
        private ProfilerRecorder _gcAllocInFrame;
        private ProfilerRecorder _mainThread;
        private SchedulingProfile _jobsProfile;
        private SchedulingProfile _pipelinedProfile;
        private long _effectStart;
        private long _spottedStart;
        private int _gcStart;
        private long _sysBefore;
        private double _createMicros;
        private long _createAlloc;
        private double _firstMicros;
        private long _firstAlloc;
        private long _heapBefore;
        private long _heapAfter;
        private long _monoBefore;
        private long _monoAfter;
        private int _faults;
        private const int AllocWindowFrames = 60;
        private int _windowGc;
        private long _windowHeap;
        private long _windowBytes;
        private long _createHeapBytes;
        private long _phaseStart;
        private int _windowFrames;
        private int _measuredFrames;
        private int _measuredGc;
        // P10-002: per-stage scheduler frame profile, present only in a Player built with
        // AIBT_SCHEDULER_FRAME_PROFILE (Invoke-Perf.ps1 -DefineSymbols); read through reflection.
        private System.Reflection.MethodInfo _profileReset;
        private System.Reflection.MethodInfo _profileSnapshot;
        private string _profile;
        // P10-004: JobsAgentFrame counters (AIBT.AgentFrameDiagnosticsV1, internal; read through reflection).
        private System.Reflection.MethodInfo _agentFrameReset;
        private System.Reflection.MethodInfo _agentFrameSnapshot;

        private void Start()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Application.runInBackground = true;
            var args = Environment.GetCommandLineArgs();
            string Arg(string name, string fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
            _out = Arg("-perfOut", Path.Combine(Application.persistentDataPath, "perf"));
            _runs = int.Parse(Arg("-perfRuns", "3"), CultureInfo.InvariantCulture);
            var only = Arg("-perfOnly", string.Empty);
            var quick = Array.IndexOf(args, "-perfQuick") >= 0;
            var set = Arg("-perfSet", "full");
            // P10-004: -perfJobWorkers 0 runs every job on the main thread (total CPU of a Jobs policy).
            var jobWorkers = Arg("-perfJobWorkers", string.Empty);
            if (jobWorkers.Length > 0) JobsUtility.JobWorkerCount = int.Parse(jobWorkers, CultureInfo.InvariantCulture);
            Directory.CreateDirectory(_out);

            _systemMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            _gcAllocInFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            // P10-001: a forced Jobs policy needs SchedulerJobsCapabilities on the world's scheduler
            // (set in Spawn); PipelinedJobs also needs pipelining permitted by the profile.
            if (!SchedulingProfile.TryCreate("aibt.perf.pipelined", 0, 1u, null, null, true, SchedulingPolicy.PipelinedJobs, out _pipelinedProfile, out var pipelinedError))
                Debug.LogError("AIBT_PERF|pipelined profile: " + pipelinedError);
            if (!SchedulingProfile.TryCreate("aibt.perf.jobs-same-frame", 0, 1u, null, null, false, SchedulingPolicy.BatchedJobsSameFrame, out _jobsProfile, out var profileError))
                Debug.LogError("AIBT_PERF|jobs profile: " + profileError);

            foreach (var entry in managedTrees)
            {
                var source = BehaviorTreeSource.FromJson(entry.json.text, entry.name);
                source.hideFlags = HideFlags.HideAndDontSave;
                _sources[entry.name] = source;
            }

            if (set == "quick") BuildQuickPlan();
            else BuildPlan(quick);
            var profileType = typeof(ProductionTreeScheduler).Assembly.GetType("AIBT.SchedulerFrameProfileV1");
            const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            _profileReset = profileType?.GetMethod("Reset", any);
            _profileSnapshot = profileType?.GetMethod("Snapshot", any);
            var agentFrameType = typeof(ProductionTreeScheduler).Assembly.GetType("AIBT.AgentFrameDiagnosticsV1");
            _agentFrameReset = agentFrameType?.GetMethod("Reset", any);
            _agentFrameSnapshot = agentFrameType?.GetMethod("Snapshot", any);
            var maxN = int.Parse(Arg("-perfMaxN", "1000000"), CultureInfo.InvariantCulture);
            var minN = int.Parse(Arg("-perfMinN", "0"), CultureInfo.InvariantCulture);
            _plan.RemoveAll(s => s.N > maxN || s.N < minN);
            if (!string.IsNullOrEmpty(only))
            {
                var keys = only.Split(',');
                _plan.RemoveAll(s => Array.FindIndex(keys, k => s.Id.Contains(k)) < 0);
            }

            var env = new StringBuilder();
            env.Append("{\"unity\":\"").Append(Application.unityVersion).Append('"');
#if ENABLE_IL2CPP
            env.Append(",\"backend\":\"IL2CPP\"");
#else
            env.Append(",\"backend\":\"Mono\"");
#endif
            env.Append(",\"development\":").Append(Debug.isDebugBuild ? "true" : "false");
            env.Append(",\"editor\":").Append(Application.isEditor ? "true" : "false");
            env.Append(",\"cpu\":\"").Append(SystemInfo.processorType).Append('"');
            env.Append(",\"logicalProcessors\":").Append(SystemInfo.processorCount);
            env.Append(",\"systemMemoryMB\":").Append(SystemInfo.systemMemorySize);
            env.Append(",\"gpu\":\"").Append(SystemInfo.graphicsDeviceName).Append('"');
            env.Append(",\"jobWorkers\":").Append(JobsUtility.JobWorkerCount);
            env.Append(",\"burstEnabled\":").Append(Unity.Burst.BurstCompiler.IsEnabled ? "true" : "false");
            env.Append(",\"vSyncCount\":").Append(QualitySettings.vSyncCount);
            env.Append(",\"targetFrameRate\":").Append(Application.targetFrameRate);
            env.Append(",\"gcAllocInFrameValid\":").Append(_gcAllocInFrame.Valid ? "true" : "false");
            env.Append(",\"systemMemoryValid\":").Append(_systemMemory.Valid ? "true" : "false");
            env.Append(",\"runs\":").Append(_runs);
            env.Append(",\"set\":\"").Append(set).Append('"');
            env.Append(",\"frameProfile\":").Append(_profileSnapshot != null ? "true" : "false");
            env.Append(",\"scenarios\":").Append(_plan.Count);
            env.Append(",\"started\":\"").Append(DateTime.UtcNow.ToString("o")).Append("\"}");
            File.WriteAllText(Path.Combine(_out, "environment.json"), env.ToString());
            Debug.Log("AIBT_PERF|START|" + env);
            NextScenario();
        }

        private void BuildPlan(bool quick)
        {
            int[] all = { 1, 100, 500, 1000, 2000, 5000 };
            int[] allNative = { 1, 100, 500, 1000, 2000, 5000, 10000, 20000 };
            int[] three = { 100, 1000, 5000 };
            int[] nativeThree = { 1000, 5000, 20000 };
            int[] realistic = { 102, 501, 1002, 2001, 5001 };

            Add(new Scenario { Id = "calib-alloc40", Kind = ScenarioKind.AllocProbe, N = 1000, Feeder = FeederMode.AllocProbe });
            foreach (var n in new[] { 1000, 5000 }) Add(new Scenario { Id = "empty-go", Kind = ScenarioKind.Empty, N = n });

            foreach (var n in all) Add(Managed("m-unity15-both", "unity-shape15-both", n));
            foreach (var n in three) Add(Managed("m-unity15-none", "unity-shape15-none", n));
            foreach (var n in three) { var s = Managed("m-unity15-both-bbwrite", "unity-shape15-both", n); s.Feeder = FeederMode.ManagedBlackboardWrite; Add(s); }
            foreach (var n in three) { var s = Managed("m-unity15-none-bbwrite", "unity-shape15-none", n); s.Feeder = FeederMode.ManagedBlackboardWrite; Add(s); }
            foreach (var n in three) { var s = Managed("m-unity15-both-interval100ms", "unity-shape15-both", n); s.TickMode = AgentTickMode.Interval; s.Interval = 0.1f; s.Measure = 3000; Add(s); }
            foreach (var n in new[] { 1000, 5000 }) { var s = Managed("m-unity15-both-manualloop", "unity-shape15-both", n); s.TickMode = AgentTickMode.Manual; s.Feeder = FeederMode.ManagedManualTick; Add(s); }

            foreach (var n in all) Add(Managed("m-shared-shape15", "shared-shape15", n));
            foreach (var n in allNative) Add(Native("n-shared-shape15", "shared-shape15", n));
            foreach (var n in new[] { 1000, 5000 }) Add(Managed("m-shared-loop16", "shared-loop16", n));
            foreach (var n in nativeThree) Add(Native("n-shared-loop16", "shared-loop16", n));
            foreach (var tree in new[] { "shared-reactive2", "shared-reactive16", "shared-reactive64" })
            {
                Add(Managed("m-" + tree, tree, 1000));
                Add(Native("n-" + tree, tree, 1000));
            }
            Add(Managed("m-shared-reactive16", "shared-reactive16", 5000));
            Add(Native("n-shared-reactive16", "shared-reactive16", 5000));

            foreach (var n in allNative) Add(Native("n-native15", "native-shape15", n));
            foreach (var n in nativeThree) { var s = Native("n-native15-fact", "native-shape15", n); s.Feeder = FeederMode.NativeFactBool; Add(s); }
            foreach (var n in nativeThree)
            {
                Add(Native("n-io-wait1", "native-wait1", n));
                Add(Native("n-io-move1", "native-move1", n));
                var fact = Native("n-io-move1-fact", "native-move1", n); fact.Feeder = FeederMode.NativeFactDistance; Add(fact);
                var both = Native("n-io-move1-fact-effect", "native-move1", n); both.Feeder = FeederMode.NativeFactDistance; both.EffectHandler = true; Add(both);
            }

            // P10-001: Jobs policies through the NativeTreeAgent facade, and Interval staggering.
            foreach (var tree in new[] { "native-shape15", "shared-loop16", "native-wait1" })
            {
                var label = tree == "native-shape15" ? "n-native15" : tree == "shared-loop16" ? "n-shared-loop16" : "n-io-wait1";
                foreach (var n in nativeThree)
                {
                    var batched = Native(label + "-jobs", tree, n); batched.Jobs = true; Add(batched);
                    var pipelined = Native(label + "-pipelined", tree, n); pipelined.Jobs = true; pipelined.Pipelined = true; Add(pipelined);
                }
            }
            foreach (var n in three) { var s = Managed("m-unity15-both-interval100ms-stagger", "unity-shape15-both", n); s.TickMode = AgentTickMode.Interval; s.Interval = 0.1f; s.Measure = 3000; s.Stagger = true; Add(s); }

            foreach (var n in realistic) Add(new Scenario { Id = "m-realistic", Kind = ScenarioKind.Realistic, N = n });
            foreach (var n in realistic) Add(new Scenario { Id = "api-only", Kind = ScenarioKind.ApiOnly, N = n });

            foreach (var s in _plan)
            {
                if (s.N >= 10000) { s.Warmup = 60; s.Measure = 150; }
                else if (s.N >= 5000 && s.TickMode != AgentTickMode.Interval) s.Measure = 300;
            }
            if (quick) foreach (var s in _plan) { s.Warmup = 30; s.Measure = 60; }
        }

        /// <summary>
        /// P10-002 quick set: the scenarios the Jobs speed-up is judged by, N = 1 000 and 5 000, each
        /// native one under the default profile (Immediate) and under BatchedJobsSameFrame, plus two
        /// managed controls. Fixed warm-up and measurement so that runs are comparable.
        /// </summary>
        private void BuildQuickPlan()
        {
            foreach (var n in new[] { 1000, 5000 })
            {
                foreach (var tree in new[] { "native-wait1", "shared-loop16", "shared-reactive16", "native-move1" })
                {
                    var label = tree == "native-wait1" ? "n-io-wait1"
                        : tree == "native-move1" ? "n-io-move1-fact-effect" : "n-" + tree;
                    foreach (var variant in new[] { 0, 1, 2, 3 })
                    {
                        var s = Native(variant == 1 ? label + "-jobs" : variant == 2 ? label + "-agentframe" : variant == 3 ? label + "-agentframe-fp" : label, tree, n);
                        s.Jobs = variant == 1;
                        s.AgentFrame = variant >= 2;
                        s.AgentFramePointer = variant == 3;
                        if (tree == "native-move1") { s.Feeder = FeederMode.NativeFactDistance; s.EffectHandler = true; }
                        Add(s);
                    }
                }
                Add(Managed("m-unity15-both", "unity-shape15-both", n));
                Add(Managed("m-shared-shape15", "shared-shape15", n));
            }
            foreach (var s in _plan) { s.Warmup = 120; s.Measure = 250; }
        }

        private void Add(Scenario s) => _plan.Add(s);

        private static Scenario Managed(string id, string tree, int n) => new Scenario { Id = id, Kind = ScenarioKind.Managed, Tree = tree, N = n };

        private static Scenario Native(string id, string tree, int n) => new Scenario { Id = id, Kind = ScenarioKind.Native, Tree = tree, N = n };

        private void NextScenario()
        {
            _planIndex++;
            if (_planIndex >= _plan.Count)
            {
                _planIndex = 0;
                _run++;
                if (_run >= _runs)
                {
                    Debug.Log("AIBT_PERF|DONE|faults=" + _faults);
                    File.WriteAllText(Path.Combine(_out, "done.txt"), "faults=" + _faults + "\n");
                    Application.Quit(0);
                    enabled = false;
                    _current = null;
                    return;
                }
            }
            _current = _plan[_planIndex];
            _phase = 0;
            _frame = 0;
        }

        /// <summary>Called by <see cref="PerfFrameEnd"/> after the bracket closed.</summary>
        public void AfterFrame()
        {
            var s = _current;
            if (s == null) return;
            switch (_phase)
            {
                case 0: // create
                    FullCollect();
                    _heapBefore = GC.GetTotalMemory(true);
                    _monoBefore = Profiler.GetMonoUsedSizeLong();
                    _sysBefore = _systemMemory.Valid ? _systemMemory.LastValue : 0;
                    Create(s);
                    _phase = 1;
                    _frame = 0;
                    break;
                case 1: // settle, then measure retained memory
                    if (++_frame < 3) break;
                    FullCollect();
                    _heapAfter = GC.GetTotalMemory(true);
                    _monoAfter = Profiler.GetMonoUsedSizeLong();
                    _phase = 2;
                    _frame = 0;
                    _phaseStart = PerfClock.Watch.ElapsedMilliseconds;
                    break;
                case 2: // warmup (frame count, capped at ~5 s for very slow configurations)
                    ++_frame;
                    if (_frame < s.Warmup && !(_frame >= 10 && PerfClock.Watch.ElapsedMilliseconds - _phaseStart > 5000)) break;
                    BeginMeasure(s);
                    _phase = 3;
                    _frame = 0;
                    _phaseStart = PerfClock.Watch.ElapsedMilliseconds;
                    break;
                case 3: // measure (sample of the frame that just ended; capped at ~20 s, at least 30 frames)
                    Sample();
                    ++_frame;
                    if (_frame < s.Measure && !(_frame >= 30 && PerfClock.Watch.ElapsedMilliseconds - _phaseStart > 20000)) break;
                    _measuredFrames = _frame;
                    _profile = _profileSnapshot != null ? (string)_profileSnapshot.Invoke(null, null) : null;
                    _measuredGc = GC.CollectionCount(0) - _gcStart;
                    _csv.Dispose();
                    _csv = null;
                    // Allocation window: GC disabled, so heap growth equals bytes allocated.
                    FullCollect();
                    GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                    _windowGc = GC.CollectionCount(0);
                    _windowHeap = GC.GetTotalMemory(false);
                    _phase = 5;
                    _frame = 0;
                    _phaseStart = PerfClock.Watch.ElapsedMilliseconds;
                    break;
                case 5:
                    ++_frame;
                    if (_frame < AllocWindowFrames && !(_frame >= 5 && PerfClock.Watch.ElapsedMilliseconds - _phaseStart > 8000)) break;
                    _windowFrames = _frame;
                    _windowBytes = GC.GetTotalMemory(false) - _windowHeap;
                    _windowGc = GC.CollectionCount(0) - _windowGc;
                    GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
                    EndMeasure(s);
                    Teardown();
                    _phase = 4;
                    _frame = 0;
                    break;
                case 4: // let Destroy finish
                    if (++_frame < 5) break;
                    NextScenario();
                    break;
            }
        }

        private static void FullCollect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void Create(Scenario s)
        {
            feeder.mode = FeederMode.None;
            feeder.Managed.Clear();
            feeder.Native.Clear();
            PerfClock.Sink = null;
            GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
            var heapStart = GC.GetTotalMemory(false);
            var allocStart = GC.GetAllocatedBytesForCurrentThread();
            var start = PerfClock.Watch.ElapsedTicks;
            long firstEnd = 0, firstAllocEnd = 0;
            switch (s.Kind)
            {
                case ScenarioKind.Empty:
                case ScenarioKind.AllocProbe:
                    for (var i = 0; i < s.N; i++)
                    {
                        _objects.Add(new GameObject("e" + i));
                        if (i == 0) { firstEnd = PerfClock.Watch.ElapsedTicks; firstAllocEnd = GC.GetAllocatedBytesForCurrentThread(); }
                    }
                    break;
                case ScenarioKind.Managed:
                    var source = _sources[s.Tree];
                    for (var i = 0; i < s.N; i++)
                    {
                        var go = new GameObject("m" + i);
                        go.SetActive(false);
                        var agent = go.AddComponent<BehaviorTreeAgent>();
                        agent.TickMode = s.TickMode;
                        if (s.TickMode == AgentTickMode.Interval) agent.TickInterval = s.Interval;
                        agent.StaggerIntervalTicks = s.Stagger;
                        agent.Tree = source;
                        agent.Faulted += OnFault;
                        go.SetActive(true);
                        _objects.Add(go);
                        _managed.Add(agent);
                        if (i == 0) { firstEnd = PerfClock.Watch.ElapsedTicks; firstAllocEnd = GC.GetAllocatedBytesForCurrentThread(); }
                    }
                    break;
                case ScenarioKind.Native:
                    var asset = FindNative(s.Tree);
                    for (var i = 0; i < s.N; i++)
                    {
                        var go = new GameObject("n" + i);
                        go.SetActive(false);
                        var agent = go.AddComponent<NativeTreeAgent>();
                        agent.Tree = asset;
                        if (s.Jobs) agent.Profile = s.Pipelined ? _pipelinedProfile : _jobsProfile;
                        if (s.AgentFrame) agent.Profile = SchedulingProfile.JobsAgentFrame;
                        if (s.EffectHandler) agent.OnEffect("move-speed", (Action<float>)CountEffect);
                        agent.Faulted += OnFault;
                        go.SetActive(true);
                        if (s.AgentFramePointer && agent.Host != null && !PerfAgentFramePointer.TryInstall(agent.Host)) _fpInstallFailed = true;
                        _objects.Add(go);
                        _native.Add(agent);
                        if (i == 0) { firstEnd = PerfClock.Watch.ElapsedTicks; firstAllocEnd = GC.GetAllocatedBytesForCurrentThread(); }
                    }
                    if (s.Jobs)
                    {
                        var world = GameObject.Find("AIBT Native World");
                        var scheduler = world != null ? world.GetComponent<ProductionTreeScheduler>() : null;
                        if (scheduler != null && SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, true, out var capabilities, out var capabilityError))
                            scheduler.SetJobsCapabilities(capabilities);
                        else Debug.LogError("AIBT_PERF|jobs capabilities could not be set");
                    }
                    break;
                case ScenarioKind.Realistic:
                case ScenarioKind.ApiOnly:
                    var groups = s.N / 3;
                    var side = Mathf.CeilToInt(Mathf.Sqrt(groups));
                    for (var g = 0; g < groups; g++)
                    {
                        var origin = new Vector3((g % side) * 30f, 0f, (g / side) * 30f);
                        if (s.Kind == ScenarioKind.Realistic) CreateRealisticGroup(origin, g);
                        else CreateApiGroup(origin, g);
                        if (g == 0) { firstEnd = PerfClock.Watch.ElapsedTicks; firstAllocEnd = GC.GetAllocatedBytesForCurrentThread(); }
                    }
                    break;
            }
            var end = PerfClock.Watch.ElapsedTicks;
            _createMicros = PerfClock.TicksToMicroseconds(end - start);
            _createAlloc = GC.GetAllocatedBytesForCurrentThread() - allocStart;
            _createHeapBytes = GC.GetTotalMemory(false) - heapStart;
            GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
            _firstMicros = PerfClock.TicksToMicroseconds(firstEnd - start);
            _firstAlloc = firstAllocEnd - allocStart;

            feeder.Managed.AddRange(_managed);
            feeder.Native.AddRange(_native);
            feeder.allocCount = s.N;
            feeder.mode = s.Feeder;
        }

        private static void CountEffect(float value) => PerfClock.EffectCount++;

        private void OnFault(string message)
        {
            _faults++;
            if (_faults < 20) Debug.LogError("AIBT_PERF|FAULT|" + _current?.Id + "|" + message);
        }

        private ProductionTreeAsset FindNative(string name)
        {
            foreach (var entry in nativeTrees) if (entry.name == name) return entry.asset;
            throw new InvalidOperationException("No native tree " + name);
        }

        private void CreateRealisticGroup(Vector3 origin, int g)
        {
            var obstacle = new GameObject("wall" + g);
            obstacle.transform.position = origin + new Vector3(0f, 0.5f, 5f);
            obstacle.AddComponent<BoxCollider>().size = new Vector3(4f, 1f, 0.5f);
            _objects.Add(obstacle);

            var wanderer = new GameObject("wanderer" + g);
            wanderer.SetActive(false);
            wanderer.transform.position = origin + new Vector3(0f, 0.5f, 0f);
            wanderer.AddComponent<CapsuleCollider>();
            var wanderAgent = wanderer.AddComponent<BehaviorTreeAgent>();
            wanderAgent.Tree = wanderTree;
            wanderAgent.Faulted += OnFault;
            wanderer.SetActive(true);
            wanderAgent.Blackboard.Set("arena-center", origin + new Vector3(0f, 0.5f, 0f));

            var guard = new GameObject("guard" + g);
            guard.SetActive(false);
            guard.transform.position = origin + new Vector3(-3f, 0.5f, -3f);
            var alert = new GameObject("alert");
            alert.transform.SetParent(guard.transform, false);
            var guardAgent = guard.AddComponent<BehaviorTreeAgent>();
            guardAgent.Tree = guardTree;
            guardAgent.SetReference("alert", alert);
            guardAgent.Faulted += OnFault;
            var route = guard.AddComponent<PatrolRoute>();
            route.points = new[]
            {
                origin + new Vector3(-6f, 0.5f, -6f), origin + new Vector3(6f, 0.5f, -6f),
                origin + new Vector3(6f, 0.5f, 6f), origin + new Vector3(-6f, 0.5f, 6f),
            };
            var senses = guard.AddComponent<GuardSenses>();
            senses.target = wanderer.transform;
            guard.SetActive(true);
            guardAgent.Events.Subscribe("spotted", CountSpotted);

            var scout = new GameObject("scout" + g);
            scout.SetActive(false);
            scout.transform.position = origin + new Vector3(3f, 0.5f, 3f);
            var scoutAgent = scout.AddComponent<BehaviorTreeAgent>();
            scoutAgent.Tree = scoutTree;
            scoutAgent.SetReference("intruder", wanderer);
            scoutAgent.Faulted += OnFault;
            scout.SetActive(true);

            _objects.Add(wanderer);
            _objects.Add(guard);
            _objects.Add(scout);
            _managed.Add(wanderAgent);
            _managed.Add(guardAgent);
            _managed.Add(scoutAgent);
        }

        private static void CountSpotted(BehaviorTreeAgent agent) => PerfClock.SpottedCount++;

        private void CreateApiGroup(Vector3 origin, int g)
        {
            var obstacle = new GameObject("wall" + g);
            obstacle.transform.position = origin + new Vector3(0f, 0.5f, 5f);
            obstacle.AddComponent<BoxCollider>().size = new Vector3(4f, 1f, 0.5f);
            _objects.Add(obstacle);
            var wanderer = new GameObject("wanderer" + g);
            wanderer.transform.position = origin + new Vector3(0f, 0.5f, 0f);
            wanderer.AddComponent<CapsuleCollider>();
            var w = wanderer.AddComponent<PerfApiOnly>();
            w.role = ApiRole.Wanderer; w.center = origin;
            var guard = new GameObject("guard" + g);
            guard.transform.position = origin + new Vector3(-3f, 0.5f, -3f);
            var gd = guard.AddComponent<PerfApiOnly>();
            gd.role = ApiRole.Guard; gd.center = origin; gd.other = wanderer.transform;
            var scout = new GameObject("scout" + g);
            scout.transform.position = origin + new Vector3(3f, 0.5f, 3f);
            var sc = scout.AddComponent<PerfApiOnly>();
            sc.role = ApiRole.Scout; sc.center = origin; sc.other = wanderer.transform;
            _objects.Add(wanderer);
            _objects.Add(guard);
            _objects.Add(scout);
        }

        private void BeginMeasure(Scenario s)
        {
            if (_scheduler == null)
            {
                var world = GameObject.Find("AIBT Native World");
                if (world != null) _scheduler = world.GetComponent<ProductionTreeScheduler>();
            }
            _profileReset?.Invoke(null, null);
            _agentFrameReset?.Invoke(null, null);
            _effectStart = PerfClock.EffectCount;
            _spottedStart = PerfClock.SpottedCount;
            _gcStart = GC.CollectionCount(0);
            var name = s.Id + "_n" + s.N + "_r" + (_run + 1);
            _csv = new StreamWriter(Path.Combine(_out, name + ".csv"), false, new UTF8Encoding(false));
            _csv.WriteLine("frame,bracket_us,alloc_bytes,feeder_us,feeder_alloc_bytes,scheduler_drive_us,frame_dt_us,gc_alloc_in_frame,main_thread_ns,heap_delta_bytes,gc_in_bracket");
            _meta = new StringBuilder();
            _meta.Append("{\"scenario\":\"").Append(s.Id).Append("\",\"kind\":\"").Append(s.Kind)
                .Append("\",\"tree\":\"").Append(s.Tree ?? string.Empty).Append("\",\"n\":").Append(s.N)
                .Append(",\"run\":").Append(_run + 1)
                .Append(",\"warmup\":").Append(s.Warmup).Append(",\"measure\":").Append(s.Measure)
                .Append(",\"tickMode\":\"").Append(s.TickMode).Append("\",\"interval\":").Append(F(s.Interval))
                .Append(",\"feeder\":\"").Append(s.Feeder).Append("\",\"effectHandler\":").Append(s.EffectHandler ? "true" : "false")
                .Append(",\"jobs\":").Append(s.Jobs ? "true" : "false")
                .Append(",\"agentFrame\":").Append(s.AgentFrame ? "true" : "false")
                .Append(",\"agentFramePointer\":").Append(s.AgentFramePointer ? (_fpInstallFailed ? "\"failed\"" : "true") : "false")
                .Append(",\"jobWorkers\":").Append(JobsUtility.JobWorkerCount)
                .Append(",\"createUs\":").Append(F(_createMicros)).Append(",\"createAllocBytes\":").Append(_createAlloc)
                .Append(",\"firstUs\":").Append(F(_firstMicros)).Append(",\"firstAllocBytes\":").Append(_firstAlloc)
                .Append(",\"heapBefore\":").Append(_heapBefore).Append(",\"heapAfter\":").Append(_heapAfter)
                .Append(",\"monoBefore\":").Append(_monoBefore).Append(",\"monoAfter\":").Append(_monoAfter)
                .Append(",\"sysBefore\":").Append(_sysBefore)
                .Append(",\"sysAfterWarmup\":").Append(_systemMemory.Valid ? _systemMemory.LastValue : 0);
        }

        private void Sample()
        {
            var bracket = PerfClock.TicksToMicroseconds(PerfClock.EndTicks - PerfClock.BeginTicks);
            var alloc = PerfClock.EndAlloc - PerfClock.BeginAlloc;
            var feederUs = PerfClock.TicksToMicroseconds(PerfClock.FeederTicks);
            var sched = _scheduler != null ? _scheduler.LastFrameConsumedMicroseconds : 0.0;
            var line = new StringBuilder(96);
            line.Append(_frame).Append(',').Append(F(bracket)).Append(',').Append(alloc).Append(',')
                .Append(F(feederUs)).Append(',').Append(PerfClock.FeederAlloc).Append(',').Append(F(sched)).Append(',')
                .Append(F(Time.unscaledDeltaTime * 1e6)).Append(',')
                .Append(_gcAllocInFrame.Valid ? _gcAllocInFrame.LastValue : -1).Append(',')
                .Append(_mainThread.Valid ? _mainThread.LastValue : -1).Append(',')
                .Append(PerfClock.EndHeap - PerfClock.BeginHeap).Append(',')
                .Append(PerfClock.EndGc - PerfClock.BeginGc);
            _csv.WriteLine(line.ToString());
        }

        private void EndMeasure(Scenario s)
        {
            var running = 0;
            foreach (var a in _managed) if (a != null && a.IsRunning) running++;
            foreach (var a in _native) if (a != null && a.IsRunning) running++;
            long completed = 0;
            foreach (var a in _native) if (a != null) completed += a.CompletedRuns;
            _meta.Append(",\"allocWindowFrames\":").Append(_windowFrames)
                .Append(",\"measuredFrames\":").Append(_measuredFrames)
                .Append(",\"allocWindowBytes\":").Append(_windowBytes)
                .Append(",\"allocWindowGc\":").Append(_windowGc)
                .Append(",\"createHeapBytes\":").Append(_createHeapBytes)
                .Append(",\"gcCollectionsGen0\":").Append(_measuredGc)
                .Append(",\"effects\":").Append(PerfClock.EffectCount - _effectStart)
                .Append(",\"spotted\":").Append(PerfClock.SpottedCount - _spottedStart)
                .Append(",\"running\":").Append(running)
                .Append(",\"nativeCompletedRuns\":").Append(completed)
                .Append(",\"faultsTotal\":").Append(_faults);
            if (_profile != null) _meta.Append(",\"frameProfile\":").Append(_profile);
            if (s.AgentFrame && _agentFrameSnapshot != null) _meta.Append(",\"agentFrameStats\":").Append((string)_agentFrameSnapshot.Invoke(null, null));
            _meta.Append('}');
            File.AppendAllText(Path.Combine(_out, "runs.jsonl"), _meta + "\n");
            Debug.Log("AIBT_PERF|RUN|" + _meta);
        }

        private bool _fpInstallFailed;

        private void Teardown()
        {
            PerfAgentFramePointer.Restore();
            feeder.mode = FeederMode.None;
            feeder.Managed.Clear();
            feeder.Native.Clear();
            foreach (var go in _objects) if (go != null) Destroy(go);
            _objects.Clear();
            _managed.Clear();
            _native.Clear();
        }

        private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private void OnDestroy()
        {
            _csv?.Dispose();
            _systemMemory.Dispose();
            _gcAllocInFrame.Dispose();
            _mainThread.Dispose();
        }
    }
}
