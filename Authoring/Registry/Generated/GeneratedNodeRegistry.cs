using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    public static class GeneratedNodeRegistry
    {
        public static NodeRegistryBuildResult Build(IEnumerable<GeneratedNodeDescriptor> nodes, bool includeBuiltIns = true)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            var ordered = new List<GeneratedNodeDescriptor>(nodes);
            ordered.Sort((left, right) =>
            {
                var comparison = Utf8OrdinalComparer.Instance.Compare(left.Manifest.TypeId, right.Manifest.TypeId);
                return comparison != 0 ? comparison : left.Manifest.Version.CompareTo(right.Manifest.Version);
            });
            var builder = includeBuiltIns ? NodeRegistryBuilder.CreateWithBuiltIns() : new NodeRegistryBuilder();
            for (var index = 0; index < ordered.Count; index++)
                builder.AddUserExtension(ordered[index].Manifest);
            return builder.Build();
        }
    }
}
