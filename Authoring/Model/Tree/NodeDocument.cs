using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public sealed class NodeDocument : IEquatable<NodeDocument>
    {
        private readonly ReadOnlyCollection<NodeId> _children;

        public NodeDocument(
            NodeId id,
            string typeId,
            int typeVersion,
            IEnumerable<NodeId> children = null,
            SemanticObject parameters = null,
            NodeObserver observer = null,
            string displayName = null,
            string description = null,
            TagSet tags = null)
            : this(id, typeId, typeVersion, children, parameters, observer, displayName, description, tags, null)
        {
        }

        public NodeDocument(
            NodeId id,
            string typeId,
            int typeVersion,
            IEnumerable<NodeId> children,
            SemanticObject parameters,
            NodeObserver observer,
            string displayName,
            string description,
            TagSet tags,
            NodeBindingMap bindings)
            : this(id, typeId, typeVersion, children, parameters, observer, displayName, description, tags, bindings, null)
        {
        }

        /// <summary>
        /// Full constructor including a tree format version 3 subtree reference (ADR-P11-001). A
        /// well-formed reference node has no type, type version, parameters, bindings, observer or
        /// children; the model keeps any combination so the validator can report it (AIBT2047).
        /// </summary>
        public NodeDocument(
            NodeId id,
            string typeId,
            int typeVersion,
            IEnumerable<NodeId> children,
            SemanticObject parameters,
            NodeObserver observer,
            string displayName,
            string description,
            TagSet tags,
            NodeBindingMap bindings,
            SubtreeReference subtree)
        {
            Id = id;
            TypeId = typeId;
            TypeVersion = typeVersion;
            _children = new List<NodeId>(children ?? Array.Empty<NodeId>()).AsReadOnly();
            Parameters = parameters;
            Bindings = bindings;
            Observer = observer;
            DisplayName = displayName;
            Description = description;
            Tags = tags;
            Subtree = subtree;
        }

        /// <summary>Creates a subtree reference node (tree format version 3, ADR-P11-001).</summary>
        public static NodeDocument CreateSubtreeReference(
            NodeId id,
            SubtreeReference subtree,
            string displayName = null,
            string description = null,
            TagSet tags = null)
        {
            if (subtree == null) throw new ArgumentNullException(nameof(subtree));
            return new NodeDocument(id, null, 0, null, SemanticObject.Empty, null, displayName, description, tags ?? TagSet.Empty, null, subtree);
        }

        public NodeId Id { get; }

        public string TypeId { get; }

        public int TypeVersion { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public SemanticObject Parameters { get; }

        public NodeBindingMap Bindings { get; }

        public NodeObserver Observer { get; }

        public IReadOnlyList<NodeId> Children => _children;

        public TagSet Tags { get; }

        /// <summary>The subtree this node stands for; null for an ordinary node.</summary>
        public SubtreeReference Subtree { get; }

        public bool IsSubtreeReference => Subtree != null;

        public NodeDocument WithChildren(IEnumerable<NodeId> children)
        {
            return new NodeDocument(Id, TypeId, TypeVersion, children, Parameters, Observer, DisplayName, Description, Tags, Bindings, Subtree);
        }

        public NodeDocument WithParameters(SemanticObject parameters)
        {
            return new NodeDocument(Id, TypeId, TypeVersion, _children, parameters, Observer, DisplayName, Description, Tags, Bindings, Subtree);
        }

        public NodeDocument WithBindings(NodeBindingMap bindings)
        {
            return new NodeDocument(Id, TypeId, TypeVersion, _children, Parameters, Observer, DisplayName, Description, Tags, bindings, Subtree);
        }

        public static NodeDocument CreateVersion2(
            NodeId id,
            string typeId,
            int typeVersion,
            NodeBindingMap bindings,
            IEnumerable<NodeId> children = null,
            SemanticObject parameters = null,
            NodeObserver observer = null,
            string displayName = null,
            string description = null,
            TagSet tags = null)
        {
            return new NodeDocument(id, typeId, typeVersion, children, parameters, observer, displayName, description, tags, bindings);
        }

        public NodeDocument WithObserver(NodeObserver observer)
        {
            return new NodeDocument(Id, TypeId, TypeVersion, _children, Parameters, observer, DisplayName, Description, Tags, Bindings, Subtree);
        }

        public NodeDocument WithDetails(string displayName, string description, TagSet tags)
        {
            return new NodeDocument(Id, TypeId, TypeVersion, _children, Parameters, Observer, displayName, description, tags, Bindings, Subtree);
        }

        public NodeDocument WithSubtree(SubtreeReference subtree)
        {
            return new NodeDocument(Id, TypeId, TypeVersion, _children, Parameters, Observer, DisplayName, Description, Tags, Bindings, subtree);
        }

        public NodeDocument WithType(string typeId, int typeVersion)
        {
            return new NodeDocument(Id, typeId, typeVersion, _children, Parameters, Observer, DisplayName, Description, Tags, Bindings, Subtree);
        }

        public bool Equals(NodeDocument other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null
                || Id != other.Id
                || !string.Equals(TypeId, other.TypeId, StringComparison.Ordinal)
                || TypeVersion != other.TypeVersion
                || !string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
                || !string.Equals(Description, other.Description, StringComparison.Ordinal)
                || !Equals(Parameters, other.Parameters)
                || !Equals(Bindings, other.Bindings)
                || !Equals(Observer, other.Observer)
                || !Equals(Tags, other.Tags)
                || !Equals(Subtree, other.Subtree)
                || _children.Count != other._children.Count)
            {
                return false;
            }

            for (var index = 0; index < _children.Count; index++)
            {
                if (_children[index] != other._children[index])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as NodeDocument);

        public override int GetHashCode() => Id.GetHashCode();
    }
}
