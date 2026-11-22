using System;
using System.Collections.Generic;
using System.Text;
using AIBT.Authoring;

namespace AIBT.Agents
{
    /// <summary>
    /// A tree compiled for the managed layer: the immutable compiled program plus, for every
    /// managed leaf, its node class and authored parameters. Shared by all agents running the
    /// same tree; each agent owns only its <see cref="ManagedTreeInstance"/>.
    /// </summary>
    internal sealed class ManagedTreeProgram
    {
        /// <summary>
        /// The managed layer runs on the main thread and its leaves read live game state, so
        /// main-thread, managed and non-deterministic nodes are all allowed here. The native
        /// path keeps its own stricter policy.
        /// </summary>
        internal static readonly ReferenceCompilationPolicy Policy = new ReferenceCompilationPolicy(
            allowManagedNodes: true,
            allowMainThreadNodes: true,
            requireDeterministicNodes: false);

        private static readonly CompiledCompilerVersion CompilerVersion = new CompiledCompilerVersion(1, 0, 0, 7);

        private ManagedTreeProgram(TreeDocument document, CompiledProgram program, ManagedNodeSet nodes, LeafBinding[] leaves)
        {
            Document = document;
            Program = program;
            Nodes = nodes;
            Leaves = leaves;
        }

        internal TreeDocument Document { get; }

        internal CompiledProgram Program { get; }

        internal ManagedNodeSet Nodes { get; }

        /// <summary>Indexed by runtime node index; null for composites, decorators and built-in leaves.</summary>
        internal LeafBinding[] Leaves { get; }

        internal static ReferenceCompilerOptions CreateOptions(string sourceId)
            => new ReferenceCompilerOptions(NormalizeSourceId(sourceId), Policy, CompilerVersion);

        internal static bool TryCompile(
            string json,
            string sourceId,
            ManagedNodeSet nodes,
            out ManagedTreeProgram program,
            out DiagnosticCollection diagnostics)
            => TryCompile(json, sourceId, nodes, null, out program, out diagnostics);

        /// <param name="resolver">Resolves subtree references (ADR-P11-001); null when the tree has none.</param>
        internal static bool TryCompile(
            string json,
            string sourceId,
            ManagedNodeSet nodes,
            ISubtreeResolver resolver,
            out ManagedTreeProgram program,
            out DiagnosticCollection diagnostics)
        {
            program = null;
            if (string.IsNullOrEmpty(json))
            {
                diagnostics = Failure("The tree source is empty.");
                return false;
            }

            var read = CanonicalTreeJson.Parse(Encoding.UTF8.GetBytes(json), NormalizeSourceId(sourceId));
            if (read.Document == null || HasError(read.Diagnostics))
            {
                diagnostics = read.Diagnostics;
                return false;
            }

            return TryCompile(read.Document, sourceId, nodes, resolver, out program, out diagnostics);
        }

        internal static bool TryCompile(
            TreeDocument document,
            string sourceId,
            ManagedNodeSet nodes,
            out ManagedTreeProgram program,
            out DiagnosticCollection diagnostics)
            => TryCompile(document, sourceId, nodes, null, out program, out diagnostics);

        /// <summary>
        /// Compiles <paramref name="document"/>, expanding its subtree references through
        /// <paramref name="resolver"/>; <see cref="Document"/> of the result is the expanded document
        /// (the same object when the tree has no reference and no interface).
        /// </summary>
        internal static bool TryCompile(
            TreeDocument document,
            string sourceId,
            ManagedNodeSet nodes,
            ISubtreeResolver resolver,
            out ManagedTreeProgram program,
            out DiagnosticCollection diagnostics)
        {
            program = null;
            if (document == null) throw new ArgumentNullException(nameof(document));
            nodes = nodes ?? ManagedNodeCatalog.Default;
            var options = CreateOptions(sourceId);
            ReferenceCompilationResult compilation;
            if (SubtreeExpander.RequiresExpansion(document))
            {
                var expansion = SubtreeExpander.Expand(
                    document,
                    resolver,
                    nodes.Registry,
                    options.Policy.CreateValidationOptions(options.SourceId));
                compilation = ReferenceCompiler.Compile(expansion, nodes.Registry, options);
                if (compilation.Success) document = expansion.Document;
            }
            else
            {
                compilation = ReferenceCompiler.Compile(document, nodes.Registry, options);
            }

            return Bind(document, sourceId, compilation, nodes, out program, out diagnostics);
        }

        /// <summary>
        /// Compiles content that was expanded ahead of time (an importer stores the expanded
        /// canonical JSON, because subtree files are not available in a Player):
        /// <paramref name="subtreeNodeSourceIds"/> gives the subtree source ID of every node that came
        /// from a subtree, so the debug map is the same as when the tree is expanded here.
        /// </summary>
        internal static bool TryCompileExpanded(
            string json,
            string sourceId,
            ManagedNodeSet nodes,
            IDictionary<NodeId, string> subtreeNodeSourceIds,
            out ManagedTreeProgram program,
            out DiagnosticCollection diagnostics)
        {
            program = null;
            if (string.IsNullOrEmpty(json))
            {
                diagnostics = Failure("The tree source is empty.");
                return false;
            }

            var read = CanonicalTreeJson.Parse(Encoding.UTF8.GetBytes(json), NormalizeSourceId(sourceId));
            if (read.Document == null || HasError(read.Diagnostics))
            {
                diagnostics = read.Diagnostics;
                return false;
            }

            var document = read.Document;
            if (SubtreeExpander.RequiresExpansion(document))
            {
                diagnostics = Failure("Expanded tree content still contains a subtree reference or an interface.");
                return false;
            }

            nodes = nodes ?? ManagedNodeCatalog.Default;
            var expansion = new SubtreeExpansion(document, document, subtreeNodeSourceIds, null);
            var compilation = ReferenceCompiler.Compile(expansion, nodes.Registry, CreateOptions(sourceId));
            return Bind(document, sourceId, compilation, nodes, out program, out diagnostics);
        }

        private static bool Bind(
            TreeDocument document,
            string sourceId,
            ReferenceCompilationResult compilation,
            ManagedNodeSet nodes,
            out ManagedTreeProgram program,
            out DiagnosticCollection diagnostics)
        {
            program = null;
            diagnostics = compilation.Diagnostics;
            if (!compilation.Success) return false;
            var refusals = ObserverRefusals(document, NormalizeSourceId(sourceId));
            if (refusals.Count != 0)
            {
                var combined = new List<Diagnostic>(compilation.Diagnostics);
                combined.AddRange(refusals);
                diagnostics = new DiagnosticCollection(combined);
                return false;
            }

            var documentNodes = new Dictionary<NodeId, NodeDocument>();
            for (var index = 0; index < document.Nodes.Count; index++)
                documentNodes[document.Nodes[index].Id] = document.Nodes[index];

            var compiled = compilation.Program;
            var leaves = new LeafBinding[compiled.Nodes.Count];
            for (var index = 0; index < compiled.DebugMap.Count; index++)
            {
                var entry = compiled.DebugMap[index];
                var record = compiled.Nodes[(int)entry.RuntimeNodeIndex];
                if (!nodes.TryGet(record.NodeTypeId, out var type)) continue;
                leaves[entry.RuntimeNodeIndex] = new LeafBinding(type, documentNodes[entry.AuthoringNodeId]);
            }

            program = new ManagedTreeProgram(document, compiled, nodes, leaves);
            return true;
        }

        /// <summary>
        /// P11-008 (review F10): one error (AIBT2051) per node with an <c>observer</c> block. The
        /// managed agent binds no observer evaluator, so such a tree would load and then stop with
        /// AIBT4002 the first time a watched key changed; it is refused when it is loaded instead.
        /// Also used by the Editor's validation of trees that run in a <c>BehaviorTreeAgent</c>.
        /// </summary>
        internal static List<Diagnostic> ObserverRefusals(TreeDocument document, string sourceId)
        {
            var result = new List<Diagnostic>();
            if (document == null) return result;
            for (var index = 0; index < document.Nodes.Count; index++)
            {
                var node = document.Nodes[index];
                if (node.Observer == null) continue;
                result.Add(TreeValidationDiagnosticCatalog.Create(
                    TreeValidationDiagnosticCodes.ObserverNotImplementedInManagedAgent,
                    "Node '" + node.Id.Value + "' has an 'observer' block, but BehaviorTreeAgent does not run observers: "
                    + "nothing would re-evaluate the condition when a watched key changes, and the agent would stop with AIBT4002. "
                    + "Remove the block. To re-check the condition on every tick, make it an earlier child of '"
                    + BuiltInNodeManifests.ReactiveSequenceTypeId + "' or '" + BuiltInNodeManifests.ReactiveSelectorTypeId
                    + "', or give a BtCondition an 'interrupt' mode.",
                    new DiagnosticLocation(sourceId, "/nodes/" + EscapePointer(node.Id.Value) + "/observer",
                        treeId: document.TreeId, nodeId: node.Id)));
            }
            return result;
        }

        private static string EscapePointer(string value) => value.Replace("~", "~0").Replace("/", "~1");

        internal static string Describe(DiagnosticCollection diagnostics)
        {
            var builder = new StringBuilder();
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(diagnostics[index].Code.Value).Append(": ").Append(diagnostics[index].Message);
            }

            return builder.ToString();
        }

        private static bool HasError(DiagnosticCollection diagnostics)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Severity == DiagnosticSeverity.Error) return true;
            }

            return false;
        }

        private static DiagnosticCollection Failure(string message)
            => new DiagnosticCollection(new[]
            {
                new Diagnostic(ReferenceCompilerDiagnosticCodes.InvalidOptions, DiagnosticSeverity.Error, message),
            });

        /// <summary>The compiler requires a relative, forward-slash logical path; an asset path already is one.</summary>
        private static string NormalizeSourceId(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) return "trees/runtime.aibt";
            var builder = new StringBuilder(sourceId.Length);
            for (var index = 0; index < sourceId.Length; index++)
            {
                var character = sourceId[index];
                if (character == '\\') character = '/';
                if (character == ':' || character == '\0') continue;
                if (character == '/' && (builder.Length == 0 || builder[builder.Length - 1] == '/')) continue;
                builder.Append(character);
            }

            var text = builder.ToString().Trim('/');
            if (text.Length == 0) return "trees/runtime.aibt";
            var segments = text.Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                if (segments[index] == "." || segments[index] == "..") return "trees/runtime.aibt";
            }

            return text;
        }

        internal sealed class LeafBinding
        {
            internal LeafBinding(ManagedNodeType type, NodeDocument document)
            {
                Type = type;
                Document = document;
            }

            internal ManagedNodeType Type { get; }

            internal NodeDocument Document { get; }
        }
    }
}
