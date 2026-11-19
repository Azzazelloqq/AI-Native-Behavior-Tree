using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Authoring.BehaviorCases;
using NUnit.Framework;

namespace AIBT.Tests.Integration.SemanticSlice
{
    /// <summary>
    /// ADR-P11-001 Stage 1: a tree that uses subtrees runs exactly like the same tree written out by
    /// hand. Each subtree golden case is executed twice by the reference executor -- once through
    /// its subtree references (resolved from the case tree's directory) and once on a hand-written
    /// format-2 copy of the expanded tree -- and the observed traces, blackboard snapshots and
    /// tree semantic hashes must be identical.
    /// </summary>
    public sealed class SubtreeGoldenEquivalenceTests
    {
        [TestCase("subtree-reactive-observer.aibtcase.json", "Trees/SubtreeHandCopies/reactive-parent.expanded.aibt.json")]
        [TestCase("subtree-twice-private-keys.aibtcase.json", "Trees/SubtreeHandCopies/twice-parent.expanded.aibt.json")]
        public void SubtreeCase_RunsExactlyLikeItsHandCopiedTree(string caseFile, string handCopyTree)
        {
            var read = BehaviorCaseJson.Parse(File.ReadAllBytes(Path.Combine(GoldenRoot(), "Cases", caseFile)), caseFile);
            Assert.That(read.Success, Is.True);
            var viaSubtrees = Run(read.Document, read.Document.Tree);
            var viaHandCopy = Run(read.Document, handCopyTree);

            Assert.That(viaSubtrees, Is.EqualTo(viaHandCopy), string.Join("\n", viaSubtrees));
        }

        [Test]
        public void SubtreeCaseTree_ExpandsToItsHandCopiedTree()
        {
            var registry = SemanticSliceNodeContracts.CreateAuthoringRegistry();
            foreach (var pair in new[]
            {
                new KeyValuePair<string, string>("Trees/Subtrees/reactive-parent.aibt.json", "Trees/SubtreeHandCopies/reactive-parent.expanded.aibt.json"),
                new KeyValuePair<string, string>("Trees/Subtrees/twice-parent.aibt.json", "Trees/SubtreeHandCopies/twice-parent.expanded.aibt.json"),
            })
            {
                var parent = Read(pair.Key);
                var expansion = SubtreeExpander.Expand(
                    parent,
                    new DirectorySubtreeResolver(Path.Combine(GoldenRoot(), "Trees", "Subtrees"), "Trees/Subtrees"),
                    registry,
                    ReferenceCompilationPolicy.Phase1.CreateValidationOptions(pair.Key));
                Assert.That(expansion.Success, Is.True, string.Join(" | ", expansion.Diagnostics.Select(item => item.Code + " " + item.Message)));

                var expandedBytes = CanonicalTreeJson.Serialize(expansion.Document).Utf8;
                var handCopyBytes = File.ReadAllBytes(Path.Combine(GoldenRoot(), pair.Value.Replace('/', Path.DirectorySeparatorChar)));
                Assert.That(System.Text.Encoding.UTF8.GetString(expandedBytes), Is.EqualTo(System.Text.Encoding.UTF8.GetString(handCopyBytes)), pair.Key);
            }
        }

        private static List<string> Run(BehaviorCaseDocument template, string tree)
        {
            var document = new BehaviorCaseDocument(
                template.Name,
                template.Description,
                tree,
                template.TreeInstanceId,
                template.RootSeed,
                template.InitialBlackboard,
                template.Steps,
                template.Tags);
            var factory = new ReferenceBehaviorCaseExecutorFactory(GoldenRoot(), SemanticSliceNodeContracts.CreateAuthoringRegistry());
            var lines = new List<string>();
            using (var executor = factory.Create(new BehaviorCaseExecutorConfiguration(document)))
            {
                for (var index = 0; index < document.Steps.Count; index++)
                {
                    var result = executor.Execute(document.Steps[index]);
                    lines.Add("step " + index + " progress=" + result.Progress + " root=" + result.RootStatus);
                    foreach (var item in result.Trace) lines.Add("  " + Describe(item));
                    foreach (var pair in result.Blackboard)
                        lines.Add("  bb " + pair.Key + "=" + Format(pair.Value.Value.BuiltInValue) + " v" + pair.Value.Version);
                }
            }

            return lines;
        }

        private static string Describe(BehaviorCaseObservedTrace item)
            => item.Event
                + (item.NodeIndex.HasValue ? " node=" + item.NodeIndex.Value.Value : string.Empty)
                + (item.Status.HasValue ? " status=" + item.Status.Value : string.Empty)
                + (item.ExitReason.HasValue ? " exit=" + item.ExitReason.Value : string.Empty)
                + (item.AbortReason.HasValue ? " abort=" + item.AbortReason.Value : string.Empty)
                + (item.SourceNodeIndex.HasValue ? " source=" + item.SourceNodeIndex.Value.Value : string.Empty)
                + (item.StableBlackboardKeyId.HasValue ? " key=" + item.StableBlackboardKeyId.Value.ToString("x16") : string.Empty)
                + (item.NewBlackboardVersion.HasValue ? " version=" + item.OldBlackboardVersion + "->" + item.NewBlackboardVersion : string.Empty)
                + " hash=" + item.TreeSemanticHash.HexadecimalValue;

        private static string Format(BlackboardValue value)
            => value.TryGetBool(out var flag) ? "Bool:" + (flag ? "true" : "false") : "hash:" + value.GetHashCode();

        private static TreeDocument Read(string relative)
        {
            var read = CanonicalTreeJson.Parse(File.ReadAllBytes(Path.Combine(GoldenRoot(), relative.Replace('/', Path.DirectorySeparatorChar))), relative);
            Assert.That(read.Success, Is.True, relative);
            return read.Document;
        }

        private static string GoldenRoot()
            => SemanticSliceTestPackagePaths.Resolve("Tests", "Fixtures", "Golden");
    }
}
