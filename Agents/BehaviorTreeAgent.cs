using System;
using System.Collections.Generic;
using AIBT.Agents;
using UnityEngine;

namespace AIBT
{
    /// <summary>When a <see cref="BehaviorTreeAgent"/> advances its tree.</summary>
    public enum AgentTickMode
    {
        /// <summary>Once per frame in Update.</summary>
        EveryFrame = 0,

        /// <summary>At a fixed interval; nodes receive the accumulated delta time.</summary>
        Interval = 1,

        /// <summary>Only when a script calls <see cref="BehaviorTreeAgent.Tick(float)"/>.</summary>
        Manual = 2,
    }

    /// <summary>
    /// Runs a behavior tree on this GameObject. Assign a tree and press Play: no generated
    /// catalog, runtime asset or host wiring is needed. Nodes are ordinary C# classes
    /// (<see cref="BtAction"/>, <see cref="BtCondition"/>) that can use this GameObject directly,
    /// and gameplay scripts exchange data with the tree through <see cref="Blackboard"/>.
    /// For very large crowds use the native (Burst) path instead.
    /// </summary>
    [AddComponentMenu("AIBT/Behavior Tree Agent")]
    [DisallowMultipleComponent]
    public sealed class BehaviorTreeAgent : MonoBehaviour
    {
        private static readonly Dictionary<TextAsset, BehaviorTreeSource> JsonSources = new Dictionary<TextAsset, BehaviorTreeSource>();

        [Tooltip("The behavior tree to run (.aibt asset).")]
        [SerializeField] private BehaviorTreeSource _tree;

        [Tooltip("Alternative for a tree stored as .aibt.json text. Ignored when Tree is assigned.")]
        [SerializeField] private TextAsset _treeJson;

        [Tooltip("When the tree advances.")]
        [SerializeField] private AgentTickMode _tickMode = AgentTickMode.EveryFrame;

        [Tooltip("Seconds between ticks when Tick Mode is Interval.")]
        [Min(0f)]
        [SerializeField] private float _tickInterval = 0.1f;

        [Tooltip("Start the tree again from its root after it finishes. Blackboard values are kept.")]
        [SerializeField] private bool _restartOnComplete = true;

        [Tooltip("Use unscaled time, so the tree keeps running while Time.timeScale is 0.")]
        [SerializeField] private bool _useUnscaledTime;

        [Tooltip("Scene objects the tree's nodes address by name, for example 'player'. Nodes with an empty reference act on this GameObject.")]
        [SerializeField] private List<AgentReference> _references = new List<AgentReference>();

        [Tooltip("Responses to named events the tree raises (aibt.unity.raise-event).")]
        [SerializeField] private List<AgentEventResponse> _eventResponses = new List<AgentEventResponse>();

        private AgentEvents _events;

        private ManagedTreeInstance _instance;
        private bool _initialized;
        private bool _ticking;
        private bool _pendingRestart;
        private bool _pendingRestartResetsBlackboard;
        private bool _pendingStop;
        private bool _faultReported;
        private bool _warnedMissingTree;
        private float _accumulatedTime;

        /// <summary>Raised when the root finishes, before an automatic restart.</summary>
        public event Action<NodeStatus> Completed;

        /// <summary>Raised once when the tree stops because a node threw or it could not be compiled.</summary>
        public event Action<string> Faulted;

        /// <summary>The assigned tree. Assigning a different tree rebuilds the agent.</summary>
        public BehaviorTreeSource Tree
        {
            get => _tree;
            set
            {
                if (_tree == value) return;
                _tree = value;
                if (_initialized) Rebuild();
            }
        }

        public AgentTickMode TickMode
        {
            get => _tickMode;
            set => _tickMode = value;
        }

        public float TickInterval
        {
            get => _tickInterval;
            set => _tickInterval = Mathf.Max(0f, value);
        }

        public bool RestartOnComplete
        {
            get => _restartOnComplete;
            set => _restartOnComplete = value;
        }

        /// <summary>
        /// This agent's blackboard. Available from Awake onward; null when no tree is assigned or
        /// the tree failed to compile (see <see cref="LastError"/>).
        /// </summary>
        public AgentBlackboard Blackboard
        {
            get
            {
                EnsureInitialized();
                return _instance?.Blackboard;
            }
        }

        /// <summary>True while the tree has a running node or is waiting to restart.</summary>
        public bool IsRunning => _instance != null && (_instance.IsActive || _pendingRestart) && !_pendingStop;

        /// <summary>The root's result once the tree has finished; null while it runs.</summary>
        public NodeStatus? LastResult => _instance?.LastResult;

        /// <summary>True when the tree stopped on an error.</summary>
        public bool HasFaulted => LastError != null;

        /// <summary>Why the tree is not running, or null.</summary>
        public string LastError { get; private set; }

        internal ManagedTreeInstance Instance => _instance;

        /// <summary>Named events the tree raises to gameplay code. See <see cref="AgentEvents"/>.</summary>
        public AgentEvents Events => _events ?? (_events = new AgentEvents(this));

        /// <summary>The named scene objects nodes can address.</summary>
        public IReadOnlyList<AgentReference> References => _references;

        /// <summary>
        /// The object registered under <paramref name="referenceName"/>, or null when there is no
        /// such entry or its object was destroyed.
        /// </summary>
        public UnityEngine.Object GetReference(string referenceName)
        {
            if (string.IsNullOrEmpty(referenceName) || _references == null) return null;
            for (var index = 0; index < _references.Count; index++)
            {
                if (string.Equals(_references[index].name, referenceName, StringComparison.Ordinal))
                    return _references[index].target != null ? _references[index].target : null;
            }

            return null;
        }

        /// <summary>
        /// The GameObject a node should act on: this agent's for an empty name, otherwise the
        /// referenced object's (a component reference resolves to its GameObject). Null when missing.
        /// </summary>
        public GameObject ResolveGameObject(string referenceName)
        {
            if (string.IsNullOrEmpty(referenceName)) return gameObject;
            var target = GetReference(referenceName);
            if (target is GameObject go) return go;
            return target is Component component ? component.gameObject : null;
        }

        /// <summary>Adds or replaces a named reference from code, for objects spawned at runtime.</summary>
        public void SetReference(string referenceName, UnityEngine.Object target)
        {
            if (string.IsNullOrEmpty(referenceName)) throw new ArgumentException("A reference name is required.", nameof(referenceName));
            if (_references == null) _references = new List<AgentReference>();
            for (var index = 0; index < _references.Count; index++)
            {
                if (!string.Equals(_references[index].name, referenceName, StringComparison.Ordinal)) continue;
                _references[index] = new AgentReference(referenceName, target);
                return;
            }

            _references.Add(new AgentReference(referenceName, target));
        }

        /// <summary>Adds an inspector-style response from code (mainly for tests and tools).</summary>
        internal void AddEventResponse(AgentEventResponse response)
        {
            if (_eventResponses == null) _eventResponses = new List<AgentEventResponse>();
            _eventResponses.Add(response);
        }

        internal int InvokeEventResponses(string eventName)
        {
            if (_eventResponses == null) return 0;
            var invoked = 0;
            for (var index = 0; index < _eventResponses.Count; index++)
            {
                var entry = _eventResponses[index];
                if (entry == null || entry.response == null || !string.Equals(entry.eventName, eventName, StringComparison.Ordinal)) continue;
                entry.response.Invoke();
                invoked++;
            }

            return invoked;
        }

        /// <summary>Advances the tree once. Call this yourself when Tick Mode is Manual.</summary>
        public void Tick(float deltaTime)
        {
            EnsureInitialized();
            if (_instance == null) return;
            if (_ticking)
            {
                Debug.LogWarning("BehaviorTreeAgent.Tick was called from inside a node and was ignored.", this);
                return;
            }

            _ticking = true;
            ManagedTickOutcome outcome;
            try
            {
                if (_pendingRestart)
                {
                    _pendingRestart = false;
                    if (!_instance.Restart(_pendingRestartResetsBlackboard))
                    {
                        ReportFault();
                        return;
                    }

                    _pendingRestartResetsBlackboard = false;
                }

                outcome = _instance.Tick(deltaTime);
            }
            finally
            {
                _ticking = false;
            }

            if (outcome == ManagedTickOutcome.Faulted)
            {
                ReportFault();
            }
            else if (outcome == ManagedTickOutcome.Completed)
            {
                if (_restartOnComplete) _pendingRestart = true;
                Completed?.Invoke(_instance.LastResult ?? NodeStatus.Failure);
            }

            if (_pendingStop)
            {
                _pendingStop = false;
                _pendingRestart = false;
                _instance.Stop();
            }
        }

        /// <summary>
        /// Starts the tree again from its root, aborting whatever is running. Blackboard values
        /// are kept unless <paramref name="resetBlackboard"/> is true. Called from inside a node,
        /// the restart happens on the next tick.
        /// </summary>
        public void Restart(bool resetBlackboard = false)
        {
            EnsureInitialized();
            if (_instance == null) return;
            _pendingStop = false;
            if (_ticking)
            {
                _pendingRestart = true;
                _pendingRestartResetsBlackboard |= resetBlackboard;
                return;
            }

            _pendingRestart = false;
            LastError = null;
            _faultReported = false;
            if (!_instance.Restart(resetBlackboard)) ReportFault();
        }

        /// <summary>Aborts whatever is running and stops ticking until <see cref="Restart"/>.</summary>
        public void Stop()
        {
            if (_instance == null) return;
            if (_ticking)
            {
                _pendingStop = true;
                return;
            }

            _pendingRestart = false;
            _instance.Stop();
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            if (_instance == null)
            {
                // Assigning the tree later from code is fine, so this is a warning and only once.
                if (!_warnedMissingTree && LastError == null && _tree == null && _treeJson == null)
                {
                    _warnedMissingTree = true;
                    Debug.LogWarning("BehaviorTreeAgent on '" + name + "' has no tree assigned and does nothing.", this);
                }

                return;
            }

            if (_tickMode == AgentTickMode.Manual) return;
            var delta = _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (_tickMode == AgentTickMode.Interval)
            {
                _accumulatedTime += delta;
                if (_accumulatedTime < _tickInterval) return;
                delta = _accumulatedTime;
                _accumulatedTime = 0f;
            }

            Tick(delta);
        }

        private void OnDestroy()
        {
            // Let running nodes exit so they can release what they started.
            if (_instance != null && !_ticking) _instance.Stop();
            _instance = null;
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            Build();
        }

        private void Rebuild()
        {
            if (_instance != null && !_ticking) _instance.Stop();
            _instance = null;
            _pendingRestart = false;
            _pendingStop = false;
            _accumulatedTime = 0f;
            Build();
        }

        private void Build()
        {
            LastError = null;
            _faultReported = false;
            var source = ResolveSource();
            if (source == null)
            {
                // An agent without a tree is a valid authoring state; it simply does nothing.
                return;
            }

            if (!source.TryGetProgram(out var program, out var error))
            {
                Fail("Behavior tree '" + source.name + "' could not be compiled:\n" + error);
                return;
            }

            try
            {
                _instance = new ManagedTreeInstance(program, this);
            }
            catch (Exception exception)
            {
                Fail("Behavior tree '" + source.name + "' could not start: " + exception.Message);
            }
        }

        private BehaviorTreeSource ResolveSource()
        {
            if (_tree != null) return _tree;
            if (_treeJson == null) return null;
            var text = _treeJson.text;
            if (!JsonSources.TryGetValue(_treeJson, out var source) || source == null || !string.Equals(source.Json, text, StringComparison.Ordinal))
            {
                source = BehaviorTreeSource.FromJson(text, _treeJson.name);
                source.hideFlags = HideFlags.HideAndDontSave;
                JsonSources[_treeJson] = source;
            }

            return source;
        }

        private void ReportFault()
        {
            if (_faultReported || _instance == null) return;
            var node = _instance.FaultNodeId;
            var message = "Behavior tree on '" + name + "' stopped" + (node != null ? " in node '" + node + "'" : string.Empty)
                + ": " + _instance.FaultMessage;
            if (_instance.FaultException != null) Debug.LogException(_instance.FaultException, this);
            Fail(message);
        }

        private void Fail(string message)
        {
            LastError = message;
            _faultReported = true;
            _pendingRestart = false;
            Debug.LogError(message, this);
            Faulted?.Invoke(message);
        }
    }
}
