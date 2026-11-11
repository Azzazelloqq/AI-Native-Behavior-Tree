using System;
using AIBT;
using UnityEngine;

namespace AIBT.PatrolResearch
{
    public enum ResearchKind { Patroller, Runner, Wanderer }

    public sealed class ResearchActor : MonoBehaviour, IProductionTreeWorldParticipant
    {
        public ResearchKind Kind;
        public ProductionTreeAsset Tree;
        public Vector3[] Route;
        public ResearchActor TargetA;
        public ResearchActor TargetB;
        public TextMesh Label;
        public TextMesh Alert;
        public float SightRange = 5.2f;
        public float SightAngle = 130f;

        private ProductionTreeProgram _program;
        private ProductionTreeWorld _world;
        private ResearchScene _scene;
        private System.Random _random;
        private int _point;
        private Vector3 _destination;
        private Float3Value _routeOffset, _offsetA, _offsetB;
        private float _dt;
        private int _candidate;
        private ProductionTreeHost.ExternalTreeWriteHandle<Float3Value> _routeHandle, _aHandle, _bHandle;
        private ProductionTreeHost.ExternalTreeWriteHandle<float> _dtHandle;
        private ProductionTreeHost.ExternalTreeWriteHandle<bool> _visibleHandle;
        private ProductionTreeHost.ExternalTreeWriteHandle<int> _candidateHandle;
        private static readonly ulong MoveId = StableHash.Fnv1A64("move");
        private static readonly ulong ModeId = StableHash.Fnv1A64("mode-event");
        private static readonly ulong ArrivedId = StableHash.Fnv1A64("arrived");
        private static readonly ulong TargetId = StableHash.Fnv1A64("target-event");

        public ProductionTreeHost Host { get; private set; }
        public int Mode { get; private set; }
        public int PursuedTarget { get; private set; }
        public int Arrivals { get; private set; }
        public int Candidate => _candidate;
        public Vector3 Destination => _destination;
        public bool AlarmShown => Alert != null && Alert.gameObject.activeSelf;
        public float PursuitDistance => PursuedTarget == 1 ? Vector3.Distance(transform.position, TargetA.transform.position)
            : PursuedTarget == 2 ? Vector3.Distance(transform.position, TargetB.transform.position) : 0f;

        public void Initialize(ProductionTreeWorld world, ResearchScene scene)
        {
            _world = world;
            _scene = scene;
            _random = new System.Random(1227);
            _destination = Route[0];
            if (!Tree.TryCreateProgram(PatrolCatalog.TryCreateRuntimeCatalog, out _program, out var programFailure)) throw new InvalidOperationException(programFailure.ToString());
            Host = gameObject.AddComponent<ProductionTreeHost>();
            if (!_program.TryBootstrap(Host, out var failure, effectCapacity: 16)) throw new InvalidOperationException(failure.Code.ToString());
            _routeHandle = Resolve<Float3Value>("route-offset", BuiltInBlackboardTypes.Float3);
            _dtHandle = Resolve<float>("dt", BuiltInBlackboardTypes.Float32);
            if (Kind == ResearchKind.Patroller)
            {
                _aHandle = Resolve<Float3Value>("target-a", BuiltInBlackboardTypes.Float3);
                _bHandle = Resolve<Float3Value>("target-b", BuiltInBlackboardTypes.Float3);
                _visibleHandle = Resolve<bool>("target-visible", BuiltInBlackboardTypes.Bool);
                _candidateHandle = Resolve<int>("candidate", BuiltInBlackboardTypes.Int32);
            }
            if (!_world.TryRegister(this, SchedulingProfile.Normal, out var registration)) throw new InvalidOperationException(registration.ToString());
        }

        private ProductionTreeHost.ExternalTreeWriteHandle<T> Resolve<T>(string id, BlackboardTypeDescriptor type) where T : unmanaged
        {
            if (!Host.TryResolveExternalTreeWrite<T>(id, NativeBlackboardTypeIdV2.BuiltIn(type), out var handle, out var failure))
                throw new InvalidOperationException(id + ": " + failure.Code);
            return handle;
        }

        public void SampleInputs()
        {
            _dt = Time.deltaTime;
            _routeOffset = Offset(_destination);
            if (Kind != ResearchKind.Patroller) return;
            _offsetA = Offset(TargetA.transform.position);
            _offsetB = Offset(TargetB.transform.position);
            var distanceA = Visible(TargetA) ? Vector3.Distance(transform.position, TargetA.transform.position) : float.PositiveInfinity;
            var distanceB = Visible(TargetB) ? Vector3.Distance(transform.position, TargetB.transform.position) : float.PositiveInfinity;
            _candidate = float.IsPositiveInfinity(Mathf.Min(distanceA, distanceB)) ? 0 : distanceA <= distanceB ? 1 : 2;
        }

        private Float3Value Offset(Vector3 goal)
        {
            var offset = goal - transform.position;
            return new Float3Value(offset.x, 0f, offset.z);
        }

        private bool Visible(ResearchActor actor)
        {
            var offset = actor.transform.position - transform.position;
            offset.y = 0;
            return offset.sqrMagnitude <= SightRange * SightRange &&
                (offset.sqrMagnitude < 0.001f || Vector3.Dot(transform.forward, offset.normalized) >= Mathf.Cos(SightAngle * 0.5f * Mathf.Deg2Rad));
        }

        public bool TryFlushInputs(out NativeRuntimeFailureV1 failure)
        {
            if (!Host.TryWriteExternalTreeValue(_routeHandle, _routeOffset, out _, out failure)
                || !Host.TryWriteExternalTreeValue(_dtHandle, _dt, out _, out failure)) return false;
            return Kind != ResearchKind.Patroller || Host.TryWriteExternalTreeValue(_aHandle, _offsetA, out _, out failure)
                && Host.TryWriteExternalTreeValue(_bHandle, _offsetB, out _, out failure)
                && Host.TryWriteExternalTreeValue(_visibleHandle, _candidate != 0, out _, out failure)
                && Host.TryWriteExternalTreeValue(_candidateHandle, _candidate, out _, out failure);
        }

        public void Receive(in ProductionTreeEffect effect)
        {
            if (effect.StableId == MoveId && effect.TryGetFloat3(out var delta))
            {
                var movement = new Vector3(delta.X, delta.Y, delta.Z);
                transform.position += movement;
                if (movement.sqrMagnitude > 0.000001f) transform.rotation = Quaternion.LookRotation(movement.normalized, Vector3.up);
            }
            else if (effect.StableId == TargetId && effect.TryGetInt32(out var target)) PursuedTarget = target;
            else if (effect.StableId == ArrivedId && effect.TryGetInt32(out _))
            {
                Arrivals++;
                if (Kind == ResearchKind.Wanderer) _destination = new Vector3((float)_random.NextDouble() * 20f - 10f, transform.position.y, (float)_random.NextDouble() * 12f - 6f);
                else { _point = (_point + 1) % Route.Length; _destination = Route[_point]; }
            }
            else if (effect.StableId == ModeId && effect.TryGetInt32(out var mode))
            {
                if (Mode != mode) { var previous = Mode; Mode = mode; _scene.Record(this, previous, mode); }
                if (Alert != null) Alert.gameObject.SetActive(mode == 2);
            }
        }

        public string ModeName => Mode == 1 ? "PATROL" : Mode == 2 ? "CHASE" : Mode == 3 ? "RETURN" : Mode == 4 ? "ROUTE" : Mode == 5 ? "WANDER" : "STARTING";

        private void OnDestroy()
        {
            _world?.TryUnregister(this);
            Host?.Stop();
            _program?.Dispose();
        }
    }
}
