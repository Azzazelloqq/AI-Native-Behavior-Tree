using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.BehaviorCases
{
    public sealed class BehaviorCaseJsonTests
    {
        [Test]
        public void FullTypedCase_RoundTripsToStableCanonicalBytesAcrossCultures()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var read = BehaviorCaseJson.Parse(FullCaseJson(), "full.aibtcase.json");

                Assert.That(read.Success, Is.True, Messages(read.Diagnostics));
                Assert.That(read.Document.Steps.Count, Is.EqualTo(3));
                Assert.That(((BehaviorCaseUpdateStep)read.Document.Steps[0]).SnapshotRevision.Value, Is.EqualTo(2));
                Assert.That(((BehaviorCaseUpdateStep)read.Document.Steps[2]).SnapshotRevision.Value, Is.EqualTo(2),
                    "Equal snapshot revisions are valid across strictly increasing updates.");

                var first = BehaviorCaseJson.Serialize(read.Document);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                var secondRead = BehaviorCaseJson.Parse(first.CopyUtf8(), "roundtrip.aibtcase.json");
                var second = BehaviorCaseJson.Serialize(secondRead.Document);

                Assert.That(first.Success, Is.True, Messages(first.Diagnostics));
                Assert.That(secondRead.Success, Is.True, Messages(secondRead.Diagnostics));
                Assert.That(second.Success, Is.True, Messages(second.Diagnostics));
                Assert.That(second.CopyUtf8(), Is.EqualTo(first.CopyUtf8()));

                var text = Encoding.UTF8.GetString(first.CopyUtf8());
                Assert.That(text, Does.EndWith("\n"));
                Assert.That(text, Does.Not.Contain("\r"));
                Assert.That(text, Does.Contain("\"contract\": \"combat.state\""));
                Assert.That(text, Does.Contain("\"value\": 0"), "Negative zero is canonicalized.");
                Assert.That(text.IndexOf("\"\ue000\"", StringComparison.Ordinal),
                    Is.LessThan(text.IndexOf("\"\ud800\udc00\"", StringComparison.Ordinal)),
                    "Map keys use ordinal UTF-8 byte order, not UTF-16 order.");
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [TestCase("\"assertions\":[]")]
        [TestCase("\"future\":true")]
        public void UnknownOrFreeFormRootFields_AreRejected(string injected)
        {
            var json = MinimalCaseJson().Replace("\"steps\":", injected + ",\"steps\":");
            AssertCode(json, BehaviorCaseJsonDiagnosticCodes.SchemaViolation);
        }

        [Test]
        public void RegisteredEnvelope_IsStrictAndDefensivelyCopied()
        {
            var read = BehaviorCaseJson.Parse(FullCaseJson());
            var value = read.Document.InitialBlackboard["payload"];
            var first = value.CopyRegisteredBytes();
            first[0] = 255;

            Assert.That(value.CopyRegisteredBytes(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.Throws<NotSupportedException>(() =>
                ((IDictionary<string, BehaviorCaseValue>)read.Document.InitialBlackboard).Add("x", value));

            var write = BehaviorCaseJson.Serialize(read.Document);
            var bytes = write.CopyUtf8();
            bytes[0] = 0;
            Assert.That(write.CopyUtf8()[0], Is.EqualTo((byte)'{'));
        }

        [TestCase("\"b\":{\"type\":\"Bool\",\"value\":true}")]
        [TestCase("\"i32\":{\"type\":\"Int32\",\"value\":-2147483648}")]
        [TestCase("\"i64\":{\"type\":\"Int64\",\"value\":-9223372036854775808}")]
        [TestCase("\"f32\":{\"type\":\"Float32\",\"value\":1.25}")]
        [TestCase("\"f64\":{\"type\":\"Float64\",\"value\":1e-12}")]
        [TestCase("\"f2\":{\"type\":\"Float2\",\"value\":{\"x\":1,\"y\":2}}")]
        [TestCase("\"f3\":{\"type\":\"Float3\",\"value\":{\"x\":1,\"y\":2,\"z\":3}}")]
        [TestCase("\"q\":{\"type\":\"Quaternion\",\"value\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}")]
        [TestCase("\"e\":{\"type\":\"Enum32\",\"contract\":\"state.v1\",\"value\":-2}")]
        [TestCase("\"s32\":{\"type\":\"FixedString32\",\"value\":\"hello\"}")]
        [TestCase("\"s64\":{\"type\":\"FixedString64\",\"value\":\"hello\"}")]
        [TestCase("\"s128\":{\"type\":\"FixedString128\",\"value\":\"hello\"}")]
        [TestCase("\"s512\":{\"type\":\"FixedString512\",\"value\":\"hello\"}")]
        [TestCase("\"agent\":{\"type\":\"AgentId\",\"value\":1}")]
        [TestCase("\"entity\":{\"type\":\"EntityId\",\"value\":1}")]
        [TestCase("\"operation\":{\"type\":\"OperationId\",\"value\":{\"treeInstanceId\":1,\"nodeIndex\":0,\"activationGeneration\":0,\"sequence\":0}}")]
        [TestCase("\"asset\":{\"type\":\"AssetId\",\"value\":{\"guid\":\"00112233445566778899aabbccddeeff\",\"localFileId\":-1}}")]
        [TestCase("\"registered\":{\"type\":\"Registered\",\"typeId\":1,\"typeVersion\":1,\"encoding\":\"base64\",\"value\":\"AA==\"}")]
        public void EveryTypedValueVariant_IsAcceptedAndRoundTrips(string member)
        {
            var json = MinimalCaseJson().Replace("\"initialBlackboard\":{}", "\"initialBlackboard\":{" + member + "}");
            var read = BehaviorCaseJson.Parse(json);
            var write = read.Success ? BehaviorCaseJson.Serialize(read.Document) : null;
            var reread = write != null && write.Success ? BehaviorCaseJson.Parse(write.CopyUtf8()) : null;

            Assert.That(read.Success, Is.True, Messages(read.Diagnostics));
            Assert.That(write.Success, Is.True, Messages(write.Diagnostics));
            Assert.That(reread.Success, Is.True, Messages(reread.Diagnostics));
        }

        [TestCase("AQI", TestName = "MissingBase64Padding")]
        [TestCase("AQI===", TestName = "ExcessBase64Padding")]
        [TestCase("AQ I=", TestName = "Base64Whitespace")]
        public void RegisteredEnvelope_RejectsNonCanonicalBase64(string encoded)
        {
            var json = MinimalCaseJson().Replace(
                "\"initialBlackboard\":{}",
                "\"initialBlackboard\":{\"x\":{\"type\":\"Registered\",\"typeId\":1,\"typeVersion\":1,\"encoding\":\"base64\",\"value\":\"" + encoded + "\"}}");
            AssertCode(json, BehaviorCaseJsonDiagnosticCodes.SchemaViolation);
        }

        [Test]
        public void CommandAndCompletionPayloads_RejectBuiltInValues()
        {
            var completion = MinimalCaseJson().Replace(
                "\"events\":[]",
                "\"events\":[],\"completions\":[{\"sourceId\":1,\"sourceSequence\":1,\"operationId\":{\"treeInstanceId\":7,\"nodeIndex\":0,\"activationGeneration\":1,\"sequence\":1},\"outcome\":\"succeeded\",\"snapshotRevision\":1,\"payload\":{\"type\":\"Int32\",\"value\":1}}]");
            var command = MinimalCaseJson().Replace(
                "\"expect\":{}",
                "\"expect\":{\"commands\":{\"match\":\"exact\",\"records\":[{\"phase\":\"execute\",\"typeId\":1,\"typeVersion\":1,\"treeInstanceId\":7,\"sequence\":1,\"payload\":{\"type\":\"Int32\",\"value\":1}}]}}");

            AssertCode(completion, BehaviorCaseJsonDiagnosticCodes.SchemaViolation);
            AssertCode(command, BehaviorCaseJsonDiagnosticCodes.SchemaViolation);
        }

        [TestCase("{\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0,\"events\":[],\"expect\":{}}", "{\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":1,\"events\":[],\"expect\":{}}", TestName = "EqualUpdateIds")]
        [TestCase("{\"updateId\":2,\"snapshotRevision\":2,\"timeMicroseconds\":0,\"events\":[],\"expect\":{}}", "{\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":1,\"events\":[],\"expect\":{}}", TestName = "DecreasingUpdateIds")]
        public void UpdateIds_MustStrictlyIncrease(string first, string second)
        {
            var json = StepsCase("{\"operation\":\"update\"," + first.Substring(1), "{\"operation\":\"update\"," + second.Substring(1));
            AssertCode(json, BehaviorCaseJsonDiagnosticCodes.SemanticViolation, "/steps/1/updateId");
        }

        [Test]
        public void SourceSequenceGaps_AreAcceptedButNonIncreasingRecordsAreRejected()
        {
            var valid = MinimalCaseJson().Replace(
                "\"events\":[]",
                "\"events\":[" + Event(9, 2) + "," + Event(9, 20) + "]");
            var invalid = MinimalCaseJson().Replace(
                "\"events\":[]",
                "\"events\":[" + Event(9, 2) + "," + Event(9, 2) + "]");

            Assert.That(BehaviorCaseJson.Parse(valid).Success, Is.True);
            AssertCode(invalid, BehaviorCaseJsonDiagnosticCodes.SemanticViolation, "/steps/0/events/1/sourceSequence");
        }

        [Test]
        public void Restart_IsNotPrevalidatedAgainstPredictedRuntimeState()
        {
            var read = BehaviorCaseJson.Parse(StepsCase("{\"operation\":\"restart\",\"expect\":{\"progress\":\"rejected\"}}"));

            Assert.That(read.Success, Is.True, Messages(read.Diagnostics));
        }

        [Test]
        public void Tolerance_IsTypedAndOnlyPermittedForFloatingPointExpectations()
        {
            var integer = MinimalCaseJson().Replace(
                "\"expect\":{}",
                "\"expect\":{\"blackboard\":[{\"key\":\"x\",\"value\":{\"type\":\"Int32\",\"value\":1},\"absoluteTolerance\":0.1}]}");
            var floating = MinimalCaseJson().Replace(
                "\"expect\":{}",
                "\"expect\":{\"blackboard\":[{\"key\":\"x\",\"value\":{\"type\":\"Float32\",\"value\":1},\"absoluteTolerance\":0.1}]}");

            AssertCode(integer, BehaviorCaseJsonDiagnosticCodes.SemanticViolation);
            Assert.That(BehaviorCaseJson.Parse(floating).Success, Is.True);
        }

        [Test]
        public void TraceFields_AreClosedAndStronglyTyped()
        {
            var wrongType = MinimalCaseJson().Replace(
                "\"expect\":{}",
                "\"expect\":{\"trace\":[{\"event\":\"node-entered\",\"nodeIndex\":\"0\"}]}");
            var unknown = MinimalCaseJson().Replace(
                "\"expect\":{}",
                "\"expect\":{\"trace\":[{\"event\":\"node-entered\",\"predicate\":\"anything\"}]}");

            AssertCode(wrongType, BehaviorCaseJsonDiagnosticCodes.SchemaViolation);
            AssertCode(unknown, BehaviorCaseJsonDiagnosticCodes.SchemaViolation);
        }

        [TestCase("{\"format\":\"aibt.case\",\"format\":\"aibt.case\"}", "AIBT9003")]
        [TestCase("{\"format\":\"aibt.case\",}", "AIBT9002")]
        [TestCase("{/*comment*/\"format\":\"aibt.case\"}", "AIBT9002")]
        [TestCase("{\"format\":\"aibt.case\",\"formatVersion\":1,\"name\":\"\\uD800\",\"tree\":\"t\",\"treeInstanceId\":1,\"rootSeed\":0,\"steps\":[{\"operation\":\"abort\"}]}", "AIBT9001")]
        public void StrictJsonReader_RejectsNonCanonicalSyntax(string json, string expectedCode)
        {
            AssertCode(json, new DiagnosticCode(expectedCode));
        }

        [Test]
        public void EscapedBackslashBeforeUnicodeText_IsNotTreatedAsUnicodeEscape()
        {
            var json = MinimalCaseJson().Replace("\"minimal\"", "\"\\\\uD800\"");

            var result = BehaviorCaseJson.Parse(json);

            Assert.That(result.Success, Is.True, Messages(result.Diagnostics));
            Assert.That(result.Document.Name, Is.EqualTo("\\uD800"));
        }

        [Test]
        public void AssetId_RejectsNonCanonicalUppercaseGuid()
        {
            var json = MinimalCaseJson().Replace(
                "\"initialBlackboard\":{}",
                "\"initialBlackboard\":{\"asset\":{\"type\":\"AssetId\",\"value\":{\"guid\":\"00112233445566778899AABBCCDDEEFF\"}}}");

            AssertCode(json, BehaviorCaseJsonDiagnosticCodes.SchemaViolation);
        }

        [Test]
        public void Utf8Reader_RejectsBomAndInvalidByteSequences()
        {
            AssertCode(new byte[] { 0xef, 0xbb, 0xbf, (byte)'{', (byte)'}' }, BehaviorCaseJsonDiagnosticCodes.InvalidUtf8);
            AssertCode(new byte[] { 0xc3, 0x28 }, BehaviorCaseJsonDiagnosticCodes.InvalidUtf8);
        }

        [Test]
        public void Schema_ExposesOnlyClosedTypedAssertions()
        {
            var path = BehaviorCaseTestPackagePaths.Resolve("Schemas~", "behavior-case.schema.json");
            var schema = JObject.Parse(File.ReadAllText(path));
            var defs = (JObject)schema["$defs"];

            Assert.That(schema["additionalProperties"].Value<bool>(), Is.False);
            Assert.That(defs["traceExpectation"]["additionalProperties"].Value<bool>(), Is.False);
            Assert.That(defs["diagnosticExpectation"]["additionalProperties"].Value<bool>(), Is.False);
            Assert.That(defs["invariant"]["additionalProperties"].Value<bool>(), Is.False);
            Assert.That(defs["registeredValue"]["properties"]["encoding"]["const"].Value<string>(), Is.EqualTo("base64"));
            Assert.That(schema.SelectTokens("$..assertions").Any(), Is.False);
            Assert.That(schema.SelectTokens("$..predicate").Any(), Is.False);
            CollectionAssert.AreEquivalent(
                new[] { "operation", "updateId", "snapshotRevision", "timeMicroseconds" },
                defs["abort"]["required"].Values<string>());
            Assert.That(defs["abort"]["properties"]["completions"], Is.Not.Null);
            var maximumUInt64 = ulong.MaxValue.ToString(CultureInfo.InvariantCulture);
            Assert.That(defs["abort"]["properties"]["stepBudget"]["maximum"].ToString(Formatting.None), Is.EqualTo(maximumUInt64));
            Assert.That(defs["expect"]["properties"]["executedSteps"]["maximum"].ToString(Formatting.None), Is.EqualTo(maximumUInt64));
            Assert.That(defs["update"]["properties"]["stepBudget"]["maximum"].ToString(Formatting.None), Is.EqualTo(maximumUInt64));
            CollectionAssert.AreEquivalent(new[] { "success", "failure" },
                defs["expect"]["properties"]["rootStatus"]["enum"].Values<string>());
            Assert.That(defs["traceExpectation"]["properties"]["treeSemanticHash"]["pattern"].Value<string>(),
                Is.EqualTo("^[0-9a-f]{64}$"));
            Assert.That(schema.Descendants().OfType<JObject>()
                .Where(x => x["type"]?.Type == JTokenType.String && x["type"].Value<string>() == "integer")
                .All(x => x["const"] != null || x["maximum"] != null), Is.True,
                "Every unconstrained persisted integer requires an explicit upper bound.");
        }

        [TestCase("{\"operation\":\"abort\"}", "/steps/0")]
        [TestCase("{\"operation\":\"abort\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0,\"future\":true}", "/steps/0/future")]
        [TestCase("{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0,\"expect\":{\"diagnostics\":[{\"code\":\"AIBT4001\",\"severity\":\"error\",\"documentId\":\"\"}]}}", "/steps/0/expect/diagnostics/0/documentId")]
        [TestCase("{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0,\"expect\":{\"diagnostics\":[{\"code\":\"AIBT4001\",\"severity\":\"error\",\"jsonPointer\":\"bad\"}]}}", "/steps/0/expect/diagnostics/0/jsonPointer")]
        public void CodecAndSchemaClosedFieldsRejectWithStablePointers(string step, string pointer)
        {
            AssertCode(StepsCase(step), BehaviorCaseJsonDiagnosticCodes.SchemaViolation, pointer);
        }

        [TestCase("positive-minimal.aibtcase.json")]
        [TestCase("cancellation.aibtcase.json")]
        [TestCase("budget-resume.aibtcase.json")]
        public void CanonicalPositiveFixtures_RoundTripByteExactly(string fileName)
        {
            var path = BehaviorCaseTestPackagePaths.Resolve("Tests", "Fixtures", "Cases", fileName);
            var source = File.ReadAllBytes(path);
            var read = BehaviorCaseJson.Parse(source, fileName);
            var write = read.Success ? BehaviorCaseJson.Serialize(read.Document) : null;

            Assert.That(read.Success, Is.True, Messages(read.Diagnostics));
            Assert.That(write.Success, Is.True, Messages(write.Diagnostics));
            Assert.That(write.CopyUtf8(), Is.EqualTo(source));
        }

        [Test]
        public void CanonicalNegativeFixture_HasStableSemanticLocation()
        {
            var path = BehaviorCaseTestPackagePaths.Resolve(
                "Tests", "Fixtures", "Cases", "negative-update-order.aibtcase.json");
            var read = BehaviorCaseJson.Parse(File.ReadAllBytes(path), "negative-update-order.aibtcase.json");

            Assert.That(read.Success, Is.False);
            Assert.That(read.Diagnostics.Single().Code, Is.EqualTo(BehaviorCaseJsonDiagnosticCodes.SemanticViolation));
            Assert.That(read.Diagnostics.Single().Location.JsonPointer, Is.EqualTo("/steps/1/updateId"));
        }

        [Test]
        public void AbortCarriesExplicitContextAndCompletionsAcrossRoundTrip()
        {
            var json = StepsCase(
                "{\"operation\":\"abort\",\"updateId\":9,\"snapshotRevision\":4,\"timeMicroseconds\":-3,\"stepBudget\":5," +
                "\"completions\":[{\"sourceId\":2,\"sourceSequence\":7," +
                "\"operationId\":{\"treeInstanceId\":7,\"nodeIndex\":0,\"activationGeneration\":1,\"sequence\":1}," +
                "\"outcome\":\"cancelled\",\"snapshotRevision\":4}]}" );

            var read = BehaviorCaseJson.Parse(json);
            var abort = (BehaviorCaseAbortStep)read.Document.Steps.Single();
            var reread = BehaviorCaseJson.Parse(BehaviorCaseJson.Serialize(read.Document).CopyUtf8());

            Assert.That(read.Success, Is.True, Messages(read.Diagnostics));
            Assert.That(abort.UpdateId, Is.EqualTo(9));
            Assert.That(abort.SnapshotRevision, Is.EqualTo(new Revision(4)));
            Assert.That(abort.TimeMicroseconds, Is.EqualTo(-3));
            Assert.That(abort.StepBudget, Is.EqualTo(5));
            Assert.That(abort.Completions.Single().Outcome, Is.EqualTo(CompletionOutcome.Cancelled));
            Assert.That(reread.Success, Is.True, Messages(reread.Diagnostics));
        }

        [Test]
        public void RootStatusIsTerminalOnlyInJsonAndProgrammaticModel()
        {
            AssertCode(
                StepsCase("{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0,\"expect\":{\"rootStatus\":\"running\"}}"),
                BehaviorCaseJsonDiagnosticCodes.SchemaViolation,
                "/steps/0/expect/rootStatus");
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BehaviorCaseExpectation(rootStatus: NodeStatus.Running));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BehaviorCaseExecutorStepResult(BehaviorCaseProgress.Waiting, NodeStatus.Running, 0));
        }

        [Test]
        public void AbortBudgetAllowsResumeButUnbudgetedAbortDoesNot()
        {
            var budgeted = StepsCase(
                "{\"operation\":\"abort\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0,\"stepBudget\":0}",
                "{\"operation\":\"resume\"}");
            var unbudgeted = StepsCase(
                "{\"operation\":\"abort\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0}",
                "{\"operation\":\"resume\"}");

            Assert.That(BehaviorCaseJson.Parse(budgeted).Success, Is.True);
            AssertCode(unbudgeted, BehaviorCaseJsonDiagnosticCodes.SemanticViolation, "/steps/1");
        }

        [Test]
        public void StepCountsAndBudgetsUseUnsigned64BitRange()
        {
            var json = MinimalCaseJson()
                .Replace("\"events\":[],", "\"stepBudget\":18446744073709551615,")
                .Replace("\"expect\":{}", "\"expect\":{\"executedSteps\":18446744073709551615}");

            var read = BehaviorCaseJson.Parse(json);
            var update = (BehaviorCaseUpdateStep)read.Document.Steps.Single();

            Assert.That(read.Success, Is.True, Messages(read.Diagnostics));
            Assert.That(update.StepBudget, Is.EqualTo(ulong.MaxValue));
            Assert.That(update.Expectation.ExecutedSteps, Is.EqualTo(ulong.MaxValue));
            Assert.That(BehaviorCaseJson.Parse(BehaviorCaseJson.Serialize(read.Document).CopyUtf8()).Success, Is.True);
        }

        [TestCase("{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0,\"stepBudget\":18446744073709551616}", "/steps/0/stepBudget")]
        [TestCase("{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0,\"events\":[{\"sourceId\":1,\"sourceSequence\":0,\"eventTypeId\":1,\"eventTypeVersion\":4294967296,\"payload\":{\"type\":\"Bool\",\"value\":true}}]}", "/steps/0/events/0/eventTypeVersion")]
        public void NumericOverflowHasExactFieldPointer(string step, string pointer)
        {
            AssertCode(StepsCase(step), BehaviorCaseJsonDiagnosticCodes.SchemaViolation, pointer);
        }

        [Test]
        public void SharedSourceSequenceNamespaceCoversEventsCompletionsAndAbort()
        {
            var operation = "{\"treeInstanceId\":7,\"nodeIndex\":0,\"activationGeneration\":1,\"sequence\":1}";
            var duplicateAcrossKinds = StepsCase(
                "{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0," +
                "\"events\":[" + Event(5, 2) + "]," +
                "\"completions\":[{\"sourceId\":5,\"sourceSequence\":2,\"operationId\":" + operation + ",\"outcome\":\"failed\",\"snapshotRevision\":1}]}");
            var decreasingAtAbort = StepsCase(
                "{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0,\"events\":[" + Event(5, 8) + "]}",
                "{\"operation\":\"abort\",\"updateId\":2,\"snapshotRevision\":1,\"timeMicroseconds\":1," +
                "\"completions\":[{\"sourceId\":5,\"sourceSequence\":7,\"operationId\":" + operation + ",\"outcome\":\"failed\",\"snapshotRevision\":1}]}");
            var distinctSources = duplicateAcrossKinds.Replace("\"sourceId\":5,\"sourceSequence\":2,\"operationId\"", "\"sourceId\":6,\"sourceSequence\":2,\"operationId\"");

            AssertCode(duplicateAcrossKinds, BehaviorCaseJsonDiagnosticCodes.SemanticViolation,
                "/steps/0/completions/0/sourceSequence");
            AssertCode(decreasingAtAbort, BehaviorCaseJsonDiagnosticCodes.SemanticViolation,
                "/steps/1/completions/0/sourceSequence");
            Assert.That(BehaviorCaseJson.Parse(distinctSources).Success, Is.True);
        }

        [Test]
        public void ProgrammaticPayloadContractsAreClosedBeforeSerialization()
        {
            var operation = new OperationId(new TreeInstanceId(1), new RuntimeNodeIndex(0), 1, 1);

            Assert.Throws<ArgumentException>(() => new BehaviorCaseCompletion(
                1, 1, operation, CompletionOutcome.Succeeded, new Revision(1),
                BehaviorCaseValue.BuiltIn(BlackboardValue.FromInt32(1))));
            Assert.Throws<ArgumentException>(() => new BehaviorCaseCommandExpectation(
                CommandPhase.Execute, new CommandType(1, 1), new TreeInstanceId(1), 1, operation,
                BehaviorCaseValue.BuiltIn(BlackboardValue.FromInt32(1))));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCaseCompletion(
                1, 1, operation, (CompletionOutcome)255, new Revision(1), null));
        }

        private static string FullCaseJson()
        {
            return "{" +
                "\"format\":\"aibt.case\",\"formatVersion\":1,\"name\":\"full\",\"description\":\"typed\",\"tree\":\"Trees/test.aibt.json\",\"treeInstanceId\":7,\"rootSeed\":42," +
                "\"initialBlackboard\":{" +
                    "\"\\uD800\\uDC00\":{\"type\":\"Float32\",\"value\":-0.0}," +
                    "\"\\uE000\":{\"type\":\"Enum32\",\"contract\":\"combat.state\",\"value\":2}," +
                    "\"payload\":{\"type\":\"Registered\",\"typeId\":101,\"typeVersion\":2,\"encoding\":\"base64\",\"value\":\"AQID\"}}," +
                "\"steps\":[" +
                    "{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":2,\"timeMicroseconds\":100,\"stepBudget\":1," +
                        "\"events\":[" + Event(4, 5) + "]," +
                        "\"completions\":[{\"sourceId\":4,\"sourceSequence\":8,\"operationId\":{\"treeInstanceId\":7,\"nodeIndex\":0,\"activationGeneration\":1,\"sequence\":9},\"outcome\":\"succeeded\",\"snapshotRevision\":2,\"payload\":{\"type\":\"Registered\",\"typeId\":101,\"typeVersion\":2,\"encoding\":\"base64\",\"value\":\"AQID\"}}]," +
                        "\"expect\":{\"progress\":\"suspended\",\"executedSteps\":1,\"trace\":[{\"event\":\"node-exited\",\"updateId\":1,\"snapshotRevision\":2,\"nodeIndex\":0,\"status\":\"success\",\"exitReason\":\"success\"}],\"invariants\":[{\"kind\":\"no-error-diagnostics\"}]}} ," +
                    "{\"operation\":\"resume\",\"stepBudget\":2,\"expect\":{\"progress\":\"completed\"}}," +
                    "{\"operation\":\"update\",\"updateId\":2,\"snapshotRevision\":2,\"timeMicroseconds\":101,\"events\":[],\"expect\":{}}]," +
                "\"tags\":[\"z\",\"a\"]}";
        }

        private static string MinimalCaseJson()
        {
            return "{\"format\":\"aibt.case\",\"formatVersion\":1,\"name\":\"minimal\",\"tree\":\"tree.aibt.json\",\"treeInstanceId\":7,\"rootSeed\":0,\"initialBlackboard\":{},\"steps\":[{\"operation\":\"update\",\"updateId\":1,\"snapshotRevision\":1,\"timeMicroseconds\":0,\"events\":[],\"expect\":{}}]}";
        }

        private static string StepsCase(params string[] steps)
        {
            return "{\"format\":\"aibt.case\",\"formatVersion\":1,\"name\":\"steps\",\"tree\":\"tree.aibt.json\",\"treeInstanceId\":7,\"rootSeed\":0,\"steps\":[" + string.Join(",", steps) + "]}";
        }

        private static string Event(ulong source, ulong sequence)
        {
            return "{\"sourceId\":" + source.ToString(CultureInfo.InvariantCulture) + ",\"sourceSequence\":" + sequence.ToString(CultureInfo.InvariantCulture) + ",\"eventTypeId\":11,\"eventTypeVersion\":1,\"payload\":{\"type\":\"Bool\",\"value\":true}}";
        }

        private static void AssertCode(string json, DiagnosticCode code, string pointer = null)
        {
            var result = BehaviorCaseJson.Parse(json, "invalid.aibtcase.json");
            Assert.That(result.Success, Is.False);
            Assert.That(result.Document, Is.Null);
            Assert.That(result.Diagnostics[0].Code, Is.EqualTo(code), Messages(result.Diagnostics));
            if (pointer != null) Assert.That(result.Diagnostics[0].Location.JsonPointer, Is.EqualTo(pointer));
        }

        private static void AssertCode(byte[] utf8, DiagnosticCode code)
        {
            var result = BehaviorCaseJson.Parse(utf8, "invalid.aibtcase.json");
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics[0].Code, Is.EqualTo(code), Messages(result.Diagnostics));
        }

        private static string Messages(DiagnosticCollection diagnostics)
        {
            return string.Join(" | ", diagnostics.Select(x => x.Code.Value + ": " + x.Message));
        }
    }
}
