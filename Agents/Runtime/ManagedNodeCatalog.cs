using System;
using System.Collections.Generic;
using System.Reflection;
using AIBT.Authoring;

namespace AIBT.Agents
{
    /// <summary>
    /// A set of managed node classes and the node registry trees are compiled against: the
    /// built-in composites, decorators and leaves, plus these classes.
    /// </summary>
    internal sealed class ManagedNodeSet
    {
        private readonly Dictionary<string, ManagedNodeType> _byTypeId;
        private readonly Dictionary<ulong, ManagedNodeType> _byNumericId;

        private ManagedNodeSet(
            List<ManagedNodeType> types,
            List<string> problems,
            Dictionary<string, ManagedNodeType> byTypeId,
            Dictionary<ulong, ManagedNodeType> byNumericId,
            NodeRegistry registry)
        {
            Types = types.AsReadOnly();
            Problems = problems.AsReadOnly();
            _byTypeId = byTypeId;
            _byNumericId = byNumericId;
            Registry = registry;
        }

        internal IReadOnlyList<ManagedNodeType> Types { get; }

        /// <summary>Classes that could not be registered, each with the reason.</summary>
        internal IReadOnlyList<string> Problems { get; }

        internal NodeRegistry Registry { get; }

        internal bool TryGet(string typeId, out ManagedNodeType type) => _byTypeId.TryGetValue(typeId, out type);

        internal bool TryGet(ulong numericTypeId, out ManagedNodeType type) => _byNumericId.TryGetValue(numericTypeId, out type);

        /// <summary>Adds every node of this set to a registry builder as an executable project extension.</summary>
        internal void AddTo(NodeRegistryBuilder builder)
        {
            for (var index = 0; index < Types.Count; index++)
                builder.AddProjectExtension(Types[index].Manifest, new HostOnlyLeafBehavior(Types[index].TypeId));
        }

        internal static ManagedNodeSet Create(IEnumerable<Type> nodeClasses, IEnumerable<string> discoveryProblems = null)
        {
            var types = new List<ManagedNodeType>();
            var problems = new List<string>(discoveryProblems ?? Array.Empty<string>());
            var byTypeId = new Dictionary<string, ManagedNodeType>(StringComparer.Ordinal);
            var byNumericId = new Dictionary<ulong, ManagedNodeType>();
            foreach (var candidate in nodeClasses)
            {
                if (!ManagedNodeType.TryCreate(candidate, out var type, out var problem))
                {
                    problems.Add(problem);
                    continue;
                }

                if (byTypeId.TryGetValue(type.TypeId, out var existing))
                {
                    problems.Add("Node type ID '" + type.TypeId + "' is declared by both " + existing.Type.FullName
                        + " and " + candidate.FullName + ". Type IDs must be unique.");
                    continue;
                }

                if (byNumericId.ContainsKey(type.NumericTypeId))
                {
                    problems.Add("Node type ID '" + type.TypeId + "' collides with another node's stable hash. Rename it.");
                    continue;
                }

                byTypeId.Add(type.TypeId, type);
                byNumericId.Add(type.NumericTypeId, type);
                types.Add(type);
            }

            types.Sort((left, right) => string.CompareOrdinal(left.TypeId, right.TypeId));
            var builder = NodeRegistryBuilder.CreateWithBuiltIns();
            for (var index = 0; index < types.Count; index++)
                builder.AddProjectExtension(types[index].Manifest, new HostOnlyLeafBehavior(types[index].TypeId));
            var built = builder.Build();
            if (!built.Success)
            {
                for (var index = 0; index < built.Diagnostics.Count; index++)
                    problems.Add(built.Diagnostics[index].Code.Value + ": " + built.Diagnostics[index].Message);
                // Keep the built-in nodes usable when a project node is rejected by the registry.
                built = NodeRegistryBuilder.CreateWithBuiltIns().Build();
                types.Clear();
                byTypeId.Clear();
                byNumericId.Clear();
            }

            return new ManagedNodeSet(types, problems, byTypeId, byNumericId, built.Registry);
        }

        /// <summary>
        /// Registry-level stand-in for a managed node. The node registry requires an executable
        /// reference behavior per leaf type, but a managed node needs its authored parameters and
        /// agent, which only <see cref="ManagedTreeInstance"/> supplies. A host that runs this
        /// stand-in directly (the fixture-based reference preview) gets a clear fault.
        /// </summary>
        private sealed class HostOnlyLeafBehavior : IReferenceLeafBehavior
        {
            private readonly string _typeId;

            internal HostOnlyLeafBehavior(string typeId)
            {
                _typeId = typeId;
            }

            public void Enter(ref ReferenceLeafContext context)
            {
            }

            public NodeStatus Tick(ref ReferenceLeafContext context)
                => throw new NotSupportedException("Node '" + _typeId + "' is a managed node and runs only in a BehaviorTreeAgent.");

            public void Abort(ref ReferenceLeafContext context, NodeAbortReason reason)
            {
            }

            public void Exit(ref ReferenceLeafContext context, NodeExitReason reason)
            {
            }
        }
    }

    /// <summary>
    /// The project's managed nodes: every <see cref="BtNode"/> class with a
    /// <see cref="BtNodeAttribute"/> in a loaded, non-test assembly. Scanned once per domain.
    /// </summary>
    internal static class ManagedNodeCatalog
    {
        private const string AgentsAssemblyName = "AIBT.Agents";
        private const string TestFrameworkAssemblyName = "nunit.framework";

        private static readonly object Gate = new object();
        private static ManagedNodeSet _default;
        private static ManagedNodeSet _standard;

        /// <summary>
        /// Only the package's own standard nodes (<c>aibt.unity.*</c>), independent of the project.
        /// The generated node catalog documents this set.
        /// </summary>
        internal static ManagedNodeSet Standard
        {
            get
            {
                lock (Gate)
                {
                    return _standard ?? (_standard = ManagedNodeSet.Create(CollectNodeClasses(typeof(BtNode).Assembly, null)));
                }
            }
        }

        internal static ManagedNodeSet Default
        {
            get
            {
                lock (Gate)
                {
                    return _default ?? (_default = Discover());
                }
            }
        }

        /// <summary>Discards the cached scan. Statics reset on a domain reload anyway.</summary>
        internal static void Refresh()
        {
            lock (Gate)
            {
                _default = null;
                _standard = null;
            }
        }

        private static ManagedNodeSet Discover()
        {
            var problems = new List<string>();
            var classes = new List<Type>();
            var own = typeof(BtNode).Assembly;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                // Node classes in a test assembly are fixtures: a test passes them to
                // ManagedNodeSet.Create itself, so they never leak into a project's node list.
                if (assembly != own && !IsProjectNodeAssembly(assembly)) continue;
                classes.AddRange(CollectNodeClasses(assembly, problems));
            }

            classes.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));
            return ManagedNodeSet.Create(classes, problems);
        }

        private static List<Type> CollectNodeClasses(Assembly assembly, List<string> problems)
        {
            var classes = new List<Type>();
            Type[] candidates;
            try
            {
                candidates = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                candidates = exception.Types;
            }

            for (var index = 0; index < candidates.Length; index++)
            {
                var type = candidates[index];
                if (type == null || type.IsAbstract || !typeof(BtNode).IsAssignableFrom(type)) continue;
                if (!type.IsDefined(typeof(BtNodeAttribute), false))
                {
                    problems?.Add(type.FullName + " derives from BtAction or BtCondition but has no [BtNode(\"project.node-name\")] attribute.");
                    continue;
                }

                classes.Add(type);
            }

            classes.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));
            return classes;
        }

        private static bool IsProjectNodeAssembly(Assembly assembly)
        {
            var references = assembly.GetReferencedAssemblies();
            var referencesAgents = false;
            for (var index = 0; index < references.Length; index++)
            {
                var name = references[index].Name;
                if (string.Equals(name, TestFrameworkAssemblyName, StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(name, AgentsAssemblyName, StringComparison.Ordinal)) referencesAgents = true;
            }

            return referencesAgents;
        }
    }
}
