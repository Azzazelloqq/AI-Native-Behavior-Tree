using System;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Editor.Graph
{
    /// <summary>
    /// P11-014 confirmation U9: the tree list (<see cref="TreeCatalogEntry.ReadAll"/>) read every tree
    /// without the project's registered blackboard types, so a tree with a key of a registered type
    /// (here <c>aibt.tests.p10010.sense</c>, declared by the P10-010 fixture shard) was listed as
    /// "Errors" (MissingRegisteredSchema) although it imports and compiles.
    /// </summary>
    public sealed class TreeCatalogRegisteredTypeTests
    {
        private const string TypeId = "aibt.tests.p10010.sense";

        private const string Json = "{ \"format\": \"aibt.tree\", \"formatVersion\": 2, \"treeId\": \"tests.p11014.u9\", \"name\": \"U9\", "
            + "\"root\": \"hold\", "
            + "\"blackboard\": { \"sense\": { \"type\": \"" + TypeId + "\", \"typeVersion\": 1, \"scope\": \"tree\" } }, "
            + "\"nodes\": { \"hold\": { \"type\": \"aibt.stdlib.wait\", \"typeVersion\": 1, \"parameters\": { \"ticks\": 2 } } } }";

        private const string SubtreeId = "tests.p11014.u9-sub";

        private const string SubtreeJson = "{ \"format\": \"aibt.tree\", \"formatVersion\": 3, \"treeId\": \"" + SubtreeId + "\", \"name\": \"U9 sub\", "
            + "\"interface\": { \"version\": 1, \"keys\": [\"hits\"] }, \"root\": \"hold\", "
            + "\"blackboard\": { \"hits\": { \"type\": \"Int32\", \"default\": 0 }, "
            + "\"sense\": { \"type\": \"" + TypeId + "\", \"typeVersion\": 1, \"scope\": \"tree\" } }, "
            + "\"nodes\": { \"hold\": { \"type\": \"aibt.stdlib.wait\", \"typeVersion\": 1, \"parameters\": { \"ticks\": 2 } } } }";

        private const string ParentJson = "{ \"format\": \"aibt.tree\", \"formatVersion\": 3, \"treeId\": \"tests.p11014.u9-parent\", \"name\": \"U9 parent\", "
            + "\"root\": \"run\", \"blackboard\": { \"hits\": { \"type\": \"Int32\", \"default\": 0 } }, "
            + "\"nodes\": { \"run\": { \"type\": \"aibt.core.memory-sequence\", \"typeVersion\": 1, \"children\": [\"sub\"] }, "
            + "\"sub\": { \"subtree\": { \"treeId\": \"" + SubtreeId + "\", \"interfaceVersion\": 1, \"keys\": { \"hits\": \"hits\" } } } } }";

        /// <summary>
        /// P11-014 review follow-up (item 3): the project tree index read every tree file without the
        /// registered blackboard types, so a subtree file with a registered-type key was unreadable there
        /// and a parent that references it could not be expanded.
        /// </summary>
        [Test]
        public void ProjectTreeIndex_ASubtreeWithARegisteredTypeKey_IsFound_AndItsParentIsNotListedAsErrors()
        {
            var catalog = FixtureCatalog();
            var directory = Path.Combine(Path.GetTempPath(), "aibt-p11014-item3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "sub.aibt"), SubtreeJson);
                var parentPath = Path.Combine(directory, "parent.aibt");
                File.WriteAllText(parentPath, ParentJson);
                var roots = new[] { new ProjectTreeIndex.ScanRoot("Assets", directory, false) };

                // Without the registered types (before: every scan) the subtree file does not parse.
                var without = ProjectTreeIndex.Scan(roots, null).Resolve(new TreeId(SubtreeId));
                var with = ProjectTreeIndex.Scan(roots, catalog.RegisteredTypes).Resolve(new TreeId(SubtreeId));
                var parent = TreeCatalogEntry.ReadAll(new[] { parentPath }, catalog.Registry, null, catalog.RegisteredTypes, roots).Single();
                Debug.Log("P11-014 item 3: subtree without the registered types " + without.Status + ", with them " + with.Status
                    + "; parent " + parent.Status + ": " + parent.Details);
                Assert.That(without.Status, Is.EqualTo(SubtreeResolutionStatus.NotFound), "built-in types alone cannot read the subtree");
                Assert.That(with.Status, Is.EqualTo(SubtreeResolutionStatus.Found));
                Assert.That(parent.Status, Is.Not.EqualTo("Errors"), parent.Details);
                Assert.That(parent.NodeCount, Is.EqualTo(2), "the expanded parent: its root and the subtree's leaf");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static ProjectNodeCatalog FixtureCatalog()
        {
            var shard = Type.GetType("AIBT.Tests.CodeGen.GenerationP10010.SimulationIoShard, AIBT.CodeGen.P10010SimulationIo.Tests");
            Assert.That(shard, Is.Not.Null, "the P10-010 fixture shard");
            var catalog = ProjectNodeCatalog.Build(new[] { shard }, Array.Empty<IReferenceLeafBehaviorProvider>());
            Assert.That(catalog.Problems, Is.Empty, string.Join("; ", catalog.Problems));
            return catalog;
        }

        [Test]
        public void U9_ReadAll_ATreeWithARegisteredTypeKey_IsNotListedAsErrors()
        {
            // The project catalog lists player assemblies only; the fixture shard is in a test assembly, so the
            // catalog is built from it here as the graph window builds the project's (ProjectNodeCatalog.Build).
            var shard = Type.GetType("AIBT.Tests.CodeGen.GenerationP10010.SimulationIoShard, AIBT.CodeGen.P10010SimulationIo.Tests");
            Assert.That(shard, Is.Not.Null, "the P10-010 fixture shard");
            var catalog = ProjectNodeCatalog.Build(new[] { shard }, Array.Empty<IReferenceLeafBehaviorProvider>());
            Assert.That(catalog.Problems, Is.Empty, string.Join("; ", catalog.Problems));
            Assert.That(catalog.RegisteredTypes.TryGet(TypeId, 1u, out _), Is.True, "the fixture shard registers " + TypeId);
            var path = Path.Combine(Path.GetTempPath(), "aibt-p11014-u9-" + Guid.NewGuid().ToString("N") + ".aibt");
            File.WriteAllText(path, Json);
            try
            {
                var entry = TreeCatalogEntry.ReadAll(new[] { path }, catalog.Registry, null, catalog.RegisteredTypes).Single();
                Debug.Log("P11-014 U9: status " + entry.Status + "; details: " + entry.Details);
                Assert.That(entry.Details ?? string.Empty, Does.Not.Contain("Registered blackboard type"), entry.Details);
                Assert.That(entry.Status, Is.Not.EqualTo("Errors"), entry.Details);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
