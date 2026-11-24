using AIBT.Editor.Debugger;
using UnityEditor;
using UnityEngine;

namespace AIBT.Editor.Trace
{
    internal static class SelectedHostTraceMenu
    {
        [MenuItem("AIBT/Attach Selected Host Trace")]
        private static void AttachSelected()
        {
            var host = Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponent<ProductionTreeHost>();
            if (!TryCreateSession(host, out var session, out var explanation))
            {
                EditorUtility.DisplayDialog("AIBT Trace", explanation, "OK");
                return;
            }
            TraceTimelineWindow.ShowWindow().AttachUnmappedSession(session);
        }

        internal static bool TryCreateSession(ProductionTreeHost host, out NativeExecutionDebuggerSession session, out string explanation)
        {
            session = null;
            if (host == null)
            {
                explanation = "Select a GameObject with a ProductionTreeHost.";
                return false;
            }
            var channel = host.TraceChannelOwner;
            if (channel == null && host.IsBootstrappedAndLive)
            {
                // P11-016: a host started with no trace level (ADR-P7-010, amendment 2026-11-24).
                explanation = "The selected host runs without a trace channel: its NativeTreeAgent's Trace is Off (or Auto in a Player), "
                    + "or it was bootstrapped with NativeTraceChannelCapacityV1.None. Set Trace to On (or pass a capacity) and start it again.";
                return false;
            }
            if (channel == null || channel.State == NativeOwnerStateV1.Disposed)
            {
                explanation = "The selected host has no live trace channel. Start and bootstrap it in Play mode first.";
                return false;
            }
            session = new NativeExecutionDebuggerSession();
            session.Attach(channel);
            explanation = null;
            return true;
        }
    }
}
