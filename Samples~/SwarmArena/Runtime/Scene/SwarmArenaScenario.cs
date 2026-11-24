using System;
using System.Collections.Generic;
using AIBT.Authoring;
using Unity.Collections;
using UnityEngine;

namespace AIBT.Samples.SwarmArena
{
    /// <summary>
    /// Sample-only bootstrap and controls. Compilation/catalog creation is explicit for this known
    /// sample tree; it is not a generic runtime tree-import pipeline.
    /// </summary>
    public sealed class SwarmArenaScenario : MonoBehaviour
    {
        [SerializeField] private TextAsset _tree;
        [SerializeField] private ProductionTreeScheduler _scheduler;
        [SerializeField] private ProductionTreeWorld _world;
        [SerializeField] private Transform _beacon;
        [SerializeField] private Transform _danger;
        [SerializeField] private int _lowPopulation = 64;
        [SerializeField] private int _highPopulation = 256;
        [SerializeField] private int _stressPopulation = 512;
        [SerializeField] private float _arenaRadius = 14f;
        [SerializeField] private float _movementSpeed = 3.5f;
        // This is a gameplay showcase, not P7-035's performance experiment. It must admit a
        // first generated-catalog wave in a cold Editor session instead of starving it forever.
        [SerializeField] private double _globalBudgetMicroseconds = 1_000_000.0;
        [SerializeField] private bool _scriptedPhases = true;

        private readonly List<SwarmArenaAgent> _agents = new List<SwarmArenaAgent>();
        private GeneratedBurstCatalogV2 _catalog;
        private GeneratedTreeRuntimeDefinitionV2 _definition;
        private SchedulingProfile _pipelinedProfile;
        private SwarmArenaInstancedPresenter _presenter;
        private System.Random _random;
        private float _phaseElapsed;
        private const float UpdateRateWindowSeconds = 2f;
        private float _telemetryElapsed;
        private float _updateRateElapsed;
        private int _updateRateFrames;
        private float _updateRateFramesPerSecond;
        private float _fpsElapsed;
        private int _fpsFrameCount;
        private int _editorFramesPerSecond;
        private int _phase;
        private int _pendingPopulation = -1;
        private string _error;
        private int _createdHostCount;
        private int _destroyedHostCount;
        private int _patrolCount;
        private int _pursueCount;
        private int _fleeCount;
        private int _attackCount;
        private int _normalProfileCount;
        private int _pipelinedProfileCount;
        private int _admittedCount;
        private int _deferredCount;
        private int _failedSchedulingCount;
        private int _attacksPerSecond;
        private ulong _updatesPerSecond;
        private ulong _normalUpdatesPerSecond;
        private ulong _pipelinedUpdatesPerSecond;
        private long _lastAttackTotal;
        private ulong _lastUpdateTotal;
        private ulong _lastNormalUpdateTotal;
        private ulong _lastPipelinedUpdateTotal;

        public int Population => _agents.Count;
        public int PendingPopulation => _pendingPopulation;
        /// <summary>Logical updates per second of all agents (2 s window).</summary>
        public ulong LogicalUpdatesPerSecond => _updatesPerSecond;
        /// <summary>Logical updates per second of the Normal half, summed over its agents (2 s window).</summary>
        public ulong NormalUpdatesPerSecond => _normalUpdatesPerSecond;
        /// <summary>Logical updates per second of the PipelinedJobs half, summed over its agents (2 s window).</summary>
        public ulong PipelinedUpdatesPerSecond => _pipelinedUpdatesPerSecond;
        /// <summary>True when the keyboard controls work: they use the legacy Input Manager.</summary>
        public static bool KeyboardControlsAvailable
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return true;
#else
                return false;
#endif
            }
        }
        public string ActivePhaseName => _phase == 0 ? "Patrol"
            : _phase == 1 ? "Pursue"
            : _phase == 2 ? "Flee"
            : _phase == 3 ? "Attack"
            : "Return";

        private void Awake()
        {
            if (_scheduler == null) _scheduler = GetComponent<ProductionTreeScheduler>();
            if (_world == null) _world = GetComponent<ProductionTreeWorld>();
            _presenter = GetComponent<SwarmArenaInstancedPresenter>();
            if (_presenter == null) _presenter = gameObject.AddComponent<SwarmArenaInstancedPresenter>();
        }

        private void Start()
        {
            if (!TryPrepareRuntime(out _error))
            {
                Debug.LogError("AIBT Swarm Arena: " + _error, this);
                return;
            }
            SetPopulation(_lowPopulation);
            ApplyScriptedPhase();
        }

        private void Update()
        {
            if (_error != null) return;
            UpdateFrameRate();
            HandleControls();
            TryApplyPendingPopulation();
            if (!_scriptedPhases) return;
            _phaseElapsed += Time.unscaledDeltaTime;
            if (_phaseElapsed < 4f) return;
            _phaseElapsed = 0f;
            _phase = (_phase + 1) % 5;
            ApplyScriptedPhase();
        }

        private void LateUpdate()
        {
            if (_error != null) return;
            // Every frame, after the scheduler delivered this frame's effects: a PipelinedJobs agent
            // receives a new move intent only once per (lifecycle steps + 1) frames.
            var deltaTime = Time.unscaledDeltaTime;
            for (var index = 0; index < _agents.Count; index++)
            {
                var agent = _agents[index];
                if (agent != null) agent.ApplyMoveIntent(deltaTime);
            }
            UpdateTelemetry();
            _presenter.Render(_agents);
        }

        public void SetPopulation(int requestedPopulation)
        {
            if (_error != null || _definition == null) return;
            var target = Mathf.Clamp(requestedPopulation, 1, 512);
            if (_agents.Count == 0)
            {
                ApplyPopulation(target);
                return;
            }
            _pendingPopulation = target;
        }

        private void TryApplyPendingPopulation()
        {
            if (_pendingPopulation < 0) return;
            for (var index = 0; index < _agents.Count; index++)
            {
                var host = _agents[index] == null ? null : _agents[index].Host;
                // A dynamic cohort must never join or leave an in-flight pipelined group. This is
                // the same safe idle boundary P7-039 uses for ordinary external fact writes.
                if (host == null || !host.CanAcceptExternalTreeWrites) return;
            }
            var target = _pendingPopulation;
            _pendingPopulation = -1;
            ApplyPopulation(target);
        }

        private void ApplyPopulation(int target)
        {
            while (_agents.Count < target) SpawnAgent(_agents.Count);
            while (_agents.Count > target)
            {
                var index = _agents.Count - 1;
                var agent = _agents[index];
                _agents.RemoveAt(index);
                agent.Shutdown();
                Destroy(agent.gameObject);
                _destroyedHostCount++;
            }
        }

        private bool TryPrepareRuntime(out string error)
        {
            error = null;
            if (_tree == null || _scheduler == null || _world == null || _beacon == null || _danger == null)
            {
                error = "Assign the tree, scheduler/world, beacon and danger references (AIBT > Samples > Swarm Arena > Create Demo builds a wired scene).";
                return false;
            }
            var parsed = CanonicalTreeJson.Parse(_tree.text, "Samples~/SwarmArena/Trees/swarm-arena.aibt.json");
            if (!parsed.Success)
            {
                error = "The authored tree could not be parsed: " + FirstDiagnostic(parsed.Diagnostics);
                return false;
            }
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
            if (!compiled.Success)
            {
                error = "The authored tree could not be compiled: " + FirstDiagnostic(compiled.Diagnostics);
                return false;
            }
            if (!SwarmArenaCatalog.TryCreateRuntimeCatalog(Allocator.Persistent, out _catalog, out var catalogFailure))
            {
                error = "Generated catalog creation failed: " + catalogFailure;
                return false;
            }
            if (!compiled.Program.TryCreateRuntimeDefinitionV2(
                    _catalog, artifact.RegisteredTypes, out _definition, out var definitionFailure))
            {
                _catalog.TryDispose(out _);
                _catalog = null;
                error = "Generated definition creation failed: " + definitionFailure;
                return false;
            }
            if (!SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 32u, 32u, true, out var capabilities, out var capabilityError))
            {
                _catalog.TryDispose(out _);
                _catalog = null;
                error = "Sample scheduling capability setup failed: " + capabilityError;
                return false;
            }
            if (!SchedulingProfile.TryCreate(
                    "aibt.samples.swarm-arena.pipelined", -1, 1u, null, null, true,
                    SchedulingPolicy.PipelinedJobs, out _pipelinedProfile, out var profileError))
            {
                _catalog.TryDispose(out _);
                _catalog = null;
                error = "Sample scheduling profile setup failed: " + profileError;
                return false;
            }
            _scheduler.SetJobsCapabilities(capabilities);
            _scheduler.SetFixedBudget(_globalBudgetMicroseconds);
            _random = new System.Random(7_034);
            return true;
        }

        private void SpawnAgent(int index)
        {
            var objectName = "Swarm Agent " + (index + 1).ToString("00");
            var agentObject = new GameObject(objectName);
            agentObject.name = objectName;
            agentObject.transform.SetParent(transform, false);
            var angle = (float)(_random.NextDouble() * Math.PI * 2.0);
            var radius = _arenaRadius * (0.35f + (float)_random.NextDouble() * 0.55f);
            agentObject.transform.position = new Vector3((float)Math.Cos(angle) * radius, 1f, (float)Math.Sin(angle) * radius);
            var patrolAngle = angle + Mathf.PI;
            var patrol = new Vector3(
                Mathf.Cos(patrolAngle) * (_arenaRadius * 0.8f), 1f,
                Mathf.Sin(patrolAngle) * (_arenaRadius * 0.8f));
            var agent = agentObject.AddComponent<SwarmArenaAgent>();
            var profile = (index & 1) == 0 ? SchedulingProfile.Normal : _pipelinedProfile;
            if (!agent.TryInitialize(
                    _world, _definition, _catalog, profile, _beacon, _danger, patrol, _movementSpeed,
                    out var initializationError))
            {
                agent.Shutdown();
                Destroy(agentObject);
                _error = initializationError;
                Debug.LogError("AIBT Swarm Arena: " + initializationError, this);
                return;
            }
            _agents.Add(agent);
            _createdHostCount++;
        }

        // The keyboard controls use the legacy Input Manager. In a project set to the Input System
        // package only they compile out; the scripted phases still run and the overlay says so.
        private void HandleControls()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            var input = new Vector3(
                Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f) input.Normalize();
            if (input.sqrMagnitude > 0f) _beacon.position += input * (6f * Time.unscaledDeltaTime);
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetPopulation(_lowPopulation);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetPopulation(_highPopulation);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetPopulation(_stressPopulation);
            if (Input.GetKeyDown(KeyCode.Space)) _scriptedPhases = !_scriptedPhases;
            if (Input.GetKeyDown(KeyCode.R))
            {
                _phase = 0;
                _phaseElapsed = 0f;
                ApplyScriptedPhase();
            }
#endif
        }

        private void ApplyScriptedPhase()
        {
            if (_phase == 0)
            {
                _beacon.position = new Vector3(_arenaRadius * 2f, 1f, 0f);
                _danger.position = new Vector3(-_arenaRadius * 2f, 1f, 0f);
            }
            else if (_phase == 1)
            {
                _beacon.position = new Vector3(0f, 1f, 0f);
                _danger.position = new Vector3(-_arenaRadius * 2f, 1f, 0f);
            }
            else if (_phase == 2)
            {
                _beacon.position = new Vector3(0f, 1f, 0f);
                _danger.position = new Vector3(-2f, 1f, 0f);
            }
            else if (_phase == 3)
            {
                _beacon.position = new Vector3(0f, 1f, 0f);
                _danger.position = new Vector3(-_arenaRadius * 2f, 1f, 0f);
            }
            else
            {
                _beacon.position = new Vector3(_arenaRadius * 2f, 1f, 0f);
                _danger.position = new Vector3(-_arenaRadius * 2f, 1f, 0f);
            }
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(14f, 14f, 680f, 302f), "AIBT Swarm Arena — live runtime telemetry");
            GUI.Label(new Rect(28f, 42f, 650f, 20f), "Phase: " + ActivePhaseName + " | seed: 7034 | population: " + Population
                + (_pendingPopulation >= 0 ? " → " + _pendingPopulation + " pending idle barrier" : string.Empty)
                + " | hosts created/destroyed: " + _createdHostCount + "/" + _destroyedHostCount);
            GUI.Label(new Rect(28f, 64f, 650f, 20f), "State: Patrol " + _patrolCount + " · Pursue " + _pursueCount
                + " · Flee " + _fleeCount + " · Attack " + _attackCount);
            GUI.Label(new Rect(28f, 86f, 650f, 20f), "Profiles: Normal " + _normalProfileCount
                + " · PipelinedJobs " + _pipelinedProfileCount + " | registered: " + _scheduler.RegisteredCount);
            GUI.Label(new Rect(28f, 108f, 650f, 20f), "Logical updates/s (2 s): Normal " + _normalUpdatesPerSecond
                + PerAgent(_normalUpdatesPerSecond, _normalProfileCount) + " · PipelinedJobs " + _pipelinedUpdatesPerSecond
                + PerAgent(_pipelinedUpdatesPerSecond, _pipelinedProfileCount));
            GUI.Label(new Rect(28f, 130f, 650f, 20f), "PipelinedJobs: one decision per (lifecycle steps + 1) frames"
                + FramesPerDecision() + "; agents move on their last intent every frame.");
            GUI.Label(new Rect(28f, 152f, 650f, 20f), "Last second: committed attacks " + _attacksPerSecond
                + " · Runtime FPS " + _editorFramesPerSecond);
            GUI.Label(new Rect(28f, 174f, 650f, 20f), "Scheduler frame " + _scheduler.LastFrameIndex + ": admitted "
                + _admittedCount + " · deferred " + _deferredCount + " · failed " + _failedSchedulingCount);
            GUI.Label(new Rect(28f, 196f, 650f, 20f), "Budget: " + _scheduler.LastFrameConsumedMicroseconds.ToString("F0")
                + " / " + FormatBudget(_scheduler.LastFrameAllocatedBudgetMicroseconds) + " μs"
                + (_scheduler.LastFrameOverran ? " · overrun observed" : " · within current frame"));
            GUI.Label(new Rect(28f, 228f, 650f, 20f), KeyboardControlsAvailable
                ? "1 = " + _lowPopulation + " · 2 = " + _highPopulation + " · 3 = " + _stressPopulation
                  + " agents | WASD beacon | Space pauses phases | R resets patrol"
                : "Keys unavailable: they need the legacy Input Manager (Player Settings > Active Input Handling).");
            GUI.Label(new Rect(28f, 250f, 650f, 38f), "Colors: blue patrol · green pursue · yellow flee · red attack. "
                + "Runtime FPS is a 0.5 s display average, not a Player benchmark.");
        }

        private static string PerAgent(ulong updatesPerSecond, int agents)
            => agents == 0 ? string.Empty : " (" + ((double)updatesPerSecond / agents).ToString("F1") + " per agent)";

        private string FramesPerDecision()
        {
            if (_pipelinedProfileCount == 0 || _pipelinedUpdatesPerSecond == 0 || _updateRateFramesPerSecond <= 0f) return string.Empty;
            var perAgent = (double)_pipelinedUpdatesPerSecond / _pipelinedProfileCount;
            return ", now about " + (_updateRateFramesPerSecond / perAgent).ToString("F0");
        }

        private void UpdateFrameRate()
        {
            _fpsElapsed += Time.unscaledDeltaTime;
            _fpsFrameCount++;
            if (_fpsElapsed < 0.5f) return;

            _editorFramesPerSecond = Mathf.RoundToInt(_fpsFrameCount / _fpsElapsed);
            _fpsElapsed = 0f;
            _fpsFrameCount = 0;
        }

        private void UpdateTelemetry()
        {
            _patrolCount = 0;
            _pursueCount = 0;
            _fleeCount = 0;
            _attackCount = 0;
            _normalProfileCount = 0;
            _pipelinedProfileCount = 0;
            long totalAttacks = 0;
            ulong totalUpdates = 0;
            ulong normalUpdates = 0;
            ulong pipelinedUpdates = 0;
            for (var index = 0; index < _agents.Count; index++)
            {
                var agent = _agents[index];
                if (agent == null) continue;
                totalAttacks += agent.AttackCount;
                var updates = agent.TotalLogicalUpdates;
                totalUpdates += updates;
                if (agent.UsesPipelining)
                {
                    _pipelinedProfileCount++;
                    pipelinedUpdates += updates;
                }
                else
                {
                    _normalProfileCount++;
                    normalUpdates += updates;
                }
                if (agent.State == SwarmArenaAgentState.Patrol) _patrolCount++;
                else if (agent.State == SwarmArenaAgentState.Pursue) _pursueCount++;
                else if (agent.State == SwarmArenaAgentState.Flee) _fleeCount++;
                else if (agent.State == SwarmArenaAgentState.Attack) _attackCount++;
            }

            _telemetryElapsed += Time.unscaledDeltaTime;
            if (_telemetryElapsed >= 0.5f)
            {
                _attacksPerSecond = (int)((totalAttacks - _lastAttackTotal) / _telemetryElapsed);
                _lastAttackTotal = totalAttacks;
                _telemetryElapsed = 0f;
            }

            // Logical updates use a 2 s window: a PipelinedJobs group commits all its agents together
            // about every 32 frames (half a second at 60 FPS), so a 0.5 s window would flicker to 0.
            _updateRateElapsed += Time.unscaledDeltaTime;
            if (_updateRateElapsed >= UpdateRateWindowSeconds)
            {
                var inverseSeconds = 1.0 / _updateRateElapsed;
                _updatesPerSecond = Rate(totalUpdates, _lastUpdateTotal, inverseSeconds);
                _normalUpdatesPerSecond = Rate(normalUpdates, _lastNormalUpdateTotal, inverseSeconds);
                _pipelinedUpdatesPerSecond = Rate(pipelinedUpdates, _lastPipelinedUpdateTotal, inverseSeconds);
                _updateRateFramesPerSecond = (float)(_updateRateFrames * inverseSeconds);
                _lastUpdateTotal = totalUpdates;
                _lastNormalUpdateTotal = normalUpdates;
                _lastPipelinedUpdateTotal = pipelinedUpdates;
                _updateRateElapsed = 0f;
                _updateRateFrames = 0;
            }
            _updateRateFrames++;

            _admittedCount = 0;
            _deferredCount = 0;
            _failedSchedulingCount = 0;
            for (var index = 0; index < _scheduler.FrameEntryCount; index++)
            {
                if (!_scheduler.TryGetFrameEntry(index, out var entry)) continue;
                if (entry.Disposition == SchedulerFrameDisposition.DeferredGlobalBudget
                    || entry.Disposition == SchedulerFrameDisposition.DeferredProfileShare) _deferredCount++;
                else if (entry.Disposition == SchedulerFrameDisposition.Failed) _failedSchedulingCount++;
                else _admittedCount++;
            }
        }

        private void OnDestroy()
        {
            for (var index = _agents.Count - 1; index >= 0; index--)
            {
                var agent = _agents[index];
                if (agent == null) continue;
                agent.Shutdown();
                DestroyImmediate(agent.gameObject);
            }
            _agents.Clear();
            if (_catalog != null)
            {
                if (!_catalog.TryDispose(out var failure))
                    Debug.LogError("AIBT Swarm Arena: catalog teardown failed: " + failure, this);
                _catalog = null;
            }
        }

        private static string FirstDiagnostic(DiagnosticCollection diagnostics)
        {
            foreach (var diagnostic in diagnostics) return diagnostic.Code + ": " + diagnostic.Message;
            return "unknown diagnostic";
        }

        // Destroyed hosts take their totals with them when the population shrinks; never report a negative rate.
        private static ulong Rate(ulong total, ulong last, double inverseSeconds)
            => total >= last ? (ulong)((total - last) * inverseSeconds) : 0ul;

        private static string FormatBudget(double microseconds)
            => double.IsPositiveInfinity(microseconds) ? "unbounded" : microseconds.ToString("F0");
    }
}
