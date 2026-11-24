using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AIBT.Samples.SwarmArena
{
    /// <summary>
    /// Sample-only visual facade. Native hosts own the simulation; this component draws one mesh per
    /// agent in its committed visible state and never decides agent behaviour. With instancing it
    /// submits at most one instanced draw per state; without it, one draw per agent.
    /// </summary>
    public sealed class SwarmArenaInstancedPresenter : MonoBehaviour
    {
        private const int StateCount = 4;

        // Patrol, Pursue, Flee, Attack. Create Demo assigns material assets saved with instancing on,
        // so a Player build keeps the instancing shader variants. Empty entries fall back to a runtime
        // copy of the primitive's default material, which a build may have stripped of those variants.
        [SerializeField] private Material[] _stateMaterials = new Material[StateCount];
        [SerializeField] private Mesh _mesh;

        private readonly Matrix4x4[][] _instances =
        {
            new Matrix4x4[64], new Matrix4x4[64], new Matrix4x4[64], new Matrix4x4[64],
        };

        private readonly int[] _counts = new int[StateCount];
        private readonly Material[] _materials = new Material[StateCount];
        private readonly Material[] _runtimeMaterials = new Material[StateCount];
        private bool _resourcesReady;

        /// <summary>False in a Player without a graphics device (<c>-nographics</c>); nothing is drawn then.</summary>
        public static bool CanDraw => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>True when the device draws each state in one instanced call.</summary>
        public static bool UsesInstancing => SystemInfo.supportsInstancing;

        public void Render(IList<SwarmArenaAgent> agents)
        {
            if (agents == null || !CanDraw) return;
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
                var count = _counts[state];
                if (count == 0) continue;
                var material = _materials[state];
                var parameters = new RenderParams(material)
                {
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * 128f),
                };
                if (UsesInstancing && material.enableInstancing)
                {
                    Graphics.RenderMeshInstanced(parameters, _mesh, 0, _instances[state], count, 0);
                    continue;
                }
                var instances = _instances[state];
                for (var index = 0; index < count; index++) Graphics.RenderMesh(parameters, _mesh, 0, instances[index]);
            }
        }

        private void OnDestroy()
        {
            for (var index = 0; index < StateCount; index++)
            {
                if (_runtimeMaterials[index] != null) Destroy(_runtimeMaterials[index]);
                _runtimeMaterials[index] = null;
                _materials[index] = null;
            }
        }

        private void EnsureResources()
        {
            if (_resourcesReady) return;
            var missingMaterial = false;
            for (var index = 0; index < StateCount; index++)
            {
                _materials[index] = _stateMaterials != null && index < _stateMaterials.Length ? _stateMaterials[index] : null;
                if (_materials[index] == null) missingMaterial = true;
            }
            if (_mesh == null || missingMaterial) CreateFallbackResources();
            _resourcesReady = true;
        }

        // The original run-time path, kept for a hand-made scene that assigns no materials or mesh.
        private void CreateFallbackResources()
        {
            var prototype = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            prototype.SetActive(false);
            if (_mesh == null) _mesh = prototype.GetComponent<MeshFilter>().sharedMesh;
            var prototypeMaterial = prototype.GetComponent<MeshRenderer>().sharedMaterial;
            for (var index = 0; index < StateCount; index++)
            {
                if (_materials[index] != null) continue;
                var material = new Material(prototypeMaterial) { enableInstancing = true };
                var color = StateColor(index);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                _runtimeMaterials[index] = material;
                _materials[index] = material;
            }
            Destroy(prototype);
        }

        /// <summary>Display colour of a visible state index (0 Patrol, 1 Pursue, 2 Flee, 3 Attack).</summary>
        public static Color StateColor(int stateIndex)
        {
            switch (stateIndex)
            {
                case 0: return new Color(0.25f, 0.75f, 1f);
                case 1: return new Color(0.35f, 1f, 0.45f);
                case 2: return new Color(1f, 0.78f, 0.2f);
                default: return new Color(1f, 0.25f, 0.25f);
            }
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
