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
