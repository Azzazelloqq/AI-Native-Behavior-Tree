#if AIBT_UNITY_AI
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;
using static AIBT.Tests.Editor.Agents.StandardNodeKit;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P9-002: the optional NavMeshAgent nodes on a NavMesh built in memory for the test. Edit Mode
    /// does not place a NavMeshAgent on a NavMesh, so these cover the failure and stop paths; path
    /// following and arrival are checked in Play Mode (Planning~/Evidence/P9-002).
    /// </summary>
    public sealed class StandardNavigationNodeTests
    {
        private SceneAgent _scene;
        private NavMeshDataInstance _navMesh;

        [SetUp]
        public void SetUp()
        {
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = new Vector3(20f, 0.1f, 20f),
                    transform = Matrix4x4.identity,
                    area = 0,
                },
            };
            var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(Vector3.zero, new Vector3(40f, 10f, 40f)), Vector3.zero, Quaternion.identity);
            _navMesh = NavMesh.AddNavMeshData(data);
            _scene = new SceneAgent();
        }

        [TearDown]
        public void TearDown()
        {
            _scene.Dispose();
            _navMesh.Remove();
        }

        [Test]
        public void TheTestNavMeshIsBuiltButEditModeDoesNotPlaceANavMeshAgentOnIt()
        {
            // Pins the Editor behavior the other tests rely on: the in-memory NavMesh answers queries,
            // while a NavMeshAgent is only placed on it in Play Mode.
            Assert.That(NavMesh.SamplePosition(Vector3.zero, out _, 2f, NavMesh.AllAreas), Is.True);
            var navigator = _scene.GameObject.AddComponent<NavMeshAgent>();
            Assert.That(navigator.isOnNavMesh, Is.False);
        }

        [Test]
        public void NavMoveToFailsWithAWarningWhileTheAgentIsNotOnANavMesh()
        {
            var navigator = _scene.GameObject.AddComponent<NavMeshAgent>();
            var speed = navigator.speed;
            _scene.Run(Tree("go", Vec3("goal", new Vector3(5f, 0f, 0f)),
                Node("go", "aibt.unity.nav-move-to", "\"target\": \"goal\", \"speed\": 7")));
            LogAssert.Expect(LogType.Warning, new Regex("not on a NavMesh"));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure));
            Assert.That(navigator.speed, Is.EqualTo(speed), "Exit restores the NavMeshAgent's own speed.");
        }

        [Test]
        public void NavNodesFailWithoutANavMeshAgent()
        {
            _scene.Run(Tree("go", Vec3("goal"), Node("go", "aibt.unity.nav-move-to", "\"target\": \"goal\"")));
            LogAssert.Expect(LogType.Warning, new Regex("needs an enabled NavMeshAgent"));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure));

            _scene.Run(Tree("stop", null, Node("stop", "aibt.unity.nav-stop", null)));
            LogAssert.Expect(LogType.Warning, new Regex("needs an enabled NavMeshAgent"));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure));
        }

        [Test]
        public void NavStopClearsThePathAndSucceeds()
        {
            var navigator = _scene.GameObject.AddComponent<NavMeshAgent>();
            _scene.Run(Tree("stop", null, Node("stop", "aibt.unity.nav-stop", null)));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Success));
            Assert.That(navigator.hasPath, Is.False);
        }
    }
}
#endif
