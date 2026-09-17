using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AIBT.Authoring;
using AIBT.Samples.SwarmArena;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace AIBT.Benchmarks.Phase7.SwarmArena
{
    /// <summary>
    /// P7-035: runs the real, unmodified Swarm Arena tree/catalog/spawn-layout/scripted-phase
    /// workload (copied in unchanged from <c>Samples~/SwarmArena/</c>) through the public P7-041
    /// production-world bridge, one policy per whole population per cell (never the showcase's own
    /// mixed 50/50 profile split), inside a real non-development IL2CPP/Burst Windows x64 Player.
    /// See <c>Planning~/Evidence/P7-035/implementation-plan.md</c> for the full design and its
    /// disclosed deviations from the showcase.
    /// </summary>
    public sealed class SwarmArenaBenchmarkProbe : MonoBehaviour
    {
        private const string OutputArgument = "-aibtP7035Output";
        private const string CellLimitArgument = "-aibtP7035CellLimit";
        private const string SuccessMarker = "AIBT_P7_035_SWARM_ARENA_OK|";
        private const string FailureMarker = "AIBT_P7_035_SWARM_ARENA_FAIL|";

        private const float WarmupSeconds = 20f;
        private const float MeasuredSeconds = 60f;
        private const float PhaseSeconds = 4f;
        private const int PhaseCount = 5;
        private const int Seed = 7034;
        private const float ArenaRadius = 14f;
        private const float MovementSpeed = 3.5f;

        // Real, recalibrated Player-derived per-step cost (NativeWorkEstimatorV1.CalibratedNanosecondsPerNodeStep,
        // P4-004's 2026-08-26 addendum). That field is internal to Runtime; this is a disclosed literal
        // duplicate for benchmark-only capability sizing, not a reference to production internals.
        private const double CalibratedNanosecondsPerNodeStep = 60.275;
        private const double CalibratedTargetBatchWorkNanoseconds = 20_000.0;
        private const double CalibratedMinimumJobWorkloadNanoseconds = 5_000.0;
        private const uint CalibratedMaxBatchSize = 2048u;
        private const uint FixedBatchSize = 32u;

        public TextAsset Tree;
        public Transform Beacon;
        public Transform Danger;

        private GeneratedBurstCatalogV2 _catalog;
        private GeneratedTreeRuntimeDefinitionV2 _definition;
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;
        private BenchmarkPresenter _presenter;
        private readonly List<BenchmarkAgent> _agents = new List<BenchmarkAgent>();
        private System.Random _random;

        private int _phase;
        private float _phaseElapsed;

        private bool _recording;
        private bool _renderingEnabled;
        private readonly Stopwatch _renderStopwatch = new Stopwatch();
        private readonly List<float> _frameTimeMs = new List<float>();
        private readonly List<double> _aibtMicroseconds = new List<double>();
        private readonly List<double> _renderMicroseconds = new List<double>();
        private readonly List<int> _registeredCounts = new List<int>();
        private readonly List<int> _admittedCounts = new List<int>();
        private readonly List<int> _deferredCounts = new List<int>();
        private readonly List<int> _failedCounts = new List<int>();
        private readonly List<double> _consumedBudgetMicroseconds = new List<double>();
        private readonly List<double> _allocatedBudgetMicroseconds = new List<double>();
        private readonly List<bool> _allocatedBudgetIsUnbounded = new List<bool>();
        private readonly List<ulong> _executedSteps = new List<ulong>();
        private readonly List<double> _observedLatencyFrames = new List<double>();
        private readonly List<bool> _overranFrames = new List<bool>();

        private static readonly int[] Populations = { 16, 64, 256, 512, 1024 };

        private static readonly CellPolicy[] Policies =
        {
            new CellPolicy("Auto", null, false, JobsCapabilityMode.Calibrated),
            new CellPolicy("Immediate", SchedulingPolicy.Immediate, false, JobsCapabilityMode.Calibrated),
            new CellPolicy("Budgeted", SchedulingPolicy.Budgeted, false, JobsCapabilityMode.Calibrated),
            new CellPolicy("BatchedJobsSameFrame", SchedulingPolicy.BatchedJobsSameFrame, false, JobsCapabilityMode.Calibrated),
            new CellPolicy("BatchedJobsSameFrame-FixedBatch32", SchedulingPolicy.BatchedJobsSameFrame, false, JobsCapabilityMode.Fixed32),
            new CellPolicy("PipelinedJobs", SchedulingPolicy.PipelinedJobs, true, JobsCapabilityMode.Calibrated),
        };

        private enum JobsCapabilityMode { Calibrated, Fixed32 }

        private readonly struct CellPolicy
        {
            internal CellPolicy(string label, SchedulingPolicy? forcedPolicy, bool pipeliningPermitted, JobsCapabilityMode capabilityMode)
            {
                Label = label;
                ForcedPolicy = forcedPolicy;
                PipeliningPermitted = pipeliningPermitted;
                CapabilityMode = capabilityMode;
            }

            internal string Label { get; }
            internal SchedulingPolicy? ForcedPolicy { get; }
            internal bool PipeliningPermitted { get; }
            internal JobsCapabilityMode CapabilityMode { get; }
        }

        private readonly List<CellResult> _results = new List<CellResult>();
        private string _outputPath;

        private void Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            StartCoroutine(RunMatrix());
        }

        private IEnumerator RunMatrix()
        {
            var arguments = Environment.GetCommandLineArgs();
            string outputError = null;
            try
            {
                _outputPath = RequiredArgument(arguments, OutputArgument);
            }
            catch (Exception exception)
            {
                if (Application.isEditor)
                    _outputPath = Path.Combine(Application.temporaryCachePath, "p7035-editor-smoke-test.json");
                else
                    outputError = exception.Message;
            }
            if (outputError != null) { Fail(outputError); yield break; }

            if (!TryBuildDefinition(out var buildError)) { Fail(buildError); yield break; }

            var totalCellCount = Populations.Length * Policies.Length * 2;
            var cellLimit = ReadOptionalCellLimit(arguments, totalCellCount, out var cellLimitError);
            if (cellLimitError != null) { Fail(cellLimitError); yield break; }
            UnityEngine.Debug.Log("AIBT_P7_035_SWARM_ARENA_START|cells=" + cellLimit + "/" + totalCellCount);

            var cellIndex = 0;
            foreach (var renderingEnabled in new[] { false, true })
            {
                foreach (var population in Populations)
                {
                    foreach (var policy in Policies)
                    {
                        if (cellIndex >= cellLimit)
                        {
                            WriteReport();
                            UnityEngine.Debug.Log(SuccessMarker + "cells=" + _results.Count);
                            Application.Quit(0);
                            yield break;
                        }

                        cellIndex++;
                        UnityEngine.Debug.Log("AIBT_P7_035_SWARM_ARENA_CELL_START|index=" + cellIndex + "/" + cellLimit
                            + "|population=" + population + "|policy=" + policy.Label + "|rendering=" + renderingEnabled);
                        string cellError = null;
                        yield return RunCell(population, policy, renderingEnabled, error => cellError = error);
                        if (cellError != null) { Fail(cellError); yield break; }
                        UnityEngine.Debug.Log("AIBT_P7_035_SWARM_ARENA_CELL_OK|index=" + cellIndex + "/" + cellLimit);
                    }
                }
            }

            WriteReport();
            UnityEngine.Debug.Log(SuccessMarker + "cells=" + _results.Count);
            Application.Quit(0);
        }

        private void Fail(string error)
        {
            UnityEngine.Debug.LogError(FailureMarker + error);
            Application.Quit(1);
        }

        private bool TryBuildDefinition(out string error)
        {
            error = null;
            if (Tree == null || Beacon == null || Danger == null)
            {
                error = "Benchmark scene is missing the tree/beacon/danger references.";
                return false;
            }
            var parsed = CanonicalTreeJson.Parse(Tree.text, "Samples~/SwarmArena/Trees/swarm-arena.aibt.json");
            if (!parsed.Success) { error = "Tree parse failed: " + FirstDiagnostic(parsed.Diagnostics); return false; }
            var artifact = GeneratedShardMetadataMaterializer.MaterializeArtifact(
                SwarmArenaShard.AibtGeneratedMetadata.ShardId,
                SwarmArenaShard.AibtGeneratedMetadata.ShardVersion,
                SwarmArenaShard.AibtGeneratedMetadata.CanonicalDescriptorJson,
                SwarmArenaShard.AibtGeneratedMetadata.DescriptorHash,
                SwarmArenaShard.AibtGeneratedMetadata.ManifestRegistryJson,
                SwarmArenaShard.AibtGeneratedMetadata.NodeRegistryHash);
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(
                parsed.Document, artifact.Nodes, artifact.RegisteredTypes,
                new ReferenceCompilerOptions(
                    "Samples~/SwarmArena/Trees/swarm-arena.aibt.json",
                    ReferenceCompilationPolicy.Phase1,
                    new CompiledCompilerVersion(1, 0, 0, 5)));
            if (!compiled.Success) { error = "Tree compile failed: " + FirstDiagnostic(compiled.Diagnostics); return false; }
            if (!SwarmArenaCatalog.TryCreateRuntimeCatalog(Allocator.Persistent, out _catalog, out var catalogFailure))
            {
                error = "Catalog creation failed: " + catalogFailure;
                return false;
            }
            if (!compiled.Program.TryCreateRuntimeDefinitionV2(_catalog, artifact.RegisteredTypes, out _definition, out var definitionFailure))
            {
                _catalog.TryDispose(out _);
                _catalog = null;
                error = "Definition creation failed: " + definitionFailure;
                return false;
            }
            return true;
        }

        private IEnumerator RunCell(int population, CellPolicy policy, bool renderingEnabled, Action<string> onError)
        {
            var schedulerObject = new GameObject("BenchmarkScheduler");
            _scheduler = schedulerObject.AddComponent<ProductionTreeScheduler>();
            _world = schedulerObject.AddComponent<ProductionTreeWorld>();
            _presenter = schedulerObject.AddComponent<BenchmarkPresenter>();

            var capabilityMode = policy.CapabilityMode;
            var maxBatch = capabilityMode == JobsCapabilityMode.Fixed32 ? FixedBatchSize : CalibratedMaxBatchSize;
            var minBatch = capabilityMode == JobsCapabilityMode.Fixed32 ? FixedBatchSize : 1u;
            if (!SchedulerJobsCapabilities.TryCreate(
                    CalibratedMinimumJobWorkloadNanoseconds, CalibratedTargetBatchWorkNanoseconds,
                    minBatch, maxBatch, maxBatch, policy.PipeliningPermitted,
                    out var capabilities, out var capabilityError))
            {
                onError("Jobs capability setup failed: " + capabilityError);
                yield break;
            }
            _scheduler.SetJobsCapabilities(capabilities);

            SchedulingProfile profile;
            if (policy.ForcedPolicy == null)
            {
                profile = SchedulingProfile.Normal;
            }
            else if (!SchedulingProfile.TryCreate(
                         "aibt.benchmarks.p7-035." + policy.Label.ToLowerInvariant(), 0, 1u, null, null,
                         policy.PipeliningPermitted, policy.ForcedPolicy, out profile, out var profileError))
            {
                onError("Scheduling profile setup failed: " + profileError);
                yield break;
            }

            _random = new System.Random(Seed);
            for (var index = 0; index < population; index++)
            {
                if (!SpawnAgent(index, profile, out var spawnError))
                {
                    onError(spawnError);
                    yield break;
                }
            }

            _phase = 0;
            _phaseElapsed = 0f;
            ApplyScriptedPhase();

            _renderingEnabled = renderingEnabled;
            _recording = false;
            var elapsed = 0f;
            while (elapsed < WarmupSeconds)
            {
                AdvancePhase();
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }

            _frameTimeMs.Clear();
            _aibtMicroseconds.Clear();
            _renderMicroseconds.Clear();
            _registeredCounts.Clear();
            _admittedCounts.Clear();
            _deferredCounts.Clear();
            _failedCounts.Clear();
            _consumedBudgetMicroseconds.Clear();
            _allocatedBudgetMicroseconds.Clear();
            _allocatedBudgetIsUnbounded.Clear();
            _executedSteps.Clear();
            _observedLatencyFrames.Clear();
            _overranFrames.Clear();
            var allocBefore = Profiler.GetMonoUsedSizeLong();
            _recording = true;
            elapsed = 0f;
            while (elapsed < MeasuredSeconds)
            {
                AdvancePhase();
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            _recording = false;
            var allocAfter = Profiler.GetMonoUsedSizeLong();

            var snapshot = CaptureFrameEntrySnapshot();
            _results.Add(BuildCellResult(population, policy, renderingEnabled, allocAfter - allocBefore, snapshot));

            for (var index = _agents.Count - 1; index >= 0; index--)
            {
                var agent = _agents[index];
                if (agent != null) agent.Shutdown();
            }
            for (var index = _agents.Count - 1; index >= 0; index--)
                if (_agents[index] != null) Destroy(_agents[index].gameObject);
            _agents.Clear();
            Destroy(schedulerObject);
            yield return null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            yield return null;
        }

        private bool SpawnAgent(int index, SchedulingProfile profile, out string error)
        {
            error = null;
            var objectName = "BenchmarkAgent" + index.ToString("0000");
            var agentObject = new GameObject(objectName);
            var angle = (float)(_random.NextDouble() * Math.PI * 2.0);
            var radius = ArenaRadius * (0.35f + (float)_random.NextDouble() * 0.55f);
            agentObject.transform.position = new Vector3((float)Math.Cos(angle) * radius, 1f, (float)Math.Sin(angle) * radius);
            var patrolAngle = angle + Mathf.PI;
            var patrol = new Vector3(
                Mathf.Cos(patrolAngle) * (ArenaRadius * 0.8f), 1f,
                Mathf.Sin(patrolAngle) * (ArenaRadius * 0.8f));
            var agent = agentObject.AddComponent<BenchmarkAgent>();
            if (!agent.TryInitialize(_world, _definition, _catalog, profile, Beacon, Danger, patrol, MovementSpeed, out error))
            {
                agent.Shutdown();
                Destroy(agentObject);
                return false;
            }
            _agents.Add(agent);
            return true;
        }

        private void AdvancePhase()
        {
            _phaseElapsed += Time.unscaledDeltaTime;
            if (_phaseElapsed < PhaseSeconds) return;
            _phaseElapsed = 0f;
            _phase = (_phase + 1) % PhaseCount;
            ApplyScriptedPhase();
        }

        private void ApplyScriptedPhase()
        {
            switch (_phase)
            {
                case 0:
                    Beacon.position = new Vector3(ArenaRadius * 2f, 1f, 0f);
                    Danger.position = new Vector3(-ArenaRadius * 2f, 1f, 0f);
                    break;
                case 1:
                    Beacon.position = new Vector3(0f, 1f, 0f);
                    Danger.position = new Vector3(-ArenaRadius * 2f, 1f, 0f);
                    break;
                case 2:
                    Beacon.position = new Vector3(0f, 1f, 0f);
                    Danger.position = new Vector3(-2f, 1f, 0f);
                    break;
                case 3:
                    Beacon.position = new Vector3(0f, 1f, 0f);
                    Danger.position = new Vector3(-ArenaRadius * 2f, 1f, 0f);
                    break;
                default:
                    Beacon.position = new Vector3(ArenaRadius * 2f, 1f, 0f);
                    Danger.position = new Vector3(-ArenaRadius * 2f, 1f, 0f);
                    break;
            }
        }

        private void LateUpdate()
        {
            if (_scheduler == null) return;

            double renderMicros = 0.0;
            if (_renderingEnabled)
            {
                _renderStopwatch.Restart();
                _presenter.Render(_agents);
                _renderStopwatch.Stop();
                renderMicros = _renderStopwatch.Elapsed.TotalMilliseconds * 1000.0;
            }

            if (!_recording) return;

            var registered = _scheduler.RegisteredCount;
            var admitted = 0;
            var deferred = 0;
            var failed = 0;
            ulong executedSteps = 0;
            ulong observedLatencyFrames = 0;
            for (var index = 0; index < _scheduler.FrameEntryCount; index++)
            {
                if (!_scheduler.TryGetFrameEntry(index, out var entry)) continue;
                if (entry.Disposition == SchedulerFrameDisposition.DeferredGlobalBudget
                    || entry.Disposition == SchedulerFrameDisposition.DeferredProfileShare) deferred++;
                else if (entry.Disposition == SchedulerFrameDisposition.Failed) failed++;
                else admitted++;
                executedSteps += entry.ExecutedSteps;
                observedLatencyFrames += entry.ObservedLatencyFrames;
            }

            _frameTimeMs.Add(Time.unscaledDeltaTime * 1000f);
            _aibtMicroseconds.Add(_scheduler.LastFrameConsumedMicroseconds);
            _renderMicroseconds.Add(renderMicros);
            _registeredCounts.Add(registered);
            _admittedCounts.Add(admitted);
            _deferredCounts.Add(deferred);
            _failedCounts.Add(failed);
            _consumedBudgetMicroseconds.Add(_scheduler.LastFrameConsumedMicroseconds);
            var allocatedBudget = _scheduler.LastFrameAllocatedBudgetMicroseconds;
            _allocatedBudgetMicroseconds.Add(double.IsPositiveInfinity(allocatedBudget) ? 0.0 : allocatedBudget);
            _allocatedBudgetIsUnbounded.Add(double.IsPositiveInfinity(allocatedBudget));
            _executedSteps.Add(executedSteps);
            _observedLatencyFrames.Add(_scheduler.FrameEntryCount == 0 ? 0.0 : (double)observedLatencyFrames / _scheduler.FrameEntryCount);
            _overranFrames.Add(_scheduler.LastFrameOverran);
        }

        private FrameEntrySnapshot CaptureFrameEntrySnapshot()
        {
            var snapshot = new FrameEntrySnapshot { policyCounts = Array.Empty<string>(), sampleCount = _scheduler.FrameEntryCount };
            if (_scheduler.FrameEntryCount == 0) return snapshot;
            var policies = new List<string>();
            var batchSizes = new List<uint>();
            var batchCounts = new List<uint>();
            var confidences = new List<string>();
            var selectionReasons = new List<string>();
            var utilizations = new List<double>();
            var expectedNodeSteps = new List<double>();
            ulong executedSteps = 0;
            ulong observedLatencyFrames = 0;
            uint requestedUpdateCadence = 0;
            for (var index = 0; index < _scheduler.FrameEntryCount; index++)
            {
                if (!_scheduler.TryGetFrameEntry(index, out var entry)) continue;
                policies.Add(entry.SelectedPolicy.ToString());
                selectionReasons.Add(entry.SelectionReason.ToString());
                expectedNodeSteps.Add(entry.ExpectedNodeStepsPerAgent);
                executedSteps += entry.ExecutedSteps;
                observedLatencyFrames += entry.ObservedLatencyFrames;
                requestedUpdateCadence = Math.Max(requestedUpdateCadence, entry.UpdateCadence);
                if (entry.HasWorkEstimate)
                {
                    batchSizes.Add(entry.BatchSize);
                    batchCounts.Add(entry.BatchCount);
                    confidences.Add(entry.Confidence.ToString());
                    utilizations.Add(entry.WorkerUtilizationProxy);
                }
            }
            snapshot.policyCounts = policies
                .GroupBy(value => value)
                .Select(group => group.Key + "=" + group.Count())
                .ToArray();
            snapshot.averageBatchSize = batchSizes.Count > 0 ? batchSizes.Average(value => (double)value) : 0.0;
            snapshot.averageBatchCount = batchCounts.Count > 0 ? batchCounts.Average(value => (double)value) : 0.0;
            snapshot.averageWorkerUtilizationProxy = utilizations.Count > 0 ? utilizations.Average() : 0.0;
            snapshot.confidenceCounts = confidences
                .GroupBy(value => value)
                .Select(group => group.Key + "=" + group.Count())
                .ToArray();
            snapshot.selectionReasonCounts = selectionReasons
                .GroupBy(value => value)
                .Select(group => group.Key + "=" + group.Count())
                .ToArray();
            snapshot.averageExpectedNodeStepsPerAgent = expectedNodeSteps.Count > 0 ? expectedNodeSteps.Average() : 0.0;
            snapshot.totalExecutedSteps = executedSteps;
            snapshot.averageObservedLatencyFrames = _scheduler.FrameEntryCount == 0 ? 0.0 : (double)observedLatencyFrames / _scheduler.FrameEntryCount;
            snapshot.requestedUpdateCadence = requestedUpdateCadence;
            snapshot.workerCount = JobsUtility.JobWorkerCount;
            return snapshot;
        }

        private CellResult BuildCellResult(int population, CellPolicy policy, bool renderingEnabled, long allocDeltaBytes, FrameEntrySnapshot snapshot)
        {
            return new CellResult
            {
                policyLabel = policy.Label,
                population = population,
                renderingEnabled = renderingEnabled,
                frameCount = _frameTimeMs.Count,
                frameTimeMsRaw = _frameTimeMs.ToArray(),
                aibtMicrosecondsRaw = _aibtMicroseconds.Select(value => (float)value).ToArray(),
                renderMicrosecondsRaw = _renderMicroseconds.Select(value => (float)value).ToArray(),
                allocatedBudgetMicrosecondsRaw = _allocatedBudgetMicroseconds.Select(value => (float)value).ToArray(),
                allocatedBudgetIsUnboundedRaw = _allocatedBudgetIsUnbounded.ToArray(),
                executedStepsRaw = _executedSteps.ToArray(),
                observedLatencyFramesRaw = _observedLatencyFrames.Select(value => (float)value).ToArray(),
                registeredCountRaw = _registeredCounts.ToArray(),
                admittedCountRaw = _admittedCounts.ToArray(),
                deferredCountRaw = _deferredCounts.ToArray(),
                failedCountRaw = _failedCounts.ToArray(),
                overranFrameCount = _overranFrames.Count(value => value),
                managedAllocDeltaBytesAfterWarmup = allocDeltaBytes,
                medianFrameTimeMs = Percentile(_frameTimeMs, 0.50),
                p95FrameTimeMs = Percentile(_frameTimeMs, 0.95),
                p99FrameTimeMs = Percentile(_frameTimeMs, 0.99),
                medianAibtMicroseconds = Percentile(_aibtMicroseconds, 0.50),
                p95AibtMicroseconds = Percentile(_aibtMicroseconds, 0.95),
                p99AibtMicroseconds = Percentile(_aibtMicroseconds, 0.99),
                medianRenderMicroseconds = Percentile(_renderMicroseconds, 0.50),
                frameEntrySnapshot = snapshot,
            };
        }

        private static double Percentile(List<float> values, double fraction)
        {
            if (values.Count == 0) return 0.0;
            var sorted = values.ToArray();
            Array.Sort(sorted);
            var index = (int)Math.Round(fraction * (sorted.Length - 1), MidpointRounding.AwayFromZero);
            return sorted[Math.Max(0, Math.Min(sorted.Length - 1, index))];
        }

        private static double Percentile(List<double> values, double fraction)
        {
            if (values.Count == 0) return 0.0;
            var sorted = values.ToArray();
            Array.Sort(sorted);
            var index = (int)Math.Round(fraction * (sorted.Length - 1), MidpointRounding.AwayFromZero);
            return sorted[Math.Max(0, Math.Min(sorted.Length - 1, index))];
        }

        private void WriteReport()
        {
            var report = new BenchmarkReport
            {
                schema = "aibt-p7-035-swarm-arena-player-v1",
                capturedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                scope = "Real Swarm Arena tree/catalog/spawn-layout/scripted-phase workload, through the public P7-041 world bridge, one policy per population per cell, in a real Windows x64 Standalone Player.",
                seed = Seed,
                warmupSeconds = WarmupSeconds,
                measuredSeconds = MeasuredSeconds,
                environment = CaptureEnvironment(),
                workloadTreeSha256 = Sha256(Tree.text),
                generatedCatalogDescriptorHash = SwarmArenaShard.AibtGeneratedMetadata.DescriptorHash,
                cells = _results.ToArray(),
                limitations = new[]
                {
                    "Movement/input-sampling cost is not independently bracketed: ProductionTreeWorld's CaptureInputs/FlushIdleInputs/DeliverEffects phases run inside ProductionTreeScheduler.Update(), which this probe does not intercept. AIBT scheduler time (LastFrameConsumedMicroseconds) and render time (bracketed directly) are real measured domains; movement/engine-overhead is only recoverable as a derived remainder (frame time - AIBT time - render time), not an independently measured figure.",
                    "The Jobs capability constants (CalibratedTargetBatchWorkNanoseconds=20000, CalibratedMinimumJobWorkloadNanoseconds=5000, max batch 2048) are disclosed round-number choices for a non-degenerate batch size, not a separately validated 'optimal' calibration -- the actually observed batch size/count for every cell is recorded in frameEntrySnapshot, so the real behavior is visible regardless of whether this choice was optimal.",
                    "PipelinedJobs is measured with its real delayed-result semantics (a wave's committed effects land at the next idle barrier, per ADR-P7-041); per-frame samples reflect that lag as-is, never resampled as same-frame data.",
                    "One run on one Windows x64 workstation; not generalized to other hardware.",
                    "frameEntrySnapshot is captured from the cell's last measured frame only, not every frame -- a representative summary of chosen policy/batch/confidence, not a full per-frame per-agent record.",
                    "No scheduler/profile/tree/node/gameplay/population-selection tuning occurred after this canonical run began, per this card's own forbidden-changes clause."
                },
            };

            var directory = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_outputPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
        }

        private static BenchmarkEnvironment CaptureEnvironment()
        {
            return new BenchmarkEnvironment
            {
                unityVersion = Application.unityVersion,
                applicationPlatform = Application.platform.ToString(),
                isEditor = Application.isEditor,
                operatingSystem = SystemInfo.operatingSystem,
                osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processorType = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                jobWorkerCount = JobsUtility.JobWorkerCount,
                systemMemoryMB = SystemInfo.systemMemorySize,
                burstEnabled = Unity.Burst.BurstCompiler.IsEnabled,
                is64BitProcess = IntPtr.Size == 8,
                thermalAndPowerConditions = "Not controlled or recorded",
            };
        }

        private static string Sha256(string value)
        {
            using (var algorithm = SHA256.Create())
            {
                var hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string FirstDiagnostic(DiagnosticCollection diagnostics)
        {
            foreach (var diagnostic in diagnostics) return diagnostic.Code + ": " + diagnostic.Message;
            return "unknown diagnostic";
        }

        private static string RequiredArgument(string[] arguments, string name)
        {
            for (var index = 0; index < arguments.Length - 1; index++)
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                    return arguments[index + 1];
            throw new ArgumentException("Missing required command-line argument " + name + ".");
        }

        private static int ReadOptionalCellLimit(string[] arguments, int defaultValue, out string error)
        {
            error = null;
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(arguments[index], CellLimitArgument, StringComparison.Ordinal)) continue;
                if (!int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    || value < 1 || value > defaultValue)
                {
                    error = CellLimitArgument + " must be an integer from 1 to " + defaultValue + ".";
                    return 0;
                }
                return value;
            }
            return defaultValue;
        }

        [Serializable]
        private sealed class BenchmarkReport
        {
            public string schema;
            public string capturedAtUtc;
            public string scope;
            public int seed;
            public float warmupSeconds;
            public float measuredSeconds;
            public BenchmarkEnvironment environment;
            public string workloadTreeSha256;
            public string generatedCatalogDescriptorHash;
            public CellResult[] cells;
            public string[] limitations;
        }

        [Serializable]
        private sealed class BenchmarkEnvironment
        {
            public string unityVersion;
            public string applicationPlatform;
            public bool isEditor;
            public string operatingSystem;
            public string osArchitecture;
            public string processArchitecture;
            public string processorType;
            public int processorCount;
            public int jobWorkerCount;
            public int systemMemoryMB;
            public bool burstEnabled;
            public bool is64BitProcess;
            public string thermalAndPowerConditions;
        }

        [Serializable]
        private sealed class FrameEntrySnapshot
        {
            public int sampleCount;
            public string[] policyCounts;
            public double averageBatchSize;
            public double averageBatchCount;
            public double averageWorkerUtilizationProxy;
            public string[] confidenceCounts;
            public string[] selectionReasonCounts;
            public double averageExpectedNodeStepsPerAgent;
            public ulong totalExecutedSteps;
            public double averageObservedLatencyFrames;
            public uint requestedUpdateCadence;
            public int workerCount;
        }

        [Serializable]
        private sealed class CellResult
        {
            public string policyLabel;
            public int population;
            public bool renderingEnabled;
            public int frameCount;
            public float[] frameTimeMsRaw;
            public float[] aibtMicrosecondsRaw;
            public float[] renderMicrosecondsRaw;
            public float[] allocatedBudgetMicrosecondsRaw;
            public bool[] allocatedBudgetIsUnboundedRaw;
            public ulong[] executedStepsRaw;
            public float[] observedLatencyFramesRaw;
            public int[] registeredCountRaw;
            public int[] admittedCountRaw;
            public int[] deferredCountRaw;
            public int[] failedCountRaw;
            public int overranFrameCount;
            public long managedAllocDeltaBytesAfterWarmup;
            public double medianFrameTimeMs;
            public double p95FrameTimeMs;
            public double p99FrameTimeMs;
            public double medianAibtMicroseconds;
            public double p95AibtMicroseconds;
            public double p99AibtMicroseconds;
            public double medianRenderMicroseconds;
            public FrameEntrySnapshot frameEntrySnapshot;
        }
    }
}
