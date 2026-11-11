using System.IO;
using UnityEditor;
using UnityEngine;
using System;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp
{
    /// <summary>
    /// Owns the bridge for window/client requests and restores it after domain reload.
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
        private static double _nextPoll;

        internal static McpBridgeListener Listener => _listener;

        static McpBridgeAutoRestart()
        {
            EditorApplication.update += PollControlRequests;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.quitting += Stop;
            if (SessionState.GetBool(WasRunningSessionKey, false)) EnsureRunning();
        }

        internal static void EnsureRunning(bool restart = false)
        {
            if (restart) Stop();
            if (_listener == null)
                _listener = new McpBridgeListener(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library")), Application.dataPath);
            _listener.Start();
        }

        internal static void Stop()
        {
            _listener?.Stop();
            _listener = null;
        }

        private static void BeforeReload()
        {
            var running = _listener != null && _listener.IsRunning || SessionState.GetBool(WasRunningSessionKey, false);
            Stop();
            NotifyRunning(running);
        }

        private static void PollControlRequests()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 0.25d;
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "AIBT", "bridge-control"));
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.EnumerateFiles(directory, "*.request.json"))
            {
                try
                {
                    var request = JObject.Parse(File.ReadAllText(path));
                    var requestId = Path.GetFileName(path).Replace(".request.json", "");
                    var expires = (long?)request["expiresUtcTicks"];
                    if ((int?)request["formatVersion"] != 1 || (string)request["requestId"] != requestId
                        || (string)request["operation"] != "ensure"
                        || !expires.HasValue || expires.Value < DateTime.UtcNow.Ticks)
                    {
                        File.Delete(path);
                        continue;
                    }
                    // A previous acknowledgement may outlive a client that exited between writes.
                    var responsePath = Path.Combine(directory, requestId + ".response.json");
                    if (File.Exists(responsePath)) { File.Delete(path); continue; }
                    var restart = (bool?)request["restart"] == true && _listener != null
                        && (int?)request["observedPort"] == _listener.Port;
                    EnsureRunning(restart);
                    _listener.RefreshDiscovery();
                    var response = new JObject { ["formatVersion"] = 1, ["requestId"] = requestId, ["running"] = true, ["port"] = _listener.Port };
                    File.WriteAllText(responsePath + ".tmp", response.ToString());
                    File.Move(responsePath + ".tmp", responsePath);
                    File.Delete(path);
                }
                catch (Exception exception) when (exception is IOException || exception is Newtonsoft.Json.JsonException || exception is System.Net.Sockets.SocketException
                    || exception is ArgumentException || exception is FormatException || exception is InvalidCastException || exception is OverflowException)
                {
                    Debug.LogWarning("AIBT bridge control request failed: " + exception.Message);
                    File.Delete(path);
                }
            }
        }

        /// <summary>Called by McpBridgeListener.Start()/Stop() to record whether a restart should happen after the next domain reload.</summary>
        internal static void NotifyRunning(bool running)
        {
            SessionState.SetBool(WasRunningSessionKey, running);
        }
    }
}
