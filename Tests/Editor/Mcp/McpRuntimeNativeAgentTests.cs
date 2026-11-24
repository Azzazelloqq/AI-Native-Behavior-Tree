using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using AIBT.Mcp;
using AIBT.Tests.Consumer;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Editor.Mcp
{
    /// <summary>
    /// P11-007 (review F7): MCP runtime_hosts and read_trace see the host of a real
    /// <see cref="NativeTreeAgent"/>, which the agent creates with HideInInspector | DontSave flags
    /// that FindObjectsByType skips. Before the fix neither tool listed it.
    /// </summary>
    public sealed class McpRuntimeNativeAgentTests
    {
        private readonly List<Object> _created = new List<Object>();
        private readonly List<NativeTreeAgent> _agents = new List<NativeTreeAgent>();
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;
        private int _baselinePrograms;

        [SetUp]
        public void SetUp()
        {
            _baselinePrograms = NativeTreeProgramCache.LiveProgramCount;
            var worldObject = Track(new GameObject("AIBT.Tests.P11007.McpWorld"));
            _scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            _world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(_world, "Awake");
            InvokePrivate(_world, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var agent in _agents) if (agent != null) agent.Deactivate();
            _agents.Clear();
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) Object.DestroyImmediate(_created[index]);
            _created.Clear();
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms), "A test leaked a shared program.");
        }

        [Test]
        public void RuntimeHostsAndReadTrace_SeeANativeTreeAgentsHost()
        {
            var agent = Spawn();
            InvokePrivate(_scheduler, "Update");
            agent.ProcessRootResult();
            var host = agent.Host;
            Assert.That(host, Is.Not.Null, agent.LastError);
            Assert.That(host.hideFlags & (HideFlags.HideInInspector | HideFlags.DontSave), Is.EqualTo(HideFlags.HideInInspector | HideFlags.DontSave),
                "The agent's host carries the flags FindObjectsByType skips.");

            var hosts = (JArray)McpRuntimeTools.Hosts()["hosts"];
            var listed = hosts.Where(item => (ulong)item["instanceId"] == host.InstanceId).ToList();
            Assert.That(listed, Has.Count.EqualTo(1), hosts.ToString());
            Assert.That((string)listed[0]["objectName"], Is.EqualTo(agent.name));
            Assert.That((string)listed[0]["scenePath"], Is.EqualTo(agent.gameObject.scene.path));
            Assert.That((long)listed[0]["updates"], Is.GreaterThan(0L));
            Assert.That((string)listed[0]["programHash"], Is.EqualTo(host.Program.Header.CompiledContentHash.HexadecimalValue));

            var trace = McpRuntimeTools.Trace(new JObject { ["instanceId"] = host.InstanceId });
            Assert.That((ulong)trace["instanceId"], Is.EqualTo(host.InstanceId), trace.ToString());
            Assert.That(trace["available"], Is.Not.Null, trace.ToString());
        }

        [Test]
        public void ReadTrace_OfAnAgentWithTraceOff_IsNotAvailable()
        {
            // P11-016: an agent set to Off has a host but no trace channel.
            var agent = Spawn(NativeTreeAgentTraceMode.Off);
            Assert.That(agent.Host.TraceChannelOwner, Is.Null);
            var trace = McpRuntimeTools.Trace(new JObject { ["instanceId"] = agent.Host.InstanceId });
            Assert.That((ulong)trace["instanceId"], Is.EqualTo(agent.Host.InstanceId), trace.ToString());
            Assert.That((bool)trace["available"], Is.False, trace.ToString());
        }

        [Test]
        public void AStoppedAgent_HasNoListedHost()
        {
            var agent = Spawn();
            var instanceId = agent.Host.InstanceId;
            agent.Stop();
            Assert.That(agent.Host, Is.Null);
            var hosts = (JArray)McpRuntimeTools.Hosts()["hosts"];
            Assert.That(hosts.Any(item => (ulong)item["instanceId"] == instanceId), Is.False, hosts.ToString());
        }

        private NativeTreeAgent Spawn(NativeTreeAgentTraceMode trace = NativeTreeAgentTraceMode.Auto)
        {
            var asset = ProductionTreeAssetBaker.Create(ProductionConsumerWorkflowTests.Tree(SemanticValue.FromNumber(1.5)),
                "Assets/p11007-mcp-agent.aibt.json", typeof(ConsumerCatalog), out var diagnostics);
            Assert.That(asset, Is.Not.Null, string.Join("; ", diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            Track(asset);
            var gameObject = Track(new GameObject("AIBT.Tests.P11007.McpAgent" + _agents.Count));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = asset;
            agent.World = _world;
            agent.RestartMode = NativeTreeRestartMode.Never;
            agent.CatalogFactory = ConsumerCatalog.TryCreateRuntimeCatalog;
            agent.TraceMode = trace;
            _agents.Add(agent);
            agent.Activate();
            Assert.That(agent.Host, Is.Not.Null, agent.LastError);
            return agent;
        }

        private T Track<T>(T value) where T : Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
