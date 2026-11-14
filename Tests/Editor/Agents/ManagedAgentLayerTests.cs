using System;
using System.Collections.Generic;
using AIBT.Agents;
using AIBT.Authoring;
using NUnit.Framework;
using UnityEngine;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P9-001: a tree with ordinary C# node classes runs on the reference executor, receives its
    /// authored parameters, and exchanges blackboard values with gameplay code by key name.
    /// </summary>
    public sealed class ManagedAgentLayerTests
    {
        private static readonly List<string> Log = new List<string>();

        private static readonly Type[] FixtureNodes =
        {
            typeof(CountNode), typeof(RecordNode), typeof(HoldNode), typeof(FlagIsSetNode),
            typeof(AddNode), typeof(ThrowNode), typeof(ParameterProbeNode),
        };

        private ManagedNodeSet _nodes;

        [SetUp]
        public void SetUp()
        {
            Log.Clear();
            _nodes = ManagedNodeSet.Create(FixtureNodes);
            Assert.That(_nodes.Problems, Is.Empty, string.Join("\n", _nodes.Problems));
        }

        [Test]
        public void ASequenceOfManagedActionsRunsAcrossTicksAndCompletes()
        {
            var instance = Start(Tree("root", null,
                Node("root", "aibt.core.memory-sequence", null, "wait", "done"),
                Node("wait", "test.count", "\"ticks\": 3"),
                Node("done", "test.record", "\"text\": \"done\"")));

            Assert.That(instance.Tick(0.1f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(instance.Tick(0.1f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(instance.Tick(0.1f), Is.EqualTo(ManagedTickOutcome.Completed));
            Assert.That(instance.LastResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(Log, Is.EqualTo(new[] { "enter wait", "exit wait", "done" }));
            Assert.That(instance.Tick(0.1f), Is.EqualTo(ManagedTickOutcome.Idle), "A finished tree is not ticked again until it is restarted.");
        }

        [Test]
        public void AuthoredParametersReachTheNodeAndOmittedOnesKeepTheirFieldInitializer()
        {
            var instance = Start(Tree("probe", null,
                Node("probe", "test.parameter-probe",
                    "\"count\": -4, \"ratio\": 0.25, \"label\": \"hello\", \"mode\": \"second-choice\", \"enabled\": false")));
            instance.Tick(0f);
            Assert.That(Log, Is.EqualTo(new[] { "-4|0.25|hello|SecondChoice|False|7" }),
                "'untouched' is omitted by the tree and must keep its initializer value 7.");
        }

        [Test]
        public void ABlackboardKeyParameterReadsAndWritesTheKeyChosenByTheTree()
        {
            var instance = Start(Tree("add", "\"score\": { \"type\": \"Int32\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": 10 }",
                Node("add", "test.add", "\"target\": \"score\", \"amount\": 5")));
            Assert.That(instance.Blackboard.Get<int>("score"), Is.EqualTo(10), "The declared default is visible before the first tick.");
            instance.Tick(0f);
            Assert.That(instance.Blackboard.Get<int>("score"), Is.EqualTo(15));
        }

        [Test]
        public void AnInterruptingConditionMakesAMemorySelectorChooseAgainAsSoonAsItsAnswerChanges()
        {
            var instance = Start(PriorityTree("both"));

            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(Log, Is.EqualTo(new[] { "enter patrol" }),
                "While the condition's answer is unchanged the running branch keeps running; it is not restarted.");
            Assert.That(instance.WatchedConditionCount, Is.EqualTo(1));

            // An external write between ticks: the lower-priority branch is interrupted on the next tick.
            instance.Blackboard.Set("alarm", true);
            Log.Clear();
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(Log, Is.EqualTo(new[] { "abort patrol", "exit patrol", "enter chase" }));

            Log.Clear();
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(Log, Is.Empty, "The chosen branch runs undisturbed while the condition still holds.");

            // The condition stops holding: its own branch is aborted and the selector falls through.
            instance.Blackboard.Set("alarm", false);
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(Log, Is.EqualTo(new[] { "abort chase", "exit chase", "enter patrol" }));
        }

        [Test]
        public void WithoutAnInterruptModeAMemorySelectorKeepsItsRunningChild()
        {
            var instance = Start(PriorityTree("none"));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            instance.Blackboard.Set("alarm", true);
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(Log, Is.EqualTo(new[] { "enter patrol" }));
            Assert.That(instance.WatchedConditionCount, Is.EqualTo(0));
        }

        [Test]
        public void ASelfInterruptDoesNotPreemptALowerPriorityBranchAndALowerPriorityInterruptDoesNotStopItsOwn()
        {
            var selfOnly = Start(PriorityTree("self"));
            selfOnly.Tick(0f);
            selfOnly.Blackboard.Set("alarm", true);
            selfOnly.Tick(0f);
            Assert.That(Log, Is.EqualTo(new[] { "enter patrol" }), "'self' only guards the branch the condition allowed.");

            Log.Clear();
            var lowerOnly = Start(PriorityTree("lower-priority"));
            lowerOnly.Tick(0f);
            lowerOnly.Blackboard.Set("alarm", true);
            lowerOnly.Tick(0f);
            Assert.That(Log, Is.EqualTo(new[] { "enter patrol", "abort patrol", "exit patrol", "enter chase" }));
            lowerOnly.Blackboard.Set("alarm", false);
            Log.Clear();
            lowerOnly.Tick(0f);
            Assert.That(Log, Is.Empty, "'lower-priority' does not stop the condition's own branch once it runs.");
        }

        [Test]
        public void AnInterruptRestartsOnlyTheOwningCompositeNotTheStepsBeforeIt()
        {
            var instance = Start(Tree("plan", Alarm,
                Node("plan", "aibt.core.memory-sequence", null, "prepare", "priority"),
                Node("prepare", "test.record", "\"text\": \"prepare\""),
                Node("priority", "aibt.core.memory-selector", null, "respond", "patrol"),
                Node("respond", "aibt.core.memory-sequence", null, "alarmed", "chase"),
                Node("alarmed", "test.flag-is-set", "\"flag\": \"alarm\", \"interrupt\": \"both\""),
                Node("chase", "test.hold", "\"name\": \"chase\""),
                Node("patrol", "test.hold", "\"name\": \"patrol\"")));
            instance.Tick(0f);
            instance.Blackboard.Set("alarm", true);
            instance.Tick(0f);
            Assert.That(Log, Is.EqualTo(new[] { "prepare", "enter patrol", "abort patrol", "exit patrol", "enter chase" }),
                "The step before the selector already finished and must not run again.");
        }

        [Test]
        public void ALowerPriorityConditionThatChangesDoesNotDisturbAHigherPriorityRunningBranch()
        {
            var instance = Start(Tree("priority",
                Alarm + ", \"hungry\": { \"type\": \"Bool\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": false }",
                Node("priority", "aibt.core.memory-selector", null, "respond", "eat", "patrol"),
                Node("respond", "aibt.core.memory-sequence", null, "alarmed", "chase"),
                Node("alarmed", "test.flag-is-set", "\"flag\": \"alarm\", \"interrupt\": \"both\""),
                Node("chase", "test.hold", "\"name\": \"chase\""),
                Node("eat", "aibt.core.memory-sequence", null, "is-hungry", "feed"),
                Node("is-hungry", "test.flag-is-set", "\"flag\": \"hungry\", \"interrupt\": \"both\""),
                Node("feed", "test.hold", "\"name\": \"feed\""),
                Node("patrol", "test.hold", "\"name\": \"patrol\"")));
            instance.Tick(0f);
            instance.Blackboard.Set("alarm", true);
            instance.Tick(0f);
            Log.Clear();

            instance.Blackboard.Set("hungry", true);
            instance.Tick(0f);
            instance.Tick(0f);
            Assert.That(Log, Is.Empty, "A lower-priority condition must not restart the higher-priority branch that is running.");

            instance.Blackboard.Set("alarm", false);
            instance.Tick(0f);
            Assert.That(Log, Is.EqualTo(new[] { "abort chase", "exit chase", "enter feed" }));
        }

        [Test]
        public void AMemorySequenceUnderAnInterruptibleSelectorStillAdvancesAcrossTicks()
        {
            var instance = Start(Tree("priority", Alarm,
                Node("priority", "aibt.core.memory-selector", null, "respond", "routine"),
                Node("respond", "aibt.core.memory-sequence", null, "alarmed", "chase"),
                Node("alarmed", "test.flag-is-set", "\"flag\": \"alarm\", \"interrupt\": \"both\""),
                Node("chase", "test.hold", "\"name\": \"chase\""),
                Node("routine", "aibt.core.memory-sequence", null, "wait", "done"),
                Node("wait", "test.count", "\"ticks\": 3"),
                Node("done", "test.record", "\"text\": \"done\"")));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Completed));
            Assert.That(Log, Is.EqualTo(new[] { "enter wait", "exit wait", "done" }));
        }

        [Test]
        public void AReactiveSelectorRestartsItsRunningChildOnEveryUpdateAsItsSpecificationRequires()
        {
            // execution-semantics-v1.md: a reactive composite aborts and exits its running child
            // before reevaluating on every update. Stateful actions therefore belong under a
            // memory composite with an interrupting condition, which the tests above cover.
            var instance = Start(Tree("priority", Alarm,
                Node("priority", "aibt.core.reactive-selector", null, "respond", "patrol"),
                Node("respond", "aibt.core.reactive-sequence", null, "alarmed", "chase"),
                Node("alarmed", "test.flag-is-set", "\"flag\": \"alarm\""),
                Node("chase", "test.hold", "\"name\": \"chase\""),
                Node("patrol", "test.hold", "\"name\": \"patrol\"")));
            instance.Tick(0f);
            instance.Tick(0f);
            Assert.That(Log, Is.EqualTo(new[] { "enter patrol", "abort patrol", "exit patrol", "enter patrol" }));
            instance.Blackboard.Set("alarm", true);
            Log.Clear();
            instance.Tick(0f);
            Assert.That(Log, Is.EqualTo(new[] { "abort patrol", "exit patrol", "enter chase" }));
        }

        [Test]
        public void BlackboardAccessIsTypeCheckedAndReportsUnknownKeys()
        {
            var instance = Start(Tree("idle", "\"speed\": { \"type\": \"Float32\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": 1.5 },"
                + "\"home\": { \"type\": \"Float3\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": { \"x\": 1.0, \"y\": 2.0, \"z\": 3.0 } }",
                Node("idle", "test.hold", "\"name\": \"idle\"")));
            var blackboard = instance.Blackboard;
            Assert.That(blackboard.Keys, Is.EquivalentTo(new[] { "speed", "home" }));
            Assert.That(blackboard.Get<float>("speed"), Is.EqualTo(1.5f));
            Assert.That(blackboard.Get<Vector3>("home"), Is.EqualTo(new Vector3(1f, 2f, 3f)));
            blackboard.Set("home", new Vector3(4f, 5f, 6f));
            Assert.That(blackboard.Get<Vector3>("home"), Is.EqualTo(new Vector3(4f, 5f, 6f)));

            Assert.That(blackboard.TrySet("speed", 3), Is.False, "An int must not be written into a Float32 key.");
            Assert.That(() => blackboard.Set("speed", 3), Throws.InvalidOperationException.With.Message.Contains("Float32"));
            Assert.That(() => blackboard.Get<float>("missing"), Throws.InstanceOf<KeyNotFoundException>());
            Assert.That(blackboard.TryGet<float>("missing", out _), Is.False);
            Assert.That(blackboard.GetOrDefault("missing", 9f), Is.EqualTo(9f));
        }

        [Test]
        public void RestartKeepsBlackboardValuesUnlessAskedToResetThem()
        {
            var instance = Start(Tree("add", "\"score\": { \"type\": \"Int32\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": 0 }",
                Node("add", "test.add", "\"target\": \"score\", \"amount\": 2")));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Completed));
            Assert.That(instance.Restart(false), Is.True);
            Assert.That(instance.Blackboard.Get<int>("score"), Is.EqualTo(2), "A restart keeps the agent's memory by default.");
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Completed));
            Assert.That(instance.Blackboard.Get<int>("score"), Is.EqualTo(4));

            Assert.That(instance.Restart(true), Is.True);
            Assert.That(instance.Blackboard.Get<int>("score"), Is.EqualTo(0));
        }

        [Test]
        public void RestartingARunningTreeAbortsItsActiveNodeFirst()
        {
            var instance = Start(Tree("hold", null, Node("hold", "test.hold", "\"name\": \"hold\"")));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(instance.Restart(false), Is.True);
            Assert.That(Log, Is.EqualTo(new[] { "enter hold", "abort hold", "exit hold" }));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            Assert.That(Log[Log.Count - 1], Is.EqualTo("enter hold"));
        }

        [Test]
        public void ANodeExceptionStopsTheTreeAndKeepsTheOriginalExceptionAndNodeId()
        {
            var instance = Start(Tree("boom", null, Node("boom", "test.throw", null)));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Faulted));
            Assert.That(instance.IsFaulted, Is.True);
            Assert.That(instance.FaultNodeId, Is.EqualTo("boom"));
            Assert.That(instance.FaultException, Is.InstanceOf<InvalidOperationException>());
            Assert.That(instance.FaultException.Message, Is.EqualTo("node failure"));
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Idle));
        }

        [Test]
        public void ATreeNamingAnUndeclaredOrWronglyTypedKeyDoesNotCompile()
        {
            var undeclared = Tree("add", null, Node("add", "test.add", "\"target\": \"score\", \"amount\": 1"));
            Assert.That(ManagedTreeProgram.TryCompile(undeclared, "trees/test.aibt", _nodes, out _, out var diagnostics), Is.False);
            Assert.That(ManagedTreeProgram.Describe(diagnostics), Does.Contain("does not declare"));

            var wrongType = Tree("add", "\"score\": { \"type\": \"Float32\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": 0.0 }",
                Node("add", "test.add", "\"target\": \"score\", \"amount\": 1"));
            Assert.That(ManagedTreeProgram.TryCompile(wrongType, "trees/test.aibt", _nodes, out _, out diagnostics), Is.False);
            Assert.That(ManagedTreeProgram.Describe(diagnostics), Does.Contain("requires Int32"));

            var missingRequired = Tree("add", null, Node("add", "test.add", "\"amount\": 1"));
            Assert.That(ManagedTreeProgram.TryCompile(missingRequired, "trees/test.aibt", _nodes, out _, out diagnostics), Is.False);
        }

        [Test]
        public void AManagedManifestDeclaresUnpackedParametersWithDefaultsAndKeyTypes()
        {
            Assert.That(_nodes.TryGet("test.add", out var add), Is.True);
            var manifest = add.Manifest;
            Assert.That(manifest.ExecutionDomain, Is.EqualTo(NodeExecutionDomain.MainThread));
            Assert.That(manifest.Kind, Is.EqualTo(NodeBehaviorKind.Action));
            Assert.That(manifest.Configuration.Fields, Is.Empty);

            var target = Find(manifest, "target");
            Assert.That(target.Type, Is.EqualTo(NodeParameterType.BlackboardKey));
            Assert.That(target.Required, Is.True);
            Assert.That(target.BlackboardValueType, Is.EqualTo("Int32"));
            var amount = Find(manifest, "amount");
            Assert.That(amount.Type, Is.EqualTo(NodeParameterType.Int32));
            Assert.That(amount.Required, Is.False);
            Assert.That(amount.DefaultValue.TryGetInt64(out var defaultAmount) && defaultAmount == 1, Is.True);

            var json = NodeManifestCanonicalJson.ToJson(manifest).ToString(Newtonsoft.Json.Formatting.None);
            Assert.That(json, Does.Contain("\"blackboard-key\""));
            Assert.That(json, Does.Contain("\"valueType\":\"Int32\""));
            Assert.That(json, Does.Not.Contain("\"packing\""), "Managed-only parameters are never packed into configuration.");

            Assert.That(_nodes.TryGet("test.flag-is-set", out var condition), Is.True);
            Assert.That(condition.Manifest.Kind, Is.EqualTo(NodeBehaviorKind.Condition));
        }

        [Test]
        public void ABurstManifestStillRequiresPackingAndRejectsManagedOnlyParameterTypes()
        {
            Assert.That(() => Manifest(NodeExecutionDomain.Burst, new NodeParameterContract("label", NodeParameterType.String, false),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>())), Throws.ArgumentException);
            Assert.That(() => Manifest(NodeExecutionDomain.Burst, new NodeParameterContract("ticks", NodeParameterType.UInt32, true),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>())), Throws.ArgumentException,
                "A Burst parameter without a packing field stays invalid.");
            Assert.That(() => Manifest(NodeExecutionDomain.MainThread, new NodeParameterContract("label", NodeParameterType.String, false),
                new NodeConfigurationDescriptor(4, 4, new[] { new NodeConfigurationField("label", 0, 4, 4) })), Throws.ArgumentException,
                "A managed-only parameter cannot declare packing.");
            Assert.That(() => Manifest(NodeExecutionDomain.MainThread, new NodeParameterContract("ticks", NodeParameterType.UInt32, true),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>())), Throws.Nothing);
        }

        [Test]
        public void TheProjectCatalogContainsTheStandardNodesAndNoTestFixtures()
        {
            ManagedNodeCatalog.Refresh();
            var catalog = ManagedNodeCatalog.Default;
            Assert.That(catalog.Problems, Is.Empty, string.Join("\n", catalog.Problems));
            Assert.That(catalog.TryGet("aibt.unity.log", out _), Is.True);
            Assert.That(catalog.TryGet("aibt.unity.move-towards", out var move), Is.True);
            Assert.That(Find(move.Manifest, "stop-distance").Type, Is.EqualTo(NodeParameterType.Float32));
            Assert.That(catalog.TryGet("test.add", out _), Is.False, "Node classes in a test assembly are fixtures, not project nodes.");
            Assert.That(catalog.Registry.TryGet("aibt.core.memory-sequence", out _), Is.True);
        }

        [TestCase("stopDistance", "stop-distance")]
        [TestCase("Speed", "speed")]
        [TestCase("_targetID", "target-id")]
        [TestCase("HTTPServerName", "http-server-name")]
        [TestCase("degreesPerSecond", "degrees-per-second")]
        public void FieldNamesMapToKebabCaseParameterNames(string field, string expected)
        {
            Assert.That(ManagedNodeType.ToKebabCase(field), Is.EqualTo(expected));
        }

        [Test]
        public void AnInvalidNodeClassIsReportedInsteadOfBreakingTheCatalog()
        {
            var set = ManagedNodeSet.Create(new[] { typeof(CountNode), typeof(UnsupportedFieldNode), typeof(BadIdNode) });
            Assert.That(set.TryGet("test.count", out _), Is.True);
            Assert.That(set.Types.Count, Is.EqualTo(1));
            Assert.That(set.Problems.Count, Is.EqualTo(2));
            Assert.That(string.Join("\n", set.Problems), Does.Contain("not a supported parameter type"));
        }

        private ManagedTreeInstance Start(string json)
        {
            Assert.That(ManagedTreeProgram.TryCompile(json, "trees/test.aibt", _nodes, out var program, out var diagnostics), Is.True,
                ManagedTreeProgram.Describe(diagnostics));
            return new ManagedTreeInstance(program, null);
        }

        private static NodeParameterContract Find(NodeManifest manifest, string name)
        {
            for (var index = 0; index < manifest.Parameters.Count; index++)
            {
                if (manifest.Parameters[index].Name == name) return manifest.Parameters[index];
            }

            Assert.Fail("Parameter '" + name + "' is not declared by " + manifest.TypeId + ".");
            return null;
        }

        private static NodeManifest Manifest(NodeExecutionDomain domain, NodeParameterContract parameter, NodeConfigurationDescriptor configuration)
        {
            return new NodeManifest("test.manifest-rules", 1, "Manifest rules", "Tests", NodeBehaviorKind.Action, "", "", domain,
                domain == NodeExecutionDomain.Burst, new[] { parameter }, new NodeChildPolicy(0, 0, true), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<string>(), new[] { NodeStatus.Success }, new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                configuration, NodeCancellationMode.AbortOnly, NodeCostHint.Trivial, new[] { new NodeManifestExample("example", "{}", "Succeeds.") });
        }

        private const string Alarm = "\"alarm\": { \"type\": \"Bool\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": false }";

        private static string PriorityTree(string interrupt)
        {
            return Tree("priority", Alarm,
                Node("priority", "aibt.core.memory-selector", null, "respond", "patrol"),
                Node("respond", "aibt.core.memory-sequence", null, "alarmed", "chase"),
                Node("alarmed", "test.flag-is-set", "\"flag\": \"alarm\", \"interrupt\": \"" + interrupt + "\""),
                Node("chase", "test.hold", "\"name\": \"chase\""),
                Node("patrol", "test.hold", "\"name\": \"patrol\""));
        }

        [BtNode("test.count")]
        private sealed class CountNode : BtAction
        {
            public int ticks = 1;
            private int _elapsed;

            protected override void OnEnter()
            {
                _elapsed = 0;
                Log.Add("enter " + NodeId);
            }

            protected override NodeStatus OnTick() => ++_elapsed >= ticks ? NodeStatus.Success : NodeStatus.Running;

            protected override void OnExit() => Log.Add("exit " + NodeId);
        }

        [BtNode("test.record")]
        private sealed class RecordNode : BtAction
        {
            public string text = "";

            protected override NodeStatus OnTick()
            {
                Log.Add(text);
                return NodeStatus.Success;
            }
        }

        [BtNode("test.hold")]
        private sealed class HoldNode : BtAction
        {
            public string name = "";

            protected override void OnEnter() => Log.Add("enter " + name);

            protected override NodeStatus OnTick() => NodeStatus.Running;

            protected override void OnAbort() => Log.Add("abort " + name);

            protected override void OnExit() => Log.Add("exit " + name);
        }

        [BtNode("test.flag-is-set")]
        private sealed class FlagIsSetNode : BtCondition
        {
            public BbKey<bool> flag;

            protected override bool Check() => Get(flag);
        }

        [BtNode("test.add")]
        private sealed class AddNode : BtAction
        {
            public BbKey<int> target;
            public int amount = 1;

            protected override NodeStatus OnTick()
            {
                Set(target, Get(target) + amount);
                return NodeStatus.Success;
            }
        }

        [BtNode("test.throw")]
        private sealed class ThrowNode : BtAction
        {
            protected override NodeStatus OnTick() => throw new InvalidOperationException("node failure");
        }

        private enum ProbeMode
        {
            FirstChoice,
            SecondChoice,
        }

        [BtNode("test.parameter-probe")]
        private sealed class ParameterProbeNode : BtAction
        {
            public int count = 1;
            public float ratio = 1f;
            public string label = "none";
            public ProbeMode mode = ProbeMode.FirstChoice;
            public bool enabled = true;
            public int untouched = 7;

            protected override NodeStatus OnTick()
            {
                Log.Add(count + "|" + ratio.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + label + "|" + mode + "|" + enabled + "|" + untouched);
                return NodeStatus.Success;
            }
        }

        [BtNode("test.unsupported-field")]
        private sealed class UnsupportedFieldNode : BtAction
        {
            public GameObject target;

            protected override NodeStatus OnTick() => NodeStatus.Success;
        }

        [BtNode("Not A Valid Id")]
        private sealed class BadIdNode : BtAction
        {
            protected override NodeStatus OnTick() => NodeStatus.Success;
        }
    }
}
