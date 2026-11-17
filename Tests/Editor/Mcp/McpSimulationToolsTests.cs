using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using AIBT.Mcp;
using AIBT.Simulation;
using AIBT.Tests.Consumer;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp
{
    /// <summary>
    /// P10-012 (ADR-P10-008 stage G4): the read-only MCP tools over registered simulation containers
    /// (runtime_simulations, describe_agent, read_agent_trace), at the dispatcher entry point the bridge calls.
    /// </summary>
    public sealed class McpSimulationToolsTests
    {
        private const int Agents = 500;
        private string _root;
        private ProductionTreeAsset _asset;
        private ProductionTreeProgram _program;
        private TreeSimulation _simulation;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "aibt-mcp-simulation-" + Guid.NewGuid().ToString("N"), "Assets");
        }

        [TearDown]
        public void TearDown()
        {
            if (_simulation != null)
            {
                TreeSimulationDebug.Unregister(_simulation);
                _simulation.Dispose();
                _simulation = null;
            }
            _program?.Dispose();
            _program = null;
            if (_asset != null) UnityEngine.Object.DestroyImmediate(_asset);
            _asset = null;
        }

        private JObject Dispatch(string tool, JObject args)
            => JObject.Parse(McpToolDispatcher.Dispatch(new JObject
            {
                ["tool"] = tool, ["args"] = args ?? new JObject(), ["grantedCategories"] = new JArray("Read"),
            }.ToString(), _root));

        /// <summary>500 agents of the consumer tree (one native leaf) thinking every 5 ticks, 12 ticks stepped, agent 7 traced.</summary>
        private TreeGroup Crowd(string name)
        {
            _asset = ProductionTreeAssetBaker.Create(ProductionConsumerWorkflowTests.Tree(SemanticValue.FromNumber(1.5)),
                "Assets/simulation-tools.aibt.json", typeof(ConsumerCatalog), out var diagnostics);
            Assert.That(_asset, Is.Not.Null, string.Join("; ", diagnostics));
            Assert.That(_asset.TryCreateProgram(ConsumerCatalog.TryCreateRuntimeCatalog, out _program, out var failure), Is.True, failure.ToString());
            _simulation = new TreeSimulation(TreeSimulationSettings.Default(33333));
            Assert.That(_simulation.TryAddGroup(_program, TreeGroupSettings.Default(Agents), out var group), Is.EqualTo(SimulationResult.Success));
            for (var index = 0; index < Agents; index++)
                Assert.That(group.TryAdd(ThinkSchedule.Spread(5), out _), Is.EqualTo(SimulationResult.Success));
            Assert.That(group.TryGetAgentAt(7, out var traced), Is.True);
            Assert.That(group.TrySetTraced(traced, true), Is.EqualTo(SimulationResult.Success));
            for (uint tick = 1; tick <= 12; tick++) Assert.That(_simulation.Step(tick), Is.EqualTo(SimulationResult.Success));
            Assert.That(TreeSimulationDebug.Register(_simulation, name), Is.True);
            return group;
        }

        [Test]
        public void WithNothingRegistered_TheListIsEmptyWithAHint_AndAgentToolsExplain()
        {
            var registered = new List<RegisteredSimulation>();
            Assume.That(TreeSimulationDebug.CopyRegistered(registered), Is.Zero, "Another container is registered in this Editor.");
            var list = Dispatch("runtime_simulations", null);
            Assert.That((JArray)list["result"]["simulations"], Is.Empty, list.ToString());
            Assert.That((string)list["result"]["hint"], Does.Contain("TreeSimulationDebug.Register"));

            foreach (var tool in new[] { "describe_agent", "read_agent_trace" })
            {
                var response = Dispatch(tool, new JObject { ["simulation"] = "crowd", ["group"] = 1, ["agent"] = 0 });
                Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9018"), response.ToString());
                Assert.That(response["error"].ToString(), Does.Contain("No simulation container is registered"));
            }
        }

        [Test]
        public void ARegisteredContainer_IsListed_DescribedAndItsTracedAgentRead()
        {
            var group = Crowd("p10012-crowd");
            var list = Dispatch("runtime_simulations", null);
            var simulation = ((JArray)list["result"]["simulations"]).Single(item => (string)item["name"] == "p10012-crowd");
            Assert.That((uint)simulation["lastTick"], Is.EqualTo(12u));
            Assert.That((bool)simulation["stepping"], Is.False);
            var info = (JObject)simulation["groups"].Single();
            Assert.That((int)info["id"], Is.EqualTo(group.Id));
            Assert.That((int)info["count"], Is.EqualTo(Agents));
            Assert.That((int)info["tracedCount"], Is.EqualTo(1));
            Assert.That((int)info["tracedAgents"].Single()["agent"], Is.EqualTo(7));
            Assert.That((string)info["program"], Is.EqualTo("Assets/simulation-tools.aibt.json"));

            var describe = Dispatch("describe_agent", new JObject { ["simulation"] = "p10012-crowd", ["group"] = group.Id, ["agent"] = 7 });
            var agent = describe["result"];
            Assert.That(agent, Is.Not.Null, describe.ToString());
            Assert.That((bool)agent["busy"], Is.False);
            Assert.That((uint)agent["generation"], Is.EqualTo(1u));
            Assert.That((bool)agent["traced"], Is.True);
            Assert.That((uint)agent["schedule"]["interval"], Is.EqualTo(5u));
            Assert.That((uint)agent["lastThink"]["tick"], Is.LessThanOrEqualTo(12u));
            Assert.That(agent["lastThink"]["reasons"].Values<string>(), Does.Contain("Scheduled"));
            Assert.That((uint)agent["nextThinkTick"], Is.GreaterThan(12u));
            Assert.That((string)agent["text"], Does.StartWith("Agent " + group.Id + ":7#1 (Assets/simulation-tools.aibt.json)"));
            Assert.That((string)agent["text"], Does.Contain("Trace: "));

            var first = Dispatch("read_agent_trace", new JObject { ["simulation"] = "p10012-crowd", ["group"] = group.Id, ["agent"] = 7, ["limit"] = 4 });
            var page = first["result"];
            Assert.That(page, Is.Not.Null, first.ToString());
            Assert.That((bool)page["available"], Is.True);
            Assert.That((bool)page["traced"], Is.True);
            Assert.That((bool)page["complete"], Is.True);
            Assert.That((bool)page["droppedIsLowerBound"], Is.False);
            Assert.That((bool)page["hasMore"], Is.True);
            var events = (JArray)page["events"];
            Assert.That(events.Count, Is.EqualTo(4));
            Assert.That((string)events[0]["fact"], Is.EqualTo("Think"));
            Assert.That((ulong)events[0]["sequence"], Is.EqualTo(1ul));
            Assert.That((string)events.Last()["fact"], Is.Not.Null);
            var next = Dispatch("read_agent_trace", new JObject
            {
                ["simulation"] = "p10012-crowd", ["group"] = group.Id, ["agent"] = 7, ["afterSequence"] = (ulong)page["nextSequence"], ["limit"] = 512,
            });
            var rest = (JArray)next["result"]["events"];
            Assert.That((ulong)rest[0]["sequence"], Is.EqualTo(5ul), "Paging continues after the last sequence read.");
            Assert.That((bool)next["result"]["hasMore"], Is.False);
            Assert.That(4 + rest.Count, Is.EqualTo((int)(ulong)page["recorded"]));
            Assert.That(rest.Any(item => (string)item["kind"] == "NodeTicked" && (string)item["nodeId"] == "root"), Is.True, next.ToString());

            var untraced = Dispatch("read_agent_trace", new JObject { ["simulation"] = "p10012-crowd", ["group"] = group.Id, ["agent"] = 8 });
            Assert.That((bool)untraced["result"]["traced"], Is.False);
            Assert.That((string)untraced["result"]["reason"], Does.Contain("TrySetTraced"));

            var wrongName = Dispatch("describe_agent", new JObject { ["simulation"] = "other", ["group"] = group.Id, ["agent"] = 7 });
            Assert.That((string)wrongName["error"]["code"], Is.EqualTo("AIBT9018"));
            Assert.That(wrongName["error"].ToString(), Does.Contain("'p10012-crowd'"));
            var wrongSlot = Dispatch("describe_agent", new JObject { ["simulation"] = "p10012-crowd", ["group"] = group.Id, ["agent"] = Agents + 3 });
            Assert.That((string)wrongSlot["error"]["code"], Is.EqualTo("AIBT9018"));
            var badLimit = Dispatch("read_agent_trace", new JObject { ["simulation"] = "p10012-crowd", ["group"] = group.Id, ["agent"] = 7, ["limit"] = 0 });
            Assert.That((string)badLimit["error"]["code"], Is.EqualTo("AIBT9018"));
        }

        [Test]
        public void TheToolsAreReadOnly_AndNeedTheReadCategory()
        {
            var group = Crowd("p10012-readonly");
            var before = group.TracedCount;
            var denied = JObject.Parse(McpToolDispatcher.Dispatch(new JObject
            {
                ["tool"] = "describe_agent", ["args"] = new JObject { ["simulation"] = "p10012-readonly", ["group"] = group.Id, ["agent"] = 7 },
                ["grantedCategories"] = new JArray(),
            }.ToString(), _root));
            Assert.That(denied["error"], Is.Not.Null, denied.ToString());
            foreach (var tool in new[] { "runtime_simulations", "describe_agent", "read_agent_trace" })
            {
                Assert.That(McpBuiltInTools.BridgeToolNames, Does.Contain(tool));
                Assert.That((string)McpOperationCatalog.Describe(tool)["permission"], Is.EqualTo("Read"));
                Dispatch(tool, new JObject { ["simulation"] = "p10012-readonly", ["group"] = group.Id, ["agent"] = 7 });
            }
            Assert.That(group.TracedCount, Is.EqualTo(before));
            Assert.That(_simulation.LastTick, Is.EqualTo(12u), "No tool steps the container.");
        }
    }
}
