using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace AIBT.Mcp
{
    /// <summary>
    /// The Unity-side half of ADR-P6-001's bridge: a TCP listener with no MCP SDK dependency,
    /// discovered by the external MCP~/Server/ process via a discovery file under Library/.
    /// Explicit start/stop only (McpBridgeWindow, or a direct caller) -- never auto-started with
    /// the Editor itself. <see cref="Start"/>/<see cref="Stop"/> record the running state in
    /// <see cref="SessionState"/> (survives a domain reload within the same Editor session,
    /// unlike a plain field) so <see cref="McpBridgeAutoRestart"/> can bring a live instance back
    /// after a script compile's domain reload destroys this object -- found necessary by P6-009,
    /// the first card whose tools write real .cs source the Editor recompiles; every prior P6
    /// tool only ever wrote data files (*.aibt.json/*.aibtcase.json), which never triggers a
    /// domain reload, so this gap never surfaced before.
    /// </summary>
    public sealed class McpBridgeListener : IDisposable
    {
        private readonly string _discoveryFilePath;
        private readonly string _projectRoot;
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        public McpBridgeListener(string libraryDirectory, string projectRoot)
        {
            _discoveryFilePath = Path.Combine(libraryDirectory, "AibtMcp.json");
            _projectRoot = projectRoot;
        }

        public bool IsRunning => _running;

        public int Port { get; private set; }

        public void Start()
        {
            if (_running)
            {
                return;
            }

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _running = true;

            WriteDiscoveryFile();
            McpBridgeAutoRestart.NotifyRunning(true);

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "AibtMcpBridge" };
            _acceptThread.Start();
        }

        /// <summary>Explicit stop, e.g. from the Bridge window's own Stop button. Clears the auto-restart flag -- a deliberate stop must not be immediately undone by the next domain reload.</summary>
        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            McpBridgeAutoRestart.NotifyRunning(false);
            try
            {
                _listener.Stop();
            }
            catch (SocketException)
            {
            }

            RemoveDiscoveryFile();
        }

        public void Dispose()
        {
            Stop();
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    return; // listener was stopped
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                ServeClient(client);
            }
        }

        private void ServeClient(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
            {
                string line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    string response;
                    try
                    {
                        response = McpToolDispatcher.Dispatch(line, _projectRoot);
                    }
                    catch (Exception ex)
                    {
                        response = new JObject
                        {
                            ["error"] = new JObject { ["code"] = "AIBT9013", ["message"] = ex.Message },
                        }.ToString(Newtonsoft.Json.Formatting.None);
                    }

                    writer.WriteLine(response);
                }
            }
        }

        private void WriteDiscoveryFile()
        {
            var json = new JObject
            {
                ["port"] = Port,
                ["process_id"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                ["project_path"] = _projectRoot.Replace('\\', '/'),
            }.ToString(Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(_discoveryFilePath, json);
        }

        private void RemoveDiscoveryFile()
        {
            try
            {
                if (File.Exists(_discoveryFilePath))
                {
                    File.Delete(_discoveryFilePath);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
