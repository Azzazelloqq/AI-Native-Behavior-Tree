#if AIBT_UNITY_AUDIO
using UnityEngine;

namespace AIBT.Nodes
{
    // Compiled only when the module behind AIBT_UNITY_AUDIO is enabled (version define in AIBT.Agents.asmdef).

    [BtNode("aibt.unity.play-audio", Category = "Audio",
        Summary = "Plays an AudioSource. Succeeds at once, or when the clip ends if wait-until-finished is set; fails when there is no AudioSource.",
        WhenToUse = "Use for barks and effect sounds. With wait-until-finished an interrupt stops the sound.",
        WhenNotToUse = "For overlapping one-shots on one source, call AudioSource.PlayOneShot from your own node.")]
    public sealed class PlayAudioNode : BtAction
    {
        public string reference = string.Empty;

        [BtParam("wait-until-finished")]
        public bool waitUntilFinished;

        private AudioSource _source;

        protected override void OnEnter()
        {
            _source = ResolveComponent<AudioSource>(reference);
            if (_source != null) _source.Play();
        }

        protected override NodeStatus OnTick()
        {
            if (_source == null)
            {
                WarnOnce("found no AudioSource on '" + SceneReference.Describe(reference) + "'.");
                return NodeStatus.Failure;
            }

            if (!waitUntilFinished) return NodeStatus.Success;
            return _source.isPlaying ? NodeStatus.Running : NodeStatus.Success;
        }

        protected override void OnAbort()
        {
            if (waitUntilFinished && _source != null) _source.Stop();
        }

        protected override void OnExit() => _source = null;
    }
}
#endif
