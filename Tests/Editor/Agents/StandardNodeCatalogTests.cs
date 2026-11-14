using System.Linq;
using AIBT.Agents;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P9-002: the standard managed nodes are one coherent, discoverable library: all register,
    /// all are documented main-thread nodes under aibt.unity.*, and the project catalog offers them.
    /// </summary>
    public sealed class StandardNodeCatalogTests
    {
        private static readonly string[] Categories =
        {
            "Animation", "Audio", "Blackboard", "Checks", "Debug", "Events", "Flow", "GameObject", "Movement", "Navigation",
        };

        [Test]
        public void EveryStandardNodeRegistersUnderTheUnityNamespaceWithADocumentedContract()
        {
            var nodes = StandardNodeKit.Nodes;
            Assert.That(nodes.Types.Count, Is.EqualTo(StandardNodeKit.StandardTypes.Count), "No standard node class is rejected.");
            foreach (var type in nodes.Types)
            {
                var manifest = type.Manifest;
                Assert.That(manifest.TypeId, Does.StartWith("aibt.unity."), type.Type.FullName);
                Assert.That(manifest.Summary, Does.EndWith("."), manifest.TypeId + " has a written summary");
                Assert.That(Categories, Does.Contain(manifest.Category), manifest.TypeId);
                Assert.That(manifest.ExecutionDomain, Is.EqualTo(NodeExecutionDomain.MainThread), manifest.TypeId + " is never presented as Burst");
                Assert.That(manifest.Deterministic, Is.False, manifest.TypeId);
            }
        }

        [Test]
        public void TheLibraryCoversEveryRequestedArea()
        {
            var ids = StandardNodeKit.Nodes.Types.Select(type => type.TypeId).ToArray();
            var expected = new[]
            {
                "aibt.unity.wait-random", "aibt.unity.wait-until", "aibt.unity.wait-frames",
                "aibt.unity.set-vector3", "aibt.unity.copy-bool", "aibt.unity.copy-int", "aibt.unity.copy-float", "aibt.unity.copy-vector3",
                "aibt.unity.increment-int", "aibt.unity.increment-float", "aibt.unity.toggle-bool",
                "aibt.unity.compare-int-keys", "aibt.unity.compare-float-keys", "aibt.unity.is-vector3-set",
                "aibt.unity.random-float", "aibt.unity.random-int", "aibt.unity.random-point", "aibt.unity.distance",
                "aibt.unity.rotate-towards", "aibt.unity.turn-by", "aibt.unity.move-direction", "aibt.unity.keep-distance",
                "aibt.unity.nav-move-to", "aibt.unity.nav-stop",
                "aibt.unity.animator-trigger", "aibt.unity.animator-bool", "aibt.unity.animator-float",
                "aibt.unity.set-active", "aibt.unity.destroy-self", "aibt.unity.get-position", "aibt.unity.has-reference",
                "aibt.unity.play-audio", "aibt.unity.raise-event",
                "aibt.unity.line-of-sight", "aibt.unity.random-chance", "aibt.unity.start-cooldown", "aibt.unity.is-cooldown-ready",
                "aibt.unity.time-since-changed",
            };
            Assert.That(ids, Is.SupersetOf(expected));
        }

        [Test]
        public void TheProjectCatalogOffersTheStandardNodes()
        {
            var registry = ManagedNodeCatalog.Default.Registry;
            foreach (var type in StandardNodeKit.Nodes.Types)
                Assert.That(registry.TryGet(type.TypeId, out _), Is.True, type.TypeId);
        }

        [Test]
        public void TheProjectCatalogOffersTheStandardNativeShardAsNativeOnlyNodes()
        {
            var catalog = global::AIBT.Editor.Registry.ProjectNodeCatalog.Discover();
            foreach (var typeId in new[] { "aibt.native.bool-is-true", "aibt.native.float-at-least", "aibt.native.set-float", "aibt.native.wait-microseconds" })
            {
                Assert.That(catalog.Registry.TryGet(typeId, out var entry), Is.True, typeId + "; problems: " + string.Join("\n", catalog.Problems));
                Assert.That(entry.Manifest.ExecutionDomain, Is.EqualTo(NodeExecutionDomain.Burst), typeId);
                Assert.That(entry.HasReferenceHandlerBinding, Is.False, typeId + " has no managed/reference implementation");
                Assert.That(catalog.Generated.ContainsKey(typeId), Is.True, typeId);
            }
        }

        [Test]
        public void AnAnyTypeKeyParameterHasNoValueTypeConstraint()
        {
            Assert.That(StandardNodeKit.Nodes.TryGet("aibt.unity.time-since-changed", out var type), Is.True);
            var key = type.Manifest.Parameters.Single(parameter => parameter.Name == "key");
            Assert.That(key.Type, Is.EqualTo(NodeParameterType.BlackboardKey));
            Assert.That(key.Required, Is.True);
            Assert.That(key.BlackboardValueType, Is.Null);
        }

        [Test]
        public void OptionalKeyParametersAreNotRequired()
        {
            Assert.That(StandardNodeKit.Nodes.TryGet("aibt.unity.random-point", out var type), Is.True);
            Assert.That(type.Manifest.Parameters.Single(parameter => parameter.Name == "center").Required, Is.False);
            Assert.That(type.Manifest.Parameters.Single(parameter => parameter.Name == "result").Required, Is.True);
        }
    }
}
