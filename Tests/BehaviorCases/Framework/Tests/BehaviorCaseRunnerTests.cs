using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.BehaviorCases
{
    public sealed class BehaviorCaseRunnerTests
    {
        [Test]
        public void FakeBackend_ReceivesControlledInputsAndMatchesOnlyObservableOutputs()
        {
            var json = "{" +
                "\"format\":\"aibt.case\",\"formatVersion\":1,\"name\":\"runner\",\"tree\":\"tree.aibt.json\",\"treeInstanceId\":7,\"rootSeed\":99," +
                "\"initialBlackboard\":{\"speed\":{\"type\":\"Float32\",\"value\":1}}," +
                "\"steps\":[" +
                    "{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":10,\"stepBudget\":3," +
                        "\"events\":[{\"sourceId\":2,\"sourceSequence\":5,\"eventTypeId\":3,\"eventTypeVersion\":1,\"payload\":{\"type\":\"Bool\",\"value\":true}}]," +
                        "\"expect\":{\"progress\":\"completed\",\"rootStatus\":\"success\",\"executedSteps\":3," +
                            "\"blackboard\":[{\"key\":\"speed\",\"value\":{\"type\":\"Float32\",\"value\":1.1},\"version\":2,\"absoluteTolerance\":0.01}]," +
                            "\"commands\":{\"match\":\"ordered-subset\",\"records\":[" + Command(2, "AQ==") + "]}," +
                            "\"trace\":[{\"event\":\"node-entered\",\"traceFormatVersion\":1,\"treeSemanticHash\":\"" + new string('f', 64) + "\",\"treeInstanceId\":7,\"sequence\":1,\"updateId\":1,\"snapshotRevision\":1,\"nodeIndex\":0}],\"diagnostics\":[]," +
                            "\"invariants\":[{\"kind\":\"no-error-diagnostics\"},{\"kind\":\"no-duplicate-command-sequences\"},{\"kind\":\"no-active-operation-leaks\"},{\"kind\":\"terminal-root-has-no-active-nodes\"}]}} ," +
                    "{\"operation\":\"resume\",\"expect\":{}}," +
                    "{\"operation\":\"abort\",\"updateId\":2,\"snapshotRevision\":1,\"timeMicroseconds\":11,\"stepBudget\":4,\"expect\":{}}," +
                    "{\"operation\":\"restart\",\"expect\":{}}]}";
            var observed = new Dictionary<string, BehaviorCaseObservedBlackboardValue>
            {
                ["speed"] = new BehaviorCaseObservedBlackboardValue(
                    BehaviorCaseValue.BuiltIn(BlackboardValue.FromFloat32(1.105f)), 2),
            };
            var expectedCommand = CommandValue(2, new byte[] { 1 });
            var extraCommand = CommandValue(1, new byte[] { 9 });
            var factory = new RecordingFactory(new[]
            {
                new BehaviorCaseExecutorStepResult(
                    BehaviorCaseProgress.Completed,
                    NodeStatus.Success,
                    3,
                    observed,
                    new[] { extraCommand, expectedCommand },
                    new[] { ObservedTrace(BehaviorCaseTraceEvent.NodeEntered, nodeIndex: new RuntimeNodeIndex(0)) },
                    DiagnosticCollection.Empty),
                EmptyResult(), EmptyResult(), EmptyResult(),
            });

            var run = BehaviorCaseRunner.Run(json, "runner.aibtcase.json", factory, Registry(9, 1));

            Assert.That(run.Success, Is.True, Failures(run));
            Assert.That(run.ExecutedStepCount, Is.EqualTo(4));
            Assert.That(factory.CreateCount, Is.EqualTo(1));
            Assert.That(factory.Configuration.Tree, Is.EqualTo("tree.aibt.json"));
            Assert.That(factory.Configuration.TreeInstanceId, Is.EqualTo(new TreeInstanceId(7)));
            Assert.That(factory.Configuration.RootSeed, Is.EqualTo(99));
            Assert.That(factory.Executor.Steps.Select(x => x.Operation), Is.EqualTo(new[]
            {
                BehaviorCaseOperation.Update,
                BehaviorCaseOperation.Resume,
                BehaviorCaseOperation.Abort,
                BehaviorCaseOperation.Restart,
            }));
            var update = (BehaviorCaseUpdateStep)factory.Executor.Steps[0];
            Assert.That(update.UpdateId, Is.EqualTo(1));
            Assert.That(update.TimeMicroseconds, Is.EqualTo(10));
            Assert.That(update.StepBudget, Is.EqualTo(3));
            Assert.That(update.Events.Single().SourceSequence, Is.EqualTo(5));
            var abort = (BehaviorCaseAbortStep)factory.Executor.Steps[2];
            Assert.That(abort.UpdateId, Is.EqualTo(2));
            Assert.That(abort.TimeMicroseconds, Is.EqualTo(11));
            Assert.That(abort.StepBudget, Is.EqualTo(4));
        }

        [Test]
        public void MalformedOrSemanticallyInvalidInput_NeverCallsFactory()
        {
            var factory = new RecordingFactory(Array.Empty<BehaviorCaseExecutorStepResult>());
            var malformed = BehaviorCaseRunner.Run(new byte[] { 0xc3, 0x28 }, "bad.aibtcase.json", factory, BehaviorCaseRegisteredValueRegistry.Empty);
            var schema = BehaviorCaseRunner.Run(
                BaseCase("{\"operation\":\"abort\",\"assertions\":[]}"),
                "schema.aibtcase.json",
                factory,
                BehaviorCaseRegisteredValueRegistry.Empty);
            var semantic = BehaviorCaseRunner.Run(
                BaseCase("{\"operation\":\"update\",\"updateId\":2,\"snapshotRevision\":1,\"timeMicroseconds\":0},{\"operation\":\"update\",\"updateId\":2,\"snapshotRevision\":1,\"timeMicroseconds\":1}"),
                "semantic.aibtcase.json",
                factory,
                BehaviorCaseRegisteredValueRegistry.Empty);

            Assert.That(malformed.Success, Is.False);
            Assert.That(malformed.InputDiagnostics[0].Code, Is.EqualTo(BehaviorCaseJsonDiagnosticCodes.InvalidUtf8));
            Assert.That(schema.Success, Is.False);
            Assert.That(schema.InputDiagnostics[0].Code, Is.EqualTo(BehaviorCaseJsonDiagnosticCodes.SchemaViolation));
            Assert.That(semantic.Success, Is.False);
            Assert.That(semantic.InputDiagnostics[0].Code, Is.EqualTo(BehaviorCaseJsonDiagnosticCodes.SemanticViolation));
            Assert.That(factory.CreateCount, Is.Zero);
            Assert.That(malformed.ExecutedStepCount, Is.Zero);
            Assert.That(schema.ExecutedStepCount, Is.Zero);
            Assert.That(semantic.ExecutedStepCount, Is.Zero);
        }

        [Test]
        public void ExplicitEmptyDiagnosticsAndTrace_AssertThatOutputsAreEmpty()
        {
            var json = BaseCase(AbortStep("\"expect\":{\"diagnostics\":[],\"trace\":[]}"));
            var diagnostic = new Diagnostic(DiagnosticCode.Parse("AIBT4001"), DiagnosticSeverity.Error, "fault");
            var actual = new BehaviorCaseExecutorStepResult(
                BehaviorCaseProgress.Completed,
                null,
                0,
                trace: new[] { ObservedTrace(BehaviorCaseTraceEvent.UpdateStarted) },
                diagnostics: new DiagnosticCollection(new[] { diagnostic }));

            var run = BehaviorCaseRunner.Run(json, null, new RecordingFactory(new[] { actual }), BehaviorCaseRegisteredValueRegistry.Empty);

            Assert.That(run.Failures.Select(x => x.Kind), Is.EquivalentTo(new[]
            {
                BehaviorCaseFailureKind.TraceMismatch,
                BehaviorCaseFailureKind.DiagnosticMismatch,
            }));
        }

        [Test]
        public void TypedInvariantFailures_AreStructured()
        {
            var json = BaseCase(AbortStep("\"expect\":{\"invariants\":[{\"kind\":\"no-active-operation-leaks\"},{\"kind\":\"terminal-root-has-no-active-nodes\"}]}"));
            var actual = new BehaviorCaseExecutorStepResult(
                BehaviorCaseProgress.Completed, NodeStatus.Failure, 0,
                activeOperationCount: 1,
                activeNodeCount: 1);

            var run = BehaviorCaseRunner.Run(json, null, new RecordingFactory(new[] { actual }), BehaviorCaseRegisteredValueRegistry.Empty);

            Assert.That(run.Failures.Count, Is.EqualTo(2));
            Assert.That(run.Failures.All(x => x.Kind == BehaviorCaseFailureKind.InvariantViolation), Is.True);
        }

        [Test]
        public void BackendException_StopsExecutionAndProducesStructuredFailure()
        {
            var factory = new ThrowingFactory();

            var run = BehaviorCaseRunner.Run(BaseCase(AbortStep() + ",{\"operation\":\"restart\"}"), null, factory, BehaviorCaseRegisteredValueRegistry.Empty);

            Assert.That(run.Success, Is.False);
            Assert.That(run.ExecutedStepCount, Is.Zero);
            Assert.That(run.Failures.Single().Kind, Is.EqualTo(BehaviorCaseFailureKind.ExecutorFault));
            Assert.That(run.Failures.Single().StepIndex, Is.Zero);
        }

        [Test]
        public void RegisteredValueRegistryRejectsUnknownOrWrongSizeBeforeFactory()
        {
            var json = BaseCase(
                "{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0," +
                "\"expect\":{\"commands\":{\"match\":\"exact\",\"records\":[" + Command(1, "AQ==") + "]}}}");
            var unknownFactory = new RecordingFactory(Array.Empty<BehaviorCaseExecutorStepResult>());
            var wrongSizeFactory = new RecordingFactory(Array.Empty<BehaviorCaseExecutorStepResult>());

            var unknown = BehaviorCaseRunner.Run(json, "unknown.aibtcase.json", unknownFactory,
                BehaviorCaseRegisteredValueRegistry.Empty);
            var wrongSize = BehaviorCaseRunner.Run(json, "size.aibtcase.json", wrongSizeFactory,
                new BehaviorCaseRegisteredValueRegistry(new[]
                {
                    new BehaviorCaseRegisteredValueContract(9, 1, 2),
                }));

            Assert.That(unknown.InputDiagnostics.Single().Code,
                Is.EqualTo(BehaviorCaseJsonDiagnosticCodes.SemanticViolation));
            Assert.That(unknown.InputDiagnostics.Single().Location.JsonPointer,
                Is.EqualTo("/steps/0/expect/commands/records/0/payload"));
            Assert.That(wrongSize.InputDiagnostics.Single().Code,
                Is.EqualTo(BehaviorCaseJsonDiagnosticCodes.SemanticViolation));
            Assert.That(unknownFactory.CreateCount, Is.Zero);
            Assert.That(wrongSizeFactory.CreateCount, Is.Zero);
        }

        [Test]
        public void OmittedCommandOperationIdIsWildcardButPresentValueIsExact()
        {
            var actualOperation = new OperationId(new TreeInstanceId(7), new RuntimeNodeIndex(0), 1, 2);
            var actual = new BehaviorCaseCommandExpectation(
                CommandPhase.Execute, new CommandType(5, 1), new TreeInstanceId(7), 1,
                actualOperation, BehaviorCaseValue.Registered(9, 1, new byte[] { 1 }));
            var wildcardJson = BaseCase(
                "{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0," +
                "\"expect\":{\"commands\":{\"match\":\"exact\",\"records\":[" + Command(1, "AQ==") + "]}}}");
            var mismatchJson = wildcardJson.Replace(
                "\"payload\":{\"type\":\"Registered\"",
                "\"operationId\":{\"treeInstanceId\":7,\"nodeIndex\":0,\"activationGeneration\":1,\"sequence\":1},\"payload\":{\"type\":\"Registered\"");

            var wildcard = BehaviorCaseRunner.Run(wildcardJson, null,
                new RecordingFactory(new[] { new BehaviorCaseExecutorStepResult(
                    BehaviorCaseProgress.Completed, null, 0, commands: new[] { actual }) }), Registry(9, 1));
            var mismatch = BehaviorCaseRunner.Run(mismatchJson, null,
                new RecordingFactory(new[] { new BehaviorCaseExecutorStepResult(
                    BehaviorCaseProgress.Completed, null, 0, commands: new[] { actual }) }), Registry(9, 1));

            Assert.That(wildcard.Success, Is.True, Failures(wildcard));
            Assert.That(mismatch.Failures.Single().Kind, Is.EqualTo(BehaviorCaseFailureKind.CommandMismatch));
        }

        [Test]
        public void CanonicalPositiveCancellationAndBudgetFixturesRunThroughTypedContract()
        {
            var positive = RunFixture(
                "positive-minimal.aibtcase.json",
                new[] { new BehaviorCaseExecutorStepResult(
                    BehaviorCaseProgress.Completed, NodeStatus.Success, 2,
                    diagnostics: DiagnosticCollection.Empty) },
                BehaviorCaseRegisteredValueRegistry.Empty);
            var cancellation = RunFixture(
                "cancellation.aibtcase.json",
                new[] { new BehaviorCaseExecutorStepResult(
                    BehaviorCaseProgress.Completed, NodeStatus.Failure, 0,
                    diagnostics: DiagnosticCollection.Empty) },
                new BehaviorCaseRegisteredValueRegistry(new[]
                {
                    new BehaviorCaseRegisteredValueContract(101, 1, 1),
                }));
            var budget = RunFixture(
                "budget-resume.aibtcase.json",
                new[]
                {
                    new BehaviorCaseExecutorStepResult(BehaviorCaseProgress.Suspended, null, 0),
                    new BehaviorCaseExecutorStepResult(BehaviorCaseProgress.Completed, NodeStatus.Success, 2),
                },
                BehaviorCaseRegisteredValueRegistry.Empty);

            Assert.That(positive.Success, Is.True, Failures(positive));
            Assert.That(cancellation.Success, Is.True, Failures(cancellation));
            Assert.That(budget.Success, Is.True, Failures(budget));
        }

        [Test]
        public void ObservedTraceContractIsValidatedAcrossUpdateAndResumeDeltas()
        {
            var json = BaseCase(
                "{\"operation\":\"update\",\"updateId\":3,\"snapshotRevision\":2,\"timeMicroseconds\":0,\"stepBudget\":1}," +
                "{\"operation\":\"resume\"}");
            var valid = BehaviorCaseRunner.Run(json, null, new RecordingFactory(new[]
            {
                new BehaviorCaseExecutorStepResult(BehaviorCaseProgress.Suspended, null, 1,
                    trace: new[] { ObservedTrace(BehaviorCaseTraceEvent.BudgetYielded, 1, 3, 2) }),
                new BehaviorCaseExecutorStepResult(BehaviorCaseProgress.Completed, NodeStatus.Success, 1,
                    trace: new[] { ObservedTrace(BehaviorCaseTraceEvent.ExecutionResumed, 2, 3, 2) }),
            }), BehaviorCaseRegisteredValueRegistry.Empty);
            var wrongContext = BehaviorCaseRunner.Run(json, null, new RecordingFactory(new[]
            {
                new BehaviorCaseExecutorStepResult(BehaviorCaseProgress.Suspended, null, 1,
                    trace: new[] { ObservedTrace(BehaviorCaseTraceEvent.BudgetYielded, 1, 4, 2) }),
                EmptyResult(),
            }), BehaviorCaseRegisteredValueRegistry.Empty);
            var duplicateSequence = BehaviorCaseRunner.Run(json, null, new RecordingFactory(new[]
            {
                new BehaviorCaseExecutorStepResult(BehaviorCaseProgress.Suspended, null, 1,
                    trace: new[] { ObservedTrace(BehaviorCaseTraceEvent.BudgetYielded, 1, 3, 2) }),
                new BehaviorCaseExecutorStepResult(BehaviorCaseProgress.Completed, null, 0,
                    trace: new[] { ObservedTrace(BehaviorCaseTraceEvent.ExecutionResumed, 1, 3, 2) }),
            }), BehaviorCaseRegisteredValueRegistry.Empty);
            var unsupportedFormat = BehaviorCaseRunner.Run(json, null, new RecordingFactory(new[]
            {
                new BehaviorCaseExecutorStepResult(BehaviorCaseProgress.Suspended, null, 1,
                    trace: new[] { ObservedTrace(BehaviorCaseTraceEvent.BudgetYielded, 1, 3, 2, traceFormatVersion: 2) }),
                EmptyResult(),
            }), BehaviorCaseRegisteredValueRegistry.Empty);

            Assert.That(valid.Success, Is.True, Failures(valid));
            Assert.That(wrongContext.Failures.Single().Kind, Is.EqualTo(BehaviorCaseFailureKind.ExecutorContract));
            Assert.That(wrongContext.ExecutedStepCount, Is.EqualTo(1));
            Assert.That(duplicateSequence.Failures.Single().Kind, Is.EqualTo(BehaviorCaseFailureKind.ExecutorContract));
            Assert.That(duplicateSequence.ExecutedStepCount, Is.EqualTo(2));
            Assert.That(unsupportedFormat.Failures.Single().Kind, Is.EqualTo(BehaviorCaseFailureKind.ExecutorContract));
            Assert.That(unsupportedFormat.ExecutedStepCount, Is.EqualTo(1));
        }

        private static BehaviorCaseRunResult RunFixture(
            string fileName,
            IReadOnlyList<BehaviorCaseExecutorStepResult> results,
            BehaviorCaseRegisteredValueRegistry registry)
        {
            var path = Path.Combine(Application.dataPath, "AIBT/Tests/Fixtures/Cases", fileName);
            return BehaviorCaseRunner.Run(File.ReadAllBytes(path), fileName,
                new RecordingFactory(results), registry);
        }

        private static BehaviorCaseExecutorStepResult EmptyResult()
            => new BehaviorCaseExecutorStepResult(BehaviorCaseProgress.Completed, null, 0);

        private static BehaviorCaseObservedTrace ObservedTrace(
            BehaviorCaseTraceEvent eventKind,
            ulong sequence = 1,
            ulong updateId = 1,
            ulong revision = 1,
            RuntimeNodeIndex? nodeIndex = null,
            TreeInstanceId? treeInstanceId = null,
            CompiledHash? hash = null,
            uint traceFormatVersion = BehaviorCaseObservedTrace.SupportedFormatVersion)
            => new BehaviorCaseObservedTrace(
                eventKind,
                traceFormatVersion,
                hash ?? new CompiledHash(new string('f', CompiledHash.HexLength)),
                treeInstanceId ?? new TreeInstanceId(7),
                sequence,
                updateId,
                new Revision(revision),
                nodeIndex);

        private static string BaseCase(string steps)
            => "{\"format\":\"aibt.case\",\"formatVersion\":1,\"name\":\"runner\",\"tree\":\"tree.aibt.json\",\"treeInstanceId\":7,\"rootSeed\":0,\"steps\":[" + steps + "]}";

        private static string AbortStep(string suffix = null)
            => "{\"operation\":\"abort\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0"
                + (suffix == null ? string.Empty : "," + suffix) + "}";

        private static BehaviorCaseRegisteredValueRegistry Registry(ulong typeId, uint version)
            => new BehaviorCaseRegisteredValueRegistry(new[]
            {
                new BehaviorCaseRegisteredValueContract(typeId, version),
            });

        private static string Command(ulong sequence, string payload)
            => "{\"phase\":\"execute\",\"typeId\":5,\"typeVersion\":1,\"treeInstanceId\":7,\"sequence\":" + sequence + ",\"payload\":{\"type\":\"Registered\",\"typeId\":9,\"typeVersion\":1,\"encoding\":\"base64\",\"value\":\"" + payload + "\"}}";

        private static BehaviorCaseCommandExpectation CommandValue(ulong sequence, byte[] payload)
            => new BehaviorCaseCommandExpectation(
                CommandPhase.Execute,
                new CommandType(5, 1),
                new TreeInstanceId(7),
                sequence,
                null,
                BehaviorCaseValue.Registered(9, 1, payload));

        private static string Failures(BehaviorCaseRunResult result)
            => string.Join(" | ", result.Failures.Select(x => x.Kind + " " + x.Pointer + ": " + x.Message));

        private sealed class RecordingFactory : IBehaviorCaseExecutorFactory
        {
            private readonly IReadOnlyList<BehaviorCaseExecutorStepResult> _results;
            internal RecordingFactory(IReadOnlyList<BehaviorCaseExecutorStepResult> results) { _results = results; }
            internal int CreateCount { get; private set; }
            internal BehaviorCaseExecutorConfiguration Configuration { get; private set; }
            internal RecordingExecutor Executor { get; private set; }
            public IBehaviorCaseExecutor Create(BehaviorCaseExecutorConfiguration configuration)
            {
                CreateCount++;
                Configuration = configuration;
                Executor = new RecordingExecutor(_results);
                return Executor;
            }
        }

        private sealed class RecordingExecutor : IBehaviorCaseExecutor
        {
            private readonly IReadOnlyList<BehaviorCaseExecutorStepResult> _results;
            internal RecordingExecutor(IReadOnlyList<BehaviorCaseExecutorStepResult> results) { _results = results; }
            internal List<BehaviorCaseStep> Steps { get; } = new List<BehaviorCaseStep>();
            public BehaviorCaseExecutorStepResult Execute(BehaviorCaseStep step)
            {
                Steps.Add(step);
                return _results[Steps.Count - 1];
            }
            public void Dispose() { }
        }

        private sealed class ThrowingFactory : IBehaviorCaseExecutorFactory
        {
            public IBehaviorCaseExecutor Create(BehaviorCaseExecutorConfiguration configuration) => new ThrowingExecutor();
        }

        private sealed class ThrowingExecutor : IBehaviorCaseExecutor
        {
            public BehaviorCaseExecutorStepResult Execute(BehaviorCaseStep step) => throw new InvalidOperationException("fake");
            public void Dispose() { }
        }
    }
}
