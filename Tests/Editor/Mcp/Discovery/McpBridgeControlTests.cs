using System;
using System.Collections;
using System.IO;
using AIBT.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIBT.Tests.Editor.Mcp.Discovery
{
    public sealed class McpBridgeControlTests
    {
        [UnityTest]
        public IEnumerator AnExplicitControlRequestStartsOneBridgeAndStopRemovesDiscovery()
        {
            var wasRunning = McpBridgeAutoRestart.Listener?.IsRunning == true;
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "AIBT", "bridge-control"));
            Directory.CreateDirectory(directory);
            var id = Guid.NewGuid().ToString("N");
            var requestPath = Path.Combine(directory, id + ".request.json");
            var responsePath = Path.Combine(directory, id + ".response.json");
            try
            {
                McpBridgeAutoRestart.Stop();
                File.WriteAllText(requestPath, new JObject
                {
                    ["formatVersion"] = 1, ["requestId"] = id, ["operation"] = "ensure",
                    ["expiresUtcTicks"] = DateTime.UtcNow.AddSeconds(10).Ticks,
                }.ToString());
                var deadline = EditorApplication.timeSinceStartup + 5;
                while (!File.Exists(responsePath) && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(File.Exists(responsePath), Is.True, "Editor must acknowledge an explicit connection request.");
                Assert.That(McpBridgeAutoRestart.Listener?.IsRunning, Is.True);
                var port = McpBridgeAutoRestart.Listener.Port;
                McpBridgeAutoRestart.EnsureRunning();
                Assert.That(McpBridgeAutoRestart.Listener.Port, Is.EqualTo(port), "Ensure must not replace a healthy listener.");
                Assert.That((string)JObject.Parse(File.ReadAllText(responsePath))["requestId"], Is.EqualTo(id));
                McpBridgeAutoRestart.Stop();
                Assert.That(File.Exists(Path.Combine(Application.dataPath, "..", "Library", "AibtMcp.json")), Is.False);
            }
            finally
            {
                File.Delete(requestPath);
                File.Delete(responsePath);
                McpBridgeAutoRestart.Stop();
                if (wasRunning) McpBridgeAutoRestart.EnsureRunning();
            }
        }

        [UnityTest]
        public IEnumerator ExpiredRequestsDoNotStartAListener()
        {
            var wasRunning = McpBridgeAutoRestart.Listener?.IsRunning == true;
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "AIBT", "bridge-control"));
            Directory.CreateDirectory(directory);
            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(directory, id + ".request.json");
            try
            {
                McpBridgeAutoRestart.Stop();
                File.WriteAllText(path, new JObject { ["formatVersion"] = 1, ["requestId"] = id, ["operation"] = "ensure", ["expiresUtcTicks"] = DateTime.UtcNow.AddSeconds(-1).Ticks }.ToString());
                var deadline = EditorApplication.timeSinceStartup + 5;
                while (File.Exists(path) && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(File.Exists(path), Is.False);
                Assert.That(McpBridgeAutoRestart.Listener, Is.Null);
            }
            finally
            {
                File.Delete(path);
                if (wasRunning) McpBridgeAutoRestart.EnsureRunning();
            }
        }
    }
}
