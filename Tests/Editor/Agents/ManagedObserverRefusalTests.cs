using System;
using System.Linq;
using AIBT.Agents;
using AIBT.Authoring;
using AIBT.Editor.Registry;
using NUnit.Framework;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P11-008 (review F10): a <c>BehaviorTreeAgent</c> does not run observer re-evaluation. A tree
    /// whose condition has an <c>observer</c> block used to load, then stop the agent with AIBT4002
    /// ("No explicit observer condition evaluator is bound") the first time a watched key changed. It is
    /// now refused when the tree is loaded (and in the Editor's managed-policy validation) with
    /// AIBT2051, naming the node. The same tree without the block still loads and runs.
    /// </summary>
    public sealed class ManagedObserverRefusalTests
    {
        private const string Alarm = "\"alarm\": { \"type\": \"Bool\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": false }";

        private static readonly Type[] FixtureNodes = { typeof(AlarmIsSetNode), typeof(HoldNode) };

        private static string GuardTree(bool withObserver)
            => Tree("root", Alarm,
                Node("root", "aibt.core.reactive-sequence", null, "guard", "hold"),
                withObserver
                    ? "\"guard\": { \"type\": \"tests.p11008.alarm-is-set\", \"typeVersion\": 1, \"parameters\": { \"flag\": \"alarm\" }, "
                      + "\"observer\": { \"mode\": \"self\", \"watchedKeys\": [\"alarm\"] } }"
                    : Node("guard", "tests.p11008.alarm-is-set", "\"flag\": \"alarm\""),
                Node("hold", "tests.p11008.hold", null));

        [Test]
        public void ATreeWithAnObserverBlock_IsRefusedWhenItIsLoaded_NamingTheNode()
        {
            var nodes = ManagedNodeSet.Create(FixtureNodes);
            Assert.That(nodes.Problems, Is.Empty, string.Join("\n", nodes.Problems));

            var loaded = ManagedTreeProgram.TryCompile(GuardTree(true), "trees/p11008-observer.aibt", nodes, out var program, out var diagnostics);

            Assert.That(loaded, Is.False, "A managed agent cannot run the observer, so the tree must not load.");
            Assert.That(program, Is.Null);
            var refusal = diagnostics.Where(value => value.Code.Value == "AIBT2051").ToArray();
            Assert.That(refusal.Length, Is.EqualTo(1), ManagedTreeProgram.Describe(diagnostics));
            Assert.That(refusal[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(refusal[0].Location.NodeId.Value, Is.EqualTo("guard"));
            Assert.That(refusal[0].Location.JsonPointer, Is.EqualTo("/nodes/guard/observer"));
            Assert.That(refusal[0].Message, Does.Contain("'guard'").And.Contain("BehaviorTreeAgent").And.Contain("reactive"));
        }

        [Test]
        public void TheSameTreeWithoutTheBlock_LoadsAndRuns()
        {
            var nodes = ManagedNodeSet.Create(FixtureNodes);
            Assert.That(ManagedTreeProgram.TryCompile(GuardTree(false), "trees/p11008-guard.aibt", nodes, out var program, out var diagnostics),
                Is.True, ManagedTreeProgram.Describe(diagnostics));
            var instance = new ManagedTreeInstance(program, null);
            instance.Blackboard.Set("alarm", true);
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Running));
            instance.Blackboard.Set("alarm", false);
            Assert.That(instance.Tick(0f), Is.EqualTo(ManagedTickOutcome.Completed), "the reactive guard fails the branch at the next tick");
            Assert.That(instance.LastResult, Is.EqualTo(NodeStatus.Failure));
        }

        [Test]
        public void EditorValidationOfAManagedTree_ReportsTheSameRefusal()
        {
            var catalog = ProjectNodeCatalog.Build(Array.Empty<Type>(), Array.Empty<IReferenceLeafBehaviorProvider>(), null,
                ManagedNodeSet.Create(FixtureNodes));
            var parsed = CanonicalTreeJson.Parse(GuardTree(true), "trees/p11008-observer.aibt");
            Assert.That(parsed.Success, Is.True);
            var options = new ReferenceCompilerOptions("trees/p11008-observer.aibt", ReferenceCompilationPolicy.Phase1,
                new CompiledCompilerVersion(1, 0, 0, 7));

            var diagnostics = catalog.ValidateAuthoring(parsed.Document, options);

            Assert.That(diagnostics.Select(value => value.Code.Value), Does.Contain("AIBT2051"),
                string.Join("\n", diagnostics.Select(value => value.Code.Value + ": " + value.Message)));
        }

        [BtNode("tests.p11008.alarm-is-set")]
        private sealed class AlarmIsSetNode : BtCondition
        {
            public BbKey<bool> flag;

            protected override bool Check() => Get(flag);
        }

        [BtNode("tests.p11008.hold")]
        private sealed class HoldNode : BtAction
        {
            protected override NodeStatus OnTick() => NodeStatus.Running;
        }
    }
}
