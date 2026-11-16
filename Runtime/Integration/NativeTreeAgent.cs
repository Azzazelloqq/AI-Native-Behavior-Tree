using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIBT
{
    /// <summary>What a <see cref="NativeTreeAgent"/> does when its root finishes.</summary>
    public enum NativeTreeRestartMode
    {
        /// <summary>Start a fresh run after every completion (the default; most AI loops forever).</summary>
        Always = 0,
        /// <summary>Keep the finished result; call <see cref="NativeTreeAgent.Restart"/> to run again.</summary>
        Never = 1,
        /// <summary>Restart only after Success.</summary>
        OnSuccess = 2,
        /// <summary>Restart only after Failure.</summary>
        OnFailure = 3,
    }

    /// <summary>
    /// One component that runs a prepared native (Burst) tree: it shares one program per
    /// (runtime asset, catalog), creates the world and scheduler when the scene has none assigned,
    /// writes named facts at the scheduler's safe boundary, and calls named effect handlers. It is
    /// a facade over <see cref="ProductionTreeAsset"/>, <see cref="ProductionTreeProgram"/>,
    /// <see cref="ProductionTreeHost"/> and <see cref="ProductionTreeWorld"/>, which stay available
    /// for full control. See ADR-P9-004.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("AIBT/Native Tree Agent")]
    public sealed class NativeTreeAgent : MonoBehaviour, IProductionTreeWorldParticipant
    {
        private const string SharedWorldName = "AIBT Native World";
        private static ProductionTreeWorld s_sharedWorld;

        [Tooltip("Runtime asset prepared from a tree for a generated catalog (Prepare Runtime in the graph editor).")]
        [SerializeField] private ProductionTreeAsset _tree;
        [Tooltip("What happens when the root finishes.")]
        [SerializeField] private NativeTreeRestartMode _restart = NativeTreeRestartMode.Always;
        [Tooltip("Committed effects buffered between deliveries. Raise it if a tree emits many effects per update.")]
        [SerializeField, Min(1)] private int _effectCapacity = 16;
        [Tooltip("Optional world to join. Empty: a shared world named '" + SharedWorldName + "' is created on demand.")]
        [SerializeField] private ProductionTreeWorld _world;

        private readonly Dictionary<string, FactSlot> _factsByKey = new Dictionary<string, FactSlot>(StringComparer.Ordinal);
        private readonly List<FactSlot> _facts = new List<FactSlot>();
        private readonly Dictionary<ulong, EffectHandler> _effects = new Dictionary<ulong, EffectHandler>();
        private ProductionTreeProgram _program;
        private ProductionTreeWorld _joinedWorld;
        private bool _active;
        private bool _resultReported;
        private bool _stopped;
        private SchedulingProfile _profile = SchedulingProfile.Normal;

        /// <summary>Raised on the main thread after the root finishes, before any automatic restart.</summary>
        public event Action<NodeStatus> Completed;

        /// <summary>Raised once when the agent cannot start or its host faults. The agent then stays stopped.</summary>
        public event Action<string> Faulted;

        /// <summary>The prepared runtime asset. Changing it while enabled restarts on the new tree.</summary>
        public ProductionTreeAsset Tree
        {
            get => _tree;
            set
            {
                if (_tree == value) return;
                _tree = value;
                if (_active) { Deactivate(); Activate(); }
            }
        }

        public NativeTreeRestartMode RestartMode { get => _restart; set => _restart = value; }

        /// <summary>
        /// Optional explicit catalog (<c>YourCatalog.TryCreateRuntimeCatalog</c>). Null resolves the
        /// catalog recorded in the asset. Assign before the agent is enabled.
        /// </summary>
        public ProductionTreeCatalogFactory CatalogFactory { get; set; }

        /// <summary>
        /// Scheduling profile used when joining the world. Assign before the agent is enabled.
        /// <see cref="SchedulingProfile.JobsAgentFrame"/> runs each agent's whole update in a Burst job
        /// (P10-004); it needs no <see cref="SchedulerJobsCapabilities"/>, and an agent the job cannot
        /// run falls back to Immediate with a reason on its frame entry.
        /// </summary>
        public SchedulingProfile Profile
        {
            get => _profile;
            set => _profile = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>World to join; null uses (and creates) the shared world. Assign before the agent is enabled.</summary>
        public ProductionTreeWorld World { get => _world; set => _world = value; }

        /// <summary>The current run's host; replaced on every restart, null while stopped.</summary>
        public ProductionTreeHost Host { get; private set; }

        /// <summary>The compiled program shared by every agent running this asset.</summary>
        public CompiledProgram Program => _program?.Program;

        /// <summary>True while a run is in progress.</summary>
        public bool IsRunning => Host != null && !Host.LastRootResult.HasValue && Host.LastFailure.Code == NativeRuntimeDiagnosticCodeV1.None;

        /// <summary>Result of the most recently finished run.</summary>
        public NodeStatus? LastResult { get; private set; }

        /// <summary>Number of runs that reached a root result since the agent was enabled.</summary>
        public int CompletedRuns { get; private set; }

        /// <summary>Why the agent stopped, or null.</summary>
        public string LastError { get; private set; }

        /// <summary>
        /// Sets a Tree-scope blackboard fact by key. The value is written at the scheduler's next
        /// safe boundary and kept across restarts. Supported: bool, int, long, float, double,
        /// <see cref="Float2Value"/>, <see cref="Float3Value"/>, <see cref="QuaternionValue"/>.
        /// No managed allocation after the first call for a key.
        /// </summary>
        public void SetFact<T>(string key, T value) where T : unmanaged
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A blackboard key is required.", nameof(key));
            if (!_factsByKey.TryGetValue(key, out var slot))
            {
                slot = new FactSlot<T>(key);
                _factsByKey.Add(key, slot);
                _facts.Add(slot);
            }
            if (!(slot is FactSlot<T> typed))
                throw new InvalidOperationException("Fact '" + key + "' was first set as " + slot.ValueType.Name + ", not " + typeof(T).Name + ".");
            typed.Set(value);
        }

        /// <summary>Sets a Float3 fact from a Unity vector.</summary>
        public void SetFact(string key, Vector3 value) => SetFact(key, new Float3Value(value.x, value.y, value.z));

        /// <summary>Handles an effect command by its binding name (the id in <c>[AibtCommandBinding]</c>). Replaces an earlier handler.</summary>
        public void OnEffect(string name, Action<int> handler) => AddEffect(name, new EffectHandler(handler));

        /// <inheritdoc cref="OnEffect(string, Action{int})"/>
        public void OnEffect(string name, Action<float> handler) => AddEffect(name, new EffectHandler(handler));

        /// <inheritdoc cref="OnEffect(string, Action{int})"/>
        public void OnEffect(string name, Action<Float3Value> handler) => AddEffect(name, new EffectHandler(handler));

        /// <summary>Handles a Float3 effect as a Unity vector.</summary>
        public void OnEffect(string name, Action<Vector3> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            AddEffect(name, new EffectHandler(value => handler(new Vector3(value.X, value.Y, value.Z))));
        }

        /// <summary>Removes the handler for an effect name.</summary>
        public bool RemoveEffect(string name) => _effects.Remove(StableHash.Fnv1A64(name ?? string.Empty));

        /// <summary>Stops the current run (cancelling active nodes) and starts a fresh one. Facts are kept.</summary>
        public void Restart()
        {
            _stopped = false;
            if (!_active) return;
            StopHost();
            StartHost();
        }

        /// <summary>Stops the current run, cancelling active nodes. <see cref="Restart"/> starts again.</summary>
        public void Stop()
        {
            _stopped = true;
            StopHost();
        }

        private void OnEnable() => Activate();

        private void OnDisable() => Deactivate();

        private void LateUpdate() => ProcessRootResult();

        internal void Activate()
        {
            if (_active) return;
            _active = true;
            CompletedRuns = 0;
            LastResult = null;
            LastError = null;
            if (!NativeTreeProgramCache.TryAcquire(_tree, CatalogFactory, out _program, out var error))
            {
                Fault(error);
                return;
            }
            if (!_stopped) StartHost();
        }

        internal void Deactivate()
        {
            if (!_active) return;
            _active = false;
            StopHost();
            NativeTreeProgramCache.Release(_program);
            _program = null;
        }

        /// <summary>Reports a finished or faulted run and applies the restart policy. Called after the scheduler frame.</summary>
        internal void ProcessRootResult()
        {
            var host = Host;
            if (host == null || _resultReported) return;
            if (host.LastFailure.Code != NativeRuntimeDiagnosticCodeV1.None)
            {
                _resultReported = true;
                Fault("The native host stopped: " + host.LastFailure.Code + "." + ForcedJobsHint());
                return;
            }
            if (!host.LastRootResult.HasValue) return;
            _resultReported = true;
            var result = host.LastRootResult.Value;
            LastResult = result;
            CompletedRuns++;
            try { Completed?.Invoke(result); }
            catch (Exception exception) { Debug.LogException(exception, this); }
            if (Host != host || !_active || _stopped) return; // a handler restarted, stopped or disabled the agent
            if (_restart == NativeTreeRestartMode.Always
                || (_restart == NativeTreeRestartMode.OnSuccess && result == NodeStatus.Success)
                || (_restart == NativeTreeRestartMode.OnFailure && result == NodeStatus.Failure))
            {
                StopHost();
                StartHost();
            }
        }

        private void StartHost()
        {
            if (_program == null || Host != null) return;
            var world = ResolveWorld();
            var host = gameObject.AddComponent<ProductionTreeHost>();
            host.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
            if (!_program.TryBootstrap(host, out var failure, _effectCapacity))
            {
                host.Stop();
                DestroyComponent(host);
                Fault("The native host could not start: " + failure.Code + ".");
                return;
            }
            Host = host;
            _resultReported = false;
            for (var index = 0; index < _facts.Count; index++) _facts[index].Rebind(host);
            if (world == null)
            {
                Fault("No ProductionTreeWorld is available.");
                return;
            }
            if (!world.TryRegister(this, _profile, out var registration))
            {
                Fault("The native host could not join its world: " + registration + ".");
                return;
            }
            _joinedWorld = world;
        }

        private void StopHost()
        {
            var host = Host;
            if (host == null) return;
            if (_joinedWorld != null) _joinedWorld.TryUnregister(this);
            _joinedWorld = null;
            host.Stop();
            Host = null;
            DestroyComponent(host);
        }

        private void Fault(string error)
        {
            LastError = error;
            StopHost();
            Debug.LogError("AIBT NativeTreeAgent '" + name + "': " + error, this);
            try { Faulted?.Invoke(error); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        /// <summary>
        /// P10-001: a profile forcing a Jobs policy fails every update (NativeCapacityPlanInvalid)
        /// until the world's scheduler has <see cref="SchedulerJobsCapabilities"/>; there is no
        /// hidden default for them (ADR-P7-033), so the fault names the missing call.
        /// </summary>
        private string ForcedJobsHint()
        {
            var forced = _profile.ForcedPolicy;
            if (forced != SchedulingPolicy.BatchedJobsSameFrame && forced != SchedulingPolicy.PipelinedJobs) return string.Empty;
            var world = _joinedWorld != null ? _joinedWorld : _world;
            var scheduler = world != null ? world.Scheduler : null;
            if (scheduler == null || scheduler.HasJobsCapabilities) return string.Empty;
            return " The profile forces " + forced.Value + ", which needs SchedulerJobsCapabilities on the world's"
                + " ProductionTreeScheduler (ProductionTreeScheduler.SetJobsCapabilities) before the first frame.";
        }

        private ProductionTreeWorld ResolveWorld()
        {
            if (_world != null) return _world;
            if (s_sharedWorld != null) return s_sharedWorld;
            // After a domain reload the static is gone but the world object survives.
            var existing = GameObject.Find(SharedWorldName);
            if (existing != null && existing.TryGetComponent(out s_sharedWorld)) return s_sharedWorld;
            var worldObject = new GameObject(SharedWorldName);
            if (Application.isPlaying) DontDestroyOnLoad(worldObject);
            else worldObject.hideFlags = HideFlags.DontSave;
            s_sharedWorld = worldObject.AddComponent<ProductionTreeWorld>();
            return s_sharedWorld;
        }

        private static void DestroyComponent(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void AddEffect(string name, EffectHandler handler)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("An effect name is required.", nameof(name));
            _effects[StableHash.Fnv1A64(name)] = handler;
        }

        // ------------------------------------------------------------------ world participant

        void IProductionTreeWorldParticipant.SampleInputs() { }

        bool IProductionTreeWorldParticipant.TryFlushInputs(out NativeRuntimeFailureV1 failure)
        {
            failure = default;
            var host = Host;
            if (host == null) return true;
            for (var index = 0; index < _facts.Count; index++)
            {
                var slot = _facts[index];
                if (!slot.Flush(host, out var error) && error != null)
                    Debug.LogError("AIBT NativeTreeAgent '" + name + "': " + error, this);
            }
            return true;
        }

        void IProductionTreeEffectReceiver.Receive(in ProductionTreeEffect effect)
        {
            if (!_effects.TryGetValue(effect.StableId, out var handler)) return;
            try { handler.Invoke(in effect); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        // ------------------------------------------------------------------ facts and effects

        private abstract class FactSlot
        {
            protected FactSlot(string key) { Key = key; }
            internal string Key { get; }
            internal abstract Type ValueType { get; }
            internal abstract void Rebind(ProductionTreeHost host);
            /// <summary>Writes a pending value; returns false with a one-time error message on failure.</summary>
            internal abstract bool Flush(ProductionTreeHost host, out string error);
        }

        private sealed class FactSlot<T> : FactSlot where T : unmanaged
        {
            private static readonly bool s_supported;
            private static readonly NativeBlackboardTypeIdV2 s_type;
            private ProductionTreeHost.ExternalTreeWriteHandle<T> _handle;
            private ProductionTreeHost _resolvedFor;
            private T _value;
            private bool _dirty;
            private bool _broken;

            static FactSlot()
            {
                s_supported = TryDescribe(out var descriptor);
                if (s_supported) s_type = NativeBlackboardTypeIdV2.BuiltIn(descriptor);
            }

            internal FactSlot(string key) : base(key)
            {
                if (!s_supported) throw new NotSupportedException("Facts of type " + typeof(T).Name + " are not supported. Use bool, int, long, float, double, Float2Value, Float3Value or QuaternionValue.");
            }

            internal override Type ValueType => typeof(T);

            internal void Set(T value)
            {
                _value = value;
                _dirty = true;
            }

            internal override void Rebind(ProductionTreeHost host)
            {
                _resolvedFor = null;
                _broken = false;
                _dirty = true; // a fresh host starts from the tree defaults; re-apply the last fact
            }

            internal override bool Flush(ProductionTreeHost host, out string error)
            {
                error = null;
                if (!_dirty || _broken) return true;
                if (!ReferenceEquals(_resolvedFor, host))
                {
                    if (!host.TryResolveExternalTreeWrite(Key, s_type, out _handle, out var resolveFailure))
                    {
                        _broken = true;
                        error = "fact '" + Key + "' (" + typeof(T).Name + ") cannot be written: " + resolveFailure.Code
                            + ". The tree must declare a Tree-scope key of this type that no node writes.";
                        return false;
                    }
                    _resolvedFor = host;
                }
                if (!host.TryWriteExternalTreeValue(_handle, _value, out _, out var failure))
                {
                    error = "fact '" + Key + "' write failed: " + failure.Code + ".";
                    return false;
                }
                _dirty = false;
                return true;
            }

            private static bool TryDescribe(out BlackboardTypeDescriptor descriptor)
            {
                var type = typeof(T);
                if (type == typeof(bool)) { descriptor = BuiltInBlackboardTypes.Bool; return true; }
                if (type == typeof(int)) { descriptor = BuiltInBlackboardTypes.Int32; return true; }
                if (type == typeof(long)) { descriptor = BuiltInBlackboardTypes.Int64; return true; }
                if (type == typeof(float)) { descriptor = BuiltInBlackboardTypes.Float32; return true; }
                if (type == typeof(double)) { descriptor = BuiltInBlackboardTypes.Float64; return true; }
                if (type == typeof(Float2Value)) { descriptor = BuiltInBlackboardTypes.Float2; return true; }
                if (type == typeof(Float3Value)) { descriptor = BuiltInBlackboardTypes.Float3; return true; }
                if (type == typeof(QuaternionValue)) { descriptor = BuiltInBlackboardTypes.Quaternion; return true; }
                descriptor = default;
                return false;
            }
        }

        private readonly struct EffectHandler
        {
            private readonly Action<int> _int32;
            private readonly Action<float> _float32;
            private readonly Action<Float3Value> _float3;

            internal EffectHandler(Action<int> handler) : this()
                => _int32 = handler ?? throw new ArgumentNullException(nameof(handler));
            internal EffectHandler(Action<float> handler) : this()
                => _float32 = handler ?? throw new ArgumentNullException(nameof(handler));
            internal EffectHandler(Action<Float3Value> handler) : this()
                => _float3 = handler ?? throw new ArgumentNullException(nameof(handler));

            internal void Invoke(in ProductionTreeEffect effect)
            {
                if (_int32 != null && effect.TryGetInt32(out var i)) _int32(i);
                else if (_float32 != null && effect.TryGetFloat32(out var f)) _float32(f);
                else if (_float3 != null && effect.TryGetFloat3(out var v)) _float3(v);
                else Debug.LogError("AIBT NativeTreeAgent: effect payload is " + effect.ValueKind + ", which does not match its handler.");
            }
        }
    }
}
