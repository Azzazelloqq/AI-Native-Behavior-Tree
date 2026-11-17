using System;

namespace AIBT.Simulation
{
    /// <summary>
    /// P10-010: names one game input of a <see cref="TreeGroup"/>: the id string nodes give in
    /// <c>[AibtInput("id")]</c> (or <c>AibtSnapshotBinding</c>) on a <c>SnapshotReadHandle&lt;T&gt;</c> field,
    /// and the row type <typeparamref name="T"/> of the game array bound to it. Create once and keep it.
    /// Experimental (ADR-P10-008) until stage G5.
    /// </summary>
    public readonly struct InputChannel<T> where T : unmanaged
    {
        private InputChannel(string name)
        {
            Name = name;
            Id = StableHash.Fnv1A64(name);
        }

        /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
        public static InputChannel<T> Create(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("An input channel id is required.", nameof(id));
            return new InputChannel<T>(id);
        }

        /// <summary>The id string.</summary>
        public string Name { get; }
        /// <summary>FNV-1a 64 of the UTF-8 id string: the binding id the generated catalog records.</summary>
        public ulong Id { get; }
        public bool IsValid => Name != null;
        public override string ToString() => "InputChannel<" + typeof(T).Name + ">(" + Name + ")";
    }

    /// <summary>
    /// P10-010: names one command (effect) of a <see cref="TreeGroup"/>: the id string nodes give in
    /// <c>[AibtCommand("id")]</c> (or <c>AibtCommandBinding</c>) on a <c>CommandHandle&lt;T&gt;</c> field, and
    /// the payload type <typeparamref name="T"/>. Experimental (ADR-P10-008) until stage G5.
    /// </summary>
    public readonly struct CommandChannel<T> where T : unmanaged
    {
        private CommandChannel(string name)
        {
            Name = name;
            Id = StableHash.Fnv1A64(name);
        }

        /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
        public static CommandChannel<T> Create(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A command channel id is required.", nameof(id));
            return new CommandChannel<T>(id);
        }

        public string Name { get; }
        /// <summary>FNV-1a 64 of the UTF-8 id string: the binding id the generated catalog records.</summary>
        public ulong Id { get; }
        public bool IsValid => Name != null;
        public override string ToString() => "CommandChannel<" + typeof(T).Name + ">(" + Name + ")";
    }

    /// <summary>
    /// P10-010: one command an agent emitted in the last step: the agent (its handle at that step; it
    /// may have been removed since) and the payload. Experimental (ADR-P10-008) until stage G5.
    /// </summary>
    public readonly struct Command<T> where T : unmanaged
    {
        internal Command(AgentHandle agent, T value)
        {
            Agent = agent;
            Value = value;
        }

        public AgentHandle Agent { get; }
        public T Value { get; }
    }
}
