using UnityEngine;

namespace AIBT.Samples.FirstAgent
{
    [RequireComponent(typeof(ProductionTreeWorld))]
    public sealed class FirstAgent : MonoBehaviour, IProductionTreeWorldParticipant
    {
        [SerializeField] private ProductionTreeAsset _tree;
        [SerializeField] private float _targetX = 5f;
        private ProductionTreeProgram _program;
        private ProductionTreeWorld _world;
        private ProductionTreeHost.ExternalTreeWriteHandle<float> _distanceHandle;
        private float _distance;

        public ProductionTreeHost Host { get; private set; }

        private void Start()
        {
            _world = GetComponent<ProductionTreeWorld>();
            if (_tree == null)
            {
                Debug.LogError("Assign a runtime asset prepared for FirstAgentCatalog.", this);
                return;
            }
            if (!_tree.TryCreateProgram(FirstAgentCatalog.TryCreateRuntimeCatalog, out _program, out var programFailure))
            {
                Debug.LogError("First Agent program creation failed: " + programFailure, this);
                return;
            }
            Host = gameObject.AddComponent<ProductionTreeHost>();
            if (!_program.TryBootstrap(Host, out var failure, effectCapacity: 4)
                || !Host.TryResolveExternalTreeWrite("distance", NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32), out _distanceHandle, out failure))
            {
                Debug.LogError("First Agent bootstrap failed: " + failure.Code, this);
                Host.Stop();
                return;
            }
            if (!_world.TryRegister(this, SchedulingProfile.Normal, out var registrationFailure))
            {
                Debug.LogError("First Agent registration failed: " + registrationFailure, this);
                Host.Stop();
            }
        }

        public void SampleInputs() => _distance = Mathf.Abs(_targetX - transform.position.x);

        public bool TryFlushInputs(out NativeRuntimeFailureV1 failure)
            => Host.TryWriteExternalTreeValue(_distanceHandle, _distance, out _, out failure);

        public void Receive(in ProductionTreeEffect effect)
        {
            if (effect.StableId != StableHash.Fnv1A64("move-speed") || !effect.TryGetFloat32(out var speed)) return;
            var position = transform.position;
            position.x = Mathf.MoveTowards(position.x, _targetX, speed * Time.deltaTime);
            transform.position = position;
        }

        private void OnDestroy()
        {
            _world?.TryUnregister(this);
            Host?.Stop();
            _program?.Dispose();
        }
    }
}
