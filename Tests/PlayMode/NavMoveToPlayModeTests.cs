#if AIBT_UNITY_AI
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace AIBT.Tests.PlayMode
{
    /// <summary>
    /// P9-006: path following of <c>aibt.unity.nav-move-to</c>. Edit Mode never places a
    /// NavMeshAgent on a NavMesh (see StandardNavigationNodeTests), so this runs in Play Mode, through
    /// the public API only: a tree from JSON, the goal written by gameplay code into the blackboard.
    /// </summary>
    public sealed class NavMoveToPlayModeTests
    {
        private const string TreeJson = @"{
  ""format"": ""aibt.tree"",
  ""formatVersion"": 2,
  ""treeId"": ""tests.playmode.nav"",
  ""name"": ""Nav"",
  ""root"": ""route"",
  ""blackboard"": {
    ""goal"": { ""type"": ""Float3"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": { ""x"": 0.0, ""y"": 0.0, ""z"": 0.0 } }
  },
  ""nodes"": {
    ""route"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""go"", ""stop""] },
    ""go"": { ""type"": ""aibt.unity.nav-move-to"", ""typeVersion"": 1, ""parameters"": { ""target"": ""goal"", ""speed"": 6.0, ""stop-distance"": 0.5 } },
    ""stop"": { ""type"": ""aibt.unity.nav-stop"", ""typeVersion"": 1 }
  }
}";

        private readonly List<Object> _created = new List<Object>();
        private NavMeshDataInstance _navMesh;

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created) if (created != null) Object.Destroy(created);
            _created.Clear();
            _navMesh.Remove();
        }

        [UnityTest]
        public IEnumerator NavMoveToRoutesAroundAWallArrivesAndRestoresTheAgentsSpeed()
        {
            // A 20 x 20 floor with a non-walkable wall at x = 0 from z = -7 to z = 10: the only way
            // from (-5, -5) to (5, -5) passes below the wall's end.
            var floor = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(20f, 0.1f, 20f),
                transform = Matrix4x4.identity,
                area = 0,
            };
            var wall = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(1f, 2f, 17f),
                transform = Matrix4x4.Translate(new Vector3(0f, 1f, 1.5f)),
                area = 1,
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> { floor, wall },
                new Bounds(Vector3.zero, new Vector3(40f, 10f, 40f)), Vector3.zero, Quaternion.identity);
            _navMesh = NavMesh.AddNavMeshData(data);
            Assert.That(NavMesh.SamplePosition(new Vector3(-5f, 0f, -5f), out var start, 2f, NavMesh.AllAreas), Is.True);

            var go = new GameObject("nav-agent");
            _created.Add(go);
            go.transform.position = start.position;
            var navigator = go.AddComponent<NavMeshAgent>();
            var ownSpeed = navigator.speed;
            yield return null;
            Assert.That(navigator.isOnNavMesh, Is.True, "Play Mode places the NavMeshAgent on the NavMesh.");

            var agent = go.AddComponent<BehaviorTreeAgent>();
            agent.RestartOnComplete = false;
            var source = BehaviorTreeSource.FromJson(TreeJson, "nav-playmode");
            _created.Add(source);
            agent.Tree = source;
            Assert.That(agent.Blackboard, Is.Not.Null, agent.LastError);
            var goal = new Vector3(5f, start.position.y, -5f);
            agent.Blackboard.Set("goal", goal);

            NodeStatus? result = null;
            agent.Completed += status => result = status;
            var minZ = go.transform.position.z;
            var sawConfiguredSpeed = false;
            var deadline = Time.realtimeSinceStartup + 15f;
            while (result == null && Time.realtimeSinceStartup < deadline)
            {
                minZ = Mathf.Min(minZ, go.transform.position.z);
                sawConfiguredSpeed |= Mathf.Approximately(navigator.speed, 6f);
                yield return null;
            }

            Assert.That(agent.HasFaulted, Is.False, agent.LastError);
            Assert.That(result, Is.EqualTo(NodeStatus.Success), "nav-move-to then nav-stop finish within 15 s.");
            var offset = go.transform.position - goal;
            offset.y = 0f;
            Assert.That(offset.magnitude, Is.LessThanOrEqualTo(0.6f), "The agent stops within stop-distance of the goal.");
            Assert.That(minZ, Is.LessThan(-7f), "The path went around the wall's end instead of through it.");
            Assert.That(sawConfiguredSpeed, Is.True, "The node's speed parameter applied while moving.");
            Assert.That(navigator.speed, Is.EqualTo(ownSpeed), "Exit restores the NavMeshAgent's own speed.");
            Assert.That(navigator.hasPath, Is.False, "nav-stop clears the path.");
        }
    }
}
#endif
