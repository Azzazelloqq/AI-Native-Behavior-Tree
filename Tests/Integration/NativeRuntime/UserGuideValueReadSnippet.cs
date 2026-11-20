using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-004: the "Reading a value back" block of <c>Documentation~/consumer-quickstart.md</c> is
    /// quoted line by line from this compiled file (<c>UserGuideSnippetTests</c>). The keys are those
    /// of the Native Guard sample.
    /// </summary>
    internal sealed class UserGuideValueReadSnippet : MonoBehaviour
    {
        private NativeTreeAgent agent;
        private string spotText;
        private long lastDeadline;

        internal string SpotText => spotText;
        internal long LastDeadline => lastDeadline;

        private void Start()
        {
            agent = GetComponent<NativeTreeAgent>();
            // The final values of a run: read them in Completed, before the next run starts.
            agent.Completed += result => agent.TryGetValue("weapon-ready-at", out lastDeadline);
        }

        private void LateUpdate()
        {
            // The current run's value; false while the agent has no run.
            if (agent.TryGetValue("sweep-spot", out int spot))
                spotText = spot < 0 ? "no spot" : "spot " + spot;
        }
    }
}
