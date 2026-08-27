using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AIBT.Authoring
{
    // Assembles the project manifest response ai-and-mcp.md's "Information available to an
    // agent" section describes: registered capabilities, a project-policy summary, and a
    // tree/revision listing. This card does not invent a project-wide tree-discovery mechanism --
    // no such thing exists in AIBT's model yet; the caller supplies which documents are "the
    // project's trees" (P6-005's server host does this for real once it exists).
    public sealed class ProjectManifestQuery
    {
        private readonly NodeRegistry _registry;
        private readonly ProjectPolicySnapshot _policy;

        public ProjectManifestQuery(NodeRegistry registry, ProjectPolicySnapshot policy)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public JObject Build(IEnumerable<TreeDocument> trees)
        {
            if (trees == null)
            {
                throw new ArgumentNullException(nameof(trees));
            }

            var orderedTrees = new List<TreeDocument>(trees);
            orderedTrees.Sort((left, right) => CanonicalJsonText.CompareUtf8(left.TreeId.Value, right.TreeId.Value));

            var treeArray = new JArray();
            for (var index = 0; index < orderedTrees.Count; index++)
            {
                var tree = orderedTrees[index];
                treeArray.Add(new JObject
                {
                    ["treeId"] = tree.TreeId.Value,
                    ["name"] = tree.Name,
                    ["revision"] = tree.Revision.Value,
                });
            }

            return new JObject
            {
                ["format"] = "aibt-project-manifest",
                ["formatVersion"] = 1,
                ["capabilities"] = new JObject
                {
                    ["burst"] = HasCapability(NodeRegistryCapabilityFlags.Burst),
                    ["managed"] = HasCapability(NodeRegistryCapabilityFlags.Managed),
                    ["mainThread"] = HasCapability(NodeRegistryCapabilityFlags.MainThread),
                    ["nonDeterministic"] = HasCapability(NodeRegistryCapabilityFlags.NonDeterministic),
                    ["sideEffects"] = HasCapability(NodeRegistryCapabilityFlags.SideEffects),
                    ["userExtensions"] = HasCapability(NodeRegistryCapabilityFlags.UserExtensions),
                },
                ["nodeRegistryHash"] = _registry.Hash,
                ["nodeCount"] = _registry.Count,
                ["policy"] = PolicyJson(_policy),
                ["trees"] = treeArray,
            };
        }

        private bool HasCapability(NodeRegistryCapabilityFlags flag)
        {
            return (_registry.Capabilities & flag) == flag;
        }

        private static JObject PolicyJson(ProjectPolicySnapshot policy)
        {
            var forbidden = new List<string>(policy.ForbiddenNodeTypes);
            forbidden.Sort(CanonicalJsonText.CompareUtf8);
            var forbiddenArray = new JArray();
            for (var index = 0; index < forbidden.Count; index++)
            {
                forbiddenArray.Add(forbidden[index]);
            }

            var warnings = new List<string>(policy.WarningsAsErrors);
            warnings.Sort(CanonicalJsonText.CompareUtf8);
            var warningsArray = new JArray();
            for (var index = 0; index < warnings.Count; index++)
            {
                warningsArray.Add(warnings[index]);
            }

            var result = new JObject
            {
                ["allowManagedNodes"] = policy.AllowManagedNodes,
                ["allowMainThreadNodes"] = policy.AllowMainThreadNodes,
                ["requireTreeDescription"] = policy.RequireTreeDescription,
                ["requireNodeDescriptions"] = policy.RequireNodeDescriptions,
                ["blackboardNaming"] = policy.BlackboardNaming,
                ["requireDeterministicNodes"] = policy.RequireDeterministicNodes,
                ["allowSideEffects"] = policy.AllowSideEffects,
                ["unreachableNodes"] = policy.UnreachableNodes,
                ["supportsAgentScope"] = policy.SupportsAgentScope,
                ["supportsSharedScope"] = policy.SupportsSharedScope,
                ["forbiddenNodeTypes"] = forbiddenArray,
                ["warningsAsErrors"] = warningsArray,
                ["performance"] = new JObject
                {
                    ["forbidUnboundedRepeaters"] = policy.ForbidUnboundedRepeaters,
                    ["requireEventDrivenServices"] = policy.RequireEventDrivenServices,
                },
            };

            if (policy.MaxTreeDepth.HasValue)
            {
                result["maxTreeDepth"] = policy.MaxTreeDepth.Value;
            }

            if (policy.MaxNodesPerTree.HasValue)
            {
                result["maxNodesPerTree"] = policy.MaxNodesPerTree.Value;
            }

            if (policy.MaxEstimatedCost.HasValue)
            {
                ((JObject)result["performance"])["maxEstimatedCost"] = policy.MaxEstimatedCost.Value;
            }

            return result;
        }
    }
}
