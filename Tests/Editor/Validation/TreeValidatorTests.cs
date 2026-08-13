using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using AIBT.Authoring;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Editor.Validation
{
    public sealed class TreeValidatorTests
    {
        private AIBT.Authoring.NodeRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            var result = NodeRegistryBuilder.CreateWithBuiltIns().Build();
            Assert.That(result.Success, Is.True);
            _registry = result.Registry;
        }

        [Test]
        public void Validate_AcceptedMinimalTree_HasNoDiagnostics()
        {
            var document = Document(new[] { Node("root", BuiltInNodeManifests.MemorySequenceTypeId) });

            var diagnostics = TreeValidator.Validate(document, _registry);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void Validate_InvalidRepresentableDocument_AccumulatesIndependentDiagnosticsWithoutThrowing()
        {
            var invalid = new NodeDocument(default, null, 0, new[] { default(NodeId) },
                new SemanticObject(new SemanticProperty[] { null, new SemanticProperty(null, null) }));
            var document = new TreeDocument(null, 99, default, null, default,
                new NodeDocument[] { null, invalid },
                new BlackboardKeyDefinition[] { null });

            DiagnosticCollection diagnostics = null;
            Assert.DoesNotThrow(() => diagnostics = TreeValidator.Validate(document, null));

            Assert.That(diagnostics.Count, Is.GreaterThanOrEqualTo(7));
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.InvalidFormat);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.UnsupportedFormatVersion);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.InvalidTreeIdentity);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.InvalidRoot);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.InvalidDocument);
        }

        [Test]
        public void Validate_DuplicateMissingCycleAndUnreachableNodes_ReportsEverySafeGraphIssue()
        {
            var root = Node("root", BuiltInNodeManifests.MemorySequenceTypeId, children: new[] { Id("cycle"), Id("missing") });
            var cycle = Node("cycle", BuiltInNodeManifests.MemorySequenceTypeId, children: new[] { Id("root") });
            var unreachable = Node("unreachable", BuiltInNodeManifests.MemorySequenceTypeId);
            var duplicateA = Node("duplicate", BuiltInNodeManifests.MemorySelectorTypeId);
            var duplicateB = Node("duplicate", BuiltInNodeManifests.MemorySequenceTypeId);
            var document = Document(new[] { unreachable, duplicateA, root, cycle, duplicateB });

            var diagnostics = TreeValidator.Validate(document, _registry);

            AssertCode(diagnostics, TreeValidationDiagnosticCodes.DuplicateNodeIdentity);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.MissingChild);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.Cycle);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.UnreachableNode);
        }

        [Test]
        public void Validate_SameSemanticNodeMapInDifferentInsertionOrder_ProducesIdenticalDiagnostics()
        {
            var a = Node("a", "missing.type", typeVersion: 2, children: new[] { Id("missing") });
            var b = Node("b", BuiltInNodeManifests.InverterTypeId);

            var first = TreeValidator.Validate(Document(new[] { a, b }, Id("a")), _registry);
            var second = TreeValidator.Validate(Document(new[] { b, a }, Id("a")), _registry);

            Assert.That(second.Count, Is.EqualTo(first.Count));
            for (var index = 0; index < first.Count; index++)
            {
                Assert.That(second[index], Is.EqualTo(first[index]));
            }
        }

        [Test]
        public void Validate_UnknownTypeAndVersion_AreSeparateDiagnosticsAtExactPointers()
        {
            var unknown = Node("unknown", "game.unknown", 1);
            var old = Node("old", BuiltInNodeManifests.MemorySequenceTypeId, 2);
            var root = Node("root", BuiltInNodeManifests.MemorySequenceTypeId, children: new[] { Id("unknown"), Id("old") });

            var diagnostics = TreeValidator.Validate(Document(new[] { root, unknown, old }), _registry);

            AssertPointer(diagnostics, TreeValidationDiagnosticCodes.UnknownNodeType, "/nodes/unknown/type");
            AssertPointer(diagnostics, TreeValidationDiagnosticCodes.UnsupportedNodeVersion, "/nodes/old/typeVersion");
        }

        [Test]
        public void Validate_ChildPolicyAndDuplicateChildren_AreRejected()
        {
            var child = Node("child", BuiltInNodeManifests.MemorySequenceTypeId);
            var root = Node("root", BuiltInNodeManifests.InverterTypeId,
                children: new[] { Id("child"), Id("child") });

            var diagnostics = TreeValidator.Validate(Document(new[] { root, child }), _registry);

            AssertCode(diagnostics, TreeValidationDiagnosticCodes.ChildPolicy);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.DuplicateChild);
        }

        [Test]
        public void Validate_Parameters_RejectUnknownMissingWrongTypeAndFailedCondition()
        {
            var parameters = Parameters(
                Property("policy", SemanticValue.FromString("threshold")),
                Property("successThreshold", SemanticValue.FromString("1")),
                Property("unknown", SemanticValue.FromBoolean(true)));
            var child = Node("child", BuiltInNodeManifests.MemorySequenceTypeId);
            var root = Node("root", BuiltInNodeManifests.ParallelTypeId,
                children: new[] { Id("child") }, parameters: parameters);

            var diagnostics = TreeValidator.Validate(Document(new[] { root, child }), _registry);

            AssertPointer(diagnostics, TreeValidationDiagnosticCodes.UnknownParameter, "/nodes/root/parameters/unknown");
            AssertPointer(diagnostics, TreeValidationDiagnosticCodes.MissingParameter, "/nodes/root/parameters/failureThreshold");
            AssertPointer(diagnostics, TreeValidationDiagnosticCodes.MissingParameter, "/nodes/root/parameters/tieBreak");
            AssertPointer(diagnostics, TreeValidationDiagnosticCodes.ParameterType, "/nodes/root/parameters/successThreshold");
        }

        [Test]
        public void Validate_ParallelThresholdCrossFieldRules_AreRejected()
        {
            var child = Node("child", BuiltInNodeManifests.MemorySequenceTypeId);
            var parameters = Parameters(
                Property("policy", SemanticValue.FromString("threshold")),
                Property("successThreshold", SemanticValue.FromUInt64(2)),
                Property("failureThreshold", SemanticValue.FromUInt64(1)),
                Property("tieBreak", SemanticValue.FromString("success-first")));
            var root = Node("root", BuiltInNodeManifests.ParallelTypeId,
                children: new[] { Id("child") }, parameters: parameters);

            var diagnostics = TreeValidator.Validate(Document(new[] { root, child }), _registry);

            AssertCode(diagnostics, TreeValidationDiagnosticCodes.ParallelThreshold);
        }

        [Test]
        public void Validate_BlackboardContractAndPhase1Scopes_AreReportedTogether()
        {
            var keys = new[]
            {
                Key("agent", "same", BlackboardScope.Agent),
                Key("shared", "same", BlackboardScope.Shared),
                Key("duplicate", "name", BlackboardScope.Tree),
                Key("duplicate", "other", BlackboardScope.Tree),
            };

            var diagnostics = TreeValidator.Validate(
                Document(new[] { Node("root", BuiltInNodeManifests.MemorySequenceTypeId) }, blackboard: keys),
                _registry);

            AssertCode(diagnostics, BlackboardDiagnosticCodes.InvalidKeyId);
            Assert.That(diagnostics.Count(item => item.Code == TreeValidationDiagnosticCodes.UnsupportedBlackboardScope), Is.EqualTo(2));
        }

        [Test]
        public void Validate_RegisteredBlackboardTypeWithoutRuntimeSchemaBindingIsDiagnostic()
        {
            const string typeId = "game.target-data";
            var descriptor = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64(typeId), 1, 8, 8,
                StableHash.Fnv1A64(typeId + ".equals"), StableHash.Fnv1A64(typeId + ".schema"));
            var key = new BlackboardKeyDefinition(
                "target", "target", BlackboardTypeReference.Registered(typeId, descriptor));

            var diagnostics = TreeValidator.Validate(
                Document(new[] { Node("root", BuiltInNodeManifests.MemorySequenceTypeId) }, blackboard: new[] { key }),
                _registry);

            AssertCode(diagnostics, BlackboardDiagnosticCodes.MissingCanonicalSchema);
        }

        [Test]
        public void Validate_Observer_RequiresReactiveContextAndDeclaredTreeKeys()
        {
            var watched = Key("agent-key", "agentKey", BlackboardScope.Agent);
            var condition = new NodeDocument(
                Id("condition"),
                BuiltInNodeManifests.MemorySequenceTypeId,
                1,
                observer: new NodeObserver("lower-priority", new[] { "agent-key", "missing", "missing" }));
            var root = Node("root", BuiltInNodeManifests.ReactiveSequenceTypeId, children: new[] { Id("condition") });

            var diagnostics = TreeValidator.Validate(Document(new[] { root, condition }, blackboard: new[] { watched }), _registry);

            AssertCode(diagnostics, TreeValidationDiagnosticCodes.InvalidObserverContext);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.InvalidWatchedKey);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.DuplicateWatchedKey);
        }

        [Test]
        public void Validate_ObserverRequiresConditionKindButAcceptsDeclaredCondition()
        {
            var key = Key("ready", "ready", BlackboardScope.Tree);
            var conditionManifest = Manifest(
                "game.ready-condition", NodeExecutionDomain.Burst, true,
                new[] { "ready" }, Array.Empty<string>(), NodeBehaviorKind.Condition);
            var condition = new NodeDocument(
                Id("condition"), conditionManifest.TypeId, 1,
                observer: new NodeObserver("self", new[] { "ready" }));
            var root = Node("root", BuiltInNodeManifests.ReactiveSequenceTypeId,
                children: new[] { Id("condition") });
            var registry = NodeRegistryBuilder.CreateWithBuiltIns()
                .AddUserExtension(conditionManifest).Build().Registry;

            var accepted = TreeValidator.Validate(Document(new[] { root, condition }, blackboard: new[] { key }), registry);
            var invalid = TreeValidator.Validate(
                Document(new[] { root, condition.WithType(BuiltInNodeManifests.MemorySequenceTypeId, 1) }, blackboard: new[] { key }),
                _registry);

            Assert.That(accepted.Any(item => item.Code == TreeValidationDiagnosticCodes.InvalidObserverContext), Is.False);
            AssertCode(invalid, TreeValidationDiagnosticCodes.InvalidObserverContext);
        }

        [Test]
        public void Validate_UnknownObserverStillReportsStructuralDiagnostics()
        {
            var unknown = new NodeDocument(Id("unknown"), "game.unknown", 1,
                observer: new NodeObserver("lower-priority", new[] { "missing" }));

            var diagnostics = TreeValidator.Validate(Document(new[] { unknown }, Id("unknown")), _registry);

            AssertCode(diagnostics, TreeValidationDiagnosticCodes.UnknownNodeType);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.InvalidObserverContext);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.InvalidWatchedKey);
        }

        [Test]
        public void Validate_DuplicateChildDoesNotAlsoMeanMultipleParents()
        {
            var child = Node("child", BuiltInNodeManifests.MemorySequenceTypeId);
            var root = Node("root", BuiltInNodeManifests.MemorySequenceTypeId,
                children: new[] { Id("child"), Id("child") });

            var diagnostics = TreeValidator.Validate(Document(new[] { root, child }), _registry);

            AssertCode(diagnostics, TreeValidationDiagnosticCodes.DuplicateChild);
            Assert.That(diagnostics.Any(item => item.Code == TreeValidationDiagnosticCodes.MultipleParents), Is.False);
        }

        [Test]
        public void Validate_UnreachablePolicyCanWarnOrAllow()
        {
            var document = Document(new[]
            {
                Node("root", BuiltInNodeManifests.MemorySequenceTypeId),
                Node("orphan", BuiltInNodeManifests.MemorySequenceTypeId),
            });

            var warning = TreeValidator.Validate(document, _registry,
                new ValidationOptions(unreachableNodes: UnreachableNodePolicy.Warning));
            var allowed = TreeValidator.Validate(document, _registry,
                new ValidationOptions(unreachableNodes: UnreachableNodePolicy.Allow));

            Assert.That(warning.Single(item => item.Code == TreeValidationDiagnosticCodes.UnreachableNode).Severity,
                Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(allowed.Any(item => item.Code == TreeValidationDiagnosticCodes.UnreachableNode), Is.False);
        }

        [Test]
        public void Validate_ProjectPolicy_EnforcesLimitsDescriptionsNamesAndForbiddenTypes()
        {
            var key = Key("target", "Bad_Name", BlackboardScope.Tree);
            var child = Node("child", BuiltInNodeManifests.MemorySequenceTypeId);
            var root = Node("root", BuiltInNodeManifests.MemorySequenceTypeId, children: new[] { Id("child") });
            var policy = new TreeValidationPolicy(
                maxTreeDepth: 1,
                maxNodesPerTree: 1,
                requireTreeDescription: true,
                requireNodeDescriptions: true,
                blackboardNaming: BlackboardNamingPolicy.CamelCase,
                forbiddenNodeTypes: new[] { BuiltInNodeManifests.MemorySequenceTypeId });

            var diagnostics = TreeValidator.Validate(
                Document(new[] { root, child }, blackboard: new[] { key }),
                _registry,
                new ValidationOptions(policy: policy));

            Assert.That(diagnostics.Count(item => item.Code == TreeValidationDiagnosticCodes.PolicyViolation),
                Is.GreaterThanOrEqualTo(6));
        }

        [Test]
        public void Validate_PerformancePoliciesAndWarningsAsErrorsAreDeterministic()
        {
            var warningCode = TreeValidationDiagnosticCodes.UnreachableNode;
            var policy = new TreeValidationPolicy(
                maxEstimatedCost: 1,
                forbidUnboundedRepeaters: true,
                warningsAsErrors: new[] { warningCode });
            var repeater = Node("root", BuiltInNodeManifests.RepeaterTypeId,
                children: new[] { Id("child") },
                parameters: Parameters(Property("stopOnFailure", SemanticValue.FromBoolean(true))));
            var child = Node("child", BuiltInNodeManifests.MemorySequenceTypeId);
            var orphan = Node("orphan", BuiltInNodeManifests.MemorySequenceTypeId);

            var diagnostics = TreeValidator.Validate(Document(new[] { repeater, child, orphan }), _registry,
                new ValidationOptions(unreachableNodes: UnreachableNodePolicy.Warning, policy: policy));

            Assert.That(diagnostics.Single(item => item.Code == warningCode).Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostics.Count(item => item.Code == TreeValidationDiagnosticCodes.PolicyViolation), Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void Validate_EventDrivenServicePolicyRejectsUncontractedServiceNodes()
        {
            var service = Manifest(
                "game.service.polling", NodeExecutionDomain.Burst, true,
                Array.Empty<string>(), Array.Empty<string>(), category: "Service");
            var registry = new NodeRegistryBuilder().AddUserExtension(service).Build().Registry;

            var diagnostics = TreeValidator.Validate(
                Document(new[] { Node("root", service.TypeId) }), registry,
                new ValidationOptions(policy: new TreeValidationPolicy(requireEventDrivenServices: true)));

            AssertCode(diagnostics, TreeValidationDiagnosticCodes.PolicyViolation);
        }

        [TestCase("valid-minimal.json", false)]
        [TestCase("invalid-graph.json", true)]
        [TestCase("invalid-parameters-observer.json", true)]
        public void ValidationFixturesAreParsedAndSnapshotClassified(string fileName, bool expectsErrors)
        {
            var path = EditorTestPackagePaths.Resolve(
                "Tests", "Fixtures", "Trees", "Validation", fileName);
            var read = CanonicalTreeJson.Parse(File.ReadAllBytes(path), fileName);
            Assert.That(read.Success, Is.True, string.Join(" | ", read.Diagnostics.Select(item => item.Message)));

            var diagnostics = TreeValidator.Validate(read.Document, _registry);
            Assert.That(diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error), Is.EqualTo(expectsErrors));
        }

        [Test]
        public void Validate_CustomManifestAccessAndCapabilities_ProduceCapabilityDiagnostics()
        {
            var manifest = Manifest(
                "game.managed-node",
                NodeExecutionDomain.Managed,
                deterministic: false,
                reads: new[] { "missing" },
                sideEffects: new[] { "game.effect" });
            var registry = new NodeRegistryBuilder().AddUserExtension(manifest).Build().Registry;
            var policy = new TreeValidationPolicy(allowSideEffects: false);

            var diagnostics = TreeValidator.Validate(
                Document(new[] { Node("root", manifest.TypeId) }),
                registry,
                new ValidationOptions(policy: policy));

            AssertCode(diagnostics, TreeValidationDiagnosticCodes.MissingBlackboardAccess);
            AssertCode(diagnostics, TreeValidationDiagnosticCodes.UnsupportedExecutionDomain);
            Assert.That(diagnostics.Count(item => item.Code == TreeValidationDiagnosticCodes.UnsupportedNodeCapability), Is.EqualTo(2));
        }

        private static TreeDocument Document(
            IEnumerable<NodeDocument> nodes,
            NodeId root = default,
            IEnumerable<BlackboardKeyDefinition> blackboard = null)
        {
            var values = nodes.ToArray();
            return new TreeDocument(
                TreeDocument.CurrentFormat,
                TreeDocument.CurrentFormatVersion,
                new TreeId("tree"),
                "Tree",
                root.IsValid ? root : Id("root"),
                values,
                blackboard);
        }

        private static NodeDocument Node(
            string id,
            string type,
            int typeVersion = 1,
            IEnumerable<NodeId> children = null,
            SemanticObject parameters = null)
        {
            return new NodeDocument(Id(id), type, typeVersion, children, parameters);
        }

        private static BlackboardKeyDefinition Key(string id, string name, BlackboardScope scope)
        {
            return new BlackboardKeyDefinition(
                id,
                name,
                BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool),
                scope,
                BlackboardDefaultValue.Bool(false));
        }

        private static NodeManifest Manifest(
            string typeId,
            NodeExecutionDomain domain,
            bool deterministic,
            IEnumerable<string> reads,
            IEnumerable<string> sideEffects,
            NodeBehaviorKind kind = NodeBehaviorKind.Action,
            string category = "Test")
        {
            return new NodeManifest(
                typeId,
                1,
                "Test node.",
                category,
                kind,
                "Use in validation tests.",
                "Do not use in production.",
                domain,
                deterministic,
                Array.Empty<NodeParameterContract>(),
                new NodeChildPolicy(0, 0, true),
                reads,
                Array.Empty<string>(),
                sideEffects,
                new[] { NodeStatus.Success },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                NodeCancellationMode.AbortOnly,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Success", "{}", "Succeeds.") });
        }

        private static SemanticObject Parameters(params SemanticProperty[] properties) => new SemanticObject(properties);

        private static SemanticProperty Property(string name, SemanticValue value) => new SemanticProperty(name, value);

        private static NodeId Id(string value) => new NodeId(value);

        private static void AssertCode(IEnumerable<Diagnostic> diagnostics, DiagnosticCode code)
        {
            Assert.That(diagnostics.Any(item => item.Code == code), Is.True, $"Expected diagnostic {code}.");
        }

        private static void AssertPointer(IEnumerable<Diagnostic> diagnostics, DiagnosticCode code, string pointer)
        {
            Assert.That(diagnostics.Any(item => item.Code == code && item.Location.JsonPointer == pointer),
                Is.True,
                $"Expected diagnostic {code} at {pointer}.");
        }
    }
}
