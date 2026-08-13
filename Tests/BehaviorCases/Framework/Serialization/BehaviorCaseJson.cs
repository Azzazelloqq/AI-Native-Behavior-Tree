using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIBT.Tests.BehaviorCases
{
    internal static class BehaviorCaseJson
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static BehaviorCaseJsonReadResult Parse(byte[] utf8, string documentId = null)
        {
            if (utf8 == null) throw new ArgumentNullException(nameof(utf8));
            try
            {
                return Parse(StrictUtf8.GetString(utf8), documentId);
            }
            catch (DecoderFallbackException)
            {
                return Failure(null, BehaviorCaseJsonDiagnostics.Create(
                    BehaviorCaseJsonDiagnosticCodes.InvalidUtf8,
                    "Input is not valid UTF-8.",
                    documentId));
            }
        }

        internal static BehaviorCaseJsonReadResult Parse(string json, string documentId = null)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (json.Length != 0 && json[0] == '\ufeff')
            {
                return Failure(json, BehaviorCaseJsonDiagnostics.Create(
                    BehaviorCaseJsonDiagnosticCodes.InvalidUtf8,
                    "A UTF-8 BOM is not permitted.", documentId, line: 1, column: 1));
            }

            if (TryFindLexicalViolation(json, out var lexicalMessage, out var line, out var column))
            {
                return Failure(json, BehaviorCaseJsonDiagnostics.Create(
                    BehaviorCaseJsonDiagnosticCodes.InvalidSyntax,
                    lexicalMessage, documentId, line: line, column: column));
            }

            if (TryFindInvalidEscapedUnicode(json, out line, out column))
            {
                return Failure(json, BehaviorCaseJsonDiagnostics.Create(
                    BehaviorCaseJsonDiagnosticCodes.InvalidUtf8,
                    "JSON contains an invalid escaped Unicode scalar sequence.",
                    documentId,
                    line: line,
                    column: column));
            }

            JToken token;
            try
            {
                using (var text = new StringReader(json))
                using (var reader = new JsonTextReader(text)
                {
                    Culture = CultureInfo.InvariantCulture,
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double,
                    SupportMultipleContent = false,
                })
                {
                    token = JToken.Load(reader, new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Load,
                    });
                    if (reader.Read()) throw new JsonReaderException("Only one JSON value is permitted.");
                }
            }
            catch (JsonReaderException exception)
            {
                var duplicate = exception.Message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0;
                return Failure(json, BehaviorCaseJsonDiagnostics.Create(
                    duplicate ? BehaviorCaseJsonDiagnosticCodes.DuplicateProperty : BehaviorCaseJsonDiagnosticCodes.InvalidSyntax,
                    duplicate ? "Duplicate object properties are not permitted." : "Invalid JSON syntax.",
                    documentId,
                    line: Positive(exception.LineNumber),
                    column: Positive(exception.LinePosition)));
            }

            try
            {
                if (!TryValidateUnicode(token))
                    throw Error(BehaviorCaseJsonDiagnosticCodes.InvalidUtf8, "JSON contains an invalid Unicode scalar sequence.", token, documentId, "");
                var document = ReadDocument(Object(token, "", documentId), documentId);
                var semanticDiagnostics = BehaviorCaseSemanticValidator.Validate(document, documentId);
                if (semanticDiagnostics.Count != 0)
                    return new BehaviorCaseJsonReadResult(null, semanticDiagnostics, json);
                return new BehaviorCaseJsonReadResult(document, DiagnosticCollection.Empty, json);
            }
            catch (BehaviorCaseJsonReadException exception)
            {
                return Failure(json, exception.Diagnostic);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is FormatException
                || exception is OverflowException)
            {
                return Failure(json, BehaviorCaseJsonDiagnostics.Create(
                    BehaviorCaseJsonDiagnosticCodes.SchemaViolation,
                    "Case value cannot be represented: " + exception.Message,
                    documentId));
            }
        }

        internal static BehaviorCaseJsonWriteResult Serialize(BehaviorCaseDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var semanticDiagnostics = BehaviorCaseSemanticValidator.Validate(document);
            if (semanticDiagnostics.Count != 0) return new BehaviorCaseJsonWriteResult(null, semanticDiagnostics);
            try
            {
                var root = WriteDocument(document);
                var builder = new StringBuilder();
                WriteCanonicalToken(builder, root, 0);
                builder.Append('\n');
                return new BehaviorCaseJsonWriteResult(StrictUtf8.GetBytes(builder.ToString()), DiagnosticCollection.Empty);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return new BehaviorCaseJsonWriteResult(null, new DiagnosticCollection(new[]
                {
                    BehaviorCaseJsonDiagnostics.Create(
                        BehaviorCaseJsonDiagnosticCodes.UnrepresentableDocument,
                        "Case cannot be serialized canonically: " + exception.Message)
                }));
            }
        }

        private static BehaviorCaseDocument ReadDocument(JObject root, string documentId)
        {
            Properties(root, "", documentId,
                Required("format", "formatVersion", "name", "tree", "treeInstanceId", "rootSeed", "steps"),
                Optional("description", "initialBlackboard", "tags"));
            if (Text(root["format"], "/format", documentId) != BehaviorCaseDocument.CurrentFormat)
                throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "The format must be 'aibt.case'.", root["format"], documentId, "/format");
            var version = Int32(root["formatVersion"], "/formatVersion", documentId);
            if (version != BehaviorCaseDocument.CurrentFormatVersion)
                throw Error(BehaviorCaseJsonDiagnosticCodes.UnsupportedVersion, "Only behavior-case format version 1 is supported.", root["formatVersion"], documentId, "/formatVersion");

            var stepsToken = Array(root["steps"], "/steps", documentId);
            if (stepsToken.Count == 0) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "At least one step is required.", stepsToken, documentId, "/steps");
            var steps = new BehaviorCaseStep[stepsToken.Count];
            for (var index = 0; index < steps.Length; index++) steps[index] = ReadStep(Object(stepsToken[index], "/steps/" + index, documentId), index, documentId);

            return new BehaviorCaseDocument(
                NonEmptyText(root["name"], "/name", documentId),
                OptionalText(root["description"], "/description", documentId),
                NonEmptyText(root["tree"], "/tree", documentId),
                new TreeInstanceId(UInt64(root["treeInstanceId"], "/treeInstanceId", documentId, nonzero: true)),
                UInt64(root["rootSeed"], "/rootSeed", documentId),
                ReadValueMap(root["initialBlackboard"], "/initialBlackboard", documentId),
                steps,
                ReadTags(root["tags"], documentId));
        }

        private static BehaviorCaseStep ReadStep(JObject step, int index, string documentId)
        {
            var pointer = "/steps/" + index;
            var operation = Text(step["operation"], pointer + "/operation", documentId);
            switch (operation)
            {
                case "update":
                    Properties(step, pointer, documentId,
                        Required("operation", "updateId", "snapshotRevision", "timeMicroseconds"),
                        Optional("stepBudget", "events", "completions", "expect"));
                    return new BehaviorCaseUpdateStep(
                        UInt64(step["updateId"], pointer + "/updateId", documentId, true),
                        new Revision(UInt64(step["snapshotRevision"], pointer + "/snapshotRevision", documentId, true)),
                        Int64(step["timeMicroseconds"], pointer + "/timeMicroseconds", documentId),
                        OptionalUInt64(step["stepBudget"], pointer + "/stepBudget", documentId),
                        ReadEvents(step["events"], pointer + "/events", documentId),
                        ReadCompletions(step["completions"], pointer + "/completions", documentId),
                        ReadExpectation(step["expect"], pointer + "/expect", documentId));
                case "resume":
                    Properties(step, pointer, documentId, Required("operation"), Optional("stepBudget", "expect"));
                    return new BehaviorCaseResumeStep(
                        OptionalUInt64(step["stepBudget"], pointer + "/stepBudget", documentId),
                        ReadExpectation(step["expect"], pointer + "/expect", documentId));
                case "restart":
                    Properties(step, pointer, documentId, Required("operation"), Optional("expect"));
                    return new BehaviorCaseControlStep(ReadExpectation(step["expect"], pointer + "/expect", documentId));
                case "abort":
                    Properties(step, pointer, documentId,
                        Required("operation", "updateId", "snapshotRevision", "timeMicroseconds"),
                        Optional("stepBudget", "completions", "expect"));
                    return new BehaviorCaseAbortStep(
                        UInt64(step["updateId"], pointer + "/updateId", documentId, true),
                        new Revision(UInt64(step["snapshotRevision"], pointer + "/snapshotRevision", documentId, true)),
                        Int64(step["timeMicroseconds"], pointer + "/timeMicroseconds", documentId),
                        OptionalUInt64(step["stepBudget"], pointer + "/stepBudget", documentId),
                        ReadCompletions(step["completions"], pointer + "/completions", documentId),
                        ReadExpectation(step["expect"], pointer + "/expect", documentId));
                default:
                    throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Unknown case operation.", step["operation"], documentId, pointer + "/operation");
            }
        }

        private static IReadOnlyDictionary<string, BehaviorCaseValue> ReadValueMap(JToken token, string pointer, string documentId)
        {
            var values = new SortedDictionary<string, BehaviorCaseValue>(AIBT.Authoring.Utf8OrdinalComparer.Instance);
            if (token == null) return values;
            var obj = Object(token, pointer, documentId);
            foreach (var property in obj.Properties())
            {
                if (property.Name.Length == 0) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Blackboard keys cannot be empty.", property.Value, documentId, pointer);
                values.Add(property.Name, ReadValue(Object(property.Value, pointer + "/" + Escape(property.Name), documentId), pointer + "/" + Escape(property.Name), documentId));
            }
            return values;
        }

        private static BehaviorCaseValue ReadValue(JObject value, string pointer, string documentId)
        {
            var type = Text(value["type"], pointer + "/type", documentId);
            if (type == "Registered")
            {
                Properties(value, pointer, documentId, Required("type", "typeId", "typeVersion", "encoding", "value"), Optional());
                if (Text(value["encoding"], pointer + "/encoding", documentId) != "base64")
                    throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Registered values require base64 encoding.", value["encoding"], documentId, pointer + "/encoding");
                var encoded = Text(value["value"], pointer + "/value", documentId);
                byte[] bytes;
                try { bytes = Convert.FromBase64String(encoded); }
                catch (FormatException) { throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Registered value is not canonical base64.", value["value"], documentId, pointer + "/value"); }
                if (Convert.ToBase64String(bytes) != encoded)
                    throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Registered value is not canonical base64.", value["value"], documentId, pointer + "/value");
                return BehaviorCaseValue.Registered(
                    UInt64(value["typeId"], pointer + "/typeId", documentId, true),
                    UInt32(value["typeVersion"], pointer + "/typeVersion", documentId, true),
                    bytes);
            }

            if (type == "Enum32")
            {
                Properties(value, pointer, documentId, Required("type", "contract", "value"), Optional());
                var contract = NonEmptyText(value["contract"], pointer + "/contract", documentId);
                return BehaviorCaseValue.Enum32(contract, Int32(value["value"], pointer + "/value", documentId));
            }

            Properties(value, pointer, documentId, Required("type", "value"), Optional());
            var payload = value["value"];
            switch (type)
            {
                case "Bool": return BehaviorCaseValue.BuiltIn(BlackboardValue.FromBool(Boolean(payload, pointer + "/value", documentId)));
                case "Int32": return BehaviorCaseValue.BuiltIn(BlackboardValue.FromInt32(Int32(payload, pointer + "/value", documentId)));
                case "Int64": return BehaviorCaseValue.BuiltIn(BlackboardValue.FromInt64(Int64(payload, pointer + "/value", documentId)));
                case "Float32": return BehaviorCaseValue.BuiltIn(BlackboardValue.FromFloat32(Float32(payload, pointer + "/value", documentId)));
                case "Float64": return BehaviorCaseValue.BuiltIn(BlackboardValue.FromFloat64(Float64(payload, pointer + "/value", documentId)));
                case "Float2":
                    var two = Vector(payload, pointer + "/value", documentId, "x", "y");
                    return BehaviorCaseValue.BuiltIn(BlackboardValue.FromFloat2(new Float2Value(two[0], two[1])));
                case "Float3":
                    var three = Vector(payload, pointer + "/value", documentId, "x", "y", "z");
                    return BehaviorCaseValue.BuiltIn(BlackboardValue.FromFloat3(new Float3Value(three[0], three[1], three[2])));
                case "Quaternion":
                    var four = Vector(payload, pointer + "/value", documentId, "x", "y", "z", "w");
                    return BehaviorCaseValue.BuiltIn(BlackboardValue.FromQuaternion(new QuaternionValue(four[0], four[1], four[2], four[3])));
                case "FixedString32": return BehaviorCaseValue.BuiltIn(BlackboardValue.FromString32(Text(payload, pointer + "/value", documentId)));
                case "FixedString64": return BehaviorCaseValue.BuiltIn(BlackboardValue.FromString64(Text(payload, pointer + "/value", documentId)));
                case "FixedString128": return BehaviorCaseValue.BuiltIn(BlackboardValue.FromString128(Text(payload, pointer + "/value", documentId)));
                case "FixedString512": return BehaviorCaseValue.BuiltIn(BlackboardValue.FromString512(Text(payload, pointer + "/value", documentId)));
                case "AgentId": return BehaviorCaseValue.BuiltIn(BlackboardValue.FromAgentId(new AgentId(UInt64(payload, pointer + "/value", documentId, true))));
                case "EntityId": return BehaviorCaseValue.BuiltIn(BlackboardValue.FromEntityId(new EntityId(UInt64(payload, pointer + "/value", documentId, true))));
                case "OperationId": return BehaviorCaseValue.BuiltIn(BlackboardValue.FromOperationId(ReadOperationId(Object(payload, pointer + "/value", documentId), pointer + "/value", documentId)));
                case "AssetId":
                    var asset = Object(payload, pointer + "/value", documentId);
                    Properties(asset, pointer + "/value", documentId, Required("guid"), Optional("localFileId"));
                    var guid = Text(asset["guid"], pointer + "/value/guid", documentId);
                    if (!IsLowerHexGuid(guid))
                        throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Asset GUID must be 32 lowercase hexadecimal characters.", asset["guid"], documentId, pointer + "/value/guid");
                    var local = asset["localFileId"] == null ? (long?)null : Int64(asset["localFileId"], pointer + "/value/localFileId", documentId);
                    return BehaviorCaseValue.BuiltIn(BlackboardValue.FromAssetId(AssetId.Parse(guid, local)));
                default:
                    throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Unknown typed value '" + type + "'.", value["type"], documentId, pointer + "/type");
            }
        }

        private static BehaviorCaseEvent[] ReadEvents(JToken token, string pointer, string documentId)
        {
            if (token == null) return System.Array.Empty<BehaviorCaseEvent>();
            var array = Array(token, pointer, documentId);
            var result = new BehaviorCaseEvent[array.Count];
            for (var index = 0; index < result.Length; index++)
            {
                var itemPointer = pointer + "/" + index;
                var item = Object(array[index], itemPointer, documentId);
                Properties(item, itemPointer, documentId, Required("sourceId", "sourceSequence", "eventTypeId", "eventTypeVersion", "payload"), Optional());
                result[index] = new BehaviorCaseEvent(
                    UInt64(item["sourceId"], itemPointer + "/sourceId", documentId, true),
                    UInt64(item["sourceSequence"], itemPointer + "/sourceSequence", documentId),
                    UInt64(item["eventTypeId"], itemPointer + "/eventTypeId", documentId, true),
                    UInt32(item["eventTypeVersion"], itemPointer + "/eventTypeVersion", documentId, true),
                    ReadValue(Object(item["payload"], itemPointer + "/payload", documentId), itemPointer + "/payload", documentId));
            }
            return result;
        }

        private static BehaviorCaseCompletion[] ReadCompletions(JToken token, string pointer, string documentId)
        {
            if (token == null) return System.Array.Empty<BehaviorCaseCompletion>();
            var array = Array(token, pointer, documentId);
            var result = new BehaviorCaseCompletion[array.Count];
            for (var index = 0; index < result.Length; index++)
            {
                var itemPointer = pointer + "/" + index;
                var item = Object(array[index], itemPointer, documentId);
                Properties(item, itemPointer, documentId, Required("sourceId", "sourceSequence", "operationId", "outcome", "snapshotRevision"), Optional("payload"));
                result[index] = new BehaviorCaseCompletion(
                    UInt64(item["sourceId"], itemPointer + "/sourceId", documentId, true),
                    UInt64(item["sourceSequence"], itemPointer + "/sourceSequence", documentId),
                    ReadOperationId(Object(item["operationId"], itemPointer + "/operationId", documentId), itemPointer + "/operationId", documentId),
                    EnumValue(item["outcome"], itemPointer + "/outcome", documentId,
                        ("succeeded", CompletionOutcome.Succeeded), ("failed", CompletionOutcome.Failed), ("cancelled", CompletionOutcome.Cancelled)),
                    new Revision(UInt64(item["snapshotRevision"], itemPointer + "/snapshotRevision", documentId, true)),
                    item["payload"] == null ? null : ReadValue(Object(item["payload"], itemPointer + "/payload", documentId), itemPointer + "/payload", documentId));
                if (result[index].Payload != null && !result[index].Payload.IsRegistered)
                    throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Completion payloads require a registered opaque contract.", item["payload"], documentId, itemPointer + "/payload");
            }
            return result;
        }

        private static BehaviorCaseExpectation ReadExpectation(JToken token, string pointer, string documentId)
        {
            if (token == null) return BehaviorCaseExpectation.Empty;
            var value = Object(token, pointer, documentId);
            Properties(value, pointer, documentId, Required(), Optional("progress", "rootStatus", "executedSteps", "blackboard", "commands", "trace", "diagnostics", "invariants"));
            return new BehaviorCaseExpectation(
                value["progress"] == null ? null : EnumValue<BehaviorCaseProgress>(value["progress"], pointer + "/progress", documentId,
                    ("completed", BehaviorCaseProgress.Completed), ("waiting", BehaviorCaseProgress.Waiting), ("suspended", BehaviorCaseProgress.Suspended), ("rejected", BehaviorCaseProgress.Rejected), ("faulted", BehaviorCaseProgress.Faulted)),
                value["rootStatus"] == null ? null : EnumValue<NodeStatus>(value["rootStatus"], pointer + "/rootStatus", documentId,
                    ("success", NodeStatus.Success), ("failure", NodeStatus.Failure)),
                value["executedSteps"] == null ? null : (ulong?)UInt64(value["executedSteps"], pointer + "/executedSteps", documentId),
                ReadBlackboardExpectations(value["blackboard"], pointer + "/blackboard", documentId),
                ReadCommands(value["commands"], pointer + "/commands", documentId),
                ReadTrace(value["trace"], pointer + "/trace", documentId),
                ReadDiagnostics(value["diagnostics"], pointer + "/diagnostics", documentId),
                ReadInvariants(value["invariants"], pointer + "/invariants", documentId),
                value["blackboard"] != null,
                value["trace"] != null,
                value["diagnostics"] != null,
                value["invariants"] != null);
        }

        private static BehaviorCaseBlackboardExpectation[] ReadBlackboardExpectations(JToken token, string pointer, string documentId)
        {
            if (token == null) return System.Array.Empty<BehaviorCaseBlackboardExpectation>();
            var array = Array(token, pointer, documentId);
            var result = new BehaviorCaseBlackboardExpectation[array.Count];
            for (var index = 0; index < result.Length; index++)
            {
                var p = pointer + "/" + index;
                var item = Object(array[index], p, documentId);
                Properties(item, p, documentId, Required("key", "value"), Optional("version", "absoluteTolerance", "relativeTolerance"));
                result[index] = new BehaviorCaseBlackboardExpectation(
                    NonEmptyText(item["key"], p + "/key", documentId),
                    ReadValue(Object(item["value"], p + "/value", documentId), p + "/value", documentId),
                    item["version"] == null ? null : (ulong?)UInt64(item["version"], p + "/version", documentId),
                    OptionalNonNegativeDouble(item["absoluteTolerance"], p + "/absoluteTolerance", documentId),
                    OptionalNonNegativeDouble(item["relativeTolerance"], p + "/relativeTolerance", documentId));
            }
            return result;
        }

        private static BehaviorCaseCommandExpectationSet ReadCommands(JToken token, string pointer, string documentId)
        {
            if (token == null) return null;
            var value = Object(token, pointer, documentId);
            Properties(value, pointer, documentId, Required("match", "records"), Optional());
            var records = Array(value["records"], pointer + "/records", documentId);
            var result = new BehaviorCaseCommandExpectation[records.Count];
            for (var index = 0; index < result.Length; index++)
            {
                var p = pointer + "/records/" + index;
                var item = Object(records[index], p, documentId);
                Properties(item, p, documentId, Required("phase", "typeId", "typeVersion", "treeInstanceId", "sequence", "payload"), Optional("operationId"));
                var payload = ReadValue(Object(item["payload"], p + "/payload", documentId), p + "/payload", documentId);
                if (!payload.IsRegistered) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Command payloads require a registered opaque contract.", item["payload"], documentId, p + "/payload");
                result[index] = new BehaviorCaseCommandExpectation(
                    EnumValue(item["phase"], p + "/phase", documentId, ("execute", CommandPhase.Execute), ("cancel", CommandPhase.Cancel)),
                    new CommandType(UInt64(item["typeId"], p + "/typeId", documentId, true), UInt32(item["typeVersion"], p + "/typeVersion", documentId, true)),
                    new TreeInstanceId(UInt64(item["treeInstanceId"], p + "/treeInstanceId", documentId, true)),
                    UInt64(item["sequence"], p + "/sequence", documentId, true),
                    item["operationId"] == null ? null : (OperationId?)ReadOperationId(Object(item["operationId"], p + "/operationId", documentId), p + "/operationId", documentId),
                    payload);
            }
            return new BehaviorCaseCommandExpectationSet(
                EnumValue(value["match"], pointer + "/match", documentId, ("exact", BehaviorCaseCommandMatch.Exact), ("ordered-subset", BehaviorCaseCommandMatch.OrderedSubset)),
                result);
        }

        private static BehaviorCaseTraceExpectation[] ReadTrace(JToken token, string pointer, string documentId)
        {
            if (token == null) return System.Array.Empty<BehaviorCaseTraceExpectation>();
            var allowed = Optional("event", "traceFormatVersion", "treeSemanticHash", "treeInstanceId", "sequence", "updateId", "snapshotRevision", "nodeIndex", "status", "exitReason", "abortReason", "sourceNodeIndex", "diagnosticCode", "stableBlackboardKeyId", "oldBlackboardVersion", "newBlackboardVersion");
            var array = Array(token, pointer, documentId);
            var result = new BehaviorCaseTraceExpectation[array.Count];
            for (var index = 0; index < result.Length; index++)
            {
                var p = pointer + "/" + index;
                var item = Object(array[index], p, documentId);
                Properties(item, p, documentId, Required("event"), allowed.Where(x => x != "event").ToArray());
                result[index] = new BehaviorCaseTraceExpectation(
                    ParseTraceEvent(Text(item["event"], p + "/event", documentId), item["event"], documentId, p + "/event"),
                    traceFormatVersion: item["traceFormatVersion"] == null ? null : (uint?)UInt32(item["traceFormatVersion"], p + "/traceFormatVersion", documentId, true),
                    treeSemanticHash: item["treeSemanticHash"] == null ? null : (CompiledHash?)ReadCompiledHash(item["treeSemanticHash"], p + "/treeSemanticHash", documentId),
                    treeInstanceId: item["treeInstanceId"] == null ? null : (TreeInstanceId?)new TreeInstanceId(UInt64(item["treeInstanceId"], p + "/treeInstanceId", documentId, true)),
                    sequence: item["sequence"] == null ? null : (ulong?)UInt64(item["sequence"], p + "/sequence", documentId, true),
                    updateId: item["updateId"] == null ? null : (ulong?)UInt64(item["updateId"], p + "/updateId", documentId, true),
                    snapshotRevision: item["snapshotRevision"] == null ? null : (Revision?)new Revision(UInt64(item["snapshotRevision"], p + "/snapshotRevision", documentId, true)),
                    nodeIndex: item["nodeIndex"] == null ? null : (RuntimeNodeIndex?)ReadNodeIndex(item["nodeIndex"], p + "/nodeIndex", documentId),
                    status: item["status"] == null ? null : (NodeStatus?)EnumValue(item["status"], p + "/status", documentId,
                        ("success", NodeStatus.Success), ("failure", NodeStatus.Failure), ("running", NodeStatus.Running)),
                    exitReason: item["exitReason"] == null ? null : (NodeExitReason?)EnumValue(item["exitReason"], p + "/exitReason", documentId,
                        ("success", NodeExitReason.Success), ("failure", NodeExitReason.Failure), ("aborted", NodeExitReason.Aborted)),
                    abortReason: item["abortReason"] == null ? null : (NodeAbortReason?)EnumValue(item["abortReason"], p + "/abortReason", documentId,
                        ("explicit", NodeAbortReason.Explicit), ("observer-self", NodeAbortReason.ObserverSelf),
                        ("observer-lower-priority", NodeAbortReason.ObserverLowerPriority), ("tree-stopped", NodeAbortReason.TreeStopped),
                        ("hot-reload", NodeAbortReason.HotReload), ("timeout", NodeAbortReason.Timeout)),
                    sourceNodeIndex: item["sourceNodeIndex"] == null ? null : (RuntimeNodeIndex?)ReadNodeIndex(item["sourceNodeIndex"], p + "/sourceNodeIndex", documentId),
                    diagnosticCode: item["diagnosticCode"] == null ? null : (DiagnosticCode?)DiagnosticCode.Parse(Text(item["diagnosticCode"], p + "/diagnosticCode", documentId)),
                    stableBlackboardKeyId: item["stableBlackboardKeyId"] == null ? null : (ulong?)UInt64(item["stableBlackboardKeyId"], p + "/stableBlackboardKeyId", documentId, true),
                    oldBlackboardVersion: item["oldBlackboardVersion"] == null ? null : (ulong?)UInt64(item["oldBlackboardVersion"], p + "/oldBlackboardVersion", documentId),
                    newBlackboardVersion: item["newBlackboardVersion"] == null ? null : (ulong?)UInt64(item["newBlackboardVersion"], p + "/newBlackboardVersion", documentId));
            }
            return result;
        }

        private static BehaviorCaseDiagnosticExpectation[] ReadDiagnostics(JToken token, string pointer, string documentId)
        {
            if (token == null) return System.Array.Empty<BehaviorCaseDiagnosticExpectation>();
            var array = Array(token, pointer, documentId);
            var result = new BehaviorCaseDiagnosticExpectation[array.Count];
            for (var index = 0; index < result.Length; index++)
            {
                var p = pointer + "/" + index;
                var item = Object(array[index], p, documentId);
                Properties(item, p, documentId, Required("code", "severity"), Optional("documentId", "jsonPointer", "treeInstanceId", "nodeId"));
                var expectedDocumentId = item["documentId"] == null
                    ? null
                    : NonEmptyText(item["documentId"], p + "/documentId", documentId);
                var expectedPointer = OptionalText(item["jsonPointer"], p + "/jsonPointer", documentId);
                if (expectedPointer != null && expectedPointer.Length != 0 && expectedPointer[0] != '/')
                    throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation,
                        "Diagnostic JSON pointer must be empty or start with '/'.",
                        item["jsonPointer"], documentId, p + "/jsonPointer");
                var location = new DiagnosticLocation(
                    expectedDocumentId,
                    expectedPointer,
                    treeInstanceId: item["treeInstanceId"] == null ? default : new TreeInstanceId(UInt64(item["treeInstanceId"], p + "/treeInstanceId", documentId, true)),
                    nodeId: item["nodeId"] == null ? default : new NodeId(NonEmptyText(item["nodeId"], p + "/nodeId", documentId)));
                result[index] = new BehaviorCaseDiagnosticExpectation(
                    DiagnosticCode.Parse(Text(item["code"], p + "/code", documentId)),
                    EnumValue(item["severity"], p + "/severity", documentId, ("error", DiagnosticSeverity.Error), ("warning", DiagnosticSeverity.Warning), ("info", DiagnosticSeverity.Info)),
                    location);
            }
            return result;
        }

        private static BehaviorCaseInvariant[] ReadInvariants(JToken token, string pointer, string documentId)
        {
            if (token == null) return System.Array.Empty<BehaviorCaseInvariant>();
            var array = Array(token, pointer, documentId);
            var result = new BehaviorCaseInvariant[array.Count];
            for (var index = 0; index < result.Length; index++)
            {
                var p = pointer + "/" + index;
                var item = Object(array[index], p, documentId);
                Properties(item, p, documentId, Required("kind"), Optional());
                result[index] = EnumValue(item["kind"], p + "/kind", documentId,
                    ("no-error-diagnostics", BehaviorCaseInvariant.NoErrorDiagnostics),
                    ("no-duplicate-command-sequences", BehaviorCaseInvariant.NoDuplicateCommandSequences),
                    ("no-active-operation-leaks", BehaviorCaseInvariant.NoActiveOperationLeaks),
                    ("terminal-root-has-no-active-nodes", BehaviorCaseInvariant.TerminalRootHasNoActiveNodes));
            }
            return result;
        }

        private static JObject WriteDocument(BehaviorCaseDocument document)
        {
            var root = new JObject
            {
                ["format"] = BehaviorCaseDocument.CurrentFormat,
                ["formatVersion"] = BehaviorCaseDocument.CurrentFormatVersion,
                ["name"] = document.Name,
            };
            if (document.Description != null) root["description"] = document.Description;
            root["tree"] = document.Tree;
            root["treeInstanceId"] = document.TreeInstanceId.Value;
            root["rootSeed"] = document.RootSeed;
            if (document.InitialBlackboard.Count != 0)
            {
                var blackboard = new JObject();
                foreach (var pair in document.InitialBlackboard) blackboard.Add(pair.Key, WriteValue(pair.Value));
                root["initialBlackboard"] = blackboard;
            }
            root["steps"] = new JArray(document.Steps.Select(WriteStep));
            if (document.Tags.Count != 0) root["tags"] = new JArray(document.Tags.OrderBy(x => x, AIBT.Authoring.Utf8OrdinalComparer.Instance));
            return root;
        }

        private static JObject WriteStep(BehaviorCaseStep step)
        {
            var value = new JObject { ["operation"] = OperationName(step.Operation) };
            if (step is BehaviorCaseUpdateStep update)
            {
                value["updateId"] = update.UpdateId;
                value["snapshotRevision"] = update.SnapshotRevision.Value;
                value["timeMicroseconds"] = update.TimeMicroseconds;
                if (update.StepBudget.HasValue) value["stepBudget"] = update.StepBudget.Value;
                if (update.Events.Count != 0) value["events"] = new JArray(update.Events.Select(WriteEvent));
                if (update.Completions.Count != 0) value["completions"] = new JArray(update.Completions.Select(WriteCompletion));
            }
            else if (step is BehaviorCaseResumeStep resume && resume.StepBudget.HasValue)
            {
                value["stepBudget"] = resume.StepBudget.Value;
            }
            else if (step is BehaviorCaseAbortStep abort)
            {
                value["updateId"] = abort.UpdateId;
                value["snapshotRevision"] = abort.SnapshotRevision.Value;
                value["timeMicroseconds"] = abort.TimeMicroseconds;
                if (abort.StepBudget.HasValue) value["stepBudget"] = abort.StepBudget.Value;
                if (abort.Completions.Count != 0)
                    value["completions"] = new JArray(abort.Completions.Select(WriteCompletion));
            }
            if (!ReferenceEquals(step.Expectation, BehaviorCaseExpectation.Empty)) value["expect"] = WriteExpectation(step.Expectation);
            return value;
        }

        private static JObject WriteEvent(BehaviorCaseEvent value) => new JObject
        {
            ["sourceId"] = value.SourceId,
            ["sourceSequence"] = value.SourceSequence,
            ["eventTypeId"] = value.TypeId,
            ["eventTypeVersion"] = value.TypeVersion,
            ["payload"] = WriteValue(value.Payload),
        };

        private static JObject WriteCompletion(BehaviorCaseCompletion value)
        {
            var result = new JObject
            {
                ["sourceId"] = value.SourceId,
                ["sourceSequence"] = value.SourceSequence,
                ["operationId"] = WriteOperationId(value.OperationId),
                ["outcome"] = value.Outcome == CompletionOutcome.Succeeded ? "succeeded" : value.Outcome == CompletionOutcome.Failed ? "failed" : "cancelled",
                ["snapshotRevision"] = value.SnapshotRevision.Value,
            };
            if (value.Payload != null) result["payload"] = WriteValue(value.Payload);
            return result;
        }

        private static JObject WriteExpectation(BehaviorCaseExpectation value)
        {
            var result = new JObject();
            if (value.Progress.HasValue) result["progress"] = ProgressName(value.Progress.Value);
            if (value.RootStatus.HasValue) result["rootStatus"] = StatusName(value.RootStatus.Value);
            if (value.ExecutedSteps.HasValue) result["executedSteps"] = value.ExecutedSteps.Value;
            if (value.HasBlackboardExpectation)
            {
                result["blackboard"] = new JArray(value.Blackboard.Select(item =>
                {
                    var obj = new JObject { ["key"] = item.Key, ["value"] = WriteValue(item.Value) };
                    if (item.Version.HasValue) obj["version"] = item.Version.Value;
                    if (item.AbsoluteTolerance.HasValue) obj["absoluteTolerance"] = item.AbsoluteTolerance.Value;
                    if (item.RelativeTolerance.HasValue) obj["relativeTolerance"] = item.RelativeTolerance.Value;
                    return obj;
                }));
            }
            if (value.Commands != null)
            {
                result["commands"] = new JObject
                {
                    ["match"] = value.Commands.Match == BehaviorCaseCommandMatch.Exact ? "exact" : "ordered-subset",
                    ["records"] = new JArray(value.Commands.Records.Select(WriteCommand)),
                };
            }
            if (value.HasTraceExpectation) result["trace"] = new JArray(value.Trace.Select(WriteTrace));
            if (value.HasDiagnosticExpectation) result["diagnostics"] = new JArray(value.Diagnostics.Select(WriteDiagnostic));
            if (value.HasInvariantExpectation) result["invariants"] = new JArray(value.Invariants.Select(x => new JObject { ["kind"] = InvariantName(x) }));
            return result;
        }

        private static JObject WriteCommand(BehaviorCaseCommandExpectation value)
        {
            var result = new JObject
            {
                ["phase"] = value.Phase == CommandPhase.Execute ? "execute" : "cancel",
                ["typeId"] = value.Type.TypeId,
                ["typeVersion"] = value.Type.Version,
                ["treeInstanceId"] = value.TreeInstanceId.Value,
                ["sequence"] = value.Sequence,
            };
            if (value.OperationId.HasValue) result["operationId"] = WriteOperationId(value.OperationId.Value);
            result["payload"] = WriteValue(value.Payload);
            return result;
        }

        private static JObject WriteTrace(BehaviorCaseTraceExpectation value)
        {
            var result = new JObject { ["event"] = TraceEventName(value.Event) };
            if (value.TraceFormatVersion.HasValue) result["traceFormatVersion"] = value.TraceFormatVersion.Value;
            if (value.TreeSemanticHash.HasValue) result["treeSemanticHash"] = value.TreeSemanticHash.Value.HexadecimalValue;
            if (value.TreeInstanceId.HasValue) result["treeInstanceId"] = value.TreeInstanceId.Value.Value;
            if (value.Sequence.HasValue) result["sequence"] = value.Sequence.Value;
            if (value.UpdateId.HasValue) result["updateId"] = value.UpdateId.Value;
            if (value.SnapshotRevision.HasValue) result["snapshotRevision"] = value.SnapshotRevision.Value.Value;
            if (value.NodeIndex.HasValue) result["nodeIndex"] = value.NodeIndex.Value.Value;
            if (value.Status.HasValue) result["status"] = StatusName(value.Status.Value);
            if (value.ExitReason.HasValue) result["exitReason"] = ExitReasonName(value.ExitReason.Value);
            if (value.AbortReason.HasValue) result["abortReason"] = AbortReasonName(value.AbortReason.Value);
            if (value.SourceNodeIndex.HasValue) result["sourceNodeIndex"] = value.SourceNodeIndex.Value.Value;
            if (value.DiagnosticCode.HasValue) result["diagnosticCode"] = value.DiagnosticCode.Value.Value;
            if (value.StableBlackboardKeyId.HasValue) result["stableBlackboardKeyId"] = value.StableBlackboardKeyId.Value;
            if (value.OldBlackboardVersion.HasValue) result["oldBlackboardVersion"] = value.OldBlackboardVersion.Value;
            if (value.NewBlackboardVersion.HasValue) result["newBlackboardVersion"] = value.NewBlackboardVersion.Value;
            return result;
        }

        private static JObject WriteDiagnostic(BehaviorCaseDiagnosticExpectation value)
        {
            var result = new JObject
            {
                ["code"] = value.Code.Value,
                ["severity"] = value.Severity == DiagnosticSeverity.Error ? "error" : value.Severity == DiagnosticSeverity.Warning ? "warning" : "info",
            };
            if (value.Location.DocumentId != null) result["documentId"] = value.Location.DocumentId;
            if (value.Location.JsonPointer != null) result["jsonPointer"] = value.Location.JsonPointer;
            if (value.Location.TreeInstanceId.IsValid) result["treeInstanceId"] = value.Location.TreeInstanceId.Value;
            if (value.Location.NodeId.IsValid) result["nodeId"] = value.Location.NodeId.Value;
            return result;
        }

        private static JObject WriteValue(BehaviorCaseValue value)
        {
            if (value.IsRegistered)
            {
                return new JObject
                {
                    ["type"] = "Registered",
                    ["typeId"] = value.RegisteredTypeId,
                    ["typeVersion"] = value.RegisteredTypeVersion,
                    ["encoding"] = "base64",
                    ["value"] = Convert.ToBase64String(value.CopyRegisteredBytes()),
                };
            }

            var builtIn = value.BuiltInValue;
            var result = new JObject { ["type"] = builtIn.Type.ToString() };
            switch (builtIn.Type)
            {
                case BlackboardValueType.Bool: builtIn.TryGetBool(out var b); result["value"] = b; break;
                case BlackboardValueType.Int32: builtIn.TryGetInt32(out var i32); result["value"] = i32; break;
                case BlackboardValueType.Int64: builtIn.TryGetInt64(out var i64); result["value"] = i64; break;
                case BlackboardValueType.Float32: builtIn.TryGetFloat32(out var f32); result["value"] = f32; break;
                case BlackboardValueType.Float64: builtIn.TryGetFloat64(out var f64); result["value"] = f64; break;
                case BlackboardValueType.Float2:
                    builtIn.TryGetFloat2(out var f2); result["value"] = new JObject { ["x"] = f2.X, ["y"] = f2.Y }; break;
                case BlackboardValueType.Float3:
                    builtIn.TryGetFloat3(out var f3); result["value"] = new JObject { ["x"] = f3.X, ["y"] = f3.Y, ["z"] = f3.Z }; break;
                case BlackboardValueType.Quaternion:
                    builtIn.TryGetQuaternion(out var q); result["value"] = new JObject { ["x"] = q.X, ["y"] = q.Y, ["z"] = q.Z, ["w"] = q.W }; break;
                case BlackboardValueType.Enum32:
                    builtIn.TryGetEnum32(out var e); result["contract"] = value.EnumContract; result["value"] = e.Value; break;
                case BlackboardValueType.FixedString32:
                case BlackboardValueType.FixedString64:
                case BlackboardValueType.FixedString128:
                case BlackboardValueType.FixedString512:
                    builtIn.TryGetFixedString(out var s); result["value"] = s; break;
                case BlackboardValueType.AgentId: builtIn.TryGetAgentId(out var agent); result["value"] = agent.Value; break;
                case BlackboardValueType.EntityId: builtIn.TryGetEntityId(out var entity); result["value"] = entity.Value; break;
                case BlackboardValueType.OperationId: builtIn.TryGetOperationId(out var operation); result["value"] = WriteOperationId(operation); break;
                case BlackboardValueType.AssetId:
                    builtIn.TryGetAssetId(out var asset);
                    var assetJson = new JObject { ["guid"] = asset.ToGuidString() };
                    if (asset.HasLocalFileId) assetJson["localFileId"] = asset.LocalFileId;
                    result["value"] = assetJson;
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
            return result;
        }

        private static OperationId ReadOperationId(JObject value, string pointer, string documentId)
        {
            Properties(value, pointer, documentId, Required("treeInstanceId", "nodeIndex", "activationGeneration", "sequence"), Optional());
            return new OperationId(
                new TreeInstanceId(UInt64(value["treeInstanceId"], pointer + "/treeInstanceId", documentId, true)),
                ReadNodeIndex(value["nodeIndex"], pointer + "/nodeIndex", documentId),
                UInt32(value["activationGeneration"], pointer + "/activationGeneration", documentId),
                UInt64(value["sequence"], pointer + "/sequence", documentId));
        }

        private static RuntimeNodeIndex ReadNodeIndex(JToken token, string pointer, string documentId)
        {
            var result = new RuntimeNodeIndex(UInt32(token, pointer, documentId));
            if (!result.IsValid)
                throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Runtime node index cannot use the invalid sentinel.", token, documentId, pointer);
            return result;
        }

        private static CompiledHash ReadCompiledHash(JToken token, string pointer, string documentId)
        {
            var value = Text(token, pointer, documentId);
            if (value.Length != CompiledHash.HexLength)
                throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected a canonical compiled hash.", token, documentId, pointer);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if ((character < '0' || character > '9') && (character < 'a' || character > 'f'))
                    throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected a canonical compiled hash.", token, documentId, pointer);
            }
            return new CompiledHash(value);
        }

        private static JObject WriteOperationId(OperationId value) => new JObject
        {
            ["treeInstanceId"] = value.TreeInstanceId.Value,
            ["nodeIndex"] = value.NodeIndex.Value,
            ["activationGeneration"] = value.ActivationGeneration,
            ["sequence"] = value.Sequence,
        };

        private static float[] Vector(JToken token, string pointer, string documentId, params string[] names)
        {
            var value = Object(token, pointer, documentId);
            Properties(value, pointer, documentId, names, Optional());
            var result = new float[names.Length];
            for (var index = 0; index < names.Length; index++) result[index] = Float32(value[names[index]], pointer + "/" + names[index], documentId);
            return result;
        }

        private static string[] ReadTags(JToken token, string documentId)
        {
            if (token == null) return System.Array.Empty<string>();
            var array = Array(token, "/tags", documentId);
            var result = new string[array.Count];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = NonEmptyText(array[index], "/tags/" + index, documentId);
                if (!seen.Add(result[index])) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Tags must be unique.", array[index], documentId, "/tags/" + index);
            }
            System.Array.Sort(result, AIBT.Authoring.Utf8OrdinalComparer.Instance);
            return result;
        }

        private static void Properties(JObject value, string pointer, string documentId, string[] required, string[] optional)
        {
            var allowed = new HashSet<string>(required, StringComparer.Ordinal);
            foreach (var name in optional) allowed.Add(name);
            foreach (var property in value.Properties())
                if (!allowed.Contains(property.Name)) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Unknown property '" + property.Name + "'.", property.Value, documentId, pointer + "/" + Escape(property.Name));
            foreach (var name in required)
                if (value.Property(name, StringComparison.Ordinal) == null) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Missing required property '" + name + "'.", value, documentId, pointer);
        }

        private static JObject Object(JToken token, string pointer, string documentId)
            => token as JObject ?? throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected an object.", token, documentId, pointer);
        private static JArray Array(JToken token, string pointer, string documentId)
            => token as JArray ?? throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected an array.", token, documentId, pointer);
        private static string Text(JToken token, string pointer, string documentId)
        {
            if (token == null || token.Type != JTokenType.String) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected a string.", token, documentId, pointer);
            return token.Value<string>();
        }
        private static string NonEmptyText(JToken token, string pointer, string documentId)
        {
            var value = Text(token, pointer, documentId);
            if (value.Length == 0) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "String cannot be empty.", token, documentId, pointer);
            return value;
        }
        private static string OptionalText(JToken token, string pointer, string documentId) => token == null ? null : Text(token, pointer, documentId);
        private static bool Boolean(JToken token, string pointer, string documentId)
        {
            if (token == null || token.Type != JTokenType.Boolean) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected a Boolean.", token, documentId, pointer);
            return token.Value<bool>();
        }
        private static int Int32(JToken token, string pointer, string documentId)
        {
            try { return checked((int)Int64(token, pointer, documentId)); }
            catch (OverflowException) { throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Integer is outside the Int32 range.", token, documentId, pointer); }
        }
        private static long Int64(JToken token, string pointer, string documentId)
        {
            if (token == null || token.Type != JTokenType.Integer) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected an integer.", token, documentId, pointer);
            if (!long.TryParse(token.ToString(Formatting.None), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var value))
                throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Integer is outside the Int64 range.", token, documentId, pointer);
            return value;
        }
        private static uint UInt32(JToken token, string pointer, string documentId, bool nonzero = false)
        {
            uint value;
            try { value = checked((uint)UInt64(token, pointer, documentId)); }
            catch (OverflowException) { throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Integer is outside the UInt32 range.", token, documentId, pointer); }
            if (nonzero && value == 0) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected a positive integer.", token, documentId, pointer);
            return value;
        }
        private static ulong UInt64(JToken token, string pointer, string documentId, bool nonzero = false)
        {
            if (token == null || token.Type != JTokenType.Integer) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected an unsigned integer.", token, documentId, pointer);
            if (!ulong.TryParse(token.ToString(Formatting.None), NumberStyles.None,
                CultureInfo.InvariantCulture, out var value))
                throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Integer is outside the UInt64 range.", token, documentId, pointer);
            if (nonzero && value == 0) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected a positive integer.", token, documentId, pointer);
            return value;
        }
        private static ulong? OptionalUInt64(JToken token, string pointer, string documentId)
            => token == null || token.Type == JTokenType.Null ? null : (ulong?)UInt64(token, pointer, documentId);
        private static float Float32(JToken token, string pointer, string documentId)
        {
            var value = (float)Float64(token, pointer, documentId);
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected a finite Float32 value.", token, documentId, pointer);
            return value == 0f ? 0f : value;
        }
        private static double Float64(JToken token, string pointer, string documentId)
        {
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
                throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected a finite number.", token, documentId, pointer);
            var value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value)) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Expected a finite number.", token, documentId, pointer);
            return value == 0d ? 0d : value;
        }
        private static double? OptionalNonNegativeDouble(JToken token, string pointer, string documentId)
        {
            if (token == null) return null;
            var value = Float64(token, pointer, documentId);
            if (value < 0) throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Tolerance cannot be negative.", token, documentId, pointer);
            return value;
        }
        private static T EnumValue<T>(JToken token, string pointer, string documentId, params (string Name, T Value)[] values)
        {
            var text = Text(token, pointer, documentId);
            for (var index = 0; index < values.Length; index++) if (values[index].Name == text) return values[index].Value;
            throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Unknown persisted enum value.", token, documentId, pointer);
        }

        private static BehaviorCaseTraceEvent ParseTraceEvent(string value, JToken token, string documentId, string pointer)
        {
            var names = new[] { "update-started", "update-completed", "node-entered", "node-ticked", "node-abort-started", "node-exited", "blackboard-changed", "observer-queued", "observer-evaluated", "command-emitted", "completion-consumed", "completion-discarded", "diagnostic-raised", "budget-yielded", "execution-resumed" };
            for (var index = 0; index < names.Length; index++) if (names[index] == value) return (BehaviorCaseTraceEvent)index;
            throw Error(BehaviorCaseJsonDiagnosticCodes.SchemaViolation, "Unknown trace event.", token, documentId, pointer);
        }

        private static string TraceEventName(BehaviorCaseTraceEvent value)
        {
            var names = new[] { "update-started", "update-completed", "node-entered", "node-ticked", "node-abort-started", "node-exited", "blackboard-changed", "observer-queued", "observer-evaluated", "command-emitted", "completion-consumed", "completion-discarded", "diagnostic-raised", "budget-yielded", "execution-resumed" };
            return names[(int)value];
        }

        private static string ExitReasonName(NodeExitReason value)
        {
            switch (value)
            {
                case NodeExitReason.Success: return "success";
                case NodeExitReason.Failure: return "failure";
                case NodeExitReason.Aborted: return "aborted";
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        private static string AbortReasonName(NodeAbortReason value)
        {
            switch (value)
            {
                case NodeAbortReason.Explicit: return "explicit";
                case NodeAbortReason.ObserverSelf: return "observer-self";
                case NodeAbortReason.ObserverLowerPriority: return "observer-lower-priority";
                case NodeAbortReason.TreeStopped: return "tree-stopped";
                case NodeAbortReason.HotReload: return "hot-reload";
                case NodeAbortReason.Timeout: return "timeout";
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        private static string OperationName(BehaviorCaseOperation value)
            => value == BehaviorCaseOperation.Update ? "update" : value == BehaviorCaseOperation.Resume ? "resume" : value == BehaviorCaseOperation.Restart ? "restart" : "abort";
        private static string ProgressName(BehaviorCaseProgress value)
            => value.ToString().ToLowerInvariant();
        private static string StatusName(NodeStatus value)
            => value.ToString().ToLowerInvariant();
        private static string InvariantName(BehaviorCaseInvariant value)
        {
            switch (value)
            {
                case BehaviorCaseInvariant.NoErrorDiagnostics: return "no-error-diagnostics";
                case BehaviorCaseInvariant.NoDuplicateCommandSequences: return "no-duplicate-command-sequences";
                case BehaviorCaseInvariant.NoActiveOperationLeaks: return "no-active-operation-leaks";
                default: return "terminal-root-has-no-active-nodes";
            }
        }
        private static string[] Required(params string[] values) => values;
        private static string[] Optional(params string[] values) => values;
        private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
        private static int? Positive(int value) => value > 0 ? value : (int?)null;

        private static bool IsLowerHexGuid(string value)
        {
            if (value == null || value.Length != 32) return false;
            for (var index = 0; index < value.Length; index++)
            {
                var c = value[index];
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            }
            return true;
        }

        private static void WriteCanonicalToken(StringBuilder builder, JToken token, int depth)
        {
            if (token is JObject obj)
            {
                builder.Append('{');
                var first = true;
                foreach (var property in obj.Properties())
                {
                    if (first) first = false; else builder.Append(',');
                    builder.Append('\n');
                    Indent(builder, depth + 1);
                    AIBT.Authoring.CanonicalJsonText.WriteString(builder, property.Name);
                    builder.Append(": ");
                    WriteCanonicalToken(builder, property.Value, depth + 1);
                }
                if (!first)
                {
                    builder.Append('\n');
                    Indent(builder, depth);
                }
                builder.Append('}');
                return;
            }

            if (token is JArray array)
            {
                builder.Append('[');
                for (var index = 0; index < array.Count; index++)
                {
                    if (index != 0) builder.Append(',');
                    builder.Append('\n');
                    Indent(builder, depth + 1);
                    WriteCanonicalToken(builder, array[index], depth + 1);
                }
                if (array.Count != 0)
                {
                    builder.Append('\n');
                    Indent(builder, depth);
                }
                builder.Append(']');
                return;
            }

            var value = token as JValue ?? throw new InvalidOperationException("Only canonical JSON values can be written.");
            switch (value.Type)
            {
                case JTokenType.String:
                    AIBT.Authoring.CanonicalJsonText.WriteString(builder, value.Value<string>());
                    break;
                case JTokenType.Boolean:
                    builder.Append(value.Value<bool>() ? "true" : "false");
                    break;
                case JTokenType.Integer:
                    builder.Append(Convert.ToString(value.Value, CultureInfo.InvariantCulture));
                    break;
                case JTokenType.Float:
                    if (value.Value is float single)
                        builder.Append(AIBT.Authoring.CanonicalJsonNumber.Format(single));
                    else
                        builder.Append(AIBT.Authoring.CanonicalJsonNumber.Format(Convert.ToDouble(value.Value, CultureInfo.InvariantCulture)));
                    break;
                case JTokenType.Null:
                    builder.Append("null");
                    break;
                default:
                    throw new InvalidOperationException("Unsupported canonical JSON token type " + value.Type + ".");
            }
        }

        private static void Indent(StringBuilder builder, int depth)
        {
            builder.Append(' ', checked(depth * 2));
        }

        private static bool TryValidateUnicode(JToken token)
        {
            if (token.Type == JTokenType.String && !ValidUnicode(token.Value<string>())) return false;
            if (token is JObject obj)
                foreach (var property in obj.Properties()) if (!ValidUnicode(property.Name) || !TryValidateUnicode(property.Value)) return false;
            if (token is JArray array)
                foreach (var child in array) if (!TryValidateUnicode(child)) return false;
            return true;
        }
        private static bool ValidUnicode(string value)
        {
            if (value == null) return false;
            for (var index = 0; index < value.Length; index++)
            {
                if (char.IsHighSurrogate(value[index]))
                {
                    if (++index >= value.Length || !char.IsLowSurrogate(value[index])) return false;
                }
                else if (char.IsLowSurrogate(value[index])) return false;
            }
            return true;
        }

        private static bool TryFindLexicalViolation(string source, out string message, out int line, out int column)
        {
            var inString = false; var escaped = false; line = 1; column = 1;
            for (var index = 0; index < source.Length; index++)
            {
                var c = source[index];
                if (!inString && c == '/' && index + 1 < source.Length && (source[index + 1] == '/' || source[index + 1] == '*')) { message = "JSON comments are not permitted."; return true; }
                if (!inString && c == ',')
                {
                    var next = index + 1;
                    while (next < source.Length && char.IsWhiteSpace(source[next])) next++;
                    if (next < source.Length && (source[next] == '}' || source[next] == ']')) { message = "Trailing commas are not permitted."; return true; }
                }
                if (c == '"' && !escaped) inString = !inString;
                if (inString && c == '\\' && !escaped) escaped = true; else escaped = false;
                if (c == '\n') { line++; column = 0; }
                column++;
            }
            message = null; return false;
        }

        private static bool TryFindInvalidEscapedUnicode(string source, out int line, out int column)
        {
            var inString = false;
            line = 1;
            column = 1;
            for (var index = 0; index < source.Length; index++)
            {
                var c = source[index];
                if (c == '"' && (index == 0 || !IsEscaped(source, index))) inString = !inString;
                if (inString && c == '\\' && !IsEscaped(source, index)
                    && index + 1 < source.Length && source[index + 1] == 'u'
                    && TryReadHexCodeUnit(source, index + 2, out var codeUnit))
                {
                    if (char.IsHighSurrogate((char)codeUnit))
                    {
                        if (index + 11 >= source.Length || source[index + 6] != '\\' || source[index + 7] != 'u'
                            || !TryReadHexCodeUnit(source, index + 8, out var low)
                            || !char.IsLowSurrogate((char)low)) return true;
                        index += 11;
                        column += 11;
                    }
                    else if (char.IsLowSurrogate((char)codeUnit)) return true;
                    else
                    {
                        index += 5;
                        column += 5;
                    }
                }

                if (c == '\n') { line++; column = 0; }
                column++;
            }

            return false;
        }

        private static bool IsEscaped(string source, int index)
        {
            var slashes = 0;
            for (var previous = index - 1; previous >= 0 && source[previous] == '\\'; previous--) slashes++;
            return (slashes & 1) != 0;
        }

        private static bool TryReadHexCodeUnit(string source, int offset, out int value)
        {
            value = 0;
            if (offset + 4 > source.Length) return false;
            for (var index = 0; index < 4; index++)
            {
                var c = source[offset + index];
                int digit;
                if (c >= '0' && c <= '9') digit = c - '0';
                else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                else return false;
                value = (value << 4) | digit;
            }
            return true;
        }

        private static BehaviorCaseJsonReadException Error(DiagnosticCode code, string message, JToken token, string documentId, string pointer)
        {
            var info = token as IJsonLineInfo;
            return new BehaviorCaseJsonReadException(BehaviorCaseJsonDiagnostics.Create(
                code, message, documentId, pointer,
                info != null && info.HasLineInfo() ? info.LineNumber : (int?)null,
                info != null && info.HasLineInfo() ? info.LinePosition : (int?)null));
        }
        private static BehaviorCaseJsonReadResult Failure(string source, Diagnostic diagnostic)
            => new BehaviorCaseJsonReadResult(null, new DiagnosticCollection(new[] { diagnostic }), source);
    }
}
