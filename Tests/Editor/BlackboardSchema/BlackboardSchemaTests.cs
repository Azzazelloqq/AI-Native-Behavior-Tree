using System.Globalization;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.BlackboardSchema
{
    public sealed class BlackboardSchemaTests
    {
        [Test]
        public void BuiltInReferencesUseTheNormativeCanonicalNames()
        {
            Assert.That(BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool).CanonicalTypeId, Is.EqualTo("Bool"));
            Assert.That(BlackboardTypeReference.BuiltIn(BlackboardValueType.FixedString512).CanonicalTypeId, Is.EqualTo("FixedString512"));
            Assert.That(BlackboardTypeReference.BuiltIn(BlackboardValueType.AssetId).CanonicalTypeId, Is.EqualTo("AssetId"));
            Assert.Throws<System.ArgumentException>(() => BlackboardTypeReference.BuiltIn(BlackboardValueType.Enum32));

            var enumType = BlackboardTypeReference.Enum32("game.alert-state");
            Assert.That(enumType.CanonicalTypeId, Is.EqualTo("Enum32"));
            Assert.That(enumType.EnumContract, Is.EqualTo("game.alert-state"));
            Assert.That(enumType.EnumContractId, Is.EqualTo(StableHash.Fnv1A64("game.alert-state")));
            Assert.That(enumType.IsValid, Is.True);
            Assert.That(enumType, Is.EqualTo(BlackboardTypeReference.Enum32("game.alert-state")));
            Assert.That(enumType, Is.Not.EqualTo(BlackboardTypeReference.Enum32("game.other-state")));
            Assert.That(
                BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32).EnumContractId,
                Is.Zero);
        }

        [Test]
        public void EnumDefaultMustMatchTheDeclaredContract()
        {
            var matching = new BlackboardKeyDefinition(
                "state", "State", BlackboardTypeReference.Enum32("game.state"),
                defaultValue: BlackboardDefaultValue.Enum32("game.state", 1));
            var mismatched = new BlackboardKeyDefinition(
                "state", "State", BlackboardTypeReference.Enum32("game.state"),
                defaultValue: BlackboardDefaultValue.Enum32("game.other-state", 1));

            Assert.That(BlackboardSchemaValidator.Validate(matching), Is.Empty);
            Assert.That(BlackboardSchemaValidator.Validate(mismatched).Single().Code,
                Is.EqualTo(BlackboardDiagnosticCodes.DefaultTypeMismatch));
        }

        [Test]
        public void MismatchedDefaultProducesADiagnosticWithoutCoercion()
        {
            var key = new BlackboardKeyDefinition(
                "target-count",
                "Target count",
                BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32),
                defaultValue: BlackboardDefaultValue.Float32(3));

            var diagnostics = BlackboardSchemaValidator.Validate(key);

            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics[0].Code, Is.EqualTo(BlackboardDiagnosticCodes.DefaultTypeMismatch));
            Assert.That(key.DefaultValue.ValueType, Is.EqualTo(BlackboardValueType.Float32));
        }

        [Test]
        public void NonFiniteAndOversizedDefaultsAreDiagnosticInputs()
        {
            var nonFinite = new BlackboardKeyDefinition(
                "speed",
                "Speed",
                BlackboardTypeReference.BuiltIn(BlackboardValueType.Float64),
                defaultValue: BlackboardDefaultValue.Float64(double.PositiveInfinity));
            var oversized = new BlackboardKeyDefinition(
                "label",
                "Label",
                BlackboardTypeReference.BuiltIn(BlackboardValueType.FixedString32),
                defaultValue: BlackboardDefaultValue.FixedString32(new string('é', 30)));

            Assert.That(BlackboardSchemaValidator.Validate(nonFinite)[0].Code,
                Is.EqualTo(BlackboardDiagnosticCodes.InvalidDefaultValue));
            Assert.That(BlackboardSchemaValidator.Validate(oversized)[0].Code,
                Is.EqualTo(BlackboardDiagnosticCodes.InvalidDefaultValue));
        }

        [Test]
        public void FixedStringCapacityIsMeasuredInUtf8BytesAndInvalidUnicodeIsRejected()
        {
            var exactCapacity = BlackboardDefaultValue.FixedString32(new string('a', BlackboardFixedStringCapacity.FixedString32));
            var beyondCapacity = BlackboardDefaultValue.FixedString32(new string('a', BlackboardFixedStringCapacity.FixedString32 + 1));
            var beyondCapacityWithUnicode = BlackboardDefaultValue.FixedString32(new string('é', 15));
            var invalidUnicode = BlackboardDefaultValue.FixedString32("\ud800");

            Assert.That(exactCapacity.IsCanonical, Is.True);
            Assert.That(beyondCapacity.IsCanonical, Is.False);
            Assert.That(beyondCapacityWithUnicode.IsCanonical, Is.False);
            Assert.That(invalidUnicode.IsCanonical, Is.False);
        }

        [Test]
        public void InvalidOpaqueIdentityDefaultProducesADiagnostic()
        {
            var key = new BlackboardKeyDefinition(
                "agent",
                "Agent",
                BlackboardTypeReference.BuiltIn(BlackboardValueType.AgentId),
                defaultValue: BlackboardDefaultValue.AgentId(default));

            Assert.That(BlackboardSchemaValidator.Validate(key)[0].Code,
                Is.EqualTo(BlackboardDiagnosticCodes.InvalidDefaultValue));
        }

        [Test]
        public void NodeLocalCannotBeDeclaredAtTreeLevel()
        {
            var key = new BlackboardKeyDefinition(
                "local",
                "Local",
                BlackboardTypeReference.BuiltIn(BlackboardValueType.Bool),
                BlackboardScope.NodeLocal);

            var diagnostics = BlackboardSchemaValidator.Validate(key);

            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics[0].Code, Is.EqualTo(BlackboardDiagnosticCodes.NodeLocalTreeDeclaration));
        }

        [Test]
        public void ValidTreeAgentAndSharedDeclarationsAreRecognized()
        {
            foreach (var scope in new[] { BlackboardScope.Tree, BlackboardScope.Agent, BlackboardScope.Shared })
            {
                var key = new BlackboardKeyDefinition(
                    "state",
                    "State",
                    BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32),
                    scope,
                    BlackboardDefaultValue.Int32(2));

                Assert.That(BlackboardSchemaValidator.Validate(key).Count, Is.Zero, scope.ToString());
            }
        }

        [Test]
        public void SchemaRejectsDuplicateIdsAndNamesWithinTheSameScope()
        {
            var type = BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32);
            var diagnostics = BlackboardSchemaValidator.Validate(new[]
            {
                new BlackboardKeyDefinition("first", "Target", type, BlackboardScope.Tree),
                new BlackboardKeyDefinition("first", "Other", type, BlackboardScope.Agent),
                new BlackboardKeyDefinition("third", "Target", type, BlackboardScope.Tree),
                new BlackboardKeyDefinition("fourth", "Target", type, BlackboardScope.Agent),
            });

            Assert.That(diagnostics.Count(item => item.Code == BlackboardDiagnosticCodes.InvalidKeyId), Is.EqualTo(1));
            Assert.That(diagnostics.Count(item => item.Code == BlackboardDiagnosticCodes.InvalidKeyName), Is.EqualTo(1));
            Assert.That(diagnostics.All(item => item.RelatedLocations.Count == 1), Is.True);
        }

        [Test]
        public void SameNameInDifferentScopesIsAllowed()
        {
            var type = BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32);
            var diagnostics = BlackboardSchemaValidator.Validate(new[]
            {
                new BlackboardKeyDefinition("tree-target", "Target", type, BlackboardScope.Tree),
                new BlackboardKeyDefinition("agent-target", "Target", type, BlackboardScope.Agent),
            });

            Assert.That(diagnostics.Count, Is.Zero);
        }

        [Test]
        public void BlackboardCodesAreRegisteredWithStructuredLocationRequirements()
        {
            Assert.That(BlackboardDiagnosticCatalog.Catalog.Count, Is.EqualTo(8));
            Assert.That(
                BlackboardDiagnosticCatalog.Catalog.TryGet(
                    BlackboardDiagnosticCodes.InvalidDefaultValue,
                    out var descriptor),
                Is.True);
            Assert.That(descriptor.Subsystem, Is.EqualTo(DiagnosticSubsystem.SemanticValidation));
            Assert.That(descriptor.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That((descriptor.RequiredFields & DiagnosticField.JsonPointer) != 0, Is.True);
        }

        [Test]
        public void UnicodeFixedStringDefaultRoundTripsIndependentlyOfCulture()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                var defaultValue = BlackboardDefaultValue.FixedString64("İstanbul 世界");

                Assert.That(defaultValue.TryGetRuntimeValue(out var runtimeValue), Is.True);
                Assert.That(runtimeValue.TryGetFixedString(out var text), Is.True);
                Assert.That(text, Is.EqualTo("İstanbul 世界"));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void RegisteredDefaultSourceRemainsUnvalidatedUntilCanonicalJsonWork()
        {
            const string typeName = "game.target-data";
            var descriptor = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64(typeName),
                2,
                16,
                8,
                StableHash.Fnv1A64(typeName + ".equals"),
                StableHash.Fnv1A64(typeName + ".schema"));
            var type = BlackboardTypeReference.Registered(typeName, descriptor);
            var key = new BlackboardKeyDefinition(
                "target",
                "Target",
                type,
                defaultValue: BlackboardDefaultValue.RegisteredSource(typeName, 2, "{\"entity\":\"4\"}"));

            var diagnostics = BlackboardSchemaValidator.Validate(key);
            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics[0].Code, Is.EqualTo(BlackboardDiagnosticCodes.InvalidDefaultValue));

            var wrongVersion = new BlackboardKeyDefinition(
                "target",
                "Target",
                type,
                defaultValue: BlackboardDefaultValue.RegisteredSource(typeName, 1, "{\"entity\":\"4\"}"));
            Assert.That(BlackboardSchemaValidator.Validate(wrongVersion)[0].Code,
                Is.EqualTo(BlackboardDiagnosticCodes.DefaultTypeMismatch));
        }
    }
}
