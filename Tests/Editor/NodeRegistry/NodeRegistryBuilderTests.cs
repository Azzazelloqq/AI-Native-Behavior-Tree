using System;
using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.NodeRegistry
{
    public sealed class NodeRegistryBuilderTests
    {
        [Test]
        public void BuiltInRegistry_IsCompleteOrderedAndBoundExplicitly()
        {
            var result = Authoring.NodeRegistryBuilder.CreateWithBuiltIns().Build();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Registry.Count, Is.EqualTo(13));
            var actualIds = result.Registry.Select(item => item.Manifest.TypeId).ToArray();
            var expectedIds = actualIds.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            Assert.That(actualIds, Is.EqualTo(expectedIds));
            Assert.That(result.Registry.All(item => item.Source == Authoring.NodeManifestSource.BuiltIn), Is.True);
            Assert.That(result.Registry.All(item => item.HasReferenceHandlerBinding), Is.True);
            Assert.That((result.Registry.Capabilities & Authoring.NodeRegistryCapabilityFlags.Burst) != 0, Is.True);
            Assert.That((result.Registry.Capabilities & Authoring.NodeRegistryCapabilityFlags.ReferenceHandlerBindings) != 0, Is.True);
            Assert.That(result.Registry.Hash, Does.Match("^[0-9a-f]{64}$"));
        }

        [Test]
        public void Build_IsIndependentOfRegistrationOrder()
        {
            var alpha = NodeManifestTestFactory.Create("example.nodes.alpha");
            var beta = NodeManifestTestFactory.Create("example.nodes.beta");

            var first = new Authoring.NodeRegistryBuilder().AddUserExtension(beta).AddUserExtension(alpha).Build();
            var second = new Authoring.NodeRegistryBuilder().AddUserExtension(alpha).AddUserExtension(beta).Build();

            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.True);
            Assert.That(first.Registry.Select(item => item.Manifest.TypeId), Is.EqualTo(new[] { "example.nodes.alpha", "example.nodes.beta" }));
            Assert.That(first.Registry.Hash, Is.EqualTo(second.Registry.Hash));
            Assert.That(first.Registry.Select(item => item.NumericTypeId), Is.EqualTo(second.Registry.Select(item => item.NumericTypeId)));
        }

        [Test]
        public void DuplicateCanonicalIdAndVersion_IsADiagnostic()
        {
            var manifest = NodeManifestTestFactory.Create("example.nodes.duplicate");

            var result = new Authoring.NodeRegistryBuilder().AddUserExtension(manifest).AddUserExtension(manifest).Build();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Registry, Is.Null);
            Assert.That(result.Diagnostics.Select(item => item.Code.Value), Does.Contain("AIBT3001"));
        }

        [Test]
        public void DifferentActiveVersionsOfCanonicalId_AreIncompatible()
        {
            var result = new Authoring.NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.versioned", 2))
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.versioned", 1))
                .Build();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics[0].Code.Value, Is.EqualTo("AIBT3002"));
            Assert.That(result.Diagnostics[0].Message, Does.Contain("versions 1 and 2"));
        }

        [Test]
        public void NumericCollision_ReportsBothCanonicalStrings()
        {
            var result = new Authoring.NodeRegistryBuilder(_ => 42)
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.first"))
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.second"))
                .Build();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics[0].Code.Value, Is.EqualTo("AIBT3003"));
            Assert.That(result.Diagnostics[0].Message, Does.Contain("example.nodes.first"));
            Assert.That(result.Diagnostics[0].Message, Does.Contain("example.nodes.second"));
            Assert.That(result.Diagnostics[0].Message, Does.Contain("42"));
        }

        [TestCase("aibt.core.not-user")]
        [TestCase("aibt.test.not-user")]
        [TestCase("aibt.reference.not-user")]
        public void UserExtension_CannotClaimReservedNamespace(string typeId)
        {
            var result = new Authoring.NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create(typeId))
                .Build();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Select(item => item.Code.Value), Does.Contain("AIBT3004"));
        }

        [Test]
        public void UserExtension_IsUnboundAndAdvertisedAsCapability()
        {
            var result = new Authoring.NodeRegistryBuilder()
                .AddUserExtension(NodeManifestTestFactory.Create("example.nodes.user"))
                .Build();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Registry[0].Source, Is.EqualTo(Authoring.NodeManifestSource.UserExtension));
            Assert.That(result.Registry[0].HasReferenceHandlerBinding, Is.False);
            Assert.That((result.Registry.Capabilities & Authoring.NodeRegistryCapabilityFlags.UserExtensions) != 0, Is.True);
            Assert.That((result.Registry.Capabilities & Authoring.NodeRegistryCapabilityFlags.ReferenceHandlerBindings) == 0, Is.True);
        }

        [Test]
        public void BindingMustMatchManifestVersionAndDomain()
        {
            var manifest = NodeManifestTestFactory.Create("aibt.core.binding", 2);
            var binding = new Authoring.NodeHandlerBindingContract("aibt.reference.binding", 1, Authoring.NodeExecutionDomain.Managed);

            var result = new Authoring.NodeRegistryBuilder().AddBuiltInForTest(manifest, binding).Build();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Select(item => item.Code.Value), Does.Contain("AIBT3005"));
        }
    }
}
