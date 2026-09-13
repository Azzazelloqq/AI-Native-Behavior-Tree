using AIBT.Editor.Debugger;
using AIBT.Editor.Trace;
using UnityEditor;
using UnityEngine;

namespace AIBT.Samples.SwarmArena.Editor
{
    /// <summary>Minimal sample convenience around the existing caller-owned trace attachment API.</summary>
    internal static class SwarmArenaTraceMenu
    {
        private const string MenuPath = "AIBT/Samples/Swarm Arena/Attach Selected Agent Trace";

        [MenuItem(MenuPath)]
        private static void AttachSelectedAgent()
        {
            var host = Selection.activeGameObject == null
                ? null
                : Selection.activeGameObject.GetComponent<ProductionTreeHost>();
            if (host == null || host.TraceChannelOwner == null)
            {
                Debug.LogWarning("AIBT Swarm Arena: select a live Swarm Agent ProductionTreeHost first.");
                return;
            }

            var session = new NativeExecutionDebuggerSession();
            session.Attach(host.TraceChannelOwner);
            TraceTimelineWindow.ShowWindow().AttachSession(session);
        }

        [MenuItem(MenuPath, true)]
        private static bool CanAttachSelectedAgent()
            => Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<ProductionTreeHost>() != null;
    }
}
