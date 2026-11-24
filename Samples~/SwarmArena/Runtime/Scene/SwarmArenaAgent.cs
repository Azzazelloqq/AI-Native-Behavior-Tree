using System;
using AIBT.Burst;
using UnityEngine;

namespace AIBT.Samples.SwarmArena
{
    public enum SwarmArenaAgentState : byte
    {
        Patrol = 1,
        Pursue = 2,
        Flee = 3,
        Attack = 4,
    }

    /// <summary>
    /// Unity-facing binding for one host. It only stages facts and applies committed public effects;
    /// it does not decide an agent state or access generated/native dispatch buffers.
    /// </summary>
    public sealed class SwarmArenaAgent : MonoBehaviour, IProductionTreeWorldParticipant
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
        private bool _moveSinceState;

        public ProductionTreeHost Host { get; private set; }
        public SwarmArenaAgentState State { get; private set; }
        /// <summary>The last committed move direction; applied every frame by <see cref="ApplyMoveIntent"/>.</summary>
        public Vector3 MoveIntent { get; private set; }
        public int AttackCount { get; private set; }
        public Vector3 PatrolPoint { get; private set; }
        public bool UsesPipelining { get; private set; }
        public ulong TotalLogicalUpdates => Host == null ? 0ul : Host.TotalUpdates;

        internal bool TryInitialize(
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
            if (world == null || definition == null || catalog == null || profile == null || beacon == null || danger == null)
            {
                error = "The Swarm Arena agent requires a world, definition, catalog, profile, beacon and danger transform.";
                return false;
            }

            _world = world;
            _beacon = beacon;
            _danger = danger;
            PatrolPoint = patrolPoint;
            _movementSpeed = movementSpeed;
            State = SwarmArenaAgentState.Patrol;
            UsesPipelining = profile.ForcedPolicy == SchedulingPolicy.PipelinedJobs;

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
                MoveIntent = new Vector3(move.X, move.Y, move.Z);
                _moveSinceState = true;
                return;
            }
            if (effect.StableId == StateIntentId && effect.TryGetInt32(out var state))
            {
                State = (SwarmArenaAgentState)state;
                // A steer node commits a move and a state together; a state committed without a move
                // (the attack branch) means the agent stands. The flag works in either delivery order.
                if (!_moveSinceState) MoveIntent = Vector3.zero;
                _moveSinceState = false;
                return;
            }
            if (effect.StableId == AttackId && effect.TryGetInt32(out _)) AttackCount++;
        }

        /// <summary>
        /// Moves the agent along its last committed move intent for one frame. The scenario calls it
        /// every frame, not once per received effect: a <see cref="SchedulingPolicy.PipelinedJobs"/>
        /// host commits one logical update per (lifecycle steps + 1) frames, so moving only when an
        /// effect arrives would move it that many times slower than a Normal host.
        /// </summary>
        public void ApplyMoveIntent(float deltaTime)
        {
            if (MoveIntent == Vector3.zero) return;
            transform.position += MoveIntent * (_movementSpeed * deltaTime);
        }

        internal void SetPatrolPoint(Vector3 point) => PatrolPoint = point;

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
}
