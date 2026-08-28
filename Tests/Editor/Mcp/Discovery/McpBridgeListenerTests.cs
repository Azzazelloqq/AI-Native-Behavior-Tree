using System.IO;
using System.Net.Sockets;
using System.Text;
using AIBT.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp.Discovery
{
    public sealed class McpBridgeListenerTests
    {
        private string _libraryDir;
        private string _projectRoot;

        [SetUp]
        public void CreateTempDirs()
        {
            var root = Path.Combine(Path.GetTempPath(), "aibt-mcp-bridge-" + System.Guid.NewGuid().ToString("N"));
            _libraryDir = Path.Combine(root, "Library");
            _projectRoot = Path.Combine(root, "Assets");
            Directory.CreateDirectory(_libraryDir);
            Directory.CreateDirectory(_projectRoot);
        }

        [TearDown]
        public void RemoveTempDirs()
        {
            var root = Directory.GetParent(_libraryDir)?.FullName;
            if (root != null && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void RepeatedStartStopCyclesLeaveNoDegradedState()
        {
            var listener = new McpBridgeListener(_libraryDir, _projectRoot);
            var discoveryFile = Path.Combine(_libraryDir, "AibtMcp.json");

            for (var cycle = 0; cycle < 3; cycle++)
            {
                listener.Start();
                Assert.That(listener.IsRunning, Is.True);
                Assert.That(listener.Port, Is.GreaterThan(0));
                Assert.That(File.Exists(discoveryFile), Is.True, "cycle " + cycle);

                listener.Stop();
                Assert.That(listener.IsRunning, Is.False);
                Assert.That(File.Exists(discoveryFile), Is.False, "cycle " + cycle);
            }
        }

        [Test]
        public void ARealTcpClientCanConnectAndReceiveADispatchedResponse()
        {
            var listener = new McpBridgeListener(_libraryDir, _projectRoot);
            listener.Start();
            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect("127.0.0.1", listener.Port);
                    using (var stream = client.GetStream())
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        writer.WriteLine("{\"tool\":\"get_node_contract\",\"args\":{\"typeId\":\"aibt.core.inverter\"},\"grantedCategories\":[\"Read\"]}");
                        var responseLine = reader.ReadLine();
                        var response = JObject.Parse(responseLine);
                        Assert.That((bool)response["result"]["found"], Is.True);
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
