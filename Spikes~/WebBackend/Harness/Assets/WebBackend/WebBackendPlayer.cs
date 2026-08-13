using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AIBT.Tests.BehaviorCases;
using AIBT.Tests.Integration.SemanticSlice;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace AIBT.Spikes.WebBackend
{
    public sealed class WebBackendPlayer : MonoBehaviour
    {
        private const int MeasurementCycles = 250;
        private const int WarmupCycles = 25;
        private static readonly string[] TreeFiles =
        {
            "async-action.aibt.json",
            "enum-snapshot.aibt.json",
            "invalid-unknown-node.aibt.json",
            "parallel-decorator.aibt.json",
            "patrol-react.aibt.json",
        };
        private static readonly string[] CaseFiles =
        {
            "async-budgeted-abort.aibtcase.json",
            "async-completion.aibtcase.json",
            "initial-blackboard.aibtcase.json",
            "parallel-decorator.aibtcase.json",
            "patrol-react.aibtcase.json",
        };

        [DllImport("__Internal")]
        private static extern void AIBT_ReportResult(string json);

        private string _fixtureError;

        private IEnumerator Start()
        {
            var result = new JObject
            {
                ["format"] = "aibt.web-spike-result",
                ["formatVersion"] = 1,
                ["unityVersion"] = Application.unityVersion,
                ["platform"] = Application.platform.ToString(),
                ["processorCount"] = SystemInfo.processorCount,
                ["graphicsDevice"] = SystemInfo.graphicsDeviceName,
                ["measurementCycles"] = MeasurementCycles,
                ["warmupCycles"] = WarmupCycles,
            };

            var goldenRoot = Path.Combine(Application.persistentDataPath, "Golden");
            yield return CopyFixtures(goldenRoot);
            if (_fixtureError != null)
            {
                result["success"] = false;
                result["exceptionType"] = "FixtureDownloadFailure";
                result["exceptionMessage"] = _fixtureError;
                Report(result.ToString(Newtonsoft.Json.Formatting.None));
                yield break;
            }

            try
            {
                var factory = new ReferenceBehaviorCaseExecutorFactory(
                    goldenRoot,
                    SemanticSliceNodeContracts.CreateAuthoringRegistry());
                var registered = RegisteredValues();
                var cases = new JArray();
                foreach (var file in CaseFiles)
                {
                    var run = BehaviorCaseRunner.Run(
                        File.ReadAllBytes(Path.Combine(goldenRoot, "Cases", file)),
                        file,
                        factory,
                        registered);
                    cases.Add(new JObject
                    {
                        ["file"] = file,
                        ["success"] = run.Success,
                        ["executedStepCount"] = run.ExecutedStepCount,
                        ["failureCount"] = run.Failures.Count,
                        ["diagnosticCount"] = run.InputDiagnostics.Count,
                    });
                }

                result["behaviorCases"] = cases;
                result["budgetEquivalence"] = RunBudgetEquivalence(factory);
                result["immediateMeasurement"] = Measure(factory, false);
                result["budgetedMeasurement"] = Measure(factory, true);
                result["success"] = cases.All(token => token.Value<bool>("success"))
                    && result["budgetEquivalence"].Value<bool>("success");
            }
            catch (Exception exception)
            {
                result["success"] = false;
                result["exceptionType"] = exception.GetType().FullName;
                result["exceptionMessage"] = exception.Message;
                result["exceptionStack"] = exception.StackTrace;
            }

            Report(result.ToString(Newtonsoft.Json.Formatting.None));
        }

        private IEnumerator CopyFixtures(string goldenRoot)
        {
            Directory.CreateDirectory(Path.Combine(goldenRoot, "Trees"));
            Directory.CreateDirectory(Path.Combine(goldenRoot, "Cases"));
            foreach (var file in TreeFiles)
                yield return CopyFixture("Trees", file, goldenRoot);
            foreach (var file in CaseFiles)
                yield return CopyFixture("Cases", file, goldenRoot);
        }

        private IEnumerator CopyFixture(string category, string file, string goldenRoot)
        {
            var source = Application.streamingAssetsPath + "/Golden/" + category + "/" + file;
            using (var request = UnityWebRequest.Get(source))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _fixtureError = "Fixture download failed: " + source + " " + request.error;
                    yield break;
                }
                File.WriteAllBytes(Path.Combine(goldenRoot, category, file), request.downloadHandler.data);
            }
        }

        private static JObject RunBudgetEquivalence(ReferenceBehaviorCaseExecutorFactory factory)
        {
            var reactive = RunBudgetEquivalenceScenario(
                factory, "reactive-blackboard-abort", "Trees/patrol-react.aibt.json", 101, 102);
            var asynchronous = RunBudgetEquivalenceScenario(
                factory, "async-command", "Trees/async-action.aibt.json", 103, 104);
            return new JObject
            {
                ["success"] = reactive.Value<bool>("success") && asynchronous.Value<bool>("success"),
                ["scenarios"] = new JArray(reactive, asynchronous),
                ["observationsCompared"] = new JArray(
                    "progress", "rootStatus", "executedSteps", "activeCounts",
                    "blackboardValuesAndVersions", "commands", "semanticTraceFields", "diagnostics"),
                ["normalization"] = "treeInstanceId and sequence fields only; BudgetYielded and ExecutionResumed filtered",
            };
        }

        private static JObject RunBudgetEquivalenceScenario(
            ReferenceBehaviorCaseExecutorFactory factory,
            string name,
            string tree,
            ulong unlimitedTreeInstanceId,
            ulong budgetedTreeInstanceId)
        {
            var unlimitedDocument = Document(
                tree,
                new TreeInstanceId(unlimitedTreeInstanceId),
                new BehaviorCaseUpdateStep(1, new Revision(1), 0, null, null, null, null));
            var budgetedDocument = Document(
                tree,
                new TreeInstanceId(budgetedTreeInstanceId),
                new BehaviorCaseUpdateStep(1, new Revision(1), 0, 1, null, null, null));
            using (var unlimitedExecutor = factory.Create(new BehaviorCaseExecutorConfiguration(unlimitedDocument)))
            using (var budgetedExecutor = factory.Create(new BehaviorCaseExecutorConfiguration(budgetedDocument)))
            {
                var unlimited = unlimitedExecutor.Execute(unlimitedDocument.Steps[0]);
                var budgetedTrace = new List<BehaviorCaseObservedTrace>();
                var budgetedCommands = new List<BehaviorCaseCommandExpectation>();
                var budgetedDiagnostics = new List<Diagnostic>();
                var budgeted = budgetedExecutor.Execute(budgetedDocument.Steps[0]);
                ulong budgetedExecutedSteps = 0;
                AppendObservation(budgetedTrace, budgetedCommands, budgetedDiagnostics, ref budgetedExecutedSteps, budgeted);
                var segments = 1;
                while (budgeted.Progress == BehaviorCaseProgress.Suspended && segments < 100)
                {
                    budgeted = budgetedExecutor.Execute(new BehaviorCaseResumeStep(1, null));
                    AppendObservation(budgetedTrace, budgetedCommands, budgetedDiagnostics, ref budgetedExecutedSteps, budgeted);
                    segments++;
                }

                var unlimitedTrace = unlimited.Trace.Where(IsSemanticTrace).ToArray();
                var success = budgeted.Progress == unlimited.Progress
                    && budgeted.RootStatus == unlimited.RootStatus
                    && budgeted.ActiveNodeCount == unlimited.ActiveNodeCount
                    && budgeted.ActiveOperationCount == unlimited.ActiveOperationCount
                    && budgetedExecutedSteps == unlimited.ExecutedSteps
                    && BlackboardEquivalent(unlimited.Blackboard, budgeted.Blackboard)
                    && CommandsEquivalent(unlimited.Commands, budgetedCommands)
                    && TraceEquivalent(unlimitedTrace, budgetedTrace)
                    && unlimited.Diagnostics.SequenceEqual(budgetedDiagnostics);
                return new JObject
                {
                    ["name"] = name,
                    ["success"] = success,
                    ["segments"] = segments,
                    ["progress"] = budgeted.Progress.ToString(),
                    ["rootStatus"] = budgeted.RootStatus.ToString(),
                    ["executedSteps"] = budgetedExecutedSteps,
                    ["blackboardSlots"] = budgeted.Blackboard.Count,
                    ["commands"] = budgetedCommands.Count,
                    ["semanticTraceEvents"] = budgetedTrace.Count,
                    ["activeNodeCount"] = budgeted.ActiveNodeCount,
                    ["activeOperationCount"] = budgeted.ActiveOperationCount,
                };
            }
        }

        private static JObject Measure(ReferenceBehaviorCaseExecutorFactory factory, bool budgeted)
        {
            var treeInstanceId = new TreeInstanceId(budgeted ? 202UL : 201UL);
            var first = new BehaviorCaseUpdateStep(1, new Revision(1), 0, budgeted ? 1UL : (ulong?)null, null, null, null);
            var document = Document(treeInstanceId, first);
            ulong semanticSteps = 0;
            var updateId = 1UL;
            var revision = 1UL;
            using (var executor = factory.Create(new BehaviorCaseExecutorConfiguration(document)))
            {
                for (var cycle = 0; cycle < WarmupCycles; cycle++)
                    ExecuteCycle(executor, budgeted, ref updateId, ref revision, cycle);
                GC.Collect();
                var beforeCollections = GC.CollectionCount(0);
                var beforeMemory = GC.GetTotalMemory(false);
                var stopwatch = Stopwatch.StartNew();
                for (var cycle = 0; cycle < MeasurementCycles; cycle++)
                    semanticSteps += ExecuteCycle(executor, budgeted, ref updateId, ref revision, WarmupCycles + cycle);
                stopwatch.Stop();
                var afterMemory = GC.GetTotalMemory(false);
                var elapsedSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.000001d);
                return new JObject
                {
                    ["policy"] = budgeted ? "SingleThreadBudgeted" : "SingleThreadImmediate",
                    ["warmupCycles"] = WarmupCycles,
                    ["cycles"] = MeasurementCycles,
                    ["semanticSteps"] = semanticSteps,
                    ["elapsedMilliseconds"] = stopwatch.Elapsed.TotalMilliseconds,
                    ["semanticStepsPerSecond"] = semanticSteps / elapsedSeconds,
                    ["gen0CollectionDelta"] = GC.CollectionCount(0) - beforeCollections,
                    ["managedHeapDeltaBytes"] = afterMemory - beforeMemory,
                    ["allocationMetricLimitation"] = "GC.GetTotalMemory delta is coarse and is not a zero-allocation proof.",
                };
            }
        }

        private static ulong ExecuteCycle(
            IBehaviorCaseExecutor executor,
            bool budgeted,
            ref ulong updateId,
            ref ulong revision,
            int cycle)
        {
            ulong semanticSteps = 0;
            var step = new BehaviorCaseUpdateStep(
                updateId++,
                new Revision(revision++),
                cycle,
                budgeted ? 1UL : (ulong?)null,
                null,
                null,
                null);
            var observed = executor.Execute(step);
            semanticSteps += observed.ExecutedSteps;
            while (observed.Progress == BehaviorCaseProgress.Suspended)
            {
                observed = executor.Execute(new BehaviorCaseResumeStep(budgeted ? 1UL : (ulong?)null, null));
                semanticSteps += observed.ExecutedSteps;
            }
            if (observed.RootStatus != NodeStatus.Success)
                throw new InvalidOperationException("Measurement tree did not complete successfully.");
            executor.Execute(new BehaviorCaseControlStep(null));
            return semanticSteps;
        }

        private static BehaviorCaseDocument Document(TreeInstanceId treeInstanceId, params BehaviorCaseStep[] steps)
            => Document("Trees/parallel-decorator.aibt.json", treeInstanceId, steps);

        private static BehaviorCaseDocument Document(
            string tree,
            TreeInstanceId treeInstanceId,
            params BehaviorCaseStep[] steps)
            => new BehaviorCaseDocument(
                "web spike",
                null,
                tree,
                treeInstanceId,
                0,
                null,
                steps,
                null);

        private static BehaviorCaseRegisteredValueRegistry RegisteredValues()
            => new BehaviorCaseRegisteredValueRegistry(new[]
            {
                new BehaviorCaseRegisteredValueContract(
                    StableHash.Fnv1A64(SemanticSliceNodeContracts.AsyncStartCommandType), 1, 0),
                new BehaviorCaseRegisteredValueContract(
                    StableHash.Fnv1A64(SemanticSliceNodeContracts.AsyncCancelCommandType), 1, 0),
            });

        private static void AppendObservation(
            ICollection<BehaviorCaseObservedTrace> trace,
            ICollection<BehaviorCaseCommandExpectation> commands,
            ICollection<Diagnostic> diagnostics,
            ref ulong executedSteps,
            BehaviorCaseExecutorStepResult result)
        {
            foreach (var item in result.Trace)
                if (IsSemanticTrace(item)) trace.Add(item);
            foreach (var item in result.Commands) commands.Add(item);
            foreach (var item in result.Diagnostics) diagnostics.Add(item);
            executedSteps = checked(executedSteps + result.ExecutedSteps);
        }

        private static bool BlackboardEquivalent(
            IReadOnlyDictionary<string, BehaviorCaseObservedBlackboardValue> left,
            IReadOnlyDictionary<string, BehaviorCaseObservedBlackboardValue> right)
        {
            if (left.Count != right.Count) return false;
            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var other)
                    || pair.Value.Version != other.Version
                    || !ValueEquivalent(pair.Value.Value, other.Value)) return false;
            }
            return true;
        }

        private static bool ValueEquivalent(BehaviorCaseValue left, BehaviorCaseValue right)
        {
            if (left.IsRegistered != right.IsRegistered) return false;
            if (!left.IsRegistered)
                return left.BuiltInValue == right.BuiltInValue
                    && string.Equals(left.EnumContract, right.EnumContract, StringComparison.Ordinal);
            return left.RegisteredTypeId == right.RegisteredTypeId
                && left.RegisteredTypeVersion == right.RegisteredTypeVersion
                && left.CopyRegisteredBytes().SequenceEqual(right.CopyRegisteredBytes());
        }

        private static bool CommandsEquivalent(
            IReadOnlyList<BehaviorCaseCommandExpectation> left,
            IReadOnlyList<BehaviorCaseCommandExpectation> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
            {
                var a = left[index];
                var b = right[index];
                if (a.Phase != b.Phase || a.Type != b.Type
                    || a.OperationId.HasValue != b.OperationId.HasValue
                    || !ValueEquivalent(a.Payload, b.Payload)) return false;
                if (a.OperationId.HasValue
                    && (a.OperationId.Value.NodeIndex != b.OperationId.Value.NodeIndex
                        || a.OperationId.Value.ActivationGeneration != b.OperationId.Value.ActivationGeneration)) return false;
            }
            return true;
        }

        private static bool TraceEquivalent(
            IReadOnlyList<BehaviorCaseObservedTrace> left,
            IReadOnlyList<BehaviorCaseObservedTrace> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
            {
                var a = left[index];
                var b = right[index];
                if (a.Event != b.Event || a.TraceFormatVersion != b.TraceFormatVersion
                    || a.TreeSemanticHash != b.TreeSemanticHash
                    || a.UpdateId != b.UpdateId || a.SnapshotRevision != b.SnapshotRevision
                    || a.NodeIndex != b.NodeIndex || a.Status != b.Status
                    || a.ExitReason != b.ExitReason || a.AbortReason != b.AbortReason
                    || a.SourceNodeIndex != b.SourceNodeIndex || a.DiagnosticCode != b.DiagnosticCode
                    || a.StableBlackboardKeyId != b.StableBlackboardKeyId
                    || a.OldBlackboardVersion != b.OldBlackboardVersion
                    || a.NewBlackboardVersion != b.NewBlackboardVersion) return false;
            }
            return true;
        }

        private static bool IsSemanticTrace(BehaviorCaseObservedTrace item)
            => item.Event != BehaviorCaseTraceEvent.BudgetYielded
                && item.Event != BehaviorCaseTraceEvent.ExecutionResumed;

        private static void Report(string json)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            AIBT_ReportResult(json);
#else
            UnityEngine.Debug.Log("AIBT_WEB_RESULT:" + json);
#endif
        }
    }
}
