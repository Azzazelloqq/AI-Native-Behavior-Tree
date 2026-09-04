using System.Collections.Generic;

namespace AIBT.Authoring.Migration
{
    /// <summary>
    /// Applies <see cref="NodeMigrationRule"/>s to a <see cref="TreeDocument"/> in memory, per
    /// <c>ADR-P7-005</c>: never mutates the source document or any file on disk, never touches the
    /// Burst-compiled node's own execution contract, and never skips a hop in a rule chain. A node
    /// with no rule for its exact version gap is left completely untouched -- the caller's own
    /// downstream validation (<see cref="TreeValidator"/>) reports it exactly as it does today,
    /// never silently guessed or partially migrated.
    /// </summary>
    public static class DocumentMigrator
    {
        public static TreeDocument TryMigrate(
            TreeDocument document,
            NodeRegistry registry,
            NodeMigrationRegistry rules,
            out IReadOnlyList<NodeMigrationOutcome> outcomes)
        {
            var outcomeList = new List<NodeMigrationOutcome>();
            var newNodes = new List<NodeDocument>(document.Nodes.Count);
            var changed = false;

            foreach (var node in document.Nodes)
            {
                if (!registry.TryGet(node.TypeId, out var entry) || node.TypeVersion < 0)
                {
                    newNodes.Add(node);
                    continue;
                }

                var targetVersion = entry.Manifest.Version;
                var currentVersion = (uint)node.TypeVersion;
                if (currentVersion >= targetVersion)
                {
                    newNodes.Add(node);
                    continue;
                }

                if (!TryMigrateNode(node, rules, targetVersion, out var migratedNode, out var changes))
                {
                    // No rule chain reaches the target version -- unhandled category (removal/type
                    // change, or simply no rule registered). Leave the node exactly as authored;
                    // TreeValidator's own UnsupportedNodeVersion check fires normally downstream.
                    newNodes.Add(node);
                    continue;
                }

                newNodes.Add(migratedNode);
                changed = true;
                outcomeList.Add(new NodeMigrationOutcome(
                    document.TreeId, node.Id, node.TypeId, currentVersion, targetVersion, changes));
            }

            outcomes = outcomeList;
            if (!changed) return document;

            return new TreeDocument(
                document.Format, document.FormatVersion, document.TreeId, document.Name,
                document.Root, newNodes, document.Blackboard, document.Description, document.Tags,
                document.Metadata, document.Revision, document.AgentContract, document.SharedContract);
        }

        private static bool TryMigrateNode(
            NodeDocument node,
            NodeMigrationRegistry rules,
            uint targetVersion,
            out NodeDocument migrated,
            out List<NodeMigrationChange> changes)
        {
            var version = (uint)node.TypeVersion;
            var parameters = node.Parameters;
            changes = new List<NodeMigrationChange>();

            while (version < targetVersion)
            {
                if (!rules.TryGetRule(node.TypeId, version, out var rule))
                {
                    migrated = null;
                    return false;
                }

                var props = new List<SemanticProperty>();
                foreach (var property in parameters.Properties)
                {
                    var renamed = FindRename(rule, property.Name);
                    if (renamed != null)
                    {
                        props.Add(new SemanticProperty(renamed.To, property.Value));
                        changes.Add(new NodeMigrationChange(
                            "field '" + renamed.From + "' renamed to '" + renamed.To + "'"));
                    }
                    else
                    {
                        props.Add(property);
                    }
                }

                foreach (var addition in rule.Additions)
                {
                    props.Add(new SemanticProperty(addition.Name, addition.DefaultValue));
                    changes.Add(new NodeMigrationChange(
                        "field '" + addition.Name + "' added, default " + Describe(addition.DefaultValue)));
                }

                parameters = new SemanticObject(props);
                version++;
            }

            migrated = new NodeDocument(
                node.Id, node.TypeId, (int)version, node.Children, parameters,
                node.Observer, node.DisplayName, node.Description, node.Tags, node.Bindings);
            return true;
        }

        private static NodeFieldRename FindRename(NodeMigrationRule rule, string fieldName)
        {
            for (var index = 0; index < rule.Renames.Count; index++)
                if (rule.Renames[index].From == fieldName)
                    return rule.Renames[index];
            return null;
        }

        private static string Describe(SemanticValue value)
        {
            switch (value.Kind)
            {
                case SemanticValueKind.Boolean: value.TryGetBoolean(out var b); return b.ToString();
                case SemanticValueKind.SignedInteger: value.TryGetInt64(out var i); return i.ToString();
                case SemanticValueKind.UnsignedInteger: value.TryGetUInt64(out var u); return u.ToString();
                case SemanticValueKind.Number: value.TryGetNumber(out var n); return n.ToString("R");
                case SemanticValueKind.String: value.TryGetString(out var s); return "\"" + s + "\"";
                default: return value.Kind.ToString();
            }
        }
    }
}
