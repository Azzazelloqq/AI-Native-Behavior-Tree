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
        {
            Id = id;
            Name = name;
            Type = type;
            Scope = scope;
            DefaultValue = defaultValue;
            Description = description;
        }

        public string Id { get; }

        public string Name { get; }

        public BlackboardTypeReference Type { get; }

        public BlackboardScope Scope { get; }

        public BlackboardDefaultValue DefaultValue { get; }

        public string Description { get; }

        public bool HasDefault => DefaultValue != null;
    }
}
