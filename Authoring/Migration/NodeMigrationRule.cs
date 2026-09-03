using System;
using System.Collections.Generic;

namespace AIBT.Authoring.Migration
{
    /// <summary>A field rename applied by a <see cref="NodeMigrationRule"/>: same value, new JSON key.</summary>
    public sealed class NodeFieldRename
    {
        public NodeFieldRename(string from, string to)
        {
            if (string.IsNullOrWhiteSpace(from)) throw new ArgumentException("A rename requires a source field name.", nameof(from));
            if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("A rename requires a target field name.", nameof(to));
            From = from;
            To = to;
        }

        public string From { get; }
        public string To { get; }
    }

    /// <summary>A field addition applied by a <see cref="NodeMigrationRule"/>: a new field with a fixed default.</summary>
    public sealed class NodeFieldAddition
    {
        public NodeFieldAddition(string name, SemanticValue defaultValue)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An addition requires a field name.", nameof(name));
            Name = name;
            DefaultValue = defaultValue ?? throw new ArgumentNullException(nameof(defaultValue));
        }

        public string Name { get; }
        public SemanticValue DefaultValue { get; }
    }

    /// <summary>
    /// Declarative migration for one node type from <see cref="SourceVersion"/> to
    /// <see cref="SourceVersion"/> + 1, per <c>ADR-P7-005</c>: field rename and field
    /// added-with-default only, pure JSON-parameter transform, never Burst/execution-layer code.
    /// </summary>
    public sealed class NodeMigrationRule
    {
        public NodeMigrationRule(
            string typeId,
            uint sourceVersion,
            IReadOnlyList<NodeFieldRename> renames = null,
            IReadOnlyList<NodeFieldAddition> additions = null)
        {
            if (string.IsNullOrWhiteSpace(typeId)) throw new ArgumentException("A migration rule requires a type ID.", nameof(typeId));
            if (sourceVersion == 0) throw new ArgumentOutOfRangeException(nameof(sourceVersion), "A source version must be at least 1.");
            TypeId = typeId;
            SourceVersion = sourceVersion;
            Renames = renames ?? Array.Empty<NodeFieldRename>();
            Additions = additions ?? Array.Empty<NodeFieldAddition>();
        }

        public string TypeId { get; }

        /// <summary>The rule migrates a node from this version to <see cref="SourceVersion"/> + 1.</summary>
        public uint SourceVersion { get; }

        public IReadOnlyList<NodeFieldRename> Renames { get; }

        public IReadOnlyList<NodeFieldAddition> Additions { get; }
    }
}
