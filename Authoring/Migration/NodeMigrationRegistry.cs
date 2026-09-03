using System;
using System.Collections.Generic;

namespace AIBT.Authoring.Migration
{
    /// <summary>
    /// A lookup table of <see cref="NodeMigrationRule"/>s keyed by (type, source version), per
    /// <c>ADR-P7-005</c>. Mirrors <see cref="NodeRegistryBuilder"/>'s own builder shape.
    /// <see cref="Empty"/> is what every real production call site uses today -- no node type has
    /// ever been version-bumped in this project, so no real rule exists yet; tests build their own
    /// populated instance via <see cref="WithRule"/>.
    /// </summary>
    public sealed class NodeMigrationRegistry
    {
        private readonly Dictionary<(string TypeId, uint SourceVersion), NodeMigrationRule> _rules;

        private NodeMigrationRegistry(Dictionary<(string, uint), NodeMigrationRule> rules)
        {
            _rules = rules;
        }

        public static NodeMigrationRegistry Empty { get; } =
            new NodeMigrationRegistry(new Dictionary<(string, uint), NodeMigrationRule>());

        public NodeMigrationRegistry WithRule(NodeMigrationRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            var key = (rule.TypeId, rule.SourceVersion);
            if (_rules.ContainsKey(key))
            {
                throw new ArgumentException(
                    "A migration rule for '" + rule.TypeId + "' from version " + rule.SourceVersion + " is already registered.",
                    nameof(rule));
            }

            var next = new Dictionary<(string, uint), NodeMigrationRule>(_rules) { [key] = rule };
            return new NodeMigrationRegistry(next);
        }

        public bool TryGetRule(string typeId, uint sourceVersion, out NodeMigrationRule rule)
            => _rules.TryGetValue((typeId, sourceVersion), out rule);
    }
}
