using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AIBT.Samples.SwarmArena
{
    /// <summary>
    /// Sample-only visual facade. Native hosts own the simulation; this component performs at most
    /// one instanced draw submission per committed visible state and never decides agent behaviour.
    /// </summary>
    public sealed class SwarmArenaInstancedPresenter : MonoBehaviour
    {
        private const int StateCount = 4;
        private readonly Matrix4x4[][] _instances =
        {
            new Matrix4x4[64], new Matrix4x4[64], new Matrix4x4[64], new Matrix4x4[64],
        };

        private readonly int[] _counts = new int[StateCount];
        private readonly Material[] _materials = new Material[StateCount];
        private Mesh _mesh;

        public void Render(IList<SwarmArenaAgent> agents)
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
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * 128f),
                };
                Graphics.RenderMeshInstanced(
                    parameters, _mesh, 0, _instances[state], _counts[state], 0);
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
