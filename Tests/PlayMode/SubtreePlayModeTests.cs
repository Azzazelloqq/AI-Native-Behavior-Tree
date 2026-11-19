using System.Collections;
using AIBT.Authoring;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIBT.Tests.PlayMode
{
    /// <summary>
    /// ADR-P11-001 Stage 1: a <see cref="BehaviorTreeAgent"/> runs a tree that uses the same subtree
    /// twice in Play Mode, through the public API only (a tree from JSON plus an in-memory resolver).
    /// </summary>
    public sealed class SubtreePlayModeTests
    {
        private const string Counter = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.counter"", ""name"": ""Counter"", ""root"": ""main"",
  ""interface"": { ""version"": 1, ""keys"": [""total""] },
  ""blackboard"": {
    ""count"": { ""type"": ""Int32"", ""default"": 0 },
    ""total"": { ""type"": ""Int32"", ""default"": 0 }
  },
  ""nodes"": {
    ""main"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""own"", ""shared""] },
    ""own"": { ""type"": ""aibt.unity.increment-int"", ""typeVersion"": 1, ""parameters"": { ""key"": ""count"", ""amount"": 1 } },
    ""shared"": { ""type"": ""aibt.unity.increment-int"", ""typeVersion"": 1, ""parameters"": { ""key"": ""total"", ""amount"": 10 } }
  }
}";

        private const string Parent = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.counters"", ""name"": ""Two Counters"", ""root"": ""root"",
  ""blackboard"": { ""sum"": { ""type"": ""Int32"", ""default"": 0 } },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""a"", ""b""] },
    ""a"": { ""subtree"": { ""treeId"": ""tests.p11.counter"", ""interfaceVersion"": 1, ""keys"": { ""total"": ""sum"" } } },
    ""b"": { ""subtree"": { ""treeId"": ""tests.p11.counter"", ""interfaceVersion"": 1, ""keys"": { ""total"": ""sum"" } } }
  }
}";

        private GameObject _agentObject;
        private BehaviorTreeSource _source;

        [TearDown]
        public void TearDown()
        {
            if (_agentObject != null) Object.Destroy(_agentObject);
            if (_source != null) Object.Destroy(_source);
        }

        [UnityTest]
        public IEnumerator BehaviorTreeAgent_RunsATreeWithSubtreesInPlayMode()
        {
            var counter = CanonicalTreeJson.Parse(Counter, "trees/counter.aibt");
            Assert.That(counter.Success, Is.True);
            var resolver = new InMemorySubtreeResolver().Add(counter.Document, "trees/counter.aibt");

            _agentObject = new GameObject("subtree-agent");
            var agent = _agentObject.AddComponent<BehaviorTreeAgent>();
            agent.RestartOnComplete = false;
            _source = BehaviorTreeSource.FromJson(Parent, "subtree-playmode", resolver);
            agent.Tree = _source;
            Assert.That(agent.Blackboard, Is.Not.Null, agent.LastError);

            NodeStatus? result = null;
            agent.Completed += status => result = status;
            var deadline = Time.realtimeSinceStartup + 10f;
            while (result == null && Time.realtimeSinceStartup < deadline) yield return null;

            Assert.That(agent.HasFaulted, Is.False, agent.LastError);
            Assert.That(result, Is.EqualTo(NodeStatus.Success));
            Assert.That(agent.Blackboard.Get<int>("a:count"), Is.EqualTo(1), "Each instance has its own private key.");
            Assert.That(agent.Blackboard.Get<int>("b:count"), Is.EqualTo(1));
            Assert.That(agent.Blackboard.Get<int>("sum"), Is.EqualTo(20), "Both instances write the parent's bound key.");
        }
    }
}
