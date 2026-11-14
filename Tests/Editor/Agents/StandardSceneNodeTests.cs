using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;
using static AIBT.Tests.Editor.Agents.StandardNodeKit;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P9-002: the standard nodes that act on the scene, each run by a real BehaviorTreeAgent on a
    /// real GameObject in Edit Mode with manual ticks.
    /// </summary>
    public sealed class StandardSceneNodeTests
    {
        private SceneAgent _scene;

        [SetUp]
        public void SetUp() => _scene = new SceneAgent();

        [TearDown]
        public void TearDown() => _scene.Dispose();

        // ---- movement ------------------------------------------------------------------------

        [Test]
        public void RotateTowardsTurnsAtItsSpeedUntilItFacesTheDirection()
        {
            _scene.Run(Tree("turn", Vec3("facing", Vector3.right),
                Node("turn", "aibt.unity.rotate-towards", "\"direction\": \"facing\", \"degrees-per-second\": 90")));
            Assert.That(_scene.Tick(0.5f), Is.EqualTo(NodeStatus.Running));
            Assert.That(_scene.Transform.eulerAngles.y, Is.EqualTo(45f).Within(0.1f));
            Assert.That(_scene.TickToEnd(0.5f), Is.EqualTo(NodeStatus.Success));
            Assert.That(Vector3.Angle(_scene.Transform.forward, Vector3.right), Is.LessThan(0.6f));
        }

        [Test]
        public void TurnByTurnsExactlyTheAngleEitherWay()
        {
            _scene.Run(Tree("turn", null, Node("turn", "aibt.unity.turn-by", "\"degrees\": -90, \"degrees-per-second\": 100")));
            Assert.That(_scene.Tick(0.5f), Is.EqualTo(NodeStatus.Running));
            Assert.That(_scene.TickToEnd(0.5f), Is.EqualTo(NodeStatus.Success));
            Assert.That(Vector3.Angle(_scene.Transform.forward, Vector3.left), Is.LessThan(0.01f));
        }

        [Test]
        public void MoveDirectionMovesAlongTheKeyOrItsForwardForTheDuration()
        {
            _scene.Run(Tree("dash", Vec3("dir", new Vector3(0, 0, 2)),
                Node("dash", "aibt.unity.move-direction", "\"direction\": \"dir\", \"speed\": 4, \"duration\": 0.5")));
            Assert.That(_scene.Tick(0.25f), Is.EqualTo(NodeStatus.Running));
            Assert.That(_scene.Tick(0.25f), Is.EqualTo(NodeStatus.Success));
            Assert.That(_scene.Transform.position, Is.EqualTo(new Vector3(0, 0, 2)), "4 units/s for 0.5 s along the normalized key.");

            _scene.Transform.position = Vector3.zero;
            _scene.Transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            _scene.Run(Tree("drift", null, Node("drift", "aibt.unity.move-direction", "\"speed\": 1, \"duration\": 0")));
            for (var tick = 0; tick < 10; tick++) Assert.That(_scene.Tick(0.5f), Is.EqualTo(NodeStatus.Running), "Duration 0 never finishes.");
            Assert.That(Vector3.Distance(_scene.Transform.position, new Vector3(5f, 0f, 0f)), Is.LessThan(0.001f), "Unbound direction is the agent's forward.");
        }

        [Test]
        public void KeepDistanceBacksAwayAndClosesInUntilInsideTheBand()
        {
            _scene.Run(Tree("keep", Vec3("enemy", new Vector3(0, 0, 1)),
                Node("keep", "aibt.unity.keep-distance", "\"target\": \"enemy\", \"min-distance\": 3, \"max-distance\": 6, \"speed\": 1")));
            Assert.That(_scene.Tick(1f), Is.EqualTo(NodeStatus.Running));
            Assert.That(_scene.Transform.position.z, Is.EqualTo(-1f).Within(0.0001f), "Too close: it backs away.");
            Assert.That(_scene.TickToEnd(1f), Is.EqualTo(NodeStatus.Success));
            Assert.That(Vector3.Distance(_scene.Transform.position, new Vector3(0, 0, 1)), Is.EqualTo(3f).Within(0.0001f));
            Assert.That(Vector3.Angle(_scene.Transform.forward, Vector3.forward), Is.LessThan(0.01f), "It keeps facing the target.");

            _scene.Agent.Blackboard.Set("enemy", new Vector3(0, 0, 20));
            _scene.Agent.Restart();
            Assert.That(_scene.TickToEnd(1f), Is.EqualTo(NodeStatus.Success));
            Assert.That(Vector3.Distance(_scene.Transform.position, new Vector3(0, 0, 20)), Is.EqualTo(6f).Within(0.0001f), "Too far: it closes in to the maximum.");
        }

        // ---- animation -----------------------------------------------------------------------

        [Test]
        public void AnimatorNodesSetParametersOnTheAnimatorOfTheAgentOrItsChildren()
        {
            var animator = _scene.Child("model").AddComponent<Animator>();
            var controller = _scene.Track(new AnimatorController());
            controller.AddLayer("Base");
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            animator.runtimeAnimatorController = controller;
            animator.Update(0f);

            _scene.Run(Tree("animate", Float("speed", 3.5f),
                Node("animate", "aibt.core.memory-sequence", null, "attack", "moving", "speed"),
                Node("attack", "aibt.unity.animator-trigger", "\"parameter\": \"Attack\""),
                Node("moving", "aibt.unity.animator-bool", "\"parameter\": \"Moving\", \"value\": true"),
                Node("speed", "aibt.unity.animator-float", "\"parameter\": \"Speed\", \"from-key\": \"speed\"")));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Success));
            Assert.That(animator.GetBool("Attack"), Is.True, "The trigger is set until the Animator consumes it.");
            Assert.That(animator.GetBool("Moving"), Is.True);
            Assert.That(animator.GetFloat("Speed"), Is.EqualTo(3.5f));
        }

        [Test]
        public void AnAnimatorNodeWithoutAnAnimatorFailsAndWarnsOnce()
        {
            _scene.Run(Tree("attack", null, Node("attack", "aibt.unity.animator-trigger", "\"parameter\": \"Attack\"")));
            LogAssert.Expect(LogType.Warning, new Regex("found no Animator on 'the agent'"));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure));
            _scene.Agent.Restart();
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- GameObject and references --------------------------------------------------------

        [Test]
        public void SetActiveTogglesTheReferencedObject()
        {
            var marker = _scene.Child("alert");
            _scene.Agent.SetReference("alert", marker);
            _scene.Run(Tree("hide", null, Node("hide", "aibt.unity.set-active", "\"reference\": \"alert\", \"active\": false")));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Success));
            Assert.That(marker.activeSelf, Is.False);
        }

        [Test]
        public void SetActiveFailsForAMissingReference()
        {
            _scene.Run(Tree("hide", null, Node("hide", "aibt.unity.set-active", "\"reference\": \"nothing\"")));
            LogAssert.Expect(LogType.Warning, new Regex("reference 'nothing' is missing"));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure));
        }

        [Test]
        public void DestroySelfDestroysTheAgentsGameObject()
        {
            _scene.Run(Tree("die", null, Node("die", "aibt.unity.destroy-self", null)));
            _scene.Agent.Tick(0.1f);
            Assert.That(_scene.GameObject == null, Is.True);
        }

        [Test]
        public void GetPositionReadsTheAgentOrANamedReferenceIntoAKey()
        {
            var player = _scene.Other("player", new Vector3(4, 0, 7));
            _scene.Transform.position = new Vector3(1, 2, 3);
            _scene.Agent.SetReference("player", player.transform);
            _scene.Run(Tree("read", Keys(Vec3("self"), Vec3("player")),
                Node("read", "aibt.core.memory-sequence", null, "me", "them"),
                Node("me", "aibt.unity.get-position", "\"result\": \"self\""),
                Node("them", "aibt.unity.get-position", "\"result\": \"player\", \"reference\": \"player\"")));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Success));
            Assert.That(_scene.Blackboard.Get<Vector3>("self"), Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(_scene.Blackboard.Get<Vector3>("player"), Is.EqualTo(new Vector3(4, 0, 7)), "A component reference resolves to its GameObject.");

            Object.DestroyImmediate(player);
            _scene.Agent.Restart();
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure), "A destroyed reference fails instead of throwing.");
        }

        [Test]
        public void HasReferenceRequiresAnAssignedLiveAndByDefaultActiveObject()
        {
            var target = _scene.Other("target", Vector3.zero);
            _scene.Run(Tree("has", null, Node("has", "aibt.unity.has-reference", null)));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure), "Unassigned.");

            _scene.Agent.SetReference("target", target);
            _scene.Agent.Restart();
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Success));

            target.SetActive(false);
            _scene.Agent.Restart();
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure), "Inactive.");

            _scene.Run(Tree("has", null, Node("has", "aibt.unity.has-reference", "\"require-active\": false")));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Success), "Inactive is accepted when require-active is off.");
        }

        // ---- audio and events ----------------------------------------------------------------

        [Test]
        public void PlayAudioStartsTheSourceAndWithoutWaitingSucceedsAtOnce()
        {
            var source = _scene.GameObject.AddComponent<AudioSource>();
            source.clip = _scene.Track(AudioClip.Create("beep", 44100, 1, 44100, false));
            _scene.Run(Tree("bark", null, Node("bark", "aibt.unity.play-audio", null)));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Success));
            Assert.That(source.isPlaying, Is.True);
            source.Stop();
        }

        [Test]
        public void PlayAudioThatWaitsStopsTheSoundWhenInterrupted()
        {
            var source = _scene.GameObject.AddComponent<AudioSource>();
            source.clip = _scene.Track(AudioClip.Create("long", 44100 * 5, 1, 44100, false));
            _scene.Run(Tree("bark", null, Node("bark", "aibt.unity.play-audio", "\"wait-until-finished\": true")));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Running));
            Assert.That(source.isPlaying, Is.True);
            _scene.Agent.Stop();
            Assert.That(source.isPlaying, Is.False, "Abort stops the sound it started.");
        }

        [Test]
        public void PlayAudioWithoutASourceFails()
        {
            _scene.Run(Tree("bark", null, Node("bark", "aibt.unity.play-audio", null)));
            LogAssert.Expect(LogType.Warning, new Regex("found no AudioSource"));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure));
        }

        [Test]
        public void RaiseEventReachesSubscribersInspectorResponsesAndAnyRaised()
        {
            var received = new List<string>();
            _scene.Agent.Events.Subscribe("spotted", agent => received.Add("handler:" + agent.name));
            _scene.Agent.Events.Subscribe("other", agent => received.Add("wrong"));
            var response = new AgentEventResponse { eventName = "spotted" };
            response.response.AddListener(() => received.Add("response"));
            _scene.Agent.AddEventResponse(response);
            _scene.Agent.Events.AnyRaised += (agent, name) => received.Add("any:" + name);

            _scene.Run(Tree("tell", null, Node("tell", "aibt.unity.raise-event", "\"event-name\": \"spotted\"")));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Success));
            Assert.That(received, Is.EqualTo(new[] { "handler:standard-node-agent", "response", "any:spotted" }));
        }

        [Test]
        public void UnsubscribedHandlersAreNoLongerCalled()
        {
            var calls = 0;
            void Handler(BehaviorTreeAgent agent) => calls++;
            _scene.Agent.Events.Subscribe("ping", Handler);
            Assert.That(_scene.Agent.Events.Raise("ping"), Is.EqualTo(1));
            _scene.Agent.Events.Unsubscribe("ping", Handler);
            Assert.That(_scene.Agent.Events.Raise("ping"), Is.EqualTo(0));
            Assert.That(calls, Is.EqualTo(1));
        }

        // ---- line of sight -------------------------------------------------------------------

        [Test]
        public void LineOfSightIsBlockedByOtherCollidersButNotByTheAgentOrTheTarget()
        {
            _scene.GameObject.AddComponent<CapsuleCollider>();
            var player = _scene.Other("player", new Vector3(0, 0, 10));
            player.AddComponent<CapsuleCollider>().center = Vector3.up;
            _scene.Agent.SetReference("player", player);
            _scene.Run(Tree("see", Vec3("player-pos", new Vector3(0, 0, 10)),
                Node("see", "aibt.unity.line-of-sight", "\"target\": \"player-pos\", \"eye-height\": 1, \"target-height\": 1, \"target-reference\": \"player\"")));
            Physics.SyncTransforms();
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Success), "Nothing in between.");

            var wall = _scene.Other("wall", new Vector3(0, 1, 5));
            wall.AddComponent<BoxCollider>().size = new Vector3(4, 4, 0.5f);
            Physics.SyncTransforms();
            _scene.Agent.Restart();
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure), "A wall blocks the ray.");
        }

        [Test]
        public void LineOfSightRespectsRangeAndFieldOfView()
        {
            _scene.Run(Tree("see", Vec3("spot", new Vector3(0, 0, 10)),
                Node("see", "aibt.unity.line-of-sight", "\"target\": \"spot\", \"max-distance\": 8")));
            Physics.SyncTransforms();
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure), "Beyond max-distance.");

            _scene.Run(Tree("see", Vec3("spot", new Vector3(0, 0, -5)),
                Node("see", "aibt.unity.line-of-sight", "\"target\": \"spot\", \"field-of-view\": 120")));
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Failure), "Behind the agent.");
            _scene.Transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            _scene.Agent.Restart();
            Assert.That(_scene.Tick(), Is.EqualTo(NodeStatus.Success), "In front once it turns around.");
        }
    }
}
