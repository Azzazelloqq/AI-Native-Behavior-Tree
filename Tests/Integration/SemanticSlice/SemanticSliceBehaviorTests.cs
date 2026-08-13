using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Tests.BehaviorCases;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Integration.SemanticSlice
{
    public sealed class SemanticSliceBehaviorTests
    {
        [TestCase("patrol-react.aibtcase.json")]
        [TestCase("parallel-decorator.aibtcase.json")]
        [TestCase("async-completion.aibtcase.json")]
        [TestCase("async-budgeted-abort.aibtcase.json")]
        [TestCase("initial-blackboard.aibtcase.json")]
        public void GoldenBehaviorCase_PassesReferenceExecutor(string fileName)
        {
            var run = RunCase(fileName);

            Assert.That(run.Success, Is.True, Failures(run));
            Assert.That(run.InputDiagnostics, Is.Empty);
        }

        [Test]
        public void AsyncAdapter_ExposesPerCallCommandCompletionAndTraceDeltas()
        {
            var document = Document(
                "Trees/async-action.aibt.json",
                new TreeInstanceId(9),
                new BehaviorCaseUpdateStep(1, new Revision(1), 0, null, null, null, null));
            var executor = Factory().Create(new BehaviorCaseExecutorConfiguration(document));
            var first = executor.Execute(document.Steps[0]);
            var operation = first.Commands.Single().OperationId.Value;
            var completion = new BehaviorCaseCompletion(
                1,
                1,
                operation,
                CompletionOutcome.Succeeded,
                new Revision(2),
                null);
            var second = executor.Execute(new BehaviorCaseUpdateStep(
                2,
                new Revision(2),
                1,
                null,
                null,
                new[] { completion },
                null));

            Assert.That(first.Progress, Is.EqualTo(BehaviorCaseProgress.Waiting));
            Assert.That(first.Commands, Has.Count.EqualTo(1));
            Assert.That(first.Trace.Any(item => item.Event == BehaviorCaseTraceEvent.CommandEmitted), Is.True);
            Assert.That(second.RootStatus, Is.EqualTo(NodeStatus.Success));
            Assert.That(second.Commands, Is.Empty);
            Assert.That(second.Trace.Any(item => item.Event == BehaviorCaseTraceEvent.CompletionConsumed), Is.True);
            Assert.That(second.Trace.All(item => item.UpdateId == 2), Is.True);
        }

        [Test]
        public void BudgetSegments_AreSemanticallyEquivalentToUnlimitedExecution()
        {
            var unlimitedDocument = Document(
                "Trees/parallel-decorator.aibt.json",
                new TreeInstanceId(11),
                new BehaviorCaseUpdateStep(1, new Revision(1), 0, null, null, null, null));
            var segmentedDocument = Document(
                "Trees/parallel-decorator.aibt.json",
                new TreeInstanceId(12),
                new BehaviorCaseUpdateStep(1, new Revision(1), 0, 1, null, null, null));
            var unlimitedExecutor = Factory().Create(new BehaviorCaseExecutorConfiguration(unlimitedDocument));
            var segmentedExecutor = Factory().Create(new BehaviorCaseExecutorConfiguration(segmentedDocument));

            var unlimited = unlimitedExecutor.Execute(unlimitedDocument.Steps[0]);
            var segmentedTrace = new List<BehaviorCaseTraceEvent>();
            var segmented = segmentedExecutor.Execute(segmentedDocument.Steps[0]);
            AppendSemanticTrace(segmentedTrace, segmented);
            var guard = 0;
            while (segmented.Progress == BehaviorCaseProgress.Suspended && guard++ < 100)
            {
                segmented = segmentedExecutor.Execute(new BehaviorCaseResumeStep(1, null));
                AppendSemanticTrace(segmentedTrace, segmented);
            }

            Assert.That(guard, Is.LessThan(100));
            Assert.That(segmented.Progress, Is.EqualTo(unlimited.Progress));
            Assert.That(segmented.RootStatus, Is.EqualTo(unlimited.RootStatus));
            CollectionAssert.AreEqual(
                unlimited.Trace
                    .Where(IsSemanticTrace)
                    .Select(item => item.Event),
                segmentedTrace);
        }

        [Test]
        public void UnsupportedRootSeedAndExternalEventsFailBeforeHiddenExecution()
        {
            var seeded = File.ReadAllText(CasePath("parallel-decorator.aibtcase.json"))
                .Replace("\"rootSeed\": 0", "\"rootSeed\": 1");
            var seededRun = BehaviorCaseRunner.Run(
                seeded,
                "seeded.aibtcase.json",
                Factory(),
                RegisteredValues());
            var events = "{\"format\":\"aibt.case\",\"formatVersion\":1,\"name\":\"events\"," +
                "\"tree\":\"Trees/parallel-decorator.aibt.json\",\"treeInstanceId\":13,\"rootSeed\":0," +
                "\"steps\":[{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0," +
                "\"events\":[{\"sourceId\":1,\"sourceSequence\":1,\"eventTypeId\":1,\"eventTypeVersion\":1," +
                "\"payload\":{\"type\":\"Bool\",\"value\":true}}]}]}";
            var eventsRun = BehaviorCaseRunner.Run(events, "events.aibtcase.json", Factory(), RegisteredValues());

            Assert.That(seededRun.ExecutedStepCount, Is.Zero);
            Assert.That(seededRun.Failures.Single().Kind, Is.EqualTo(BehaviorCaseFailureKind.FactoryFault));
            Assert.That(eventsRun.ExecutedStepCount, Is.Zero);
            Assert.That(eventsRun.Failures.Single().Kind, Is.EqualTo(BehaviorCaseFailureKind.ExecutorFault));
        }

        [Test]
        public void Enum32InitialValue_RoundTripsThroughReferenceSnapshotWithCanonicalContract()
        {
            const string contract = "aibt.test.alert-state";
            var document = new BehaviorCaseDocument(
                "enum snapshot",
                null,
                "Trees/enum-snapshot.aibt.json",
                new TreeInstanceId(14),
                0,
                new Dictionary<string, BehaviorCaseValue>
                {
                    { "state", BehaviorCaseValue.Enum32(contract, 7) },
                },
                new BehaviorCaseStep[]
                {
                    new BehaviorCaseUpdateStep(1, new Revision(1), 0, null, null, null, null),
                },
                null);
            var result = Factory().Create(new BehaviorCaseExecutorConfiguration(document)).Execute(document.Steps[0]);

            var value = result.Blackboard["state"].Value;
            Assert.That(value.EnumContract, Is.EqualTo(contract));
            Assert.That(value.BuiltInValue.TryGetEnum32(out var enumValue), Is.True);
            Assert.That(enumValue.ContractTypeId, Is.EqualTo(StableHash.Fnv1A64(contract)));
            Assert.That(enumValue.Value, Is.EqualTo(7));
            Assert.That(result.Blackboard["state"].Version, Is.Zero);
        }

        private static BehaviorCaseRunResult RunCase(string fileName)
            => BehaviorCaseRunner.Run(
                File.ReadAllBytes(CasePath(fileName)),
                fileName,
                Factory(),
                RegisteredValues());

        private static ReferenceBehaviorCaseExecutorFactory Factory()
            => new ReferenceBehaviorCaseExecutorFactory(
                GoldenRoot(),
                SemanticSliceNodeContracts.CreateAuthoringRegistry());

        private static BehaviorCaseRegisteredValueRegistry RegisteredValues()
            => new BehaviorCaseRegisteredValueRegistry(new[]
            {
                new BehaviorCaseRegisteredValueContract(
                    StableHash.Fnv1A64(SemanticSliceNodeContracts.AsyncStartCommandType), 1, 0),
                new BehaviorCaseRegisteredValueContract(
                    StableHash.Fnv1A64(SemanticSliceNodeContracts.AsyncCancelCommandType), 1, 0),
            });

        private static BehaviorCaseDocument Document(
            string tree,
            TreeInstanceId treeInstanceId,
            params BehaviorCaseStep[] steps)
            => new BehaviorCaseDocument(
                "direct adapter",
                null,
                tree,
                treeInstanceId,
                0,
                null,
                steps,
                null);

        private static void AppendSemanticTrace(
            ICollection<BehaviorCaseTraceEvent> target,
            BehaviorCaseExecutorStepResult result)
        {
            foreach (var item in result.Trace)
                if (IsSemanticTrace(item)) target.Add(item.Event);
        }

        private static bool IsSemanticTrace(BehaviorCaseObservedTrace item)
            => item.Event != BehaviorCaseTraceEvent.BudgetYielded
                && item.Event != BehaviorCaseTraceEvent.ExecutionResumed;

        private static string GoldenRoot()
            => Path.Combine(Application.dataPath, "AIBT/Tests/Fixtures/Golden");

        private static string CasePath(string fileName)
            => Path.Combine(GoldenRoot(), "Cases", fileName);

        private static string Failures(BehaviorCaseRunResult run)
            => string.Join(" | ", run.Failures.Select(item => item.Kind + " "
                + item.Pointer + ": " + item.Message));
    }
}
