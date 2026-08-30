using System.IO;
using UnityEditor;
using UnityEngine;

namespace AIBT.Mcp
{
    /// <summary>
    /// Brings the MCP bridge back after a domain reload destroys its <see cref="McpBridgeListener"/>
    /// instance. <c>[InitializeOnLoad]</c>'s static constructor runs after every domain reload (and
    /// on Editor startup); <see cref="SessionState"/> (unlike a plain static field) survives that
    /// reload within the same Editor session, so it is what records "was the bridge actually
    /// running a moment ago, deliberately, not just stopped." Found necessary by P6-009: it is the
    /// first MCP tool group whose calls write real .cs source, so it is the first to ever trigger
    /// the domain reload that kills the bridge mid-session -- every prior P6 card only wrote data
    /// files, which Unity never recompiles.
    /// </summary>
    [InitializeOnLoad]
    internal static class McpBridgeAutoRestart
    {
        private const string WasRunningSessionKey = "AIBT.Mcp.BridgeWasRunning";
        private static McpBridgeListener _listener;

        static McpBridgeAutoRestart()
        {
            if (!SessionState.GetBool(WasRunningSessionKey, false))
            {
                return;
            }

            var libraryDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
            _listener = new McpBridgeListener(libraryDirectory, Application.dataPath);
            _listener.Start();
        }

        /// <summary>Called by McpBridgeListener.Start()/Stop() to record whether a restart should happen after the next domain reload.</summary>
        internal static void NotifyRunning(bool running)
        {
            SessionState.SetBool(WasRunningSessionKey, running);
        }
    }
}
