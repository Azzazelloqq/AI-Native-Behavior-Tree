using UnityEngine;

namespace AIBT.Samples.SimpleAgent
{
    /// <summary>
    /// Gameplay code reacting to a tree event: the guard's tree raises "spotted"
    /// (aibt.unity.raise-event) and this beacon flashes and counts. It only subscribes by name;
    /// it knows nothing about the tree.
    /// </summary>
    public sealed class SpottedFlash : MonoBehaviour
    {
        public BehaviorTreeAgent source;
        public Renderer beacon;
        public Color idle = new Color(0.2f, 0.2f, 0.25f);
        public Color flash = new Color(1f, 0.85f, 0.1f);
        public float seconds = 0.6f;

        private float _until;
        private MaterialPropertyBlock _block;

        public int Count { get; private set; }

        private void OnEnable()
        {
            if (source != null) source.Events.Subscribe("spotted", OnSpotted);
        }

        private void OnDisable()
        {
            if (source != null) source.Events.Unsubscribe("spotted", OnSpotted);
        }

        private void OnSpotted(BehaviorTreeAgent agent)
        {
            Count++;
            _until = Time.time + seconds;
        }

        private void Update()
        {
            if (beacon == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            var color = Time.time < _until ? flash : idle;
            _block.SetColor("_BaseColor", color);
            _block.SetColor("_Color", color);
            beacon.SetPropertyBlock(_block);
        }
    }
}
