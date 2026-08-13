using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public sealed class TreeDocument
    {
        public const string CurrentFormat = "aibt.tree";
        public const int CurrentFormatVersion = 1;

        private List<BlackboardKeyDefinition> _blackboard;
        private ReadOnlyCollection<BlackboardKeyDefinition> _blackboardView;
        private List<NodeDocument> _nodes;
        private ReadOnlyCollection<NodeDocument> _nodesView;

        public TreeDocument(
            string format,
            int formatVersion,
            TreeId treeId,
            string name,
            NodeId root,
            IEnumerable<NodeDocument> nodes,
            IEnumerable<BlackboardKeyDefinition> blackboard = null,
            string description = null,
            TagSet tags = null,
            SemanticObject metadata = null,
            Revision revision = default)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            Format = format;
            FormatVersion = formatVersion;
            TreeId = treeId;
            Name = name;
            Root = root;
            Description = description;
            Tags = tags;
            Metadata = metadata;
            _nodes = new List<NodeDocument>(nodes);
            _nodesView = _nodes.AsReadOnly();
            _blackboard = new List<BlackboardKeyDefinition>(blackboard ?? Array.Empty<BlackboardKeyDefinition>());
            _blackboardView = _blackboard.AsReadOnly();
            Revision = revision.IsValid ? revision : new Revision(1);
        }

        public string Format { get; private set; }

        public int FormatVersion { get; private set; }

        public TreeId TreeId { get; private set; }

        public string Name { get; private set; }

        public string Description { get; private set; }

        public NodeId Root { get; private set; }

        public IReadOnlyList<BlackboardKeyDefinition> Blackboard => _blackboardView;

        public IReadOnlyList<NodeDocument> Nodes => _nodesView;

        public TagSet Tags { get; private set; }

        public SemanticObject Metadata { get; private set; }

        public Revision Revision { get; private set; }

        public bool SetFormat(string format, int formatVersion)
        {
            if (string.Equals(Format, format, StringComparison.Ordinal) && FormatVersion == formatVersion)
            {
                return false;
            }

            Mutate(() =>
            {
                Format = format;
                FormatVersion = formatVersion;
            });
            return true;
        }

        public bool SetIdentity(TreeId treeId)
        {
            if (TreeId == treeId)
            {
                return false;
            }

            Mutate(() => TreeId = treeId);
            return true;
        }

        public bool SetName(string name)
        {
            if (string.Equals(Name, name, StringComparison.Ordinal))
            {
                return false;
            }

            Mutate(() => Name = name);
            return true;
        }

        public bool SetDescription(string description)
        {
            if (string.Equals(Description, description, StringComparison.Ordinal))
            {
                return false;
            }

            Mutate(() => Description = description);
            return true;
        }

        public bool SetRoot(NodeId root)
        {
            if (Root == root)
            {
                return false;
            }

            Mutate(() => Root = root);
            return true;
        }

        public bool SetTags(TagSet tags)
        {
            if (Equals(Tags, tags))
            {
                return false;
            }

            Mutate(() => Tags = tags);
            return true;
        }

        public bool SetMetadata(SemanticObject metadata)
        {
            if (Equals(Metadata, metadata))
            {
                return false;
            }

            Mutate(() => Metadata = metadata);
            return true;
        }

        public bool SetBlackboard(IEnumerable<BlackboardKeyDefinition> blackboard)
        {
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            var replacement = new List<BlackboardKeyDefinition>(blackboard);
            if (BlackboardSequenceEqual(_blackboard, replacement))
            {
                return false;
            }

            Mutate(() =>
            {
                _blackboard = replacement;
                _blackboardView = _blackboard.AsReadOnly();
            });
            return true;
        }

        public void AddNode(NodeDocument node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            Mutate(() => _nodes.Add(node));
        }

        public bool ReplaceNodeAt(int index, NodeDocument node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (index < 0 || index >= _nodes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (Equals(_nodes[index], node))
            {
                return false;
            }

            Mutate(() => _nodes[index] = node);
            return true;
        }

        public bool RemoveNodeAt(int index)
        {
            if (index < 0 || index >= _nodes.Count)
            {
                return false;
            }

            Mutate(() => _nodes.RemoveAt(index));
            return true;
        }

        private void Mutate(Action mutation)
        {
            if (Revision.Value == ulong.MaxValue)
            {
                throw new InvalidOperationException("The document revision cannot be incremented further.");
            }

            mutation();
            Revision = new Revision(Revision.Value + 1);
        }

        private static bool BlackboardSequenceEqual(
            IReadOnlyList<BlackboardKeyDefinition> left,
            IReadOnlyList<BlackboardKeyDefinition> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (var index = 0; index < left.Count; index++)
            {
                if (!BlackboardKeyEquals(left[index], right[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool BlackboardKeyEquals(BlackboardKeyDefinition left, BlackboardKeyDefinition right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return string.Equals(left.Id, right.Id, StringComparison.Ordinal)
                && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                && left.Type == right.Type
                && left.Scope == right.Scope
                && string.Equals(left.Description, right.Description, StringComparison.Ordinal)
                && BlackboardDefaultEquals(left.DefaultValue, right.DefaultValue);
        }

        private static bool BlackboardDefaultEquals(BlackboardDefaultValue left, BlackboardDefaultValue right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null
                || left.ValueType != right.ValueType
                || left.IsCanonical != right.IsCanonical
                || left.RegisteredTypeVersion != right.RegisteredTypeVersion
                || !string.Equals(left.EnumContract, right.EnumContract, StringComparison.Ordinal)
                || !string.Equals(left.SourceText, right.SourceText, StringComparison.Ordinal)
                || !string.Equals(left.RegisteredTypeId, right.RegisteredTypeId, StringComparison.Ordinal))
            {
                return false;
            }

            var leftHasRuntimeValue = left.TryGetRuntimeValue(out var leftRuntimeValue);
            var rightHasRuntimeValue = right.TryGetRuntimeValue(out var rightRuntimeValue);
            return leftHasRuntimeValue == rightHasRuntimeValue
                && (!leftHasRuntimeValue || leftRuntimeValue == rightRuntimeValue);
        }
    }
}
