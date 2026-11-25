using System;

namespace AIBT
{
    /// <summary>
    /// Registers a <see cref="BtAction"/> or <see cref="BtCondition"/> class as a behavior-tree
    /// node. The type ID is the stable, lowercase, project-qualified name written into tree files,
    /// for example <c>game.move-to</c>. Renaming the class does not change it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class BtNodeAttribute : Attribute
    {
        public BtNodeAttribute(string typeId)
        {
            TypeId = typeId;
        }

        /// <summary>
        /// Stable node type ID, such as <c>game.move-to</c>. It hides <see cref="Attribute.TypeId"/> (an
        /// object that identifies attribute instances); <c>new</c> states that on purpose (CS0114) and
        /// changes nothing in the compiled assembly.
        /// </summary>
        public new string TypeId { get; }

        /// <summary>Menu path in the node search, such as <c>Movement</c>. Defaults to <c>Project</c>.</summary>
        public string Category { get; set; }

        /// <summary>One sentence shown in the editor and node catalog.</summary>
        public string Summary { get; set; }

        /// <summary>Optional guidance on when this node fits.</summary>
        public string WhenToUse { get; set; }

        /// <summary>Optional guidance on when another node fits better.</summary>
        public string WhenNotToUse { get; set; }

        /// <summary>
        /// Contract version, starting at one. Increase it when parameters change incompatibly so
        /// existing trees report a version mismatch instead of silently changing behavior.
        /// </summary>
        public uint Version { get; set; } = 1;
    }

    /// <summary>
    /// Optional settings for a node parameter. Every public instance field of a node is already a
    /// parameter; use this only to rename it or change whether a tree must supply it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class BtParamAttribute : Attribute
    {
        public BtParamAttribute()
        {
        }

        public BtParamAttribute(string name)
        {
            Name = name;
        }

        /// <summary>Parameter name in tree files. Defaults to the field name in kebab-case.</summary>
        public string Name { get; }

        /// <summary>The tree must supply this value. Blackboard keys are required by default.</summary>
        public bool Required { get; set; }

        /// <summary>The tree may leave this blackboard key unbound.</summary>
        public bool Optional { get; set; }
    }

    /// <summary>Excludes a public field from a node's parameters.</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class BtIgnoreAttribute : Attribute
    {
    }
}
