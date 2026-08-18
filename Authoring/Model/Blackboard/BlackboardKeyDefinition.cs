namespace AIBT.Authoring
{
    public sealed class BlackboardKeyDefinition
    {
        public BlackboardKeyDefinition(
            string id,
            string name,
            BlackboardTypeReference type,
            BlackboardScope scope = BlackboardScope.Tree,
            BlackboardDefaultValue defaultValue = null,
            string description = null)
            : this(id, name, type, scope, defaultValue, description, BlackboardReductionKind.None)
        {
        }

        public BlackboardKeyDefinition(
            string id,
            string name,
            BlackboardTypeReference type,
            BlackboardScope scope,
            BlackboardDefaultValue defaultValue,
            string description,
            BlackboardReductionKind reduction)
        {
            Id = id;
            Name = name;
            Type = type;
            Scope = scope;
            DefaultValue = defaultValue;
            Description = description;
            Reduction = reduction;
        }

        public string Id { get; }

        public string Name { get; }

        public BlackboardTypeReference Type { get; }

        public BlackboardScope Scope { get; }

        public BlackboardDefaultValue DefaultValue { get; }

        public string Description { get; }

        public BlackboardReductionKind Reduction { get; }

        public bool HasDefault => DefaultValue != null;
    }
}
