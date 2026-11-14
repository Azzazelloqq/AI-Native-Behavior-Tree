using System.Text;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>Compact canonical tree JSON for the managed agent layer tests.</summary>
    internal static class ManagedTestTrees
    {
        internal static string Tree(string root, string blackboard, params string[] nodes)
            => TreeWithContracts(root, null, blackboard, nodes);

        internal static string TreeWithContracts(string root, string blackboardContracts, string blackboard, params string[] nodes)
        {
            return "{ \"format\": \"aibt.tree\", \"formatVersion\": 2, \"treeId\": \"tree.test.managed\", \"name\": \"Managed\", \"root\": \""
                + root + "\", "
                + (blackboardContracts == null ? string.Empty : "\"blackboardContracts\": { " + blackboardContracts + " }, ")
                + (blackboard == null ? string.Empty : "\"blackboard\": { " + blackboard + " }, ")
                + "\"nodes\": { " + string.Join(", ", nodes) + " } }";
        }

        internal static string Node(string id, string type, string parameters, params string[] children)
        {
            var builder = new StringBuilder();
            builder.Append('"').Append(id).Append("\": { \"type\": \"").Append(type).Append("\", \"typeVersion\": 1");
            if (parameters != null) builder.Append(", \"parameters\": { ").Append(parameters).Append(" }");
            if (children.Length > 0) builder.Append(", \"children\": [\"").Append(string.Join("\", \"", children)).Append("\"]");
            return builder.Append(" }").ToString();
        }
    }
}
