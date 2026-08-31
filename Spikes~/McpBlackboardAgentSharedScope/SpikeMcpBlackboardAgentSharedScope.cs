using System;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.McpBlackboardAgentSharedScopeSpike
{
    /// <summary>
    /// P6-014 disposable spike. Answers both open questions the card's own two investigation
    /// passes left unresolved, on real evidence -- and finds the true picture is one layer harder
    /// than either pass suspected:
    /// <list type="bullet">
    /// <item>Question 1 (does enabling Agent/Shared for MCP require touching the shared
    /// <c>ReferenceCompilationPolicy.Phase1</c> constant?): no -- it is a plain constructible object,
    /// not a hardcoded gate. But this turns out not to matter (see below).</item>
    /// <item>Question 2 (does the reference executor actually execute Agent/Shared blackboard reads/
    /// writes once validation is bypassed?): the question itself assumed compilation would succeed
    /// once <c>TreeValidator</c> accepts the document with the policy flags on. It does not:
    /// <c>ReferenceCompiler.cs</c> itself unconditionally rejects any non-Tree-scope blackboard slot
    /// with <c>AIBT3012</c> ("Phase 1 compilation supports only Tree-scope blackboard slots"),
    /// regardless of <c>SupportsAgentScope</c>/<c>SupportsSharedScope</c> -- this check does not
    /// consult the policy at all. A validated Agent/Shared-scope v2 document can never become a
    /// <c>CompiledProgram</c> today, so the runtime-storage question never even arises.</item>
    /// </list>
    /// Archived to <c>Spikes~/McpBlackboardAgentSharedScope/</c> once proven.
    /// </summary>
    public sealed class SpikeMcpBlackboardAgentSharedScope
    {
        [Test]
        public void TreeValidator_AcceptsAgentScope_OnlyWhenThePolicyOptInFlagIsSet_AndNeverTouchesPhase1()
        {
            // A distinct policy instance, never the shared Phase1 constant itself.
            var mcpBlackboardPolicy = new ReferenceCompilationPolicy(supportsAgentScope: true, supportsSharedScope: true);
            Assert.That(mcpBlackboardPolicy, Is.Not.SameAs(ReferenceCompilationPolicy.Phase1));
            Assert.That(ReferenceCompilationPolicy.Phase1.SupportsAgentScope, Is.False,
                "the shared Phase1 constant must remain completely untouched by this decision");
            Assert.That(ReferenceCompilationPolicy.Phase1.SupportsSharedScope, Is.False);

            var document = AgentScopeTree();
            var registry = ReferencePreviewFixtureEnvironment.CreateNodeRegistry();

            var acceptedOptions = mcpBlackboardPolicy.CreateValidationOptions("trees/p6-014-spike.aibt.json");
            var accepted = TreeValidator.Validate(document, registry, acceptedOptions);
            Assert.That(accepted.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False,
                string.Join(" | ", accepted.Select(d => d.Code + ": " + d.Message)));

            var rejectedOptions = ReferenceCompilationPolicy.Phase1.CreateValidationOptions("trees/p6-014-spike.aibt.json");
            var rejected = TreeValidator.Validate(document, registry, rejectedOptions);
            Assert.That(rejected.Any(d => d.Code == TreeValidationDiagnosticCodes.UnsupportedBlackboardScope), Is.True,
                "Phase1's own default validation options (flags off) must still reject an Agent-scope key");
        }

        [Test]
        public void ReferenceCompiler_RejectsAgentScope_UnconditionallyRegardlessOfThePolicyFlag()
        {
            // The real, decisive finding: even with the exact same opt-in policy TreeValidator just
            // accepted the document under, ReferenceCompiler.Compile still fails -- its own
            // Tree-scope-only check (Authoring/Compilation/ReferenceCompiler.cs) does not read
            // SupportsAgentScope/SupportsSharedScope at all, unlike TreeValidator.
            var mcpBlackboardPolicy = new ReferenceCompilationPolicy(supportsAgentScope: true, supportsSharedScope: true);
            var document = AgentScopeTree();
            var registry = ReferencePreviewFixtureEnvironment.CreateNodeRegistry();
            var options = new ReferenceCompilerOptions(
                "trees/p6-014-spike.aibt.json", mcpBlackboardPolicy, new CompiledCompilerVersion(1, 0, 0, 1));

            var compilation = ReferenceCompiler.Compile(document, registry, options);

            Assert.That(compilation.Success, Is.False,
                "confirms the compiler's own Tree-scope-only rejection is unconditional, not policy-gated");
            Assert.That(compilation.Diagnostics.Any(d => d.Code == ReferenceCompilerDiagnosticCodes.UnsupportedCapability), Is.True,
                string.Join(" | ", compilation.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        }

        private static TreeDocument AgentScopeTree()
        {
            var key = new BlackboardKeyDefinition(
                "score", "score", BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32),
                BlackboardScope.Agent, BlackboardDefaultValue.Int32(0), null, BlackboardReductionKind.None);
            var leaf = new NodeDocument(
                new NodeId("leaf"), ReferenceFixtureNodeManifests.SuccessTypeId, 1, Array.Empty<NodeId>(),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var agentContract = new BlackboardScopeContract("p6014.spike.agent", 1);
            return TreeDocument.CreateVersion2(
                new TreeId("tree.p6-014-spike"), "Spec", leaf.Id, new[] { leaf },
                agentContract, null, new[] { key },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }
    }
}
