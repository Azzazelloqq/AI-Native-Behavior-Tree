using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AIBT.Agents;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P9-002 helpers: the package's own standard node classes as a node set, blackboard key
    /// declarations, and a disposable scene agent driven with manual ticks.
    /// </summary>
    internal static class StandardNodeKit
    {
        private static ManagedNodeSet s_nodes;

        /// <summary>Every [BtNode] class shipped in AIBT.Agents, and nothing from the project.</summary>
        internal static IReadOnlyList<Type> StandardTypes => typeof(BtNode).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(BtNode).IsAssignableFrom(type) && type.IsDefined(typeof(BtNodeAttribute), false))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        internal static ManagedNodeSet Nodes
        {
            get
            {
                if (s_nodes != null) return s_nodes;
                s_nodes = ManagedNodeSet.Create(StandardTypes);
                Assert.That(s_nodes.Problems, Is.Empty, string.Join("\n", s_nodes.Problems));
                return s_nodes;
            }
        }

        internal static string Bool(string name, bool value = false)
            => Key(name, "Bool", value ? "true" : "false");

        internal static string Int(string name, int value = 0)
            => Key(name, "Int32", value.ToString(CultureInfo.InvariantCulture));

        internal static string Float(string name, float value = 0f)
            => Key(name, "Float32", value.ToString("R", CultureInfo.InvariantCulture));

        internal static string Vec3(string name, Vector3 value = default)
            => Key(name, "Float3", string.Format(CultureInfo.InvariantCulture, "{{ \"x\": {0:R}, \"y\": {1:R}, \"z\": {2:R} }}", value.x, value.y, value.z));

        internal static string Keys(params string[] keys) => string.Join(", ", keys);

        internal static ManagedTreeInstance Headless(string json)
        {
            Assert.That(ManagedTreeProgram.TryCompile(json, "trees/standard.aibt", Nodes, out var program, out var diagnostics), Is.True,
                ManagedTreeProgram.Describe(diagnostics));
            return new ManagedTreeInstance(program, null);
        }

        /// <summary>Runs a tree to completion within <paramref name="maxTicks"/> and returns its result.</summary>
        internal static NodeStatus RunToEnd(ManagedTreeInstance instance, float delta = 0.1f, int maxTicks = 1000)
        {
            for (var tick = 0; tick < maxTicks; tick++)
            {
                var outcome = instance.Tick(delta);
                Assert.That(outcome, Is.Not.EqualTo(ManagedTickOutcome.Faulted), instance.FaultMessage);
                if (outcome == ManagedTickOutcome.Completed) return instance.LastResult.Value;
            }

            Assert.Fail("The tree did not finish within " + maxTicks + " ticks.");
            return NodeStatus.Failure;
        }

        /// <summary>Runs a one-shot tree again from its root, keeping blackboard values.</summary>
        internal static NodeStatus RunAgain(ManagedTreeInstance instance, float delta = 0.1f)
        {
            Assert.That(instance.Restart(false), Is.True);
            return RunToEnd(instance, delta);
        }

        private static string Key(string name, string type, string value)
            => "\"" + name + "\": { \"type\": \"" + type + "\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": " + value + " }";
    }

    /// <summary>A GameObject with a manually ticked BehaviorTreeAgent, destroyed by <see cref="Dispose"/>.</summary>
    internal sealed class SceneAgent : IDisposable
    {
        private readonly List<Object> _created = new List<Object>();

        internal SceneAgent(string name = "standard-node-agent")
        {
            GameObject = Track(new GameObject(name));
            Agent = GameObject.AddComponent<BehaviorTreeAgent>();
            Agent.TickMode = AgentTickMode.Manual;
            Agent.RestartOnComplete = false;
        }

        internal GameObject GameObject { get; }

        internal BehaviorTreeAgent Agent { get; }

        internal Transform Transform => GameObject.transform;

        internal AgentBlackboard Blackboard => Agent.Blackboard;

        internal T Track<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        internal GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(Transform, false);
            return child;
        }

        internal GameObject Other(string name, Vector3 position)
        {
            var other = Track(new GameObject(name));
            other.transform.position = position;
            return other;
        }

        internal void Run(string json)
        {
            var source = Track(BehaviorTreeSource.FromJson(json, "standard-test", StandardNodeKit.Nodes));
            Agent.Tree = source;
            Assert.That(Agent.Blackboard, Is.Not.Null, Agent.LastError);
        }

        /// <summary>Ticks once and returns the tree's status: Running while it has not finished.</summary>
        internal NodeStatus Tick(float delta = 0.1f)
        {
            Agent.Tick(delta);
            Assert.That(Agent.HasFaulted, Is.False, Agent.LastError);
            return Agent.LastResult ?? NodeStatus.Running;
        }

        /// <summary>Ticks until the tree finishes and returns its result.</summary>
        internal NodeStatus TickToEnd(float delta = 0.1f, int maxTicks = 1000)
        {
            for (var tick = 0; tick < maxTicks; tick++)
            {
                var status = Tick(delta);
                if (status != NodeStatus.Running) return status;
            }

            Assert.Fail("The tree did not finish within " + maxTicks + " ticks.");
            return NodeStatus.Failure;
        }

        public void Dispose()
        {
            for (var index = _created.Count - 1; index >= 0; index--)
            {
                if (_created[index] != null) Object.DestroyImmediate(_created[index]);
            }

            _created.Clear();
        }
    }
}
