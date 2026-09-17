using AIBT.Burst;
using UnityEngine;

namespace AIBT.Benchmarks.Phase7.SwarmArena
{
    public enum BenchmarkAgentState : byte
    {
        Patrol = 1,
        Pursue = 2,
        Flee = 3,
        Attack = 4,
    }

    /// <summary>
    /// Benchmark-only clone of <c>AIBT.Samples.SwarmArena.SwarmArenaAgent</c>, written independently
    /// against only the public P7-041 production-world bridge (never the sample's own internal
    /// TryInitialize), so P7-035 measures the same public bridge a real consumer would use, per
    /// ADR-P7-041's own stated consequence.
    /// </summary>
    public sealed class BenchmarkAgent : MonoBehaviour, IProductionTreeWorldParticipant
    {
        private static readonly ulong MoveIntentId = StableHash.Fnv1A64("move-intent");
        private static readonly ulong StateIntentId = StableHash.Fnv1A64("state-intent");
        private static readonly ulong AttackId = StableHash.Fnv1A64("attack");

        private ProductionTreeWorld _world;
        private Transform _beacon;
        private Transform _danger;
        private ProductionTreeHost.ExternalTreeWriteHandle<Float3Value> _agentPosition;
        private ProductionTreeHost.ExternalTreeWriteHandle<Float3Value> _beaconPosition;
        private ProductionTreeHost.ExternalTreeWriteHandle<Float3Value> _dangerPosition;
        private ProductionTreeHost.ExternalTreeWriteHandle<Float3Value> _patrolPoint;
        private Float3Value _stagedAgentPosition;
        private Float3Value _stagedBeaconPosition;
        private Float3Value _stagedDangerPosition;
        private Float3Value _stagedPatrolPoint;
        private float _movementSpeed;
        private bool _registered;

        public ProductionTreeHost Host { get; private set; }
        public BenchmarkAgentState State { get; private set; } = BenchmarkAgentState.Patrol;
        public int AttackCount { get; private set; }
        public Vector3 PatrolPoint { get; private set; }

        public bool TryInitialize(
            ProductionTreeWorld world,
            GeneratedTreeRuntimeDefinitionV2 definition,
            GeneratedBurstCatalogV2 catalog,
            SchedulingProfile profile,
            Transform beacon,
            Transform danger,
            Vector3 patrolPoint,
            float movementSpeed,
            out string error)
        {
            error = null;
            _world = world;
            _beacon = beacon;
            _danger = danger;
            PatrolPoint = patrolPoint;
            _movementSpeed = movementSpeed;
            State = BenchmarkAgentState.Patrol;

            Host = gameObject.AddComponent<ProductionTreeHost>();
            if (!Host.TryConfigureEffectDelivery(4, out var configurationFailure))
            {
                error = "Effect delivery configuration failed: " + configurationFailure.Code;
                return false;
            }
            if (!Host.TryBootstrap(
                    definition, catalog,
                    new NativeTraceChannelCapacityV1(256, 0, 0, 512),
                    () => (long)(Time.unscaledTimeAsDouble * 1_000_000.0),
                    out var bootstrapFailure))
            {
                error = "Host bootstrap failed: " + bootstrapFailure.Code;
                return false;
            }

            var float3 = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float3);
            if (!Host.TryResolveExternalTreeWrite("agent-position", float3, out _agentPosition, out var agentFailure))
            {
                error = "External Tree-scope binding failed: " + agentFailure.Code;
                return false;
            }
            if (!Host.TryResolveExternalTreeWrite("beacon-position", float3, out _beaconPosition, out var beaconFailure))
            {
                error = "External Tree-scope binding failed: " + beaconFailure.Code;
                return false;
            }
            if (!Host.TryResolveExternalTreeWrite("danger-position", float3, out _dangerPosition, out var dangerFailure))
            {
                error = "External Tree-scope binding failed: " + dangerFailure.Code;
                return false;
            }
            if (!Host.TryResolveExternalTreeWrite("patrol-point", float3, out _patrolPoint, out var patrolFailure))
            {
                error = "External Tree-scope binding failed: " + patrolFailure.Code;
                return false;
            }
            if (!_world.TryRegister(this, profile, out var registrationFailure))
            {
                error = "Scheduler registration failed: " + registrationFailure;
                return false;
            }
            _registered = true;
            return true;
        }

        public void SampleInputs()
        {
            _stagedAgentPosition = ToFloat3(transform.position);
            _stagedBeaconPosition = ToFloat3(_beacon.position);
            _stagedDangerPosition = ToFloat3(_danger.position);
            _stagedPatrolPoint = ToFloat3(PatrolPoint);
        }

        public bool TryFlushInputs(out NativeRuntimeFailureV1 failure)
        {
            if (!Host.TryWriteExternalTreeValue(_agentPosition, _stagedAgentPosition, out _, out failure)) return false;
            if (!Host.TryWriteExternalTreeValue(_beaconPosition, _stagedBeaconPosition, out _, out failure)) return false;
            if (!Host.TryWriteExternalTreeValue(_dangerPosition, _stagedDangerPosition, out _, out failure)) return false;
            return Host.TryWriteExternalTreeValue(_patrolPoint, _stagedPatrolPoint, out _, out failure);
        }

        public void Receive(in ProductionTreeEffect effect)
        {
            if (effect.StableId == MoveIntentId && effect.TryGetFloat3(out var move))
            {
                transform.position += new Vector3(move.X, move.Y, move.Z) * (_movementSpeed * Time.unscaledDeltaTime);
                return;
            }
            if (effect.StableId == StateIntentId && effect.TryGetInt32(out var state))
            {
                State = (BenchmarkAgentState)state;
                return;
            }
            if (effect.StableId == AttackId && effect.TryGetInt32(out _)) AttackCount++;
        }

        internal void Shutdown()
        {
            if (_registered)
            {
                _world.TryUnregister(this);
                _registered = false;
            }
        }

        private void OnDestroy() => Shutdown();
        private static Float3Value ToFloat3(Vector3 value) => new Float3Value(value.x, value.y, value.z);
    }

    /// <summary>
    /// Minimal instanced-draw clone of <c>SwarmArenaInstancedPresenter</c>, parameterized over
    /// <see cref="BenchmarkAgent"/> instead of the sample's own agent type. Bracketed by the probe's
    /// own <c>Stopwatch</c> so rendering cost is isolated from AIBT scheduling cost.
    /// </summary>
    internal sealed class BenchmarkPresenter : MonoBehaviour
    {
        private const int StateCount = 4;
        private readonly Matrix4x4[][] _instances =
        {
            new Matrix4x4[64], new Matrix4x4[64], new Matrix4x4[64], new Matrix4x4[64],
        };

        private readonly int[] _counts = new int[StateCount];
        private readonly Material[] _materials = new Material[StateCount];
        private Mesh _mesh;

        public void Render(System.Collections.Generic.IList<BenchmarkAgent> agents)
        {
            if (agents == null) return;
            EnsureResources();
            for (var index = 0; index < StateCount; index++) _counts[index] = 0;

            for (var index = 0; index < agents.Count; index++)
            {
                var agent = agents[index];
                if (agent == null) continue;
                var state = Mathf.Clamp((int)agent.State - 1, 0, StateCount - 1);
                var count = _counts[state];
                EnsureCapacity(state, count + 1);
                _instances[state][count] = Matrix4x4.TRS(
                    agent.transform.position, Quaternion.identity, new Vector3(0.55f, 1f, 0.55f));
                _counts[state] = count + 1;
            }

            for (var state = 0; state < StateCount; state++)
            {
                if (_counts[state] == 0) continue;
                var parameters = new RenderParams(_materials[state])
                {
                    shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows = false,
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * 128f),
                };
                Graphics.RenderMeshInstanced(parameters, _mesh, 0, _instances[state], _counts[state], 0);
            }
        }

        private void OnDestroy()
        {
            for (var index = 0; index < _materials.Length; index++)
            {
                if (_materials[index] != null) Destroy(_materials[index]);
                _materials[index] = null;
            }
        }

        private void EnsureResources()
        {
            if (_mesh != null) return;
            var prototype = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            prototype.SetActive(false);
            _mesh = prototype.GetComponent<MeshFilter>().sharedMesh;
            var prototypeMaterial = prototype.GetComponent<MeshRenderer>().sharedMaterial;
            var colors = new[]
            {
                new Color(0.25f, 0.75f, 1f), new Color(0.35f, 1f, 0.45f),
                new Color(1f, 0.78f, 0.2f), new Color(1f, 0.25f, 0.25f),
            };
            for (var index = 0; index < StateCount; index++)
            {
                var material = new Material(prototypeMaterial) { enableInstancing = true };
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colors[index]);
                if (material.HasProperty("_Color")) material.SetColor("_Color", colors[index]);
                _materials[index] = material;
            }
            Destroy(prototype);
        }

        private void EnsureCapacity(int state, int required)
        {
            if (required <= _instances[state].Length) return;
            var capacity = _instances[state].Length;
            while (capacity < required) capacity *= 2;
            var instances = _instances[state];
            System.Array.Resize(ref instances, capacity);
            _instances[state] = instances;
        }
    }
}
